using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sentinel.AiHarness
{
    public class HarnessReport
    {
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("O");

        [JsonPropertyName("overall_status")]
        public string OverallStatus { get; set; } = "FAIL";

        [JsonPropertyName("summary")]
        public HarnessSummary Summary { get; set; } = new();

        [JsonPropertyName("models")]
        public List<ModelReport> Models { get; set; } = new();
    }

    public class HarnessSummary
    {
        [JsonPropertyName("total_tests")]
        public int TotalTests { get; set; }

        [JsonPropertyName("passed_tests")]
        public int PassedTests { get; set; }

        [JsonPropertyName("failed_tests")]
        public int FailedTests { get; set; }

        [JsonPropertyName("drift_detected")]
        public bool DriftDetected { get; set; }

        [JsonPropertyName("models_evaluated")]
        public int ModelsEvaluated { get; set; }

        [JsonPropertyName("models_passed")]
        public int ModelsPassed { get; set; }
    }

    public class ModelReport
    {
        [JsonPropertyName("model_name")]
        public string ModelName { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "FAIL";

        [JsonPropertyName("accuracy")]
        public double Accuracy { get; set; }

        [JsonPropertyName("precision")]
        public double Precision { get; set; }

        [JsonPropertyName("recall")]
        public double Recall { get; set; }

        [JsonPropertyName("f1_score")]
        public double F1Score { get; set; }

        [JsonPropertyName("baseline_accuracy")]
        public double BaselineAccuracy { get; set; }

        [JsonPropertyName("drift_flag")]
        public string DriftFlag { get; set; } = "FAIL";

        [JsonPropertyName("drift_delta")]
        public double DriftDelta { get; set; }

        [JsonPropertyName("total_cases")]
        public int TotalCases { get; set; }

        [JsonPropertyName("true_positives")]
        public int TruePositives { get; set; }

        [JsonPropertyName("false_positives")]
        public int FalsePositives { get; set; }

        [JsonPropertyName("true_negatives")]
        public int TrueNegatives { get; set; }

        [JsonPropertyName("false_negatives")]
        public int FalseNegatives { get; set; }

        [JsonPropertyName("details")]
        public List<string> Details { get; set; } = new();
    }

    public static class ReportSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static string ToJson(HarnessReport report)
        {
            return JsonSerializer.Serialize(report, Options);
        }
    }
}
