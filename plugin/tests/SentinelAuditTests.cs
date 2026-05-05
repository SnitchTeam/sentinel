using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Oxide.Core;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelAuditTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;
        private readonly List<TestPlayer> _localPlayers = new();

        public SentinelAuditTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_audit_test_{Guid.NewGuid()}.db");
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

        private TestPlayer CreatePlayer(ulong steamId, string name, float x = 0, float y = 0, float z = 0)
        {
            var p = new TestPlayer
            {
                UserIDString = steamId.ToString(),
                displayName = name,
                Position = new Vector3(x, y, z),
                Rotation = new Vector3(0, 0, 0)
            };
            _localPlayers.Add(p);
            return p;
        }

        private SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            return connection;
        }

        private List<AuditRow> GetAuditRows(string? actionType = null)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = actionType != null
                ? "SELECT actor_steam_id, target_steam_id, action_type, reason, duration_minutes, success, details_json, timestamp FROM sentinel_actions WHERE action_type = @type ORDER BY id;"
                : "SELECT actor_steam_id, target_steam_id, action_type, reason, duration_minutes, success, details_json, timestamp FROM sentinel_actions ORDER BY id;";
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
                    Reason = reader.IsDBNull(3) ? null : reader.GetString(3),
                    DurationMinutes = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Success = reader.GetInt32(5) == 1,
                    DetailsJson = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Timestamp = reader.GetInt64(7)
                });
            }
            return rows;
        }

        private class AuditRow
        {
            public string ActorSteamId { get; set; } = "";
            public string? TargetSteamId { get; set; }
            public string ActionType { get; set; } = "";
            public string? Reason { get; set; }
            public int? DurationMinutes { get; set; }
            public bool Success { get; set; }
            public string? DetailsJson { get; set; }
            public long Timestamp { get; set; }
        }

        private class TestableSentinel : SentinelPlugin
        {
            public List<TestPlayer> LocalPlayers { get; set; } = new();
            public List<ItemDefinition> TestItemDefinitions { get; set; } = new();
            public List<string> Logs { get; } = new();
            public override void Puts(string message) => Logs.Add(message);
            public override void PrintWarning(string message) => Logs.Add($"[WARN] {message}");
            public override void PrintError(string message) => Logs.Add($"[ERROR] {message}");

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

            protected override List<ItemDefinition> GetAllItemDefinitions()
            {
                return TestItemDefinitions;
            }

            protected override Item? CreateItemByName(string shortname, int amount)
            {
                var def = TestItemDefinitions.FirstOrDefault(d => d.shortname.Equals(shortname, StringComparison.OrdinalIgnoreCase));
                if (def == null) return null;
                return new Item { info = def, amount = amount };
            }
        }

        private class TestPlayer : BasePlayer
        {
            public bool WasKicked { get; private set; }
            public string? LastKickReason { get; private set; }
            public List<string> ChatMessages { get; } = new();

            public override void Kick(string reason)
            {
                WasKicked = true;
                LastKickReason = reason;
            }

            public override void ChatMessage(string message)
            {
                ChatMessages.Add(message);
            }
        }

        private class MockPermission : Oxide.Core.Libraries.Permission
        {
            private readonly Dictionary<string, HashSet<string>> _perms = new();
            private readonly Dictionary<string, HashSet<string>> _groupPerms = new();
            private readonly Dictionary<string, HashSet<string>> _userGroups = new();
            private readonly Dictionary<string, GroupInfo> _groups = new();

            public class GroupInfo
            {
                public string Title { get; set; } = "";
                public int Rank { get; set; }
                public string? Parent { get; set; }
            }

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

            public override bool CreateGroup(string group, string title, int rank)
            {
                _groups[group] = new GroupInfo { Title = title, Rank = rank };
                if (!_groupPerms.ContainsKey(group))
                    _groupPerms[group] = new HashSet<string>();
                return true;
            }

            public override bool RemoveGroup(string group)
            {
                _groups.Remove(group);
                _groupPerms.Remove(group);
                return true;
            }

            public override bool GroupExists(string group)
            {
                return _groups.ContainsKey(group);
            }

            public override string[] GetGroups()
            {
                return _groups.Keys.ToArray();
            }

            public override string[] GetGroupPermissions(string group)
            {
                return _groupPerms.TryGetValue(group, out var perms) ? perms.ToArray() : Array.Empty<string>();
            }

            public override void GrantGroupPermission(string group, string perm, Oxide.Plugins.RustPlugin owner)
            {
                if (!_groupPerms.TryGetValue(group, out var set))
                {
                    set = new HashSet<string>();
                    _groupPerms[group] = set;
                }
                set.Add(perm);
            }

            public override void RevokeGroupPermission(string group, string perm)
            {
                if (_groupPerms.TryGetValue(group, out var set))
                    set.Remove(perm);
            }

            public override void AddUserGroup(string id, string group)
            {
                if (!_userGroups.TryGetValue(id, out var set))
                {
                    set = new HashSet<string>();
                    _userGroups[id] = set;
                }
                set.Add(group);
            }

            public override void RemoveUserGroup(string id, string group)
            {
                if (_userGroups.TryGetValue(id, out var set))
                    set.Remove(group);
            }

            public override string[] GetUsersInGroup(string group)
            {
                var users = new List<string>();
                foreach (var kvp in _userGroups)
                {
                    if (kvp.Value.Contains(group))
                        users.Add(kvp.Key);
                }
                return users.ToArray();
            }

            public override string[] GetUserGroups(string id)
            {
                return _userGroups.TryGetValue(id, out var groups) ? groups.ToArray() : Array.Empty<string>();
            }

            public override bool SetGroupTitle(string group, string title)
            {
                if (!_groups.TryGetValue(group, out var info)) return false;
                info.Title = title;
                return true;
            }

            public override bool SetGroupParent(string group, string parent)
            {
                if (!_groups.TryGetValue(group, out var info)) return false;
                info.Parent = string.IsNullOrEmpty(parent) ? null : parent;
                return true;
            }
        }

        // Helper to invoke private console command handlers
        private void InvokeConsoleCommand(string methodName, ConsoleSystem.Arg arg)
        {
            var method = typeof(SentinelPlugin).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(_plugin, new object[] { arg });
        }

        private ConsoleSystem.Arg BuildArg(string[]? args, BasePlayer? player = null)
        {
            var arg = new ConsoleSystem.Arg();
            typeof(ConsoleSystem.Arg).GetProperty("Args")?.SetValue(arg, args);
            return arg;
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-041: Every moderation action generates an audit row
        // ---------------------------------------------------------
        [Fact]
        public void Moderation_Kick_AuditRow_HasDurationAndReason()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.kick");

            _plugin.ExecuteKick(admin, "Target", "Testing kick", out _);

            var rows = GetAuditRows("kick");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.Equal("Testing kick", rows[0].Reason);
            Assert.True(rows[0].Success);
        }

        [Fact]
        public void Moderation_Ban_AuditRow_HasDurationAndReason()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            _plugin.ExecuteBan(admin, "Target", "Cheating", 60, out _);

            var rows = GetAuditRows("ban");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.Equal("Cheating", rows[0].Reason);
            Assert.Equal(60, rows[0].DurationMinutes);
            Assert.True(rows[0].Success);
        }

        [Fact]
        public void Moderation_Ban_Permanent_AuditRow_HasNullDuration()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            _plugin.ExecuteBan(admin, "Target", "Cheating", null, out _);

            var rows = GetAuditRows("ban");
            Assert.Single(rows);
            Assert.Null(rows[0].DurationMinutes);
        }

        [Fact]
        public void Moderation_Warn_AuditRow_HasReason()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.warn");

            _plugin.ExecuteWarn(admin, "Target", "First warning", out _);

            var rows = GetAuditRows("warn");
            Assert.Single(rows);
            Assert.Equal("First warning", rows[0].Reason);
            Assert.True(rows[0].Success);
        }

        [Fact]
        public void Moderation_Mute_AuditRow_HasReasonAndDuration()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.mute");

            _plugin.ExecuteMute(admin, "Target", "chat", 30, out _);

            var rows = GetAuditRows("mute");
            Assert.Single(rows);
            Assert.Equal("chat", rows[0].Reason);
            Assert.Equal(30, rows[0].DurationMinutes);
        }

        [Fact]
        public void Moderation_Freeze_AuditRow_HasReason()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.freeze");

            _plugin.ExecuteFreeze(admin, "Target", out _);

            var rows = GetAuditRows("freeze");
            Assert.Single(rows);
            Assert.Equal("frozen", rows[0].Reason);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-042: Teleport actions generate audit rows with coordinates
        // ---------------------------------------------------------
        [Fact]
        public void Teleport_TPto_AuditRow_HasCoordinates()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            var target = CreatePlayer(76561190000000002, "Target", 100, 50, 100);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            _plugin.ExecuteTeleportTo(admin, "Target", out _);

            var rows = GetAuditRows("teleport");
            Assert.Single(rows);
            Assert.Contains("tpto", rows[0].DetailsJson);
            Assert.Contains("fromX", rows[0].DetailsJson);
            Assert.Contains("toX", rows[0].DetailsJson);
        }

        [Fact]
        public void Teleport_TPme_AuditRow_HasCoordinates()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 200, 75, 200);
            var target = CreatePlayer(76561190000000002, "Target", 0, 0, 0);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            _plugin.ExecuteTeleportMe(admin, "Target", out _);

            var rows = GetAuditRows("teleport");
            Assert.Single(rows);
            Assert.Contains("tpme", rows[0].DetailsJson);
            Assert.Contains("fromX", rows[0].DetailsJson);
            Assert.Contains("toX", rows[0].DetailsJson);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-043: Permission group changes generate audit rows with old/new values
        // ---------------------------------------------------------
        [Fact]
        public void Group_Create_AuditRow_HasDetails()
        {
            var arg = BuildArg(new[] { "create", "test_group", "Test Group" });
            InvokeConsoleCommand("CCmdGroup", arg);

            var rows = GetAuditRows("group_create");
            Assert.Single(rows);
            Assert.Equal("console", rows[0].ActorSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("test_group", rows[0].DetailsJson);
            Assert.Contains("Test Group", rows[0].DetailsJson);
        }

        [Fact]
        public void Group_Delete_AuditRow_HasDetails()
        {
            _plugin.CreateGroup("del_audit", "Del Audit", null, out _);

            var arg = BuildArg(new[] { "delete", "del_audit" });
            InvokeConsoleCommand("CCmdGroup", arg);

            var rows = GetAuditRows("group_delete");
            Assert.Single(rows);
            Assert.Equal("console", rows[0].ActorSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("del_audit", rows[0].DetailsJson);
        }

        [Fact]
        public void Group_UpdateTitle_AuditRow_HasOldAndNewValues()
        {
            _plugin.CreateGroup("upd_audit", "Old Title", null, out _);

            var arg = BuildArg(new[] { "update", "upd_audit", "title", "New Title" });
            InvokeConsoleCommand("CCmdGroup", arg);

            var rows = GetAuditRows("group_update");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
            Assert.Contains("Old Title", rows[0].DetailsJson);
            Assert.Contains("New Title", rows[0].DetailsJson);
        }

        [Fact]
        public void Group_UpdateParent_AuditRow_HasOldAndNewValues()
        {
            _plugin.CreateGroup("parent_audit", "Parent", null, out _);
            _plugin.CreateGroup("child_audit", "Child", null, out _);

            var arg = BuildArg(new[] { "update", "child_audit", "parent", "parent_audit" });
            InvokeConsoleCommand("CCmdGroup", arg);

            var rows = GetAuditRows("group_update");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
            Assert.Contains("\"old\":\"null\"", rows[0].DetailsJson);
            Assert.Contains("parent_audit", rows[0].DetailsJson);
        }

        [Fact]
        public void Group_AddUser_AuditRow_HasTarget()
        {
            var target = CreatePlayer(76561190000000002, "Target");
            _plugin.CreateGroup("add_user_audit", "Add User", null, out _);

            var arg = BuildArg(new[] { "add", "add_user_audit", "Target" });
            InvokeConsoleCommand("CCmdGroup", arg);

            var rows = GetAuditRows("group_add_user");
            Assert.Single(rows);
            Assert.Equal("console", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.True(rows[0].Success);
        }

        [Fact]
        public void Group_RemoveUser_AuditRow_HasTarget()
        {
            var target = CreatePlayer(76561190000000002, "Target");
            _plugin.CreateGroup("rem_user_audit", "Rem User", null, out _);
            _plugin.AddUserToGroup("rem_user_audit", "Target", out _);

            var arg = BuildArg(new[] { "remove", "rem_user_audit", "Target" });
            InvokeConsoleCommand("CCmdGroup", arg);

            var rows = GetAuditRows("group_remove_user");
            Assert.Single(rows);
            Assert.Equal("console", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.True(rows[0].Success);
        }

        [Fact]
        public void Group_GrantPermission_AuditRow_HasPermission()
        {
            _plugin.CreateGroup("grant_audit", "Grant", null, out _);

            var arg = BuildArg(new[] { "grant", "grant_audit", "sentinel.kick" });
            InvokeConsoleCommand("CCmdGroup", arg);

            var rows = GetAuditRows("group_grant_permission");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
            Assert.Contains("sentinel.kick", rows[0].DetailsJson);
        }

        [Fact]
        public void Group_RevokePermission_AuditRow_HasPermission()
        {
            _plugin.CreateGroup("revoke_audit", "Revoke", null, out _);
            _plugin.GrantGroupPermission("revoke_audit", "sentinel.kick", out _);

            var arg = BuildArg(new[] { "revoke", "revoke_audit", "sentinel.kick" });
            InvokeConsoleCommand("CCmdGroup", arg);

            var rows = GetAuditRows("group_revoke_permission");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
            Assert.Contains("sentinel.kick", rows[0].DetailsJson);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-044: Item grants generate audit rows with shortname, quantity, target, and actor
        // ---------------------------------------------------------
        [Fact]
        public void Item_Give_AuditRow_HasShortnameAndQuantity()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");
            _plugin.TestItemDefinitions.Add(new ItemDefinition { shortname = "wood", displayName = "Wood", stackable = 1000 });

            _plugin.ExecuteGiveItem(admin, "Target", "wood", 100, out _);

            var rows = GetAuditRows("item_give");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.Contains("wood", rows[0].DetailsJson);
            Assert.Contains("100", rows[0].DetailsJson);
        }

        [Fact]
        public void Item_Drop_AuditRow_HasShortnameAndQuantity()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");
            _plugin.TestItemDefinitions.Add(new ItemDefinition { shortname = "stones", displayName = "Stones", stackable = 1000 });

            _plugin.ExecuteDropItem(admin, "Target", "stones", 50, out _);

            var rows = GetAuditRows("item_drop");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.Contains("stones", rows[0].DetailsJson);
            Assert.Contains("50", rows[0].DetailsJson);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-045: World control changes generate audit rows with old/new values
        // ---------------------------------------------------------
        [Fact]
        public void World_Time_AuditRow_HasOldAndNewValues()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");

            _plugin.ExecuteSetTime(admin, 12.5, out _);

            var rows = GetAuditRows("world_time");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
            Assert.Contains("oldHour", rows[0].DetailsJson);
            Assert.Contains("newHour", rows[0].DetailsJson);
        }

        [Fact]
        public void World_Weather_AuditRow_HasOldAndNewValues()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.world");

            _plugin.ExecuteSetWeather(admin, "storm", out _);

            var rows = GetAuditRows("world_weather");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
            Assert.Contains("oldWeather", rows[0].DetailsJson);
            Assert.Contains("newWeather", rows[0].DetailsJson);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-046: Audit log supports time-range query
        // ---------------------------------------------------------
        [Fact]
        public void QueryAuditLog_ByTimeRange_ReturnsOnlyMatchingRows()
        {
            var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Insert rows with explicitly different timestamps
            using var connection = CreateConnection();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO sentinel_actions (actor_steam_id, action_type, timestamp, success) VALUES (@actor, @type, @ts, 1);";
                cmd.Parameters.AddWithValue("@actor", "actor1");
                cmd.Parameters.AddWithValue("@type", "time_test");
                cmd.Parameters.AddWithValue("@ts", baseTime);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO sentinel_actions (actor_steam_id, action_type, timestamp, success) VALUES (@actor, @type, @ts, 1);";
                cmd.Parameters.AddWithValue("@actor", "actor2");
                cmd.Parameters.AddWithValue("@type", "time_test");
                cmd.Parameters.AddWithValue("@ts", baseTime + 10);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO sentinel_actions (actor_steam_id, action_type, timestamp, success) VALUES (@actor, @type, @ts, 1);";
                cmd.Parameters.AddWithValue("@actor", "actor3");
                cmd.Parameters.AddWithValue("@type", "time_test");
                cmd.Parameters.AddWithValue("@ts", baseTime + 20);
                cmd.ExecuteNonQuery();
            }

            var results = _plugin.QueryAuditLog(fromTimestamp: baseTime + 15);
            Assert.Equal(1, results.Count);
            Assert.Equal("actor3", results[0].ActorSteamId);
            Assert.All(results, r => Assert.True(r.Timestamp >= baseTime + 15));
        }

        [Fact]
        public void QueryAuditLog_ByTimeRange_BothBounds()
        {
            var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var connection = CreateConnection();
            for (int i = 0; i < 3; i++)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO sentinel_actions (actor_steam_id, action_type, timestamp, success) VALUES (@actor, @type, @ts, 1);";
                cmd.Parameters.AddWithValue("@actor", $"actor{i}");
                cmd.Parameters.AddWithValue("@type", "bound_test");
                cmd.Parameters.AddWithValue("@ts", baseTime + i * 10);
                cmd.ExecuteNonQuery();
            }

            var all = _plugin.QueryAuditLog(actionType: "bound_test");
            Assert.Equal(3, all.Count);

            var bounded = _plugin.QueryAuditLog(fromTimestamp: baseTime + 5, toTimestamp: baseTime + 15, actionType: "bound_test");
            Assert.Single(bounded);
            Assert.Equal("actor1", bounded[0].ActorSteamId);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-047: Audit log supports filtering by actor
        // ---------------------------------------------------------
        [Fact]
        public void QueryAuditLog_ByActor_ReturnsOnlyMatchingRows()
        {
            _plugin.LogAuditAction("actor_a", null, null, null, "kick", null, null, true);
            _plugin.LogAuditAction("actor_b", null, null, null, "ban", null, null, true);
            _plugin.LogAuditAction("actor_a", null, null, null, "warn", null, null, true);

            var results = _plugin.QueryAuditLog(actorSteamId: "actor_a");
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal("actor_a", r.ActorSteamId));
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-048: Audit log supports filtering by target
        // ---------------------------------------------------------
        [Fact]
        public void QueryAuditLog_ByTarget_ReturnsOnlyMatchingRows()
        {
            _plugin.LogAuditAction("actor1", null, "target_x", null, "kick", null, null, true);
            _plugin.LogAuditAction("actor1", null, "target_y", null, "ban", null, null, true);
            _plugin.LogAuditAction("actor1", null, "target_x", null, "warn", null, null, true);

            var results = _plugin.QueryAuditLog(targetSteamId: "target_x");
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal("target_x", r.TargetSteamId));
        }

        [Fact]
        public void QueryAuditLog_ByActorAndTarget_CombinedFilter()
        {
            _plugin.LogAuditAction("actor1", null, "target1", null, "kick", null, null, true);
            _plugin.LogAuditAction("actor1", null, "target2", null, "ban", null, null, true);
            _plugin.LogAuditAction("actor2", null, "target1", null, "warn", null, null, true);

            var results = _plugin.QueryAuditLog(actorSteamId: "actor1", targetSteamId: "target1");
            Assert.Single(results);
            Assert.Equal("actor1", results[0].ActorSteamId);
            Assert.Equal("target1", results[0].TargetSteamId);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-049: Audit log requires sentinel.audit permission to query
        // ---------------------------------------------------------
        [Fact]
        public void AuditQuery_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            // No sentinel.audit permission granted

            var result = _plugin.ExecuteAuditQuery(admin, null, null, null, null, null, out var entries, out var total);

            Assert.False(result);
            Assert.Null(entries);

            var rows = GetAuditRows("audit_query");
            Assert.Single(rows);
            Assert.False(rows[0].Success);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
        }

        [Fact]
        public void AuditQuery_WithPermission_IsAllowed()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.audit");

            _plugin.LogAuditAction("some_actor", null, null, null, "kick", null, null, true);

            var result = _plugin.ExecuteAuditQuery(admin, null, null, null, null, null, out var entries, out var total);

            Assert.True(result);
            Assert.NotNull(entries);
            Assert.True(total >= 1);

            var rows = GetAuditRows("audit_query");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
            Assert.Contains("returned", rows[0].DetailsJson);
        }

        [Fact]
        public void AuditQuery_Console_HasPermissionByDefault()
        {
            var result = _plugin.ExecuteAuditQuery(null, null, null, null, null, null, out var entries, out var total);

            Assert.True(result);
            Assert.NotNull(entries);

            var rows = GetAuditRows("audit_query");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-050: Audit database survives server restart
        // ---------------------------------------------------------
        [Fact]
        public void AuditLog_SurvivesDatabaseReload()
        {
            _plugin.LogAuditAction("actor1", null, "target1", null, "kick", "test", null, true);
            _plugin.LogAuditAction("actor2", null, "target2", null, "ban", "cheating", 60, true);

            var preCount = GetAuditRows().Count;
            Assert.Equal(2, preCount);

            // Simulate reload
            _plugin.CloseDatabase();
            _plugin.InitializeDatabase(_dbPath);

            var postCount = GetAuditRows().Count;
            Assert.Equal(2, postCount);

            var results = _plugin.QueryAuditLog(actorSteamId: "actor1");
            Assert.Single(results);
            Assert.Equal("kick", results[0].ActionType);
            Assert.Equal("test", results[0].Reason);
        }

        [Fact]
        public void AuditLog_DurationMinutes_PersistsAfterReload()
        {
            _plugin.LogAuditAction("actor1", null, "target1", null, "ban", "cheating", 1440, true);

            _plugin.CloseDatabase();
            _plugin.InitializeDatabase(_dbPath);

            var rows = GetAuditRows("ban");
            Assert.Single(rows);
            Assert.Equal(1440, rows[0].DurationMinutes);
        }

        // ---------------------------------------------------------
        // Additional query method tests
        // ---------------------------------------------------------
        [Fact]
        public void QueryAuditLog_ByActionType_ReturnsOnlyMatchingRows()
        {
            _plugin.LogAuditAction("actor1", null, null, null, "kick", null, null, true);
            _plugin.LogAuditAction("actor1", null, null, null, "ban", null, null, true);
            _plugin.LogAuditAction("actor1", null, null, null, "kick", null, null, true);

            var results = _plugin.QueryAuditLog(actionType: "kick");
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal("kick", r.ActionType));
        }

        [Fact]
        public void QueryAuditLog_LimitAndOffset_WorkCorrectly()
        {
            for (int i = 0; i < 5; i++)
            {
                _plugin.LogAuditAction("actor1", null, null, null, "test", null, null, true);
                System.Threading.Thread.Sleep(10);
            }

            var all = _plugin.QueryAuditLog(actionType: "test");
            Assert.True(all.Count >= 5);

            var limited = _plugin.QueryAuditLog(actionType: "test", limit: 2);
            Assert.True(limited.Count <= 2);

            var offset = _plugin.QueryAuditLog(actionType: "test", limit: 2, offset: 2);
            Assert.True(offset.Count <= 2);
            // Since ordered by timestamp DESC, offset should skip newest
            if (all.Count >= 4 && limited.Count == 2 && offset.Count == 2)
            {
                Assert.True(offset[0].Timestamp <= limited[limited.Count - 1].Timestamp);
            }
        }

        [Fact]
        public void CountAuditLog_ReturnsCorrectTotal()
        {
            _plugin.LogAuditAction("actor1", null, null, null, "kick", null, null, true);
            _plugin.LogAuditAction("actor1", null, null, null, "ban", null, null, true);

            var count = _plugin.CountAuditLog();
            Assert.True(count >= 2);

            var filteredCount = _plugin.CountAuditLog(actionType: "kick");
            Assert.True(filteredCount >= 1);
        }

        [Fact]
        public void QueryAuditLog_ReturnsCorrectEntryStructure()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _plugin.LogAuditAction("actor1", "AdminName", "target1", "TargetName", "kick", "test reason", 30, true, "{\"details\":true}");

            var results = _plugin.QueryAuditLog();
            var entry = results.FirstOrDefault(r => r.ActorSteamId == "actor1" && r.ActionType == "kick");
            Assert.NotNull(entry);
            Assert.Equal("AdminName", entry.ActorName);
            Assert.Equal("target1", entry.TargetSteamId);
            Assert.Equal("TargetName", entry.TargetName);
            Assert.Equal("kick", entry.ActionType);
            Assert.Equal("test reason", entry.Reason);
            Assert.Equal(30, entry.DurationMinutes);
            Assert.Equal("{\"details\":true}", entry.DetailsJson);
            Assert.True(entry.Success);
            Assert.True(entry.Timestamp >= now);
        }

        [Fact]
        public void AuditLog_FailedAction_StillPersisted()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            // No permission granted

            _plugin.ExecuteKick(admin, "Target", "test", out _);

            _plugin.CloseDatabase();
            _plugin.InitializeDatabase(_dbPath);

            var rows = GetAuditRows("kick");
            Assert.Single(rows);
            Assert.False(rows[0].Success);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
        }
    }
}
