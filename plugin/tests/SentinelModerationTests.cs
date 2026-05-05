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
    public class SentinelModerationTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;

        private readonly List<TestPlayer> _localPlayers = new();

        public SentinelModerationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_mod_test_{Guid.NewGuid()}.db");
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
                ? "SELECT actor_steam_id, target_steam_id, action_type, success FROM sentinel_actions WHERE action_type = @type ORDER BY id;"
                : "SELECT actor_steam_id, target_steam_id, action_type, success FROM sentinel_actions ORDER BY id;";
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
                    Success = reader.GetInt32(3) == 1
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
        }

        private class TestableSentinel : SentinelPlugin
        {
            public List<string> Logs { get; } = new();
            public List<TestPlayer> LocalPlayers { get; set; } = new();
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
                    // wildcard support: sentinel.* grants all sentinel.x
                    if (set.Contains("sentinel.*"))
                    {
                        return perm.StartsWith("sentinel.", StringComparison.OrdinalIgnoreCase);
                    }
                }
                return false;
            }
        }

        // VAL-ADMIN-005: Kick action removes player and requires sentinel.kick permission
        [Fact]
        public void Kick_WithPermission_RemovesTarget()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.kick");

            var result = _plugin.ExecuteKick(admin, "Target", "Testing kick", out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.True(target.WasKicked);
            Assert.Equal("Testing kick", target.LastKickReason);
        }

        [Fact]
        public void Kick_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            // No permission granted

            var result = _plugin.ExecuteKick(admin, "Target", "Testing kick", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.False(target.WasKicked);
        }

        [Fact]
        public void Kick_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.kick");

            _plugin.ExecuteKick(admin, "Target", "Testing kick", out _);

            var rows = GetAuditRows("kick");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.True(rows[0].Success);
        }

        [Fact]
        public void Kick_WithoutPermission_GeneratesFailedAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            CreatePlayer(76561190000000002, "Target");

            _plugin.ExecuteKick(admin, "Target", "Testing kick", out _);

            var rows = GetAuditRows("kick");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.False(rows[0].Success);
        }

        // VAL-ADMIN-006: Ban action persists and requires sentinel.ban permission
        [Fact]
        public void Ban_WithPermission_PersistsInDatabase()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var result = _plugin.ExecuteBan(admin, "Target", "Cheating", 60, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.True(target.WasKicked);
            Assert.True(_plugin.IsBanned(target.UserIDString));
        }

        [Fact]
        public void Ban_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");

            var result = _plugin.ExecuteBan(admin, "Target", "Cheating", 60, out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.False(target.WasKicked);
            Assert.False(_plugin.IsBanned(target.UserIDString));
        }

        [Fact]
        public void Ban_BySteamId_WithoutOnlinePlayer_Persists()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var result = _plugin.ExecuteBan(admin, "76561190000000099", "Cheating", null, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.True(_plugin.IsBanned("76561190000000099"));
        }

        [Fact]
        public void Ban_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            _plugin.ExecuteBan(admin, "Target", "Cheating", 60, out _);

            var rows = GetAuditRows("ban");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.True(rows[0].Success);
        }

        [Fact]
        public void Ban_ExpiredBan_IsNotActive()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            // Ban with 0 duration (expired immediately in practice by setting expires_at to now - 1)
            using var command = _plugin.GetDbConnection()!.CreateCommand();
            command.CommandText = @"
                INSERT INTO sentinel_bans (steam_id, name, banned_by_steam_id, banned_by_name, reason, active, created_at, expires_at)
                VALUES ('76561190000000002', 'Target', '76561190000000001', 'Admin', 'Cheating', 1, @now, @now);";
            command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1);
            command.ExecuteNonQuery();

            Assert.False(_plugin.IsBanned("76561190000000002"));
        }

        [Fact]
        public void Ban_ReconnectIsRejected()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");
            _plugin.ExecuteBan(admin, "76561190000000002", "Cheating", null, out _);

            var identity = new AuthenticationTicketIdentity { Userid = "76561190000000002" };
            var result = _plugin.OnUserApprove(identity);

            Assert.NotNull(result);
            Assert.Contains("banned", (result as string)!.ToLowerInvariant());
        }

        [Fact]
        public void Ban_CleanPlayer_CanConnect()
        {
            var identity = new AuthenticationTicketIdentity { Userid = "76561190000000002" };
            var result = _plugin.OnUserApprove(identity);

            Assert.Null(result);
        }

        // VAL-ADMIN-007: Warn action increments warn count and notifies player
        [Fact]
        public void Warn_WithPermission_IncrementsWarnCount()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.warn");

            _plugin.ExecuteWarn(admin, "Target", "First warning", out _);
            _plugin.ExecuteWarn(admin, "Target", "Second warning", out _);

            var state = _plugin.GetOrCreateModerationState(target.UserIDString);
            Assert.Equal(2, state.WarnCount);
        }

        [Fact]
        public void Warn_WithPermission_NotifiesTarget()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.warn");

            _plugin.ExecuteWarn(admin, "Target", "Stop that", out _);

            Assert.Contains(target.ChatMessages, m => m.Contains("WARNING") && m.Contains("Stop that"));
        }

        [Fact]
        public void Warn_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");

            var result = _plugin.ExecuteWarn(admin, "Target", "Stop that", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Equal(0, _plugin.GetOrCreateModerationState(target.UserIDString).WarnCount);
        }

        [Fact]
        public void Warn_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.warn");

            _plugin.ExecuteWarn(admin, "Target", "Stop that", out _);

            var rows = GetAuditRows("warn");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
        }

        // VAL-ADMIN-008: Mute action blocks chat and/or voice with optional duration
        [Fact]
        public void Mute_Chat_BlocksChat()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.mute");

            var result = _plugin.ExecuteMute(admin, "Target", "chat", null, out var error);

            Assert.True(result);
            Assert.Empty(error);

            var state = _plugin.GetOrCreateModerationState(target.UserIDString);
            Assert.True(state.IsChatMuted);
            Assert.False(state.IsVoiceMuted);
        }

        [Fact]
        public void Mute_Voice_BlocksVoice()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.mute");

            var result = _plugin.ExecuteMute(admin, "Target", "voice", null, out _);

            Assert.True(result);
            var state = _plugin.GetOrCreateModerationState(target.UserIDString);
            Assert.False(state.IsChatMuted);
            Assert.True(state.IsVoiceMuted);
        }

        [Fact]
        public void Mute_All_BlocksBoth()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.mute");

            var result = _plugin.ExecuteMute(admin, "Target", "all", null, out _);

            Assert.True(result);
            var state = _plugin.GetOrCreateModerationState(target.UserIDString);
            Assert.True(state.IsChatMuted);
            Assert.True(state.IsVoiceMuted);
        }

        [Fact]
        public void Mute_WithDuration_SetsExpiry()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.mute");

            _plugin.ExecuteMute(admin, "Target", "chat", 60, out _);

            var state = _plugin.GetOrCreateModerationState(target.UserIDString);
            Assert.NotNull(state.MuteExpiresAt);
            Assert.True(state.MuteExpiresAt > DateTime.UtcNow.AddMinutes(59));
        }

        [Fact]
        public void Mute_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");

            var result = _plugin.ExecuteMute(admin, "Target", "chat", null, out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.False(_plugin.GetOrCreateModerationState(target.UserIDString).IsChatMuted);
        }

        [Fact]
        public void Mute_InvalidType_ReturnsError()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.mute");

            var result = _plugin.ExecuteMute(admin, "Target", "invalid", null, out var error);

            Assert.False(result);
            Assert.Equal("Invalid mute type. Use: chat, voice, or all", error);
        }

        [Fact]
        public void Mute_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.mute");

            _plugin.ExecuteMute(admin, "Target", "chat", 30, out _);

            var rows = GetAuditRows("mute");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
        }

        [Fact]
        public void Mute_ChatHook_BlocksMutedPlayer()
        {
            var target = CreatePlayer(76561190000000002, "Target");
            var state = _plugin.GetOrCreateModerationState(target.UserIDString);
            state.IsChatMuted = true;

            var result = _plugin.OnPlayerChat(target, "Hello world");

            Assert.NotNull(result);
            Assert.True((bool)result!);
            Assert.Contains(target.ChatMessages, m => m.Contains("muted"));
        }

        [Fact]
        public void Mute_ChatHook_AllowsUnmutedPlayer()
        {
            var target = CreatePlayer(76561190000000002, "Target");
            var result = _plugin.OnPlayerChat(target, "Hello world");
            Assert.Null(result);
        }

        [Fact]
        public void Mute_ExpiredMute_AllowsChat()
        {
            var target = CreatePlayer(76561190000000002, "Target");
            var state = _plugin.GetOrCreateModerationState(target.UserIDString);
            state.IsChatMuted = true;
            state.MuteExpiresAt = DateTime.UtcNow.AddMinutes(-1);

            var result = _plugin.OnPlayerChat(target, "Hello world");
            Assert.Null(result);
            Assert.False(state.IsChatMuted);
        }

        // VAL-ADMIN-009: Freeze immobilizes player and requires sentinel.freeze permission
        [Fact]
        public void Freeze_WithPermission_TogglesFrozenState()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.freeze");

            var result1 = _plugin.ExecuteFreeze(admin, "Target", out var error1);
            Assert.True(result1);
            Assert.True(_plugin.GetOrCreateModerationState(target.UserIDString).IsFrozen);

            var result2 = _plugin.ExecuteFreeze(admin, "Target", out var error2);
            Assert.True(result2);
            Assert.False(_plugin.GetOrCreateModerationState(target.UserIDString).IsFrozen);
        }

        [Fact]
        public void Freeze_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");

            var result = _plugin.ExecuteFreeze(admin, "Target", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.False(_plugin.GetOrCreateModerationState(target.UserIDString).IsFrozen);
        }

        [Fact]
        public void Freeze_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.freeze");

            _plugin.ExecuteFreeze(admin, "Target", out _);

            var rows = GetAuditRows("freeze");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
        }

        [Fact]
        public void Freeze_NotifiesTarget()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.freeze");

            _plugin.ExecuteFreeze(admin, "Target", out _);

            Assert.Contains(target.ChatMessages, m => m.Contains("frozen"));
        }

        // VAL-ADMIN-010: Permission gates enforced for all moderation actions
        [Theory]
        [InlineData(nameof(SentinelPlugin.ExecuteKick))]
        [InlineData(nameof(SentinelPlugin.ExecuteBan))]
        [InlineData(nameof(SentinelPlugin.ExecuteWarn))]
        [InlineData(nameof(SentinelPlugin.ExecuteMute))]
        [InlineData(nameof(SentinelPlugin.ExecuteFreeze))]
        public void PermissionMatrix_WithoutPermission_ActionDenied(string methodName)
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            // No permission granted

            bool result;
            string error;

            switch (methodName)
            {
                case nameof(SentinelPlugin.ExecuteKick):
                    result = _plugin.ExecuteKick(admin, "Target", "test", out error);
                    break;
                case nameof(SentinelPlugin.ExecuteBan):
                    result = _plugin.ExecuteBan(admin, "Target", "test", null, out error);
                    break;
                case nameof(SentinelPlugin.ExecuteWarn):
                    result = _plugin.ExecuteWarn(admin, "Target", "test", out error);
                    break;
                case nameof(SentinelPlugin.ExecuteMute):
                    result = _plugin.ExecuteMute(admin, "Target", "chat", null, out error);
                    break;
                case nameof(SentinelPlugin.ExecuteFreeze):
                    result = _plugin.ExecuteFreeze(admin, "Target", out error);
                    break;
                default:
                    throw new ArgumentException($"Unknown method: {methodName}");
            }

            Assert.False(result);
        }

        [Theory]
        [InlineData("sentinel.kick", nameof(SentinelPlugin.ExecuteKick))]
        [InlineData("sentinel.ban", nameof(SentinelPlugin.ExecuteBan))]
        [InlineData("sentinel.warn", nameof(SentinelPlugin.ExecuteWarn))]
        [InlineData("sentinel.mute", nameof(SentinelPlugin.ExecuteMute))]
        [InlineData("sentinel.freeze", nameof(SentinelPlugin.ExecuteFreeze))]
        public void PermissionMatrix_WithPermission_ActionAllowed(string permissionNode, string methodName)
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, permissionNode);

            bool result;
            string error;

            switch (methodName)
            {
                case nameof(SentinelPlugin.ExecuteKick):
                    result = _plugin.ExecuteKick(admin, "Target", "test", out error);
                    break;
                case nameof(SentinelPlugin.ExecuteBan):
                    result = _plugin.ExecuteBan(admin, "Target", "test", null, out error);
                    break;
                case nameof(SentinelPlugin.ExecuteWarn):
                    result = _plugin.ExecuteWarn(admin, "Target", "test", out error);
                    break;
                case nameof(SentinelPlugin.ExecuteMute):
                    result = _plugin.ExecuteMute(admin, "Target", "chat", null, out error);
                    break;
                case nameof(SentinelPlugin.ExecuteFreeze):
                    result = _plugin.ExecuteFreeze(admin, "Target", out error);
                    break;
                default:
                    throw new ArgumentException($"Unknown method: {methodName}");
            }

            Assert.True(result);
        }

        [Fact]
        public void ConsoleHasPermission_ByDefault()
        {
            // Console (null player) should have permission for all actions
            var target = CreatePlayer(76561190000000002, "Target");
            Assert.True(_plugin.HasPermission(null, "sentinel.kick"));
        }

        [Fact]
        public void WildcardPermission_GrantsAllSentinelPerms()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.*");

            Assert.True(_plugin.ExecuteKick(admin, "Target", "test", out _));
            Assert.True(_plugin.ExecuteBan(admin, "Target", "test", null, out _));
            Assert.True(_plugin.ExecuteWarn(admin, "Target", "test", out _));
            Assert.True(_plugin.ExecuteMute(admin, "Target", "chat", null, out _));
            Assert.True(_plugin.ExecuteFreeze(admin, "Target", out _));
        }
    }
}
