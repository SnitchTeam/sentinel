using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Oxide.Plugins
{
    public class AntiCheatVerdict
    {
        [JsonPropertyName("cheat_likelihood")]
        public int CheatLikelihood { get; set; }

        [JsonPropertyName("primary_indicators")]
        public List<string> PrimaryIndicators { get; set; } = new();
    }

    public class AntiCheatEvent
    {
        public long Id { get; set; }
        public string SteamId { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string MetricName { get; set; } = "";
        public double ObservedValue { get; set; }
        public double BaselineMean { get; set; }
        public double BaselineStdDev { get; set; }
        public double ZScore { get; set; }
        public int CheatLikelihood { get; set; }
        public string PrimaryIndicators { get; set; } = "";
        public bool IsHeuristic { get; set; }
        public long Timestamp { get; set; }
    }

    public class PlayerBaseline
    {
        public string SteamId { get; set; } = "";
        public string MetricName { get; set; } = "";
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public int SampleCount { get; set; }
        public long LastUpdated { get; set; }
    }

    public partial class Sentinel
    {
        public virtual PlayerBaseline? QueryBaseline(string steamId, string metricName)
        {
            if (_dbConnection == null) return null;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT steam_id, metric_name, mean, std_dev, sample_count, last_updated
                    FROM sentinel_baselines
                    WHERE steam_id = @steamId AND metric_name = @metricName
                    LIMIT 1;";
                command.Parameters.AddWithValue("@steamId", steamId);
                command.Parameters.AddWithValue("@metricName", metricName);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    return new PlayerBaseline
                    {
                        SteamId = reader.GetString(0),
                        MetricName = reader.GetString(1),
                        Mean = reader.GetDouble(2),
                        StdDev = reader.GetDouble(3),
                        SampleCount = reader.GetInt32(4),
                        LastUpdated = reader.GetInt64(5)
                    };
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] Baseline query failed: {ex.Message}");
            }

            return null;
        }

        public virtual void ComputeAndStoreBaseline(string steamId, string metricName, List<double> values)
        {
            if (_dbConnection == null || values.Count == 0) return;

            var mean = values.Average();
            var variance = values.Count > 1
                ? values.Average(v => (v - mean) * (v - mean))
                : 0.0;
            var stdDev = Math.Sqrt(variance);

            try
            {
                using var checkCmd = _dbConnection.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM sentinel_baselines WHERE steam_id = @steamId AND metric_name = @metricName;";
                checkCmd.Parameters.AddWithValue("@steamId", steamId);
                checkCmd.Parameters.AddWithValue("@metricName", metricName);
                var exists = Convert.ToInt64(checkCmd.ExecuteScalar()) > 0;

                using var command = _dbConnection.CreateCommand();
                if (exists)
                {
                    command.CommandText = @"
                        UPDATE sentinel_baselines
                        SET mean = @mean, std_dev = @stdDev, sample_count = @sampleCount, last_updated = @lastUpdated
                        WHERE steam_id = @steamId AND metric_name = @metricName;";
                }
                else
                {
                    command.CommandText = @"
                        INSERT INTO sentinel_baselines (steam_id, metric_name, mean, std_dev, sample_count, last_updated)
                        VALUES (@steamId, @metricName, @mean, @stdDev, @sampleCount, @lastUpdated);";
                }

                command.Parameters.AddWithValue("@steamId", steamId);
                command.Parameters.AddWithValue("@metricName", metricName);
                command.Parameters.AddWithValue("@mean", mean);
                command.Parameters.AddWithValue("@stdDev", stdDev);
                command.Parameters.AddWithValue("@sampleCount", values.Count);
                command.Parameters.AddWithValue("@lastUpdated", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                command.ExecuteNonQuery();

                _runtimeBridge?.LogInfo($"[Sentinel] AntiCheat baseline updated for {steamId} | metric={metricName} | mu={mean:F4} | sigma={stdDev:F4} | n={values.Count}");
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] Baseline store failed: {ex.Message}");
            }
        }

        public virtual double ComputeZScore(double observed, double mean, double stdDev)
        {
            if (stdDev == 0) return observed > mean ? 999.0 : 0.0;
            return (observed - mean) / stdDev;
        }

        public virtual AntiCheatEvent? EvaluatePlayerMetrics(string steamId, string playerName, Dictionary<string, double> metrics)
        {
            if (metrics == null || metrics.Count == 0) return null;

            AntiCheatEvent? flaggedEvent = null;
            double maxZScore = 0.0;

            foreach (var kvp in metrics)
            {
                var baseline = QueryBaseline(steamId, kvp.Key);
                if (baseline == null) continue;

                var zScore = ComputeZScore(kvp.Value, baseline.Mean, baseline.StdDev);

                _runtimeBridge?.LogInfo($"[Sentinel] AntiCheat check {steamId} | metric={kvp.Key} | observed={kvp.Value:F4} | mu={baseline.Mean:F4} | sigma={baseline.StdDev:F4} | z={zScore:F4}");

                if (zScore > 3.0 && zScore > maxZScore)
                {
                    maxZScore = zScore;
                    flaggedEvent = new AntiCheatEvent
                    {
                        SteamId = steamId,
                        PlayerName = playerName,
                        MetricName = kvp.Key,
                        ObservedValue = kvp.Value,
                        BaselineMean = baseline.Mean,
                        BaselineStdDev = baseline.StdDev,
                        ZScore = zScore,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                }
            }

            return flaggedEvent;
        }

        public virtual AntiCheatVerdict RunAntiCheatAgent(string steamId, string playerName, Dictionary<string, double> metrics)
        {
            var flaggedEvent = EvaluatePlayerMetrics(steamId, playerName, metrics);
            if (flaggedEvent == null)
            {
                return new AntiCheatVerdict { CheatLikelihood = 0, PrimaryIndicators = new List<string>() };
            }

            var requestId = Guid.NewGuid().ToString("N");
            var sw = Stopwatch.StartNew();
            var prompt = BuildAntiCheatPrompt(flaggedEvent);
            var redactedPrompt = PiiRedactor.Redact(prompt);
            LlmResponse? llmResponse = null;
            AntiCheatVerdict? verdict = null;

            try
            {
                llmResponse = SendAiRequest(prompt);
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogWarning($"[Sentinel] AntiCheat LLM call failed: {ex.Message}");
            }

            if (llmResponse != null && llmResponse.Success && !llmResponse.IsFallback)
            {
                verdict = ParseAntiCheatVerdict(llmResponse.Content);
            }

            if (verdict == null)
            {
                _runtimeBridge?.LogWarning("[Sentinel] AntiCheat falling back to hard threshold rule.");
                verdict = HeuristicAntiCheatFlag(flaggedEvent);
                flaggedEvent.IsHeuristic = true;
            }
            else
            {
                flaggedEvent.IsHeuristic = false;
            }

            sw.Stop();

            flaggedEvent.CheatLikelihood = verdict.CheatLikelihood;
            flaggedEvent.PrimaryIndicators = string.Join(",", verdict.PrimaryIndicators);

            InsertAntiCheatEvent(flaggedEvent);

            LogAiQuery(
                agentName: "AntiCheat",
                requestId: requestId,
                promptHash: ComputeDeterministicHash(prompt),
                redactedInput: redactedPrompt,
                rawOutput: llmResponse?.Content ?? "[HEURISTIC]",
                durationMs: (int)sw.ElapsedMilliseconds
            );

            if (verdict.CheatLikelihood >= 50)
            {
                var suggestion = new AiSuggestion
                {
                    PlayerName = playerName,
                    SteamId = steamId,
                    Behavior = $"AntiCheat: {flaggedEvent.MetricName} z={flaggedEvent.ZScore:F2}",
                    Confidence = Math.Clamp(verdict.CheatLikelihood, 0, 100),
                    RecommendedAction = verdict.CheatLikelihood >= 80 ? "ban" : "warn",
                    Reason = $"Anti-cheat flag: {flaggedEvent.MetricName} exceeded baseline by {flaggedEvent.ZScore:F2} sigma. Indicators: {string.Join(", ", verdict.PrimaryIndicators)}",
                    AgentName = "AntiCheat"
                };
                AddAiSuggestion(suggestion);
            }

            return verdict;
        }

        public virtual string BuildAntiCheatPrompt(AntiCheatEvent flaggedEvent)
        {
            var lines = new List<string>
            {
                "You are Sentinel AI Anti-Cheat Agent. Analyze the following player metric anomaly and render a verdict.",
                "",
                "Return ONLY a JSON object with this exact schema (no markdown, no explanation):",
                "{\"cheat_likelihood\":0,\"primary_indicators\":[\"string\"]}",
                "",
                "cheat_likelihood must be an integer between 0 and 100.",
                "primary_indicators must be a list of specific cheat indicators (e.g., \"aim_assist\", \"speed_hack\", \"wallhack\").",
                "",
                $"Player: {flaggedEvent.PlayerName} ({flaggedEvent.SteamId})",
                $"Flagged metric: {flaggedEvent.MetricName}",
                $"Observed value: {flaggedEvent.ObservedValue.ToString("F4", CultureInfo.InvariantCulture)}",
                $"Baseline mean (mu): {flaggedEvent.BaselineMean.ToString("F4", CultureInfo.InvariantCulture)}",
                $"Baseline std dev (sigma): {flaggedEvent.BaselineStdDev.ToString("F4", CultureInfo.InvariantCulture)}",
                $"Z-score (sigma deviation): {flaggedEvent.ZScore.ToString("F4", CultureInfo.InvariantCulture)}",
                "",
                "Based on this statistical anomaly, what is the likelihood this player is cheating and what are the primary indicators?"
            };

            return string.Join("\n", lines);
        }

        public virtual AntiCheatVerdict? ParseAntiCheatVerdict(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;

            try
            {
                var text = ExtractLlmText(content);
                if (string.IsNullOrWhiteSpace(text)) return null;

                var verdict = JsonSerializer.Deserialize<AntiCheatVerdict>(text);
                if (verdict == null) return null;

                verdict.CheatLikelihood = Math.Clamp(verdict.CheatLikelihood, 0, 100);
                verdict.PrimaryIndicators = verdict.PrimaryIndicators?
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .ToList() ?? new List<string>();

                return verdict;
            }
            catch
            {
                return null;
            }
        }

        public virtual AntiCheatVerdict HeuristicAntiCheatFlag(AntiCheatEvent flaggedEvent)
        {
            int likelihood;
            if (flaggedEvent.ZScore > 4.0)
            {
                likelihood = 95;
            }
            else
            {
                likelihood = (int)Math.Clamp((flaggedEvent.ZScore - 3.0) * 50.0 + 50.0, 50.0, 94.0);
            }

            var indicators = new List<string> { "statistical_anomaly", $"z_score_{flaggedEvent.ZScore:F2}" };

            if (flaggedEvent.ZScore > 4.0)
            {
                indicators.Add("extreme_outlier");
                _runtimeBridge?.LogWarning($"[Sentinel] AntiCheat auto-flagged {flaggedEvent.SteamId} for {flaggedEvent.MetricName} (z={flaggedEvent.ZScore:F2} > 4 sigma)");
            }

            return new AntiCheatVerdict
            {
                CheatLikelihood = likelihood,
                PrimaryIndicators = indicators
            };
        }

        public virtual void InsertAntiCheatEvent(AntiCheatEvent evt)
        {
            if (_dbConnection == null) return;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO sentinel_anticheat_events (
                        steam_id, player_name, metric_name, observed_value, baseline_mean,
                        baseline_std_dev, z_score, cheat_likelihood, primary_indicators,
                        is_heuristic, timestamp
                    ) VALUES (
                        @steamId, @playerName, @metricName, @observedValue, @baselineMean,
                        @baselineStdDev, @zScore, @cheatLikelihood, @primaryIndicators,
                        @isHeuristic, @timestamp
                    );";

                command.Parameters.AddWithValue("@steamId", evt.SteamId);
                command.Parameters.AddWithValue("@playerName", evt.PlayerName);
                command.Parameters.AddWithValue("@metricName", evt.MetricName);
                command.Parameters.AddWithValue("@observedValue", evt.ObservedValue);
                command.Parameters.AddWithValue("@baselineMean", evt.BaselineMean);
                command.Parameters.AddWithValue("@baselineStdDev", evt.BaselineStdDev);
                command.Parameters.AddWithValue("@zScore", evt.ZScore);
                command.Parameters.AddWithValue("@cheatLikelihood", evt.CheatLikelihood);
                command.Parameters.AddWithValue("@primaryIndicators", evt.PrimaryIndicators);
                command.Parameters.AddWithValue("@isHeuristic", evt.IsHeuristic ? 1 : 0);
                command.Parameters.AddWithValue("@timestamp", evt.Timestamp);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] AntiCheat event insert failed: {ex.Message}");
            }
        }

        public virtual List<AntiCheatEvent> QueryAntiCheatEvents(string steamId, int limit = 100)
        {
            var events = new List<AntiCheatEvent>();
            if (_dbConnection == null) return events;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT id, steam_id, player_name, metric_name, observed_value, baseline_mean,
                           baseline_std_dev, z_score, cheat_likelihood, primary_indicators,
                           is_heuristic, timestamp
                    FROM sentinel_anticheat_events
                    WHERE steam_id = @steamId
                    ORDER BY timestamp DESC
                    LIMIT @limit;";
                command.Parameters.AddWithValue("@steamId", steamId);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    events.Add(new AntiCheatEvent
                    {
                        Id = reader.GetInt64(0),
                        SteamId = reader.GetString(1),
                        PlayerName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        MetricName = reader.GetString(3),
                        ObservedValue = reader.GetDouble(4),
                        BaselineMean = reader.GetDouble(5),
                        BaselineStdDev = reader.GetDouble(6),
                        ZScore = reader.GetDouble(7),
                        CheatLikelihood = reader.GetInt32(8),
                        PrimaryIndicators = reader.IsDBNull(9) ? "" : reader.GetString(9),
                        IsHeuristic = reader.GetInt64(10) == 1,
                        Timestamp = reader.GetInt64(11)
                    });
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] AntiCheat event query failed: {ex.Message}");
            }

            return events;
        }
    }
}
