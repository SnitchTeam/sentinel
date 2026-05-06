using Microsoft.Data.Sqlite;
using System;

namespace Oxide.Plugins
{
    public class AiCostTracker
    {
        private readonly SqliteConnection? _dbConnection;
        private readonly IRuntimeBridge? _logger;
        private string _currentAlertDate = "";
        private bool _alerted80 = false;
        private bool _alerted100 = false;

        public AiCostTracker(SqliteConnection? dbConnection, IRuntimeBridge? logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public double GetDailySpend(DateTime day)
        {
            if (_dbConnection == null) return 0.0;
            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = "SELECT COALESCE(SUM(cost_usd), 0) FROM sentinel_ai_cost_log WHERE day = @day;";
                command.Parameters.AddWithValue("@day", day.ToString("yyyy-MM-dd"));
                var result = command.ExecuteScalar();
                return Convert.ToDouble(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Sentinel] Cost tracking query failed: {ex.Message}");
                return 0.0;
            }
        }

        public void RecordCost(string provider, string model, int inputTokens, int outputTokens, double costUsd)
        {
            if (_dbConnection == null) return;
            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO sentinel_ai_cost_log (provider, model, input_tokens, output_tokens, cost_usd, day, timestamp)
                    VALUES (@provider, @model, @inputTokens, @outputTokens, @costUsd, @day, @timestamp);";
                command.Parameters.AddWithValue("@provider", provider);
                command.Parameters.AddWithValue("@model", model);
                command.Parameters.AddWithValue("@inputTokens", inputTokens);
                command.Parameters.AddWithValue("@outputTokens", outputTokens);
                command.Parameters.AddWithValue("@costUsd", costUsd);
                command.Parameters.AddWithValue("@day", DateTime.UtcNow.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Sentinel] Cost tracking insert failed: {ex.Message}");
            }
        }

        public void AlertIfNeeded(double currentSpend, double cap)
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (_currentAlertDate != today)
            {
                _currentAlertDate = today;
                _alerted80 = false;
                _alerted100 = false;
            }

            var pct = currentSpend / cap;

            if (pct >= 1.0 && !_alerted100)
            {
                _alerted100 = true;
                _logger?.LogWarning($"[Sentinel] AI COST ALERT: 100% of daily cap reached (${currentSpend:F2}/${cap:F2}). Subsequent requests will use heuristic stubs.");
            }
            else if (pct >= 0.8 && !_alerted80)
            {
                _alerted80 = true;
                _logger?.LogWarning($"[Sentinel] AI COST ALERT: 80% of daily cap reached (${currentSpend:F2}/${cap:F2}).");
            }
        }
    }
}
