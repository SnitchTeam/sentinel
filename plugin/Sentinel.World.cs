using System;
using System.Collections.Generic;
using Oxide.Core;

namespace Oxide.Plugins
{
    public partial class Sentinel
    {
        private const string WORLD_STATE_TABLE = "sentinel_world_state";

        // ---- Virtual hooks for testability --------------------------------
        protected virtual void ApplyTimeOfDay(double hour)
        {
            // Real runtime: TOD_Sky.Instance.Cycle.Hour = hour;
        }

        protected virtual double GetTimeOfDay()
        {
            // Real runtime: return TOD_Sky.Instance.Cycle.Hour;
            return -1;
        }

        protected virtual void ApplyWeather(string weather)
        {
            // Real runtime: transitions weather via Climate/ConsoleSystem
        }

        protected virtual string GetWeather()
        {
            // Real runtime: returns current weather state
            return "unknown";
        }

        // ---- Commands -----------------------------------------------------
        public bool ExecuteSetTime(BasePlayer? admin, double hour, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.world"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "world_time", $"set {hour}", null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            if (hour < 0 || hour > 24)
            {
                error = "Hour must be between 0 and 24.";
                LogAuditAction(actorId, actorName, null, null, "world_time", $"set {hour}", null, false,
                    $"{{\"reason\":\"invalid_range\",\"requested\":{hour}}}");
                return false;
            }

            double oldHour = GetTimeOfDay();
            ApplyTimeOfDay(hour);

            if (PluginConfig?.World?.PersistOverrides ?? true)
            {
                string? currentWeather = GetWeather();
                SaveWorldState(hour, currentWeather, actorId);
            }

            LogAuditAction(actorId, actorName, null, null, "world_time", $"set {hour}", null, true,
                $"{{\"oldHour\":{oldHour},\"newHour\":{hour}}}");
            return true;
        }

        public bool ExecuteSetWeather(BasePlayer? admin, string weather, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.world"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "world_weather", weather, null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var normalized = weather.ToLowerInvariant();
            if (normalized != "clear" && normalized != "rain" && normalized != "storm")
            {
                error = "Weather must be clear, rain, or storm.";
                LogAuditAction(actorId, actorName, null, null, "world_weather", weather, null, false,
                    $"{{\"reason\":\"invalid_type\",\"requested\":\"{weather}\"}}");
                return false;
            }

            string oldWeather = GetWeather();
            ApplyWeather(normalized);

            if (PluginConfig?.World?.PersistOverrides ?? true)
            {
                double currentTime = GetTimeOfDay();
                SaveWorldState(currentTime, normalized, actorId);
            }

            LogAuditAction(actorId, actorName, null, null, "world_weather", normalized, null, true,
                $"{{\"oldWeather\":\"{oldWeather}\",\"newWeather\":\"{normalized}\"}}");
            return true;
        }

        [ConsoleCommand("sentinel.world.time")]
        void CCmdWorldTime(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.world.time \u003chour (0-24)\u003e");
                return;
            }

            if (!double.TryParse(arg.Args[0], out var hour))
            {
                Puts("[Sentinel] Invalid hour.");
                return;
            }

            var admin = arg.Player();
            if (!ExecuteSetTime(admin, hour, out var error))
            {
                Puts($"[Sentinel] Set time failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Time set to {hour:F2}.");
            }
        }

        [ConsoleCommand("sentinel.world.weather")]
        void CCmdWorldWeather(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.world.weather \u003cclear|rain|storm\u003e");
                return;
            }

            var weather = arg.Args[0];
            var admin = arg.Player();
            if (!ExecuteSetWeather(admin, weather, out var error))
            {
                Puts($"[Sentinel] Set weather failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Weather set to {weather.ToLowerInvariant()}.");
            }
        }

        // ---- Persistence --------------------------------------------------
        private void SaveWorldState(double? timeOverride, string? weatherOverride, string updatedBy)
        {
            if (_dbConnection == null) return;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = $@"
                    INSERT INTO {WORLD_STATE_TABLE} (id, time_override, weather_override, updated_at, updated_by)
                    VALUES (1, @time, @weather, @timestamp, @by)
                    ON CONFLICT(id) DO UPDATE SET
                        time_override = excluded.time_override,
                        weather_override = excluded.weather_override,
                        updated_at = excluded.updated_at,
                        updated_by = excluded.updated_by;";

                command.Parameters.AddWithValue("@time", timeOverride.HasValue ? (object)timeOverride.Value : DBNull.Value);
                command.Parameters.AddWithValue("@weather", weatherOverride ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                command.Parameters.AddWithValue("@by", updatedBy);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] Save world state failed: {ex.Message}");
            }
        }

        public void RestoreWorldState()
        {
            if (_dbConnection == null) return;
            if (!(PluginConfig?.World?.PersistOverrides ?? true)) return;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = $@"
                    SELECT time_override, weather_override
                    FROM {WORLD_STATE_TABLE}
                    WHERE id = 1;";

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                    {
                        var time = reader.GetDouble(0);
                        ApplyTimeOfDay(time);
                        _runtimeBridge?.LogInfo($"[Sentinel] Restored time override: {time:F2}");
                    }

                    if (!reader.IsDBNull(1))
                    {
                        var weather = reader.GetString(1);
                        ApplyWeather(weather);
                        _runtimeBridge?.LogInfo($"[Sentinel] Restored weather override: {weather}");
                    }
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] Restore world state failed: {ex.Message}");
            }
        }

        // ---- Schema helper ------------------------------------------------
        public void CreateWorldStateSchema()
        {
            if (_dbConnection == null) return;

            using var command = _dbConnection.CreateCommand();
            command.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {WORLD_STATE_TABLE} (
                    id INTEGER PRIMARY KEY,
                    time_override REAL,
                    weather_override TEXT,
                    updated_at INTEGER NOT NULL,
                    updated_by TEXT
                );";
            command.ExecuteNonQuery();
        }
    }
}
