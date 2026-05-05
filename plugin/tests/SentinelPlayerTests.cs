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
    public class SentinelPlayerTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly List<TestPlayer> _localPlayers = new();

        public SentinelPlayerTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_player_test_{Guid.NewGuid()}.db");
            _plugin = new TestableSentinel();
            _plugin.LocalPlayers = _localPlayers;
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

        // VAL-ADMIN-001: Online player list returns all connected users
        [Fact]
        public void PlayerList_ReturnsAllConnectedUsers()
        {
            var p1 = CreatePlayer(76561190000000001, "Alice");
            var p2 = CreatePlayer(76561190000000002, "Bob");
            var p3 = CreatePlayer(76561190000000003, "Charlie");

            _plugin.OnPlayerConnected(p1);
            _plugin.OnPlayerConnected(p2);
            _plugin.OnPlayerConnected(p3);

            var list = _plugin.GetOnlinePlayers();
            Assert.Equal(3, list.Count);
            Assert.Contains(list, x => x.Name == "Alice" && x.SteamId == "76561190000000001");
            Assert.Contains(list, x => x.Name == "Bob" && x.SteamId == "76561190000000002");
            Assert.Contains(list, x => x.Name == "Charlie" && x.SteamId == "76561190000000003");
        }

        [Fact]
        public void PlayerList_CountMatchesConnectedPlayers()
        {
            var p1 = CreatePlayer(76561190000000001, "Alice");
            var p2 = CreatePlayer(76561190000000002, "Bob");

            _plugin.OnPlayerConnected(p1);
            _plugin.OnPlayerConnected(p2);

            var list = _plugin.GetOnlinePlayers();
            Assert.Equal(2, list.Count);
        }

        // VAL-ADMIN-002: Player search filters by name substring
        [Fact]
        public void Search_FiltersByNameSubstring_CaseInsensitive()
        {
            var p1 = CreatePlayer(76561190000000001, "BobTheBuilder");
            var p2 = CreatePlayer(76561190000000002, "SpongeBob");
            var p3 = CreatePlayer(76561190000000003, "Alice");

            _plugin.OnPlayerConnected(p1);
            _plugin.OnPlayerConnected(p2);
            _plugin.OnPlayerConnected(p3);

            var results = _plugin.SearchOnlinePlayers("bob");
            Assert.Equal(2, results.Count);
            Assert.Contains(results, x => x.Name == "BobTheBuilder");
            Assert.Contains(results, x => x.Name == "SpongeBob");
            Assert.DoesNotContain(results, x => x.Name == "Alice");
        }

        [Fact]
        public void Search_EmptyQuery_ReturnsAll()
        {
            var p1 = CreatePlayer(76561190000000001, "Alice");
            _plugin.OnPlayerConnected(p1);

            var results = _plugin.SearchOnlinePlayers("");
            Assert.Single(results);
        }

        // VAL-ADMIN-003: Player search filters by exact SteamID
        [Fact]
        public void Search_FiltersByExactSteamId()
        {
            var p1 = CreatePlayer(76561190000000001, "Alice");
            var p2 = CreatePlayer(76561190000000002, "Bob");

            _plugin.OnPlayerConnected(p1);
            _plugin.OnPlayerConnected(p2);

            var results = _plugin.SearchOnlinePlayers("76561190000000002");
            Assert.Single(results);
            Assert.Equal("Bob", results[0].Name);
        }

        [Fact]
        public void Search_PartialSteamId_DoesNotMatch()
        {
            var p1 = CreatePlayer(76561190000000001, "Alice");
            _plugin.OnPlayerConnected(p1);

            var results = _plugin.SearchOnlinePlayers("7656119");
            Assert.Empty(results);
        }

        // VAL-ADMIN-004: Offline player history retains last 100 disconnects
        [Fact]
        public void OfflineHistory_RetainsDisconnectedPlayers()
        {
            var p1 = CreatePlayer(76561190000000001, "Alice");
            _plugin.OnPlayerConnected(p1);
            _plugin.OnPlayerDisconnected(p1, "Disconnected by user");

            var history = _plugin.GetOfflineHistory();
            Assert.Single(history);
            Assert.Equal("Alice", history[0].Name);
            Assert.Equal("76561190000000001", history[0].SteamId);
            Assert.Equal("Disconnected by user", history[0].DisconnectReason);
            Assert.True(history[0].DisconnectedAt <= DateTime.UtcNow);
        }

        [Fact]
        public void OfflineHistory_RetainsLast100Disconnects()
        {
            for (int i = 0; i < 105; i++)
            {
                var p = CreatePlayer((ulong)(76561190000000000UL + (ulong)i), $"Player{i}");
                _plugin.OnPlayerConnected(p);
                _plugin.OnPlayerDisconnected(p, "test");
            }

            var history = _plugin.GetOfflineHistory();
            Assert.Equal(100, history.Count);
            // Oldest should have been evicted
            Assert.DoesNotContain(history, h => h.Name == "Player0");
            Assert.DoesNotContain(history, h => h.Name == "Player1");
            Assert.DoesNotContain(history, h => h.Name == "Player2");
            Assert.DoesNotContain(history, h => h.Name == "Player3");
            Assert.DoesNotContain(history, h => h.Name == "Player4");
            // Newest should be present
            Assert.Contains(history, h => h.Name == "Player104");
        }

        [Fact]
        public void OfflineHistory_SearchableByName()
        {
            var p1 = CreatePlayer(76561190000000001, "Alice");
            var p2 = CreatePlayer(76561190000000002, "Bob");
            _plugin.OnPlayerConnected(p1);
            _plugin.OnPlayerConnected(p2);
            _plugin.OnPlayerDisconnected(p1, "test");
            _plugin.OnPlayerDisconnected(p2, "test");

            var results = _plugin.SearchOfflinePlayers("Alice");
            Assert.Single(results);
            Assert.Equal("Alice", results[0].Name);
        }

        [Fact]
        public void OfflineHistory_SearchableBySteamId()
        {
            var p1 = CreatePlayer(76561190000000001, "Alice");
            var p2 = CreatePlayer(76561190000000002, "Bob");
            _plugin.OnPlayerConnected(p1);
            _plugin.OnPlayerConnected(p2);
            _plugin.OnPlayerDisconnected(p1, "test");
            _plugin.OnPlayerDisconnected(p2, "test");

            var results = _plugin.SearchOfflinePlayers("76561190000000002");
            Assert.Single(results);
            Assert.Equal("Bob", results[0].Name);
        }

        [Fact]
        public void ResolveTarget_FindsByExactSteamId()
        {
            var p1 = CreatePlayer(76561190000000001, "Alice");
            var p2 = CreatePlayer(76561190000000002, "Bob");
            BasePlayer.activePlayerList.Add(p1);
            BasePlayer.activePlayerList.Add(p2);

            var found = _plugin.ResolveTarget("76561190000000002");
            Assert.NotNull(found);
            Assert.Equal("Bob", found!.displayName);
        }

        [Fact]
        public void ResolveTarget_FindsByPartialName()
        {
            var p1 = CreatePlayer(76561190000000001, "Alice");
            var p2 = CreatePlayer(76561190000000002, "BobTheBuilder");
            BasePlayer.activePlayerList.Add(p1);
            BasePlayer.activePlayerList.Add(p2);

            var found = _plugin.ResolveTarget("Builder");
            Assert.NotNull(found);
            Assert.Equal("BobTheBuilder", found!.displayName);
        }

        [Fact]
        public void ResolveTarget_ReturnsNullForMissing()
        {
            var found = _plugin.ResolveTarget("NonExistent");
            Assert.Null(found);
        }
    }
}
