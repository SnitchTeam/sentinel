using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Sentinel", "Snitch Team", "1.0.0")]
    [Description("AI Admin & Anti-Cheat Suite for Rust")]
    public partial class Sentinel : RustPlugin
    {
        private void Init()
        {
            Puts("Sentinel initialized.");
        }
    }
}
