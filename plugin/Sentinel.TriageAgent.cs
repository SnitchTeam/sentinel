using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Oxide.Plugins
{
    public class TriageAnomaly
    {
        [JsonPropertyName("player_id")]
        public string player_id { get; set; } = "";

        [JsonPropertyName("anomaly_type")]
        public string anomaly_type { get; set; } = "";

        [JsonPropertyName("severity")]
        public string severity { get; set; } = "";

        [JsonPropertyName("confidence")]
        public double confidence { get; set; }
    }

    public class ActionRecord
    {
        public string ActorSteamId { get; set; } = "";
        public string? ActorName { get; set; }
        public string? TargetSteamId { get; set; }
        public string? TargetName { get; set; }
        public string ActionType { get; set; } = "";
        public string? Reason { get; set; }
        public long Timestamp { get; set; }
        public bool Success { get; set; }
    }

    public partial class Sentinel
    {
        public virtual List<TriageAnomaly> RunTriageAgent()
        {
            var actions = QueryRecentActions(500);
            var requestId = Guid.NewGuid().ToString("N");
            var prompt = BuildTriagePrompt(actions);
            var redactedPrompt = PiiRedactor.Redact(prompt);

            var sw = Stopwatch.StartNew();
            LlmResponse? llmResponse = null;
            List<TriageAnomaly>? anomalies = null;

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
                _runtimeBridge?.LogWarning($"[Sentinel] Triage LLM call failed: {ex.Message}");
            }

            if (llmResponse != null && llmResponse.Success && !llmResponse.IsFallback)
            {
                anomalies = ParseLlmTriageResponse(llmResponse.Content);
            }

            if (anomalies == null)
            {
                _runtimeBridge?.LogInfo("[Sentinel] Triage falling back to heuristic rules.");
                anomalies = HeuristicTriage(actions);
            }

            sw.Stop();

            LogAiQuery(
                agentName: "Triage",
                requestId: requestId,
                promptHash: ComputeDeterministicHash(prompt),
                redactedInput: redactedPrompt,
                rawOutput: llmResponse?.Content ?? "[HEURISTIC]",
                durationMs: (int)sw.ElapsedMilliseconds
            );

            foreach (var anomaly in anomalies)
            {
                var suggestion = new AiSuggestion
                {
                    PlayerName = anomaly.player_id,
                    SteamId = anomaly.player_id,
                    Behavior = anomaly.anomaly_type,
                    Confidence = (int)Math.Clamp(anomaly.confidence, 0, 100),
                    RecommendedAction = anomaly.severity == "high" ? "ban" : "warn",
                    Reason = $"Triage anomaly: {anomaly.anomaly_type} (severity={anomaly.severity}, confidence={anomaly.confidence:F1}%)",
                    AgentName = "Triage"
                };
                AddAiSuggestion(suggestion);
            }

            return anomalies;
        }

        public virtual List<ActionRecord> QueryRecentActions(int limit)
        {
            var actions = new List<ActionRecord>();
            if (_dbConnection == null) return actions;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT actor_steam_id, actor_name, target_steam_id, target_name, action_type, reason, timestamp, success
                    FROM sentinel_actions
                    ORDER BY timestamp DESC
                    LIMIT @limit;";
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    actions.Add(new ActionRecord
                    {
                        ActorSteamId = reader.GetString(0),
                        ActorName = reader.IsDBNull(1) ? null : reader.GetString(1),
                        TargetSteamId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        TargetName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        ActionType = reader.GetString(4),
                        Reason = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Timestamp = reader.GetInt64(6),
                        Success = reader.GetInt64(7) == 1
                    });
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] Triage query failed: {ex.Message}");
            }

            return actions;
        }

        public virtual string BuildTriagePrompt(List<ActionRecord> actions)
        {
            var lines = new List<string>
            {
                "You are Sentinel AI Triage Agent. Analyze the following player action log and identify statistical outliers and anomalies.",
                "",
                "Return ONLY a JSON array with this exact schema (no markdown, no explanation):",
                "[{\"player_id\":\"string\",\"anomaly_type\":\"string\",\"severity\":\"low|medium|high\",\"confidence\":0.0}]",
                "",
                $"Action log ({actions.Count} most recent records):"
            };

            foreach (var action in actions)
            {
                var target = action.TargetSteamId ?? "none";
                var reason = action.Reason ?? "none";
                lines.Add($"- [{action.Timestamp}] actor={action.ActorSteamId} target={target} type={action.ActionType} reason={reason} success={action.Success}");
            }

            return string.Join("\n", lines);
        }

        public virtual List<TriageAnomaly>? ParseLlmTriageResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;

            try
            {
                var anomalies = JsonSerializer.Deserialize<List<TriageAnomaly>>(content);
                if (anomalies != null) return anomalies;
            }
            catch { }

            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var message = choices[0].GetProperty("message");
                    var messageContent = message.GetProperty("content").GetString();
                    if (!string.IsNullOrEmpty(messageContent))
                    {
                        var anomalies = JsonSerializer.Deserialize<List<TriageAnomaly>>(messageContent);
                        if (anomalies != null) return anomalies;
                    }
                }
            }
            catch { }

            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
                {
                    var text = contentArray[0].GetProperty("text").GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        var anomalies = JsonSerializer.Deserialize<List<TriageAnomaly>>(text);
                        if (anomalies != null) return anomalies;
                    }
                }
            }
            catch { }

            return null;
        }

        public virtual List<TriageAnomaly> HeuristicTriage(List<ActionRecord> actions)
        {
            var anomalies = new List<TriageAnomaly>();
            if (actions.Count == 0) return anomalies;

            var playerCounts = actions
                .Where(a => !string.IsNullOrEmpty(a.TargetSteamId))
                .GroupBy(a => a.TargetSteamId!)
                .Select(g => new { PlayerId = g.Key, Count = g.Count() })
                .ToList();

            if (playerCounts.Count == 0) return anomalies;

            var counts = playerCounts.Select(p => (double)p.Count).ToList();
            var mean = counts.Average();
            var variance = counts.Average(c => (c - mean) * (c - mean));
            var stdDev = Math.Sqrt(variance);

            foreach (var pc in playerCounts)
            {
                var z = stdDev == 0 ? (pc.Count > mean ? 999.0 : 0.0) : (pc.Count - mean) / stdDev;
                string severity;
                if (z > 3) severity = "high";
                else if (z > 2) severity = "medium";
                else if (z > 1) severity = "low";
                else continue;

                anomalies.Add(new TriageAnomaly
                {
                    player_id = pc.PlayerId,
                    anomaly_type = "action_frequency_spike",
                    severity = severity,
                    confidence = Math.Min(z / 4.0 * 100.0, 100.0)
                });
            }

            return anomalies;
        }

        public virtual void LogAiQuery(string agentName, string requestId, string promptHash, string redactedInput, string rawOutput, int durationMs, double? costUsd = null)
        {
            if (_dbConnection == null) return;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO sentinel_ai_log (
                        agent_name, request_id, prompt_hash, redacted_input, raw_output, duration_ms, cost_usd, timestamp
                    ) VALUES (
                        @agentName, @requestId, @promptHash, @redactedInput, @rawOutput, @durationMs, @costUsd, @timestamp
                    );";

                command.Parameters.AddWithValue("@agentName", agentName);
                command.Parameters.AddWithValue("@requestId", requestId);
                command.Parameters.AddWithValue("@promptHash", promptHash);
                command.Parameters.AddWithValue("@redactedInput", redactedInput);
                command.Parameters.AddWithValue("@rawOutput", rawOutput);
                command.Parameters.AddWithValue("@durationMs", durationMs);
                command.Parameters.AddWithValue("@costUsd", costUsd.HasValue ? (object)costUsd.Value : DBNull.Value);
                command.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] AI query log failed: {ex.Message}");
            }
        }

        private static string ComputeDeterministicHash(string input)
        {
            if (string.IsNullOrEmpty(input)) return "00000000";
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
#if NET5_0_OR_GREATER
            var hash = System.Security.Cryptography.MD5.HashData(bytes);
#else
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(bytes);
#endif
            return Convert.ToHexString(hash)[..8];
        }
    }
}
