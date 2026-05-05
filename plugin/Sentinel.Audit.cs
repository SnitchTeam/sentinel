using System;

namespace Oxide.Plugins
{
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
                        action_type, details_json, timestamp, success
                    ) VALUES (
                        @actorId, @actorName, @targetId, @targetName,
                        @actionType, @details, @timestamp, @success
                    );";

                command.Parameters.AddWithValue("@actorId", actorSteamId);
                command.Parameters.AddWithValue("@actorName", actorName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@targetId", targetSteamId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@targetName", targetName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@actionType", actionType);
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
    }
}
