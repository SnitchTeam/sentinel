using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    public class BanDraftResult
    {
        public string Reason { get; set; } = "";
        public bool IsHeuristic { get; set; }
        public bool HasCitation { get; set; }
    }

    public partial class Sentinel
    {
        // Regex patterns to detect concrete citations in ban draft text
        private static readonly Regex[] CitationPatterns = new[]
        {
            new Regex(@"\[\s*Log[:\s][^\]]+\]", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\bRule\s*[§#]\s*\d+(\.\d+)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\b\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}", RegexOptions.Compiled),
            new Regex(@"\b\d{10,13}\b", RegexOptions.Compiled),
            new Regex(@"\bLine\s+\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\bRef[:\s]\s*\w+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        public virtual bool ContainsCitation(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return CitationPatterns.Any(p => p.IsMatch(text));
        }

        public virtual BanDraftResult RunBanDraftAgent(
            string playerSteamId,
            string playerName,
            List<ActionRecord> evidence,
            List<string>? ruleIds = null)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var prompt = BuildBanDraftPrompt(playerSteamId, playerName, evidence, ruleIds);
            var redactedPrompt = PiiRedactor.Redact(prompt);

            var sw = Stopwatch.StartNew();
            LlmResponse? llmResponse = null;
            BanDraftResult? result = null;

            try
            {
                llmResponse = SendAiRequest(prompt);

                if (llmResponse != null && llmResponse.Success && !llmResponse.IsFallback)
                {
                    var rawReason = ExtractLlmText(llmResponse.Content);
                    if (!string.IsNullOrWhiteSpace(rawReason) && rawReason.Length <= 500 && ContainsCitation(rawReason))
                    {
                        result = new BanDraftResult
                        {
                            Reason = rawReason,
                            IsHeuristic = false,
                            HasCitation = true
                        };
                    }
                    else if (!string.IsNullOrWhiteSpace(rawReason) && rawReason.Length <= 500 && !ContainsCitation(rawReason))
                    {
                        _runtimeBridge?.LogWarning("[Sentinel] Ban Draft LLM response lacks citation. Retrying...");

                        llmResponse = SendAiRequest(prompt);
                        if (llmResponse != null && llmResponse.Success && !llmResponse.IsFallback)
                        {
                            var retryReason = ExtractLlmText(llmResponse.Content);
                            if (!string.IsNullOrWhiteSpace(retryReason) && retryReason.Length <= 500)
                            {
                                result = new BanDraftResult
                                {
                                    Reason = retryReason,
                                    IsHeuristic = false,
                                    HasCitation = ContainsCitation(retryReason)
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogWarning($"[Sentinel] Ban Draft LLM call failed: {ex.Message}");
            }

            if (result == null || !result.HasCitation)
            {
                _runtimeBridge?.LogInfo("[Sentinel] Ban Draft falling back to heuristic stub.");
                result = HeuristicBanDraft(playerSteamId, playerName, evidence, ruleIds);
            }

            sw.Stop();

            LogAiQuery(
                agentName: "BanDraft",
                requestId: requestId,
                promptHash: ComputeDeterministicHash(prompt),
                redactedInput: redactedPrompt,
                rawOutput: llmResponse?.Content ?? "[HEURISTIC]",
                durationMs: (int)sw.ElapsedMilliseconds
            );

            // Create an AI suggestion so admins can thumbs-up/down the ban draft
            var banDraftSuggestion = new AiSuggestion
            {
                Id = requestId,
                PlayerName = playerName,
                SteamId = playerSteamId,
                Behavior = "ban_draft",
                Confidence = result.HasCitation ? 85 : 60,
                RecommendedAction = "ban",
                Reason = result.Reason,
                DurationMinutes = null,
                AgentName = "BanDraft"
            };
            AddAiSuggestion(banDraftSuggestion);

            return result;
        }

        public virtual string BuildBanDraftPrompt(
            string playerSteamId,
            string playerName,
            List<ActionRecord> evidence,
            List<string>? ruleIds)
        {
            var lines = new List<string>
            {
                "You are Sentinel AI Ban Draft Agent. Draft a concise ban reason for the following player.",
                "",
                "Requirements:",
                "- Include at least one concrete citation (timestamp, log line, or rule ID)",
                "- Provide a human-readable explanation",
                "- Output must be 500 characters or fewer",
                "- Do NOT include markdown formatting",
                "",
                $"Player: {playerName} ({playerSteamId})"
            };

            if (ruleIds != null && ruleIds.Count > 0)
            {
                lines.Add($"Applicable rules: {string.Join(", ", ruleIds)}");
            }

            lines.Add("");
            lines.Add($"Evidence ({evidence.Count} records):");

            foreach (var action in evidence.Take(50))
            {
                var target = action.TargetSteamId ?? "none";
                var reason = action.Reason ?? "none";
                lines.Add($"- [{action.Timestamp}] actor={action.ActorSteamId} target={target} type={action.ActionType} reason={reason}");
            }

            lines.Add("");
            lines.Add("Return ONLY the ban reason text. No extra commentary.");

            return string.Join("\n", lines);
        }

        public virtual string ExtractLlmText(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";

            // Try to extract from OpenAI chat completion format
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var message = choices[0].GetProperty("message");
                    var messageContent = message.GetProperty("content").GetString();
                    if (!string.IsNullOrEmpty(messageContent)) return messageContent.Trim();
                }
            }
            catch { }

            // Try to extract from Anthropic format
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
                {
                    var text = contentArray[0].GetProperty("text").GetString();
                    if (!string.IsNullOrEmpty(text)) return text.Trim();
                }
            }
            catch { }

            // Try raw JSON string (quoted)
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    return doc.RootElement.GetString()?.Trim() ?? "";
                }
            }
            catch { }

            // Return raw content trimmed
            return content.Trim();
        }

        public virtual BanDraftResult HeuristicBanDraft(
            string playerSteamId,
            string playerName,
            List<ActionRecord> evidence,
            List<string>? ruleIds)
        {
            var mostRecent = evidence
                .Where(e => e.Timestamp > 0)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefault();

            var timestamp = mostRecent != null
                ? DateTimeOffset.FromUnixTimeSeconds(mostRecent.Timestamp).ToString("yyyy-MM-ddTHH:mm:ssZ")
                : DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            var ruleCitation = ruleIds != null && ruleIds.Count > 0
                ? $" Rule {ruleIds[0]}."
                : "";

            var evidenceType = mostRecent?.ActionType ?? "violation";
            var reason = $"Player {playerName} banned for {evidenceType}.{ruleCitation} [Log:{timestamp}] Evidence from audit log supports this action.";

            if (reason.Length > 500)
            {
                reason = reason.Substring(0, 500);
            }

            return new BanDraftResult
            {
                Reason = reason,
                IsHeuristic = true,
                HasCitation = true
            };
        }
    }
}
