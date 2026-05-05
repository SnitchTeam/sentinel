using System.Reflection;
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
            RegisterPermissions();
            InitializeDefaultGroups();
            RestoreWorldState();
            EmitBootBanner();
        }

        private void Unload()
        {
            CloseDatabase();
        }

        public override void Puts(string message)
        {
            CaptureConsoleLine(message, "INFO");
            base.Puts(message);
        }

        public override void PrintWarning(string message)
        {
            CaptureConsoleLine(message, "WARN");
            base.PrintWarning(message);
        }

        public override void PrintError(string message)
        {
            CaptureConsoleLine(message, "ERROR");
            base.PrintError(message);
        }

        public void EmitBootBanner()
        {
            var attr = typeof(Sentinel).GetCustomAttribute<InfoAttribute>();
            var version = attr?.Version ?? "unknown";
            var runtime = _runtimeBridge?.Runtime.ToString() ?? "Unknown";
            var dbStatus = (_dbConnection != null && _dbConnection.State == System.Data.ConnectionState.Open)
                ? "Ready"
                : "NotReady";

            _runtimeBridge?.LogInfo($"[Sentinel] Boot — Sentinel v{version} | Runtime={runtime} | Database={dbStatus}");
        }
    }
}
