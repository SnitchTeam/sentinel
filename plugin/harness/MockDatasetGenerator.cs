using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Plugins;

namespace Sentinel.AiHarness
{
    /// <summary>
    /// Generates labeled mock player behavior datasets for AI agent validation.
    /// All datasets are deterministic to ensure reproducible harness runs.
    /// </summary>
    public static class MockDatasetGenerator
    {
        // Fixed seed for reproducibility
        private static readonly Random Rng = new(42);

        public static List<TriageTestCase> GenerateTriageDataset(int count = 60)
        {
            var cases = new List<TriageTestCase>();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Keep total actions <= 500 so all fit in the Triage agent's query window.
            int anomalousCount = count / 6;           // ~16% anomalous
            int normalCount = count - anomalousCount;

            for (int i = 0; i < count; i++)
            {
                var playerId = $"76561190000000{i:D2}";
                bool isAnomalous = i < anomalousCount;
                int actionCount = isAnomalous ? Rng.Next(30, 50) : Rng.Next(1, 3);
                string expectedSeverity = isAnomalous ? "high" : "none";
                double confidence = isAnomalous ? Rng.NextDouble() * 20 + 80 : 0;

                var actions = new List<ActionRecord>();
                for (int j = 0; j < actionCount; j++)
                {
                    actions.Add(new ActionRecord
                    {
                        ActorSteamId = "76561190000000001",
                        ActorName = "Admin",
                        TargetSteamId = playerId,
                        TargetName = $"Player{i}",
                        ActionType = j % 3 == 0 ? "kick" : (j % 3 == 1 ? "warn" : "ban"),
                        Reason = $"Test reason {j}",
                        Timestamp = now - (i * 1000) - j * 60,
                        Success = true
                    });
                }

                cases.Add(new TriageTestCase
                {
                    PlayerId = playerId,
                    Actions = actions,
                    ExpectedAnomalous = isAnomalous,
                    ExpectedSeverity = expectedSeverity,
                    ExpectedConfidence = confidence
                });
            }

            return cases;
        }

        public static List<BanDraftTestCase> GenerateBanDraftDataset(int count = 50)
        {
            var cases = new List<BanDraftTestCase>();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            for (int i = 0; i < count; i++)
            {
                bool shouldHaveCitation = i % 3 != 0; // 66% should have citations
                var evidence = new List<ActionRecord>();
                for (int j = 0; j < 5; j++)
                {
                    evidence.Add(new ActionRecord
                    {
                        ActorSteamId = "76561190000000001",
                        ActorName = "Admin",
                        TargetSteamId = $"76561190000000{i:D2}",
                        TargetName = $"Player{i}",
                        ActionType = "kick",
                        Reason = $"Cheating incident {j}",
                        Timestamp = now - j * 60,
                        Success = true
                    });
                }

                cases.Add(new BanDraftTestCase
                {
                    PlayerSteamId = $"76561190000000{i:D2}",
                    PlayerName = $"Player{i}",
                    Evidence = evidence,
                    ExpectedHasCitation = shouldHaveCitation,
                    RuleIds = new List<string> { "§1.1", "§3.2" }
                });
            }

            return cases;
        }

        public static List<SearchTestCase> GenerateSearchDataset(int count = 40)
        {
            var queries = new List<(string Query, bool ShouldBeValid, string ExpectedTable)>
            {
                ("Show bans from last week", true, "sentinel_bans"),
                ("List all kicks", true, "sentinel_actions"),
                ("Find players in admin group", true, "sentinel_group_members"),
                ("Show recent warnings", true, "sentinel_actions"),
                ("Count active bans", true, "sentinel_bans"),
                ("Delete all players", false, ""),
                ("Drop table sentinel_bans", false, ""),
                ("Insert fake ban", false, ""),
                ("Update ban reasons", false, ""),
                ("Show actions and bans union", false, ""),
            };

            var cases = new List<SearchTestCase>();
            for (int i = 0; i < count; i++)
            {
                var q = queries[i % queries.Count];
                cases.Add(new SearchTestCase
                {
                    NaturalLanguageQuery = q.Query,
                    ExpectedValid = q.ShouldBeValid,
                    ExpectedTable = q.ExpectedTable
                });
            }

            return cases;
        }

