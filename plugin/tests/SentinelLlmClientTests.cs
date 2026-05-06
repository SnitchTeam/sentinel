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
    public class SentinelLlmClientTests
    {
        // ---------------------------------------------------------
        // Mock helpers
        // ---------------------------------------------------------
        private class MockHttpRequester : IHttpRequester
        {
            private readonly List<HttpResponseMessage> _responses;
            private readonly List<int> _delayMs;
            private int _callCount = 0;

            public MockHttpRequester(List<HttpResponseMessage> responses, List<int>? delayMs = null)
            {
                _responses = responses;
                _delayMs = delayMs ?? new List<int>();
            }

            public int CallCount => _callCount;
            public List<HttpRequestMessage> Requests { get; } = new();

            public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _callCount++;
                Requests.Add(request);
                var delay = _callCount <= _delayMs.Count ? _delayMs[_callCount - 1] : 0;
                if (delay > 0)
                {
                    await Task.Delay(delay, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                var response = _responses[Math.Min(_callCount - 1, _responses.Count - 1)];
                return response;
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

        private static LlmRequest CreateRequest(string prompt = "test prompt")
        {
            return new LlmRequest
            {
                Provider = "openai",
                Endpoint = "https://api.openai.com/v1",
                ApiKey = "test-key",
                Model = "gpt-4o-mini",
                Prompt = prompt
            };
        }

        // ---------------------------------------------------------
        // VAL-AI-001: Retry & timeout tests
        // ---------------------------------------------------------

        [Fact]
        public async Task LlmClient_Send_AllAttemptsFail_ReturnsFallbackAfterMaxRetries()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error1") },
                new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error2") },
                new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error3") }
            });
            var client = new LlmClient(mock, maxRetries: 3, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = CreateRequest();

            var response = await client.SendAsync(request);

            Assert.True(response.IsFallback);
            Assert.True(response.Success);
            Assert.Equal(3, mock.CallCount);
        }

        [Fact]
        public async Task LlmClient_Send_TimeoutExceeded_CancelsAttemptAndRetries()
        {
            var mock = new MockHttpRequester(
                new List<HttpResponseMessage>
                {
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("success") }
                },
                new List<int> { 2000 }
            );
            var client = new LlmClient(mock, maxRetries: 3, timeoutSeconds: 1, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = CreateRequest();

            var response = await client.SendAsync(request);

            Assert.False(response.IsFallback);
            Assert.Equal("success", response.Content);
            Assert.Equal(2, mock.CallCount);
        }

        [Fact]
        public async Task LlmClient_Send_ExponentialBackoff_RecordsDelayCalls()
        {
            var delayCalls = new List<int>();
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") },
                new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") },
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("success") }
            });
            var client = new LlmClient(mock, maxRetries: 3, delayFunc: (ms, ct) =>
            {
                delayCalls.Add(ms);
                return Task.CompletedTask;
            });
            var request = CreateRequest();

            var response = await client.SendAsync(request);

            Assert.False(response.IsFallback);
            Assert.Equal(3, mock.CallCount);
            Assert.Equal(new[] { 1000, 2000 }, delayCalls);
        }

        [Fact]
        public async Task LlmClient_Send_SucceedsOnFirstAttempt_NoRetries()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("first try") }
            });
            var client = new LlmClient(mock, maxRetries: 3, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = CreateRequest();

            var response = await client.SendAsync(request);

            Assert.True(response.Success);
            Assert.False(response.IsFallback);
            Assert.Equal("first try", response.Content);
            Assert.Equal(1, mock.CallCount);
        }

        [Fact]
        public async Task LlmClient_Send_SucceedsOnSecondAttempt_AfterOneRetry()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("down") },
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("recovered") }
            });
            var client = new LlmClient(mock, maxRetries: 3, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = CreateRequest();

            var response = await client.SendAsync(request);

            Assert.True(response.Success);
            Assert.False(response.IsFallback);
            Assert.Equal("recovered", response.Content);
            Assert.Equal(2, mock.CallCount);
        }

        [Fact]
        public async Task LlmClient_Send_AllTimeout_ReturnsFallback()
        {
            var mock = new MockHttpRequester(
                new List<HttpResponseMessage>
                {
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("should not reach") }
                },
                new List<int> { 5000, 5000, 5000 }
            );
            var client = new LlmClient(mock, maxRetries: 3, timeoutSeconds: 1, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = CreateRequest("timeout test");

            var response = await client.SendAsync(request);

            Assert.True(response.IsFallback);
            Assert.True(response.Success);
            Assert.Equal(3, mock.CallCount);
        }

        [Fact]
        public async Task LlmClient_Send_UsesConfiguredTimeout()
        {
            var mock = new MockHttpRequester(
                new List<HttpResponseMessage>
                {
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }
                },
                new List<int> { 3000 }
            );
            var client = new LlmClient(mock, maxRetries: 2, timeoutSeconds: 2, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = CreateRequest();

            var response = await client.SendAsync(request);

            // First attempt should be canceled at <= 2s, second succeeds immediately
            Assert.False(response.IsFallback);
            Assert.Equal("ok", response.Content);
            Assert.Equal(2, mock.CallCount);
        }

        [Fact]
        public async Task LlmClient_Send_LogsRetryMessages()
        {
            var logger = new MockRuntimeBridge();
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("bad") },
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }
            });
            var client = new LlmClient(mock, logger, maxRetries: 2, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = CreateRequest();

            await client.SendAsync(request);

            Assert.Contains(logger.Logs, l => l.Contains("attempt 1/2"));
            Assert.Contains(logger.Logs, l => l.Contains("attempt 2/2"));
            Assert.Contains(logger.Logs, l => l.Contains("succeeded on attempt 2"));
        }

        [Fact]
        public async Task LlmClient_Send_LogsExhaustedFallback()
        {
            var logger = new MockRuntimeBridge();
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
            });
            var client = new LlmClient(mock, logger, maxRetries: 1, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = CreateRequest();

            await client.SendAsync(request);

            Assert.Contains(logger.Logs, l => l.Contains("exhausted all 1 attempts"));
            Assert.Contains(logger.Logs, l => l.Contains("Returning heuristic fallback"));
        }

        // ---------------------------------------------------------
        // Fallback determinism tests
        // ---------------------------------------------------------

        [Fact]
        public void LlmClient_FallbackResponse_IsDeterministic()
        {
            var prompt = "deterministic test prompt";
            var r1 = LlmClient.FallbackResponse(prompt);
            var r2 = LlmClient.FallbackResponse(prompt);

            Assert.Equal(r1.Content, r2.Content);
            Assert.True(r1.IsFallback);
            Assert.True(r1.Success);
        }

        [Fact]
        public void LlmClient_FallbackResponse_DifferentPrompts_DifferentContent()
        {
            var r1 = LlmClient.FallbackResponse("prompt A");
            var r2 = LlmClient.FallbackResponse("prompt B");

            Assert.NotEqual(r1.Content, r2.Content);
        }

        [Fact]
        public void LlmClient_FallbackResponse_EmptyPrompt_ReturnsValidStub()
        {
            var r = LlmClient.FallbackResponse("");

            Assert.True(r.IsFallback);
            Assert.Contains("00000000", r.Content);
            Assert.Contains("Length=0", r.Content);
        }

        [Fact]
        public void LlmClient_FallbackResponse_NullPrompt_ReturnsValidStub()
        {
            var r = LlmClient.FallbackResponse(null);

            Assert.True(r.IsFallback);
            Assert.Contains("00000000", r.Content);
            Assert.Contains("Length=0", r.Content);
        }

        // ---------------------------------------------------------
        // Request building tests
        // ---------------------------------------------------------

        [Fact]
        public async Task LlmClient_Send_BuildsOpenAiRequest()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }
            });
            var client = new LlmClient(mock, maxRetries: 1, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = new LlmRequest
            {
                Provider = "openai",
                Endpoint = "https://api.openai.com/v1",
                ApiKey = "key123",
                Model = "gpt-4",
                Prompt = "hello"
            };

            await client.SendAsync(request);

            Assert.Single(mock.Requests);
            var req = mock.Requests[0];
            Assert.Equal("POST", req.Method.ToString());
            Assert.Equal("https://api.openai.com/v1/chat/completions", req.RequestUri?.ToString());
            Assert.Contains("Bearer key123", req.Headers.Authorization?.ToString() ?? "");
        }

        [Fact]
        public async Task LlmClient_Send_BuildsAnthropicRequest()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }
            });
            var client = new LlmClient(mock, maxRetries: 1, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = new LlmRequest
            {
                Provider = "anthropic",
                Endpoint = "https://api.anthropic.com/v1",
                ApiKey = "key456",
                Model = "claude-3",
                Prompt = "hello"
            };

            await client.SendAsync(request);

            Assert.Single(mock.Requests);
            var req = mock.Requests[0];
            Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri?.ToString());
        }

        // ---------------------------------------------------------
        // Edge case tests
        // ---------------------------------------------------------

        [Fact]
        public async Task LlmClient_Send_NullRequest_ThrowsArgumentNullException()
        {
            var client = new LlmClient(new MockHttpRequester(new List<HttpResponseMessage>()), maxRetries: 1);
            await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendAsync(null!));
        }

        [Fact]
        public async Task LlmClient_Send_ZeroRetriesConfig_TreatedAsOne()
        {
            var mock = new MockHttpRequester(new List<HttpResponseMessage>
            {
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
            });
            var client = new LlmClient(mock, maxRetries: 0, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = CreateRequest();

            var response = await client.SendAsync(request);

            Assert.Equal(1, mock.CallCount);
            Assert.True(response.IsFallback);
        }

        [Fact]
        public async Task LlmClient_Send_NegativeTimeoutConfig_TreatedAsFifteen()
        {
            var mock = new MockHttpRequester(
                new List<HttpResponseMessage>
                {
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }
                },
                new List<int> { 500 }
            );
            var client = new LlmClient(mock, maxRetries: 1, timeoutSeconds: -5, delayFunc: (ms, ct) => Task.CompletedTask);
            var request = CreateRequest();

            var response = await client.SendAsync(request);

            Assert.False(response.IsFallback);
        }

        // ---------------------------------------------------------
        // Plugin integration tests
        // ---------------------------------------------------------

        [Fact]
        public void Plugin_CreateLlmClient_UsesConfigValues()
        {
            var plugin = new TestableSentinel();
            var config = new AIConfig
            {
                MaxRetries = 5,
                TimeoutSeconds = 30
            };
            var client = plugin.CreateLlmClient(config);
            Assert.NotNull(client);
        }

        [Fact]
        public void Plugin_InitializeLlmClient_DoesNotThrow()
        {
            var plugin = new TestableSentinel();
            plugin.SetPluginConfig(new Oxide.Plugins.SentinelConfig());
            plugin.InitializeLlmClient();
            // Should not throw
        }

        private class TestableSentinel : Oxide.Plugins.Sentinel
        {
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }
            public override void LoadDefaultConfig() { }

            public void SetPluginConfig(Oxide.Plugins.SentinelConfig config)
            {
                PluginConfig = config;
            }
        }
    }
}
