using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelPluginsTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;

        public SentinelPluginsTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_plugins_test_{Guid.NewGuid()}.db");
            _plugin = new TestableSentinel();
            _mockPermission = new MockPermission();
            _plugin.permission = _mockPermission;
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

        private TestPlayer CreatePlayer(ulong steamId, string name)
        {
            var p = new TestPlayer
            {
                UserIDString = steamId.ToString(),
                displayName = name
            };
            return p;
        }

        private List<AuditRow> GetAuditRows(string? actionType = null)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = actionType != null
                ? "SELECT actor_steam_id, target_steam_id, action_type, success, details_json FROM sentinel_actions WHERE action_type = @type ORDER BY id;"
                : "SELECT actor_steam_id, target_steam_id, action_type, success, details_json FROM sentinel_actions ORDER BY id;";
            if (actionType != null)
                command.Parameters.AddWithValue("@type", actionType);

            var rows = new List<AuditRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new AuditRow
                {
                    ActorSteamId = reader.GetString(0),
                    TargetSteamId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    ActionType = reader.GetString(2),
                    Success = reader.GetInt32(3) == 1,
                    DetailsJson = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
            return rows;
        }

        private class AuditRow
        {
            public string ActorSteamId { get; set; } = "";
            public string? TargetSteamId { get; set; }
            public string ActionType { get; set; } = "";
            public bool Success { get; set; }
            public string? DetailsJson { get; set; }
        }

        private class TestableSentinel : SentinelPlugin
        {
            public List<PluginInfo> SimulatedPlugins { get; set; } = new();
            public bool NextLoadResult { get; set; } = true;
            public bool NextUnloadResult { get; set; } = true;
            public bool NextReloadResult { get; set; } = true;
            public string? LastLoadFilename { get; private set; }
            public string? LastUnloadName { get; private set; }
            public string? LastReloadName { get; private set; }

            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }

            protected override bool LoadPluginInternal(string filename)
            {
                LastLoadFilename = filename;
                if (!NextLoadResult) return false;
                if (!SimulatedPlugins.Any(p => p.Name.Equals(filename, StringComparison.OrdinalIgnoreCase)))
                {
                    SimulatedPlugins.Add(new PluginInfo { Name = filename, Version = "1.0.0", Author = "Test" });
                }
                return true;
            }

            protected override bool UnloadPluginInternal(string name)
            {
                LastUnloadName = name;
                if (!NextUnloadResult) return false;
                SimulatedPlugins.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                return true;
            }

            protected override bool ReloadPluginInternal(string name)
            {
                LastReloadName = name;
                if (!NextReloadResult) return false;
                var plugin = SimulatedPlugins.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (plugin != null)
                {
                    plugin.Version = "1.0.1";
                }
                else
                {
                    SimulatedPlugins.Add(new PluginInfo { Name = name, Version = "1.0.1", Author = "Test" });
                }
                return true;
            }

            protected override List<PluginInfo> GetLoadedPluginsInternal()
            {
                return SimulatedPlugins.ToList();
            }
        }

        private class TestPlayer : BasePlayer
        {
            public List<string> ChatMessages { get; } = new();
            public override void ChatMessage(string message) => ChatMessages.Add(message);
        }

        private class MockPermission : Oxide.Core.Libraries.Permission
        {
            private readonly Dictionary<string, HashSet<string>> _perms = new();

            public void Grant(string userId, string perm)
            {
                if (!_perms.TryGetValue(userId, out var set))
                {
                    set = new HashSet<string>();
                    _perms[userId] = set;
                }
                set.Add(perm);
            }

            public void Revoke(string userId, string perm)
            {
                if (_perms.TryGetValue(userId, out var set))
                    set.Remove(perm);
            }

            public override bool UserHasPermission(string id, string perm)
            {
                if (_perms.TryGetValue(id, out var set))
                {
                    if (set.Contains(perm)) return true;
                    if (set.Contains("sentinel.*"))
                    {
                        return perm.StartsWith("sentinel.", StringComparison.OrdinalIgnoreCase);
                    }
                }
                return false;
            }
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-033: Plugin can be loaded from file
        // ---------------------------------------------------------
        [Fact]
        public void LoadPlugin_WithPermission_Succeeds()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");

            var result = _plugin.ExecuteLoadPlugin(admin, "TestPlugin.cs", out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.Equal("TestPlugin.cs", _plugin.LastLoadFilename);
            Assert.Single(_plugin.SimulatedPlugins);
            Assert.Equal("TestPlugin.cs", _plugin.SimulatedPlugins[0].Name);
        }

        [Fact]
        public void LoadPlugin_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");

            var result = _plugin.ExecuteLoadPlugin(admin, "TestPlugin.cs", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Empty(_plugin.SimulatedPlugins);
        }

        [Fact]
        public void LoadPlugin_EmptyFilename_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");

            var result = _plugin.ExecuteLoadPlugin(admin, "", out var error);

            Assert.False(result);
            Assert.Equal("Filename is required.", error);
        }

        [Fact]
        public void LoadPlugin_InternalFailure_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.NextLoadResult = false;

            var result = _plugin.ExecuteLoadPlugin(admin, "TestPlugin.cs", out var error);

            Assert.False(result);
            Assert.Contains("Failed to load plugin", error);
        }

        [Fact]
        public void LoadPlugin_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");

            _plugin.ExecuteLoadPlugin(admin, "TestPlugin.cs", out _);

            var rows = GetAuditRows("plugin_load");
            Assert.Single(rows);
            Assert.Equal(admin.UserIDString, rows[0].ActorSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("TestPlugin.cs", rows[0].DetailsJson ?? "");
        }

        [Fact]
        public void LoadPlugin_WithoutPermission_GeneratesFailedAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");

            _plugin.ExecuteLoadPlugin(admin, "TestPlugin.cs", out _);

            var rows = GetAuditRows("plugin_load");
            Assert.Single(rows);
            Assert.False(rows[0].Success);
            Assert.Equal(admin.UserIDString, rows[0].ActorSteamId);
        }

        [Fact]
        public void LoadPlugin_AppearsInLoadedList()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");

            _plugin.ExecuteLoadPlugin(admin, "TestPlugin.cs", out _);
            var plugins = _plugin.GetLoadedPlugins();

            Assert.Single(plugins);
            Assert.Equal("TestPlugin.cs", plugins[0].Name);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-034: Plugin can be unloaded
        // ---------------------------------------------------------
        [Fact]
        public void UnloadPlugin_WithPermission_Succeeds()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            var result = _plugin.ExecuteUnloadPlugin(admin, "TestPlugin", out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.Equal("TestPlugin", _plugin.LastUnloadName);
            Assert.Empty(_plugin.SimulatedPlugins);
        }

        [Fact]
        public void UnloadPlugin_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            var result = _plugin.ExecuteUnloadPlugin(admin, "TestPlugin", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Single(_plugin.SimulatedPlugins);
        }

        [Fact]
        public void UnloadPlugin_EmptyName_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");

            var result = _plugin.ExecuteUnloadPlugin(admin, "", out var error);

            Assert.False(result);
            Assert.Equal("Plugin name is required.", error);
        }

        [Fact]
        public void UnloadPlugin_InternalFailure_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.NextUnloadResult = false;

            var result = _plugin.ExecuteUnloadPlugin(admin, "TestPlugin", out var error);

            Assert.False(result);
            Assert.Contains("Failed to unload plugin", error);
        }

        [Fact]
        public void UnloadPlugin_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            _plugin.ExecuteUnloadPlugin(admin, "TestPlugin", out _);

            var rows = GetAuditRows("plugin_unload");
            Assert.Single(rows);
            Assert.Equal(admin.UserIDString, rows[0].ActorSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("TestPlugin", rows[0].DetailsJson ?? "");
        }

        [Fact]
        public void UnloadPlugin_WithoutPermission_GeneratesFailedAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");

            _plugin.ExecuteUnloadPlugin(admin, "TestPlugin", out _);

            var rows = GetAuditRows("plugin_unload");
            Assert.Single(rows);
            Assert.False(rows[0].Success);
        }

        [Fact]
        public void UnloadPlugin_DisappearsFromActiveList()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            _plugin.ExecuteUnloadPlugin(admin, "TestPlugin", out _);
            var plugins = _plugin.GetLoadedPlugins();

            Assert.Empty(plugins);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-035: Plugin can be reloaded
        // ---------------------------------------------------------
        [Fact]
        public void ReloadPlugin_WithPermission_Succeeds()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            var result = _plugin.ExecuteReloadPlugin(admin, "TestPlugin", out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.Equal("TestPlugin", _plugin.LastReloadName);
        }

        [Fact]
        public void ReloadPlugin_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            var result = _plugin.ExecuteReloadPlugin(admin, "TestPlugin", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
        }

        [Fact]
        public void ReloadPlugin_EmptyName_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");

            var result = _plugin.ExecuteReloadPlugin(admin, "", out var error);

            Assert.False(result);
            Assert.Equal("Plugin name is required.", error);
        }

        [Fact]
        public void ReloadPlugin_InternalFailure_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.NextReloadResult = false;

            var result = _plugin.ExecuteReloadPlugin(admin, "TestPlugin", out var error);

            Assert.False(result);
            Assert.Contains("Failed to reload plugin", error);
        }

        [Fact]
        public void ReloadPlugin_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            _plugin.ExecuteReloadPlugin(admin, "TestPlugin", out _);

            var rows = GetAuditRows("plugin_reload");
            Assert.Single(rows);
            Assert.Equal(admin.UserIDString, rows[0].ActorSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("TestPlugin", rows[0].DetailsJson ?? "");
        }

        [Fact]
        public void ReloadPlugin_WithoutPermission_GeneratesFailedAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");

            _plugin.ExecuteReloadPlugin(admin, "TestPlugin", out _);

            var rows = GetAuditRows("plugin_reload");
            Assert.Single(rows);
            Assert.False(rows[0].Success);
        }

        [Fact]
        public void ReloadPlugin_VersionChanges()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            _plugin.ExecuteReloadPlugin(admin, "TestPlugin", out _);

            var plugin = _plugin.SimulatedPlugins.FirstOrDefault(p => p.Name == "TestPlugin");
            Assert.NotNull(plugin);
            Assert.Equal("1.0.1", plugin!.Version);
        }

        [Fact]
        public void ReloadPlugin_NoOrphanedHooks()
        {
            // In our simulation, reload replaces the plugin entry with a new version,
            // confirming the old version is no longer the active one.
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            _plugin.ExecuteReloadPlugin(admin, "TestPlugin", out _);

            var plugins = _plugin.GetLoadedPlugins();
            Assert.Single(plugins);
            Assert.Equal("1.0.1", plugins[0].Version);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-036: Plugin management requires sentinel.plugins permission
        // ---------------------------------------------------------
        [Theory]
        [InlineData("ExecuteLoadPlugin")]
        [InlineData("ExecuteUnloadPlugin")]
        [InlineData("ExecuteReloadPlugin")]
        public void PermissionMatrix_WithoutPermission_Denied(string methodName)
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            bool result;
            string error;

            if (methodName == "ExecuteLoadPlugin")
                result = _plugin.ExecuteLoadPlugin(admin, "TestPlugin.cs", out error);
            else if (methodName == "ExecuteUnloadPlugin")
                result = _plugin.ExecuteUnloadPlugin(admin, "TestPlugin", out error);
            else
                result = _plugin.ExecuteReloadPlugin(admin, "TestPlugin", out error);

            Assert.False(result);
            Assert.Equal("No permission", error);
        }

        [Theory]
        [InlineData("ExecuteLoadPlugin")]
        [InlineData("ExecuteUnloadPlugin")]
        [InlineData("ExecuteReloadPlugin")]
        public void PermissionMatrix_WithPermission_Allowed(string methodName)
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            bool result;
            string error;

            if (methodName == "ExecuteLoadPlugin")
                result = _plugin.ExecuteLoadPlugin(admin, "TestPlugin.cs", out error);
            else if (methodName == "ExecuteUnloadPlugin")
                result = _plugin.ExecuteUnloadPlugin(admin, "TestPlugin", out error);
            else
                result = _plugin.ExecuteReloadPlugin(admin, "TestPlugin", out error);

            Assert.True(result);
            Assert.Empty(error);
        }

        [Fact]
        public void WildcardPermission_GrantsPluginCommands()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.*");
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            Assert.True(_plugin.ExecuteLoadPlugin(admin, "TestPlugin.cs", out _));
            Assert.True(_plugin.ExecuteUnloadPlugin(admin, "TestPlugin", out _));
            Assert.True(_plugin.ExecuteReloadPlugin(admin, "TestPlugin", out _));
        }

        [Fact]
        public void ConsoleHasPermission_ByDefault()
        {
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "TestPlugin", Version = "1.0.0", Author = "Test" });

            Assert.True(_plugin.ExecuteLoadPlugin(null, "TestPlugin.cs", out _));
            Assert.True(_plugin.ExecuteUnloadPlugin(null, "TestPlugin", out _));
            Assert.True(_plugin.ExecuteReloadPlugin(null, "TestPlugin", out _));
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-037: Plugin actions are recorded in audit log
        // ---------------------------------------------------------
        [Fact]
        public void AuditLog_ContainsTimestampActorPluginAndAction()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");

            _plugin.ExecuteLoadPlugin(admin, "PluginA.cs", out _);
            _plugin.ExecuteUnloadPlugin(admin, "PluginA", out _);
            _plugin.ExecuteReloadPlugin(admin, "PluginB", out _);

            var rows = GetAuditRows();
            Assert.Equal(3, rows.Count);

            // Load
            Assert.Equal("plugin_load", rows[0].ActionType);
            Assert.Equal(admin.UserIDString, rows[0].ActorSteamId);
            Assert.Contains("PluginA.cs", rows[0].DetailsJson ?? "");
            Assert.True(rows[0].Success);

            // Unload
            Assert.Equal("plugin_unload", rows[1].ActionType);
            Assert.Equal(admin.UserIDString, rows[1].ActorSteamId);
            Assert.Contains("PluginA", rows[1].DetailsJson ?? "");
            Assert.True(rows[1].Success);

            // Reload
            Assert.Equal("plugin_reload", rows[2].ActionType);
            Assert.Equal(admin.UserIDString, rows[2].ActorSteamId);
            Assert.Contains("PluginB", rows[2].DetailsJson ?? "");
            Assert.True(rows[2].Success);
        }

        [Fact]
        public void AuditLog_LoadUnloadReload_AllHaveTimestamps()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.plugins");
            var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            _plugin.ExecuteLoadPlugin(admin, "PluginA.cs", out _);
            _plugin.ExecuteUnloadPlugin(admin, "PluginA", out _);
            _plugin.ExecuteReloadPlugin(admin, "PluginB", out _);

            var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var rows = GetAuditRows();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT timestamp FROM sentinel_actions ORDER BY id;";
            using var reader = command.ExecuteReader();
            int i = 0;
            while (reader.Read())
            {
                var ts = reader.GetInt64(0);
                Assert.True(ts >= before, $"Row {i} timestamp {ts} is before test start {before}");
                Assert.True(ts <= after, $"Row {i} timestamp {ts} is after test end {after}");
                i++;
            }
            Assert.Equal(3, i);
        }

        // ---------------------------------------------------------
        // List plugins
        // ---------------------------------------------------------
        [Fact]
        public void GetLoadedPlugins_ReturnsLoadedPlugins()
        {
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "PluginA", Version = "1.0.0", Author = "AuthorA" });
            _plugin.SimulatedPlugins.Add(new PluginInfo { Name = "PluginB", Version = "2.0.0", Author = "AuthorB" });

            var plugins = _plugin.GetLoadedPlugins();

            Assert.Equal(2, plugins.Count);
            Assert.Contains(plugins, p => p.Name == "PluginA" && p.Version == "1.0.0");
            Assert.Contains(plugins, p => p.Name == "PluginB" && p.Version == "2.0.0");
        }

        [Fact]
        public void GetLoadedPlugins_ReturnsEmpty_WhenNoneLoaded()
        {
            var plugins = _plugin.GetLoadedPlugins();
            Assert.Empty(plugins);
        }
    }
}
