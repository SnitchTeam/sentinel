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
    public class SentinelWorldTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;
        private readonly List<TestPlayer> _localPlayers = new();

        public SentinelWorldTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_world_test_{Guid.NewGuid()}.db");
            _plugin = new TestableSentinel();
            _plugin.LocalPlayers = _localPlayers;
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
            _localPlayers.Add(p);
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

        private (double? time, string? weather) GetWorldStateFromDb()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT time_override, weather_override FROM sentinel_world_state WHERE id = 1;";
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                double? time = reader.IsDBNull(0) ? null : reader.GetDouble(0);
                string? weather = reader.IsDBNull(1) ? null : reader.GetString(1);
                return (time, weather);
            }
            return (null, null);
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
            public List<TestPlayer> LocalPlayers { get; set; } = new();
            public List<string> Logs { get; } = new();
            public double? LastSetTime { get; private set; }
            public string? LastSetWeather { get; private set; }
            public double SimulatedTime { get; set; } = 12.0;
            public string SimulatedWeather { get; set; } = "clear";

            public override void Puts(string message) => Logs.Add(message);
            public override void PrintWarning(string message) => Logs.Add($"[WARN] {message}");
            public override void PrintError(string message) => Logs.Add($"[ERROR] {message}");
            public void SetConfig(SentinelConfig config) => PluginConfig = config;

            protected override void ApplyTimeOfDay(double hour)
            {
                LastSetTime = hour;
                SimulatedTime = hour;
            }

            protected override double GetTimeOfDay() => SimulatedTime;

            protected override void ApplyWeather(string weather)
            {
                LastSetWeather = weather;
                SimulatedWeather = weather;
            }

            protected override string GetWeather() => SimulatedWeather;

            protected override BasePlayer? ResolveTargetInternal(string identifier)
            {
                if (string.IsNullOrWhiteSpace(identifier)) return null;
                foreach (var p in LocalPlayers)
                {
                    if (p.UserIDString == identifier) return p;
                }
                foreach (var p in LocalPlayers)
                {
                    if (p.displayName.Contains(identifier, StringComparison.OrdinalIgnoreCase)) return p;
                }
                return null;
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
        // VAL-ADMIN-029: Time of day can be set and advances normally
        // ---------------------------------------------------------
        [Fact]
        public void SetTime_WithPermission_Succeeds()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");

            var result = _plugin.ExecuteSetTime(admin, 14.5, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.Equal(14.5, _plugin.LastSetTime);
        }

        [Fact]
        public void SetTime_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");

            var result = _plugin.ExecuteSetTime(admin, 14.5, out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Null(_plugin.LastSetTime);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(25)]
        [InlineData(24.1)]
        public void SetTime_OutOfRange_Fails(double hour)
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");

            var result = _plugin.ExecuteSetTime(admin, hour, out var error);

            Assert.False(result);
            Assert.Equal("Hour must be between 0 and 24.", error);
        }

        [Fact]
        public void SetTime_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");

            _plugin.ExecuteSetTime(admin, 18.0, out _);
            var rows = GetAuditRows("world_time");

            Assert.Single(rows);
            Assert.Equal(admin.UserIDString, rows[0].ActorSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("18", rows[0].DetailsJson ?? "");
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-030: Weather can be set to clear, rain, or storm
        // ---------------------------------------------------------
        [Theory]
        [InlineData("clear")]
        [InlineData("rain")]
        [InlineData("storm")]
        [InlineData("CLEAR")]
        [InlineData("Rain")]
        public void SetWeather_ValidType_Succeeds(string weather)
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");

            var result = _plugin.ExecuteSetWeather(admin, weather, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.Equal(weather.ToLowerInvariant(), _plugin.LastSetWeather);
        }

        [Fact]
        public void SetWeather_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");

            var result = _plugin.ExecuteSetWeather(admin, "rain", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Null(_plugin.LastSetWeather);
        }

        [Theory]
        [InlineData("snow")]
        [InlineData("fog")]
        [InlineData("")]
        public void SetWeather_InvalidType_Fails(string weather)
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");

            var result = _plugin.ExecuteSetWeather(admin, weather, out var error);

            Assert.False(result);
            Assert.Equal("Weather must be clear, rain, or storm.", error);
        }

        [Fact]
        public void SetWeather_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");

            _plugin.ExecuteSetWeather(admin, "storm", out _);
            var rows = GetAuditRows("world_weather");

            Assert.Single(rows);
            Assert.Equal(admin.UserIDString, rows[0].ActorSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("storm", rows[0].DetailsJson ?? "");
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-031: Persistence
        // ---------------------------------------------------------
        [Fact]
        public void SetTime_PersistenceEnabled_SavesToDatabase()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");
            _plugin.SetConfig(new SentinelConfig { World = new WorldConfig { PersistOverrides = true } });

            _plugin.ExecuteSetTime(admin, 22.0, out _);
            var state = GetWorldStateFromDb();

            Assert.Equal(22.0, state.time);
        }

        [Fact]
        public void SetWeather_PersistenceEnabled_SavesToDatabase()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");
            _plugin.SetConfig(new SentinelConfig { World = new WorldConfig { PersistOverrides = true } });

            _plugin.ExecuteSetWeather(admin, "storm", out _);
            var state = GetWorldStateFromDb();

            Assert.Equal("storm", state.weather);
        }

        [Fact]
        public void SetTime_PersistenceDisabled_DoesNotSaveToDatabase()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");
            _plugin.SetConfig(new SentinelConfig { World = new WorldConfig { PersistOverrides = false } });

            _plugin.ExecuteSetTime(admin, 22.0, out _);
            var state = GetWorldStateFromDb();

            Assert.Null(state.time);
        }

        [Fact]
        public void SetWeather_PersistenceDisabled_DoesNotSaveToDatabase()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");
            _plugin.SetConfig(new SentinelConfig { World = new WorldConfig { PersistOverrides = false } });

            _plugin.ExecuteSetWeather(admin, "storm", out _);
            var state = GetWorldStateFromDb();

            Assert.Null(state.weather);
        }

        [Fact]
        public void RestoreWorldState_RestoresSavedOverrides()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");
            _plugin.SetConfig(new SentinelConfig { World = new WorldConfig { PersistOverrides = true } });

            _plugin.ExecuteSetTime(admin, 7.5, out _);
            _plugin.ExecuteSetWeather(admin, "rain", out _);

            // Create a fresh plugin instance pointing at the same DB
            var freshPlugin = new TestableSentinel();
            freshPlugin.SetConfig(new SentinelConfig { World = new WorldConfig { PersistOverrides = true } });
            freshPlugin.permission = _mockPermission;
            freshPlugin.InitializeDatabase(_dbPath);

            freshPlugin.RestoreWorldState();

            Assert.Equal(7.5, freshPlugin.LastSetTime);
            Assert.Equal("rain", freshPlugin.LastSetWeather);

            freshPlugin.CloseDatabase();
        }

        [Fact]
        public void RestoreWorldState_PersistenceDisabled_DoesNothing()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");
            _plugin.SetConfig(new SentinelConfig { World = new WorldConfig { PersistOverrides = true } });

            _plugin.ExecuteSetTime(admin, 3.0, out _);

            var freshPlugin = new TestableSentinel();
            freshPlugin.SetConfig(new SentinelConfig { World = new WorldConfig { PersistOverrides = false } });
            freshPlugin.permission = _mockPermission;
            freshPlugin.InitializeDatabase(_dbPath);

            freshPlugin.RestoreWorldState();

            Assert.Null(freshPlugin.LastSetTime);

            freshPlugin.CloseDatabase();
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-032: Permission matrix
        // ---------------------------------------------------------
        [Fact]
        public void PermissionMatrix_SetTime_WithPermission_Allowed()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");

            var result = _plugin.ExecuteSetTime(admin, 10.0, out _);
            Assert.True(result);
        }

        [Fact]
        public void PermissionMatrix_SetTime_WithoutPermission_Denied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");

            var result = _plugin.ExecuteSetTime(admin, 10.0, out _);
            Assert.False(result);
        }

        [Fact]
        public void PermissionMatrix_SetWeather_WithPermission_Allowed()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");

            var result = _plugin.ExecuteSetWeather(admin, "clear", out _);
            Assert.True(result);
        }

        [Fact]
        public void PermissionMatrix_SetWeather_WithoutPermission_Denied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");

            var result = _plugin.ExecuteSetWeather(admin, "clear", out _);
            Assert.False(result);
        }

        [Fact]
        public void WildcardPermission_GrantsWorldCommands()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.*");

            Assert.True(_plugin.ExecuteSetTime(admin, 15.0, out _));
            Assert.True(_plugin.ExecuteSetWeather(admin, "storm", out _));
        }

        [Fact]
        public void ConsoleHasPermission_ByDefault()
        {
            var result = _plugin.ExecuteSetTime(null, 8.0, out _);
            Assert.True(result);
        }
    }
}
