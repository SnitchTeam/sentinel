using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelAntiCheatAgentTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockRuntimeBridge _logger;

        public SentinelAntiCheatAgentTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_anticheat_test_{Guid.NewGuid()}.db");
            _plugin = new TestableSentinel();
            _logger = new MockRuntimeBridge();
            _plugin.InitializeRuntimeBridgeCustom(_logger);
            _plugin.InitializeDatabase(_dbPath);
        }

        public void Dispose()
        {
            _plugin.CloseDatabase();
            CleanupDbFiles(_dbPath);
        }

        private static void CleanupDbFiles(string dbPath)
        {
            try { File.Delete(dbPath); } catch { }
            try { File.Delete(dbPath + "-shm"); } catch { }
            try { File.Delete(dbPath + "-wal"); } catch { }
        }

        private List<AiLogRow> GetAiLogRows()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT agent_name, request_id, prompt_hash, redacted_input, raw_output, duration_ms
                FROM sentinel_ai_log ORDER BY id;";
            var rows = new List<AiLogRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new AiLogRow
                {
                    AgentName = reader.GetString(0),
                    RequestId = reader.GetString(1),
                    PromptHash = reader.GetString(2),
                    RedactedInput = reader.GetString(3),
                    RawOutput = reader.GetString(4),
                    DurationMs = reader.GetInt32(5)
                });
            }
            return rows;
        }

        private List<AntiCheatEventRow> GetAntiCheatEvents()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT steam_id, player_name, metric_name, observed_value, baseline_mean,
                       baseline_std_dev, z_score, cheat_likelihood, primary_indicators,
                       is_heuristic, timestamp
                FROM sentinel_anticheat_events ORDER BY id;";
            var rows = new List<AntiCheatEventRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new AntiCheatEventRow
                {
                    SteamId = reader.GetString(0),
                    PlayerName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    MetricName = reader.GetString(2),
                    ObservedValue = reader.GetDouble(3),
                    BaselineMean = reader.GetDouble(4),
                    BaselineStdDev = reader.GetDouble(5),
                    ZScore = reader.GetDouble(6),
                    CheatLikelihood = reader.GetInt32(7),
                    PrimaryIndicators = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    IsHeuristic = reader.GetInt64(9) == 1,
                    Timestamp = reader.GetInt64(10)
                });
            }
            return rows;
        }

        private PlayerBaseline? GetBaseline(string steamId, string metricName)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT steam_id, metric_name, mean, std_dev, sample_count, last_updated
                FROM sentinel_baselines WHERE steam_id = @steamId AND metric_name = @metricName;";
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
            return null;
        }

        private class AiLogRow
        {
            public string AgentName { get; set; } = "";
            public string RequestId { get; set; } = "";
            public string PromptHash { get; set; } = "";
            public string RedactedInput { get; set; } = "";
            public string RawOutput { get; set; } = "";
            public int DurationMs { get; set; }
        }

        private class AntiCheatEventRow
        {
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

        private class MockLlmClient : LlmClient
        {
            private readonly LlmResponse _response;
            public List<LlmRequest> Requests { get; } = new();

            public MockLlmClient(LlmResponse response) : base(new DefaultHttpRequester())
            {
                _response = response;
            }

            public override Task<LlmResponse> SendAsync(LlmRequest request)
            {
                Requests.Add(request);
                return Task.FromResult(_response);
            }
        }

        private class MockRuntimeBridge : IRuntimeBridge
        {
            public RuntimeType Runtime => RuntimeType.Oxide;
            public List<string> Logs { get; } = new();
            public void LogInfo(string message) => Logs.Add($"INFO: {message}");
            public void LogWarning(string message) => Logs.Add($"WARN: {message}");
            public void LogError(string message) => Logs.Add($"ERROR: {message}");
        }

        private class TestableSentinel : SentinelPlugin
        {
            public MockLlmClient? MockLlmClient { get; set; }

            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }
            public override void LoadDefaultConfig() { }

            public void InitializeRuntimeBridgeCustom(MockRuntimeBridge bridge)
            {
                var field = typeof(SentinelPlugin).GetField("_runtimeBridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(this, bridge);
            }

            public void SetPluginConfig(SentinelConfig config)
            {
                PluginConfig = config;
            }

            public override LlmClient CreateLlmClient(AIConfig config)
            {
                if (MockLlmClient != null) return MockLlmClient;
                return base.CreateLlmClient(config);
            }
        }

        // ---------------------------------------------------------
        // Baseline computation tests
        // ---------------------------------------------------------

        [Fact]
        public void Baseline_ComputeAndStore_CalculatesMuAndSigma()
        {
            var values = new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 };
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", values);

            var baseline = GetBaseline("76561190000000001", "headshot_ratio");
            Assert.NotNull(baseline);
            Assert.Equal(14.0, baseline.Mean, precision: 4);
            Assert.True(baseline.StdDev > 0);
            Assert.Equal(5, baseline.SampleCount);
        }

        [Fact]
        public void Baseline_ComputeAndStore_LogsMuAndSigma()
        {
            var values = new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 };
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", values);

            Assert.Contains(_logger.Logs, l => l.Contains("mu=") && l.Contains("sigma="));
            Assert.Contains(_logger.Logs, l => l.Contains("76561190000000001") && l.Contains("headshot_ratio"));
        }

        [Fact]
        public void Baseline_Update_ExistingBaseline_Overwrites()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0 });
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 20.0, 22.0, 24.0 });

            var baseline = GetBaseline("76561190000000001", "headshot_ratio");
            Assert.NotNull(baseline);
            Assert.Equal(22.0, baseline.Mean, precision: 4);
            Assert.Equal(3, baseline.SampleCount);
        }

        [Fact]
        public void Baseline_SingleValue_StdDevIsZero()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0 });

            var baseline = GetBaseline("76561190000000001", "headshot_ratio");
            Assert.NotNull(baseline);
            Assert.Equal(10.0, baseline.Mean, precision: 4);
            Assert.Equal(0.0, baseline.StdDev, precision: 4);
        }

        [Fact]
        public void Baseline_EmptyValues_DoesNothing()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double>());
            var baseline = GetBaseline("76561190000000001", "headshot_ratio");
            Assert.Null(baseline);
        }

        // ---------------------------------------------------------
        // Z-score computation tests
        // ---------------------------------------------------------

        [Fact]
        public void ZScore_NormalValues_ComputesCorrectly()
        {
            var z = _plugin.ComputeZScore(16.0, 10.0, 2.0);
            Assert.Equal(3.0, z, precision: 4);
        }

        [Fact]
        public void ZScore_ZeroStdDev_ObservedGreater_ReturnsLarge()
        {
            var z = _plugin.ComputeZScore(15.0, 10.0, 0.0);
            Assert.Equal(999.0, z);
        }

        [Fact]
        public void ZScore_ZeroStdDev_ObservedEqual_ReturnsZero()
        {
            var z = _plugin.ComputeZScore(10.0, 10.0, 0.0);
            Assert.Equal(0.0, z);
        }

        // ---------------------------------------------------------
        // Metric evaluation / threshold tests
        // ---------------------------------------------------------

        [Fact]
        public void EvaluateMetrics_NoBaseline_ReturnsNull()
        {
            var metrics = new Dictionary<string, double> { { "headshot_ratio", 15.0 } };
            var result = _plugin.EvaluatePlayerMetrics("76561190000000001", "Player", metrics);
            Assert.Null(result);
        }

        [Fact]
        public void EvaluateMetrics_ZScoreBelow3_ReturnsNull()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            var metrics = new Dictionary<string, double> { { "headshot_ratio", 15.0 } };
            var result = _plugin.EvaluatePlayerMetrics("76561190000000001", "Player", metrics);
            Assert.Null(result);
        }

        [Fact]
        public void EvaluateMetrics_ZScoreAbove3_ReturnsFlaggedEvent()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            var result = _plugin.EvaluatePlayerMetrics("76561190000000001", "Player", metrics);
            Assert.NotNull(result);
            Assert.Equal("headshot_ratio", result.MetricName);
            Assert.True(result.ZScore > 3.0);
        }

        [Fact]
        public void EvaluateMetrics_LogsCheckLine()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            _plugin.EvaluatePlayerMetrics("76561190000000001", "Player", metrics);

            Assert.Contains(_logger.Logs, l => l.Contains("AntiCheat check") && l.Contains("headshot_ratio") && l.Contains("z="));
        }

        [Fact]
        public void EvaluateMetrics_MultipleMetrics_PicksHighestZAbove3()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            _plugin.ComputeAndStoreBaseline("76561190000000001", "fire_rpm", new List<double> { 100.0, 102.0, 104.0, 106.0, 108.0 });

            var metrics = new Dictionary<string, double>
            {
                { "headshot_ratio", 25.0 },
                { "fire_rpm", 500.0 }
            };
            var result = _plugin.EvaluatePlayerMetrics("76561190000000001", "Player", metrics);
            Assert.NotNull(result);
            Assert.Equal("fire_rpm", result.MetricName);
            Assert.True(result.ZScore > _plugin.ComputeZScore(25.0, 14.0, Math.Sqrt(8.0)));
        }

        // ---------------------------------------------------------
        // LLM prompt tests
        // ---------------------------------------------------------

        [Fact]
        public void AntiCheatPrompt_ContainsFlaggedMetricAndZScore()
        {
            var evt = new AntiCheatEvent
            {
                SteamId = "76561190000000001",
                PlayerName = "TestPlayer",
                MetricName = "headshot_ratio",
                ObservedValue = 30.0,
                BaselineMean = 14.0,
                BaselineStdDev = 2.828,
                ZScore = 5.66
            };
            var prompt = _plugin.BuildAntiCheatPrompt(evt);

            Assert.Contains("headshot_ratio", prompt);
            Assert.Contains("5.66", prompt);
            Assert.Contains("Z-score", prompt);
            Assert.Contains("cheat_likelihood", prompt);
            Assert.Contains("primary_indicators", prompt);
        }

        [Fact]
        public void AntiCheatPrompt_ContainsPlayerAndBaselineStats()
        {
            var evt = new AntiCheatEvent
            {
                SteamId = "76561190000000001",
                PlayerName = "TestPlayer",
                MetricName = "headshot_ratio",
                ObservedValue = 30.0,
                BaselineMean = 14.0,
                BaselineStdDev = 2.828,
                ZScore = 5.66
            };
            var prompt = _plugin.BuildAntiCheatPrompt(evt);

            Assert.Contains("TestPlayer", prompt);
            Assert.Contains("76561190000000001", prompt);
            Assert.Contains("Baseline mean", prompt);
            Assert.Contains("Baseline std dev", prompt);
        }

        // ---------------------------------------------------------
        // LLM verdict parsing tests
        // ---------------------------------------------------------

        [Fact]
        public void ParseVerdict_ValidJson_ReturnsVerdict()
        {
            var json = "{\"cheat_likelihood\":85,\"primary_indicators\":[\"aim_assist\",\"wallhack\"]}";
            var verdict = _plugin.ParseAntiCheatVerdict(json);

            Assert.NotNull(verdict);
            Assert.Equal(85, verdict.CheatLikelihood);
            Assert.Equal(2, verdict.PrimaryIndicators.Count);
            Assert.Contains("aim_assist", verdict.PrimaryIndicators);
        }

        [Fact]
        public void ParseVerdict_OpenAiFormat_ExtractsContent()
        {
            var wrapper = new
            {
                choices = new[]
                {
                    new { message = new { content = "{\"cheat_likelihood\":72,\"primary_indicators\":[\"speed_hack\"]}" } }
                }
            };
            var verdict = _plugin.ParseAntiCheatVerdict(JsonSerializer.Serialize(wrapper));

            Assert.NotNull(verdict);
            Assert.Equal(72, verdict.CheatLikelihood);
            Assert.Contains("speed_hack", verdict.PrimaryIndicators);
        }

        [Fact]
        public void ParseVerdict_AnthropicFormat_ExtractsText()
        {
            var wrapper = new
            {
                content = new[]
                {
                    new { text = "{\"cheat_likelihood\":60,\"primary_indicators\":[\"macro\"]}" }
                }
            };
            var verdict = _plugin.ParseAntiCheatVerdict(JsonSerializer.Serialize(wrapper));

            Assert.NotNull(verdict);
            Assert.Equal(60, verdict.CheatLikelihood);
            Assert.Contains("macro", verdict.PrimaryIndicators);
        }

        [Fact]
        public void ParseVerdict_ClampsLikelihoodAbove100()
        {
            var json = "{\"cheat_likelihood\":150,\"primary_indicators\":[\"aimbot\"]}";
            var verdict = _plugin.ParseAntiCheatVerdict(json);
            Assert.NotNull(verdict);
            Assert.Equal(100, verdict.CheatLikelihood);
        }

        [Fact]
        public void ParseVerdict_ClampsLikelihoodBelow0()
        {
            var json = "{\"cheat_likelihood\":-20,\"primary_indicators\":[\"aimbot\"]}";
            var verdict = _plugin.ParseAntiCheatVerdict(json);
            Assert.NotNull(verdict);
            Assert.Equal(0, verdict.CheatLikelihood);
        }

        [Fact]
        public void ParseVerdict_MalformedJson_ReturnsNull()
        {
            var verdict = _plugin.ParseAntiCheatVerdict("not json");
            Assert.Null(verdict);
        }

        [Fact]
        public void ParseVerdict_Null_ReturnsNull()
        {
            var verdict = _plugin.ParseAntiCheatVerdict(null);
            Assert.Null(verdict);
        }

        // ---------------------------------------------------------
        // Heuristic fallback tests
        // ---------------------------------------------------------

        [Fact]
        public void Heuristic_ZScoreAbove4_Returns95Likelihood()
        {
            var evt = new AntiCheatEvent { ZScore = 4.5, SteamId = "76561190000000001", MetricName = "headshot_ratio" };
            var verdict = _plugin.HeuristicAntiCheatFlag(evt);

            Assert.Equal(95, verdict.CheatLikelihood);
            Assert.Contains("extreme_outlier", verdict.PrimaryIndicators);
            Assert.Contains("statistical_anomaly", verdict.PrimaryIndicators);
        }

        [Fact]
        public void Heuristic_ZScoreAbove4_LogsAutoFlag()
        {
            var evt = new AntiCheatEvent { ZScore = 4.5, SteamId = "76561190000000001", MetricName = "headshot_ratio" };
            _plugin.HeuristicAntiCheatFlag(evt);

            Assert.Contains(_logger.Logs, l => l.Contains("auto-flagged") && l.Contains("76561190000000001") && l.Contains("headshot_ratio"));
        }

        [Fact]
        public void Heuristic_ZScoreExactly3_Returns50Likelihood()
        {
            var evt = new AntiCheatEvent { ZScore = 3.0, SteamId = "76561190000000001", MetricName = "headshot_ratio" };
            var verdict = _plugin.HeuristicAntiCheatFlag(evt);

            Assert.Equal(50, verdict.CheatLikelihood);
            Assert.DoesNotContain("extreme_outlier", verdict.PrimaryIndicators);
        }

        [Fact]
        public void Heuristic_ZScoreBetween3And4_ReturnsScaledLikelihood()
        {
            var evt = new AntiCheatEvent { ZScore = 3.5, SteamId = "76561190000000001", MetricName = "headshot_ratio" };
            var verdict = _plugin.HeuristicAntiCheatFlag(evt);

            Assert.True(verdict.CheatLikelihood > 50 && verdict.CheatLikelihood < 95);
        }

        // ---------------------------------------------------------
        // Full agent pipeline tests
        // ---------------------------------------------------------

        [Fact]
        public void RunAgent_NoBaseline_ReturnsZeroLikelihood()
        {
            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            var verdict = _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            Assert.Equal(0, verdict.CheatLikelihood);
            Assert.Empty(verdict.PrimaryIndicators);
        }

        [Fact]
        public void RunAgent_ZScoreBelow3_ReturnsZeroLikelihood()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            var metrics = new Dictionary<string, double> { { "headshot_ratio", 15.0 } };
            var verdict = _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            Assert.Equal(0, verdict.CheatLikelihood);
            Assert.Empty(verdict.PrimaryIndicators);
        }

        [Fact]
        public void RunAgent_LlmSuccess_ReturnsParsedVerdict()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "{\"cheat_likelihood\":80,\"primary_indicators\":[\"aim_assist\"]}"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            var verdict = _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            Assert.Equal(80, verdict.CheatLikelihood);
            Assert.Contains("aim_assist", verdict.PrimaryIndicators);

            var events = GetAntiCheatEvents();
            Assert.Single(events);
            Assert.Equal(80, events[0].CheatLikelihood);
            Assert.False(events[0].IsHeuristic);
        }

        [Fact]
        public void RunAgent_LlmUnavailable_UsesHeuristicFallback()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            var fallbackResponse = LlmClient.FallbackResponse("test prompt");
            _plugin.MockLlmClient = new MockLlmClient(fallbackResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            var verdict = _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            Assert.True(verdict.CheatLikelihood >= 50);
            Assert.Contains(_logger.Logs, l => l.Contains("AntiCheat falling back to hard threshold rule"));

            var events = GetAntiCheatEvents();
            Assert.Single(events);
            Assert.True(events[0].IsHeuristic);
        }

        [Fact]
        public void RunAgent_NoApiKey_UsesHeuristicFallback()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "" } });
            _plugin.InitializeLlmClient();

            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            var verdict = _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            Assert.True(verdict.CheatLikelihood >= 50);
            Assert.Contains(_logger.Logs, l => l.Contains("AntiCheat falling back to hard threshold rule"));
        }

        [Fact]
        public void RunAgent_LlmPrompt_ContainsZScoreAndMetric()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "{\"cheat_likelihood\":80,\"primary_indicators\":[\"aim_assist\"]}"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            Assert.Single(_plugin.MockLlmClient.Requests);
            var prompt = _plugin.MockLlmClient.Requests[0].Prompt;
            Assert.Contains("headshot_ratio", prompt);
            Assert.Contains("Z-score", prompt);
        }

        [Fact]
        public void RunAgent_LlmSuccess_CreatesAiSuggestion()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "{\"cheat_likelihood\":80,\"primary_indicators\":[\"aim_assist\"]}"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            Assert.True(_plugin.SuggestionCount > 0);
            var suggestion = _plugin.GetNextSuggestion();
            Assert.NotNull(suggestion);
            Assert.Equal("AntiCheat", suggestion!.AgentName);
            Assert.Equal("ban", suggestion.RecommendedAction);
        }

        [Fact]
        public void RunAgent_LlmLowLikelihood_NoSuggestion()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "{\"cheat_likelihood\":30,\"primary_indicators\":[\"lag\"]}"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            // Cheat likelihood < 50, so no suggestion should be created
            // But the event should still be logged
            var events = GetAntiCheatEvents();
            Assert.Single(events);
            Assert.Equal(30, events[0].CheatLikelihood);
        }

        [Fact]
        public void RunAgent_LogsAiQuery()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "{\"cheat_likelihood\":80,\"primary_indicators\":[\"aim_assist\"]}"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            var rows = GetAiLogRows();
            Assert.Single(rows);
            Assert.Equal("AntiCheat", rows[0].AgentName);
            Assert.NotEmpty(rows[0].RequestId);
            Assert.NotEmpty(rows[0].PromptHash);
        }

        [Fact]
        public void RunAgent_HeuristicFallback_LogsAiQueryWithHeuristic()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "" } });
            _plugin.InitializeLlmClient();

            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            var rows = GetAiLogRows();
            Assert.Single(rows);
            Assert.Equal("AntiCheat", rows[0].AgentName);
            Assert.Contains("HEURISTIC", rows[0].RawOutput);
        }

        [Fact]
        public void RunAgent_EmptyMetrics_ReturnsZero()
        {
            var verdict = _plugin.RunAntiCheatAgent("76561190000000001", "Player", new Dictionary<string, double>());
            Assert.Equal(0, verdict.CheatLikelihood);
            Assert.Empty(verdict.PrimaryIndicators);
        }

        [Fact]
        public void RunAgent_NullMetrics_ReturnsZero()
        {
            var verdict = _plugin.RunAntiCheatAgent("76561190000000001", "Player", null!);
            Assert.Equal(0, verdict.CheatLikelihood);
            Assert.Empty(verdict.PrimaryIndicators);
        }

        [Fact]
        public void RunAgent_HardThreshold_ZScoreAbove4_AutoFlagHighLikelihood()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 10.0, 10.0, 10.0, 10.0 });
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "" } });
            _plugin.InitializeLlmClient();

            var metrics = new Dictionary<string, double> { { "headshot_ratio", 100.0 } };
            var verdict = _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            Assert.Equal(95, verdict.CheatLikelihood);
            Assert.Contains("extreme_outlier", verdict.PrimaryIndicators);
        }

        [Fact]
        public void QueryAntiCheatEvents_ReturnsEvents()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "" } });
            _plugin.InitializeLlmClient();

            var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 } };
            _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);

            var events = _plugin.QueryAntiCheatEvents("76561190000000001");
            Assert.Single(events);
            Assert.Equal("headshot_ratio", events[0].MetricName);
            Assert.Equal("Player", events[0].PlayerName);
        }

        [Fact]
        public void QueryAntiCheatEvents_LimitRespected()
        {
            _plugin.ComputeAndStoreBaseline("76561190000000001", "headshot_ratio", new List<double> { 10.0, 12.0, 14.0, 16.0, 18.0 });
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "" } });
            _plugin.InitializeLlmClient();

            for (int i = 0; i < 5; i++)
            {
                var metrics = new Dictionary<string, double> { { "headshot_ratio", 30.0 + i } };
                _plugin.RunAntiCheatAgent("76561190000000001", "Player", metrics);
            }

            var events = _plugin.QueryAntiCheatEvents("76561190000000001", 2);
            Assert.Equal(2, events.Count);
        }
    }
}
