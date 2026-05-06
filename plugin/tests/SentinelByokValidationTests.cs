using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Oxide.Plugins;
using Xunit;

namespace Sentinel.Tests
{
    public class SentinelByokValidationTests
    {
        // ---------------------------------------------------------
        // Mock helpers
        // ---------------------------------------------------------
        private class MockHttpRequester : IHttpRequester
        {
            private readonly List<HttpResponseMessage> _responses;
            private int _callCount = 0;

            public MockHttpRequester(List<HttpResponseMessage> responses)
            {
                _responses = responses;
            }

            public int CallCount => _callCount;
            public List<HttpRequestMessage> Requests { get; } = new();

            public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _callCount++;
                Requests.Add(request);
                var response = _responses[Math.Min(_callCount - 1, _responses.Count - 1)];
                return Task.FromResult(response);
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

        // ---------------------------------------------------------
        // VAL-AI-002: BYOK Key Validation tests
        // ---------------------------------------------------------

        [Fact]
        public async Task ByokValidator_OpenAI_200_ReturnsTrueAndLogsAccepted()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[]}") }
            });
            var logger = new MockRuntimeBridge();
            var validator = new ByokValidator(mock, logger);

            var result = await validator.ValidateAsync("openai", "https://api.openai.com/v1", "sk-test");

            Assert.True(result);
            Assert.Contains(logger.Logs, l => l.Contains("BYOK key accepted") && l.Contains("openai"));
            Assert.Single(mock.Requests);
            Assert.Equal("GET", mock.Requests[0].Method.ToString());
            Assert.Equal("https://api.openai.com/v1/models", mock.Requests[0].RequestUri?.ToString());
            Assert.Contains("Bearer sk-test", mock.Requests[0].Headers.Authorization?.ToString() ?? "");
        }

        [Fact]
        public async Task ByokValidator_OpenAI_401_ReturnsFalseAndLogsRejected()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("invalid key") }
            });
            var logger = new MockRuntimeBridge();
            var validator = new ByokValidator(mock, logger);

            var result = await validator.ValidateAsync("openai", "https://api.openai.com/v1", "sk-bad");

            Assert.False(result);
            Assert.Contains(logger.Logs, l => l.Contains("BYOK key rejected") && l.Contains("HTTP 401"));
        }

        [Fact]
        public async Task ByokValidator_OpenAI_403_ReturnsFalseAndLogsRejected()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("forbidden") }
            });
            var logger = new MockRuntimeBridge();
            var validator = new ByokValidator(mock, logger);

            var result = await validator.ValidateAsync("openai", "https://api.openai.com/v1", "sk-bad");

            Assert.False(result);
            Assert.Contains(logger.Logs, l => l.Contains("BYOK key rejected") && l.Contains("HTTP 403"));
        }

        [Fact]
        public async Task ByokValidator_Anthropic_200_ReturnsTrueAndLogsAccepted()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[]}") }
            });
            var logger = new MockRuntimeBridge();
            var validator = new ByokValidator(mock, logger);

            var result = await validator.ValidateAsync("anthropic", "https://api.anthropic.com/v1", "sk-ant-test");

            Assert.True(result);
            Assert.Contains(logger.Logs, l => l.Contains("BYOK key accepted") && l.Contains("anthropic"));
            Assert.Single(mock.Requests);
            Assert.Equal("GET", mock.Requests[0].Method.ToString());
            Assert.Equal("https://api.anthropic.com/v1/models", mock.Requests[0].RequestUri?.ToString());
            Assert.True(mock.Requests[0].Headers.Contains("anthropic-version"));
        }

        [Fact]
        public async Task ByokValidator_Anthropic_401_ReturnsFalseAndLogsRejected()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("invalid key") }
            });
            var logger = new MockRuntimeBridge();
            var validator = new ByokValidator(mock, logger);

            var result = await validator.ValidateAsync("anthropic", "https://api.anthropic.com/v1", "sk-ant-bad");

            Assert.False(result);
            Assert.Contains(logger.Logs, l => l.Contains("BYOK key rejected") && l.Contains("anthropic") && l.Contains("HTTP 401"));
        }

        [Fact]
        public async Task ByokValidator_Anthropic_404ThenMessages200_ReturnsTrue()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") },
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"content\":[]}") }
            });
            var logger = new MockRuntimeBridge();
            var validator = new ByokValidator(mock, logger);

            var result = await validator.ValidateAsync("anthropic", "https://api.anthropic.com/v1", "sk-ant-test");

            Assert.True(result);
            Assert.Equal(2, mock.CallCount);
            Assert.Equal("POST", mock.Requests[1].Method.ToString());
            Assert.Equal("https://api.anthropic.com/v1/messages", mock.Requests[1].RequestUri?.ToString());
            Assert.Contains(logger.Logs, l => l.Contains("BYOK key accepted") && l.Contains("anthropic") && l.Contains("POST /v1/messages"));
        }

        [Fact]
        public async Task ByokValidator_Anthropic_404ThenMessages401_ReturnsFalse()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.NotFound),
                new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("invalid") }
            });
            var logger = new MockRuntimeBridge();
            var validator = new ByokValidator(mock, logger);

            var result = await validator.ValidateAsync("anthropic", "https://api.anthropic.com/v1", "sk-ant-bad");

            Assert.False(result);
            Assert.Equal(2, mock.CallCount);
            Assert.Contains(logger.Logs, l => l.Contains("BYOK key rejected") && l.Contains("anthropic") && l.Contains("HTTP 401"));
        }

        [Fact]
        public async Task ByokValidator_EmptyKey_ReturnsFalse()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>());
            var logger = new MockRuntimeBridge();
            var validator = new ByokValidator(mock, logger);

            var result = await validator.ValidateAsync("openai", "https://api.openai.com/v1", "");

            Assert.False(result);
            Assert.Contains(logger.Logs, l => l.Contains("No BYOK API key configured"));
            Assert.Equal(0, mock.CallCount);
        }

        [Fact]
        public async Task ByokValidator_NetworkException_ReturnsFalse()
        {
            var mock = new FailingHttpRequester(new Exception("connection refused"));
            var logger = new MockRuntimeBridge();
            var validator = new ByokValidator(mock, logger);

            var result = await validator.ValidateAsync("openai", "https://api.openai.com/v1", "sk-test");

            Assert.False(result);
            Assert.Contains(logger.Logs, l => l.Contains("BYOK key validation failed") && l.Contains("connection refused"));
        }

        [Fact]
        public async Task ByokValidator_OpenAI_500_ReturnsFalse()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("server error") }
            });
            var logger = new MockRuntimeBridge();
            var validator = new ByokValidator(mock, logger);

            var result = await validator.ValidateAsync("openai", "https://api.openai.com/v1", "sk-test");

            Assert.False(result);
            Assert.Contains(logger.Logs, l => l.Contains("BYOK key validation returned HTTP 500"));
        }

        // ---------------------------------------------------------
        // Plugin integration tests
        // ---------------------------------------------------------

        [Fact]
        public void Plugin_InitializeLlmClient_ValidKey_SetsByokKeyValidTrue()
        {
            var plugin = new TestableSentinel();
            var config = new SentinelConfig
            {
                AI = new AIConfig
                {
                    Provider = "openai",
                    Endpoint = "https://api.openai.com/v1",
                    ApiKey = "sk-test",
                    MaxRetries = 1,
                    TimeoutSeconds = 5
                }
            };
            plugin.SetPluginConfig(config);

            plugin.InitializeLlmClient();

            Assert.True(config.ByokKeyValid);
            Assert.Contains(plugin.Logs, l => l.Contains("BYOK key accepted"));
        }

        [Fact]
        public void Plugin_InitializeLlmClient_InvalidKey_SetsByokKeyValidFalse()
        {
            var plugin = new TestableSentinel(rejectByok: true);
            var config = new SentinelConfig
            {
                AI = new AIConfig
                {
                    Provider = "openai",
                    Endpoint = "https://api.openai.com/v1",
                    ApiKey = "sk-bad",
                    MaxRetries = 1,
                    TimeoutSeconds = 5
                }
            };
            plugin.SetPluginConfig(config);

            plugin.InitializeLlmClient();

            Assert.False(config.ByokKeyValid);
            Assert.Contains(plugin.Logs, l => l.Contains("BYOK key rejected"));
        }

        [Fact]
        public void Plugin_InitializeLlmClient_NoKey_LogsWarning()
        {
            var plugin = new TestableSentinel();
            var config = new SentinelConfig
            {
                AI = new AIConfig
                {
                    Provider = "openai",
                    Endpoint = "https://api.openai.com/v1",
                    ApiKey = "",
                    MaxRetries = 1,
                    TimeoutSeconds = 5
                }
            };
            plugin.SetPluginConfig(config);

            plugin.InitializeLlmClient();

            Assert.False(config.ByokKeyValid);
            Assert.Contains(plugin.Logs, l => l.Contains("No BYOK API key configured"));
        }

        [Fact]
        public void Plugin_InitializeLlmClient_NullConfig_UsesDefaults()
        {
            var plugin = new TestableSentinel();
            plugin.SetPluginConfig(new SentinelConfig());

            plugin.InitializeLlmClient();

            Assert.False(plugin.PluginConfig!.ByokKeyValid);
            Assert.Contains(plugin.Logs, l => l.Contains("No BYOK API key configured"));
        }

        // ---------------------------------------------------------
        // Supporting mocks
        // ---------------------------------------------------------

        private class FailingHttpRequester : IHttpRequester
        {
            private readonly Exception _exception;

            public FailingHttpRequester(Exception exception)
            {
                _exception = exception;
            }

            public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw _exception;
            }
        }

        private class TestableSentinel : Oxide.Plugins.Sentinel
        {
            private readonly bool _rejectByok;
            public List<string> Logs { get; } = new();

            public TestableSentinel(bool rejectByok = false)
            {
                _rejectByok = rejectByok;
            }

            public override void Puts(string message)
            {
                Logs.Add($"INFO: {message}");
                base.Puts(message);
            }

            public override void PrintWarning(string message)
            {
                Logs.Add($"WARN: {message}");
                base.PrintWarning(message);
            }

            public override void PrintError(string message)
            {
                Logs.Add($"ERROR: {message}");
                base.PrintError(message);
            }

            public override void LoadDefaultConfig() { }

            public void SetPluginConfig(Oxide.Plugins.SentinelConfig config)
            {
                PluginConfig = config;
            }

            public override ByokValidator CreateByokValidator()
            {
                var logger = new MockRuntimeBridge();
                var mock = new MockHttpRequester(new List<HttpResponseMessage>
                {
                    new HttpResponseMessage(_rejectByok ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}")
                    }
                });
                return new ByokValidator(mock, logger);
            }
        }
    }
}
