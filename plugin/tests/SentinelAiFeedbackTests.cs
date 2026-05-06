using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Oxide.Core;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelAiFeedbackTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;
        private readonly MockRuntimeBridge _logger;
        private readonly List<TestPlayer> _localPlayers = new();

        public SentinelAiFeedbackTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_ai_feedback_test_{Guid.NewGuid()}.db");
            _plugin = new TestableSentinel();
            _logger = new MockRuntimeBridge();
            _plugin.InitializeRuntimeBridgeCustom(_logger);
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

        private void SeedAiLogQuery(string agentName, string requestId)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO sentinel_ai_log (agent_name, request_id, prompt_hash, redacted_input, raw_output, duration_ms, timestamp)
                VALUES (@agentName, @requestId, 'hash', 'input', 'output', 100, @ts);";
            cmd.Parameters.AddWithValue("@agentName", agentName);
            cmd.Parameters.AddWithValue("@requestId", requestId);
            cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.ExecuteNonQuery();
        }

        private List<FeedbackRow> GetFeedbackRows()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT agent_name, request_id, verdict, admin_steam_id, timestamp
                FROM sentinel_ai_log
                WHERE verdict IS NOT NULL
                ORDER BY id;";
            var rows = new List<FeedbackRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new FeedbackRow
                {
                    AgentName = reader.GetString(0),
                    RequestId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Verdict = reader.GetString(2),
                    AdminSteamId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Timestamp = reader.GetInt64(4)
                });
            }
            return rows;
        }

        private List<AiLogQueryRow> GetAiLogQueryRows()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT agent_name, request_id, prompt_hash, raw_output
                FROM sentinel_ai_log
                WHERE verdict IS NULL
                ORDER BY id;";
            var rows = new List<AiLogQueryRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new AiLogQueryRow
                {
                    AgentName = reader.GetString(0),
                    RequestId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    PromptHash = reader.GetString(2),
                    RawOutput = reader.GetString(3)
                });
            }
            return rows;
        }

        private class FeedbackRow
        {
            public string AgentName { get; set; } = "";
            public string? RequestId { get; set; }
            public string Verdict { get; set; } = "";
            public string? AdminSteamId { get; set; }
            public long Timestamp { get; set; }
        }

        private class AiLogQueryRow
        {
            public string AgentName { get; set; } = "";
            public string? RequestId { get; set; }
            public string PromptHash { get; set; } = "";
            public string RawOutput { get; set; } = "";
        }

        private class MockRuntimeBridge : IRuntimeBridge
        {
            public RuntimeType Runtime => RuntimeType.Oxide;
            public List<string> Logs { get; } = new();
            public void LogInfo(string message) => Logs.Add($"INFO: {message}");
            public void LogWarning(string message) => Logs.Add($"WARN: {message}");
            public void LogError(string message) => Logs.Add($"ERROR: {message}");
        }

        private class TestableSentinel : SentinelPlugin
        {
            public List<TestPlayer> LocalPlayers { get; set; } = new();
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }

            public void InitializeRuntimeBridgeCustom(MockRuntimeBridge bridge)
            {
                var field = typeof(SentinelPlugin).GetField("_runtimeBridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(this, bridge);
            }

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

        // ---------------------------------------------------------
        // VAL-AI-011: Feedback loop — thumbs-down logs to ai_log
        // ---------------------------------------------------------
        [Fact]
        public void Feedback_ThumbsDown_LogsAiLogRow_WithSchemaFields()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var requestId = Guid.NewGuid().ToString("N");
            SeedAiLogQuery("BanDraft", requestId);

            var result = _plugin.ExecuteAiFeedback(admin, requestId, "reject", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var rows = GetFeedbackRows();
            Assert.Single(rows);
            Assert.Equal("BanDraft", rows[0].AgentName);
            Assert.Equal(requestId, rows[0].RequestId);
            Assert.Equal("reject", rows[0].Verdict);
            Assert.Equal(admin.UserIDString, rows[0].AdminSteamId);
            Assert.True(rows[0].Timestamp > 0);
        }

        [Fact]
        public void Feedback_ThumbsUp_LogsAiLogRow_WithSchemaFields()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var requestId = Guid.NewGuid().ToString("N");
            SeedAiLogQuery("Triage", requestId);

            var result = _plugin.ExecuteAiFeedback(admin, requestId, "accept", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var rows = GetFeedbackRows();
            Assert.Single(rows);
            Assert.Equal("Triage", rows[0].AgentName);
            Assert.Equal(requestId, rows[0].RequestId);
            Assert.Equal("accept", rows[0].Verdict);
            Assert.Equal(admin.UserIDString, rows[0].AdminSteamId);
        }

        [Fact]
        public void Feedback_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var requestId = Guid.NewGuid().ToString("N");
            SeedAiLogQuery("AntiCheat", requestId);

            var result = _plugin.ExecuteAiFeedback(admin, requestId, "accept", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Empty(GetFeedbackRows());
        }

        [Fact]
        public void Feedback_InvalidVerdict_IsRejected()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var requestId = Guid.NewGuid().ToString("N");
            SeedAiLogQuery("Triage", requestId);

            var result = _plugin.ExecuteAiFeedback(admin, requestId, "maybe", out var error);

            Assert.False(result);
            Assert.Equal("Verdict must be 'accept' or 'reject'", error);
            Assert.Empty(GetFeedbackRows());
        }

        [Fact]
        public void Feedback_UnknownRequestId_IsRejected()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var result = _plugin.ExecuteAiFeedback(admin, "nonexistent", "accept", out var error);

            Assert.False(result);
            Assert.Equal("AI log entry not found for request ID", error);
            Assert.Empty(GetFeedbackRows());
        }

        [Fact]
        public void Feedback_Console_HasPermissionByDefault()
        {
            var requestId = Guid.NewGuid().ToString("N");
            SeedAiLogQuery("Search", requestId);

            var result = _plugin.ExecuteAiFeedback(null, requestId, "reject", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var rows = GetFeedbackRows();
            Assert.Single(rows);
            Assert.Equal("console", rows[0].AdminSteamId);
        }

        // ---------------------------------------------------------
        // VAL-AI-011: Aggregation by agent computes weekly accuracy
        // ---------------------------------------------------------
        [Fact]
        public void Feedback_Aggregation_ByAgent_ReturnsNonZeroCounts()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            void InsertFeedback(string agent, string verdict)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO sentinel_ai_log (agent_name, request_id, verdict, admin_steam_id, timestamp)
                    VALUES (@agent, @req, @verdict, 'admin', @ts);";
                cmd.Parameters.AddWithValue("@agent", agent);
                cmd.Parameters.AddWithValue("@req", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@verdict", verdict);
                cmd.Parameters.AddWithValue("@ts", now);
                cmd.ExecuteNonQuery();
            }

            InsertFeedback("Triage", "accept");
            InsertFeedback("Triage", "accept");
            InsertFeedback("Triage", "reject");
            InsertFeedback("BanDraft", "accept");
            InsertFeedback("BanDraft", "reject");
            InsertFeedback("BanDraft", "reject");

            var agg = _plugin.QueryAiFeedbackAggregation(sinceTimestamp: now - 86400);

            Assert.Equal(2, agg.Count);

            var triage = agg.FirstOrDefault(a => a.AgentName == "Triage");
            Assert.NotNull(triage);
            Assert.Equal(3, triage.TotalFeedback);
            Assert.Equal(2, triage.Accepts);
            Assert.Equal(1, triage.Rejects);

            var banDraft = agg.FirstOrDefault(a => a.AgentName == "BanDraft");
            Assert.NotNull(banDraft);
            Assert.Equal(3, banDraft.TotalFeedback);
            Assert.Equal(1, banDraft.Accepts);
            Assert.Equal(2, banDraft.Rejects);
        }

        [Fact]
        public void Feedback_Aggregation_ExcludesOldEntries()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO sentinel_ai_log (agent_name, request_id, verdict, admin_steam_id, timestamp)
                    VALUES ('Triage', @req, 'accept', 'admin', @ts);";
                cmd.Parameters.AddWithValue("@req", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@ts", now - 86400 * 8); // 8 days ago
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO sentinel_ai_log (agent_name, request_id, verdict, admin_steam_id, timestamp)
                    VALUES ('Triage', @req, 'reject', 'admin', @ts);";
                cmd.Parameters.AddWithValue("@req", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@ts", now - 86400 * 2); // 2 days ago
                cmd.ExecuteNonQuery();
            }

            var agg = _plugin.QueryAiFeedbackAggregation(sinceTimestamp: now - 86400 * 7);
            Assert.Single(agg);
            Assert.Equal(1, agg[0].TotalFeedback);
            Assert.Equal(0, agg[0].Accepts);
            Assert.Equal(1, agg[0].Rejects);
        }

        [Fact]
        public void Feedback_Aggregation_IgnoresQueryRows()
        {
            var requestId = Guid.NewGuid().ToString("N");
            SeedAiLogQuery("Triage", requestId);

            var agg = _plugin.QueryAiFeedbackAggregation();
            Assert.Empty(agg);
        }

        // ---------------------------------------------------------
        // VAL-AI-011: Console command trace for feedback submission
        // ---------------------------------------------------------
        [Fact]
        public void Console_AiFeedback_ExecutesAndLogs()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var requestId = Guid.NewGuid().ToString("N");
            SeedAiLogQuery("AntiCheat", requestId);

            var method = GetCommandMethod("CCmdAiAction");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { "feedback", requestId, "reject" }, admin);
            method!.Invoke(_plugin, new object[] { arg });

            var rows = GetFeedbackRows();
            Assert.Single(rows);
            Assert.Equal("AntiCheat", rows[0].AgentName);
            Assert.Equal("reject", rows[0].Verdict);
        }

        [Fact]
        public void Console_AiFeedback_MissingArgs_ShowsUsage()
        {
            var method = GetCommandMethod("CCmdAiAction");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { "feedback", "only-one-arg" });
            method!.Invoke(_plugin, new object[] { arg });

            Assert.Empty(GetFeedbackRows());
        }

        // ---------------------------------------------------------
        // BanDraftAgent creates an AiSuggestion for feedback
        // ---------------------------------------------------------
        [Fact]
        public void BanDraftAgent_CreatesAiSuggestion()
        {
            var evidence = new List<ActionRecord>
            {
                new ActionRecord
                {
                    ActorSteamId = "76561190000000002",
                    TargetSteamId = "76561190000000001",
                    ActionType = "kick",
                    Reason = "Cheating",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Success = true
                }
            };

            var result = _plugin.RunBanDraftAgent("76561190000000001", "TargetPlayer", evidence);

            Assert.NotNull(result);
            Assert.True(_plugin.SuggestionCount > 0);

            var suggestion = _plugin.GetNextSuggestion();
            Assert.NotNull(suggestion);
            Assert.Equal("BanDraft", suggestion.AgentName);
            Assert.Equal("ban", suggestion.RecommendedAction);
            Assert.Equal("TargetPlayer", suggestion.PlayerName);
            Assert.Equal("76561190000000001", suggestion.SteamId);
            Assert.Equal("ban_draft", suggestion.Behavior);
            Assert.False(string.IsNullOrEmpty(suggestion.Reason));
        }

        [Fact]
        public void BanDraftAgent_Suggestion_CanBeRejected()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var evidence = new List<ActionRecord>
            {
                new ActionRecord
                {
                    ActorSteamId = "76561190000000002",
                    TargetSteamId = "76561190000000001",
                    ActionType = "kick",
                    Reason = "Cheating",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Success = true
                }
            };

            var result = _plugin.RunBanDraftAgent("76561190000000001", "TargetPlayer", evidence);
            var suggestion = _plugin.GetNextSuggestion();
            Assert.NotNull(suggestion);

            var rejectResult = _plugin.ExecuteAiReject(admin, suggestion.Id, out var error);
            Assert.True(rejectResult);
            Assert.Empty(error);
            Assert.Null(_plugin.GetSuggestionById(suggestion.Id));

            var feedbackRows = GetFeedbackRows();
            Assert.Single(feedbackRows);
            Assert.Equal("BanDraft", feedbackRows[0].AgentName);
            Assert.Equal("reject", feedbackRows[0].Verdict);
        }

        [Fact]
        public void BanDraftAgent_Suggestion_CanBeAccepted()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "TargetPlayer");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            var evidence = new List<ActionRecord>
            {
                new ActionRecord
                {
                    ActorSteamId = "76561190000000001",
                    TargetSteamId = "76561190000000002",
                    ActionType = "kick",
                    Reason = "Cheating",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Success = true
                }
            };

            var result = _plugin.RunBanDraftAgent("76561190000000002", "TargetPlayer", evidence);
            var suggestion = _plugin.GetNextSuggestion();
            Assert.NotNull(suggestion);

            var acceptResult = _plugin.ExecuteAiAccept(admin, suggestion.Id, out var error);
            Assert.True(acceptResult);
            Assert.Empty(error);
            Assert.True(target.WasKicked);

            var feedbackRows = GetFeedbackRows();
            Assert.Single(feedbackRows);
            Assert.Equal("BanDraft", feedbackRows[0].AgentName);
            Assert.Equal("accept", feedbackRows[0].Verdict);
        }

        // ---------------------------------------------------------
        // Audit logging for feedback
        // ---------------------------------------------------------
        [Fact]
        public void Feedback_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ai");

            var requestId = Guid.NewGuid().ToString("N");
            SeedAiLogQuery("RuleLookup", requestId);

            _plugin.ExecuteAiFeedback(admin, requestId, "accept", out _);

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT action_type, success, details_json FROM sentinel_actions WHERE action_type = 'ai_feedback';";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("ai_feedback", reader.GetString(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Contains(requestId, reader.GetString(2));
        }
    }
}
