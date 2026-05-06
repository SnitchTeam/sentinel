using System.Reflection;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Sentinel", "Snitch Team", "1.0.0")]
    [Description("AI Admin & Anti-Cheat Suite for Rust")]
    public partial class Sentinel : RustPlugin
    {
        private LlmClient? _llmClient;

        private void Init()
        {
            LoadPluginConfig();
            InitializeRuntimeBridge();
            InitializeDatabase(GetDatabasePath());
            RegisterPermissions();
            InitializeDefaultGroups();
            RestoreWorldState();
            InitializeAiCostTracker();
            InitializeLlmClient();
            InitializeDiscordRouter();
            InitializeWebServer();
            InitializeTickProfiler();
            EmitBootBanner();
        }

        private void Unload()
        {
            _tickProfiler?.StopProfiling();
            StopWebServer();
            StopDiscordRouter();
            CloseDatabase();
        }

        public virtual void InitializeLlmClient()
        {
            var config = PluginConfig?.AI ?? new AIConfig();
            _llmClient = CreateLlmClient(config);

            if (PluginConfig != null)
            {
                PluginConfig.ByokKeyValid = false;
            }

            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                PrintWarning("[Sentinel] No BYOK API key configured. AI features disabled.");
                return;
            }

            var validator = CreateByokValidator();
            var isValid = validator.ValidateAsync(config.Provider, config.Endpoint, config.ApiKey)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            if (PluginConfig != null)
            {
                PluginConfig.ByokKeyValid = isValid;
            }

            if (!isValid)
            {
                PrintError($"[Sentinel] BYOK key rejected for provider {config.Provider}. AI features disabled.");
            }
            else
            {
                Puts($"[Sentinel] BYOK key accepted for provider {config.Provider}.");
            }
        }

        public virtual LlmClient CreateLlmClient(AIConfig config)
        {
            return new LlmClient(new DefaultHttpRequester(), _runtimeBridge, config.MaxRetries, config.TimeoutSeconds);
        }

        public virtual ByokValidator CreateByokValidator()
        {
            return new ByokValidator(new DefaultHttpRequester(), _runtimeBridge);
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
            var version = GetVersionString();
            var runtime = _runtimeBridge?.Runtime.ToString() ?? "Unknown";
            var dbStatus = (_dbConnection != null && _dbConnection.State == System.Data.ConnectionState.Open)
                ? "Ready"
                : "NotReady";

            _runtimeBridge?.LogInfo($"[Sentinel] Boot — Sentinel v{version} | Runtime={runtime} | Database={dbStatus}");
            DispatchDiscordWebhook("system", "Sentinel System", $"Sentinel v{version} started. Runtime={runtime}, Database={dbStatus}.");
        }
    }
}
