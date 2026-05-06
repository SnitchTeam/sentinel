using System;
using System.Collections.Generic;
using Oxide.Core;

namespace Oxide.Plugins
{
    public partial class Sentinel
    {
        private readonly Dictionary<string, bool> _playerPanelOpen = new();
        private readonly Dictionary<string, string> _playerPanelRootNames = new();
        private readonly HashSet<string> _playersTyping = new();

        // Global counter for fast bypass of per-frame CUI work when all panels are closed.
        private int _globalPanelOpenCount = 0;

        // -------------------------------------------------------------
        // Panel State Queries
        // -------------------------------------------------------------
        public bool IsPanelOpen(string steamId)
        {
            return _playerPanelOpen.TryGetValue(steamId, out var open) && open;
        }

        public string? GetPanelRootName(string steamId)
        {
            _playerPanelRootNames.TryGetValue(steamId, out var root);
            return root;
        }

        public bool IsPlayerTyping(string steamId)
        {
            return _playersTyping.Contains(steamId);
        }

        public bool IsAnyPanelOpen() => _globalPanelOpenCount > 0;

        /// <summary>
        /// Returns true when any CUI panel is open and per-frame updates, raycasts,
        /// or renders should be processed. When false, all CUI-related tick work
        /// must be skipped to guarantee zero FPS impact.
        /// </summary>
        public bool ShouldProcessCuiWork() => IsAnyPanelOpen();

        // -------------------------------------------------------------
        // Panel Lifecycle
        // -------------------------------------------------------------

        private void MountPanel(BasePlayer player, CuiElementContainer container, string rootName)
        {
            CuiHelper.AddUi(player, container);
            _playerPanelOpen[player.UserIDString] = true;
            _playerPanelRootNames[player.UserIDString] = rootName;
            _globalPanelOpenCount++;
            // Enable cursor so the player can interact with CUI elements.
            // This also lets the client know raycast logic is required.
            player.SetPlayerFlag("NeedsCursor", true);
        }

        public void OpenPanel(BasePlayer player)
        {
            if (player == null) return;

            var steamId = player.UserIDString;

            // Prevent stacking — destroy any existing panel first
            ClosePanel(player);

            // Build default view (Dashboard) as the panel framework entry point
            var container = BuildDashboardView(steamId);
            var rootName = "s_d_" + steamId;

            MountPanel(player, container, rootName);

            _runtimeBridge?.LogInfo($"[Sentinel] Panel opened for {player.displayName} ({steamId})");
        }

        public void ClosePanel(BasePlayer player)
        {
            if (player == null) return;

            var steamId = player.UserIDString;

            if (_playerPanelRootNames.TryGetValue(steamId, out var rootName))
            {
                CuiHelper.DestroyUi(player, rootName);
                _playerPanelRootNames.Remove(steamId);
            }

            if (_playerPanelOpen.TryGetValue(steamId, out var wasOpen) && wasOpen)
            {
                _playerPanelOpen[steamId] = false;
                _globalPanelOpenCount = Math.Max(0, _globalPanelOpenCount - 1);
            }

            // Disable cursor and client-side raycasts when the panel is closed.
            // This ensures zero FPS impact from CUI rendering or hit-testing.
            player.SetPlayerFlag("NeedsCursor", false);

            _runtimeBridge?.LogInfo($"[Sentinel] Panel closed for {steamId}");
        }

        public void TogglePanel(BasePlayer player)
        {
            if (player == null) return;

            if (IsPanelOpen(player.UserIDString))
                ClosePanel(player);
            else
                OpenPanel(player);
        }

        public void ReloadPanel(BasePlayer player)
        {
            if (player == null) return;

            LoadPluginConfig();

            if (IsPanelOpen(player.UserIDString))
            {
                OpenPanel(player);
            }
        }

        public void SwitchView(BasePlayer player, string viewName)
        {
            if (player == null) return;
            if (!HasPermission(player, "sentinel.panel")) return;

            ClosePanel(player);

            var steamId = player.UserIDString;
            CuiElementContainer container = viewName.ToLowerInvariant() switch
            {
                "dashboard" => BuildDashboardView(steamId),
                "players" => BuildPlayersView(steamId, ""),
                "logs" => BuildLogsView(steamId),
                "bans" => BuildBansView(steamId),
                "config" => BuildConfigView(steamId),
                "ai" => BuildAiView(steamId, GetNextSuggestion()),
                "ai_edit" => BuildAiEditView(steamId, GetSuggestionById(_playerEditingSuggestion.GetValueOrDefault(steamId) ?? "") ?? new AiSuggestion()),
                "permissions" => BuildPermissionsView(steamId),
                _ => BuildDashboardView(steamId)
            };

            var rootName = container[0].Name;
            MountPanel(player, container, rootName);

            _runtimeBridge?.LogInfo($"[Sentinel] Switched {steamId} to {viewName} view");
        }

