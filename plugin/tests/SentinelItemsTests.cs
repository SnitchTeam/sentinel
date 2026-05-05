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
    public class SentinelItemsTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;
        private readonly List<TestPlayer> _localPlayers = new();

        public SentinelItemsTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_items_test_{Guid.NewGuid()}.db");
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

        private void AddTestItems()
        {
            _plugin.TestItemDefinitions.AddRange(new[]
            {
                new ItemDefinition { shortname = "rifle.ak", displayName = "Assault Rifle", stackable = 1 },
                new ItemDefinition { shortname = "rifle.bolt", displayName = "Bolt Action Rifle", stackable = 1 },
                new ItemDefinition { shortname = "ammo.rifle", displayName = "5.56 Rifle Ammo", stackable = 128 },
                new ItemDefinition { shortname = "wood", displayName = "Wood", stackable = 1000 },
                new ItemDefinition { shortname = "stones", displayName = "Stones", stackable = 1000 },
            });
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
            public List<ItemDefinition> TestItemDefinitions { get; set; } = new();
            public List<(BasePlayer player, Item item)> GivenItems { get; set; } = new();
            public List<(BasePlayer player, Item item)> DroppedItems { get; set; } = new();
            public int TestCapacity { get; set; } = 24;
            public int TestOccupancy { get; set; } = 0;

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

            protected override List<ItemDefinition> GetAllItemDefinitions() => TestItemDefinitions;

            protected override Item? CreateItemByName(string shortname, int amount)
            {
                var def = TestItemDefinitions.FirstOrDefault(d => d.shortname.Equals(shortname, StringComparison.OrdinalIgnoreCase));
                if (def == null) return null;
                return new Item { info = def, amount = amount };
            }

            protected override bool GiveItemToInventory(BasePlayer player, Item item)
            {
                if (TestOccupancy < TestCapacity)
                {
                    TestOccupancy++;
                    GivenItems.Add((player, item));
                    return true;
                }
                return false;
            }

            protected override void DropItemAtPlayerFeet(BasePlayer player, Item item)
            {
                DroppedItems.Add((player, item));
            }

            protected override int GetInventoryCapacity(BasePlayer player) => TestCapacity;
            protected override int GetInventoryOccupancy(BasePlayer player) => TestOccupancy;
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
        // VAL-ADMIN-024: Item search returns matching items by name
        // ---------------------------------------------------------
        [Fact]
        public void SearchItems_PartialShortname_ReturnsMatches()
        {
            AddTestItems();
            var results = _plugin.SearchItems("rifle");
            Assert.Equal(3, results.Count);
            Assert.Contains(results, r => r.Shortname == "rifle.ak");
            Assert.Contains(results, r => r.Shortname == "rifle.bolt");
            Assert.Contains(results, r => r.Shortname == "ammo.rifle");
        }

        [Fact]
        public void SearchItems_CaseInsensitive()
        {
            AddTestItems();
            var results = _plugin.SearchItems("RIFLE");
            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void SearchItems_NoMatch_ReturnsEmpty()
        {
            AddTestItems();
            var results = _plugin.SearchItems("pistol");
            Assert.Empty(results);
        }

        [Fact]
        public void SearchItems_EmptyQuery_ReturnsEmpty()
        {
            AddTestItems();
            var results = _plugin.SearchItems("");
            Assert.Empty(results);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-025: Item can be spawned into target player's inventory
        // ---------------------------------------------------------
        [Fact]
        public void GiveItem_WithPermission_AddsToInventory()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");

            var result = _plugin.ExecuteGiveItem(admin, "Target", "rifle.ak", 1, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.Single(_plugin.GivenItems);
            Assert.Equal("rifle.ak", _plugin.GivenItems[0].item.info.shortname);
            Assert.Equal(1, _plugin.GivenItems[0].item.amount);
        }

        [Fact]
        public void GiveItem_WithoutPermission_IsDenied()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");

            var result = _plugin.ExecuteGiveItem(admin, "Target", "rifle.ak", 1, out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Empty(_plugin.GivenItems);
            Assert.Empty(_plugin.DroppedItems);
        }

        [Fact]
        public void GiveItem_TargetNotFound_Fails()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");

            var result = _plugin.ExecuteGiveItem(admin, "Missing", "rifle.ak", 1, out var error);

            Assert.False(result);
            Assert.Equal("Player not found", error);
        }

        [Fact]
        public void GiveItem_InvalidItem_Fails()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");

            var result = _plugin.ExecuteGiveItem(admin, "Target", "nonexistent.item", 1, out var error);

            Assert.False(result);
            Assert.Contains("not found", error);
        }

        [Fact]
        public void GiveItem_ZeroQuantity_Fails()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");

            var result = _plugin.ExecuteGiveItem(admin, "Target", "wood", 0, out var error);

            Assert.False(result);
            Assert.Contains("greater than 0", error);
        }

        [Fact]
        public void GiveItem_NotifiesTarget()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");

            _plugin.ExecuteGiveItem(admin, "Target", "rifle.ak", 1, out _);

            Assert.Contains(target.ChatMessages, m => m.Contains("received") && m.Contains("rifle.ak"));
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-026: Item can be spawned at target player's feet
        // ---------------------------------------------------------
        [Fact]
        public void DropItem_WithPermission_DropsAtFeet()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");

            var result = _plugin.ExecuteDropItem(admin, "Target", "stones", 50, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.Single(_plugin.DroppedItems);
            Assert.Equal("stones", _plugin.DroppedItems[0].item.info.shortname);
            Assert.Equal(50, _plugin.DroppedItems[0].item.amount);
        }

        [Fact]
        public void DropItem_WithoutPermission_IsDenied()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");

            var result = _plugin.ExecuteDropItem(admin, "Target", "stones", 50, out var error);

            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Empty(_plugin.DroppedItems);
        }

        [Fact]
        public void DropItem_NotifiesTarget()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");

            _plugin.ExecuteDropItem(admin, "Target", "stones", 50, out _);

            Assert.Contains(target.ChatMessages, m => m.Contains("dropped at your feet"));
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-027: Item grant respects max stack size and capacity
        // ---------------------------------------------------------
        [Fact]
        public void GiveItem_OversizedQuantity_SplitsStacks()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");
            _plugin.TestCapacity = 10; // plenty of room

            var result = _plugin.ExecuteGiveItem(admin, "Target", "ammo.rifle", 200, out var error);

            Assert.True(result);
            Assert.Empty(error);
            // stackable = 128, so 200 = 128 + 72
            Assert.Equal(2, _plugin.GivenItems.Count);
            Assert.Equal(128, _plugin.GivenItems[0].item.amount);
            Assert.Equal(72, _plugin.GivenItems[1].item.amount);
            Assert.Empty(_plugin.DroppedItems);
        }

        [Fact]
        public void GiveItem_InventoryFull_DropsExcess()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");
            _plugin.TestCapacity = 1;
            _plugin.TestOccupancy = 0;

            var result = _plugin.ExecuteGiveItem(admin, "Target", "ammo.rifle", 200, out var error);

            Assert.True(result);
            Assert.Empty(error);
            // First stack of 128 fits, second stack of 72 is dropped
            Assert.Single(_plugin.GivenItems);
            Assert.Equal(128, _plugin.GivenItems[0].item.amount);
            Assert.Single(_plugin.DroppedItems);
            Assert.Equal(72, _plugin.DroppedItems[0].item.amount);
        }

        [Fact]
        public void GiveItem_FullInventory_DropsAll()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");
            _plugin.TestCapacity = 0;
            _plugin.TestOccupancy = 0;

            var result = _plugin.ExecuteGiveItem(admin, "Target", "rifle.ak", 1, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.Empty(_plugin.GivenItems);
            Assert.Single(_plugin.DroppedItems);
            Assert.Equal("rifle.ak", _plugin.DroppedItems[0].item.info.shortname);
        }

        [Fact]
        public void DropItem_OversizedQuantity_SplitsStacks()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");

            var result = _plugin.ExecuteDropItem(admin, "Target", "ammo.rifle", 300, out var error);

            Assert.True(result);
            Assert.Empty(error);
            // 300 / 128 = 3 stacks (128 + 128 + 44)
            Assert.Equal(3, _plugin.DroppedItems.Count);
            Assert.Equal(128, _plugin.DroppedItems[0].item.amount);
            Assert.Equal(128, _plugin.DroppedItems[1].item.amount);
            Assert.Equal(44, _plugin.DroppedItems[2].item.amount);
        }

        // ---------------------------------------------------------
        // Audit logging
        // ---------------------------------------------------------
        [Fact]
        public void GiveItem_GeneratesAuditRow()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");

            _plugin.ExecuteGiveItem(admin, "Target", "rifle.ak", 1, out _);

            var rows = GetAuditRows("item_give");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("rifle.ak", rows[0].DetailsJson);
        }

        [Fact]
        public void GiveItem_WithoutPermission_GeneratesFailedAuditRow()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            CreatePlayer(76561190000000002, "Target");

            _plugin.ExecuteGiveItem(admin, "Target", "rifle.ak", 1, out _);

            var rows = GetAuditRows("item_give");
            Assert.Single(rows);
            Assert.False(rows[0].Success);
        }

        [Fact]
        public void DropItem_GeneratesAuditRow()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");

            _plugin.ExecuteDropItem(admin, "Target", "stones", 50, out _);

            var rows = GetAuditRows("item_drop");
            Assert.Single(rows);
            Assert.Equal("76561190000000001", rows[0].ActorSteamId);
            Assert.Equal("76561190000000002", rows[0].TargetSteamId);
            Assert.True(rows[0].Success);
            Assert.Contains("stones", rows[0].DetailsJson);
        }

        [Fact]
        public void DropItem_WithoutPermission_GeneratesFailedAuditRow()
        {
            AddTestItems();
            var admin = CreatePlayer(76561190000000001, "Admin");
            CreatePlayer(76561190000000002, "Target");

            _plugin.ExecuteDropItem(admin, "Target", "stones", 50, out _);

            var rows = GetAuditRows("item_drop");
            Assert.Single(rows);
            Assert.False(rows[0].Success);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-028: Item grant requires sentinel.items permission
        // ---------------------------------------------------------
        [Theory]
        [InlineData("ExecuteGiveItem")]
        [InlineData("ExecuteDropItem")]
        public void PermissionMatrix_WithoutPermission_Denied(string methodName)
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            AddTestItems();

            bool result;
            string error;

            if (methodName == "ExecuteGiveItem")
                result = _plugin.ExecuteGiveItem(admin, "Target", "rifle.ak", 1, out error);
            else
                result = _plugin.ExecuteDropItem(admin, "Target", "rifle.ak", 1, out error);

            Assert.False(result);
            Assert.Equal("No permission", error);
        }

        [Theory]
        [InlineData("ExecuteGiveItem")]
        [InlineData("ExecuteDropItem")]
        public void PermissionMatrix_WithPermission_Allowed(string methodName)
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.items");
            AddTestItems();

            bool result;
            string error;

            if (methodName == "ExecuteGiveItem")
                result = _plugin.ExecuteGiveItem(admin, "Target", "rifle.ak", 1, out error);
            else
                result = _plugin.ExecuteDropItem(admin, "Target", "rifle.ak", 1, out error);

            Assert.True(result);
            Assert.Empty(error);
        }

        [Fact]
        public void WildcardPermission_GrantsItemCommands()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.*");
            AddTestItems();

            Assert.True(_plugin.ExecuteGiveItem(admin, "Target", "rifle.ak", 1, out _));
            Assert.True(_plugin.ExecuteDropItem(admin, "Target", "rifle.ak", 1, out _));
        }

        [Fact]
        public void ConsoleHasPermission_ByDefault()
        {
            var target = CreatePlayer(76561190000000002, "Target");
            AddTestItems();

            Assert.True(_plugin.ExecuteGiveItem(null, "Target", "rifle.ak", 1, out _));
            Assert.True(_plugin.ExecuteDropItem(null, "Target", "rifle.ak", 1, out _));
        }
    }
}
