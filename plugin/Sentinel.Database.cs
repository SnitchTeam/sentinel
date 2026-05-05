using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace Oxide.Plugins
{
    public partial class Sentinel
    {
        private SqliteConnection? _dbConnection;

        public string GetDatabasePath()
        {
            return Path.Combine("data", "sentinel.db");
        }

        public void InitializeDatabase(string dbPath)
        {
            CloseDatabase();

            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _dbConnection = new SqliteConnection($"Data Source={dbPath}");
            _dbConnection.Open();
            EnableWalMode();
            CreateSchema();
        }

        public SqliteConnection? GetDbConnection() => _dbConnection;

        public void CloseDatabase()
        {
            if (_dbConnection != null)
            {
                _dbConnection.Close();
                _dbConnection.Dispose();
                _dbConnection = null;
            }
        }

        private void EnableWalMode()
        {
            using var command = _dbConnection!.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = WAL;";
            var result = command.ExecuteScalar()?.ToString();
            Puts($"[Sentinel] SQLite journal mode: {result}");
        }

        private bool ColumnExists(string tableName, string columnName)
        {
            using var command = _dbConnection!.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1) == columnName) return true;
            }
            return false;
        }

        private void CreateSchema()
        {
            var tables = new[]
            {
                @"CREATE TABLE IF NOT EXISTS sentinel_actions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    actor_steam_id TEXT NOT NULL,
                    actor_name TEXT,
                    target_steam_id TEXT,
                    target_name TEXT,
                    action_type TEXT NOT NULL,
                    reason TEXT,
                    duration_minutes INTEGER,
                    details_json TEXT,
                    timestamp INTEGER NOT NULL,
                    success INTEGER NOT NULL DEFAULT 1
                );",
                @"CREATE TABLE IF NOT EXISTS sentinel_bans (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    steam_id TEXT NOT NULL,
                    name TEXT,
                    banned_by_steam_id TEXT NOT NULL,
                    banned_by_name TEXT,
                    reason TEXT NOT NULL,
                    ai_draft INTEGER NOT NULL DEFAULT 0,
                    duration_minutes INTEGER,
                    active INTEGER NOT NULL DEFAULT 1,
                    created_at INTEGER NOT NULL,
                    expires_at INTEGER,
                    revoked_at INTEGER
                );",
                @"CREATE TABLE IF NOT EXISTS sentinel_groups (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    title TEXT,
                    permissions_json TEXT,
                    parent_group TEXT,
                    created_at INTEGER NOT NULL,
                    system_protected INTEGER NOT NULL DEFAULT 0
                );",
                @"CREATE TABLE IF NOT EXISTS sentinel_group_members (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    group_id INTEGER NOT NULL,
                    steam_id TEXT NOT NULL,
                    added_at INTEGER NOT NULL,
                    FOREIGN KEY (group_id) REFERENCES sentinel_groups(id)
                );",
                @"CREATE TABLE IF NOT EXISTS sentinel_ai_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    agent_name TEXT NOT NULL,
                    request_id TEXT,
                    prompt_hash TEXT,
                    redacted_input TEXT,
                    raw_output TEXT,
                    verdict TEXT,
                    admin_steam_id TEXT,
                    edit_diff TEXT,
                    duration_ms INTEGER,
                    cost_usd REAL,
                    timestamp INTEGER NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS sentinel_baselines (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    steam_id TEXT NOT NULL,
                    metric_name TEXT NOT NULL,
                    mean REAL NOT NULL,
                    std_dev REAL NOT NULL,
                    sample_count INTEGER NOT NULL,
                    last_updated INTEGER NOT NULL
                );"
            };

            foreach (var sql in tables)
            {
                using var command = _dbConnection!.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            var indexes = new[]
            {
                "CREATE INDEX IF NOT EXISTS idx_actions_timestamp ON sentinel_actions(timestamp);",
                "CREATE INDEX IF NOT EXISTS idx_actions_actor ON sentinel_actions(actor_steam_id);",
                "CREATE INDEX IF NOT EXISTS idx_actions_target ON sentinel_actions(target_steam_id);",
                "CREATE INDEX IF NOT EXISTS idx_bans_steam_id ON sentinel_bans(steam_id);",
                "CREATE INDEX IF NOT EXISTS idx_bans_active ON sentinel_bans(active);",
                "CREATE INDEX IF NOT EXISTS idx_group_members_group ON sentinel_group_members(group_id);",
                "CREATE INDEX IF NOT EXISTS idx_group_members_steam ON sentinel_group_members(steam_id);",
                "CREATE INDEX IF NOT EXISTS idx_ai_log_timestamp ON sentinel_ai_log(timestamp);",
                "CREATE INDEX IF NOT EXISTS idx_baselines_steam_metric ON sentinel_baselines(steam_id, metric_name);"
            };

            foreach (var sql in indexes)
            {
                using var command = _dbConnection!.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            // Migrate: add system_protected column to existing databases
            if (!ColumnExists("sentinel_groups", "system_protected"))
            {
                try
                {
                    using var command = _dbConnection!.CreateCommand();
                    command.CommandText = "ALTER TABLE sentinel_groups ADD COLUMN system_protected INTEGER NOT NULL DEFAULT 0;";
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    _runtimeBridge?.LogWarning($"[Sentinel] Migration warning: {ex.Message}");
                }
            }

            // Migrate: add duration_minutes and reason columns to sentinel_actions
            if (!ColumnExists("sentinel_actions", "duration_minutes"))
            {
                try
                {
                    using var command = _dbConnection!.CreateCommand();
                    command.CommandText = "ALTER TABLE sentinel_actions ADD COLUMN duration_minutes INTEGER;";
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    _runtimeBridge?.LogWarning($"[Sentinel] Migration warning: {ex.Message}");
                }
            }

            if (!ColumnExists("sentinel_actions", "reason"))
            {
                try
                {
                    using var command = _dbConnection!.CreateCommand();
                    command.CommandText = "ALTER TABLE sentinel_actions ADD COLUMN reason TEXT;";
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    _runtimeBridge?.LogWarning($"[Sentinel] Migration warning: {ex.Message}");
                }
            }

            CreateWorldStateSchema();

            Puts("[Sentinel] Database schema initialized.");
        }
    }
}
