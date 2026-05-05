using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Sentinel", "Snitch Team", "1.0.0")]
    [Description("AI Admin & Anti-Cheat Suite for Rust")]
    public partial class Sentinel : RustPlugin
    {
        private void Init()
        {
            LoadPluginConfig();
            InitializeRuntimeBridge();
            InitializeDatabase(GetDatabasePath());
            _runtimeBridge?.LogInfo("[Sentinel] Sentinel initialized.");
        }

        private void Unload()
        {
            CloseDatabase();
        }
    }
}
