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
    public class SentinelConsoleTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;

        public SentinelConsoleTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_console_test_{Guid.NewGuid()}.db");
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

        // -------------------------------------------------------------
        // Buffer Rolling Behavior
        // -------------------------------------------------------------
        [Fact]
        public void Buffer_After600Lines_EvictsOldest100()
        {
            for (int i = 1; i <= 600; i++)
            {
                _plugin.CaptureConsoleLine($"Line {i}", "INFO");
            }

            var lines = _plugin.ReadConsoleBuffer();
            Assert.Equal(500, lines.Count);
            Assert.Equal("[INFO] Line 101", lines[0]);
            Assert.Equal("[INFO] Line 600", lines[499]);
        }

        [Fact]
        public void Buffer_Under600Lines_NoEviction()
        {
            for (int i = 1; i <= 500; i++)
            {
                _plugin.CaptureConsoleLine($"Line {i}", "INFO");
            }

            var lines = _plugin.ReadConsoleBuffer();
            Assert.Equal(500, lines.Count);
            Assert.Equal("[INFO] Line 1", lines[0]);
        }

        [Fact]
        public void Buffer_At601Lines_EvictsOldest100()
        {
            for (int i = 1; i <= 601; i++)
            {
                _plugin.CaptureConsoleLine($"Line {i}", "INFO");
            }

            var lines = _plugin.ReadConsoleBuffer();
            Assert.Equal(501, lines.Count);
            Assert.Equal("[INFO] Line 101", lines[0]);
            Assert.Equal("[INFO] Line 601", lines[500]);
        }

        [Fact]
        public void Buffer_Capture_PreservesLevels()
        {
            _plugin.CaptureConsoleLine("info message", "INFO");
            _plugin.CaptureConsoleLine("warn message", "WARN");
            _plugin.CaptureConsoleLine("error message", "ERROR");

            var lines = _plugin.ReadConsoleBuffer();
            Assert.Equal(3, lines.Count);
            Assert.Contains("[INFO] info message", lines[0]);
            Assert.Contains("[WARN] warn message", lines[1]);
            Assert.Contains("[ERROR] error message", lines[2]);
        }

        [Fact]
        public void Buffer_NullOrEmptyMessage_IsIgnored()
        {
            _plugin.CaptureConsoleLine(null, "INFO");
            _plugin.CaptureConsoleLine("", "INFO");
            _plugin.CaptureConsoleLine("valid", "INFO");

            var lines = _plugin.ReadConsoleBuffer();
            Assert.Single(lines);
            Assert.Equal("[INFO] valid", lines[0]);
        }

        // -------------------------------------------------------------
        // Filtering
        // -------------------------------------------------------------
        [Fact]
        public void Buffer_Filter_ReturnsOnlyMatchingLines()
        {
            _plugin.CaptureConsoleLine("error: something failed", "ERROR");
            _plugin.CaptureConsoleLine("info: all good", "INFO");
            _plugin.CaptureConsoleLine("error: another failure", "ERROR");
            _plugin.CaptureConsoleLine("warn: caution", "WARN");

            var filtered = _plugin.ReadConsoleBuffer("error");
            Assert.Equal(2, filtered.Count);
            Assert.All(filtered, line => Assert.Contains("error", line, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Buffer_Filter_CaseInsensitive()
        {
            _plugin.CaptureConsoleLine("ERROR: loud", "ERROR");
            _plugin.CaptureConsoleLine("error: quiet", "ERROR");
            _plugin.CaptureConsoleLine("Error: mixed", "ERROR");

            var filtered = _plugin.ReadConsoleBuffer("error");
            Assert.Equal(3, filtered.Count);
        }

        [Fact]
        public void Buffer_Filter_NoMatches_ReturnsEmpty()
        {
            _plugin.CaptureConsoleLine("hello world", "INFO");
            var filtered = _plugin.ReadConsoleBuffer("xyz");
            Assert.Empty(filtered);
        }

        [Fact]
        public void Buffer_Filter_EmptyFilter_ReturnsAll()
        {
            _plugin.CaptureConsoleLine("line one", "INFO");
            _plugin.CaptureConsoleLine("line two", "INFO");

            var all = _plugin.ReadConsoleBuffer("");
            var noFilter = _plugin.ReadConsoleBuffer(null);
            Assert.Equal(2, all.Count);
            Assert.Equal(2, noFilter.Count);
        }

        [Fact]
        public void Buffer_Filter_AfterEviction_ReturnsCorrectMatches()
        {
            for (int i = 1; i <= 600; i++)
            {
                _plugin.CaptureConsoleLine($"Line {i} error={(i % 2 == 0 ? "yes" : "no")}", "INFO");
            }

            var filtered = _plugin.ReadConsoleBuffer("yes");
            // Lines 101-600 remain. Even lines have "yes": 102, 104, ..., 600 = 250 lines
            Assert.Equal(250, filtered.Count);
            Assert.All(filtered, line => Assert.Contains("yes", line));
        }

        // -------------------------------------------------------------
        // Permission Matrix
        // -------------------------------------------------------------
        [Fact]
        public void ReadConsole_WithoutPermission_IsDenied()
        {
            var player = CreatePlayer(76561190000000001, "NoPerm");
            var result = _plugin.TryReadConsole(player, null, out var lines, out var error);
            Assert.False(result);
            Assert.Equal("No permission", error);
            Assert.Empty(lines);
        }

        [Fact]
        public void ReadConsole_WithPermission_Allowed()
        {
            var player = CreatePlayer(76561190000000001, "HasPerm");
            _mockPermission.Grant(player.UserIDString, "sentinel.console");

            _plugin.CaptureConsoleLine("test log", "INFO");
            var result = _plugin.TryReadConsole(player, null, out var lines, out var error);
            Assert.True(result);
            Assert.Equal("", error);
            Assert.Single(lines);
        }

        [Fact]
        public void FilterConsole_WithoutPermission_IsDenied()
        {
            var player = CreatePlayer(76561190000000001, "NoPerm");
            var result = _plugin.TryReadConsole(player, "test", out var lines, out var error);
            Assert.False(result);
            Assert.Equal("No permission", error);
        }

        [Fact]
        public void FilterConsole_WithPermission_Allowed()
        {
            var player = CreatePlayer(76561190000000001, "HasPerm");
            _mockPermission.Grant(player.UserIDString, "sentinel.console");

            _plugin.CaptureConsoleLine("alpha", "INFO");
            _plugin.CaptureConsoleLine("beta", "INFO");
            var result = _plugin.TryReadConsole(player, "alpha", out var lines, out var error);
            Assert.True(result);
            Assert.Single(lines);
            Assert.Contains("alpha", lines[0]);
        }

        [Fact]
        public void ConsolePermission_ConsoleHasPermission_ByDefault()
        {
            var result = _plugin.TryReadConsole(null, null, out var lines, out var error);
            Assert.True(result);
            Assert.Equal("", error);
        }

        [Fact]
        public void ConsolePermission_WildcardPermission_GrantsConsoleAccess()
        {
            var player = CreatePlayer(76561190000000001, "Wildcard");
            _mockPermission.Grant(player.UserIDString, "sentinel.*");

            _plugin.CaptureConsoleLine("wildcard test", "INFO");
            var result = _plugin.TryReadConsole(player, null, out var lines, out var error);
            Assert.True(result);
            Assert.Single(lines);
        }

        // -------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------
        private class TestableSentinel : SentinelPlugin
        {
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }
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
        }

        private class TestPlayer : BasePlayer { }
    }
}
