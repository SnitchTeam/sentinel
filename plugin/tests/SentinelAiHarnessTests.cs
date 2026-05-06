using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sentinel.AiHarness;
using Xunit;

namespace Sentinel.Tests
{
    public class SentinelAiHarnessTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly AiValidationHarness _harness;

        public SentinelAiHarnessTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_harness_test_{Guid.NewGuid()}.db");
            _harness = new AiValidationHarness(_dbPath);
        }

        public void Dispose()
        {
            _harness.Cleanup();
        }

        [Fact]
        public void Harness_RunAll_ProducesReport_WithFiveModels()
        {
            var report = _harness.RunAll();

            Assert.NotNull(report);
            Assert.Equal(5, report.Models.Count);
            Assert.Contains(report.Models, m => m.ModelName == "Triage");
            Assert.Contains(report.Models, m => m.ModelName == "BanDraft");
            Assert.Contains(report.Models, m => m.ModelName == "Search");
            Assert.Contains(report.Models, m => m.ModelName == "RuleLookup");
            Assert.Contains(report.Models, m => m.ModelName == "AntiCheat");
        }

        [Fact]
        public void Harness_Report_ContainsSummary_WithNonZeroTotals()
        {
            var report = _harness.RunAll();

            Assert.True(report.Summary.TotalTests > 0, "Total tests should be > 0");
            Assert.True(report.Summary.PassedTests > 0, "Passed tests should be > 0");
            Assert.Equal(5, report.Summary.ModelsEvaluated);
        }

        [Fact]
        public void Harness_Triage_AccuracyWithinTolerance()
        {
            var report = _harness.RunAll();
            var triage = report.Models.First(m => m.ModelName == "Triage");

            Assert.InRange(triage.Accuracy, 0.85, 1.0);
            Assert.InRange(triage.F1Score, 0.70, 1.0);
            Assert.True(triage.TotalCases > 0);
        }

        [Fact]
        public void Harness_BanDraft_AccuracyWithinTolerance()
        {
            var report = _harness.RunAll();
            var banDraft = report.Models.First(m => m.ModelName == "BanDraft");

            Assert.InRange(banDraft.Accuracy, 0.85, 1.0);
            Assert.True(banDraft.TotalCases > 0);
        }

        [Fact]
        public void Harness_Search_AccuracyWithinTolerance()
        {
            var report = _harness.RunAll();
            var search = report.Models.First(m => m.ModelName == "Search");

            Assert.InRange(search.Accuracy, 0.85, 1.0);
            Assert.True(search.TotalCases > 0);
        }

        [Fact]
        public void Harness_RuleLookup_AccuracyWithinTolerance()
        {
            var report = _harness.RunAll();
            var ruleLookup = report.Models.First(m => m.ModelName == "RuleLookup");

            Assert.InRange(ruleLookup.Accuracy, 0.60, 1.0);
            Assert.True(ruleLookup.TotalCases > 0);
        }

        [Fact]
        public void Harness_AntiCheat_AccuracyWithinTolerance()
        {
            var report = _harness.RunAll();
            var antiCheat = report.Models.First(m => m.ModelName == "AntiCheat");

            Assert.InRange(antiCheat.Accuracy, 0.85, 1.0);
            Assert.InRange(antiCheat.F1Score, 0.70, 1.0);
            Assert.True(antiCheat.TotalCases > 0);
        }

        [Fact]
        public void Harness_Report_HasTimestamp()
        {
            var report = _harness.RunAll();
            Assert.False(string.IsNullOrWhiteSpace(report.Timestamp));
            Assert.True(DateTimeOffset.TryParse(report.Timestamp, out _));
        }

        [Fact]
        public void Harness_Report_SerializesToJson()
        {
            var report = _harness.RunAll();
            var json = ReportSerializer.ToJson(report);

            Assert.False(string.IsNullOrWhiteSpace(json));
            Assert.Contains("\"overall_status\"", json);
            Assert.Contains("\"models\"", json);
            Assert.Contains("\"accuracy\"", json);
            Assert.Contains("\"f1_score\"", json);
        }

        [Fact]
        public void Harness_Report_AllModelsHaveDriftFlag()
        {
            var report = _harness.RunAll();
            foreach (var model in report.Models)
            {
                Assert.True(model.DriftFlag == "PASS" || model.DriftFlag == "FAIL",
                    $"Model {model.ModelName} drift flag should be PASS or FAIL");
            }
        }

        [Fact]
        public void Harness_Report_Consistency_TotalEqualsTPPlusFPPlusTNPlusFN()
        {
            var report = _harness.RunAll();
            foreach (var model in report.Models)
            {
                var sum = model.TruePositives + model.FalsePositives + model.TrueNegatives + model.FalseNegatives;
                Assert.Equal(model.TotalCases, sum);
            }
        }

        [Fact]
        public void Harness_DriftDetection_AccuracyWithinTwoPercentOfBaseline_IsPass()
        {
            var report = _harness.RunAll();
            foreach (var model in report.Models)
            {
                double delta = model.BaselineAccuracy - model.Accuracy;
                if (delta <= 0.02)
                {
                    Assert.Equal("PASS", model.DriftFlag);
                }
            }
        }

        [Fact]
        public void Harness_OverallStatus_PassWhenAllModelsPass()
        {
            var report = _harness.RunAll();
            if (report.Models.All(m => m.Status == "PASS"))
            {
                Assert.Equal("PASS", report.OverallStatus);
            }
            else
            {
                Assert.Equal("FAIL", report.OverallStatus);
            }
        }

        [Fact]
        public void Harness_MockDataset_Triage_HasExpectedDistribution()
        {
            var dataset = MockDatasetGenerator.GenerateTriageDataset(100);
            var anomalous = dataset.Count(d => d.ExpectedAnomalous);
            Assert.InRange(anomalous, 15, 25); // ~20% anomalous with seed 42
        }

        [Fact]
        public void Harness_MockDataset_AntiCheat_HasExpectedDistribution()
        {
            var dataset = MockDatasetGenerator.GenerateAntiCheatDataset(80);
            var cheaters = dataset.Count(d => d.ExpectedCheater);
            Assert.InRange(cheaters, 15, 25); // ~25% cheaters with seed 42
        }

        [Fact]
        public void Harness_MockDataset_Search_ContainsMaliciousQueries()
        {
            var dataset = MockDatasetGenerator.GenerateSearchDataset(40);
            var malicious = dataset.Count(d => !d.ExpectedValid);
            Assert.True(malicious > 0, "Dataset should contain some malicious queries");
        }
    }
}
