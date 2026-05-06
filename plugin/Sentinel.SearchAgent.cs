using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    public class SearchAgentResult
    {
        public string Sql { get; set; } = "";
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public bool IsHeuristic { get; set; }
        public List<Dictionary<string, object>>? Rows { get; set; }
    }

    public class SqlValidationResult
    {
        public bool IsValid { get; set; }
        public string Reason { get; set; } = "";
    }

    public partial class Sentinel
    {
        private static readonly Regex WhitelistSelectRegex = new(@"^\s*SELECT\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DmlBlacklistRegex = new(
            @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|CREATE|EXEC|EXECUTE|MERGE|REPLACE|GRANT|REVOKE|UNION)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MultiStatementRegex = new(@";\s*\S", RegexOptions.Compiled);

        public virtual SqlValidationResult ValidateSqlWhitelist(string? sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return new SqlValidationResult { IsValid = false, Reason = "SQL is empty or whitespace." };

            if (!WhitelistSelectRegex.IsMatch(sql))
                return new SqlValidationResult { IsValid = false, Reason = "SQL does not start with SELECT." };

            if (DmlBlacklistRegex.IsMatch(sql))
                return new SqlValidationResult { IsValid = false, Reason = "SQL contains forbidden DML/DDL keywords." };

            if (MultiStatementRegex.IsMatch(sql))
                return new SqlValidationResult { IsValid = false, Reason = "SQL contains multiple statements." };

            return new SqlValidationResult { IsValid = true, Reason = "" };
        }

        public virtual SearchAgentResult RunSearchAgent(string? nlQuery)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var sw = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(nlQuery))
            {
                return new SearchAgentResult
                {
                    Success = false,
                    Error = "Query is empty. Please provide a natural-language query.",
                    IsHeuristic = false
                };
            }

            if (_dbConnection == null)
            {
                return new SearchAgentResult
                {
                    Success = false,
                    Error = "Database is not available.",
                    IsHeuristic = false
                };
            }

            try
            {
                PiiRedactor.ValidatePrompt(nlQuery);
            }
            catch (PromptInjectionException ex)
            {
                return new SearchAgentResult
                {
                    Success = false,
                    Error = $"Query rejected: {ex.Message}",
                    IsHeuristic = false
                };
            }

            var prompt = BuildSearchPrompt(nlQuery);
            var redactedPrompt = PiiRedactor.Redact(prompt);
            LlmResponse? llmResponse = null;
            SearchAgentResult? result = null;

            try
            {
                var config = PluginConfig?.AI ?? new AIConfig();
                if (_llmClient != null && !string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    var request = new LlmRequest
                    {
                        Provider = config.Provider,
                        Endpoint = config.Endpoint,
                        ApiKey = config.ApiKey,
                        Model = config.Model,
                        Prompt = prompt
                    };

                    llmResponse = _llmClient.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogWarning($"[Sentinel] Search LLM call failed: {ex.Message}");
            }

            if (llmResponse != null)
            {
                if (llmResponse.Success && !llmResponse.IsFallback)
                {
                    var rawSql = ExtractLlmText(llmResponse.Content);
                    result = ProcessGeneratedSql(rawSql, requestId, isHeuristic: false);
                }
                else if (!llmResponse.Success && !string.IsNullOrEmpty(llmResponse.Error))
                {
                    // Propagate security rejections (prompt injection, etc.) rather than falling back
                    if (llmResponse.Error.Contains("rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        result = new SearchAgentResult
                        {
                            Success = false,
                            Error = $"Query rejected: {llmResponse.Error}",
                            IsHeuristic = false
                        };
                    }
                }
            }

            if (result == null)
            {
                _runtimeBridge?.LogInfo("[Sentinel] Search falling back to heuristic rules.");
                var heuristicSql = HeuristicSearchSql(nlQuery);
                result = ProcessGeneratedSql(heuristicSql, requestId, isHeuristic: true);
            }

            sw.Stop();

            LogAiQuery(
                agentName: "Search",
                requestId: requestId,
                promptHash: ComputeDeterministicHash(prompt),
                redactedInput: redactedPrompt,
                rawOutput: llmResponse?.Content ?? "[HEURISTIC]",
                durationMs: (int)sw.ElapsedMilliseconds
            );

            return result;
        }

        private SearchAgentResult ProcessGeneratedSql(string rawSql, string requestId, bool isHeuristic)
        {
            var validation = ValidateSqlWhitelist(rawSql);
            if (!validation.IsValid)
            {
                _runtimeBridge?.LogWarning($"[Sentinel] SQL whitelist rejected: {validation.Reason} | SQL: {rawSql}");
                return new SearchAgentResult
                {
                    Success = false,
                    Error = $"Query rejected for safety reasons: {validation.Reason}",
                    Sql = rawSql,
                    IsHeuristic = isHeuristic
                };
            }

            try
            {
                var rows = ExecuteReadOnlySql(rawSql);
                return new SearchAgentResult
                {
                    Success = true,
                    Sql = rawSql,
                    Rows = rows,
                    IsHeuristic = isHeuristic
                };
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] Search SQL execution failed: {ex.Message}");
                return new SearchAgentResult
                {
                    Success = false,
                    Error = $"SQL execution failed: {ex.Message}",
                    Sql = rawSql,
                    IsHeuristic = isHeuristic
                };
            }
        }

        public virtual string BuildSearchPrompt(string nlQuery)
        {
            var lines = new List<string>
            {
                "You are Sentinel AI Search Agent. Convert the following natural-language query into a read-only SQL SELECT statement for the Sentinel SQLite database.",
                "",
                "Available tables:",
                "- sentinel_actions (id, actor_steam_id, actor_name, target_steam_id, target_name, action_type, reason, duration_minutes, timestamp, success)",
                "- sentinel_bans (id, steam_id, name, banned_by_steam_id, banned_by_name, reason, ai_draft, duration_minutes, active, created_at, expires_at, revoked_at)",
                "- sentinel_groups (id, name, title, permissions_json, parent_group, created_at)",
                "- sentinel_group_members (id, group_id, steam_id, added_at)",
                "- sentinel_ai_log (id, agent_name, request_id, prompt_hash, redacted_input, raw_output, verdict, admin_steam_id, edit_diff, duration_ms, cost_usd, timestamp)",
                "- sentinel_baselines (id, steam_id, metric_name, mean, std_dev, sample_count, last_updated)",
                "",
                "Rules:",
                "- Generate ONLY a SELECT statement.",
                "- Do NOT generate INSERT, UPDATE, DELETE, DROP, ALTER, TRUNCATE, CREATE, or any DML/DDL.",
                "- Use SQLite syntax.",
                "- Return ONLY the raw SQL query. No markdown, no explanation, no backticks.",
                "",
                $"Natural-language query: {nlQuery}"
            };

            return string.Join("\n", lines);
        }

        public virtual string HeuristicSearchSql(string nlQuery)
        {
            var lower = nlQuery.ToLowerInvariant();
            string table;
            string timeColumn;

            if (lower.Contains("ban"))
            {
                table = "sentinel_bans";
                timeColumn = "created_at";
            }
            else if (lower.Contains("group") && lower.Contains("member"))
            {
                table = "sentinel_group_members";
                timeColumn = "added_at";
            }
            else if (lower.Contains("group"))
            {
                table = "sentinel_groups";
                timeColumn = "created_at";
            }
            else if (lower.Contains("ai") || lower.Contains("log"))
            {
                table = "sentinel_ai_log";
                timeColumn = "timestamp";
            }
            else if (lower.Contains("baseline"))
            {
                table = "sentinel_baselines";
                timeColumn = "last_updated";
            }
            else if (lower.Contains("action"))
            {
                table = "sentinel_actions";
                timeColumn = "timestamp";
            }
            else if (lower.Contains("player") || lower.Contains("kick") || lower.Contains("warn") || lower.Contains("mute") || lower.Contains("freeze"))
            {
                table = "sentinel_actions";
                timeColumn = "timestamp";
            }
            else
            {
                table = "sentinel_actions";
                timeColumn = "timestamp";
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var oneWeekAgo = now - (7 * 24 * 60 * 60);

            if (lower.Contains("last week") || lower.Contains("recent") || lower.Contains("today"))
            {
                return $"SELECT * FROM {table} WHERE {timeColumn} > {oneWeekAgo} ORDER BY {timeColumn} DESC LIMIT 50;";
            }

            return $"SELECT * FROM {table} LIMIT 50;";
        }

        public virtual List<Dictionary<string, object>> ExecuteReadOnlySql(string sql)
        {
            var rows = new List<Dictionary<string, object>>();
            if (_dbConnection == null) return rows;

            using var command = _dbConnection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null! : reader.GetValue(i);
                    row[name] = value!;
                }
                rows.Add(row);
            }

            return rows;
        }

        public virtual List<Dictionary<string, object>> GetExecutionPlan(string sql)
        {
            var rows = new List<Dictionary<string, object>>();
            if (_dbConnection == null) return rows;

            var validation = ValidateSqlWhitelist(sql);
            if (!validation.IsValid) return rows;

            using var command = _dbConnection.CreateCommand();
            command.CommandText = $"EXPLAIN QUERY PLAN {sql}";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null! : reader.GetValue(i);
                    row[name] = value!;
                }
                rows.Add(row);
            }

            return rows;
        }
    }
}
