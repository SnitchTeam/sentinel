using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelConfigTests : IDisposable
    {
        private readonly string _configPath;
        private readonly string _configDir;

        public SentinelConfigTests()
        {
            _configDir = Path.Combine(Path.GetTempPath(), $"sentinel_config_test_{Guid.NewGuid()}");
            _configPath = Path.Combine(_configDir, "Sentinel.json");
            Directory.CreateDirectory(_configDir);
        }

        public void Dispose()
        {
            try { File.Delete(_configPath); } catch { }
            try { Directory.Delete(_configDir, true); } catch { }
        }

        private SentinelPlugin CreatePlugin()
        {
            var plugin = new TestableSentinel(_configPath);
            plugin.Config = new Oxide.Core.Plugins.DynamicConfigFile(_configPath);
            return plugin;
        }

        [Fact]
        public void Config_GeneratesFile_OnFirstLoad()
        {
            Assert.False(File.Exists(_configPath));

            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            Assert.True(File.Exists(_configPath));
        }

        [Fact]
        public void Config_RegeneratesFile_AfterDeletion()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();
            Assert.True(File.Exists(_configPath));

            File.Delete(_configPath);
            Assert.False(File.Exists(_configPath));

            plugin.LoadPluginConfig();
            Assert.True(File.Exists(_configPath));
        }

        [Fact]
        public void Config_ContainsRequiredTopLevelKeys()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            var json = File.ReadAllText(_configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("database", out _));
            Assert.True(root.TryGetProperty("logging", out _));
            Assert.True(root.TryGetProperty("ai", out _));
            Assert.True(root.TryGetProperty("bans", out _));
            Assert.True(root.TryGetProperty("groups", out _));
            Assert.True(root.TryGetProperty("world", out _));
            Assert.True(root.TryGetProperty("webPanel", out _));
        }

        [Fact]
        public void Config_Database_IsNotNull()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            var config = plugin.Config!.ReadObject<Oxide.Plugins.SentinelConfig>();
            Assert.NotNull(config);
            Assert.NotNull(config.Database);
            Assert.NotNull(config.Database.Path);
            Assert.True(config.Database.BusyTimeoutMs > 0);
        }

        [Fact]
        public void Config_Logging_IsNotNull()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            var config = plugin.Config!.ReadObject<Oxide.Plugins.SentinelConfig>();
            Assert.NotNull(config);
            Assert.NotNull(config.Logging);
            Assert.NotNull(config.Logging.Level);
            Assert.True(config.Logging.MaxLogFiles > 0);
        }

        [Fact]
        public void Config_AI_IsNotNull()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            var config = plugin.Config!.ReadObject<Oxide.Plugins.SentinelConfig>();
            Assert.NotNull(config);
            Assert.NotNull(config.AI);
            Assert.NotNull(config.AI.Provider);
            Assert.NotNull(config.AI.Model);
            Assert.NotNull(config.AI.Endpoint);
            Assert.True(config.AI.MaxRetries >= 0);
            Assert.True(config.AI.TimeoutSeconds > 0);
        }

        [Fact]
        public void Config_Bans_IsNotNull()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            var config = plugin.Config!.ReadObject<Oxide.Plugins.SentinelConfig>();
            Assert.NotNull(config);
            Assert.NotNull(config.Bans);
            Assert.True(config.Bans.DefaultDurationMinutes >= 0);
            Assert.NotNull(config.Bans.AppealUrl);
        }

        [Fact]
        public void Config_Groups_IsNotNull()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            var config = plugin.Config!.ReadObject<Oxide.Plugins.SentinelConfig>();
            Assert.NotNull(config);
            Assert.NotNull(config.Groups);
            Assert.NotNull(config.Groups.DefaultGroups);
            Assert.NotEmpty(config.Groups.DefaultGroups);
        }

        [Fact]
        public void Config_DefaultGroups_ContainsExpectedGroups()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            var config = plugin.Config!.ReadObject<Oxide.Plugins.SentinelConfig>();
            Assert.NotNull(config);
            Assert.Contains("sentinel_admin", config.Groups.DefaultGroups.Keys);
            Assert.Contains("sentinel_moderator", config.Groups.DefaultGroups.Keys);
            Assert.Contains("sentinel_trial_mod", config.Groups.DefaultGroups.Keys);
        }

        [Fact]
        public void Config_DefaultGroups_HaveTitlesAndPermissions()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            var config = plugin.Config!.ReadObject<Oxide.Plugins.SentinelConfig>();
            Assert.NotNull(config);

            var admin = config.Groups.DefaultGroups["sentinel_admin"];
            Assert.False(string.IsNullOrEmpty(admin.Title));
            Assert.NotEmpty(admin.Permissions);

            var moderator = config.Groups.DefaultGroups["sentinel_moderator"];
            Assert.False(string.IsNullOrEmpty(moderator.Title));
            Assert.NotEmpty(moderator.Permissions);

            var trial = config.Groups.DefaultGroups["sentinel_trial_mod"];
            Assert.False(string.IsNullOrEmpty(trial.Title));
            Assert.NotEmpty(trial.Permissions);
        }

        [Fact]
        public void Config_NoRequiredFieldIsNull()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            var json = File.ReadAllText(_configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var requiredKeys = new[] { "database", "logging", "ai", "bans", "groups", "world", "webPanel" };
            foreach (var key in requiredKeys)
            {
                Assert.True(root.TryGetProperty(key, out var prop), $"Missing key: {key}");
                Assert.NotEqual(JsonValueKind.Null, prop.ValueKind);
                Assert.NotEqual(JsonValueKind.Undefined, prop.ValueKind);
            }
        }

        [Fact]
        public void Config_LoadPluginConfig_PopulatesPluginConfig()
        {
            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            Assert.NotNull(plugin.PluginConfig);
            Assert.NotNull(plugin.PluginConfig.Database);
            Assert.NotNull(plugin.PluginConfig.Logging);
            Assert.NotNull(plugin.PluginConfig.AI);
            Assert.NotNull(plugin.PluginConfig.Bans);
            Assert.NotNull(plugin.PluginConfig.Groups);
            Assert.NotNull(plugin.PluginConfig.World);
            Assert.NotNull(plugin.PluginConfig.WebPanel);
        }

        [Fact]
        public void Config_ReadsExistingFile()
        {
            var defaultConfig = new Oxide.Plugins.SentinelConfig
            {
                Database = new Oxide.Plugins.DatabaseConfig { Path = "custom/db.db", BusyTimeoutMs = 10000 },
                Logging = new Oxide.Plugins.LoggingConfig { Level = "Debug", LogToConsole = false, LogToFile = true, MaxLogFiles = 14 },
                AI = new Oxide.Plugins.AIConfig { Provider = "anthropic", Model = "claude-3-haiku", Endpoint = "https://api.anthropic.com/v1", ApiKey = "test-key", DailyUsdCap = 10.0, MaxRetries = 5, TimeoutSeconds = 20 },
                Bans = new Oxide.Plugins.BansConfig { DefaultDurationMinutes = 2880, AllowAppeals = false, AppealUrl = "https://example.com", BroadcastBans = false },
                Groups = new Oxide.Plugins.GroupsConfig
                {
                    DefaultGroups = new Dictionary<string, Oxide.Plugins.GroupDefinition>
                    {
                        ["custom_group"] = new Oxide.Plugins.GroupDefinition { Title = "Custom", Permissions = new List<string> { "sentinel.kick" } }
                    }
                }
            };

            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            File.WriteAllText(_configPath, JsonSerializer.Serialize(defaultConfig, options));

            var plugin = CreatePlugin();
            plugin.LoadPluginConfig();

            Assert.Equal("custom/db.db", plugin.PluginConfig!.Database.Path);
            Assert.Equal(10000, plugin.PluginConfig.Database.BusyTimeoutMs);
            Assert.Equal("Debug", plugin.PluginConfig.Logging.Level);
            Assert.False(plugin.PluginConfig.Logging.LogToConsole);
            Assert.Equal("anthropic", plugin.PluginConfig.AI.Provider);
            Assert.Equal("claude-3-haiku", plugin.PluginConfig.AI.Model);
            Assert.Equal(2880, plugin.PluginConfig.Bans.DefaultDurationMinutes);
            Assert.False(plugin.PluginConfig.Bans.AllowAppeals);
            Assert.Contains("custom_group", plugin.PluginConfig.Groups.DefaultGroups.Keys);
        }

        private class TestableSentinel : SentinelPlugin
        {
            private readonly string _configPath;

            public TestableSentinel(string configPath)
            {
                _configPath = configPath;
            }

            public override string GetConfigPath() => _configPath;
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }
        }
    }
}
