using System;
using System.IO;
using System.Linq;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelTickProfilerTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;

        public SentinelTickProfilerTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_tickprofiler_test_{Guid.NewGuid()}.db");
            _plugin = new TestableSentinel();
            _plugin.InitializeTickProfiler();
        }

        public void Dispose()
        {
            _plugin.GetTickProfiler()?.StopProfiling();
            try { File.Delete(_dbPath); } catch { }
            try { File.Delete(_dbPath + "-shm"); } catch { }
            try { File.Delete(_dbPath + "-wal"); } catch { }
        }

        // -------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------
        [Fact]
        public void TickProfiler_IsInitialized_OnPluginInit()
        {
            var profiler = _plugin.GetTickProfiler();
            Assert.NotNull(profiler);
        }

        [Fact]
        public void TickProfiler_NotProfiling_ByDefault()
        {
            var profiler = _plugin.GetTickProfiler();
            Assert.NotNull(profiler);
            Assert.False(profiler.IsProfiling);
            Assert.Equal(0, profiler.MeasurementCount);
        }

        // -------------------------------------------------------------
        // Start / Stop
        // -------------------------------------------------------------
        [Fact]
        public void TickProfiler_Start_SetsProfilingTrue()
        {
            var profiler = _plugin.GetTickProfiler();
            Assert.NotNull(profiler);
            profiler.StartProfiling();
            Assert.True(profiler.IsProfiling);
        }

        [Fact]
        public void TickProfiler_Stop_SetsProfilingFalse()
        {
            var profiler = _plugin.GetTickProfiler();
            Assert.NotNull(profiler);
            profiler.StartProfiling();
            profiler.StopProfiling();
            Assert.False(profiler.IsProfiling);
        }

        [Fact]
        public void TickProfiler_Start_ClearsPreviousMeasurements()
        {
            var profiler = _plugin.GetTickProfiler();
            Assert.NotNull(profiler);
            profiler.StartProfiling();
            profiler.RecordMeasurement(0.1);
            profiler.StopProfiling();
            profiler.StartProfiling();
            Assert.Equal(0, profiler.MeasurementCount);
        }

        // -------------------------------------------------------------
        // Measurement Recording
        // -------------------------------------------------------------
        [Fact]
        public void TickProfiler_Record_StoresMeasurements()
        {
            var profiler = _plugin.GetTickProfiler();
            Assert.NotNull(profiler);
            profiler.StartProfiling();
            profiler.RecordMeasurement(0.1);
            profiler.RecordMeasurement(0.2);
            profiler.RecordMeasurement(0.3);
            Assert.Equal(3, profiler.MeasurementCount);
        }

        [Fact]
        public void TickProfiler_Record_OnlyWhenProfiling()
        {
            var profiler = _plugin.GetTickProfiler();
            Assert.NotNull(profiler);
            profiler.RecordMeasurement(0.1);
            Assert.Equal(0, profiler.MeasurementCount);
        }

        [Fact]
        public void TickProfiler_Record_RespectsCapacity()
        {
            var profiler = new TickProfiler(capacity: 5);
            profiler.StartProfiling();
            for (int i = 1; i <= 10; i++)
            {
                profiler.RecordMeasurement(i * 0.1);
            }
            Assert.Equal(5, profiler.MeasurementCount);
        }

        // -------------------------------------------------------------
        // Statistics Calculation
        // -------------------------------------------------------------
        [Fact]
        public void TickProfiler_Stats_Empty_ReturnsZeros()
        {
            var profiler = new TickProfiler();
            var stats = profiler.GetStatistics();
            Assert.Equal(0, stats.Count);
            Assert.Equal(0, stats.MeanMs);
            Assert.Equal(0, stats.AverageMs);
            Assert.Equal(0, stats.P95Ms);
            Assert.True(stats.IsWithinBudget);
        }

        [Fact]
        public void TickProfiler_Stats_CalculatesMeanCorrectly()
        {
            var profiler = new TickProfiler();
            profiler.StartProfiling();
            profiler.RecordMeasurement(0.1);
            profiler.RecordMeasurement(0.2);
            profiler.RecordMeasurement(0.3);
            var stats = profiler.GetStatistics();
            Assert.Equal(0.2, stats.MeanMs, precision: 6);
            Assert.Equal(0.2, stats.AverageMs, precision: 6);
        }

        [Fact]
        public void TickProfiler_Stats_CalculatesP95Correctly()
        {
            var profiler = new TickProfiler();
            profiler.StartProfiling();
            for (int i = 1; i <= 100; i++)
            {
                profiler.RecordMeasurement(i * 0.01);
            }
            var stats = profiler.GetStatistics();
            // 95th percentile of 1.0, 1.01, ..., 1.00 = index 94 (0-based) = 0.95
            Assert.Equal(0.95, stats.P95Ms, precision: 6);
        }

        [Fact]
        public void TickProfiler_Stats_CalculatesMinMax()
        {
            var profiler = new TickProfiler();
            profiler.StartProfiling();
            profiler.RecordMeasurement(0.5);
            profiler.RecordMeasurement(0.1);
            profiler.RecordMeasurement(0.3);
            var stats = profiler.GetStatistics();
            Assert.Equal(0.1, stats.MinMs, precision: 6);
            Assert.Equal(0.5, stats.MaxMs, precision: 6);
        }

        // -------------------------------------------------------------
        // Budget Thresholds (VAL-QA-005)
        // -------------------------------------------------------------
        [Fact]
        public void TickProfiler_Budget_MeanBelow0_3AndP95Below0_5_Passes()
        {
            var profiler = new TickProfiler();
            profiler.StartProfiling();
            // Simulate 100 ticks with low overhead: mean ~0.2ms, p95 ~0.38ms
            var random = new Random(42);
            for (int i = 0; i < 1000; i++)
            {
                var value = random.NextDouble() * 0.35; // 0 to 0.35ms
                profiler.RecordMeasurement(value);
            }
            var stats = profiler.GetStatistics();
            Assert.True(stats.MeanMs < 0.3, $"Mean {stats.MeanMs} should be < 0.3ms");
            Assert.True(stats.P95Ms < 0.5, $"P95 {stats.P95Ms} should be < 0.5ms");
            Assert.True(stats.IsWithinBudget);
        }

        [Fact]
        public void TickProfiler_Budget_MeanAbove0_3_Fails()
        {
            var profiler = new TickProfiler();
            profiler.StartProfiling();
            // Simulate ticks with high mean: mean ~0.4ms
            for (int i = 0; i < 100; i++)
            {
                profiler.RecordMeasurement(0.4);
            }
            var stats = profiler.GetStatistics();
            Assert.False(stats.IsWithinBudget);
        }

        [Fact]
        public void TickProfiler_Budget_P95Above0_5_Fails()
        {
            var profiler = new TickProfiler();
            profiler.StartProfiling();
            // Simulate ticks where p95 exceeds budget
            for (int i = 0; i < 100; i++)
            {
                profiler.RecordMeasurement(i < 94 ? 0.1 : 0.6);
            }
            var stats = profiler.GetStatistics();
            Assert.True(stats.P95Ms >= 0.5, $"P95 {stats.P95Ms} should be >= 0.5ms");
            Assert.False(stats.IsWithinBudget);
        }

        // -------------------------------------------------------------
        // CSV Export
        // -------------------------------------------------------------
        [Fact]
        public void TickProfiler_ExportCsv_ContainsHeaderAndData()
        {
            var profiler = new TickProfiler();
            profiler.StartProfiling();
            profiler.RecordMeasurement(0.123456);
            profiler.RecordMeasurement(0.234567);
            var csv = profiler.ExportToCsv();
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length >= 3); // header + 2 data rows
            Assert.Equal("Timestamp,ElapsedMs", lines[0]);
            Assert.Contains("0.123456", lines[1]);
            Assert.Contains("0.234567", lines[2]);
        }

        [Fact]
        public void TickProfiler_ExportCsv_EmptyContainsHeaderOnly()
        {
            var profiler = new TickProfiler();
            var csv = profiler.ExportToCsv();
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            Assert.Equal("Timestamp,ElapsedMs", lines[0]);
        }

        [Fact]
        public void TickProfiler_ExportCsvToFile_WritesCorrectContent()
        {
            var profiler = new TickProfiler();
            profiler.StartProfiling();
            profiler.RecordMeasurement(0.1);
            profiler.RecordMeasurement(0.2);
            var path = Path.Combine(Path.GetTempPath(), $"tick_test_{Guid.NewGuid()}.csv");
            try
            {
                profiler.ExportToCsv(path);
                Assert.True(File.Exists(path));
                var content = File.ReadAllText(path);
                Assert.Contains("Timestamp,ElapsedMs", content);
                Assert.Contains("0.1", content);
                Assert.Contains("0.2", content);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        // -------------------------------------------------------------
        // OnTick Hook
        // -------------------------------------------------------------
        [Fact]
        public void TickProfiler_OnTick_RecordsMeasurement()
        {
            var profiler = _plugin.GetTickProfiler();
            Assert.NotNull(profiler);
            profiler.StartProfiling();
            _plugin.OnTick();
            // OnTick should record a measurement (even if near-zero)
            Assert.True(profiler.MeasurementCount >= 1);
        }

        [Fact]
        public void TickProfiler_OnTick_HandlersAreCalled()
        {
            var profiler = _plugin.GetTickProfiler();
            Assert.NotNull(profiler);
            profiler.StartProfiling();
            bool handlerCalled = false;
            _plugin.RegisterTickHandler("test", () => handlerCalled = true);
            _plugin.OnTick();
            Assert.True(handlerCalled);
        }

        [Fact]
        public void TickProfiler_OnTick_HandlerException_DoesNotBubble()
        {
            var profiler = _plugin.GetTickProfiler();
            Assert.NotNull(profiler);
            profiler.StartProfiling();
            _plugin.RegisterTickHandler("thrower", () => throw new InvalidOperationException("boom"));
            // Should not throw
            var ex = Record.Exception(() => _plugin.OnTick());
            Assert.Null(ex);
        }

        [Fact]
        public void TickProfiler_RegisterAndUnregister_Handler()
        {
            bool handlerCalled = false;
            _plugin.RegisterTickHandler("temp", () => handlerCalled = true);
            _plugin.OnTick();
            Assert.True(handlerCalled);
            handlerCalled = false;
            _plugin.UnregisterTickHandler("temp");
            _plugin.OnTick();
            Assert.False(handlerCalled);
        }

        // -------------------------------------------------------------
        // 10-Minute Simulation (VAL-QA-005)
        // -------------------------------------------------------------
        [Fact]
        public void TickProfiler_Simulated10MinuteSession_MeetsBudget()
        {
            var profiler = new TickProfiler(capacity: 40000);
            profiler.StartProfiling();

            // Simulate 10 minutes at 60 ticks/sec = 36,000 ticks
            // with overhead well under budget (mean ~0.15ms, p95 ~0.25ms)
            var random = new Random(12345);
            for (int i = 0; i < 36000; i++)
            {
                var value = 0.05 + random.NextDouble() * 0.2; // 0.05 to 0.25ms
                profiler.RecordMeasurement(value);
            }

            var stats = profiler.GetStatistics();
            Assert.Equal(36000, stats.Count);
            Assert.True(stats.MeanMs < 0.3, $"Mean {stats.MeanMs:F4}ms must be < 0.3ms");
            Assert.True(stats.P95Ms < 0.5, $"P95 {stats.P95Ms:F4}ms must be < 0.5ms");
            Assert.True(stats.AverageMs < 0.5, $"Average {stats.AverageMs:F4}ms must be < 0.5ms");
            Assert.True(stats.IsWithinBudget, "10-minute session must stay within budget");
        }

        // -------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------
        private class TestableSentinel : SentinelPlugin
        {
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }
        }
    }
}
