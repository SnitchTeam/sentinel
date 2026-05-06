using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelDiscordTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;
        private readonly List<TestPlayer> _localPlayers = new();
        private readonly MockDiscordWebhookClient _mockDiscordClient;

        public SentinelDiscordTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_discord_test_{Guid.NewGuid()}.db");
            _plugin = new TestableSentinel();
            _plugin.LocalPlayers = _localPlayers;
            _mockPermission = new MockPermission();
            _plugin.permission = _mockPermission;
            _mockDiscordClient = new MockDiscordWebhookClient();

            _plugin.SetPluginConfig(new SentinelConfig
            {
                Discord = new DiscordConfig
                {
                    Enabled = true,
                    Webhooks = new Dictionary<string, string>
                    {
                        ["ban"] = "https://discord.com/api/webhooks/ban",
                        ["kick"] = "https://discord.com/api/webhooks/kick",
                        ["warn"] = "https://discord.com/api/webhooks/warn",
                        ["mute"] = "https://discord.com/api/webhooks/mute",
                        ["ai_alert"] = "https://discord.com/api/webhooks/ai",
                        ["daily_digest"] = "https://discord.com/api/webhooks/digest",
                        ["system"] = "https://discord.com/api/webhooks/system"
                    },
                    DailyDigestHour = 8
                }
            });

            _plugin.InitializeRuntimeBridge();
            _plugin.InitializeDatabase(_dbPath);
            _plugin.SetDiscordClient(_mockDiscordClient);
            _plugin.InitializeDiscordRouter();
        }

        public void Dispose()
        {
            _plugin.StopDiscordRouter();
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

        // ---------------------------------------------------------
        // Mock helpers
        // ---------------------------------------------------------
        private class MockDiscordWebhookClient : IDiscordWebhookClient
        {
            public List<(string Url, string Content)> Requests { get; } = new();

            public Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
            {
                var json = "";
                if (content is StringContent sc)
                {
                    json = sc.ReadAsStringAsync().GetAwaiter().GetResult();
                }
                Requests.Add((url, json));
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
            }
        }

        private class TestableSentinel : SentinelPlugin
        {
            public List<TestPlayer> LocalPlayers { get; set; } = new();
            public List<string> Logs { get; } = new();
            public override void Puts(string message) => Logs.Add(message);
            public override void PrintWarning(string message) => Logs.Add($"[WARN] {message}");
            public override void PrintError(string message) => Logs.Add($"[ERROR] {message}");

            public MockDiscordWebhookClient? DiscordClient { get; set; }

            public void SetPluginConfig(SentinelConfig config)
            {
                PluginConfig = config;
            }

            public void SetDiscordClient(MockDiscordWebhookClient client)
            {
                DiscordClient = client;
            }

            public override IDiscordWebhookClient CreateDefaultDiscordWebhookClient()
            {
                return DiscordClient ?? new MockDiscordWebhookClient();
            }

            private DateTime _utcNow = DateTime.UtcNow;
            public override DateTime GetUtcNow() => _utcNow;
            public void SetUtcNow(DateTime value) => _utcNow = value;

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
            public override void Kick(string reason)
            {
                WasKicked = true;
                LastKickReason = reason;
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
        }

        // ---------------------------------------------------------
        // VAL-DISCORD-001: Webhook Per-Action Routing
        // ---------------------------------------------------------

        [Fact]
        public void Router_BanMapped_CallsBanUrl()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.ban");

            _plugin.ExecuteBan(admin, "Target", "Cheating", 1440, out var error);

            Assert.True(string.IsNullOrEmpty(error));
            Assert.Single(_mockDiscordClient.Requests);
            Assert.Equal("https://discord.com/api/webhooks/ban", _mockDiscordClient.Requests[0].Url);
        }

        [Fact]
        public void Router_KickMapped_CallsKickUrl()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.kick");

            _plugin.ExecuteKick(admin, "Target", "Toxicity", out var error);

            Assert.True(string.IsNullOrEmpty(error));
            Assert.Single(_mockDiscordClient.Requests);
            Assert.Equal("https://discord.com/api/webhooks/kick", _mockDiscordClient.Requests[0].Url);
        }

        [Fact]
        public void Router_WarnMapped_CallsWarnUrl()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.warn");

            _plugin.ExecuteWarn(admin, "Target", "Rule break", out var error);

            Assert.True(string.IsNullOrEmpty(error));
            Assert.Single(_mockDiscordClient.Requests);
            Assert.Equal("https://discord.com/api/webhooks/warn", _mockDiscordClient.Requests[0].Url);
        }

        [Fact]
        public void Router_MuteMapped_CallsMuteUrl()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.mute");

            _plugin.ExecuteMute(admin, "Target", "chat", 60, out var error);

            Assert.True(string.IsNullOrEmpty(error));
            Assert.Single(_mockDiscordClient.Requests);
            Assert.Equal("https://discord.com/api/webhooks/mute", _mockDiscordClient.Requests[0].Url);
        }

        [Fact]
        public void Router_UnmappedAction_ZeroRequests()
        {
            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.freeze");

            _plugin.ExecuteFreeze(admin, "Target", out var error);

            Assert.True(string.IsNullOrEmpty(error));
            Assert.Empty(_mockDiscordClient.Requests);
        }

        [Fact]
        public void Router_AiAlertMapped_CallsAiAlertUrl()
        {
            var suggestion = new AiSuggestion
            {
                PlayerName = "Hacker",
                SteamId = "76561190000000003",
                Behavior = "aimbot",
                Confidence = 95,
                RecommendedAction = "ban",
                Reason = "Aimbot detected",
                AgentName = "AntiCheat"
            };

            _plugin.AddAiSuggestion(suggestion);

            Assert.Single(_mockDiscordClient.Requests);
            Assert.Equal("https://discord.com/api/webhooks/ai", _mockDiscordClient.Requests[0].Url);
        }

        [Fact]
        public void Router_Disabled_ZeroRequests()
        {
            _plugin.SetPluginConfig(new SentinelConfig
            {
                Discord = new DiscordConfig
                {
                    Enabled = false,
                    Webhooks = new Dictionary<string, string> { ["kick"] = "https://discord.com/api/webhooks/kick" }
                }
            });
            _plugin.InitializeDiscordRouter();

            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.kick");

            _plugin.ExecuteKick(admin, "Target", "Test", out _);

            Assert.Empty(_mockDiscordClient.Requests);
        }

        [Fact]
        public void Router_EmptyUrl_ZeroRequests()
        {
            _plugin.SetPluginConfig(new SentinelConfig
            {
                Discord = new DiscordConfig
                {
                    Enabled = true,
                    Webhooks = new Dictionary<string, string> { ["kick"] = "" }
                }
            });
            _plugin.InitializeDiscordRouter();

            var admin = CreatePlayer(76561190000000001, "Admin");
            var target = CreatePlayer(76561190000000002, "Target");
            _mockPermission.Grant(admin.UserIDString, "sentinel.kick");

            _plugin.ExecuteKick(admin, "Target", "Test", out _);

            Assert.Empty(_mockDiscordClient.Requests);
        }

        // ---------------------------------------------------------
        // VAL-DISCORD-002: Embed Templates Render Correctly
        // ---------------------------------------------------------

        [Theory]
        [InlineData("ban", 0xE74C3C)]
        [InlineData("kick", 0xE67E22)]
        [InlineData("warn", 0xF1C40F)]
        [InlineData("mute", 0x9B59B6)]
        [InlineData("ai_alert", 0xD35400)]
        [InlineData("daily_digest", 0x3498DB)]
        [InlineData("system", 0x95A5A6)]
        public void EmbedTemplate_HasCorrectColor(string templateName, int expectedColor)
        {
            var router = new DiscordWebhookRouter(
                new DiscordConfig(),
                new MockDiscordWebhookClient(),
                null,
                "1.0.0"
            );

            var embed = router.BuildEmbed(templateName, "Title", "Desc");

            Assert.Equal(expectedColor, embed.Color);
            Assert.Equal("Title", embed.Title);
            Assert.Equal("Desc", embed.Description);
            Assert.NotNull(embed.Footer);
            Assert.Contains("Sentinel v1.0.0", embed.Footer!.Text);
            Assert.False(string.IsNullOrEmpty(embed.Timestamp));
        }

        [Fact]
        public void EmbedTemplate_Ban_ProducesValidJson()
        {
            var router = new DiscordWebhookRouter(
                new DiscordConfig(),
                new MockDiscordWebhookClient(),
                null,
                "1.0.0"
            );

            var embed = router.BuildEmbed("ban", "Player Banned", "TestPlayer was banned");
            var payload = new DiscordWebhookPayload { Embeds = new List<DiscordEmbed> { embed } };
            var json = JsonSerializer.Serialize(payload);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("embeds", out var embeds));
            Assert.Equal(1, embeds.GetArrayLength());
            var first = embeds[0];
            Assert.Equal("Player Banned", first.GetProperty("title").GetString());
            Assert.Equal("TestPlayer was banned", first.GetProperty("description").GetString());
            Assert.Equal(0xE74C3C, first.GetProperty("color").GetInt32());
            Assert.True(first.TryGetProperty("timestamp", out _));
            Assert.True(first.TryGetProperty("footer", out var footer));
            Assert.True(footer.TryGetProperty("text", out var text));
            Assert.Contains("Sentinel v1.0.0", text.GetString());
        }

        [Fact]
        public void EmbedTemplate_AllSeven_HaveDistinctColors()
        {
            var router = new DiscordWebhookRouter(
                new DiscordConfig(),
                new MockDiscordWebhookClient(),
                null,
                "1.0.0"
            );

            var colors = new List<int>();
            foreach (var template in new[] { "ban", "kick", "warn", "mute", "ai_alert", "daily_digest", "system" })
            {
                var embed = router.BuildEmbed(template, "T", "D");
                colors.Add(embed.Color);
            }

            Assert.Equal(7, colors.Distinct().Count());
        }

        [Fact]
        public void EmbedTemplate_UnknownTemplate_FallsBackToSystemColor()
        {
            var router = new DiscordWebhookRouter(
                new DiscordConfig(),
                new MockDiscordWebhookClient(),
                null,
                "1.0.0"
            );

            var embed = router.BuildEmbed("unknown", "T", "D");
            Assert.Equal(0x95A5A6, embed.Color);
        }

        // ---------------------------------------------------------
        // VAL-DISCORD-003: Daily Digest Cron Configurable Hour
        // ---------------------------------------------------------

        [Fact]
        public void Digest_AtConfiguredHour_FiresOnce()
        {
            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();
            Assert.Single(_mockDiscordClient.Requests);
        }

        [Fact]
        public void Digest_BeforeConfiguredHour_DoesNotFire()
        {
            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 7, 59, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();
            Assert.Empty(_mockDiscordClient.Requests);
        }

        [Fact]
        public void Digest_AfterFireSameDay_DoesNotFireAgain()
        {
            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();
            Assert.Single(_mockDiscordClient.Requests);

            _mockDiscordClient.Requests.Clear();
            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 8, 1, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();
            Assert.Empty(_mockDiscordClient.Requests);
        }

        [Fact]
        public void Digest_NextDay_FiresAgain()
        {
            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();
            Assert.Single(_mockDiscordClient.Requests);

            _mockDiscordClient.Requests.Clear();
            _plugin.SetUtcNow(new DateTime(2026, 5, 7, 8, 0, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();
            Assert.Single(_mockDiscordClient.Requests);
        }

        [Fact]
        public void Digest_ConfigurableHour()
        {
            _plugin.SetPluginConfig(new SentinelConfig
            {
                Discord = new DiscordConfig
                {
                    Enabled = true,
                    Webhooks = new Dictionary<string, string> { ["daily_digest"] = "https://discord.com/api/webhooks/digest" },
                    DailyDigestHour = 14
                }
            });
            _plugin.InitializeDiscordRouter();

            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 13, 0, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();
            Assert.Empty(_mockDiscordClient.Requests);

            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 14, 0, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();
            Assert.Single(_mockDiscordClient.Requests);
        }

        [Fact]
        public void Digest_AggregatesLast24Hours()
        {
            var since = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var cmd1 = connection.CreateCommand();
            cmd1.CommandText = "INSERT INTO sentinel_actions (actor_steam_id, action_type, timestamp, success) VALUES ('1', 'kick', @ts, 1);";
            cmd1.Parameters.AddWithValue("@ts", since + 1);
            cmd1.ExecuteNonQuery();

            using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = "INSERT INTO sentinel_bans (steam_id, name, banned_by_steam_id, banned_by_name, reason, active, created_at) VALUES ('2', 'Player', '1', 'Admin', 'test', 1, @ts);";
            cmd2.Parameters.AddWithValue("@ts", since + 1);
            cmd2.ExecuteNonQuery();

            using var cmd3 = connection.CreateCommand();
            cmd3.CommandText = "INSERT INTO sentinel_ai_log (agent_name, request_id, duration_ms, timestamp) VALUES ('Triage', 'r1', 100, @ts);";
            cmd3.Parameters.AddWithValue("@ts", since + 1);
            cmd3.ExecuteNonQuery();

            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();

            Assert.Single(_mockDiscordClient.Requests);
            var content = _mockDiscordClient.Requests[0].Content;
            Assert.Contains("Actions: 1", content);
            Assert.Contains("Bans: 1", content);
            Assert.Contains("AI Alerts: 1", content);
        }

        [Fact]
        public void Digest_NoMapping_ZeroRequests()
        {
            _plugin.SetPluginConfig(new SentinelConfig
            {
                Discord = new DiscordConfig
                {
                    Enabled = true,
                    Webhooks = new Dictionary<string, string>(),
                    DailyDigestHour = 8
                }
            });
            _plugin.InitializeDiscordRouter();

            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();

            Assert.Empty(_mockDiscordClient.Requests);
        }

        [Fact]
        public void Digest_Disabled_ZeroRequests()
        {
            _plugin.SetPluginConfig(new SentinelConfig
            {
                Discord = new DiscordConfig
                {
                    Enabled = false,
                    Webhooks = new Dictionary<string, string> { ["daily_digest"] = "https://discord.com/api/webhooks/digest" },
                    DailyDigestHour = 8
                }
            });
            _plugin.InitializeDiscordRouter();

            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();

            Assert.Empty(_mockDiscordClient.Requests);
        }

        [Fact]
        public void Digest_ExcludesOldData()
        {
            var oldTs = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeSeconds();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var cmd1 = connection.CreateCommand();
            cmd1.CommandText = "INSERT INTO sentinel_actions (actor_steam_id, action_type, timestamp, success) VALUES ('1', 'kick', @ts, 1);";
            cmd1.Parameters.AddWithValue("@ts", oldTs);
            cmd1.ExecuteNonQuery();

            _plugin.SetUtcNow(new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc));
            _plugin.CheckDigestTimer();

            Assert.Single(_mockDiscordClient.Requests);
            var content = _mockDiscordClient.Requests[0].Content;
            Assert.Contains("Actions: 0", content);
        }

        // ---------------------------------------------------------
        // System event dispatch
        // ---------------------------------------------------------

        [Fact]
        public void System_BootBanner_Dispatches()
        {
            _plugin.EmitBootBanner();
            Assert.Single(_mockDiscordClient.Requests);
            Assert.Equal("https://discord.com/api/webhooks/system", _mockDiscordClient.Requests[0].Url);
            Assert.Contains("Sentinel v1.0.0", _mockDiscordClient.Requests[0].Content);
        }
    }
}
