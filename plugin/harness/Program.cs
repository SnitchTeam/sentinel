using System;
using System.IO;
using System.Threading.Tasks;

namespace Sentinel.AiHarness
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            string? outputPath = null;
            bool verbose = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length)
                        {
                            outputPath = args[++i];
                        }
                        break;
                    case "--verbose":
                    case "-v":
                        verbose = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintHelp();
                        return 0;
                }
            }

            var dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_harness_{Guid.NewGuid()}.db");
            var harness = new AiValidationHarness(dbPath);

            try
            {
                Console.WriteLine("[Sentinel AI Harness] Starting end-to-end inference validation...");
                var report = harness.RunAll();

                var json = ReportSerializer.ToJson(report);

                if (!string.IsNullOrEmpty(outputPath))
                {
                    var dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    await File.WriteAllTextAsync(outputPath, json);
                    Console.WriteLine($"[Sentinel AI Harness] Report written to: {outputPath}");
                }

                if (verbose || string.IsNullOrEmpty(outputPath))
                {
                    Console.WriteLine(json);
                }

                Console.WriteLine($"[Sentinel AI Harness] Overall status: {report.OverallStatus}");
                Console.WriteLine($"[Sentinel AI Harness] Models passed: {report.Summary.ModelsPassed}/{report.Summary.ModelsEvaluated}");
                Console.WriteLine($"[Sentinel AI Harness] Drift detected: {report.Summary.DriftDetected}");

                return report.OverallStatus == "PASS" ? 0 : 1;
            }
            finally
            {
                harness.Cleanup();
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("Sentinel AI Validation Harness");
            Console.WriteLine("Usage: sentinel-ai-validate [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -o, --output <path>   Write JSON report to file");
            Console.WriteLine("  -v, --verbose         Print full JSON report to stdout");
            Console.WriteLine("  -h, --help            Show this help message");
            Console.WriteLine();
            Console.WriteLine("Exit codes:");
            Console.WriteLine("  0  All models PASS, no drift detected");
            Console.WriteLine("  1  One or more models FAIL or drift detected");
        }
    }
}
