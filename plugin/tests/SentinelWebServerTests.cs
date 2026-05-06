using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    [Collection("WebServerSequential")]
    public class SentinelWebServerTests : IDisposable
    {
        private readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private const string TestAuthToken = "test-token-12345";

        public void Dispose()
        {
            _client.Dispose();
        }

        private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestAuthToken);
            return request;
        }

        [Fact]
        public void WebServer_DefaultConfig_HasWebPanelSection()
        {
            var config = new Oxide.Plugins.SentinelConfig();
            Assert.NotNull(config.WebPanel);
            Assert.True(config.WebPanel.Enabled);
            Assert.Equal(31002, config.WebPanel.Port);
            Assert.Equal(60, config.WebPanel.RateLimitPerMinute);
            Assert.Equal("", config.WebPanel.AuthToken);
        }

        [Fact]
        public async Task WebServer_Starts_OnConfiguredPort_RespondsOk()
        {
            var port = 31005;
            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken);

            server.Start(port);
            Assert.True(server.IsRunning);
            Assert.Equal(port, server.CurrentPort);

            try
            {
                var request = CreateAuthenticatedRequest(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                var response = await _client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.Contains("\"status\":\"ok\"", body);
                Assert.Contains("Sentinel Web Panel", body);
            }
            finally
            {
                server.Stop();
            }

            Assert.False(server.IsRunning);
        }

        [Fact]
        public void WebServer_PortConflict_EntersDisabledState()
        {
            var port = 31006;
            var occupant = new HttpListener();
            occupant.Prefixes.Add($"http://127.0.0.1:{port}/");
            occupant.Start();

            try
            {
                var logger = new TestRuntimeBridge();
                var listener = new Oxide.Plugins.HttpListenerWrapper();
                var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken);

                server.Start(port);

                Assert.False(server.IsRunning);
                Assert.True(server.IsDisabled);
                Assert.Contains($"Web panel port {port} is in use", logger.LastError);
            }
            finally
            {
                occupant.Stop();
            }
        }

        [Fact]
        public async Task WebServer_ConfigReload_SwitchesPort()
        {
            var port1 = 31007;
            var port2 = 31008;

            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken);

            server.Start(port1);
            Assert.True(server.IsRunning);
            Assert.Equal(port1, server.CurrentPort);

            var request1 = CreateAuthenticatedRequest(HttpMethod.Get, $"http://127.0.0.1:{port1}/");
            var response1 = await _client.SendAsync(request1);
            Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            server.RestartIfPortChanged(port2);
            stopwatch.Stop();

            Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Port switch took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
            Assert.True(server.IsRunning);
            Assert.Equal(port2, server.CurrentPort);

            var request2 = CreateAuthenticatedRequest(HttpMethod.Get, $"http://127.0.0.1:{port2}/");
            var response2 = await _client.SendAsync(request2);
            Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

            try
            {
                await _client.GetAsync($"http://127.0.0.1:{port1}/");
                Assert.Fail("Expected exception for old port");
            }
            catch (HttpRequestException)
            {
                // Expected
            }

            server.Stop();
        }

        [Fact]
        public void WebServer_Disabled_RetriesOnNewPort()
        {
            var port1 = 31009;
            var port2 = 31010;

            var occupant = new HttpListener();
            occupant.Prefixes.Add($"http://127.0.0.1:{port1}/");
            occupant.Start();

            try
            {
                var logger = new TestRuntimeBridge();
                var listener = new Oxide.Plugins.HttpListenerWrapper();
                var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken);

                server.Start(port1);
                Assert.True(server.IsDisabled);

                server.RestartIfPortChanged(port2);
                Assert.True(server.IsRunning);
                Assert.False(server.IsDisabled);
                Assert.Equal(port2, server.CurrentPort);
            }
            finally
            {
                occupant.Stop();
            }
        }

        [Fact]
        public void WebServer_Stop_IsIdempotent()
        {
            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken);

            server.Stop();
            server.Stop();
            Assert.False(server.IsRunning);
        }

        [Fact]
        public void Config_WebPanel_IsNotNull()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"sentinel_ws_test_{Guid.NewGuid()}");
            var configPath = Path.Combine(dir, "Sentinel.json");
            Directory.CreateDirectory(dir);

            var plugin = new TestableSentinelWithConfig(configPath);
            plugin.LoadPluginConfig();

            var config = plugin.Config!.ReadObject<Oxide.Plugins.SentinelConfig>();
            Assert.NotNull(config);
            Assert.NotNull(config.WebPanel);
            Assert.True(config.WebPanel.Port > 0);

            try { File.Delete(configPath); } catch { }
            try { Directory.Delete(dir, true); } catch { }
        }

        // ─── Auth tests ───

        [Fact]
        public async Task Auth_MissingToken_Returns401_EmptyBody()
        {
            var port = 31100;
            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken);

            server.Start(port);
            try
            {
                var response = await _client.GetAsync($"http://127.0.0.1:{port}/");
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.Equal("", body);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task Auth_MalformedToken_Returns401_EmptyBody()
        {
            var port = 31101;
            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken);

            server.Start(port);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                request.Headers.TryAddWithoutValidation("Authorization", "Basic dXNlcjpwYXNz");
                var response = await _client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.Equal("", body);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task Auth_InvalidToken_Returns401_EmptyBody()
        {
            var port = 31102;
            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken);

            server.Start(port);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong-token");
                var response = await _client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.Equal("", body);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task Auth_ValidToken_Returns200()
        {
            var port = 31103;
            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken);

            server.Start(port);
            try
            {
                var request = CreateAuthenticatedRequest(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                var response = await _client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void Auth_ConstantTimeComparison_EqualTokens()
        {
            Assert.True(Oxide.Plugins.SentinelWebAuth.SecureCompareTokens("same", "same"));
        }

        [Fact]
        public void Auth_ConstantTimeComparison_DifferentTokens()
        {
            Assert.False(Oxide.Plugins.SentinelWebAuth.SecureCompareTokens("token-a", "token-b"));
        }

        [Fact]
        public void Auth_ConstantTimeComparison_NullTokens()
        {
            Assert.True(Oxide.Plugins.SentinelWebAuth.SecureCompareTokens(null!, null!));
            Assert.False(Oxide.Plugins.SentinelWebAuth.SecureCompareTokens("a", null!));
            Assert.False(Oxide.Plugins.SentinelWebAuth.SecureCompareTokens(null!, "a"));
        }

        [Fact]
        public void Auth_ConstantTimeComparison_TimingDistribution()
        {
            const string correctToken = "correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345";
            const string earlyDiff = "xorrect-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345";
            const string lateDiff = "correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-12345-correct-token-1234x";
            const int warmup = 1000;
            const int iterations = 5000;

            // Warmup
            for (int i = 0; i < warmup; i++)
            {
                Oxide.Plugins.SentinelWebAuth.SecureCompareTokens(correctToken, earlyDiff);
                Oxide.Plugins.SentinelWebAuth.SecureCompareTokens(correctToken, lateDiff);
            }

            long earlyTotal = 0;
            long lateTotal = 0;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var e = System.Diagnostics.Stopwatch.StartNew();
                Oxide.Plugins.SentinelWebAuth.SecureCompareTokens(correctToken, earlyDiff);
                e.Stop();
                earlyTotal += e.ElapsedTicks;

                var l = System.Diagnostics.Stopwatch.StartNew();
                Oxide.Plugins.SentinelWebAuth.SecureCompareTokens(correctToken, lateDiff);
                l.Stop();
                lateTotal += l.ElapsedTicks;
            }
            sw.Stop();

            double earlyMean = (double)earlyTotal / iterations;
            double lateMean = (double)lateTotal / iterations;
            double maxMean = Math.Max(earlyMean, lateMean);
            double relativeDiff = maxMean > 0 ? Math.Abs(earlyMean - lateMean) / maxMean : 0;

            // Constant-time comparison should show no significant timing difference
            // regardless of where the mismatch occurs. Allow 35% tolerance for JIT/GC noise.
            Assert.True(relativeDiff < 0.35,
                $"Timing difference too large: early_mean={earlyMean:F2} ticks, late_mean={lateMean:F2} ticks, relative_diff={relativeDiff:F2}");
        }

        // ─── Rate limit tests ───

        [Fact]
        public async Task RateLimit_WithinLimit_Returns200()
        {
            var port = 31104;
            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken, rateLimitPerMinute: 5);

            server.Start(port);
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var request = CreateAuthenticatedRequest(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                    var response = await _client.SendAsync(request);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task RateLimit_ExceedsLimit_Returns429_WithRetryAfter()
        {
            var port = 31105;
            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken, rateLimitPerMinute: 3);

            server.Start(port);
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var request = CreateAuthenticatedRequest(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                    var response = await _client.SendAsync(request);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }

                // 4th request should be rate limited
                var blockedRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                var blockedResponse = await _client.SendAsync(blockedRequest);
                Assert.Equal((HttpStatusCode)429, blockedResponse.StatusCode);
                Assert.True(blockedResponse.Headers.Contains("Retry-After"));
                var retryAfter = blockedResponse.Headers.GetValues("Retry-After");
                Assert.Single(retryAfter);
                Assert.True(int.Parse(retryAfter.First()) > 0);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task RateLimit_SlidingWindow_OldestRequestExpires()
        {
            var port = 31106;
            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken, rateLimitPerMinute: 2, rateLimitWindowSeconds: 1);

            server.Start(port);
            try
            {
                // Use up the limit
                for (int i = 0; i < 2; i++)
                {
                    var request = CreateAuthenticatedRequest(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                    var response = await _client.SendAsync(request);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }

                // 3rd request blocked
                var blocked = CreateAuthenticatedRequest(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                var blockedResp = await _client.SendAsync(blocked);
                Assert.Equal((HttpStatusCode)429, blockedResp.StatusCode);

                // Wait for sliding window to expire the oldest request (window = 1 second)
                await Task.Delay(1100);

                // Now should succeed (sliding window, not hard bucket)
                var retry = CreateAuthenticatedRequest(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                var retryResp = await _client.SendAsync(retry);
                Assert.Equal(HttpStatusCode.OK, retryResp.StatusCode);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task RateLimit_UnauthenticatedRequests_StillCounted()
        {
            var port = 31107;
            var logger = new TestRuntimeBridge();
            var listener = new Oxide.Plugins.HttpListenerWrapper();
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger, TestAuthToken, rateLimitPerMinute: 2);

            server.Start(port);
            try
            {
                // Two unauthenticated requests (will get 401 but still count toward rate limit)
                for (int i = 0; i < 2; i++)
                {
                    var response = await _client.GetAsync($"http://127.0.0.1:{port}/");
                    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                }

                // 3rd request should be rate limited even without auth
                var blocked = await _client.GetAsync($"http://127.0.0.1:{port}/");
                Assert.Equal((HttpStatusCode)429, blocked.StatusCode);
            }
            finally
            {
                server.Stop();
            }
        }

        private class TestableSentinelWithConfig : SentinelPlugin
        {
            private readonly string _configPath;
            public TestableSentinelWithConfig(string configPath)
            {
                _configPath = configPath;
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                Config = new Oxide.Core.Plugins.DynamicConfigFile(_configPath);
            }
            public override string GetConfigPath() => _configPath;
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }
        }

        private class TestRuntimeBridge : Oxide.Plugins.IRuntimeBridge
        {
            public string LastError { get; private set; } = "";
            public Oxide.Plugins.RuntimeType Runtime => Oxide.Plugins.RuntimeType.Oxide;
            public void LogInfo(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message) => LastError = message;
        }
    }
}