        public static List<RuleLookupTestCase> GenerateRuleLookupDataset(int count = 60)
        {
            // Behavior descriptions mapped to the DEFAULT sentinel_rules seeded by the plugin.
            var behaviors = new List<(string Description, List<string> ExpectedRuleIds)>
            {
                ("Player using aimbot and esp wallhack cheat hack", new List<string> { "§1.1" }),
                ("Toxic hate racist slur harass in chat", new List<string> { "§2.1", "§2.2" }),
                ("Exploit bug glitch dupe items for profit", new List<string> { "§1.2" }),
                ("Racist slur toxic hate harass in voice", new List<string> { "§2.1", "§2.2" }),
                ("Building inside rocks exploit bug glitch", new List<string> { "§1.2", "§3.1" }),
                ("Normal farming wood stone gather", new List<string>()),
                ("Player using speed hack bypass circumvent evade", new List<string> { "§1.1", "§5.1" }),
                ("Teamkill grief destroy disrupt teammate base", new List<string> { "§3.1" }),
            };

            var cases = new List<RuleLookupTestCase>();
            for (int i = 0; i < count; i++)
            {
                var b = behaviors[i % behaviors.Count];
                cases.Add(new RuleLookupTestCase
                {
                    BehaviorDescription = b.Description,
                    ExpectedRuleIds = b.ExpectedRuleIds
                });
            }

            return cases;
        }

        public static List<AntiCheatTestCase> GenerateAntiCheatDataset(int count = 80)
        {
            var cases = new List<AntiCheatTestCase>();

            for (int i = 0; i < count; i++)
            {
                var steamId = $"76561190000000{i:D2}";
                // 25% are cheaters (high z-score)
                bool isCheater = i % 4 == 0;
                // Non-cheaters must stay below 3-sigma threshold:
                // mean=0.25, std=0.05 => 3-sigma = 0.40
                double headshotRatio = isCheater ? 0.85 + Rng.NextDouble() * 0.14 : 0.15 + Rng.NextDouble() * 0.19;
                // mean=220, std=30 => 3-sigma = 130 (lower is more suspicious)
                double shotInterval = isCheater ? 50 + Rng.NextDouble() * 30 : 200 + Rng.NextDouble() * 60;

                var metrics = new Dictionary<string, double>
                {
                    { "headshot_ratio", headshotRatio },
                    { "shot_interval_ms", shotInterval }
                };

                var baselines = new Dictionary<string, PlayerBaseline>
                {
                    {
                        "headshot_ratio",
                        new PlayerBaseline
                        {
                            SteamId = steamId,
                            MetricName = "headshot_ratio",
                            Mean = 0.25,
                            StdDev = 0.05,
                            SampleCount = 100,
                            LastUpdated = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
                        }
                    },
                    {
                        "shot_interval_ms",
                        new PlayerBaseline
                        {
                            SteamId = steamId,
                            MetricName = "shot_interval_ms",
                            Mean = 220.0,
                            StdDev = 30.0,
                            SampleCount = 100,
                            LastUpdated = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
                        }
                    }
                };

                cases.Add(new AntiCheatTestCase
                {
                    SteamId = steamId,
                    PlayerName = $"Player{i}",
                    Metrics = metrics,
                    Baselines = baselines,
                    ExpectedCheater = isCheater
                });
            }

            return cases;
        }
    }

    public class TriageTestCase
    {
        public string PlayerId { get; set; } = "";
        public List<ActionRecord> Actions { get; set; } = new();
        public bool ExpectedAnomalous { get; set; }
        public string ExpectedSeverity { get; set; } = "";
        public double ExpectedConfidence { get; set; }
    }

    public class BanDraftTestCase
    {
        public string PlayerSteamId { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public List<ActionRecord> Evidence { get; set; } = new();
        public bool ExpectedHasCitation { get; set; }
        public List<string> RuleIds { get; set; } = new();
    }

    public class SearchTestCase
    {
        public string NaturalLanguageQuery { get; set; } = "";
        public bool ExpectedValid { get; set; }
        public string ExpectedTable { get; set; } = "";
    }

    public class RuleLookupTestCase
    {
        public string BehaviorDescription { get; set; } = "";
        public List<string> ExpectedRuleIds { get; set; } = new();
    }

    public class AntiCheatTestCase
    {
        public string SteamId { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public Dictionary<string, double> Metrics { get; set; } = new();
        public Dictionary<string, PlayerBaseline> Baselines { get; set; } = new();
        public bool ExpectedCheater { get; set; }
    }
}
