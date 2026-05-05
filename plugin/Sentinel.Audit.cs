using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Oxide.Core;

namespace Oxide.Plugins
{
    public class AuditLogEntry
    {
        public long Id { get; set; }
        public string ActorSteamId { get; set; } = "";
        public string? ActorName { get; set; }
        public string? TargetSteamId { get; set; }
        public string? TargetName { get; set; }
        public string ActionType { get; set; } = "";
        public string? Reason { get; set; }
        public int? DurationMinutes { get; set; }
        public string? DetailsJson { get; set; }
        public long Timestamp { get; set; }
        public bool Success { get; set; }
    }

    public partial class Sentinel
    {
        public void LogAuditAction(
            string actorSteamId,
            string? actorName,
            string? targetSteamId,
            string? targetName,
            string actionType,
            string? reason,
            int? durationMinutes,
            bool success,
            string? detailsJson = null)
        {
            if (_dbConnection == null) return;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO sentinel_actions (
                        actor_steam_id, actor_name, target_steam_id, target_name,
                        action_type, reason, duration_minutes, details_json, timestamp, success
                    ) VALUES (
                        @actorId, @actorName, @targetId, @targetName,
                        @actionType, @reason, @duration, @details, @timestamp, @success
                    );";

                command.Parameters.AddWithValue("@actorId", actorSteamId);
                command.Parameters.AddWithValue("@actorName", actorName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@targetId", targetSteamId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@targetName", targetName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@actionType", actionType);
                command.Parameters.AddWithValue("@reason", reason ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@duration", durationMinutes.HasValue ? (object)durationMinutes.Value : DBNull.Value);
                command.Parameters.AddWithValue("@details", detailsJson ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                command.Parameters.AddWithValue("@success", success ? 1 : 0);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] Audit log failed: {ex.Message}");
            }
        }

        public List<AuditLogEntry> QueryAuditLog(
            long? fromTimestamp = null,
            long? toTimestamp = null,
            string? actorSteamId = null,
            string? targetSteamId = null,
            string? actionType = null,
            int? limit = null,
            int? offset = null)
        {
            var results = new List<AuditLogEntry>();
            if (_dbConnection == null) return results;

            try
            {
                var conditions = new List<string>();
                if (fromTimestamp.HasValue) conditions.Add("timestamp >= @from");
                if (toTimestamp.HasValue) conditions.Add("timestamp <= @to");
                if (!string.IsNullOrEmpty(actorSteamId)) conditions.Add("actor_steam_id = @actor");
                if (!string.IsNullOrEmpty(targetSteamId)) conditions.Add("target_steam_id = @target");
                if (!string.IsNullOrEmpty(actionType)) conditions.Add("action_type = @type");

                var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
                var limitClause = limit.HasValue ? "LIMIT @limit" : "";
                var offsetClause = offset.HasValue ? "OFFSET @offset" : "";

                using var command = _dbConnection.CreateCommand();
                command.CommandText = $@"
                    SELECT id, actor_steam_id, actor_name, target_steam_id, target_name,
                           action_type, reason, duration_minutes, details_json, timestamp, success
                    FROM sentinel_actions
                    {whereClause}
                    ORDER BY timestamp DESC
                    {limitClause} {offsetClause};";

                if (fromTimestamp.HasValue) command.Parameters.AddWithValue("@from", fromTimestamp.Value);
                if (toTimestamp.HasValue) command.Parameters.AddWithValue("@to", toTimestamp.Value);
                if (!string.IsNullOrEmpty(actorSteamId)) command.Parameters.AddWithValue("@actor", actorSteamId);
                if (!string.IsNullOrEmpty(targetSteamId)) command.Parameters.AddWithValue("@target", targetSteamId);
                if (!string.IsNullOrEmpty(actionType)) command.Parameters.AddWithValue("@type", actionType);
                if (limit.HasValue) command.Parameters.AddWithValue("@limit", limit.Value);
                if (offset.HasValue) command.Parameters.AddWithValue("@offset", offset.Value);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(ReadAuditLogEntry(reader));
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] Audit query failed: {ex.Message}");
            }

            return results;
        }

        public long CountAuditLog(
            long? fromTimestamp = null,
            long? toTimestamp = null,
            string? actorSteamId = null,
            string? targetSteamId = null,
            string? actionType = null)
        {
            if (_dbConnection == null) return 0;

            try
            {
                var conditions = new List<string>();
                if (fromTimestamp.HasValue) conditions.Add("timestamp >= @from");
                if (toTimestamp.HasValue) conditions.Add("timestamp <= @to");
                if (!string.IsNullOrEmpty(actorSteamId)) conditions.Add("actor_steam_id = @actor");
                if (!string.IsNullOrEmpty(targetSteamId)) conditions.Add("target_steam_id = @target");
                if (!string.IsNullOrEmpty(actionType)) conditions.Add("action_type = @type");

                var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

                using var command = _dbConnection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM sentinel_actions {whereClause};";

                if (fromTimestamp.HasValue) command.Parameters.AddWithValue("@from", fromTimestamp.Value);
                if (toTimestamp.HasValue) command.Parameters.AddWithValue("@to", toTimestamp.Value);
                if (!string.IsNullOrEmpty(actorSteamId)) command.Parameters.AddWithValue("@actor", actorSteamId);
                if (!string.IsNullOrEmpty(targetSteamId)) command.Parameters.AddWithValue("@target", targetSteamId);
                if (!string.IsNullOrEmpty(actionType)) command.Parameters.AddWithValue("@type", actionType);

                return Convert.ToInt64(command.ExecuteScalar()!);
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] Audit count failed: {ex.Message}");
                return 0;
            }
        }

