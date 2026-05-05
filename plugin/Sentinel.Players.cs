using System;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    public class PlayerInfo
    {
        public string SteamId { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime ConnectedSince { get; set; }
        public string? IpAddress { get; set; }
        public int Ping { get; set; }
    }

    public class OfflinePlayerInfo
    {
        public string SteamId { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime DisconnectedAt { get; set; }
        public string? DisconnectReason { get; set; }
    }

    public class PlayerModerationState
    {
        public int WarnCount { get; set; }
        public bool IsChatMuted { get; set; }
        public bool IsVoiceMuted { get; set; }
        public DateTime? MuteExpiresAt { get; set; }
        public bool IsFrozen { get; set; }
    }

    public partial class Sentinel
    {
        private readonly Dictionary<string, PlayerInfo> _onlinePlayers = new();
        private readonly Queue<OfflinePlayerInfo> _offlineHistory = new(100);
        private readonly Dictionary<string, PlayerModerationState> _moderationStates = new();

        public void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            var steamId = player.UserIDString;
            _onlinePlayers[steamId] = new PlayerInfo
            {
                SteamId = steamId,
                Name = player.displayName ?? "Unknown",
                ConnectedSince = DateTime.UtcNow,
                IpAddress = player.Address
            };
        }

        public void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            var steamId = player.UserIDString;
            _onlinePlayers.Remove(steamId);

            if (_offlineHistory.Count >= 100)
                _offlineHistory.Dequeue();

            _offlineHistory.Enqueue(new OfflinePlayerInfo
            {
                SteamId = steamId,
                Name = player.displayName ?? "Unknown",
                DisconnectedAt = DateTime.UtcNow,
                DisconnectReason = reason
            });
        }

        public IReadOnlyList<PlayerInfo> GetOnlinePlayers()
        {
            return _onlinePlayers.Values.ToList();
        }

        public IReadOnlyList<PlayerInfo> SearchOnlinePlayers(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return GetOnlinePlayers();

            query = query.Trim();
            var results = new List<PlayerInfo>();

            foreach (var p in _onlinePlayers.Values)
            {
                if (p.SteamId == query) results.Add(p);
                else if (p.Name != null && p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) results.Add(p);
            }

            return results;
        }

        public IReadOnlyList<OfflinePlayerInfo> GetOfflineHistory()
        {
            return _offlineHistory.ToList();
        }

        public IReadOnlyList<OfflinePlayerInfo> SearchOfflinePlayers(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return GetOfflineHistory();

            query = query.Trim();
            var results = new List<OfflinePlayerInfo>();

            foreach (var p in _offlineHistory)
            {
                if (p.SteamId == query) results.Add(p);
                else if (p.Name != null && p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) results.Add(p);
            }

            return results;
        }

        public PlayerModerationState GetOrCreateModerationState(string steamId)
        {
            if (!_moderationStates.TryGetValue(steamId, out var state))
            {
                state = new PlayerModerationState();
                var persistedCount = GetWarnCountFromDatabase(steamId);
                state.WarnCount = persistedCount;
                _moderationStates[steamId] = state;
            }
            return state;
        }

        public void ClearModerationStates()
        {
            _moderationStates.Clear();
        }

        private int GetWarnCountFromDatabase(string steamId)
        {
            if (_dbConnection == null) return 0;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = "SELECT warn_count FROM sentinel_warnings WHERE target_id = @targetId;";
                command.Parameters.AddWithValue("@targetId", steamId);
                var result = command.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result);
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetWarnCount failed: {ex.Message}");
            }
            return 0;
        }

        public PlayerInfo? FindOnlinePlayer(string steamIdOrName)
        {
            if (_onlinePlayers.TryGetValue(steamIdOrName, out var byId)) return byId;
            return _onlinePlayers.Values.FirstOrDefault(p =>
                p.Name.Equals(steamIdOrName, StringComparison.OrdinalIgnoreCase));
        }

        public BasePlayer? ResolveTarget(string identifier)
        {
            return ResolveTargetInternal(identifier);
        }

        protected virtual BasePlayer? ResolveTargetInternal(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return null;

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p?.UserIDString == identifier) return p;
            }

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p?.displayName != null && p.displayName.Contains(identifier, StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            return null;
        }

        public bool IsBanned(string steamId)
        {
            if (_dbConnection == null) return false;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT COUNT(*) FROM sentinel_bans
                    WHERE steam_id = @steamId
                      AND active = 1
                      AND (expires_at IS NULL OR expires_at > @now);";
                command.Parameters.AddWithValue("@steamId", steamId);
                command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                var count = Convert.ToInt64(command.ExecuteScalar());
                return count > 0;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] IsBanned check failed: {ex.Message}");
                return false;
            }
        }

        public string? GetBanMessage(string steamId)
        {
            if (_dbConnection == null) return null;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT reason, expires_at FROM sentinel_bans
                    WHERE steam_id = @steamId
                      AND active = 1
                      AND (expires_at IS NULL OR expires_at > @now)
                    ORDER BY created_at DESC
                    LIMIT 1;";
                command.Parameters.AddWithValue("@steamId", steamId);
                command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    var reason = reader.GetString(0);
                    var expires = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                    var expiryText = expires.HasValue
                        ? $" Expires: {DateTimeOffset.FromUnixTimeSeconds(expires.Value).UtcDateTime:yyyy-MM-dd HH:mm} UTC."
                        : " Permanent ban.";
                    return $"You are banned from this server. Reason: {reason}.{expiryText}";
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetBanMessage failed: {ex.Message}");
            }
            return null;
        }

        public object? OnUserApprove(AuthenticationTicketIdentity identity)
        {
            if (identity == null) return null;
            var steamId = identity.Userid;
            if (IsBanned(steamId))
            {
                var msg = GetBanMessage(steamId) ?? "You are banned from this server.";
                return msg;
            }
            return null;
        }

        public object? OnPlayerChat(BasePlayer player, string message, int channel = 0)
        {
            if (player == null) return null;
            var state = GetOrCreateModerationState(player.UserIDString);
            if (state.IsChatMuted)
            {
                if (state.MuteExpiresAt.HasValue && state.MuteExpiresAt.Value <= DateTime.UtcNow)
                {
                    state.IsChatMuted = false;
                    return null;
                }
                player.ChatMessage("You are muted and cannot chat.");
                return true; // Block chat
            }
            return null;
        }
    }
}
