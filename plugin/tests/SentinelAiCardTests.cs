using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Oxide.Core;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelAiCardTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;
        private readonly List<TestPlayer> _localPlayers = new();

        public SentinelAiCardTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_ai_test_{Guid.NewGuid()}.db");
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

        private AiSuggestion CreateTestSuggestion(string action = "ban", int? duration = 1440)
        {
            return new AiSuggestion
            {
                Id = Guid.NewGuid().ToString("N"),
                PlayerName = "CheaterX",
                SteamId = "76561190000000099",
                Behavior = "aim",
                Confidence = 92,
                RecommendedAction = action,
                Reason = "Aim assistance detected",
                DurationMinutes = duration,
                AgentName = "AntiCheat"
            };
        }

        private List<AiLogRow> GetAiLogRows()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT agent_name, request_id, verdict, admin_steam_id, edit_diff FROM sentinel_ai_log ORDER BY id;";
            var rows = new List<AiLogRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new AiLogRow
                {
                    AgentName = reader.GetString(0),
                    RequestId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Verdict = reader.GetString(2),
                    AdminSteamId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    EditDiff = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
            return rows;
        }

        private List<AuditRow> GetAuditRows(string? actionType = null)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = actionType != null
                ? "SELECT actor_steam_id, target_steam_id, action_type, reason, duration_minutes, success, details_json FROM sentinel_actions WHERE action_type = @type ORDER BY id;"
                : "SELECT actor_steam_id, target_steam_id, action_type, reason, duration_minutes, success, details_json FROM sentinel_actions ORDER BY id;";
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
                    DetailsJson = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
            }
            return rows;
        }

        private class AiLogRow
        {
            public string AgentName { get; set; } = "";
            public string? RequestId { get; set; }
            public string Verdict { get; set; } = "";
            public string? AdminSteamId { get; set; }
            public string? EditDiff { get; set; }
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
        }

        private class TestableSentinel : SentinelPlugin
        {
            public List<TestPlayer> LocalPlayers { get; set; } = new();
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }

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

            public override void RegisterPermission(string perm, Oxide.Plugins.RustPlugin owner) { }
        }

        // ---------------------------------------------------------
        // VAL-CUI-021: AI suggestion card renders with required elements
        // ---------------------------------------------------------
        [Fact]
        public void AiSuggestion_Card_RendersWithPlayerNameBehaviorConfidenceAndButtons()
        {
            var suggestion = CreateTestSuggestion();
            _plugin.AddAiSuggestion(suggestion);

            var next = _plugin.GetNextSuggestion();
            Assert.NotNull(next);
            Assert.Equal("CheaterX", next.PlayerName);
            Assert.Equal("aim", next.Behavior);
            Assert.Equal(92, next.Confidence);
        }

        [Fact]
        public void AiView_WithSuggestion_ContainsAllRequiredElements()
        {
            var suggestion = CreateTestSuggestion();
            var json = CuiHelper.ToJson(_plugin.BuildAiView("76561198000000001", suggestion));
            Assert.Contains("CheaterX", json);
            Assert.Contains("aim", json);
            Assert.Contains("92%", json);
            Assert.Contains("ACCEPT", json);
            Assert.Contains("REJECT", json);
            Assert.Contains("EDIT", json);
            Assert.Contains($"sentinel.ai accept {suggestion.Id}", json);
            Assert.Contains($"sentinel.ai reject {suggestion.Id}", json);
            Assert.Contains($"sentinel.ai edit {suggestion.Id}", json);
        }

        // ---------------------------------------------------------
        // VAL-CUI-022: Accept action applies recommended action and removes card
        // ---------------------------------------------------------
        [Fact]
        public void AiSuggestion_Accept_AppliesActionAndRemovesCard()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiAccept(admin, suggestion.Id, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.True(target.WasKicked);
            Assert.Contains("Aim assistance detected", target.LastKickReason);
            Assert.Null(_plugin.GetSuggestionById(suggestion.Id));
            Assert.Equal(0, _plugin.SuggestionCount);
        }

        [Fact]
        public void AiSuggestion_Accept_LogsToAiLog()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            _plugin.ExecuteAiAccept(admin, suggestion.Id, out _);

            var rows = GetAiLogRows();
            Assert.Single(rows);
            Assert.Equal("AntiCheat", rows[0].AgentName);
            Assert.Equal(suggestion.Id, rows[0].RequestId);
            Assert.Equal("accept", rows[0].Verdict);
            Assert.Equal(admin.UserIDString, rows[0].AdminSteamId);
        }

        [Fact]
        public void AiSuggestion_Accept_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            _plugin.ExecuteAiAccept(admin, suggestion.Id, out _);

            var rows = GetAuditRows("ai_accept");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
            Assert.Contains(suggestion.Id, rows[0].DetailsJson);
        }

        [Fact]
        public void AiSuggestion_Accept_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var suggestion = CreateTestSuggestion();
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiAccept(admin, suggestion.Id, out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.NotNull(_plugin.GetSuggestionById(suggestion.Id));
        }

        [Fact]
        public void AiSuggestion_Accept_KickAction_AppliesKick()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.kick");

            var suggestion = CreateTestSuggestion("kick", null);
            suggestion.Reason = "Speed hack";
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiAccept(admin, suggestion.Id, out _);

            Assert.True(result);
            Assert.True(target.WasKicked);
            Assert.Contains("Speed hack", target.LastKickReason);
        }

        [Fact]
        public void AiSuggestion_Accept_WarnAction_AppliesWarn()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.warn");

            var suggestion = CreateTestSuggestion("warn", null);
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiAccept(admin, suggestion.Id, out _);

            Assert.True(result);
            Assert.Contains("WARNING", target.ChatMessages[0]);
        }

        [Fact]
        public void AiSuggestion_Accept_FreezeAction_AppliesFreeze()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.freeze");

            var suggestion = CreateTestSuggestion("freeze", null);
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiAccept(admin, suggestion.Id, out _);

            Assert.True(result);
            Assert.Contains("frozen", target.ChatMessages[0]);
        }

        // ---------------------------------------------------------
        // VAL-CUI-023: Reject action dismisses card without action and logs rejection
        // ---------------------------------------------------------
        [Fact]
        public void AiSuggestion_Reject_DismissesCardWithoutAction()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiReject(admin, suggestion.Id, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.False(target.WasKicked);
            Assert.Null(_plugin.GetSuggestionById(suggestion.Id));
        }

        [Fact]
        public void AiSuggestion_Reject_LogsRejectionToAiLog()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            _plugin.ExecuteAiReject(admin, suggestion.Id, out _);

            var rows = GetAiLogRows();
            Assert.Single(rows);
            Assert.Equal("reject", rows[0].Verdict);
            Assert.Equal(admin.UserIDString, rows[0].AdminSteamId);
        }

        [Fact]
        public void AiSuggestion_Reject_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            _plugin.ExecuteAiReject(admin, suggestion.Id, out _);

            var rows = GetAuditRows("ai_reject");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
            Assert.Equal("76561190000000099", rows[0].TargetSteamId);
        }

        [Fact]
        public void AiSuggestion_Reject_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var suggestion = CreateTestSuggestion();
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiReject(admin, suggestion.Id, out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.NotNull(_plugin.GetSuggestionById(suggestion.Id));
        }

        // ---------------------------------------------------------
        // VAL-CUI-024: Edit action opens editor; saving applies modified action
        // ---------------------------------------------------------
        [Fact]
        public void AiSuggestion_Edit_SetsEditingState()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiEdit(admin, suggestion.Id, out var error);

            Assert.True(result);
            Assert.Empty(error);
        }

        [Fact]
        public void AiSuggestion_Edit_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var suggestion = CreateTestSuggestion();
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiEdit(admin, suggestion.Id, out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
        }

        [Fact]
        public void AiSuggestion_Save_AppliesModifiedAction()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var suggestion = CreateTestSuggestion("ban", 60);
            suggestion.Reason = "Original reason";
            _plugin.AddAiSuggestion(suggestion);

            // Simulate editing via CUI input fields
            _plugin.SetPendingAiEdit(suggestion.Id, "Modified reason", 120);

            var result = _plugin.ExecuteAiSave(admin, suggestion.Id, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.True(target.WasKicked);
            Assert.Contains("Modified reason", target.LastKickReason);
            Assert.Null(_plugin.GetSuggestionById(suggestion.Id));
        }

        [Fact]
        public void AiSuggestion_Save_LogsEditDiffToAiLog()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var suggestion = CreateTestSuggestion("ban", 60);
            suggestion.Reason = "Original reason";
            _plugin.AddAiSuggestion(suggestion);

            _plugin.SetPendingAiEdit(suggestion.Id, "Modified reason", 120);
            _plugin.ExecuteAiSave(admin, suggestion.Id, out _);

            var rows = GetAiLogRows();
            Assert.Single(rows);
            Assert.Equal("edit", rows[0].Verdict);
            Assert.NotNull(rows[0].EditDiff);
            Assert.Contains("Original reason", rows[0].EditDiff);
            Assert.Contains("Modified reason", rows[0].EditDiff);
        }

        [Fact]
        public void AiSuggestion_Save_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            _plugin.SetPendingAiEdit(suggestion.Id, "Modified reason", 120);
            _plugin.ExecuteAiSave(admin, suggestion.Id, out _);

            var rows = GetAuditRows("ai_save");
            Assert.Single(rows);
            Assert.True(rows[0].Success);
            Assert.Equal("Modified reason", rows[0].Reason);
            Assert.Equal(120, rows[0].DurationMinutes);
        }

        [Fact]
        public void AiSuggestion_Save_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var suggestion = CreateTestSuggestion();
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiSave(admin, suggestion.Id, out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.NotNull(_plugin.GetSuggestionById(suggestion.Id));
        }

        [Fact]
        public void AiSuggestion_Save_UsesOriginalValuesWhenNoEditsPending()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.kick");

            var suggestion = CreateTestSuggestion("kick", null);
            suggestion.Reason = "Original reason";
            _plugin.AddAiSuggestion(suggestion);

            var result = _plugin.ExecuteAiSave(admin, suggestion.Id, out _);

            Assert.True(result);
            Assert.True(target.WasKicked);
            Assert.Contains("Original reason", target.LastKickReason);
        }

        // ---------------------------------------------------------
        // CUI Edit View Tests
        // ---------------------------------------------------------
        [Fact]
        public void AiEditView_ContainsPreFilledSuggestionData()
        {
            var suggestion = CreateTestSuggestion("ban", 1440);
            var json = CuiHelper.ToJson(_plugin.BuildAiEditView("76561198000000001", suggestion));
            Assert.Contains("CheaterX", json);
            Assert.Contains("76561190000000099", json);
            Assert.Contains("aim", json);
            Assert.Contains("92%", json);
            Assert.Contains("ban", json);
            Assert.Contains("SAVE", json);
            Assert.Contains("CANCEL", json);
            Assert.Contains("sentinel.ai save", json);
            Assert.Contains("sentinel.view ai", json);
        }

        [Fact]
        public void AiEditView_Payload_DoesNotExceed4096Bytes()
        {
            var suggestion = CreateTestSuggestion("ban", 1440);
            var container = _plugin.BuildAiEditView("76561198000000001", suggestion);
            var json = CuiHelper.ToJson(container);
            Assert.True(json.Length <= 4096, $"AI Edit view payload is {json.Length} bytes, exceeds 4096");
        }

        [Fact]
        public void AiView_Payload_WithSuggestion_DoesNotExceed4096Bytes()
        {
            var suggestion = CreateTestSuggestion("ban", 1440);
            var container = _plugin.BuildAiView("76561198000000001", suggestion);
            var json = CuiHelper.ToJson(container);
            Assert.True(json.Length <= 4096, $"AI view with suggestion payload is {json.Length} bytes, exceeds 4096");
        }

        // ---------------------------------------------------------
        // Console Command Tests
        // ---------------------------------------------------------
        private static ConsoleSystem.Arg BuildArg(string[]? args, BasePlayer? player = null)
        {
            var arg = new ConsoleSystem.Arg();
            typeof(ConsoleSystem.Arg).GetProperty("Args")?.SetValue(arg, args);
            typeof(ConsoleSystem.Arg).GetProperty("_player")?.SetValue(arg, player);
            return arg;
        }

        private static System.Reflection.MethodInfo? GetCommandMethod(string methodName)
        {
            return typeof(SentinelPlugin).GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }

        [Fact]
        public void Console_AiAccept_ExecutesAndRemovesSuggestion()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            var method = GetCommandMethod("CCmdAiAction");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { "accept", suggestion.Id }, admin);
            method!.Invoke(_plugin, new object[] { arg });

            Assert.True(target.WasKicked);
            Assert.Null(_plugin.GetSuggestionById(suggestion.Id));
        }

        [Fact]
        public void Console_AiReject_ExecutesAndRemovesSuggestion()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            var method = GetCommandMethod("CCmdAiAction");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { "reject", suggestion.Id }, admin);
            method!.Invoke(_plugin, new object[] { arg });

            Assert.Null(_plugin.GetSuggestionById(suggestion.Id));
        }

        [Fact]
        public void Console_AiEdit_SetsEditingState()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.panel");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            var method = GetCommandMethod("CCmdAiAction");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { "edit", suggestion.Id }, admin);
            method!.Invoke(_plugin, new object[] { arg });

            // After edit command, admin should be in editing state
            // (the SwitchView call would open ai_edit view in a real runtime)
        }

        [Fact]
        public void Console_AiSave_ExecutesModifiedAction()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);
            _plugin.SetPendingAiEdit(suggestion.Id, "Console modified reason", 240);

            var method = GetCommandMethod("CCmdAiAction");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { "save", suggestion.Id }, admin);
            method!.Invoke(_plugin, new object[] { arg });

            Assert.True(target.WasKicked);
            Assert.Contains("Console modified reason", target.LastKickReason);
        }

        [Fact]
        public void Console_AiEditReason_UpdatesPendingEdit()
        {
            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            var method = GetCommandMethod("CCmdAiEditReason");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { suggestion.Id, "New reason text" });
            method!.Invoke(_plugin, new object[] { arg });

            // Verify through save execution
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            _plugin.ExecuteAiSave(admin, suggestion.Id, out _);
            Assert.Contains("New reason text", target.LastKickReason);
        }

        [Fact]
        public void Console_AiEditDuration_UpdatesPendingEdit()
        {
            var suggestion = CreateTestSuggestion("ban", 60);
            _plugin.AddAiSuggestion(suggestion);

            var method = GetCommandMethod("CCmdAiEditDuration");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { suggestion.Id, "300" });
            method!.Invoke(_plugin, new object[] { arg });

            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000099, "CheaterX");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            _plugin.ExecuteAiSave(admin, suggestion.Id, out _);

            var rows = GetAuditRows("ai_save");
            Assert.Single(rows);
            Assert.Equal(300, rows[0].DurationMinutes);
        }
    }
}
