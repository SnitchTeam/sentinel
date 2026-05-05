using System;
using System.Collections.Generic;
using System.Reflection;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelRuntimeTests
    {
        private class TestableSentinel : SentinelPlugin
        {
            public List<string> Logs { get; } = new();

            public override void Puts(string message) => Logs.Add(message);
            public override void PrintWarning(string message) => Logs.Add($"[WARN] {message}");
            public override void PrintError(string message) => Logs.Add($"[ERROR] {message}");
        }

        private class OxideSentinel : TestableSentinel
        {
            protected override RuntimeType DetectRuntime() => RuntimeType.Oxide;
        }

        private class CarbonSentinel : TestableSentinel
        {
            protected override RuntimeType DetectRuntime() => RuntimeType.Carbon;
        }

        private class ThrowingSentinel : TestableSentinel
        {
            protected override RuntimeType DetectRuntime() => throw new Exception("Simulated detection failure");
        }

        private class UnknownSentinel : TestableSentinel
        {
            protected override RuntimeType DetectRuntime() => RuntimeType.Unknown;
        }

        [Fact]
        public void RuntimeBridge_DetectsOxide_AndLogsRuntime()
        {
            var plugin = new OxideSentinel();
            plugin.InitializeRuntimeBridge();

            Assert.Equal(RuntimeType.Oxide, plugin.DetectedRuntime);
            Assert.Contains(plugin.Logs, l => l.Contains("Runtime=Oxide"));
        }

        [Fact]
        public void RuntimeBridge_DetectsCarbon_AndLogsRuntime()
        {
            var plugin = new CarbonSentinel();
            plugin.InitializeRuntimeBridge();

            Assert.Equal(RuntimeType.Carbon, plugin.DetectedRuntime);
            Assert.Contains(plugin.Logs, l => l.Contains("Runtime=Carbon"));
        }

        [Fact]
        public void RuntimeBridge_InitializesWithoutExceptions()
        {
            var plugin = new TestableSentinel();
            var ex = Record.Exception(() => plugin.InitializeRuntimeBridge());

            Assert.Null(ex);
            Assert.NotEqual(RuntimeType.Unknown, plugin.DetectedRuntime);
        }

        [Fact]
        public void RuntimeBridge_FallsBackToOxide_WhenDetectionThrows()
        {
            var plugin = new ThrowingSentinel();
            var ex = Record.Exception(() => plugin.InitializeRuntimeBridge());

            Assert.Null(ex);
            Assert.Equal(RuntimeType.Oxide, plugin.DetectedRuntime);
        }

        [Fact]
        public void RuntimeBridge_UnknownRuntime_FallsBackToOxide()
        {
            var plugin = new UnknownSentinel();
            plugin.InitializeRuntimeBridge();

            Assert.Equal(RuntimeType.Oxide, plugin.DetectedRuntime);
            Assert.Contains(plugin.Logs, l => l.Contains("Runtime=Oxide"));
        }

        [Fact]
        public void RuntimeBridge_OxideBridge_LogsInfo()
        {
            var plugin = new OxideSentinel();
            plugin.InitializeRuntimeBridge();
            plugin.Logs.Clear();

            var bridge = GetRuntimeBridge(plugin);
            Assert.NotNull(bridge);
            bridge.LogInfo("Test info message");

            Assert.Contains("Test info message", plugin.Logs);
        }

        [Fact]
        public void RuntimeBridge_OxideBridge_LogsWarning()
        {
            var plugin = new OxideSentinel();
            plugin.InitializeRuntimeBridge();
            plugin.Logs.Clear();

            var bridge = GetRuntimeBridge(plugin);
            Assert.NotNull(bridge);
            bridge.LogWarning("Test warning message");

            Assert.Contains("[WARN] Test warning message", plugin.Logs);
        }

        [Fact]
        public void RuntimeBridge_OxideBridge_LogsError()
        {
            var plugin = new OxideSentinel();
            plugin.InitializeRuntimeBridge();
            plugin.Logs.Clear();

            var bridge = GetRuntimeBridge(plugin);
            Assert.NotNull(bridge);
            bridge.LogError("Test error message");

            Assert.Contains("[ERROR] Test error message", plugin.Logs);
        }

        [Fact]
        public void RuntimeBridge_CarbonBridge_LogsInfo()
        {
            var plugin = new CarbonSentinel();
            plugin.InitializeRuntimeBridge();
            plugin.Logs.Clear();

            var bridge = GetRuntimeBridge(plugin);
            Assert.NotNull(bridge);
            bridge.LogInfo("Test carbon info");

            Assert.Contains("Test carbon info", plugin.Logs);
        }

        [Fact]
        public void RuntimeBridge_BridgeIsNotNull_AfterInit()
        {
            var plugin = new TestableSentinel();
            plugin.InitializeRuntimeBridge();

            var bridge = GetRuntimeBridge(plugin);
            Assert.NotNull(bridge);
        }

        [Fact]
        public void RuntimeBridge_DetectedRuntime_DefaultsToUnknown_BeforeInit()
        {
            var plugin = new TestableSentinel();
            Assert.Equal(RuntimeType.Unknown, plugin.DetectedRuntime);
        }

        private static IRuntimeBridge? GetRuntimeBridge(SentinelPlugin plugin)
        {
            var field = typeof(SentinelPlugin).GetField("_runtimeBridge", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(plugin) as IRuntimeBridge;
        }
    }
}
