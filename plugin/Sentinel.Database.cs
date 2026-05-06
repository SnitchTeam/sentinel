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
                );",
                @"CREATE TABLE IF NOT EXISTS sentinel_warnings (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    target_id TEXT NOT NULL UNIQUE,
                    target_name TEXT,
                    warn_count INTEGER NOT NULL DEFAULT 1,
                    last_reason TEXT,
                    last_warned_at INTEGER NOT NULL,
                    created_at INTEGER NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS sentinel_rules (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    rule_id TEXT NOT NULL UNIQUE,
                    title TEXT NOT NULL,
                    description TEXT NOT NULL,
                    category TEXT,
                    keywords TEXT,
                    created_at INTEGER NOT NULL
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
                "CREATE INDEX IF NOT EXISTS idx_baselines_steam_metric ON sentinel_baselines(steam_id, metric_name);",
                "CREATE INDEX IF NOT EXISTS idx_warnings_target_id ON sentinel_warnings(target_id);",
                "CREATE INDEX IF NOT EXISTS idx_rules_rule_id ON sentinel_rules(rule_id);",
                "CREATE INDEX IF NOT EXISTS idx_rules_category ON sentinel_rules(category);"
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
            SeedDefaultRules();

            Puts("[Sentinel] Database schema initialized.");
        }

        private void SeedDefaultRules()
        {
            if (_dbConnection == null) return;

            try
            {
                using var checkCmd = _dbConnection.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM sentinel_rules;";
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count > 0) return;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogWarning($"[Sentinel] Rule seed check failed: {ex.Message}");
                return;
            }

            var defaultRules = new[]
            {
                ("§1.1", "No Cheating", "Using third-party software, aimbots, ESP, wallhacks, or any form of cheating is strictly prohibited.", "Gameplay", "cheat,aimbot,esp,wallhack,hack,script,macro,recoil"),
                ("§1.2", "No Exploits", "Abusing game bugs, glitches, or unintended mechanics to gain an unfair advantage is prohibited.", "Gameplay", "exploit,bug,glitch,clip,duplicate,duping,fly,undermesh"),
                ("§1.3", "No Teaming in Solo", "Forming alliances or teaming with other players in solo game modes is not allowed.", "Gameplay", "team,alliance,solo,teamup,help,coop"),
                ("§2.1", "No Toxicity", "Harassment, excessive trash talk, or targeted abuse towards other players is prohibited.", "Behavior", "toxic,harass,abuse,bully,threat,grief"),
                ("§2.2", "No Hate Speech", "Racism, sexism, homophobia, slurs, or any form of hate speech will not be tolerated.", "Behavior", "racism,slur,hate,nazi,homophobic,sexism,discrimination"),
                ("§2.3", "No Doxxing", "Sharing personal information about other players without consent is strictly forbidden.", "Behavior", "dox,private,info,address,phone,leak,personal"),
                ("§3.1", "No Base Griefing", "Intentionally blocking, upgrading, or destroying another player's base without authorization is prohibited.", "Raiding", "grief,block,wall,tc,authorize,tool cupboard,building"),
                ("§3.2", "No Offline Raid Abuse", "Repeatedly offline raiding the same target or using alt accounts to bypass raid limits is not allowed.", "Raiding", "offline,raid,alt,account,repeat,target"),
                ("§4.1", "No Advertising", "Advertising other servers, services, or products in chat or via signs is prohibited.", "Chat", "advert,server,discord,promo,link,url,website"),
                ("§4.2", "No Spam", "Excessive messaging, spamming chat, or using macros to flood communication channels is not allowed.", "Chat", "spam,flood,repeat,macro,chat,message,caps"),
                ("§5.1", "No Alternate Accounts", "Using alternate accounts to evade bans, mutes, or raid limits is strictly prohibited.", "Accounts", "alt,alternate,evade,bypass,smurf,multi,account"),
                ("§5.2", "No Account Sharing", "Sharing your Steam account or allowing others to play on your account is not permitted.", "Accounts", "share,account,steam,give,login,password")
            };

            try
            {
                foreach (var (ruleId, title, description, category, keywords) in defaultRules)
                {
                    using var cmd = _dbConnection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO sentinel_rules (rule_id, title, description, category, keywords, created_at)
                        VALUES (@ruleId, @title, @description, @category, @keywords, @createdAt);";
                    cmd.Parameters.AddWithValue("@ruleId", ruleId);
                    cmd.Parameters.AddWithValue("@title", title);
                    cmd.Parameters.AddWithValue("@description", description);
                    cmd.Parameters.AddWithValue("@category", category);
                    cmd.Parameters.AddWithValue("@keywords", keywords);
                    cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    cmd.ExecuteNonQuery();
                }
                Puts($"[Sentinel] Seeded {defaultRules.Length} default rules.");
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogWarning($"[Sentinel] Rule seeding failed: {ex.Message}");
            }
        }
    }
}
