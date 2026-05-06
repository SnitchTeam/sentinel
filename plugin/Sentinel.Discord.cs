using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    public class DiscordEmbed
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("color")]
        public int Color { get; set; }

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = "";

        [JsonPropertyName("footer")]
        public DiscordEmbedFooter? Footer { get; set; }
    }

    public class DiscordEmbedFooter
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    public class DiscordWebhookPayload
    {
        [JsonPropertyName("embeds")]
        public List<DiscordEmbed> Embeds { get; set; } = new();
    }

    public interface IDiscordWebhookClient
    {
        Task<HttpResponseMessage> PostAsync(string url, HttpContent content);
    }

    public class DefaultDiscordWebhookClient : IDiscordWebhookClient
    {
        private static readonly HttpClient _client = new HttpClient();
        public Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
            => _client.PostAsync(url, content);
    }

    public class DiscordWebhookRouter
    {
        private readonly DiscordConfig _config;
        private readonly IDiscordWebhookClient _client;
        private readonly IRuntimeBridge? _logger;
        private readonly string _version;

        public DiscordWebhookRouter(DiscordConfig config, IDiscordWebhookClient client, IRuntimeBridge? logger, string version)
        {
            _config = config;
            _client = client;
            _logger = logger;
            _version = version;
        }

        public virtual async Task SendAsync(string actionType, DiscordEmbed embed)
        {
            if (!_config.Enabled) return;
            if (!_config.Webhooks.TryGetValue(actionType, out var url)) return;
            if (string.IsNullOrWhiteSpace(url)) return;

            var payload = new DiscordWebhookPayload { Embeds = new List<DiscordEmbed> { embed } };
            var json = JsonSerializer.Serialize(payload);

            try
            {
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await _client.PostAsync(url, content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning($"[Sentinel] Discord webhook {actionType} failed: HTTP {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Sentinel] Discord webhook {actionType} exception: {ex.Message}");
            }
        }

        public virtual DiscordEmbed BuildEmbed(string templateName, string title, string description)
        {
            var color = templateName.ToLowerInvariant() switch
            {
                "ban" => 0xE74C3C,
                "kick" => 0xE67E22,
                "warn" => 0xF1C40F,
                "mute" => 0x9B59B6,
                "ai_alert" => 0xD35400,
                "daily_digest" => 0x3498DB,
                "system" => 0x95A5A6,
                _ => 0x95A5A6
            };

            return new DiscordEmbed
            {
                Title = title,
                Description = description,
                Color = color,
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                Footer = new DiscordEmbedFooter { Text = $"Sentinel v{_version}" }
            };
        }
    }

    public partial class Sentinel
    {
        private DiscordWebhookRouter? _discordRouter;
        private System.Threading.Timer? _digestTimer;
        private DateTime? _lastDigestDate;

        public virtual string GetVersionString()
        {
            var attr = typeof(Sentinel).GetCustomAttribute<InfoAttribute>();
            return attr?.Version ?? "unknown";
        }

        public virtual IDiscordWebhookClient CreateDefaultDiscordWebhookClient()
        {
            return new DefaultDiscordWebhookClient();
        }

        public virtual void InitializeDiscordRouter()
        {
            var config = PluginConfig?.Discord ?? new DiscordConfig();
            _discordRouter = new DiscordWebhookRouter(config, CreateDefaultDiscordWebhookClient(), _runtimeBridge, GetVersionString());

            if (config.Enabled)
            {
                _digestTimer = new System.Threading.Timer(_ => CheckDigestTimer(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
        }

        public virtual void StopDiscordRouter()
        {
            _digestTimer?.Dispose();
            _digestTimer = null;
        }

        public virtual DateTime GetUtcNow() => DateTime.UtcNow;

        public virtual void CheckDigestTimer()
        {
            if (_discordRouter == null) return;
            var config = PluginConfig?.Discord ?? new DiscordConfig();
            if (!config.Enabled) return;

            var now = GetUtcNow();
            var configuredHour = config.DailyDigestHour;

            if (_lastDigestDate.HasValue && _lastDigestDate.Value.Date == now.Date)
                return;

            if (now.Hour >= configuredHour)
            {
                SendDailyDigest();
                _lastDigestDate = now;
            }
        }

        public virtual void SendDailyDigest()
        {
            if (_discordRouter == null) return;
            var config = PluginConfig?.Discord ?? new DiscordConfig();
            if (!config.Enabled) return;
            if (!config.Webhooks.ContainsKey("daily_digest")) return;

            var since = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds();

            var actionCount = CountAuditLog(fromTimestamp: since);
            var banCount = CountBansSince(since);
            var aiCount = CountAiLogSince(since);

            var description = $"**Daily Digest** — Last 24 hours\n" +
                $"• Actions: {actionCount}\n" +
                $"• Bans: {banCount}\n" +
                $"• AI Alerts: {aiCount}";

            var embed = _discordRouter.BuildEmbed("daily_digest", "Sentinel Daily Digest", description);
            _ = _discordRouter.SendAsync("daily_digest", embed);
        }

        public virtual long CountBansSince(long sinceTimestamp)
        {
            if (_dbConnection == null) return 0;
            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sentinel_bans WHERE created_at >= @since;";
                command.Parameters.AddWithValue("@since", sinceTimestamp);
                return Convert.ToInt64(command.ExecuteScalar()!);
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] CountBansSince failed: {ex.Message}");
                return 0;
            }
        }

        public virtual long CountAiLogSince(long sinceTimestamp)
        {
            if (_dbConnection == null) return 0;
            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sentinel_ai_log WHERE timestamp >= @since;";
                command.Parameters.AddWithValue("@since", sinceTimestamp);
                return Convert.ToInt64(command.ExecuteScalar()!);
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] CountAiLogSince failed: {ex.Message}");
                return 0;
            }
        }

        public virtual void DispatchDiscordWebhook(string actionType, string title, string description)
        {
            if (_discordRouter == null) return;
            var config = PluginConfig?.Discord ?? new DiscordConfig();
            if (!config.Enabled) return;
            if (!config.Webhooks.ContainsKey(actionType)) return;

            var templateName = actionType.ToLowerInvariant() switch
            {
                "ban" => "ban",
                "kick" => "kick",
                "warn" => "warn",
                "mute" => "mute",
                "ai_alert" => "ai_alert",
                "daily_digest" => "daily_digest",
                "system" => "system",
                _ => "system"
            };

            var embed = _discordRouter.BuildEmbed(templateName, title, description);
            _ = _discordRouter.SendAsync(actionType, embed);
        }
    }
}
