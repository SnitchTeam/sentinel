using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oxide.Core;

namespace Oxide.Plugins
{
    public readonly struct TickMeasurement
    {
        public DateTime Timestamp { get; init; }
        public double ElapsedMs { get; init; }
    }

    public readonly struct TickStatistics
    {
        public int Count { get; init; }
        public double MeanMs { get; init; }
        public double AverageMs { get; init; }
        public double P95Ms { get; init; }
        public double MinMs { get; init; }
        public double MaxMs { get; init; }
        public TimeSpan Duration { get; init; }
        public bool IsWithinBudget { get; init; }
    }

    public class TickProfiler
    {
        private readonly Queue<TickMeasurement> _measurements;
        private readonly int _capacity;
        private readonly object _lock = new();
        private bool _isProfiling;
        private long _currentTickStart;
        private DateTime _profilingStartTime;

        public TickProfiler(int capacity = 40000)
        {
            _capacity = capacity;
            _measurements = new Queue<TickMeasurement>(capacity);
        }

        public bool IsProfiling => _isProfiling;
        public int MeasurementCount
        {
            get
            {
                lock (_lock)
                {
                    return _measurements.Count;
                }
            }
        }

        public void StartProfiling()
        {
            lock (_lock)
            {
                _measurements.Clear();
                _isProfiling = true;
                _profilingStartTime = DateTime.UtcNow;
            }
        }

        public void StopProfiling()
        {
            lock (_lock)
            {
                _isProfiling = false;
            }
        }

        public void BeginTick()
        {
            if (!_isProfiling) return;
            _currentTickStart = Stopwatch.GetTimestamp();
        }

        public void EndTick()
        {
            if (!_isProfiling) return;
            var elapsedTicks = Stopwatch.GetTimestamp() - _currentTickStart;
            var elapsedMs = (elapsedTicks * 1000.0) / Stopwatch.Frequency;
            RecordMeasurement(elapsedMs);
        }

        public void RecordMeasurement(double elapsedMs)
        {
            lock (_lock)
            {
                if (!_isProfiling) return;
                _measurements.Enqueue(new TickMeasurement
                {
                    Timestamp = DateTime.UtcNow,
                    ElapsedMs = elapsedMs
                });
                while (_measurements.Count > _capacity)
                {
                    _measurements.Dequeue();
                }
            }
        }

        public TickStatistics GetStatistics()
        {
            lock (_lock)
            {
                var count = _measurements.Count;
                if (count == 0)
                {
                    return new TickStatistics
                    {
                        Count = 0,
                        MeanMs = 0,
                        AverageMs = 0,
                        P95Ms = 0,
                        MinMs = 0,
                        MaxMs = 0,
                        Duration = TimeSpan.Zero,
                        IsWithinBudget = true
                    };
                }

                var values = new List<double>(count);
                double sum = 0;
                double min = double.MaxValue;
                double max = double.MinValue;

                foreach (var m in _measurements)
                {
                    var v = m.ElapsedMs;
                    values.Add(v);
                    sum += v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }

                var mean = sum / count;
                values.Sort();
                var p95Index = (int)Math.Ceiling(count * 0.95) - 1;
                if (p95Index < 0) p95Index = 0;
                if (p95Index >= count) p95Index = count - 1;
                var p95 = values[p95Index];

                var duration = _profilingStartTime != default
                    ? DateTime.UtcNow - _profilingStartTime
                    : TimeSpan.Zero;

                // Budget: average < 0.5ms, 95th < 0.5ms, mean < 0.3ms
                var isWithinBudget = mean < 0.3 && p95 < 0.5 && (sum / count) < 0.5;

                return new TickStatistics
                {
                    Count = count,
                    MeanMs = mean,
                    AverageMs = mean,
                    P95Ms = p95,
                    MinMs = min,
                    MaxMs = max,
                    Duration = duration,
                    IsWithinBudget = isWithinBudget
                };
            }
        }

