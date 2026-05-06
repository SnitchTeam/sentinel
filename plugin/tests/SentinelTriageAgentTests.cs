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
    public class SentinelTriageAgentTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockRuntimeBridge _logger;

        public SentinelTriageAgentTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_triage_test_{Guid.NewGuid()}.db");
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

        private void SeedActions(int count, string? targetSteamId = null)
        {
            for (int i = 0; i < count; i++)
            {
                _plugin.LogAuditAction(
                    actorSteamId: "76561190000000001",
                    actorName: "Admin",
                    targetSteamId: targetSteamId ?? $"76561190000000{(i % 50 + 10):D2}",
                    targetName: $"Player{i % 50}",
                    actionType: "kick",
                    reason: "Test reason",
                    durationMinutes: null,
                    success: true
                );
            }
        }

        private void SeedActionsForPlayer(string targetSteamId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _plugin.LogAuditAction(
                    actorSteamId: "76561190000000001",
                    actorName: "Admin",
                    targetSteamId: targetSteamId,
                    targetName: "TargetPlayer",
                    actionType: "warn",
                    reason: "Suspicious behavior",
                    durationMinutes: null,
                    success: true
                );
            }
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

        private int CountPromptRecords(string prompt)
        {
            return prompt.Split('\n').Count(line => line.TrimStart().StartsWith("- ["));
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

        [Fact]
        public void Triage_Prompt_ContainsExactly500Records()
        {
            SeedActions(600);
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "[]"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            _plugin.RunTriageAgent();

            Assert.Single(_plugin.MockLlmClient.Requests);
            var prompt = _plugin.MockLlmClient.Requests[0].Prompt;
            var recordCount = CountPromptRecords(prompt);
            Assert.Equal(500, recordCount);
        }

        [Fact]
        public void Triage_Prompt_ContainsFewerWhenTableSmaller()
        {
            SeedActions(50);
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "[]"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            _plugin.RunTriageAgent();

            Assert.Single(_plugin.MockLlmClient.Requests);
            var prompt = _plugin.MockLlmClient.Requests[0].Prompt;
            var recordCount = CountPromptRecords(prompt);
            Assert.Equal(50, recordCount);
        }

        [Fact]
        public void Triage_LlmResponse_ValidJsonSchema()
        {
            SeedActions(10);
            var anomalies = new List<TriageAnomaly>
            {
                new TriageAnomaly { player_id = "76561198000000099", anomaly_type = "aim_assist", severity = "high", confidence = 92.5 }
            };
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = JsonSerializer.Serialize(anomalies)
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var result = _plugin.RunTriageAgent();

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("76561198000000099", result[0].player_id);
            Assert.Equal("aim_assist", result[0].anomaly_type);
            Assert.Equal("high", result[0].severity);
            Assert.True(result[0].confidence > 0);
        }

        [Fact]
        public void Triage_LlmResponse_OpenAiFormat_ParsedCorrectly()
        {
            SeedActions(10);
            var anomalies = new List<TriageAnomaly>
            {
                new TriageAnomaly { player_id = "76561198000000099", anomaly_type = "aim_assist", severity = "high", confidence = 92.5 }
            };
            var openAiWrapper = new
            {
                choices = new[]
                {
                    new { message = new { content = JsonSerializer.Serialize(anomalies) } }
                }
            };
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = JsonSerializer.Serialize(openAiWrapper)
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var result = _plugin.RunTriageAgent();

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("76561198000000099", result[0].player_id);
        }

        [Fact]
        public void Triage_LlmFallback_HeuristicOutputLogged()
        {
            SeedActions(100);
            var fallbackResponse = LlmClient.FallbackResponse("test prompt");
            _plugin.MockLlmClient = new MockLlmClient(fallbackResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var result = _plugin.RunTriageAgent();

            Assert.NotNull(result);
            Assert.Contains(_logger.Logs, l => l.Contains("Triage falling back to heuristic rules"));

            var rows = GetAiLogRows();
            Assert.Single(rows);
            Assert.Equal("Triage", rows[0].AgentName);
            Assert.Contains("HEURISTIC", rows[0].RawOutput);
        }

        [Fact]
        public void Triage_Heuristic_ThreeSigmaFlagsHigh()
        {
            SeedActionsForPlayer("76561190000000001", 50);
            for (int i = 0; i < 10; i++)
            {
                SeedActionsForPlayer($"765611900000000{i + 2:D2}", 1);
            }

            var fallbackResponse = LlmClient.FallbackResponse("test prompt");
            _plugin.MockLlmClient = new MockLlmClient(fallbackResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var result = _plugin.RunTriageAgent();

            var highAnomaly = result.FirstOrDefault(a => a.player_id == "76561190000000001");
            Assert.NotNull(highAnomaly);
            Assert.Equal("high", highAnomaly!.severity);
            Assert.Equal("action_frequency_spike", highAnomaly.anomaly_type);
        }

        [Fact]
        public void Triage_Heuristic_MediumAndLow()
        {
            SeedActionsForPlayer("76561190000000001", 60);
            SeedActionsForPlayer("76561190000000002", 35);
            for (int i = 0; i < 20; i++)
            {
                SeedActionsForPlayer($"765611900000000{i + 3:D2}", 1);
            }

            var fallbackResponse = LlmClient.FallbackResponse("test prompt");
            _plugin.MockLlmClient = new MockLlmClient(fallbackResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var result = _plugin.RunTriageAgent();

            var highAnomaly = result.FirstOrDefault(a => a.player_id == "76561190000000001");
            Assert.NotNull(highAnomaly);
            Assert.Equal("high", highAnomaly!.severity);

            var mediumAnomaly = result.FirstOrDefault(a => a.player_id == "76561190000000002");
            Assert.NotNull(mediumAnomaly);
            Assert.Equal("medium", mediumAnomaly!.severity);
        }

        [Fact]
        public void Triage_EmptyActions_ReturnsEmpty()
        {
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "[]"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            var result = _plugin.RunTriageAgent();

            Assert.Empty(result);
        }

        [Fact]
        public void Triage_NoApiKey_UsesHeuristic()
        {
            SeedActions(50);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "" } });
            _plugin.InitializeLlmClient();

            var result = _plugin.RunTriageAgent();

            Assert.Contains(_logger.Logs, l => l.Contains("Triage falling back to heuristic rules"));
            Assert.NotNull(result);
        }

        [Fact]
        public void Triage_CreatesAiSuggestions()
        {
            SeedActionsForPlayer("76561190000000001", 50);
            for (int i = 0; i < 10; i++)
            {
                SeedActionsForPlayer($"765611900000000{i + 2:D2}", 1);
            }

            var fallbackResponse = LlmClient.FallbackResponse("test prompt");
            _plugin.MockLlmClient = new MockLlmClient(fallbackResponse);
            _plugin.SetPluginConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "test-key" } });
            _plugin.InitializeLlmClient();

            _plugin.RunTriageAgent();

            Assert.True(_plugin.SuggestionCount > 0);
            var suggestion = _plugin.GetNextSuggestion();
            Assert.NotNull(suggestion);
            Assert.Equal("Triage", suggestion!.AgentName);
        }
    }
}
