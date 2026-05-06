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

        public void Dispose()
        {
            _client.Dispose();
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
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger);

            server.Start(port);
            Assert.True(server.IsRunning);
            Assert.Equal(port, server.CurrentPort);

            try
            {
                var response = await _client.GetAsync($"http://127.0.0.1:{port}/");
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
                var server = new Oxide.Plugins.SentinelWebServer(listener, logger);

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
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger);

            server.Start(port1);
            Assert.True(server.IsRunning);
            Assert.Equal(port1, server.CurrentPort);

            var response1 = await _client.GetAsync($"http://127.0.0.1:{port1}/");
            Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            server.RestartIfPortChanged(port2);
            stopwatch.Stop();

            Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Port switch took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
            Assert.True(server.IsRunning);
            Assert.Equal(port2, server.CurrentPort);

            var response2 = await _client.GetAsync($"http://127.0.0.1:{port2}/");
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
                var server = new Oxide.Plugins.SentinelWebServer(listener, logger);

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
            var server = new Oxide.Plugins.SentinelWebServer(listener, logger);

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