        private static AuditLogEntry ReadAuditLogEntry(SqliteDataReader reader)
        {
            return new AuditLogEntry
            {
                Id = reader.GetInt64(0),
                ActorSteamId = reader.GetString(1),
                ActorName = reader.IsDBNull(2) ? null : reader.GetString(2),
                TargetSteamId = reader.IsDBNull(3) ? null : reader.GetString(3),
                TargetName = reader.IsDBNull(4) ? null : reader.GetString(4),
                ActionType = reader.GetString(5),
                Reason = reader.IsDBNull(6) ? null : reader.GetString(6),
                DurationMinutes = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                DetailsJson = reader.IsDBNull(8) ? null : reader.GetString(8),
                Timestamp = reader.GetInt64(9),
                Success = reader.GetInt32(10) == 1
            };
        }

        public bool ExecuteAuditQuery(
            BasePlayer? admin,
            long? fromTimestamp,
            long? toTimestamp,
            string? actorSteamId,
            string? targetSteamId,
            string? actionType,
            out List<AuditLogEntry>? entries,
            out long total)
        {
            entries = null;
            total = 0;
            var callerId = admin?.UserIDString ?? "console";
            var callerName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.audit"))
            {
                LogAuditAction(callerId, callerName, null, null, "audit_query", null, null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            entries = QueryAuditLog(fromTimestamp: fromTimestamp, toTimestamp: toTimestamp, actorSteamId: actorSteamId, targetSteamId: targetSteamId, actionType: actionType, limit: 100);
            total = CountAuditLog(fromTimestamp: fromTimestamp, toTimestamp: toTimestamp, actorSteamId: actorSteamId, targetSteamId: targetSteamId, actionType: actionType);

            LogAuditAction(callerId, callerName, null, null, "audit_query", null, null, true,
                $"{{\"filters\":\"from={fromTimestamp},to={toTimestamp},actor={actorSteamId},target={targetSteamId},type={actionType}\",\"returned\":{entries.Count},\"total\":{total}}}");
            return true;
        }

        [ConsoleCommand("sentinel.audit.query")]
        void CCmdAuditQuery(ConsoleSystem.Arg arg)
        {
            var admin = arg.Player();

            long? fromTs = null;
            long? toTs = null;
            string? filterActor = null;
            string? filterTarget = null;
            string? filterType = null;

            if (arg.Args != null)
            {
                for (int i = 0; i < arg.Args.Length; i++)
                {
                    var key = arg.Args[i].ToLowerInvariant();
                    switch (key)
                    {
                        case "--from":
                            if (i + 1 < arg.Args.Length && long.TryParse(arg.Args[i + 1], out var fromVal))
                            {
                                fromTs = fromVal;
                                i++;
                            }
                            break;
                        case "--to":
                            if (i + 1 < arg.Args.Length && long.TryParse(arg.Args[i + 1], out var toVal))
                            {
                                toTs = toVal;
                                i++;
                            }
                            break;
                        case "--actor":
                            if (i + 1 < arg.Args.Length)
                            {
                                filterActor = arg.Args[i + 1];
                                i++;
                            }
                            break;
                        case "--target":
                            if (i + 1 < arg.Args.Length)
                            {
                                filterTarget = arg.Args[i + 1];
                                i++;
                            }
                            break;
                        case "--type":
                            if (i + 1 < arg.Args.Length)
                            {
                                filterType = arg.Args[i + 1];
                                i++;
                            }
                            break;
                    }
                }
            }

            if (!ExecuteAuditQuery(admin, fromTs, toTs, filterActor, filterTarget, filterType, out var entries, out var total))
            {
                Puts("[Sentinel] You don't have permission to query the audit log.");
                return;
            }

            Puts($"[Sentinel] Audit query returned {entries?.Count ?? 0} of {total} rows:");
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    var ts = DateTimeOffset.FromUnixTimeSeconds(entry.Timestamp).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                    var target = entry.TargetSteamId != null ? $" -> {entry.TargetSteamId}" : "";
                    var reason = entry.Reason != null ? $" | {entry.Reason}" : "";
                    Puts($"  [{ts}] {entry.ActionType} | {entry.ActorSteamId}{target}{reason} | success={entry.Success}");
                }
            }
        }
    }
}
