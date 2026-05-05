using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oxide.Core;

namespace Oxide.Plugins
{
    public class PluginInfo
    {
        public string Name { get; set; } = "";
        public string? Version { get; set; }
        public string? Author { get; set; }
    }

    public partial class Sentinel
    {
        // -------------------------------------------------------------
        // Runtime bridge to Oxide PluginManager (virtual for testability)
        // -------------------------------------------------------------
        protected virtual bool LoadPluginInternal(string filename)
        {
            try
            {
                var pm = GetOxidePluginManager();
                if (pm == null) return false;

                // Try LoadPlugin(string directory, string name)
                var loadMethod = pm.GetType().GetMethod("LoadPlugin", new[] { typeof(string), typeof(string) });
                if (loadMethod != null)
                {
                    var result = loadMethod.Invoke(pm, new object[] { "plugins", filename });
                    return result != null;
                }

                // Fallback: LoadPlugin(string path)
                loadMethod = pm.GetType().GetMethod("LoadPlugin", new[] { typeof(string) });
                if (loadMethod != null)
                {
                    var path = System.IO.Path.Combine("plugins", filename);
                    var result = loadMethod.Invoke(pm, new object[] { path });
                    return result != null;
                }
            }
            catch { }
            return false;
        }

        protected virtual bool UnloadPluginInternal(string name)
        {
            try
            {
                var pm = GetOxidePluginManager();
                if (pm == null) return false;

                var getPluginMethod = pm.GetType().GetMethod("GetPlugin", new[] { typeof(string) });
                var plugin = getPluginMethod?.Invoke(pm, new object[] { name });
                if (plugin == null) return false;

                var unloadMethod = pm.GetType().GetMethod("UnloadPlugin");
                unloadMethod?.Invoke(pm, new object[] { plugin });
                return true;
            }
            catch { }
            return false;
        }

        protected virtual bool ReloadPluginInternal(string name)
        {
            try
            {
                var pm = GetOxidePluginManager();
                if (pm == null) return false;

                var getPluginMethod = pm.GetType().GetMethod("GetPlugin", new[] { typeof(string) });
                var plugin = getPluginMethod?.Invoke(pm, new object[] { name });
                if (plugin == null) return false;

                var reloadMethod = pm.GetType().GetMethod("ReloadPlugin");
                reloadMethod?.Invoke(pm, new object[] { plugin });
                return true;
            }
            catch { }
            return false;
        }

        protected virtual List<PluginInfo> GetLoadedPluginsInternal()
        {
            var list = new List<PluginInfo>();
            try
            {
                var pm = GetOxidePluginManager();
                if (pm == null) return list;

                var pluginsProp = pm.GetType().GetProperty("Plugins");
                var plugins = pluginsProp?.GetValue(pm) as System.Collections.IEnumerable;
                if (plugins == null) return list;

                foreach (var plugin in plugins)
                {
                    if (plugin == null) continue;
                    var type = plugin.GetType();
                    var nameProp = type.GetProperty("Name");
                    var versionProp = type.GetProperty("Version");
                    var authorProp = type.GetProperty("Author");

                    list.Add(new PluginInfo
                    {
                        Name = nameProp?.GetValue(plugin)?.ToString() ?? "Unknown",
                        Version = versionProp?.GetValue(plugin)?.ToString(),
                        Author = authorProp?.GetValue(plugin)?.ToString()
                    });
                }
            }
            catch { }
            return list;
        }

        private object? GetOxidePluginManager()
        {
            try
            {
                var interfaceType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .FirstOrDefault(t => t.FullName == "Oxide.Core.Interface");

                if (interfaceType == null) return null;

                var oxideProp = interfaceType.GetProperty("Oxide", BindingFlags.Static | BindingFlags.Public);
                var oxide = oxideProp?.GetValue(null);
                if (oxide == null) return null;

                var pmProp = oxide.GetType().GetProperty("PluginManager");
                return pmProp?.GetValue(oxide);
            }
            catch { return null; }
        }

