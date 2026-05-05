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

        // -------------------------------------------------------------
        // Panel Lifecycle
        // -------------------------------------------------------------
        public void OpenPanel(BasePlayer player)
        {
            if (player == null) return;

            var steamId = player.UserIDString;

            // Prevent stacking — destroy any existing panel first
            ClosePanel(player);

            // Build default view (Dashboard) as the panel framework entry point
            var container = BuildDashboardView(steamId);
            var rootName = "s_d_" + steamId;

            CuiHelper.AddUi(player, container);

            _playerPanelOpen[steamId] = true;
            _playerPanelRootNames[steamId] = rootName;

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

            _playerPanelOpen[steamId] = false;
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
                "players" => BuildPlayersView(steamId),
                "logs" => BuildLogsView(steamId),
                "bans" => BuildBansView(steamId),
                "config" => BuildConfigView(steamId),
                "ai" => BuildAiView(steamId),
                "permissions" => BuildPermissionsView(steamId),
                _ => BuildDashboardView(steamId)
            };

            var rootName = container[0].Name;
            CuiHelper.AddUi(player, container);

            _playerPanelOpen[steamId] = true;
            _playerPanelRootNames[steamId] = rootName;

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
            if (PluginConfig?.Cui?.HotkeyEnabled == false) return;

            // Production hook: delegates to the testable handler.
            // In a real Oxide runtime the InputState bitmask would be inspected
            // for the configured hotkey before calling HandleHotkeyPress.
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
    }
}
