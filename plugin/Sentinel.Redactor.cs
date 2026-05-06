using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    public class PromptInjectionException : Exception
    {
        public PromptInjectionException(string message) : base(message) { }
    }

    public class PiiRedactor
    {
        // SteamID64 always starts with 7656119 and is 17 digits total
        private static readonly Regex SteamId64Regex = new(@"7656119[0-9]{10}", RegexOptions.Compiled);

        // IPv4: four octets 0-255 separated by dots
        private static readonly Regex IPv4Regex = new(@"(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)", RegexOptions.Compiled);

        // IPv6: comprehensive pattern covering full, compressed, and IPv4-mapped forms
        private static readonly Regex IPv6Regex = new(@"(?:(?:[0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}|(?:[0-9a-fA-F]{1,4}:){1,7}:|(?:[0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|(?:[0-9a-fA-F]{1,4}:){1,5}(?::[0-9a-fA-F]{1,4}){1,2}|(?:[0-9a-fA-F]{1,4}:){1,4}(?::[0-9a-fA-F]{1,4}){1,3}|(?:[0-9a-fA-F]{1,4}:){1,3}(?::[0-9a-fA-F]{1,4}){1,4}|(?:[0-9a-fA-F]{1,4}:){1,2}(?::[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:(?::[0-9a-fA-F]{1,4}){1,6}|:(?::[0-9a-fA-F]{1,4}){1,7}|::(?:[fF]{4}(?::0{1,4})?:)?(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)|(?:[0-9a-fA-F]{1,4}:){1,4}:(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)|fe80:(?::[0-9a-fA-F]{0,4}){0,4}%[0-9a-zA-Z]{1,})", RegexOptions.Compiled);

        // Discord snowflake IDs: 17-19 digit numbers (word-bounded)
        private static readonly Regex DiscordIdRegex = new(@"\b\d{17,19}\b", RegexOptions.Compiled);

        private static readonly string[] InjectionMarkers = new[]
        {
            "ignore previous instructions",
            "system prompt",
            "DAN",
            "ignore all previous",
            "disregard previous",
            "forget previous",
            "override instructions",
            "ignore your instructions",
            "bypass restrictions",
            "jailbreak",
            "do anything now",
            "pretend to be",
            "you are now",
            "new instructions",
            "replace your"
        };

        /// <summary>
        /// Replaces PII in the input with non-reversible tokens.
        /// Does not mutate the original string.
        /// </summary>
        public static string Redact(string input, List<string>? playerNames = null)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = input;

            // 1. Replace SteamID64s first (they are also 17 digits)
            result = SteamId64Regex.Replace(result, "[STEAMID]");

            // 2. Replace IPv4 addresses
            result = IPv4Regex.Replace(result, "[IP]");

            // 3. Replace IPv6 addresses
            result = IPv6Regex.Replace(result, "[IP]");

            // 4. Replace Discord snowflake IDs (remaining 17-19 digit sequences)
            result = DiscordIdRegex.Replace(result, "[DISCORD]");

            // 5. Replace player names with indexed tokens
            if (playerNames != null)
            {
                var uniqueNames = playerNames
                    .Where(n => !string.IsNullOrWhiteSpace(n) && n.Length >= 2)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(n => n.Length)
                    .ToList();

                int playerIndex = 1;
                foreach (var name in uniqueNames)
                {
                    if (result.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result = Regex.Replace(result, Regex.Escape(name), $"[PLAYER:{playerIndex}]", RegexOptions.IgnoreCase);
                        playerIndex++;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Validates a prompt for known prompt-injection markers.
        /// Throws <see cref="PromptInjectionException"/> if a marker is found.
        /// </summary>
        public static void ValidatePrompt(string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return;

            var lower = prompt.ToLowerInvariant();
            foreach (var marker in InjectionMarkers)
            {
                if (lower.Contains(marker))
                {
                    throw new PromptInjectionException($"Input rejected: contains blocked injection marker '{marker}'.");
                }
            }
        }

        /// <summary>
        /// Wraps a prompt with sentinel delimiters to help the LLM distinguish
        /// user data from system instructions.
        /// </summary>
        public static string WrapWithDelimiters(string prompt)
        {
            return $"<user_input>{prompt}</user_input>";
        }
    }
}
