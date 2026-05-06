using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    public class ServerRule
    {
        public string RuleId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string Keywords { get; set; } = "";
    }

    public class RuleMatch
    {
        public string RuleId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public double Score { get; set; }
    }

    public class RuleLookupResult
    {
        public List<RuleMatch> Matches { get; set; } = new();
        public bool IsHeuristic { get; set; }
    }

    public partial class Sentinel
    {
        private static readonly Regex TokenizerRegex = new(@"[^a-z0-9\s]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public virtual List<ServerRule> QueryAllRules()
        {
            var rules = new List<ServerRule>();
            if (_dbConnection == null) return rules;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT rule_id, title, description, category, keywords
                    FROM sentinel_rules
                    ORDER BY rule_id;";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rules.Add(new ServerRule
                    {
                        RuleId = reader.GetString(0),
                        Title = reader.GetString(1),
                        Description = reader.GetString(2),
                        Category = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        Keywords = reader.IsDBNull(4) ? "" : reader.GetString(4)
                    });
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] Rule lookup query failed: {ex.Message}");
            }

            return rules;
        }

        public virtual RuleLookupResult RunRuleLookupAgent(string behaviorDescription)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var sw = Stopwatch.StartNew();

            var rules = QueryAllRules();
            if (rules.Count == 0)
            {
                _runtimeBridge?.LogWarning("[Sentinel] Rule Lookup: no rules found in rule_index.");
                return new RuleLookupResult { Matches = new List<RuleMatch>(), IsHeuristic = true };
            }

            var prompt = BuildRuleLookupPrompt(behaviorDescription, rules);
            var redactedPrompt = PiiRedactor.Redact(prompt);
            LlmResponse? llmResponse = null;
            RuleLookupResult? result = null;

            try
            {
                PiiRedactor.ValidatePrompt(behaviorDescription);
            }
            catch (PromptInjectionException ex)
            {
                _runtimeBridge?.LogWarning($"[Sentinel] Rule Lookup prompt injection blocked: {ex.Message}");
                sw.Stop();
                LogAiQuery(
                    agentName: "RuleLookup",
                    requestId: requestId,
                    promptHash: ComputeDeterministicHash(prompt),
                    redactedInput: redactedPrompt,
                    rawOutput: $"[REJECTED] {ex.Message}",
                    durationMs: (int)sw.ElapsedMilliseconds
                );
                return new RuleLookupResult { Matches = new List<RuleMatch>(), IsHeuristic = true };
            }

            try
            {
                llmResponse = SendAiRequest(prompt);
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogWarning($"[Sentinel] Rule Lookup LLM call failed: {ex.Message}");
            }

            if (llmResponse != null && llmResponse.Success && !llmResponse.IsFallback)
            {
                var parsed = ParseLlmRuleLookupResponse(llmResponse.Content);
                if (parsed != null)
                {
                    result = new RuleLookupResult
                    {
                        Matches = parsed,
                        IsHeuristic = false
                    };
                }
            }

            if (result == null)
            {
                _runtimeBridge?.LogInfo("[Sentinel] Rule Lookup falling back to heuristic keyword scoring.");
                var scored = HeuristicRuleScoring(behaviorDescription, rules);
                result = new RuleLookupResult
                {
                    Matches = scored,
                    IsHeuristic = true
                };
            }

            // Apply threshold: keep only scores >= 0.6, max 3, sorted descending
            var filtered = result.Matches
                .Where(m => m.Score >= 0.6)
                .OrderByDescending(m => m.Score)
                .Take(3)
                .ToList();

            if (filtered.Count == 0)
            {
                _runtimeBridge?.LogWarning($"[Sentinel] Rule Lookup low-confidence: no rule exceeded 0.6 threshold for behavior='{behaviorDescription}'.");
            }

            sw.Stop();

            LogAiQuery(
                agentName: "RuleLookup",
                requestId: requestId,
                promptHash: ComputeDeterministicHash(prompt),
                redactedInput: redactedPrompt,
                rawOutput: llmResponse?.Content ?? "[HEURISTIC]",
                durationMs: (int)sw.ElapsedMilliseconds
            );

            return new RuleLookupResult
            {
                Matches = filtered,
                IsHeuristic = result.IsHeuristic
            };
        }

        public virtual string BuildRuleLookupPrompt(string behaviorDescription, List<ServerRule> rules)
        {
            var lines = new List<string>
            {
                "You are Sentinel AI Rule Lookup Agent. Match the following behavior description to the most relevant server rules.",
                "",
                "Return ONLY a JSON array with this exact schema (no markdown, no explanation):",
                "[{\"rule_id\":\"string\",\"title\":\"string\",\"score\":0.0}]",
                "",
                "Rules:",
                "- Return at most 3 rules.",
                "- Score must be between 0.0 and 1.0.",
                "- Only include rules with score >= 0.6.",
                "- Sort by score descending (most relevant first).",
                "",
                $"Behavior description: {behaviorDescription}",
                "",
                "Available rules:"
            };

            foreach (var rule in rules)
            {
                lines.Add($"- {rule.RuleId}: {rule.Title} | {rule.Description}");
            }

            return string.Join("\n", lines);
        }

        public virtual List<RuleMatch>? ParseLlmRuleLookupResponse(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;

            try
            {
                var text = ExtractLlmText(content);
                if (string.IsNullOrWhiteSpace(text)) return null;

                var matches = JsonSerializer.Deserialize<List<RuleMatch>>(text);
                if (matches == null) return null;

                // Validate and sanitize scores
                foreach (var match in matches)
                {
                    match.Score = Math.Clamp(match.Score, 0.0, 1.0);
                }

                return matches.Where(m => m.Score >= 0.6).OrderByDescending(m => m.Score).Take(3).ToList();
            }
            catch
            {
                return null;
            }
        }

        public virtual List<RuleMatch> HeuristicRuleScoring(string behaviorDescription, List<ServerRule> rules)
        {
            var behaviorTokens = Tokenize(behaviorDescription);
            if (behaviorTokens.Count == 0) return new List<RuleMatch>();

            var matches = new List<RuleMatch>();

            foreach (var rule in rules)
            {
                // Weight keywords 3x and title 2x to boost relevance of direct keyword matches
                var ruleText = $"{rule.Title} {rule.Title} {rule.Description} {rule.Keywords} {rule.Keywords} {rule.Keywords}";
                var ruleTokens = Tokenize(ruleText);
                if (ruleTokens.Count == 0) continue;

                var score = ComputeCosineSimilarity(behaviorTokens, ruleTokens);

                matches.Add(new RuleMatch
                {
                    RuleId = rule.RuleId,
                    Title = rule.Title,
                    Description = rule.Description,
                    Score = Math.Round(score, 4)
                });
            }

            return matches.OrderByDescending(m => m.Score).ToList();
        }

        public virtual List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();

            var normalized = TokenizerRegex.Replace(text, " ").ToLowerInvariant();
            var tokens = normalized.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 2)
                .ToList();

            return tokens;
        }

        public virtual double ComputeCosineSimilarity(List<string> tokensA, List<string> tokensB)
        {
            if (tokensA.Count == 0 || tokensB.Count == 0) return 0.0;

            var freqA = tokensA.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
            var freqB = tokensB.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());

            var allTokens = new HashSet<string>(freqA.Keys);
            allTokens.UnionWith(freqB.Keys);

            double dotProduct = 0;
            double normA = 0;
            double normB = 0;

            foreach (var token in allTokens)
            {
                var a = freqA.TryGetValue(token, out var va) ? va : 0;
                var b = freqB.TryGetValue(token, out var vb) ? vb : 0;

                dotProduct += a * b;
            }

            foreach (var kvp in freqA)
            {
                normA += kvp.Value * kvp.Value;
            }

            foreach (var kvp in freqB)
            {
                normB += kvp.Value * kvp.Value;
            }

            if (normA == 0 || normB == 0) return 0.0;

            var similarity = dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
            return Math.Clamp(similarity, 0.0, 1.0);
        }
    }
}