        // -------------------------------------------------------------
        // Load Plugin
        // -------------------------------------------------------------
        public bool ExecuteLoadPlugin(BasePlayer? admin, string filename, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.plugins"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "plugin_load", null, null, false, filename);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            if (string.IsNullOrWhiteSpace(filename))
            {
                error = "Filename is required.";
                LogAuditAction(actorId, actorName, null, null, "plugin_load", null, null, false, "");
                return false;
            }

            var success = LoadPluginInternal(filename);
            if (!success)
            {
                error = $"Failed to load plugin '{filename}'.";
                LogAuditAction(actorId, actorName, null, null, "plugin_load", null, null, false, filename);
                return false;
            }

            LogAuditAction(actorId, actorName, null, null, "plugin_load", null, null, true, filename);
            return true;
        }

        // -------------------------------------------------------------
        // Unload Plugin
        // -------------------------------------------------------------
        public bool ExecuteUnloadPlugin(BasePlayer? admin, string name, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.plugins"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "plugin_unload", null, null, false, name);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Plugin name is required.";
                LogAuditAction(actorId, actorName, null, null, "plugin_unload", null, null, false, "");
                return false;
            }

            var success = UnloadPluginInternal(name);
            if (!success)
            {
                error = $"Failed to unload plugin '{name}'.";
                LogAuditAction(actorId, actorName, null, null, "plugin_unload", null, null, false, name);
                return false;
            }

            LogAuditAction(actorId, actorName, null, null, "plugin_unload", null, null, true, name);
            return true;
        }

        // -------------------------------------------------------------
        // Reload Plugin
        // -------------------------------------------------------------
        public bool ExecuteReloadPlugin(BasePlayer? admin, string name, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.plugins"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "plugin_reload", null, null, false, name);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Plugin name is required.";
                LogAuditAction(actorId, actorName, null, null, "plugin_reload", null, null, false, "");
                return false;
            }

            var success = ReloadPluginInternal(name);
            if (!success)
            {
                error = $"Failed to reload plugin '{name}'.";
                LogAuditAction(actorId, actorName, null, null, "plugin_reload", null, null, false, name);
                return false;
            }

            LogAuditAction(actorId, actorName, null, null, "plugin_reload", null, null, true, name);
            return true;
        }

        // -------------------------------------------------------------
        // List Loaded Plugins
        // -------------------------------------------------------------
        public List<PluginInfo> GetLoadedPlugins()
        {
            return GetLoadedPluginsInternal();
        }

        // -------------------------------------------------------------
        // Console Commands
        // -------------------------------------------------------------
        [ConsoleCommand("sentinel.plugin.load")]
        void CCmdPluginLoad(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.plugin.load <filename>");
                return;
            }

            var admin = arg.Player();
            var filename = arg.Args[0];

            if (!ExecuteLoadPlugin(admin, filename, out var error))
            {
                Puts($"[Sentinel] Plugin load failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Loaded plugin '{filename}'.");
            }
        }

        [ConsoleCommand("sentinel.plugin.unload")]
        void CCmdPluginUnload(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.plugin.unload <name>");
                return;
            }

            var admin = arg.Player();
            var name = arg.Args[0];

            if (!ExecuteUnloadPlugin(admin, name, out var error))
            {
                Puts($"[Sentinel] Plugin unload failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Unloaded plugin '{name}'.");
            }
        }

        [ConsoleCommand("sentinel.plugin.reload")]
        void CCmdPluginReload(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.plugin.reload <name>");
                return;
            }

            var admin = arg.Player();
            var name = arg.Args[0];

            if (!ExecuteReloadPlugin(admin, name, out var error))
            {
                Puts($"[Sentinel] Plugin reload failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Reloaded plugin '{name}'.");
            }
        }

        [ConsoleCommand("sentinel.plugin.list")]
        void CCmdPluginList(ConsoleSystem.Arg arg)
        {
            var admin = arg.Player();
            if (!HasPermission(admin, "sentinel.plugins"))
            {
                Puts("[Sentinel] You don't have permission to manage plugins.");
                return;
            }

            var plugins = GetLoadedPlugins();
            if (plugins.Count == 0)
            {
                Puts("[Sentinel] No plugins loaded.");
                return;
            }

            Puts($"[Sentinel] Loaded plugins ({plugins.Count}):");
            foreach (var p in plugins)
            {
                Puts($"  - {p.Name} v{p.Version} by {p.Author}");
            }
        }
    }
}
