using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;

namespace Oxide.Plugins
{
    public class AiSuggestion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string PlayerName { get; set; } = "";
        public string SteamId { get; set; } = "";
        public string Behavior { get; set; } = "";
        public int Confidence { get; set; }
        public string RecommendedAction { get; set; } = ""; // ban, kick, warn, mute, freeze
        public string Reason { get; set; } = "";
        public int? DurationMinutes { get; set; }
        public string AgentName { get; set; } = "";
        public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public class AiFeedbackAggregationRow
    {
        public string AgentName { get; set; } = "";
        public long TotalFeedback { get; set; }
        public long Accepts { get; set; }
        public long Rejects { get; set; }
    }

    public partial class Sentinel
    {
        private readonly List<AiSuggestion> _aiSuggestions = new();
        private readonly Dictionary<string, string> _playerEditingSuggestion = new();
        private readonly Dictionary<string, (string? Reason, int? Duration)> _pendingAiEdits = new();

        public AiSuggestion? GetNextSuggestion()
        {
            return _aiSuggestions.FirstOrDefault();
        }

        public AiSuggestion? GetSuggestionById(string id)
        {
            return _aiSuggestions.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public void AddAiSuggestion(AiSuggestion suggestion)
        {
            _aiSuggestions.Add(suggestion);
        }

        public bool RemoveSuggestion(string id)
        {
            var suggestion = GetSuggestionById(id);
            if (suggestion == null) return false;
            _aiSuggestions.Remove(suggestion);
            return true;
        }

        public int SuggestionCount => _aiSuggestions.Count;

        public void SetPendingAiEdit(string suggestionId, string reason, int? duration)
        {
            _pendingAiEdits[suggestionId] = (reason, duration);
        }

        public string? QueryAgentNameByRequestId(string requestId)
        {
            if (_dbConnection == null) return null;
            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = "SELECT agent_name FROM sentinel_ai_log WHERE request_id = @requestId LIMIT 1;";
                command.Parameters.AddWithValue("@requestId", requestId);
                var result = command.ExecuteScalar();
                return result?.ToString();
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] QueryAgentNameByRequestId failed: {ex.Message}");
                return null;
            }
        }

        public bool ExecuteAiFeedback(BasePlayer? admin, string requestId, string verdict, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.ai"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "ai_feedback", $"{requestId}:{verdict}", null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var normalizedVerdict = verdict.ToLowerInvariant();
            if (normalizedVerdict != "accept" && normalizedVerdict != "reject")
            {
                error = "Verdict must be 'accept' or 'reject'";
                LogAuditAction(actorId, actorName, null, null, "ai_feedback", $"{requestId}:{verdict}", null, false);
                return false;
            }

            var agentName = QueryAgentNameByRequestId(requestId);
            if (string.IsNullOrEmpty(agentName))
            {
                error = "AI log entry not found for request ID";
                LogAuditAction(actorId, actorName, null, null, "ai_feedback", $"{requestId}:{verdict}", null, false);
                return false;
            }

            LogAiFeedback(agentName, requestId, normalizedVerdict, actorId);
            LogAuditAction(actorId, actorName, null, null, "ai_feedback", $"{requestId}:{normalizedVerdict}", null, true,
                $"{{\"requestId\":\"{requestId}\",\"agent\":\"{agentName}\",\"verdict\":\"{normalizedVerdict}\"}}");
            return true;
        }

