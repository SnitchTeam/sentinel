using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    public class DatabaseConfig
    {
        public string Path { get; set; } = "data/sentinel.db";
        public int BusyTimeoutMs { get; set; } = 5000;
    }

    public class LoggingConfig
    {
        public string Level { get; set; } = "Info";
        public bool LogToConsole { get; set; } = true;
        public bool LogToFile { get; set; } = true;
        public int MaxLogFiles { get; set; } = 7;
    }

    public class AIConfig
    {
        public string Provider { get; set; } = "openai";
        public string Model { get; set; } = "gpt-4o-mini";
        public string Endpoint { get; set; } = "https://api.openai.com/v1";
        public string ApiKey { get; set; } = "";
        public double DailyUsdCap { get; set; } = 5.0;
        public int MaxRetries { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 15;
    }

    public class BansConfig
    {
        public int DefaultDurationMinutes { get; set; } = 1440;
        public bool AllowAppeals { get; set; } = true;
        public string AppealUrl { get; set; } = "";
        public bool BroadcastBans { get; set; } = true;
    }

    public class GroupDefinition
    {
        public string Title { get; set; } = "";
        public List<string> Permissions { get; set; } = new();
    }

    public class GroupsConfig
    {
        public Dictionary<string, GroupDefinition> DefaultGroups { get; set; } = new()
        {
            ["sentinel_admin"] = new GroupDefinition
            {
                Title = "Sentinel Admin",
                Permissions = new List<string> { "sentinel.*" }
            },
            ["sentinel_moderator"] = new GroupDefinition
            {
                Title = "Sentinel Moderator",
                Permissions = new List<string>
                {
                    "sentinel.kick", "sentinel.ban", "sentinel.warn",
                    "sentinel.mute", "sentinel.freeze"
                }
            },
            ["sentinel_trial_mod"] = new GroupDefinition
            {
                Title = "Sentinel Trial Mod",
                Permissions = new List<string> { "sentinel.kick", "sentinel.warn" }
            }
        };
    }

    public class WorldConfig
    {
        public bool PersistOverrides { get; set; } = true;
    }

    public class SentinelConfig
    {
        public DatabaseConfig Database { get; set; } = new();
        public LoggingConfig Logging { get; set; } = new();
        public AIConfig AI { get; set; } = new();
        public BansConfig Bans { get; set; } = new();
        public GroupsConfig Groups { get; set; } = new();
        public WorldConfig World { get; set; } = new();
    }

    public partial class Sentinel
    {
        public SentinelConfig? PluginConfig { get; protected set; }

        public virtual string GetConfigPath()
        {
            return "config/Sentinel.json";
        }

        public override void LoadDefaultConfig()
        {
            var defaultConfig = new SentinelConfig();
            Config?.WriteObject(defaultConfig);
            PluginConfig = defaultConfig;
        }

        public void LoadPluginConfig()
        {
            if (Config == null)
            {
                Config = new Oxide.Core.Plugins.DynamicConfigFile(GetConfigPath());
            }

            if (!System.IO.File.Exists(GetConfigPath()))
            {
                LoadDefaultConfig();
            }
            else
            {
                PluginConfig = Config.ReadObject<SentinelConfig>();
            }
        }
    }
}
