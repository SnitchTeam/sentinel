using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelLoggingTests : IDisposable
    {
        private readonly string _dbPath;

        public SentinelLoggingTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_logging_test_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            try { File.Delete(_dbPath); } catch { }
            try { File.Delete(_dbPath + "-shm"); } catch { }
            try { File.Delete(_dbPath + "-wal"); } catch { }
        }

        private class TestableSentinel : SentinelPlugin
        {
            public List<string> Logs { get; } = new();

            public override void Puts(string message) => Logs.Add(message);
            public override void PrintWarning(string message) => Logs.Add($"[WARN] {message}");
            public override void PrintError(string message) => Logs.Add($"[ERROR] {message}");
        }

        [Fact]
        public void BootBanner_ContainsPluginName()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeRuntimeBridge();
            plugin.InitializeDatabase(_dbPath);
            plugin.EmitBootBanner();

            var banner = plugin.Logs.LastOrDefault(l => l.Contains("Boot — Sentinel"));
            Assert.NotNull(banner);
            Assert.Contains("Sentinel", banner);
        }

        [Fact]
        public void BootBanner_ContainsVersion()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeRuntimeBridge();
            plugin.InitializeDatabase(_dbPath);
            plugin.EmitBootBanner();

            var attr = typeof(SentinelPlugin).GetCustomAttribute<Oxide.Core.Plugins.InfoAttribute>();
            Assert.NotNull(attr);

            var banner = plugin.Logs.LastOrDefault(l => l.Contains("Boot — Sentinel"));
            Assert.NotNull(banner);
            Assert.Contains(attr.Version, banner);
        }

        [Fact]
        public void BootBanner_ContainsRuntimeName()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeRuntimeBridge();
            plugin.InitializeDatabase(_dbPath);
            plugin.EmitBootBanner();

            var banner = plugin.Logs.LastOrDefault(l => l.Contains("Boot — Sentinel"));
            Assert.NotNull(banner);
            Assert.Contains("Runtime=", banner);
        }

        [Fact]
        public void BootBanner_ContainsDatabaseStatus()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeRuntimeBridge();
            plugin.InitializeDatabase(_dbPath);
            plugin.EmitBootBanner();

            var banner = plugin.Logs.LastOrDefault(l => l.Contains("Boot — Sentinel"));
            Assert.NotNull(banner);
            Assert.Contains("Database=", banner);
        }

        [Fact]
        public void BootBanner_AppearsExactlyOncePerLoad()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeRuntimeBridge();
            plugin.InitializeDatabase(_dbPath);
            plugin.EmitBootBanner();

            var bannerCount = plugin.Logs.Count(l => l.Contains("Boot — Sentinel") && l.Contains("Runtime="));
            Assert.Equal(1, bannerCount);
        }

        [Fact]
        public void BootBanner_IsVisibleAtInfoLevel()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeRuntimeBridge();
            plugin.InitializeDatabase(_dbPath);
            plugin.EmitBootBanner();

            var banner = plugin.Logs.LastOrDefault(l => l.Contains("Boot — Sentinel"));
            Assert.NotNull(banner);

            // Info-level messages go through Puts, not PrintWarning/PrintError
            Assert.DoesNotContain("[WARN]", banner);
            Assert.DoesNotContain("[ERROR]", banner);
        }

        [Fact]
        public void Reload_PreservesRowCounts_InAllSevenTables()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeDatabase(_dbPath);
            SeedAllTables(_dbPath);

            var preReloadCounts = GetRowCounts(_dbPath);
            Assert.Equal(7, preReloadCounts.Count);

            // Simulate reload: close and re-initialize
            plugin.CloseDatabase();
            plugin.InitializeDatabase(_dbPath);

            var postReloadCounts = GetRowCounts(_dbPath);
            Assert.Equal(7, postReloadCounts.Count);

            foreach (var table in preReloadCounts.Keys)
            {
                Assert.Equal(preReloadCounts[table], postReloadCounts[table]);
            }
        }

        [Fact]
        public void Reload_DoesNotThrowForeignKeyViolations()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeDatabase(_dbPath);
            SeedAllTables(_dbPath);

            plugin.CloseDatabase();
            var ex = Record.Exception(() => plugin.InitializeDatabase(_dbPath));

            Assert.Null(ex);
        }

        [Fact]
        public void Reload_DoesNotThrowSchemaRecreationErrors()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeDatabase(_dbPath);
            SeedAllTables(_dbPath);

            plugin.CloseDatabase();
            var ex = Record.Exception(() => plugin.InitializeDatabase(_dbPath));

            Assert.Null(ex);
        }

        [Fact]
        public void Reload_MultipleTimes_PreservesData()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeDatabase(_dbPath);
            SeedAllTables(_dbPath);

            var expectedCounts = GetRowCounts(_dbPath);

            for (int i = 0; i < 3; i++)
            {
                plugin.CloseDatabase();
                plugin.InitializeDatabase(_dbPath);
            }

            var actualCounts = GetRowCounts(_dbPath);
            foreach (var table in expectedCounts.Keys)
            {
                Assert.Equal(expectedCounts[table], actualCounts[table]);
            }
        }

        private static void SeedAllTables(string dbPath)
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO sentinel_actions (actor_steam_id, action_type, timestamp, success)
                                    VALUES ('76561190000000001', 'kick', 1234567890, 1);";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO sentinel_bans (steam_id, banned_by_steam_id, reason, created_at)
                                    VALUES ('76561190000000002', '76561190000000001', 'cheating', 1234567890);";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO sentinel_groups (name, title, permissions_json, created_at)
                                    VALUES ('admins', 'Admins', '{}', 1234567890);";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO sentinel_group_members (group_id, steam_id, added_at)
                                    VALUES (1, '76561190000000003', 1234567890);";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO sentinel_ai_log (agent_name, timestamp)
                                    VALUES ('triage', 1234567890);";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO sentinel_baselines (steam_id, metric_name, mean, std_dev, sample_count, last_updated)
                                    VALUES ('76561190000000004', 'headshot_ratio', 0.5, 0.1, 100, 1234567890);";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO sentinel_warnings (target_id, target_name, warn_count, last_reason, last_warned_at, created_at)
                                    VALUES ('76561190000000005', 'WarnedPlayer', 2, 'spam', 1234567890, 1234567890);";
                cmd.ExecuteNonQuery();
            }
        }

        private static Dictionary<string, long> GetRowCounts(string dbPath)
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            var tables = new[] { "sentinel_actions", "sentinel_bans", "sentinel_groups", "sentinel_group_members", "sentinel_ai_log", "sentinel_baselines", "sentinel_warnings" };
            var counts = new Dictionary<string, long>();

            foreach (var table in tables)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
                counts[table] = (long)cmd.ExecuteScalar()!;
            }

            return counts;
        }
    }
}