        public string ExportToCsv()
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,ElapsedMs");
                foreach (var m in _measurements)
                {
                    sb.AppendLine(
                        $"{m.Timestamp:O},{m.ElapsedMs.ToString("F6", CultureInfo.InvariantCulture)}");
                }
                return sb.ToString();
            }
        }

        public void ExportToCsv(string filePath)
        {
            var csv = ExportToCsv();
            File.WriteAllText(filePath, csv);
        }
    }

    public partial class Sentinel
    {
        private TickProfiler? _tickProfiler;
        private readonly Dictionary<string, Action> _tickHandlers = new();

        public void InitializeTickProfiler()
        {
            _tickProfiler = new TickProfiler(capacity: 40000);
        }

        public void RegisterTickHandler(string name, Action handler)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _tickHandlers[name] = handler;
        }

        public void UnregisterTickHandler(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _tickHandlers.Remove(name);
        }

        public void OnTick()
        {
            _tickProfiler?.BeginTick();

            try
            {
                foreach (var handler in _tickHandlers.Values.ToList())
                {
                    try
                    {
                        handler();
                    }
                    catch (Exception ex)
                    {
                        PrintError($"[Sentinel] Tick handler exception: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                PrintError($"[Sentinel] OnTick exception: {ex.Message}");
            }

            _tickProfiler?.EndTick();
        }

        // -------------------------------------------------------------
        // Console Commands
        // -------------------------------------------------------------
        [ConsoleCommand("sentinel.tickprofile.start")]
        void CCmdTickProfileStart(ConsoleSystem.Arg arg)
        {
            if (_tickProfiler == null)
            {
                Puts("[Sentinel] Tick profiler not initialized.");
                return;
            }

            _tickProfiler.StartProfiling();
            Puts("[Sentinel] Tick profiling started.");
        }

        [ConsoleCommand("sentinel.tickprofile.stop")]
        void CCmdTickProfileStop(ConsoleSystem.Arg arg)
        {
            if (_tickProfiler == null)
            {
                Puts("[Sentinel] Tick profiler not initialized.");
                return;
            }

            _tickProfiler.StopProfiling();
            var stats = _tickProfiler.GetStatistics();
            Puts($"[Sentinel] Tick profiling stopped. Samples={stats.Count}, Mean={stats.MeanMs:F4}ms, P95={stats.P95Ms:F4}ms, WithinBudget={stats.IsWithinBudget}");
        }

        [ConsoleCommand("sentinel.tickprofile.status")]
        void CCmdTickProfileStatus(ConsoleSystem.Arg arg)
        {
            if (_tickProfiler == null)
            {
                Puts("[Sentinel] Tick profiler not initialized.");
                return;
            }

            var stats = _tickProfiler.GetStatistics();
            Puts($"[Sentinel] Tick Profile Status — Profiling={_tickProfiler.IsProfiling}, Samples={stats.Count}, Duration={stats.Duration.TotalMinutes:F1}min");
            Puts($"[Sentinel]   Mean={stats.MeanMs:F4}ms, Average={stats.AverageMs:F4}ms, P95={stats.P95Ms:F4}ms, Min={stats.MinMs:F4}ms, Max={stats.MaxMs:F4}ms");
            Puts($"[Sentinel]   Budget: mean<0.3ms, avg<0.5ms, p95<0.5ms — Passed={stats.IsWithinBudget}");
        }

        [ConsoleCommand("sentinel.tickprofile.export")]
        void CCmdTickProfileExport(ConsoleSystem.Arg arg)
        {
            if (_tickProfiler == null)
            {
                Puts("[Sentinel] Tick profiler not initialized.");
                return;
            }

            var fileName = arg.Args != null && arg.Args.Length > 0
                ? arg.Args[0]
                : $"sentinel_tick_profile_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

            try
            {
                var path = Path.Combine("oxide", "data", "Sentinel", fileName);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _tickProfiler.ExportToCsv(path);
                Puts($"[Sentinel] Tick profile exported to {path} ({_tickProfiler.MeasurementCount} samples).");
            }
            catch (Exception ex)
            {
                PrintError($"[Sentinel] Failed to export tick profile: {ex.Message}");
            }
        }

        // Public accessors for testing
        public TickProfiler? GetTickProfiler() => _tickProfiler;
    }
}
