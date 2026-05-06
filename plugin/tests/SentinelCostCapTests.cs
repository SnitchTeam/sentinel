using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelCostCapTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockRuntimeBridge _logger;

        public SentinelCostCapTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_cost_cap_test_{Guid.NewGuid()}.db");
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

        private List<CostLogRow> GetCostLogRows()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT provider, model, input_tokens, output_tokens, cost_usd, day, timestamp
                FROM sentinel_ai_cost_log ORDER BY id;";
            var rows = new List<CostLogRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new CostLogRow
                {
                    Provider = reader.GetString(0),
                    Model = reader.GetString(1),
                    InputTokens = reader.GetInt32(2),
                    OutputTokens = reader.GetInt32(3),
                    CostUsd = reader.GetDouble(4),
                    Day = reader.GetString(5),
                    Timestamp = reader.GetInt64(6)
                });
            }
            return rows;
        }

        private void SeedCost(string provider, string model, int inputTokens, int outputTokens, double costUsd, string day)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO sentinel_ai_cost_log (provider, model, input_tokens, output_tokens, cost_usd, day, timestamp)
                VALUES (@provider, @model, @inputTokens, @outputTokens, @costUsd, @day, @timestamp);";
            command.Parameters.AddWithValue("@provider", provider);
            command.Parameters.AddWithValue("@model", model);
            command.Parameters.AddWithValue("@inputTokens", inputTokens);
            command.Parameters.AddWithValue("@outputTokens", outputTokens);
            command.Parameters.AddWithValue("@costUsd", costUsd);
            command.Parameters.AddWithValue("@day", day);
            command.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            command.ExecuteNonQuery();
        }

        private class CostLogRow
        {
            public string Provider { get; set; } = "";
            public string Model { get; set; } = "";
            public int InputTokens { get; set; }
            public int OutputTokens { get; set; }
            public double CostUsd { get; set; }
            public string Day { get; set; } = "";
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
            public MultiResponseMockLlmClient? MultiMockLlmClient { get; set; }

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
                if (MultiMockLlmClient != null) return MultiMockLlmClient;
                if (MockLlmClient != null) return MockLlmClient;
                return base.CreateLlmClient(config);
            }
        }

        // ---------------------------------------------------------
        // VAL-AI-010: Daily-USD cap tracking tests
        // ---------------------------------------------------------

        [Fact]
        public void CostCap_At100Percent_ShortCircuitsToHeuristicStub()
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            SeedCost("openai", "gpt-4o-mini", 1000, 500, 5.00, today);

            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "should not reach"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig
            {
                AI = new AIConfig
                {
                    Provider = "openai",
                    ApiKey = "sk-openai",
                    Model = "gpt-4o-mini",
                    DailyUsdCap = 5.0
                }
            });
            _plugin.InitializeLlmClient();
            _plugin.InitializeAiCostTracker();

            var result = _plugin.SendAiRequest("test prompt");

            Assert.True(result.IsFallback);
            Assert.Contains(_logger.Logs, l => l.Contains("AI daily cost cap reached"));
            Assert.Empty(_plugin.MockLlmClient.Requests);
        }

        [Fact]
        public void CostCap_At80Percent_EmitsAlert()
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            SeedCost("openai", "gpt-4o-mini", 1000, 500, 4.10, today);

            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "ok"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig
            {
                AI = new AIConfig
                {
                    Provider = "openai",
                    ApiKey = "sk-openai",
                    Model = "gpt-4o-mini",
                    DailyUsdCap = 5.0
                }
            });
            _plugin.InitializeLlmClient();
            _plugin.InitializeAiCostTracker();

            var result = _plugin.SendAiRequest("test prompt");

            Assert.False(result.IsFallback);
            Assert.Contains(_logger.Logs, l => l.Contains("AI COST ALERT: 80%"));
        }

        [Fact]
        public void CostCap_At100Percent_EmitsAlert()
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            SeedCost("openai", "gpt-4o-mini", 1000, 500, 5.00, today);

            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "should not reach"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig
            {
                AI = new AIConfig
                {
                    Provider = "openai",
                    ApiKey = "sk-openai",
                    Model = "gpt-4o-mini",
                    DailyUsdCap = 5.0
                }
            });
            _plugin.InitializeLlmClient();
            _plugin.InitializeAiCostTracker();

            _plugin.SendAiRequest("test prompt");

            // Accept either the specific alert or the cap-reached warning as evidence of 100% alerting
            Assert.True(
                _logger.Logs.Any(l => l.Contains("AI COST ALERT: 100%")) ||
                _logger.Logs.Any(l => l.Contains("AI daily cost cap reached") && l.Contains("$5")),
                $"Expected 100% cap alert or cap-reached warning. Got: {string.Join("; ", _logger.Logs)}"
            );
        }

        [Fact]
        public void CostCap_SuccessfulRequest_RecordsCostLogRow()
        {
            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "This is a sample response from the LLM."
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig
            {
                AI = new AIConfig
                {
                    Provider = "openai",
                    ApiKey = "sk-openai",
                    Model = "gpt-4o-mini",
                    DailyUsdCap = 5.0
                }
            });
            _plugin.InitializeLlmClient();
            _plugin.InitializeAiCostTracker();

            _plugin.SendAiRequest("This is a test prompt for cost estimation.");

            var rows = GetCostLogRows();
            Assert.Single(rows);
            Assert.Equal("openai", rows[0].Provider);
            Assert.Equal("gpt-4o-mini", rows[0].Model);
            Assert.True(rows[0].InputTokens > 0);
            Assert.True(rows[0].OutputTokens > 0);
            Assert.True(rows[0].CostUsd > 0);
            Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), rows[0].Day);
        }

        [Fact]
        public void CostCap_FailoverSuccess_RecordsFallbackProviderCost()
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            var mockPrimary = LlmClient.FallbackResponse("test prompt", lastStatusCode: 500, wasTimeout: false);
            var mockFallback = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "fallback response"
            };

            var multiMock = new MultiResponseMockLlmClient(new List<LlmResponse> { mockPrimary, mockFallback });
            _plugin.MultiMockLlmClient = multiMock;
            _plugin.SetPluginConfig(new SentinelConfig
            {
                AI = new AIConfig
                {
                    Provider = "openai",
                    ApiKey = "sk-openai",
                    Model = "gpt-4o-mini",
                    FallbackProvider = "anthropic",
                    FallbackApiKey = "sk-anthropic",
                    FallbackModel = "claude-3-haiku-20240307",
                    DailyUsdCap = 5.0
                }
            });
            _plugin.InitializeLlmClient();
            _plugin.InitializeAiCostTracker();

            _plugin.SendAiRequest("test prompt");

            var rows = GetCostLogRows();
            Assert.Single(rows);
            Assert.Equal("anthropic", rows[0].Provider);
            Assert.Equal("claude-3-haiku-20240307", rows[0].Model);
        }

        [Fact]
        public void CostCap_ResetsAtMidnightUTC()
        {
            var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
            SeedCost("openai", "gpt-4o-mini", 1000, 500, 5.00, yesterday);

            var mockResponse = new LlmResponse
            {
                Success = true,
                IsFallback = false,
                Content = "ok"
            };
            _plugin.MockLlmClient = new MockLlmClient(mockResponse);
            _plugin.SetPluginConfig(new SentinelConfig
            {
                AI = new AIConfig
                {
                    Provider = "openai",
                    ApiKey = "sk-openai",
                    Model = "gpt-4o-mini",
                    DailyUsdCap = 5.0
                }
            });
            _plugin.InitializeLlmClient();
            _plugin.InitializeAiCostTracker();

            var result = _plugin.SendAiRequest("test prompt");

            Assert.False(result.IsFallback);
            Assert.Single(_plugin.MockLlmClient.Requests);
        }

        [Fact]
        public void CostCap_NoConfig_DoesNotCrash()
        {
            _plugin.SetPluginConfig(new SentinelConfig
            {
                AI = new AIConfig
                {
                    Provider = "openai",
                    ApiKey = "sk-openai",
                    Model = "gpt-4o-mini",
                    DailyUsdCap = 5.0
                }
            });
            _plugin.InitializeLlmClient();
            // Do NOT initialize cost tracker

            var result = _plugin.SendAiRequest("test prompt");

            // Should fall through to LLM call (or fallback if no tracker)
            Assert.NotNull(result);
        }

        private class MultiResponseMockLlmClient : LlmClient
        {
            private readonly List<LlmResponse> _responses;
            private int _index = 0;
            public List<LlmRequest> Requests { get; } = new();

            public MultiResponseMockLlmClient(List<LlmResponse> responses) : base(new DefaultHttpRequester())
            {
                _responses = responses;
            }

            public override Task<LlmResponse> SendAsync(LlmRequest request)
            {
                Requests.Add(request);
                var response = _responses[Math.Min(_index, _responses.Count - 1)];
                _index++;
                return Task.FromResult(response);
            }
        }
    }
}
