using System;
using System.Linq;

namespace Oxide.Plugins
{
    public enum RuntimeType
    {
        Unknown,
        Oxide,
        Carbon
    }

    public interface IRuntimeBridge
    {
        RuntimeType Runtime { get; }
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
    }

    public class OxideBridge : IRuntimeBridge
    {
        public RuntimeType Runtime => RuntimeType.Oxide;
        private readonly Sentinel _plugin;

        public OxideBridge(Sentinel plugin)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        }

        public void LogInfo(string message) => _plugin.Puts(message);
        public void LogWarning(string message) => _plugin.PrintWarning(message);
        public void LogError(string message) => _plugin.PrintError(message);
    }

    public class CarbonBridge : IRuntimeBridge
    {
        public RuntimeType Runtime => RuntimeType.Carbon;
        private readonly Sentinel _plugin;

        public CarbonBridge(Sentinel plugin)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        }

        public void LogInfo(string message) => _plugin.Puts(message);
        public void LogWarning(string message) => _plugin.PrintWarning(message);
        public void LogError(string message) => _plugin.PrintError(message);
    }

    public partial class Sentinel
    {
        private IRuntimeBridge? _runtimeBridge;

        public RuntimeType DetectedRuntime => _runtimeBridge?.Runtime ?? RuntimeType.Unknown;

        public void InitializeRuntimeBridge()
        {
            RuntimeType runtime;
            try
            {
                runtime = DetectRuntime();
            }
            catch
            {
                runtime = RuntimeType.Oxide;
            }

            _runtimeBridge = runtime switch
            {
                RuntimeType.Carbon => new CarbonBridge(this),
                RuntimeType.Oxide => new OxideBridge(this),
                _ => new OxideBridge(this)
            };

            _runtimeBridge.LogInfo($"[Sentinel] Runtime={_runtimeBridge.Runtime}");
        }

        protected virtual RuntimeType DetectRuntime()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in assemblies)
                {
                    string? name = null;
                    try
                    {
                        name = assembly.GetName().Name;
                    }
                    catch
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(name) &&
                        name.Contains("Carbon", StringComparison.OrdinalIgnoreCase))
                    {
                        return RuntimeType.Carbon;
                    }
                }

                var carbonType = Type.GetType("Carbon.CorePlugin, Carbon.Core", throwOnError: false)
                    ?? Type.GetType("Carbon.BasePlugin, Carbon", throwOnError: false)
                    ?? Type.GetType("Carbon.Plugin, Carbon", throwOnError: false);

                if (carbonType != null)
                    return RuntimeType.Carbon;

                return RuntimeType.Oxide;
            }
            catch
            {
                return RuntimeType.Oxide;
            }
        }
    }
}
