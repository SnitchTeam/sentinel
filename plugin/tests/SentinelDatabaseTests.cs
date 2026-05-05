using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Sentinel.Tests
{
    public class SentinelDatabaseTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly Oxide.Plugins.Sentinel _plugin;

        public SentinelDatabaseTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_test_{Guid.NewGuid()}.db");
            _plugin = new Oxide.Plugins.Sentinel();
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

        private SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            return connection;
        }

        private List<string> GetTableNames()
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'sentinel_%' ORDER BY name;";

            var tables = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
            return tables;
        }

        private List<string> GetColumnNames(string tableName)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";

            var columns = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
            return columns;
        }

        [Fact]
        public void Schema_CreatesExpectedTableCount()
        {
            var tables = GetTableNames();
            Assert.Equal(8, tables.Count);
        }

        [Fact]
        public void Schema_CreatesRequiredTableNames()
        {
            var tables = GetTableNames();
            Assert.Contains("sentinel_actions", tables);
            Assert.Contains("sentinel_bans", tables);
            Assert.Contains("sentinel_groups", tables);
            Assert.Contains("sentinel_group_members", tables);
            Assert.Contains("sentinel_ai_log", tables);
            Assert.Contains("sentinel_baselines", tables);
            Assert.Contains("sentinel_warnings", tables);
        }

        [Fact]
        public void Schema_EnablesWalMode()
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";

            var result = command.ExecuteScalar() as string;
            Assert.Equal("wal", result);
        }

        [Fact]
        public void Schema_ActionsTable_HasPrimaryKeyAndDomainColumns()
        {
            var columns = GetColumnNames("sentinel_actions");
            Assert.Contains("id", columns);
            Assert.Contains("actor_steam_id", columns);
            Assert.Contains("action_type", columns);
            Assert.Contains("timestamp", columns);
            Assert.Contains("success", columns);
        }

        [Fact]
        public void Schema_BansTable_HasPrimaryKeyAndDomainColumns()
        {
            var columns = GetColumnNames("sentinel_bans");
            Assert.Contains("id", columns);
            Assert.Contains("steam_id", columns);
            Assert.Contains("reason", columns);
            Assert.Contains("expires_at", columns);
            Assert.Contains("active", columns);
            Assert.Contains("created_at", columns);
        }

        [Fact]
        public void Schema_GroupsTable_HasPrimaryKeyAndDomainColumns()
        {
            var columns = GetColumnNames("sentinel_groups");
            Assert.Contains("id", columns);
            Assert.Contains("name", columns);
            Assert.Contains("permissions_json", columns);
            Assert.Contains("created_at", columns);
        }

        [Fact]
        public void Schema_GroupMembersTable_HasPrimaryKeyAndDomainColumns()
        {
            var columns = GetColumnNames("sentinel_group_members");
            Assert.Contains("id", columns);
            Assert.Contains("group_id", columns);
            Assert.Contains("steam_id", columns);
            Assert.Contains("added_at", columns);
        }

        [Fact]
        public void Schema_AiLogTable_HasPrimaryKeyAndDomainColumns()
        {
            var columns = GetColumnNames("sentinel_ai_log");
            Assert.Contains("id", columns);
            Assert.Contains("agent_name", columns);
            Assert.Contains("verdict", columns);
            Assert.Contains("timestamp", columns);
            Assert.Contains("cost_usd", columns);
        }

        [Fact]
        public void Schema_BaselinesTable_HasPrimaryKeyAndDomainColumns()
        {
            var columns = GetColumnNames("sentinel_baselines");
            Assert.Contains("id", columns);
            Assert.Contains("steam_id", columns);
            Assert.Contains("metric_name", columns);
            Assert.Contains("mean", columns);
            Assert.Contains("std_dev", columns);
            Assert.Contains("sample_count", columns);
        }

        [Fact]
        public void Schema_WarningsTable_HasPrimaryKeyAndDomainColumns()
        {
            var columns = GetColumnNames("sentinel_warnings");
            Assert.Contains("id", columns);
            Assert.Contains("target_id", columns);
            Assert.Contains("target_name", columns);
            Assert.Contains("warn_count", columns);
            Assert.Contains("last_reason", columns);
            Assert.Contains("last_warned_at", columns);
            Assert.Contains("created_at", columns);
        }

        [Fact]
        public void Schema_IsIdempotent()
        {
            _plugin.CloseDatabase();
            _plugin.InitializeDatabase(_dbPath);

            var tables = GetTableNames();
            Assert.Equal(8, tables.Count);
        }

        [Fact]
        public void Schema_CreatesIndexes()
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'idx_%' ORDER BY name;";

            var indexes = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                indexes.Add(reader.GetString(0));
            }

            Assert.Contains("idx_actions_timestamp", indexes);
            Assert.Contains("idx_bans_steam_id", indexes);
            Assert.Contains("idx_group_members_group", indexes);
            Assert.Contains("idx_baselines_steam_metric", indexes);
            Assert.Contains("idx_warnings_target_id", indexes);
        }
    }
}
