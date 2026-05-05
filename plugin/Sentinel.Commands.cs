using System;
using System.Text.Json;
using Oxide.Core;

namespace Oxide.Plugins
{
    public partial class Sentinel
    {
        // ---------------------------------------------------------------
        // Kick
        // ---------------------------------------------------------------
        public bool ExecuteKick(BasePlayer? admin, string targetIdentifier, string reason, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.kick"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "kick", reason, null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var target = ResolveTarget(targetIdentifier);
            if (target == null)
            {
                error = "Player not found";
                LogAuditAction(actorId, actorName, null, null, "kick", reason, null, false);
                return false;
            }

            target.Kick(reason);
            LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "kick", reason, null, true);
            return true;
        }

        [ConsoleCommand("sentinel.kick")]
        void CCmdKick(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.kick \u003cplayer\u003e \"[reason]\"");
                return;
            }

            var targetId = arg.Args[0];
            var reason = arg.Args.Length > 1 ? arg.Args[1] : "No reason given";
            var admin = arg.Player();

            if (!ExecuteKick(admin, targetId, reason, out var error))
            {
                Puts($"[Sentinel] Kick failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Kicked {targetId}: {reason}");
            }
        }

        // ---------------------------------------------------------------
        // Ban
        // ---------------------------------------------------------------
        public bool ExecuteBan(BasePlayer? admin, string targetIdentifier, string reason, int? durationMinutes, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.ban"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "ban", reason, durationMinutes, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var target = ResolveTarget(targetIdentifier);
            string targetSteamId;
            string targetName;

            if (target != null)
            {
                targetSteamId = target.UserIDString;
                targetName = target.displayName;
                target.Kick($"Banned: {reason}");
            }
            else
            {
                // Assume identifier is a SteamID if no online player matched
                targetSteamId = targetIdentifier;
                targetName = "Unknown";
            }

            if (_dbConnection != null)
            {
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
                        );";

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

                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    _runtimeBridge?.LogError($"[Sentinel] Ban insert failed: {ex.Message}");
                    error = "Database error";
                    LogAuditAction(actorId, actorName, targetSteamId, targetName, "ban", reason, durationMinutes, false);
                    return false;
                }
            }

            LogAuditAction(actorId, actorName, targetSteamId, targetName, "ban", reason, durationMinutes, true);
            return true;
        }

        [ConsoleCommand("sentinel.ban")]
        void CCmdBan(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2)
            {
                Puts("Usage: sentinel.ban \u003cplayer\u003e \u003cduration_minutes|0\u003e \"[reason]\"");
                return;
            }

            var targetId = arg.Args[0];
            var duration = int.TryParse(arg.Args[1], out var d) && d > 0 ? d : (int?)null;
            var reason = arg.Args.Length > 2 ? arg.Args[2] : "No reason given";
            var admin = arg.Player();

            if (!ExecuteBan(admin, targetId, reason, duration, out var error))
            {
                Puts($"[Sentinel] Ban failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Banned {targetId} for {duration?.ToString() ?? "permanent"} minutes: {reason}");
            }
        }

        // ---------------------------------------------------------------
        // Warn
        // ---------------------------------------------------------------
        public bool ExecuteWarn(BasePlayer? admin, string targetIdentifier, string reason, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.warn"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "warn", reason, null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var target = ResolveTarget(targetIdentifier);
            if (target == null)
            {
                error = "Player not found";
                LogAuditAction(actorId, actorName, null, null, "warn", reason, null, false);
                return false;
            }

            var warnCount = PersistWarn(target.UserIDString, target.displayName, reason);

            var state = GetOrCreateModerationState(target.UserIDString);
            state.WarnCount = warnCount;
            target.ChatMessage($"[Sentinel] WARNING: {reason} (Total warnings: {warnCount})");

            LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "warn", reason, null, true,
                $"{{\"warnCount\":{warnCount}}}");
            return true;
        }

        private int PersistWarn(string targetId, string targetName, string reason)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int warnCount = 1;

            if (_dbConnection != null)
            {
                try
                {
                    using var selectCmd = _dbConnection.CreateCommand();
                    selectCmd.CommandText = "SELECT warn_count FROM sentinel_warnings WHERE target_id = @targetId;";
                    selectCmd.Parameters.AddWithValue("@targetId", targetId);
                    var existing = selectCmd.ExecuteScalar();

                    if (existing != null && existing != DBNull.Value)
                    {
                        warnCount = Convert.ToInt32(existing) + 1;
                        using var updateCmd = _dbConnection.CreateCommand();
                        updateCmd.CommandText = @"
                            UPDATE sentinel_warnings
                            SET warn_count = @count,
                                target_name = @name,
                                last_reason = @reason,
                                last_warned_at = @now
                            WHERE target_id = @targetId;";
                        updateCmd.Parameters.AddWithValue("@count", warnCount);
                        updateCmd.Parameters.AddWithValue("@name", targetName);
                        updateCmd.Parameters.AddWithValue("@reason", reason);
                        updateCmd.Parameters.AddWithValue("@now", now);
                        updateCmd.Parameters.AddWithValue("@targetId", targetId);
                        updateCmd.ExecuteNonQuery();
                    }
                    else
                    {
                        using var insertCmd = _dbConnection.CreateCommand();
                        insertCmd.CommandText = @"
                            INSERT INTO sentinel_warnings (target_id, target_name, warn_count, last_reason, last_warned_at, created_at)
                            VALUES (@targetId, @name, 1, @reason, @now, @now);";
                        insertCmd.Parameters.AddWithValue("@targetId", targetId);
                        insertCmd.Parameters.AddWithValue("@name", targetName);
                        insertCmd.Parameters.AddWithValue("@reason", reason);
                        insertCmd.Parameters.AddWithValue("@now", now);
                        insertCmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    _runtimeBridge?.LogError($"[Sentinel] PersistWarn failed: {ex.Message}");
                }
            }

            return warnCount;
        }

        [ConsoleCommand("sentinel.warn")]
        void CCmdWarn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2)
            {
                Puts("Usage: sentinel.warn \u003cplayer\u003e \"[reason]\"");
                return;
            }

            var targetId = arg.Args[0];
            var reason = arg.Args[1];
            var admin = arg.Player();

            if (!ExecuteWarn(admin, targetId, reason, out var error))
            {
                Puts($"[Sentinel] Warn failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Warned {targetId}: {reason}");
            }
        }

        // ---------------------------------------------------------------
        // Mute
        // ---------------------------------------------------------------
        public bool ExecuteMute(BasePlayer? admin, string targetIdentifier, string muteType, int? durationMinutes, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.mute"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "mute", $"{muteType}", durationMinutes, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var target = ResolveTarget(targetIdentifier);
            if (target == null)
            {
                error = "Player not found";
                LogAuditAction(actorId, actorName, null, null, "mute", $"{muteType}", durationMinutes, false);
                return false;
            }

            var state = GetOrCreateModerationState(target.UserIDString);
            DateTime? expires = durationMinutes.HasValue ? DateTime.UtcNow.AddMinutes(durationMinutes.Value) : null;

            if (muteType.Equals("chat", StringComparison.OrdinalIgnoreCase))
            {
                state.IsChatMuted = true;
            }
            else if (muteType.Equals("voice", StringComparison.OrdinalIgnoreCase))
            {
                state.IsVoiceMuted = true;
            }
            else if (muteType.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                state.IsChatMuted = true;
                state.IsVoiceMuted = true;
            }
            else
            {
                error = "Invalid mute type. Use: chat, voice, or all";
                LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "mute", $"{muteType}", durationMinutes, false);
                return false;
            }

            state.MuteExpiresAt = expires;
            target.ChatMessage($"[Sentinel] You have been muted ({muteType}).{(durationMinutes.HasValue ? $" Duration: {durationMinutes} minutes." : "")}");

            LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "mute", $"{muteType}", durationMinutes, true,
                $"{{\"muteType\":\"{muteType}\",\"warnCount\":{state.WarnCount}}}");
            return true;
        }

        [ConsoleCommand("sentinel.mute")]
        void CCmdMute(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2)
            {
                Puts("Usage: sentinel.mute \u003cplayer\u003e \u003ctype\u003e [duration_minutes]");
                return;
            }

            var targetId = arg.Args[0];
            var muteType = arg.Args[1];
            var duration = arg.Args.Length > 2 && int.TryParse(arg.Args[2], out var dm) && dm > 0 ? dm : (int?)null;
            var admin = arg.Player();

            if (!ExecuteMute(admin, targetId, muteType, duration, out var error))
            {
                Puts($"[Sentinel] Mute failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Muted {targetId} ({muteType}) for {duration?.ToString() ?? "permanent"} minutes.");
            }
        }

        // ---------------------------------------------------------------
        // Freeze
        // ---------------------------------------------------------------
        public bool ExecuteFreeze(BasePlayer? admin, string targetIdentifier, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.freeze"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "freeze", null, null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var target = ResolveTarget(targetIdentifier);
            if (target == null)
            {
                error = "Player not found";
                LogAuditAction(actorId, actorName, null, null, "freeze", null, null, false);
                return false;
            }

            var state = GetOrCreateModerationState(target.UserIDString);
            state.IsFrozen = !state.IsFrozen;

            var action = state.IsFrozen ? "frozen" : "unfrozen";
            target.ChatMessage($"[Sentinel] You have been {action}.");

            LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "freeze", action, null, true,
                $"{{\"frozen\":{state.IsFrozen.ToString().ToLower()}}}");
            return true;
        }

        [ConsoleCommand("sentinel.freeze")]
        void CCmdFreeze(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.freeze \u003cplayer\u003e");
                return;
            }

            var targetId = arg.Args[0];
            var admin = arg.Player();

            if (!ExecuteFreeze(admin, targetId, out var error))
            {
                Puts($"[Sentinel] Freeze failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Toggled freeze for {targetId}.");
            }
        }
    }
}
