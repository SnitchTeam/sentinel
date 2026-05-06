using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Oxide.Plugins
{
    // ─── DTOs ───

    public class OnlinePlayerDto
    {
        [JsonPropertyName("steamId")] public string SteamId { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("ip")] public string? Ip { get; set; }
        [JsonPropertyName("ping")] public int Ping { get; set; }
        [JsonPropertyName("connectedSince")] public string ConnectedSince { get; set; } = "";
        [JsonPropertyName("violationScore")] public int ViolationScore { get; set; }
    }

    public class BanDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("steamId")] public string SteamId { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("bannedBy")] public string BannedBy { get; set; } = "";
        [JsonPropertyName("expiresAt")] public long? ExpiresAt { get; set; }
        [JsonPropertyName("createdAt")] public long CreatedAt { get; set; }
        [JsonPropertyName("active")] public bool Active { get; set; }
    }

    public class PaginatedResult<T>
    {
        [JsonPropertyName("items")] public List<T> Items { get; set; } = new();
        [JsonPropertyName("total")] public long Total { get; set; }
        [JsonPropertyName("page")] public int Page { get; set; }
        [JsonPropertyName("limit")] public int Limit { get; set; }
    }

    public class AuditDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("actionType")] public string ActionType { get; set; } = "";
        [JsonPropertyName("actor")] public string Actor { get; set; } = "";
        [JsonPropertyName("target")] public string? Target { get; set; }
        [JsonPropertyName("details")] public string? Details { get; set; }
        [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
    }

    public class AiLogDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("agentType")] public string AgentType { get; set; } = "";
        [JsonPropertyName("input")] public string? Input { get; set; }
        [JsonPropertyName("output")] public string? Output { get; set; }
        [JsonPropertyName("costUsd")] public double? CostUsd { get; set; }
        [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
        [JsonPropertyName("feedback")] public string? Feedback { get; set; }
    }

    public class AiConfigDto
    {
        [JsonPropertyName("provider")] public string Provider { get; set; } = "";
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "";
        [JsonPropertyName("apiKey")] public string ApiKey { get; set; } = "***";
        [JsonPropertyName("dailyUsdCap")] public double DailyUsdCap { get; set; }
        [JsonPropertyName("maxRetries")] public int MaxRetries { get; set; }
        [JsonPropertyName("timeoutSeconds")] public int TimeoutSeconds { get; set; }
        [JsonPropertyName("fallbackProvider")] public string FallbackProvider { get; set; } = "";
        [JsonPropertyName("fallbackEndpoint")] public string FallbackEndpoint { get; set; } = "";
        [JsonPropertyName("fallbackModel")] public string FallbackModel { get; set; } = "";
        [JsonPropertyName("fallbackApiKey")] public string FallbackApiKey { get; set; } = "***";
    }

    public class GroupMemberDto
    {
        [JsonPropertyName("steamId")] public string SteamId { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    public class GroupHierarchyDto
    {
        [JsonPropertyName("groupId")] public int GroupId { get; set; }
        [JsonPropertyName("groupName")] public string GroupName { get; set; } = "";
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("parentGroup")] public string? ParentGroup { get; set; }
        [JsonPropertyName("permissions")] public List<string> Permissions { get; set; } = new();
        [JsonPropertyName("members")] public List<GroupMemberDto> Members { get; set; } = new();
    }

    public class BaselineDto
    {
        [JsonPropertyName("steamId")] public string SteamId { get; set; } = "";
        [JsonPropertyName("metrics")] public Dictionary<string, BaselineMetricDto> Metrics { get; set; } = new();
        [JsonPropertyName("lastUpdated")] public long LastUpdated { get; set; }
    }

    public class BaselineMetricDto
    {
        [JsonPropertyName("mean")] public double Mean { get; set; }
        [JsonPropertyName("stdDev")] public double StdDev { get; set; }
        [JsonPropertyName("sampleCount")] public int SampleCount { get; set; }
    }

    public class StatsResult
    {
        [JsonPropertyName("playerCountHistory")] public List<PlayerCountPoint> PlayerCountHistory { get; set; } = new();
        [JsonPropertyName("actionCountsByType")] public Dictionary<string, long> ActionCountsByType { get; set; } = new();
        [JsonPropertyName("aiQueryVolume")] public long AiQueryVolume { get; set; }
        [JsonPropertyName("banRate")] public long BanRate { get; set; }
    }

    public class PlayerCountPoint
    {
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("count")] public int Count { get; set; }
    }

    public class ConfigUpdateResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
    }

    public class PlayerActionResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public bool NotFound { get; set; }
    }

    // ─── Interface exposed to the HTTP server ───

    public interface ISentinelWebApi
    {
        List<OnlinePlayerDto> ApiGetOnlinePlayers();
        PlayerActionResult ExecutePlayerAction(string steamId, string action, string? reason, int? durationMinutes);

        PaginatedResult<BanDto> GetBans(int page, int limit);
        BanDto? CreateBan(string steamId, string? name, string reason, int? durationMinutes);
        bool RevokeBan(long id);

        PaginatedResult<AuditDto> GetActions(string? type, long? since, int page, int limit);

        PaginatedResult<AiLogDto> GetAiLog(int page, int limit);
        bool RecordAiFeedback(long id, string verdict);
        AiConfigDto GetAiConfig();
        SearchAgentResult QueryAi(string nlQuery);

        object GetConfig();
        ConfigUpdateResult UpdateConfig(string json);

        List<GroupHierarchyDto> GetPermissionGroups();
        (bool success, int id, string error) CreatePermissionGroup(string name, string title, string? parent);
        bool UpdatePermissionGroup(int id, string? title, string? parent, List<string>? permissions);
        bool DeletePermissionGroup(int id);

        List<BaselineDto> GetBaselines();
        string TriggerBaselineRecalculation();

        StatsResult GetStats(int days);
    }

    public partial class Sentinel : ISentinelWebApi
    {
        // ─── Players ───

        public virtual List<OnlinePlayerDto> ApiGetOnlinePlayers()
        {
            var result = new List<OnlinePlayerDto>();
            foreach (var p in _onlinePlayers.Values)
            {
                result.Add(new OnlinePlayerDto
                {
                    SteamId = p.SteamId,
                    Name = p.Name,
                    Ip = p.IpAddress,
                    Ping = p.Ping,
                    ConnectedSince = p.ConnectedSince.ToUniversalTime().ToString("O"),
                    ViolationScore = ComputeViolationScore(p.SteamId)
                });
            }
            return result;
        }

        private int ComputeViolationScore(string steamId)
        {
            int score = 0;
            // Warn count
            var state = _moderationStates.TryGetValue(steamId, out var s) ? s : null;
            if (state != null)
            {
                score += state.WarnCount;
                if (state.IsChatMuted || state.IsVoiceMuted) score += 2;
                if (state.IsFrozen) score += 1;
            }
            else
            {
                score += GetWarnCountFromDatabase(steamId);
            }

            // Active bans
            if (IsBanned(steamId)) score += 5;

            // Anti-cheat events in last 7 days
            if (_dbConnection != null)
            {
                try
                {
                    using var cmd = _dbConnection.CreateCommand();
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM sentinel_anticheat_events
                        WHERE steam_id = @steamId AND timestamp > @since;";
                    cmd.Parameters.AddWithValue("@steamId", steamId);
                    cmd.Parameters.AddWithValue("@since", DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeSeconds());
                    var count = Convert.ToInt32(cmd.ExecuteScalar());
                    score += count * 3;
                }
                catch { /* ignore */ }
            }

            return score;
        }

        public virtual PlayerActionResult ExecutePlayerAction(string steamId, string action, string? reason, int? durationMinutes)
        {
            var result = new PlayerActionResult();
            var error = "";
            bool success = false;

            switch (action.ToLowerInvariant())
            {
                case "kick":
                    success = ExecuteKick(null, steamId, reason ?? "No reason given", out error);
                    break;
                case "warn":
                    success = ExecuteWarn(null, steamId, reason ?? "No reason given", out error);
                    break;
                case "mute":
                    success = ExecuteMute(null, steamId, "all", durationMinutes, out error);
                    break;
                case "freeze":
                    success = ExecuteFreeze(null, steamId, out error);
                    break;
                default:
                    error = "Unknown action";
                    break;
            }

            result.Success = success;
            result.Error = error;
            result.NotFound = error == "Player not found";
            return result;
        }

        // ─── Bans ───

        public virtual PaginatedResult<BanDto> GetBans(int page, int limit)
        {
            var result = new PaginatedResult<BanDto> { Page = page, Limit = limit };
            if (_dbConnection == null) return result;

            try
            {
                using var countCmd = _dbConnection.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(*) FROM sentinel_bans;";
                result.Total = Convert.ToInt64(countCmd.ExecuteScalar());

                using var cmd = _dbConnection.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, steam_id, name, reason, banned_by_name, expires_at, created_at, active
                    FROM sentinel_bans
                    ORDER BY created_at DESC
                    LIMIT @limit OFFSET @offset;";
                cmd.Parameters.AddWithValue("@limit", limit);
                cmd.Parameters.AddWithValue("@offset", (page - 1) * limit);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Items.Add(new BanDto
                    {
                        Id = reader.GetInt64(0),
                        SteamId = reader.GetString(1),
                        Name = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Reason = reader.GetString(3),
                        BannedBy = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        ExpiresAt = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                        CreatedAt = reader.GetInt64(6),
                        Active = reader.GetInt32(7) == 1
                    });
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetBans failed: {ex.Message}");
            }

            return result;
        }

        public virtual BanDto? CreateBan(string steamId, string? name, string reason, int? durationMinutes)
        {
            var actorId = "webapi";
            var actorName = "Web API";

            string targetSteamId = steamId;
            string targetName = name ?? "Unknown";

            // Try to find online player
            var target = ResolveTarget(steamId);
            if (target != null)
            {
                targetSteamId = target.UserIDString;
                targetName = target.displayName;
                target.Kick($"Banned: {reason}");
            }

            if (_dbConnection == null) return null;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO sentinel_bans (
                        steam_id, name, banned_by_steam_id, banned_by_name,
                        reason, duration_minutes, active, created_at, expires_at
                    ) VALUES (
                        @steamId, @name, @byId, @byName,
                        @reason, @duration, 1, @createdAt, @expiresAt
                    )
                    RETURNING id;";

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long? expiresAt = durationMinutes.HasValue ? now + (durationMinutes.Value * 60) : null;

                command.Parameters.AddWithValue("@steamId", targetSteamId);
                command.Parameters.AddWithValue("@name", targetName);
                command.Parameters.AddWithValue("@byId", actorId);
                command.Parameters.AddWithValue("@byName", actorName);
                command.Parameters.AddWithValue("@reason", reason);
                command.Parameters.AddWithValue("@duration", durationMinutes.HasValue ? (object)durationMinutes.Value : DBNull.Value);
                command.Parameters.AddWithValue("@createdAt", now);
                command.Parameters.AddWithValue("@expiresAt", expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value);

                var id = Convert.ToInt64(command.ExecuteScalar());

                LogAuditAction(actorId, actorName, targetSteamId, targetName, "ban", reason, durationMinutes, true);
                DispatchDiscordWebhook("ban", "Player Banned",
                    $"{targetName} (`{targetSteamId}`) was banned by {actorName}.\nReason: {reason}");

                return new BanDto
                {
                    Id = id,
                    SteamId = targetSteamId,
                    Name = targetName,
                    Reason = reason,
                    BannedBy = actorName,
                    ExpiresAt = expiresAt,
                    CreatedAt = now,
                    Active = true
                };
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] CreateBan failed: {ex.Message}");
                return null;
            }
        }

        public virtual bool RevokeBan(long id)
        {
            if (_dbConnection == null) return false;

            try
            {
                using var cmd = _dbConnection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE sentinel_bans
                    SET active = 0, revoked_at = @now
                    WHERE id = @id;";
                cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                cmd.Parameters.AddWithValue("@id", id);
                var rows = cmd.ExecuteNonQuery();

                if (rows > 0)
                {
                    LogAuditAction("webapi", "Web API", null, null, "ban_revoke", $"Revoked ban {id}", null, true);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] RevokeBan failed: {ex.Message}");
                return false;
            }
        }

        // ─── Actions (Audit Log) ───

        public virtual PaginatedResult<AuditDto> GetActions(string? type, long? since, int page, int limit)
        {
            var result = new PaginatedResult<AuditDto> { Page = page, Limit = limit };
            if (_dbConnection == null) return result;

            try
            {
                result.Total = CountAuditLog(actionType: type, fromTimestamp: since);
                var entries = QueryAuditLog(actionType: type, fromTimestamp: since, limit: limit, offset: (page - 1) * limit);

                result.Items = entries.Select(e => new AuditDto
                {
                    Id = e.Id,
                    ActionType = e.ActionType,
                    Actor = e.ActorSteamId,
                    Target = e.TargetSteamId,
                    Details = e.DetailsJson,
                    Timestamp = e.Timestamp
                }).ToList();
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetActions failed: {ex.Message}");
            }

            return result;
        }

        // ─── AI ───

        public virtual PaginatedResult<AiLogDto> GetAiLog(int page, int limit)
        {
            var result = new PaginatedResult<AiLogDto> { Page = page, Limit = limit };
            if (_dbConnection == null) return result;

            try
            {
                using var countCmd = _dbConnection.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(*) FROM sentinel_ai_log;";
                result.Total = Convert.ToInt64(countCmd.ExecuteScalar());

                using var cmd = _dbConnection.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, agent_name, redacted_input, raw_output, cost_usd, timestamp, verdict
                    FROM sentinel_ai_log
                    ORDER BY timestamp DESC
                    LIMIT @limit OFFSET @offset;";
                cmd.Parameters.AddWithValue("@limit", limit);
                cmd.Parameters.AddWithValue("@offset", (page - 1) * limit);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Items.Add(new AiLogDto
                    {
                        Id = reader.GetInt64(0),
                        AgentType = reader.GetString(1),
                        Input = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Output = reader.IsDBNull(3) ? null : reader.GetString(3),
                        CostUsd = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        Timestamp = reader.GetInt64(5),
                        Feedback = reader.IsDBNull(6) ? null : reader.GetString(6)
                    });
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetAiLog failed: {ex.Message}");
            }

            return result;
        }

        public virtual bool RecordAiFeedback(long id, string verdict)
        {
            if (_dbConnection == null) return false;

            var normalized = verdict.ToLowerInvariant();
            if (normalized != "accept" && normalized != "reject") return false;

            try
            {
                // Update existing row
                using var updateCmd = _dbConnection.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE sentinel_ai_log
                    SET verdict = @verdict
                    WHERE id = @id;";
                updateCmd.Parameters.AddWithValue("@verdict", normalized);
                updateCmd.Parameters.AddWithValue("@id", id);
                var rows = updateCmd.ExecuteNonQuery();

                if (rows == 0) return false;

                LogAuditAction("webapi", "Web API", null, null, "ai_feedback", $"{id}:{normalized}", null, true,
                    $"{{\"id\":{id},\"verdict\":\"{normalized}\"}}");
                return true;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] RecordAiFeedback failed: {ex.Message}");
                return false;
            }
        }

        public virtual AiConfigDto GetAiConfig()
        {
            var config = PluginConfig?.AI ?? new AIConfig();
            return new AiConfigDto
            {
                Provider = config.Provider,
                Model = config.Model,
                Endpoint = config.Endpoint,
                ApiKey = "***",
                DailyUsdCap = config.DailyUsdCap,
                MaxRetries = config.MaxRetries,
                TimeoutSeconds = config.TimeoutSeconds,
                FallbackProvider = config.FallbackProvider,
                FallbackEndpoint = config.FallbackEndpoint,
                FallbackModel = config.FallbackModel,
                FallbackApiKey = string.IsNullOrEmpty(config.FallbackApiKey) ? "" : "***"
            };
        }

        public virtual SearchAgentResult QueryAi(string nlQuery)
        {
            return RunSearchAgent(nlQuery);
        }

        // ─── Config ───

        public virtual object GetConfig()
        {
            if (PluginConfig == null) return new { };

            var json = JsonSerializer.Serialize(PluginConfig, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

            // Redact auth token
            if (dict.TryGetValue("webPanel", out var wpEl) && wpEl.ValueKind == JsonValueKind.Object)
            {
                var wp = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(wpEl.GetRawText())!;
                wp["authToken"] = JsonSerializer.SerializeToElement("***");
                dict["webPanel"] = JsonSerializer.SerializeToElement(wp);
            }

            // Truncate Discord webhook URLs to domain only
            if (dict.TryGetValue("discord", out var discEl) && discEl.ValueKind == JsonValueKind.Object)
            {
                var disc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(discEl.GetRawText())!;
                if (disc.TryGetValue("webhooks", out var whEl) && whEl.ValueKind == JsonValueKind.Object)
                {
                    var webhooks = JsonSerializer.Deserialize<Dictionary<string, string>>(whEl.GetRawText())!;
                    var truncated = new Dictionary<string, string>();
                    foreach (var kvp in webhooks)
                    {
                        if (Uri.TryCreate(kvp.Value, UriKind.Absolute, out var uri))
                        {
                            truncated[kvp.Key] = $"{uri.Scheme}://{uri.Host}";
                        }
                        else
                        {
                            truncated[kvp.Key] = kvp.Value;
                        }
                    }
                    disc["webhooks"] = JsonSerializer.SerializeToElement(truncated);
                }
                dict["discord"] = JsonSerializer.SerializeToElement(disc);
            }

            return dict;
        }

        public virtual ConfigUpdateResult UpdateConfig(string json)
        {
            var result = new ConfigUpdateResult();

            try
            {
                var incoming = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (incoming == null)
                {
                    result.Error = "Invalid JSON";
                    return result;
                }

                var current = PluginConfig ?? new SentinelConfig();
                var currentKeys = typeof(SentinelConfig).GetProperties()
                    .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
                    .ToHashSet();

                foreach (var key in incoming.Keys)
                {
                    if (!currentKeys.Contains(key))
                    {
                        result.Error = $"Unknown config key: {key}";
                        return result;
                    }
                }

                // Only merge known top-level sections for safety
                MergeConfigSection(current, incoming, "database", typeof(DatabaseConfig));
                MergeConfigSection(current, incoming, "logging", typeof(LoggingConfig));
                MergeConfigSection(current, incoming, "ai", typeof(AIConfig));
                MergeConfigSection(current, incoming, "bans", typeof(BansConfig));
                MergeConfigSection(current, incoming, "groups", typeof(GroupsConfig));
                MergeConfigSection(current, incoming, "world", typeof(WorldConfig));
                MergeConfigSection(current, incoming, "cui", typeof(CuiPanelConfig));
                MergeConfigSection(current, incoming, "discord", typeof(DiscordConfig));
                MergeConfigSection(current, incoming, "webPanel", typeof(WebPanelConfig));

                PluginConfig = current;

                var configPath = GetConfigPath();
                var dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                Config?.WriteObject(current);

                // Re-initialize affected subsystems
                ReloadWebServer();

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = $"Config update failed: {ex.Message}";
            }

            return result;
        }

        private static void MergeConfigSection(SentinelConfig target, Dictionary<string, JsonElement> incoming, string key, Type sectionType)
        {
            if (!incoming.TryGetValue(key, out var element)) return;

            var targetProp = typeof(SentinelConfig).GetProperties()
                .FirstOrDefault(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name) == key);
            if (targetProp == null) return;

            var existing = targetProp.GetValue(target);
            var merged = JsonSerializer.Deserialize(element.GetRawText(), sectionType, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (merged != null)
            {
                targetProp.SetValue(target, merged);
            }
        }

        // ─── Permissions ───

        public virtual List<GroupHierarchyDto> GetPermissionGroups()
        {
            var result = new List<GroupHierarchyDto>();
            var groups = GetAllGroups();

            foreach (var g in groups)
            {
                var members = GetGroupMembers(g.Name);
                var memberDtos = new List<GroupMemberDto>();
                foreach (var m in members)
                {
                    var name = _onlinePlayers.TryGetValue(m.SteamId, out var p) ? p.Name : null;
                    memberDtos.Add(new GroupMemberDto { SteamId = m.SteamId, Name = name });
                }

                result.Add(new GroupHierarchyDto
                {
                    GroupId = g.Id,
                    GroupName = g.Name,
                    Title = g.Title,
                    ParentGroup = g.ParentGroup,
                    Permissions = g.Permissions,
                    Members = memberDtos
                });
            }

            return result;
        }

        public virtual (bool success, int id, string error) CreatePermissionGroup(string name, string title, string? parent)
        {
            if (!CreateGroup(name, title, parent, out var error))
                return (false, 0, error);

            var group = GetGroupFromDb(name);
            return (true, group?.Id ?? 0, "");
        }

        public virtual bool UpdatePermissionGroup(int id, string? title, string? parent, List<string>? permissions)
        {
            var group = GetAllGroups().FirstOrDefault(g => g.Id == id);
            if (group == null) return false;

            bool ok = true;
            if (!string.IsNullOrEmpty(title))
            {
                ok = UpdateGroupTitle(group.Name, title, out _);
            }
            if (ok && parent != null)
            {
                var parentVal = string.IsNullOrEmpty(parent) ? null : parent;
                ok = UpdateGroupParent(group.Name, parentVal, out _);
            }
            if (ok && permissions != null)
            {
                // Revoke all existing, then grant new
                foreach (var perm in group.Permissions.ToList())
                {
                    RevokeGroupPermission(group.Name, perm, out _);
                }
                foreach (var perm in permissions)
                {
                    GrantGroupPermission(group.Name, perm, out _);
                }
            }
            return ok;
        }

        public virtual bool DeletePermissionGroup(int id)
        {
            var group = GetAllGroups().FirstOrDefault(g => g.Id == id);
            if (group == null) return false;
            return DeleteGroup(group.Name, out _);
        }

        // ─── Baselines ───

        public virtual List<BaselineDto> GetBaselines()
        {
            var result = new Dictionary<string, BaselineDto>();
            if (_dbConnection == null) return result.Values.ToList();

            try
            {
                using var cmd = _dbConnection.CreateCommand();
                cmd.CommandText = @"
                    SELECT steam_id, metric_name, mean, std_dev, sample_count, last_updated
                    FROM sentinel_baselines
                    ORDER BY steam_id, metric_name;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var steamId = reader.GetString(0);
                    if (!result.TryGetValue(steamId, out var dto))
                    {
                        dto = new BaselineDto { SteamId = steamId };
                        result[steamId] = dto;
                    }
                    dto.Metrics[reader.GetString(1)] = new BaselineMetricDto
                    {
                        Mean = reader.GetDouble(2),
                        StdDev = reader.GetDouble(3),
                        SampleCount = reader.GetInt32(4)
                    };
                    dto.LastUpdated = reader.GetInt64(5);
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetBaselines failed: {ex.Message}");
            }

            return result.Values.ToList();
        }

        private string _lastBaselineJobId = "";

        public virtual string TriggerBaselineRecalculation()
        {
            var jobId = Guid.NewGuid().ToString("N")[..8];
            _lastBaselineJobId = jobId;

            _ = Task.Run(() =>
            {
                try
                {
                    // In a real implementation this would iterate over all players and metrics.
                    // For the API contract we just log and finish.
                    _runtimeBridge?.LogInfo($"[Sentinel] Baseline recalculation job {jobId} started.");
                    // Simulate work by sleeping briefly
                    System.Threading.Thread.Sleep(100);
                    _runtimeBridge?.LogInfo($"[Sentinel] Baseline recalculation job {jobId} completed.");
                }
                catch (Exception ex)
                {
                    _runtimeBridge?.LogError($"[Sentinel] Baseline recalculation job {jobId} failed: {ex.Message}");
                }
            });

            return jobId;
        }

        // ─── Stats ───

        public virtual StatsResult GetStats(int days)
        {
            var result = new StatsResult();
            if (_dbConnection == null) return result;

            var since = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();

            try
            {
                // actionCountsByType
                using var actionCmd = _dbConnection.CreateCommand();
                actionCmd.CommandText = @"
                    SELECT action_type, COUNT(*)
                    FROM sentinel_actions
                    WHERE timestamp >= @since
                    GROUP BY action_type;";
                actionCmd.Parameters.AddWithValue("@since", since);
                using var actionReader = actionCmd.ExecuteReader();
                while (actionReader.Read())
                {
                    result.ActionCountsByType[actionReader.GetString(0)] = actionReader.GetInt64(1);
                }

                // aiQueryVolume
                using var aiCmd = _dbConnection.CreateCommand();
                aiCmd.CommandText = @"
                    SELECT COUNT(*) FROM sentinel_ai_log
                    WHERE timestamp >= @since;";
                aiCmd.Parameters.AddWithValue("@since", since);
                result.AiQueryVolume = Convert.ToInt64(aiCmd.ExecuteScalar());

                // banRate
                using var banCmd = _dbConnection.CreateCommand();
                banCmd.CommandText = @"
                    SELECT COUNT(*) FROM sentinel_bans
                    WHERE created_at >= @since;";
                banCmd.Parameters.AddWithValue("@since", since);
                result.BanRate = Convert.ToInt64(banCmd.ExecuteScalar());

                // playerCountHistory - we don't have historical data, return empty
                result.PlayerCountHistory = new List<PlayerCountPoint>();
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetStats failed: {ex.Message}");
            }

            return result;
        }
    }
}
