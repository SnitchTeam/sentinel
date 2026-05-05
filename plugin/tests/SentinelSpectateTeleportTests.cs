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
    public class SentinelSpectateTeleportTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;
        private readonly List<TestPlayer> _localPlayers = new();

        public SentinelSpectateTeleportTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_spec_test_{Guid.NewGuid()}.db");
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
            public List<TestPlayer> LocalPlayers { get; set; } = new();
            public bool TerrainValid { get; set; } = true;
            public string TerrainError { get; set; } = "";
            public float TerrainHeight { get; set; } = 0f;
            public bool BuildingCheckResult { get; set; } = false;

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

            protected override bool IsInsideBuilding(Vector3 position)
            {
                return BuildingCheckResult;
            }

            protected override float GetTerrainHeight(float x, float z)
            {
                return TerrainHeight;
            }

            protected override bool IsValidTeleportDestination(Vector3 destination, out string error)
            {
                if (!TerrainValid)
                {
                    error = TerrainError;
                    return false;
                }
                error = "";
                return true;
            }
        }

        private class TestPlayer : BasePlayer
        {
            public bool WasKicked { get; private set; }
            public string? LastKickReason { get; private set; }
            public List<string> ChatMessages { get; } = new();
            public List<string> Flags { get; } = new();

            public override void Kick(string reason)
            {
                WasKicked = true;
                LastKickReason = reason;
            }

            public override void ChatMessage(string message)
            {
                ChatMessages.Add(message);
            }

            public override void SetPlayerFlag(string flag, bool value)
            {
                if (value)
                {
                    if (!Flags.Contains(flag)) Flags.Add(flag);
                }
                else
                {
                    Flags.Remove(flag);
                }
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
                    if (set.Contains("sentinel.*"))
                    {
                        return perm.StartsWith("sentinel.", StringComparison.OrdinalIgnoreCase);
                    }
                }
                return false;
            }
        }

        // VAL-ADMIN-011: Admin can enter spectate on a target player
        [Fact]
        public void SpectateEnter_WithPermission_Succeeds()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 10, 20, 30);
            var target = CreatePlayer(76561190000000002, "Target", 50, 60, 70);
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            var result = _plugin.ExecuteSpectate(admin, "Target", out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.True(_plugin.IsSpectating(admin.UserIDString));
            Assert.Contains("Spectating", admin.Flags);
        }

        [Fact]
        public void SpectateEnter_SavesOriginalPosition()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 10, 20, 30);
            var target = CreatePlayer(76561190000000002, "Target", 50, 60, 70);
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "Target", out _);

            var state = _plugin.GetSpectateState(admin.UserIDString);
            Assert.NotNull(state);
            Assert.Equal(10f, state!.OriginalPosition.x);
            Assert.Equal(20f, state.OriginalPosition.y);
            Assert.Equal(30f, state.OriginalPosition.z);
        }

        [Fact]
        public void SpectateEnter_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "Target", out _);

            var rows = GetAuditRows("spectate");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("enter", rows[0].DetailsJson);
        }

        // VAL-ADMIN-012: Admin can exit spectate and return to original position within 0.1 units
        [Fact]
        public void SpectateExit_RestoresOriginalPosition()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 10, 20, 30);
            var target = CreatePlayer(76561190000000002, "Target", 50, 60, 70);
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "Target", out _);
            // Simulate movement during spectate
            admin.Position = new Vector3(100, 200, 300);

            var result = _plugin.ExecuteExitSpectate(admin, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.Equal(10f, admin.Position.x);
            Assert.Equal(20f, admin.Position.y);
            Assert.Equal(30f, admin.Position.z);
        }

        [Fact]
        public void SpectateExit_RestoresOriginalRotation()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            admin.Rotation = new Vector3(0, 90, 0);
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "Target", out _);
            admin.Rotation = new Vector3(0, 180, 0);

            _plugin.ExecuteExitSpectate(admin, out _);

            Assert.Equal(90f, admin.Rotation.y);
        }

        [Fact]
        public void SpectateExit_RemovesSpectateFlag()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "Target", out _);
            _plugin.ExecuteExitSpectate(admin, out _);

            Assert.DoesNotContain("Spectating", admin.Flags);
            Assert.False(_plugin.IsSpectating(admin.UserIDString));
        }

        [Fact]
        public void SpectateExit_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "Target", out _);
            _plugin.ExecuteExitSpectate(admin, out _);

            var rows = GetAuditRows("spectate");
            Assert.Equal(2, rows.Count);
            Assert.True(rows[1].Success);
            Assert.Contains("exit", rows[1].DetailsJson);
        }

        // VAL-ADMIN-013: Spectate is blocked without sentinel.spectate permission
        [Fact]
        public void SpectateEnter_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");

            var result = _plugin.ExecuteSpectate(admin, "Target", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.False(_plugin.IsSpectating(admin.UserIDString));
        }

        [Fact]
        public void SpectateExit_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");
            _plugin.ExecuteSpectate(admin, "Target", out _);
            _mockPermission.Revoke(admin.UserIDString, "sentinel.spectate");

            var result = _plugin.ExecuteExitSpectate(admin, out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
        }

        [Fact]
        public void SpectateEnter_WithoutPermission_GeneratesFailedAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            CreatePlayer(76561190000000002, "Target");

            _plugin.ExecuteSpectate(admin, "Target", out _);

            var rows = GetAuditRows("spectate");
            Assert.Single(rows);
            Assert.False(rows[0].Success);
        }

        [Fact]
        public void SpectateEnter_TargetNotFound_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            var result = _plugin.ExecuteSpectate(admin, "NonExistent", out var error);

            Assert.False(result);
            Assert.Equal("Target player not found.", error);
        }

        [Fact]
        public void SpectateEnter_NullAdmin_Fails()
        {
            var target = CreatePlayer(76561190000000002, "Target");

            var result = _plugin.ExecuteSpectate(null, "Target", out var error);

            Assert.False(result);
            Assert.Equal("Spectate requires an in-game player.", error);
        }

        [Fact]
        public void SpectateExit_NotSpectating_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            var result = _plugin.ExecuteExitSpectate(admin, out var error);

            Assert.False(result);
            Assert.Equal("You are not currently spectating.", error);
        }

        // VAL-ADMIN-014: TPto teleports admin to target player
        [Fact]
        public void TPto_WithPermission_Succeeds()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            var target = CreatePlayer(76561190000000002, "Target", 100, 50, 100);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            var result = _plugin.ExecuteTeleportTo(admin, "Target", out var error);

            Assert.True(result);
            Assert.Empty(error);
            var distance = Vector3.Distance(admin.Position, target.Position);
            Assert.True(distance <= 2.0f, $"Distance {distance} exceeds 2.0 units");
        }

        [Fact]
        public void TPto_AdminPositionMatchesTargetWithinTwoUnits()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            var target = CreatePlayer(76561190000000002, "Target", 100, 50, 100);
            target.Rotation = new Vector3(0, 0, 0); // Facing north
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            _plugin.ExecuteTeleportTo(admin, "Target", out _);

            // With yaw=0, offset should be +z
            var expectedOffset = new Vector3(0, 0, 1.5f);
            var expectedPos = target.Position + expectedOffset;
            var distance = Vector3.Distance(admin.Position, expectedPos);
            Assert.True(distance < 0.1f, $"Admin position {admin.Position} is too far from expected {expectedPos}");
        }

        [Fact]
        public void TPto_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            var target = CreatePlayer(76561190000000002, "Target", 100, 50, 100);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            _plugin.ExecuteTeleportTo(admin, "Target", out _);

            var rows = GetAuditRows("teleport");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("tpto", rows[0].DetailsJson);
        }

        // VAL-ADMIN-015: TPme teleports target player to admin
        [Fact]
        public void TPme_WithPermission_Succeeds()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 200, 75, 200);
            var target = CreatePlayer(76561190000000002, "Target", 0, 0, 0);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            var result = _plugin.ExecuteTeleportMe(admin, "Target", out var error);

            Assert.True(result);
            Assert.Empty(error);
            var distance = Vector3.Distance(target.Position, admin.Position);
            Assert.True(distance <= 2.0f, $"Distance {distance} exceeds 2.0 units");
        }

        [Fact]
        public void TPme_TargetPositionMatchesAdminWithinTwoUnits()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 200, 75, 200);
            admin.Rotation = new Vector3(0, 90, 0); // Facing east
            var target = CreatePlayer(76561190000000002, "Target", 0, 0, 0);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            _plugin.ExecuteTeleportMe(admin, "Target", out _);

            // With yaw=90, offset should be +x
            var expectedOffset = new Vector3(1.5f, 0, 0);
            var expectedPos = admin.Position + expectedOffset;
            var distance = Vector3.Distance(target.Position, expectedPos);
            Assert.True(distance < 0.1f, $"Target position {target.Position} is too far from expected {expectedPos}");
        }

        [Fact]
        public void TPme_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 200, 75, 200);
            var target = CreatePlayer(76561190000000002, "Target", 0, 0, 0);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            _plugin.ExecuteTeleportMe(admin, "Target", out _);

            var rows = GetAuditRows("teleport");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("tpme", rows[0].DetailsJson);
        }

        // VAL-ADMIN-016: Teleport respects terrain and prevents embedding
        [Fact]
        public void TPto_InvalidDestination_Aborts()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            var target = CreatePlayer(76561190000000002, "Target", 100, 50, 100);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");
            _plugin.TerrainValid = false;
            _plugin.TerrainError = "Destination is below terrain.";

            var result = _plugin.ExecuteTeleportTo(admin, "Target", out var error);

            Assert.False(result);
            Assert.Equal("Destination is below terrain.", error);
            // Admin should not have moved
            Assert.Equal(0f, admin.Position.x);
            Assert.Equal(0f, admin.Position.y);
            Assert.Equal(0f, admin.Position.z);
        }

        [Fact]
        public void TPme_InvalidDestination_Aborts()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 200, 75, 200);
            var target = CreatePlayer(76561190000000002, "Target", 0, 0, 0);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");
            _plugin.TerrainValid = false;
            _plugin.TerrainError = "Destination is inside a building block.";

            var result = _plugin.ExecuteTeleportMe(admin, "Target", out var error);

            Assert.False(result);
            Assert.Equal("Destination is inside a building block.", error);
            // Target should not have moved
            Assert.Equal(0f, target.Position.x);
            Assert.Equal(0f, target.Position.y);
            Assert.Equal(0f, target.Position.z);
        }

        [Fact]
        public void TPto_InsideBuilding_BaseValidation_Aborts()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            var target = CreatePlayer(76561190000000002, "Target", 100, 50, 100);

            var plugin = new TerrainValidatingSentinel
            {
                LocalPlayers = _localPlayers,
                BuildingCheckResult = true,
                TerrainHeight = 0f
            };
            plugin.permission = _mockPermission;
            plugin.InitializeDatabase(_dbPath);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            var result = plugin.ExecuteTeleportTo(admin, "Target", out var error);

            Assert.False(result);
            Assert.Equal("Destination is inside a building block.", error);
            Assert.Equal(0f, admin.Position.x);

            plugin.CloseDatabase();
        }

        [Fact]
        public void TPto_BelowTerrain_BaseValidation_Aborts()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            var target = CreatePlayer(76561190000000002, "Target", 100, 50, 100);

            var plugin = new TerrainValidatingSentinel
            {
                LocalPlayers = _localPlayers,
                BuildingCheckResult = false,
                TerrainHeight = 200f // Terrain is higher than target position
            };
            plugin.permission = _mockPermission;
            plugin.InitializeDatabase(_dbPath);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            var result = plugin.ExecuteTeleportTo(admin, "Target", out var error);

            Assert.False(result);
            Assert.Equal("Destination is below terrain.", error);
            Assert.Equal(0f, admin.Position.x);

            plugin.CloseDatabase();
        }

        // VAL-ADMIN-017: Teleport requires sentinel.teleport permission
        [Fact]
        public void TPto_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            var target = CreatePlayer(76561190000000002, "Target", 100, 50, 100);

            var result = _plugin.ExecuteTeleportTo(admin, "Target", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Equal(0f, admin.Position.x);
        }

        [Fact]
        public void TPme_WithoutPermission_IsDenied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 200, 75, 200);
            var target = CreatePlayer(76561190000000002, "Target", 0, 0, 0);

            var result = _plugin.ExecuteTeleportMe(admin, "Target", out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Equal(0f, target.Position.x);
        }

        [Fact]
        public void TPto_WithoutPermission_GeneratesFailedAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            CreatePlayer(76561190000000002, "Target");

            _plugin.ExecuteTeleportTo(admin, "Target", out _);

            var rows = GetAuditRows("teleport");
            Assert.Single(rows);
            Assert.False(rows[0].Success);
        }

        [Fact]
        public void TPme_WithoutPermission_GeneratesFailedAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            CreatePlayer(76561190000000002, "Target");

            _plugin.ExecuteTeleportMe(admin, "Target", out _);

            var rows = GetAuditRows("teleport");
            Assert.Single(rows);
            Assert.False(rows[0].Success);
        }

        [Fact]
        public void TPto_TargetNotFound_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            var result = _plugin.ExecuteTeleportTo(admin, "NonExistent", out var error);

            Assert.False(result);
            Assert.Equal("Target player not found.", error);
        }

        [Fact]
        public void TPme_TargetNotFound_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            var result = _plugin.ExecuteTeleportMe(admin, "NonExistent", out var error);

            Assert.False(result);
            Assert.Equal("Target player not found.", error);
        }

        [Fact]
        public void TPto_NullAdmin_Fails()
        {
            var target = CreatePlayer(76561190000000002, "Target");

            var result = _plugin.ExecuteTeleportTo(null, "Target", out var error);

            Assert.False(result);
            Assert.Equal("TPto requires an in-game player.", error);
        }

        [Fact]
        public void TPme_NullAdmin_Fails()
        {
            var target = CreatePlayer(76561190000000002, "Target");

            var result = _plugin.ExecuteTeleportMe(null, "Target", out var error);

            Assert.False(result);
            Assert.Equal("TPme requires an in-game player.", error);
        }

        [Fact]
        public void PermissionMatrix_Teleport_WithoutPermission_Denied()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");

            Assert.False(_plugin.ExecuteTeleportTo(admin, "Target", out _));
            Assert.False(_plugin.ExecuteTeleportMe(admin, "Target", out _));
        }

        [Fact]
        public void PermissionMatrix_Teleport_WithPermission_Allowed()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            var target = CreatePlayer(76561190000000002, "Target", 100, 50, 100);
            _mockPermission.Grant(admin.UserIDString, "sentinel.teleport");

            Assert.True(_plugin.ExecuteTeleportTo(admin, "Target", out _));
            Assert.True(_plugin.ExecuteTeleportMe(admin, "Target", out _));
        }

        [Fact]
        public void WildcardPermission_GrantsSpectateAndTeleport()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.*");

            Assert.True(_plugin.ExecuteSpectate(admin, "Target", out _));
            Assert.True(_plugin.ExecuteExitSpectate(admin, out _));
            Assert.True(_plugin.ExecuteTeleportTo(admin, "Target", out _));
            Assert.True(_plugin.ExecuteTeleportMe(admin, "Target", out _));
        }

        [Fact]
        public void ConsoleHasPermission_ByDefault()
        {
            var target = CreatePlayer(76561190000000002, "Target");
            Assert.True(_plugin.HasPermission(null, "sentinel.teleport"));
            Assert.True(_plugin.HasPermission(null, "sentinel.spectate"));
        }

        // Fix: re-spectate position loss — VAL-ADMIN-012
        [Fact]
        public void Spectate_Respect_AlreadySpectating_Rejects()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 10, 20, 30);
            var targetA = CreatePlayer(76561190000000002, "TargetA", 50, 60, 70);
            var targetB = CreatePlayer(76561190000000003, "TargetB", 80, 90, 100);
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "TargetA", out _);
            var result = _plugin.ExecuteSpectate(admin, "TargetB", out var error);

            Assert.False(result);
            Assert.Equal("Already spectating TargetA — exit first", error);
        }

        [Fact]
        public void Spectate_Respect_OriginalPositionPreserved()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 10, 20, 30);
            var targetA = CreatePlayer(76561190000000002, "TargetA", 50, 60, 70);
            var targetB = CreatePlayer(76561190000000003, "TargetB", 80, 90, 100);
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "TargetA", out _);
            _plugin.ExecuteSpectate(admin, "TargetB", out _);

            var state = _plugin.GetSpectateState(admin.UserIDString);
            Assert.NotNull(state);
            Assert.Equal(10f, state!.OriginalPosition.x);
            Assert.Equal(20f, state.OriginalPosition.y);
            Assert.Equal(30f, state.OriginalPosition.z);
        }

        [Fact]
        public void Spectate_Respect_OriginalRotationPreserved()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 0, 0, 0);
            admin.Rotation = new Vector3(0, 45, 0);
            var targetA = CreatePlayer(76561190000000002, "TargetA");
            var targetB = CreatePlayer(76561190000000003, "TargetB");
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "TargetA", out _);
            _plugin.ExecuteSpectate(admin, "TargetB", out _);

            var state = _plugin.GetSpectateState(admin.UserIDString);
            Assert.NotNull(state);
            Assert.Equal(45f, state!.OriginalRotation.y);
        }

        [Fact]
        public void Spectate_Respect_ExitRestoresFirstOriginalPosition()
        {
            var admin = CreatePlayer(76561190000000001, "Admin", 10, 20, 30);
            var targetA = CreatePlayer(76561190000000002, "TargetA", 50, 60, 70);
            var targetB = CreatePlayer(76561190000000003, "TargetB", 80, 90, 100);
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "TargetA", out _);
            admin.Position = new Vector3(100, 200, 300);
            _plugin.ExecuteSpectate(admin, "TargetB", out _); // should fail
            admin.Position = new Vector3(400, 500, 600);

            var exitResult = _plugin.ExecuteExitSpectate(admin, out var exitError);

            Assert.True(exitResult);
            Assert.Empty(exitError);
            Assert.Equal(10f, admin.Position.x);
            Assert.Equal(20f, admin.Position.y);
            Assert.Equal(30f, admin.Position.z);
        }

        [Fact]
        public void Spectate_Respect_FailedReSpectate_GeneratesAuditRow()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var targetA = CreatePlayer(76561190000000002, "TargetA");
            var targetB = CreatePlayer(76561190000000003, "TargetB");
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "TargetA", out _);
            _plugin.ExecuteSpectate(admin, "TargetB", out _);

            var rows = GetAuditRows("spectate");
            Assert.Equal(2, rows.Count);
            Assert.True(rows[0].Success); // first enter succeeds
            Assert.False(rows[1].Success); // second enter fails
            Assert.Contains("TargetA", rows[1].DetailsJson);
        }

        [Fact]
        public void Spectate_Respect_TargetNameInState()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var targetA = CreatePlayer(76561190000000002, "TargetA");
            _mockPermission.Grant(admin.UserIDString, "sentinel.spectate");

            _plugin.ExecuteSpectate(admin, "TargetA", out _);

            var state = _plugin.GetSpectateState(admin.UserIDString);
            Assert.NotNull(state);
            Assert.Equal("TargetA", state!.TargetName);
        }

        // Helper class that uses the base IsValidTeleportDestination (not the override)
        private class TerrainValidatingSentinel : SentinelPlugin
        {
            public List<TestPlayer> LocalPlayers { get; set; } = new();
            public bool BuildingCheckResult { get; set; } = false;
            public float TerrainHeight { get; set; } = 0f;

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

            protected override bool IsInsideBuilding(Vector3 position)
            {
                return BuildingCheckResult;
            }

            protected override float GetTerrainHeight(float x, float z)
            {
                return TerrainHeight;
            }

            // Do NOT override IsValidTeleportDestination — use base implementation
        }
    }
}