        public List<AiFeedbackAggregationRow> QueryAiFeedbackAggregation(long? sinceTimestamp = null)
        {
            var results = new List<AiFeedbackAggregationRow>();
            if (_dbConnection == null) return results;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT agent_name,
                           COUNT(*) AS total,
                           SUM(CASE WHEN verdict = 'accept' THEN 1 ELSE 0 END) AS accepts,
                           SUM(CASE WHEN verdict = 'reject' THEN 1 ELSE 0 END) AS rejects
                    FROM sentinel_ai_log
                    WHERE verdict IS NOT NULL
                      AND (@since IS NULL OR timestamp >= @since)
                    GROUP BY agent_name
                    ORDER BY agent_name;";
                command.Parameters.AddWithValue("@since", sinceTimestamp.HasValue ? (object)sinceTimestamp.Value : DBNull.Value);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new AiFeedbackAggregationRow
                    {
                        AgentName = reader.GetString(0),
                        TotalFeedback = reader.GetInt64(1),
                        Accepts = reader.GetInt64(2),
                        Rejects = reader.GetInt64(3)
                    });
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] AI feedback aggregation query failed: {ex.Message}");
            }

            return results;
        }

        public void LogAiFeedback(string agentName, string requestId, string verdict, string adminSteamId, string? editDiff = null, double? costUsd = null)
        {
            if (_dbConnection == null) return;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO sentinel_ai_log (
                        agent_name, request_id, verdict, admin_steam_id, edit_diff, cost_usd, timestamp
                    ) VALUES (
                        @agentName, @requestId, @verdict, @adminId, @editDiff, @costUsd, @timestamp
                    );";

                command.Parameters.AddWithValue("@agentName", agentName);
                command.Parameters.AddWithValue("@requestId", requestId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@verdict", verdict);
                command.Parameters.AddWithValue("@adminId", adminSteamId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@editDiff", editDiff ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@costUsd", costUsd.HasValue ? (object)costUsd.Value : DBNull.Value);
                command.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] AI feedback log failed: {ex.Message}");
            }
        }

        public bool ExecuteAiAccept(BasePlayer? admin, string suggestionId, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.ai"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "ai_accept", suggestionId, null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var suggestion = GetSuggestionById(suggestionId);
            if (suggestion == null)
            {
                error = "Suggestion not found";
                LogAuditAction(actorId, actorName, null, null, "ai_accept", suggestionId, null, false);
                return false;
            }

            bool actionResult = false;
            string actionError = "";

            switch (suggestion.RecommendedAction.ToLowerInvariant())
            {
                case "ban":
                    actionResult = ExecuteBan(admin, suggestion.SteamId, suggestion.Reason, suggestion.DurationMinutes, out actionError);
                    break;
                case "kick":
                    actionResult = ExecuteKick(admin, suggestion.SteamId, suggestion.Reason, out actionError);
                    break;
                case "warn":
                    actionResult = ExecuteWarn(admin, suggestion.SteamId, suggestion.Reason, out actionError);
                    break;
                case "mute":
                    actionResult = ExecuteMute(admin, suggestion.SteamId, "all", suggestion.DurationMinutes, out actionError);
                    break;
                case "freeze":
                    actionResult = ExecuteFreeze(admin, suggestion.SteamId, out actionError);
                    break;
                default:
                    error = $"Unknown action: {suggestion.RecommendedAction}";
                    LogAuditAction(actorId, actorName, suggestion.SteamId, suggestion.PlayerName, "ai_accept", error, null, false);
                    return false;
            }

            if (!actionResult)
            {
                error = $"Action failed: {actionError}";
                LogAuditAction(actorId, actorName, suggestion.SteamId, suggestion.PlayerName, "ai_accept", error, null, false);
                return false;
            }

            LogAiFeedback(suggestion.AgentName, suggestion.Id, "accept", actorId);
            RemoveSuggestion(suggestionId);

            LogAuditAction(actorId, actorName, suggestion.SteamId, suggestion.PlayerName, "ai_accept", suggestion.Reason, suggestion.DurationMinutes, true,
                $"{{\"suggestionId\":\"{suggestionId}\",\"action\":\"{suggestion.RecommendedAction}\",\"confidence\":{suggestion.Confidence}}}");

            return true;
        }

        public bool ExecuteAiReject(BasePlayer? admin, string suggestionId, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.ai"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "ai_reject", suggestionId, null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var suggestion = GetSuggestionById(suggestionId);
            if (suggestion == null)
            {
                error = "Suggestion not found";
                LogAuditAction(actorId, actorName, null, null, "ai_reject", suggestionId, null, false);
                return false;
            }

            LogAiFeedback(suggestion.AgentName, suggestion.Id, "reject", actorId);
            RemoveSuggestion(suggestionId);

            LogAuditAction(actorId, actorName, suggestion.SteamId, suggestion.PlayerName, "ai_reject", suggestion.Reason, suggestion.DurationMinutes, true,
                $"{{\"suggestionId\":\"{suggestionId}\",\"action\":\"{suggestion.RecommendedAction}\",\"confidence\":{suggestion.Confidence}}}");

            return true;
        }

        public bool ExecuteAiEdit(BasePlayer? admin, string suggestionId, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";

            if (!HasPermission(admin, "sentinel.ai"))
            {
                error = "No permission";
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var suggestion = GetSuggestionById(suggestionId);
            if (suggestion == null)
            {
                error = "Suggestion not found";
                return false;
            }

            _playerEditingSuggestion[actorId] = suggestionId;
            _pendingAiEdits[suggestionId] = (suggestion.Reason, suggestion.DurationMinutes);
            return true;
        }

        public bool ExecuteAiSave(BasePlayer? admin, string suggestionId, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.ai"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "ai_save", suggestionId, null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var suggestion = GetSuggestionById(suggestionId);
            if (suggestion == null)
            {
                error = "Suggestion not found";
                LogAuditAction(actorId, actorName, null, null, "ai_save", suggestionId, null, false);
                return false;
            }

            var oldReason = suggestion.Reason;
            var oldDuration = suggestion.DurationMinutes;

            if (_pendingAiEdits.TryGetValue(suggestionId, out var pending))
            {
                if (!string.IsNullOrEmpty(pending.Reason))
                    suggestion.Reason = pending.Reason;
                if (pending.Duration.HasValue)
                    suggestion.DurationMinutes = pending.Duration.Value;
            }

            bool actionResult = false;
            string actionError = "";

            switch (suggestion.RecommendedAction.ToLowerInvariant())
            {
                case "ban":
                    actionResult = ExecuteBan(admin, suggestion.SteamId, suggestion.Reason, suggestion.DurationMinutes, out actionError);
                    break;
                case "kick":
                    actionResult = ExecuteKick(admin, suggestion.SteamId, suggestion.Reason, out actionError);
                    break;
                case "warn":
                    actionResult = ExecuteWarn(admin, suggestion.SteamId, suggestion.Reason, out actionError);
                    break;
                case "mute":
                    actionResult = ExecuteMute(admin, suggestion.SteamId, "all", suggestion.DurationMinutes, out actionError);
                    break;
                case "freeze":
                    actionResult = ExecuteFreeze(admin, suggestion.SteamId, out actionError);
                    break;
                default:
                    error = $"Unknown action: {suggestion.RecommendedAction}";
                    LogAuditAction(actorId, actorName, suggestion.SteamId, suggestion.PlayerName, "ai_save", error, null, false);
                    return false;
            }

            if (!actionResult)
            {
                error = $"Action failed: {actionError}";
                LogAuditAction(actorId, actorName, suggestion.SteamId, suggestion.PlayerName, "ai_save", error, null, false);
                return false;
            }

            var editDiff = $"{{\"oldReason\":\"{oldReason}\",\"newReason\":\"{suggestion.Reason}\",\"oldDuration\":{oldDuration?.ToString() ?? "null"},\"newDuration\":{suggestion.DurationMinutes?.ToString() ?? "null"}}}";
            LogAiFeedback(suggestion.AgentName, suggestion.Id, "edit", actorId, editDiff);

            RemoveSuggestion(suggestionId);
            _playerEditingSuggestion.Remove(actorId);
            _pendingAiEdits.Remove(suggestionId);

            LogAuditAction(actorId, actorName, suggestion.SteamId, suggestion.PlayerName, "ai_save", suggestion.Reason, suggestion.DurationMinutes, true,
                $"{{\"suggestionId\":\"{suggestionId}\",\"action\":\"{suggestion.RecommendedAction}\",\"confidence\":{suggestion.Confidence}}}");

            return true;
        }

        [ConsoleCommand("sentinel.ai")]
        void CCmdAiAction(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.ai <accept|reject|edit|save|feedback> <suggestionId|requestId> [args...]");
                return;
            }

            var action = arg.Args[0].ToLowerInvariant();
            var admin = arg.Player();
            var suggestionId = arg.Args.Length > 1 ? arg.Args[1] : "";

            switch (action)
            {
                case "accept":
                    if (string.IsNullOrEmpty(suggestionId))
                    {
                        Puts("[Sentinel] Usage: sentinel.ai accept <suggestionId>");
                        return;
                    }
                    if (!ExecuteAiAccept(admin, suggestionId, out var acceptError))
                    {
                        Puts($"[Sentinel] AI accept failed: {acceptError}");
                    }
                    else
                    {
                        Puts($"[Sentinel] AI suggestion {suggestionId} accepted.");
                    }
                    break;

                case "reject":
                    if (string.IsNullOrEmpty(suggestionId))
                    {
                        Puts("[Sentinel] Usage: sentinel.ai reject <suggestionId>");
                        return;
                    }
                    if (!ExecuteAiReject(admin, suggestionId, out var rejectError))
                    {
                        Puts($"[Sentinel] AI reject failed: {rejectError}");
                    }
                    else
                    {
                        Puts($"[Sentinel] AI suggestion {suggestionId} rejected.");
                    }
                    break;

                case "edit":
                    if (string.IsNullOrEmpty(suggestionId))
                    {
                        Puts("[Sentinel] Usage: sentinel.ai edit <suggestionId>");
                        return;
                    }
                    if (!ExecuteAiEdit(admin, suggestionId, out var editError))
                    {
                        Puts($"[Sentinel] AI edit failed: {editError}");
                    }
                    else
                    {
                        if (admin != null)
                        {
                            SwitchView(admin, "ai_edit");
                        }
                    }
                    break;

                case "save":
                    if (arg.Args.Length < 2)
                    {
                        Puts("[Sentinel] Usage: sentinel.ai save <suggestionId>");
                        return;
                    }
                    var saveId = arg.Args[1];
                    if (!ExecuteAiSave(admin, saveId, out var saveError))
                    {
                        Puts($"[Sentinel] AI save failed: {saveError}");
                    }
                    else
                    {
                        Puts($"[Sentinel] AI suggestion {saveId} saved and applied.");
                    }
                    break;

                case "feedback":
                    if (arg.Args.Length < 3)
                    {
                        Puts("[Sentinel] Usage: sentinel.ai feedback <requestId> <accept|reject>");
                        return;
                    }
                    var feedbackRequestId = arg.Args[1];
                    var feedbackVerdict = arg.Args[2];
                    if (!ExecuteAiFeedback(admin, feedbackRequestId, feedbackVerdict, out var feedbackError))
                    {
                        Puts($"[Sentinel] AI feedback failed: {feedbackError}");
                    }
                    else
                    {
                        Puts($"[Sentinel] AI feedback recorded for {feedbackRequestId}: {feedbackVerdict}.");
                    }
                    break;

                default:
                    Puts($"[Sentinel] Unknown AI action: {action}");
                    break;
            }
        }

        [ConsoleCommand("sentinel.ai.edit.reason")]
        void CCmdAiEditReason(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1) return;
            var id = arg.Args[0];
            var reason = arg.Args.Length > 1 ? string.Join(" ", arg.Args, 1, arg.Args.Length - 1) : "";
            _pendingAiEdits[id] = (reason, _pendingAiEdits.TryGetValue(id, out var existing) ? existing.Duration : null);
        }

        [ConsoleCommand("sentinel.ai.edit.duration")]
        void CCmdAiEditDuration(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1) return;
            var id = arg.Args[0];
            int? duration = arg.Args.Length > 1 && int.TryParse(arg.Args[1], out var d) && d > 0 ? d : (int?)null;
            _pendingAiEdits[id] = (_pendingAiEdits.TryGetValue(id, out var existing) ? existing.Reason : null, duration);
        }
    }
}