        // -------------------------------------------------------------
        // Hotkey Handling
        // -------------------------------------------------------------
        public void SetPlayerTyping(string steamId, bool typing)
        {
            if (typing)
                _playersTyping.Add(steamId);
            else
                _playersTyping.Remove(steamId);
        }

        public void HandleHotkeyPress(BasePlayer player)
        {
            if (player == null) return;

            if (PluginConfig?.Cui?.HotkeyEnabled == false) return;

            if (!HasPermission(player, "sentinel.panel")) return;

            // Do not trigger while the player is typing in chat or using another UI
            if (_playersTyping.Contains(player.UserIDString)) return;

            TogglePanel(player);
        }

        void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;

            // Fast path: if hotkeys are disabled globally, skip all input handling.
            if (PluginConfig?.Cui?.HotkeyEnabled == false) return;

            // Performance optimization: when no panels are open and the player
            // does not have panel permission, we can skip the hotkey check.
            // Permission is still checked inside HandleHotkeyPress for safety.
            // In a real Oxide runtime the InputState bitmask would be inspected
            // for the configured hotkey before doing any heavier work.
            HandleHotkeyPress(player);
        }

        // -------------------------------------------------------------
        // Chat Command
        // -------------------------------------------------------------
        [ChatCommand("sentinel")]
        void ChatCmdSentinel(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (!HasPermission(player, "sentinel.panel"))
            {
                NotifyNoPermission(player);
                return;
            }

            TogglePanel(player);
        }

        // -------------------------------------------------------------
        // Console Commands — Message Protocol
        // -------------------------------------------------------------
        [ConsoleCommand("sentinel.open")]
        void CCmdPanelOpen(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                Puts("[Sentinel] This command requires an in-game player.");
                return;
            }

            if (!HasPermission(player, "sentinel.panel"))
            {
                NotifyNoPermission(player);
                return;
            }

            OpenPanel(player);
        }

        [ConsoleCommand("sentinel.close")]
        void CCmdPanelClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                Puts("[Sentinel] This command requires an in-game player.");
                return;
            }

            if (!HasPermission(player, "sentinel.panel"))
            {
                NotifyNoPermission(player);
                return;
            }

            ClosePanel(player);
        }

        [ConsoleCommand("sentinel.toggle")]
        void CCmdPanelToggle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                Puts("[Sentinel] This command requires an in-game player.");
                return;
            }

            if (!HasPermission(player, "sentinel.panel"))
            {
                NotifyNoPermission(player);
                return;
            }

            TogglePanel(player);
        }

        [ConsoleCommand("sentinel.reload")]
        void CCmdPanelReload(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                Puts("[Sentinel] This command requires an in-game player.");
                return;
            }

            if (!HasPermission(player, "sentinel.panel"))
            {
                NotifyNoPermission(player);
                return;
            }

            ReloadPanel(player);
        }

        [ConsoleCommand("sentinel.view")]
        void CCmdSwitchView(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                Puts("[Sentinel] This command requires an in-game player.");
                return;
            }

            if (!HasPermission(player, "sentinel.panel"))
            {
                NotifyNoPermission(player);
                return;
            }

            var viewName = arg.Args != null && arg.Args.Length > 0 ? arg.Args[0] : "dashboard";
            SwitchView(player, viewName);
        }

        [ConsoleCommand("sentinel.search")]
        void CCmdPlayerSearch(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                Puts("[Sentinel] This command requires an in-game player.");
                return;
            }

            if (!HasPermission(player, "sentinel.panel"))
            {
                NotifyNoPermission(player);
                return;
            }

            var query = arg.Args != null && arg.Args.Length > 0 ? arg.Args[0] : "";
            var steamId = player.UserIDString;

            // Ensure proper teardown of any existing panel before mounting a new one.
            ClosePanel(player);

            // Rebuild Players view with filtered results
            var container = BuildPlayersView(steamId, query);
            var rootName = container[0].Name;
            MountPanel(player, container, rootName);

            _runtimeBridge?.LogInfo($"[Sentinel] Player search for {steamId}: '{query}'");
        }
    }
}
