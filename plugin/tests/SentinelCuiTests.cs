using System;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;
using Oxide.Plugins;
using Oxide.Core;

namespace Sentinel.Tests
{
    public class SentinelCuiTests
    {
        private class TestableSentinel : SentinelPlugin
        {
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }
        }

        private static readonly string _pid = "76561198000000001";

        private static readonly AiSuggestion _testSuggestion = new()
        {
            Id = "test-suggestion-001",
            PlayerName = "PlayerA",
            SteamId = "76561190000000001",
            Behavior = "aim",
            Confidence = 85,
            RecommendedAction = "ban",
            Reason = "Aim assistance detected",
            DurationMinutes = 1440,
            AgentName = "AntiCheat"
        };

        private static CuiElementContainer GetView(TestableSentinel plugin, string viewName)
        {
            return viewName switch
            {
                "Dashboard" => plugin.BuildDashboardView(_pid),
                "Players" => plugin.BuildPlayersView(_pid),
                "Logs" => plugin.BuildLogsView(_pid),
                "Bans" => plugin.BuildBansView(_pid),
                "Config" => plugin.BuildConfigView(_pid),
                "AI" => plugin.BuildAiView(_pid, _testSuggestion),
                "Permissions" => plugin.BuildPermissionsView(_pid),
                _ => throw new ArgumentException("Unknown view: " + viewName)
            };
        }

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Players")]
        [InlineData("Logs")]
        [InlineData("Bans")]
        [InlineData("Config")]
        [InlineData("AI")]
        [InlineData("Permissions")]
        public void ViewPayload_DoesNotExceed4096Bytes(string viewName)
        {
            var plugin = new TestableSentinel();
            var container = GetView(plugin, viewName);
            var json = CuiHelper.ToJson(container);
            Assert.True(json.Length <= 4096, $"{viewName} payload is {json.Length} bytes, exceeds 4096");
        }

        [Fact]
        public void DesignToken_Background_IsExactHex()
        {
            Assert.Equal("#0a0a0a", SentinelPlugin.CUI_COLOR_BACKGROUND);
        }

        [Fact]
        public void DesignToken_PrimaryText_IsExactHex()
        {
            Assert.Equal("#fafafa", SentinelPlugin.CUI_COLOR_PRIMARY_TEXT);
        }

        [Fact]
        public void DesignToken_SecondaryText_IsExactHex()
        {
            Assert.Equal("#aab2c0", SentinelPlugin.CUI_COLOR_SECONDARY_TEXT);
        }

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Players")]
        [InlineData("Logs")]
        [InlineData("Bans")]
        [InlineData("Config")]
        [InlineData("AI")]
        [InlineData("Permissions")]
        public void ViewPayload_ContainsCorrectBackgroundColor(string viewName)
        {
            var plugin = new TestableSentinel();
            var container = GetView(plugin, viewName);
            var json = CuiHelper.ToJson(container);
            Assert.Contains("#0a0a0a", json);
        }

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Players")]
        [InlineData("Logs")]
        [InlineData("Bans")]
        [InlineData("Config")]
        [InlineData("AI")]
        [InlineData("Permissions")]
        public void ViewPayload_ContainsCorrectPrimaryTextColor(string viewName)
        {
            var plugin = new TestableSentinel();
            var container = GetView(plugin, viewName);
            var json = CuiHelper.ToJson(container);
            Assert.Contains("#fafafa", json);
        }

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Players")]
        [InlineData("Logs")]
        [InlineData("Bans")]
        [InlineData("Config")]
        [InlineData("AI")]
        [InlineData("Permissions")]
        public void ViewPayload_ContainsCorrectSecondaryTextColor(string viewName)
        {
            var plugin = new TestableSentinel();
            var container = GetView(plugin, viewName);
            var json = CuiHelper.ToJson(container);
            Assert.Contains("#aab2c0", json);
        }

        [Fact]
        public void Builder_ProducesValidPanelWithImageAndRectTransform()
        {
            var plugin = new TestableSentinel();
            var c = plugin.NewCuiContainer();
            plugin.AddPanel(c, "p1", "Overlay", "#0a0a0a", "0 0", "1 1", "0 0", "0 0");
            var json = CuiHelper.ToJson(c);
            Assert.Contains("\"type\":\"UnityEngine.UI.Image\"", json);
            Assert.Contains("\"type\":\"RectTransform\"", json);
            Assert.Contains("\"name\":\"p1\"", json);
            Assert.Contains("\"parent\":\"Overlay\"", json);
            Assert.Contains("\"sprite\":\"assets/icons/icon.png\"", json);
        }

        [Fact]
        public void Builder_ProducesValidButtonWithCommand()
        {
            var plugin = new TestableSentinel();
            var c = plugin.NewCuiContainer();
            plugin.AddButton(c, "b1", "Overlay", "Click", "#2563eb", "#fafafa", "sentinel.cmd", "0 0", "1 1", "0 0", "0 0");
            var json = CuiHelper.ToJson(c);
            Assert.Contains("\"type\":\"UnityEngine.UI.Button\"", json);
            Assert.Contains("\"command\":\"sentinel.cmd\"", json);
            Assert.Contains("\"type\":\"UnityEngine.UI.Text\"", json);
        }

        [Fact]
        public void Builder_ProducesValidInputField()
        {
            var plugin = new TestableSentinel();
            var c = plugin.NewCuiContainer();
            plugin.AddInputField(c, "i1", "Overlay", "Type here", "#fafafa", "sentinel.input", "0 0", "1 1", "0 0", "0 0");
            var json = CuiHelper.ToJson(c);
            Assert.Contains("\"type\":\"UnityEngine.UI.InputField\"", json);
            Assert.Contains("\"placeholder\":\"Type here\"", json);
            Assert.Contains("\"needskeyboard\":true", json);
        }

        [Fact]
        public void Builder_PanelColor_IsExactHex()
        {
            var plugin = new TestableSentinel();
            var c = plugin.NewCuiContainer();
            plugin.AddPanel(c, "p1", "Overlay", SentinelPlugin.CUI_COLOR_BACKGROUND, "0 0", "1 1", "0 0", "0 0");
            var json = CuiHelper.ToJson(c);
            Assert.Contains("\"color\":\"#0a0a0a\"", json);
        }

        [Fact]
        public void Builder_LabelTextColor_IsExactHex()
        {
            var plugin = new TestableSentinel();
            var c = plugin.NewCuiContainer();
            plugin.AddLabel(c, "l1", "Overlay", "Hello", SentinelPlugin.CUI_COLOR_PRIMARY_TEXT, 14, "0 0", "1 1", "0 0", "0 0");
            var json = CuiHelper.ToJson(c);
            Assert.Contains("\"color\":\"#fafafa\"", json);
        }

        [Fact]
        public void Builder_SecondaryLabelTextColor_IsExactHex()
        {
            var plugin = new TestableSentinel();
            var c = plugin.NewCuiContainer();
            plugin.AddLabel(c, "l1", "Overlay", "Meta", SentinelPlugin.CUI_COLOR_SECONDARY_TEXT, 10, "0 0", "1 1", "0 0", "0 0");
            var json = CuiHelper.ToJson(c);
            Assert.Contains("\"color\":\"#aab2c0\"", json);
        }

        [Fact]
        public void PayloadSize_Helper_ReturnsCorrectByteCount()
        {
            var plugin = new TestableSentinel();
            var c = plugin.NewCuiContainer();
            plugin.AddPanel(c, "p1", "Overlay", "#0a0a0a", "0 0", "1 1", "0 0", "0 0");
            var size = plugin.GetCuiPayloadSize(c);
            var json = CuiHelper.ToJson(c);
            Assert.Equal(json.Length, size);
        }

        // -------------------------------------------------------------
        // Panel Framework Tests
        // -------------------------------------------------------------

        private class PanelTestableSentinel : SentinelPlugin
        {
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }
            public void SetConfig(SentinelConfig config) => PluginConfig = config;
        }

        private class PanelMockPermission : Oxide.Core.Libraries.Permission
        {
            private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> _perms = new();

            public void Grant(string userId, string perm)
            {
                if (!_perms.ContainsKey(userId))
                    _perms[userId] = new System.Collections.Generic.HashSet<string>();
                _perms[userId].Add(perm);
            }

            public override bool UserHasPermission(string id, string perm)
            {
                if (_perms.TryGetValue(id, out var perms))
                    return perms.Contains(perm) || perms.Contains("sentinel.*");
                return false;
            }

            public override void RegisterPermission(string perm, Oxide.Plugins.RustPlugin owner) { }
        }

        private class PanelTestPlayer : BasePlayer
        {
            public System.Collections.Generic.List<string> ChatMessages { get; } = new();
            public override void ChatMessage(string message) => ChatMessages.Add(message);
        }

        private static PanelTestPlayer CreateTestPlayer(string steamId, string name)
        {
            return new PanelTestPlayer
            {
                UserIDString = steamId,
                displayName = name
            };
        }

        private static System.Reflection.MethodInfo? GetCommandMethod(string methodName)
        {
            return typeof(SentinelPlugin).GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }

        private static ConsoleSystem.Arg BuildArg(string[]? args, BasePlayer? player = null)
        {
            var arg = new ConsoleSystem.Arg();
            typeof(ConsoleSystem.Arg).GetProperty("Args")
                ?.SetValue(arg, args ?? System.Array.Empty<string>());
            typeof(ConsoleSystem.Arg).GetProperty("_player")
                ?.SetValue(arg, player);
            return arg;
        }

        [Fact]
        public void Panel_OpenPanel_SetsStateOpen()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);

            Assert.True(plugin.IsPanelOpen("76561198000000001"));
            Assert.NotNull(plugin.GetPanelRootName("76561198000000001"));
        }

        [Fact]
        public void Panel_ClosePanel_SetsStateClosed()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);
            plugin.ClosePanel(player);

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
            Assert.Null(plugin.GetPanelRootName("76561198000000001"));
        }

        [Fact]
        public void Panel_OpenPanel_PreventsStacking()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);
            var firstRoot = plugin.GetPanelRootName("76561198000000001");
            plugin.OpenPanel(player);
            var secondRoot = plugin.GetPanelRootName("76561198000000001");

            Assert.Equal(firstRoot, secondRoot);
            Assert.True(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_TogglePanel_OpensWhenClosed()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.TogglePanel(player);

            Assert.True(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_TogglePanel_ClosesWhenOpen()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);
            plugin.TogglePanel(player);

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_ChatCommand_TogglesPanel()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            var method = GetCommandMethod("ChatCmdSentinel");
            Assert.NotNull(method);

            // First call opens
            method!.Invoke(plugin, new object[] { player, "sentinel", System.Array.Empty<string>() });
            Assert.True(plugin.IsPanelOpen("76561198000000001"));

            // Second call closes
            method.Invoke(plugin, new object[] { player, "sentinel", System.Array.Empty<string>() });
            Assert.False(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_ChatCommand_WithoutPermission_IsBlocked()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            // Do NOT grant sentinel.panel
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            var method = GetCommandMethod("ChatCmdSentinel");
            Assert.NotNull(method);

            method!.Invoke(plugin, new object[] { player, "sentinel", System.Array.Empty<string>() });

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
            Assert.Contains("don't have permission", player.ChatMessages[0]);
        }

        [Fact]
        public void Panel_ConsoleOpen_OpensPanel()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            var method = GetCommandMethod("CCmdPanelOpen");
            Assert.NotNull(method);

            var arg = BuildArg(System.Array.Empty<string>(), player);
            method!.Invoke(plugin, new object[] { arg });

            Assert.True(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_ConsoleClose_ClosesPanel()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);

            var method = GetCommandMethod("CCmdPanelClose");
            Assert.NotNull(method);

            var arg = BuildArg(System.Array.Empty<string>(), player);
            method!.Invoke(plugin, new object[] { arg });

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_ConsoleToggle_TogglesPanel()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            var method = GetCommandMethod("CCmdPanelToggle");
            Assert.NotNull(method);

            var arg = BuildArg(System.Array.Empty<string>(), player);

            // Toggle open
            method!.Invoke(plugin, new object[] { arg });
            Assert.True(plugin.IsPanelOpen("76561198000000001"));

            // Toggle close
            method.Invoke(plugin, new object[] { arg });
            Assert.False(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_ConsoleToggle_TenIterations_Consistent()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            var method = GetCommandMethod("CCmdPanelToggle");
            Assert.NotNull(method);

            var arg = BuildArg(System.Array.Empty<string>(), player);

            for (int i = 0; i < 10; i++)
            {
                bool expectedOpen = (i % 2) == 0; // 0,2,4,6,8 -> open after toggle
                method!.Invoke(plugin, new object[] { arg });
                Assert.Equal(expectedOpen, plugin.IsPanelOpen("76561198000000001"));
            }
        }

        [Fact]
        public void Panel_ConsoleReload_RefreshesOpenPanel()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);
            var oldRoot = plugin.GetPanelRootName("76561198000000001");

            var method = GetCommandMethod("CCmdPanelReload");
            Assert.NotNull(method);

            var arg = BuildArg(System.Array.Empty<string>(), player);
            method!.Invoke(plugin, new object[] { arg });

            Assert.True(plugin.IsPanelOpen("76561198000000001"));
            // Root should be recreated (same name pattern but state preserved)
            Assert.NotNull(plugin.GetPanelRootName("76561198000000001"));
        }

        [Fact]
        public void Panel_ConsoleReload_WhenClosed_KeepsClosed()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            var method = GetCommandMethod("CCmdPanelReload");
            Assert.NotNull(method);

            var arg = BuildArg(System.Array.Empty<string>(), player);
            method!.Invoke(plugin, new object[] { arg });

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_Hotkey_OpensPanel()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.HandleHotkeyPress(player);

            Assert.True(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_Hotkey_ClosesPanel()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);
            plugin.HandleHotkeyPress(player);

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_Hotkey_WhileTyping_DoesNotTrigger()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.SetPlayerTyping("76561198000000001", true);
            plugin.HandleHotkeyPress(player);

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_Hotkey_WithoutPermission_DoesNotOpen()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            // No sentinel.panel granted
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.HandleHotkeyPress(player);

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_Hotkey_DisabledInConfig_DoesNotOpen()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;
            plugin.SetConfig(new SentinelConfig { Cui = new CuiPanelConfig { HotkeyEnabled = false } });

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.HandleHotkeyPress(player);

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void Panel_Config_HasDefaultHotkeyK()
        {
            var config = new SentinelConfig();
            Assert.Equal("K", config.Cui.Hotkey);
            Assert.True(config.Cui.HotkeyEnabled);
        }

        // -------------------------------------------------------------
        // View Content Validation
        // -------------------------------------------------------------

        [Fact]
        public void Dashboard_ContainsThreatCount()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildDashboardView(_pid));
            Assert.Contains("Threats", json);
        }

        [Fact]
        public void Dashboard_ContainsStatusIndicator()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildDashboardView(_pid));
            Assert.Contains("Status", json);
            Assert.Contains("Online", json);
        }

        [Fact]
        public void Dashboard_ContainsRecentAlerts()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildDashboardView(_pid));
            Assert.Contains("Alert 1", json);
            Assert.Contains("Alert 2", json);
            Assert.Contains("HIGH", json);
            Assert.Contains("MED", json);
        }

        [Fact]
        public void Dashboard_ContainsQuickActionButtons()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildDashboardView(_pid));
            Assert.Contains("SCAN", json);
            Assert.Contains("PLAYERS", json);
            Assert.Contains("BANS", json);
            Assert.Contains("sentinel.scan", json);
            Assert.Contains("sentinel.view players", json);
            Assert.Contains("sentinel.view bans", json);
        }

        [Fact]
        public void Players_ContainsSearchInput()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildPlayersView(_pid));
            Assert.Contains("Search", json);
            Assert.Contains("sentinel.search", json);
            Assert.Contains("UnityEngine.UI.InputField", json);
        }

        [Fact]
        public void Players_ContainsPlayerRowWithAllActions()
        {
            var plugin = new PlayerCuiTestableSentinel();
            plugin.AddTestPlayer(76561190000000001, "Player0");
            var json = CuiHelper.ToJson(plugin.BuildPlayersView(_pid));
            Assert.Contains("Player0", json);
            Assert.Contains("sentinel.warn 76561190000000001", json);
            Assert.Contains("sentinel.kick 76561190000000001", json);
            Assert.Contains("sentinel.ban 76561190000000001", json);
            Assert.Contains("sentinel.inspect 76561190000000001", json);
        }

        [Fact]
        public void Players_RowActions_WithinPanelBounds()
        {
            var plugin = new TestableSentinel();
            var container = plugin.BuildPlayersView(_pid);
            // Ensure all anchors are within 0-1 range
            foreach (var el in container)
            {
                foreach (var comp in el.Components)
                {
                    if (comp is CuiRectTransformComponent rt)
                    {
                        var mins = rt.AnchorMin.Split(' ');
                        var maxs = rt.AnchorMax.Split(' ');
                        Assert.True(float.Parse(mins[0], System.Globalization.CultureInfo.InvariantCulture) >= 0 && float.Parse(maxs[0], System.Globalization.CultureInfo.InvariantCulture) <= 1, "X anchors out of bounds");
                        Assert.True(float.Parse(mins[1], System.Globalization.CultureInfo.InvariantCulture) >= 0 && float.Parse(maxs[1], System.Globalization.CultureInfo.InvariantCulture) <= 1, "Y anchors out of bounds");
                    }
                }
            }
        }

        [Fact]
        public void Logs_ContainsTimestampedEntries()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildLogsView(_pid));
            Assert.Contains("05-06", json);
            Assert.Contains("Log entry 1", json);
            Assert.Contains("Log entry 2", json);
        }

        [Fact]
        public void Logs_ContainsSeverityBadges()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildLogsView(_pid));
            Assert.Contains("[ERR]", json);
            Assert.Contains("[WARN]", json);
        }

        [Fact]
        public void Logs_ContainsFilterControls()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildLogsView(_pid));
            Assert.Contains("sentinel.logseverity", json);
            Assert.Contains("ALL", json);
            Assert.Contains("ERR", json);
        }

        [Fact]
        public void Logs_ContainsPagination()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildLogsView(_pid));
            Assert.Contains("PREV", json);
            Assert.Contains("NEXT", json);
            Assert.Contains("sentinel.logpage prev", json);
            Assert.Contains("sentinel.logpage next", json);
            Assert.Contains("1 / 5", json);
        }

        [Fact]
        public void Bans_ContainsBanListWithNamesReasonsExpiry()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildBansView(_pid));
            Assert.Contains("Player0", json);
            Assert.Contains("Player1", json);
            Assert.Contains("Cheating", json);
            Assert.Contains("7d", json);
        }

        [Fact]
        public void Bans_ContainsUnbanAction()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildBansView(_pid));
            Assert.Contains("UNBAN", json);
            Assert.Contains("sentinel.unban", json);
        }

        [Fact]
        public void Bans_ContainsFilterAndSortControls()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildBansView(_pid));
            Assert.Contains("sentinel.banfilter", json);
            Assert.Contains("sentinel.bansort", json);
            Assert.Contains("SORT", json);
            Assert.Contains("UnityEngine.UI.InputField", json);
        }

        [Fact]
        public void Config_ContainsCategorizedSettings()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildConfigView(_pid));
            Assert.Contains("AuditLog", json);
            Assert.Contains("Discord", json);
        }

        [Fact]
        public void Config_ContainsToggleButtons()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildConfigView(_pid));
            Assert.Contains("ON", json);
            Assert.Contains("sentinel.togglecfg", json);
        }

        [Fact]
        public void Config_ContainsNumericInput()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildConfigView(_pid));
            Assert.Contains("Daily Cap", json);
            Assert.Contains("sentinel.cfgnum", json);
            Assert.Contains("UnityEngine.UI.InputField", json);
        }

        [Fact]
        public void Config_ContainsSaveButton()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildConfigView(_pid));
            Assert.Contains("SAVE", json);
            Assert.Contains("sentinel.savecfg", json);
        }

        [Fact]
        public void Ai_ContainsModelStatus()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildAiView(_pid));
            Assert.Contains("Model:", json);
            Assert.Contains("gpt-4o-mini", json);
            Assert.Contains("Online", json);
        }

        [Fact]
        public void Ai_ContainsConfidenceThreshold()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildAiView(_pid));
            Assert.Contains("Threshold", json);
            Assert.Contains("75%", json);
        }

        [Fact]
        public void Ai_ContainsSuggestionQueueWithActions()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildAiView(_pid, _testSuggestion));
            Assert.Contains("PlayerA", json);
            Assert.Contains("aim", json);
            Assert.Contains("85%", json);
            Assert.Contains("ACCEPT", json);
            Assert.Contains("REJECT", json);
            Assert.Contains("EDIT", json);
            Assert.Contains("sentinel.ai accept", json);
            Assert.Contains("sentinel.ai reject", json);
            Assert.Contains("sentinel.ai edit", json);
        }

        [Fact]
        public void Ai_WithoutSuggestion_ShowsEmptyMessage()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildAiView(_pid));
            Assert.Contains("No AI suggestions pending", json);
        }

        [Fact]
        public void AiEditView_ContainsPreFilledFields()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildAiEditView(_pid, _testSuggestion));
            Assert.Contains("PlayerA", json);
            Assert.Contains("76561190000000001", json);
            Assert.Contains("aim", json);
            Assert.Contains("85%", json);
            Assert.Contains("ban", json);
            Assert.Contains("SAVE", json);
            Assert.Contains("CANCEL", json);
            Assert.Contains("sentinel.ai save", json);
            Assert.Contains("sentinel.view ai", json);
            Assert.Contains("sentinel.ai.edit.reason", json);
            Assert.Contains("sentinel.ai.edit.duration", json);
        }

        [Fact]
        public void AiEditView_Payload_DoesNotExceed4096Bytes()
        {
            var plugin = new TestableSentinel();
            var container = plugin.BuildAiEditView(_pid, _testSuggestion);
            var json = CuiHelper.ToJson(container);
            Assert.True(json.Length <= 4096, $"AI Edit view payload is {json.Length} bytes, exceeds 4096");
        }

        [Fact]
        public void Ai_ContainsResponseHistory()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildAiView(_pid));
            Assert.Contains("HISTORY", json);
            Assert.Contains("Triage result", json);
        }

        [Fact]
        public void Permissions_ContainsRoleList()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildPermissionsView(_pid));
            Assert.Contains("admin", json);
            Assert.Contains("moderator", json);
        }

        [Fact]
        public void Permissions_ContainsPermissionMatrix()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildPermissionsView(_pid));
            Assert.Contains("sentinel.*", json);
            Assert.Contains("kick,ban,warn", json);
        }

        [Fact]
        public void Permissions_ContainsAddRemoveControls()
        {
            var plugin = new TestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildPermissionsView(_pid));
            Assert.Contains("sentinel.perm add", json);
            Assert.Contains("sentinel.perm remove", json);
            Assert.Contains("UnityEngine.UI.Button", json);
        }

        // -------------------------------------------------------------
        // View Switching
        // -------------------------------------------------------------

        [Theory]
        [InlineData("dashboard")]
        [InlineData("players")]
        [InlineData("logs")]
        [InlineData("bans")]
        [InlineData("config")]
        [InlineData("ai")]
        [InlineData("permissions")]
        public void SwitchView_OpensRequestedView(string viewName)
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.SwitchView(player, viewName);

            Assert.True(plugin.IsPanelOpen("76561198000000001"));
            var root = plugin.GetPanelRootName("76561198000000001");
            Assert.NotNull(root);
            Assert.StartsWith("s_", root);
        }

        [Fact]
        public void SwitchView_WithoutPermission_DoesNotOpen()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.SwitchView(player, "players");

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void ConsoleViewCommand_SwitchesToPlayers()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            var method = GetCommandMethod("CCmdSwitchView");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { "players" }, player);
            method!.Invoke(plugin, new object[] { arg });

            Assert.True(plugin.IsPanelOpen("76561198000000001"));
        }

        [Fact]
        public void ConsoleViewCommand_InvalidView_FallsBackToDashboard()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            var method = GetCommandMethod("CCmdSwitchView");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { "unknown" }, player);
            method!.Invoke(plugin, new object[] { arg });

            Assert.True(plugin.IsPanelOpen("76561198000000001"));
            var root = plugin.GetPanelRootName("76561198000000001");
            Assert.Contains("s_d_", root);
        }

        // -------------------------------------------------------------
        // Dynamic Players View
        // -------------------------------------------------------------

        private class PlayerCuiTestableSentinel : SentinelPlugin
        {
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }

            public void AddTestPlayer(ulong steamId, string name)
            {
                OnPlayerConnected(new BasePlayer { UserIDString = steamId.ToString(), displayName = name });
            }
        }

        [Fact]
        public void PlayersView_WithNoPlayers_ShowsEmptyMessage()
        {
            var plugin = new PlayerCuiTestableSentinel();
            var json = CuiHelper.ToJson(plugin.BuildPlayersView(_pid));
            Assert.Contains("No matching players", json);
            Assert.Contains("PLAYERS (0)", json);
        }

        [Fact]
        public void PlayersView_WithPlayers_ShowsCorrectRowCount()
        {
            var plugin = new PlayerCuiTestableSentinel();
            plugin.AddTestPlayer(76561190000000001, "Alice");
            plugin.AddTestPlayer(76561190000000002, "Bob");

            var container = plugin.BuildPlayersView(_pid);
            var json = CuiHelper.ToJson(container);
            Assert.Contains("PLAYERS (2)", json);
            // Only the first player is rendered due to 4KB payload limit
            Assert.Contains("Alice", json);
            Assert.DoesNotContain("Bob", json);
        }

        [Fact]
        public void PlayersView_WithSearchQuery_FiltersResults()
        {
            var plugin = new PlayerCuiTestableSentinel();
            plugin.AddTestPlayer(76561190000000001, "Alice");
            plugin.AddTestPlayer(76561190000000002, "BobTheBuilder");
            plugin.AddTestPlayer(76561190000000003, "Charlie");

            var container = plugin.BuildPlayersView(_pid, "bob");
            var json = CuiHelper.ToJson(container);
            Assert.Contains("PLAYERS (1)", json);
            Assert.Contains("BobTheBuilder", json);
            Assert.DoesNotContain("Alice", json);
            Assert.DoesNotContain("Charlie", json);
        }

        [Fact]
        public void PlayersView_Search_IsCaseInsensitive()
        {
            var plugin = new PlayerCuiTestableSentinel();
            plugin.AddTestPlayer(76561190000000001, "Alice");
            plugin.AddTestPlayer(76561190000000002, "BOB");

            var container = plugin.BuildPlayersView(_pid, "alice");
            var json = CuiHelper.ToJson(container);
            Assert.Contains("Alice", json);

            var container2 = plugin.BuildPlayersView(_pid, "BOB");
            var json2 = CuiHelper.ToJson(container2);
            Assert.Contains("BOB", json2);
        }

        [Fact]
        public void PlayersView_Search_ByExactSteamId()
        {
            var plugin = new PlayerCuiTestableSentinel();
            plugin.AddTestPlayer(76561190000000001, "Alice");
            plugin.AddTestPlayer(76561190000000002, "Bob");

            var container = plugin.BuildPlayersView(_pid, "76561190000000002");
            var json = CuiHelper.ToJson(container);
            Assert.Contains("PLAYERS (1)", json);
            Assert.Contains("Bob", json);
            Assert.DoesNotContain("Alice", json);
        }

        [Fact]
        public void PlayersView_RowActions_ContainCorrectSteamIds()
        {
            var plugin = new PlayerCuiTestableSentinel();
            plugin.AddTestPlayer(76561190000000001, "Alice");

            var container = plugin.BuildPlayersView(_pid);
            var json = CuiHelper.ToJson(container);
            Assert.Contains("sentinel.warn 76561190000000001", json);
            Assert.Contains("sentinel.kick 76561190000000001", json);
            Assert.Contains("sentinel.ban 76561190000000001", json);
            Assert.Contains("sentinel.inspect 76561190000000001", json);
        }

        [Fact]
        public void PlayersView_RowActions_EmitsCorrectCommands_ForMultiplePlayers()
        {
            var plugin = new PlayerCuiTestableSentinel();
            plugin.AddTestPlayer(76561190000000001, "Alice");
            plugin.AddTestPlayer(76561190000000002, "Bob");

            var container = plugin.BuildPlayersView(_pid);
            var json = CuiHelper.ToJson(container);
            // Only the first player's row is rendered due to 4KB payload limit
            Assert.Contains("sentinel.warn 76561190000000001", json);
            Assert.Contains("sentinel.kick 76561190000000001", json);
            Assert.Contains("sentinel.ban 76561190000000001", json);
            Assert.Contains("sentinel.inspect 76561190000000001", json);
            Assert.DoesNotContain("sentinel.warn 76561190000000002", json);
            Assert.DoesNotContain("sentinel.kick 76561190000000002", json);
            Assert.DoesNotContain("sentinel.ban 76561190000000002", json);
            Assert.DoesNotContain("sentinel.inspect 76561190000000002", json);
        }

        [Fact]
        public void PlayersView_Payload_WithMaxRows_DoesNotExceed4096Bytes()
        {
            var plugin = new PlayerCuiTestableSentinel();
            // Add a player to ensure the dynamic row is rendered
            plugin.AddTestPlayer(76561190000000001, "Player1");

            var container = plugin.BuildPlayersView(_pid);
            var json = CuiHelper.ToJson(container);
            Assert.True(json.Length <= 4096, $"Players view with 1 row is {json.Length} bytes, exceeds 4096");
        }

        [Fact]
        public void PlayerSearch_Command_RebuildsViewWithFilteredResults()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.OnPlayerConnected(player);

            var method = GetCommandMethod("CCmdPlayerSearch");
            Assert.NotNull(method);

            // Search for self
            var arg = BuildArg(new[] { "Test" }, player);
            method!.Invoke(plugin, new object[] { arg });

            Assert.True(plugin.IsPanelOpen("76561198000000001"));
            var root = plugin.GetPanelRootName("76561198000000001");
            Assert.NotNull(root);
            Assert.StartsWith("s_p_", root);
        }

        [Fact]
        public void PlayerSearch_Command_WithoutPermission_IsBlocked()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            // Do NOT grant sentinel.panel
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            var method = GetCommandMethod("CCmdPlayerSearch");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { "test" }, player);
            method!.Invoke(plugin, new object[] { arg });

            Assert.False(plugin.IsPanelOpen("76561198000000001"));
            Assert.Contains("don't have permission", player.ChatMessages[0]);
        }

        // -------------------------------------------------------------
        // Performance: Zero FPS Impact When Panel is Closed
        // -------------------------------------------------------------

        private class FlagTrackingPlayer : PanelTestPlayer
        {
            public System.Collections.Generic.Dictionary<string, bool> Flags { get; } = new();
            public override void SetPlayerFlag(string flag, bool value) => Flags[flag] = value;
        }

        private static FlagTrackingPlayer CreateFlagTrackingPlayer(string steamId, string name)
        {
            return new FlagTrackingPlayer
            {
                UserIDString = steamId,
                displayName = name
            };
        }

        [Fact]
        public void Performance_OpenPanel_SetsNeedsCursorFlag()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateFlagTrackingPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);

            Assert.True(player.Flags.TryGetValue("NeedsCursor", out var cursor) && cursor,
                "OpenPanel must set NeedsCursor=true so the client enables raycast/interaction logic.");
        }

        [Fact]
        public void Performance_ClosePanel_ClearsNeedsCursorFlag()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateFlagTrackingPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);
            plugin.ClosePanel(player);

            Assert.True(player.Flags.TryGetValue("NeedsCursor", out var cursor) && !cursor,
                "ClosePanel must set NeedsCursor=false to disable client-side raycasts and input capture.");
        }

        [Fact]
        public void Performance_IsAnyPanelOpen_TrueWhenAtLeastOnePanelOpen()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            Assert.False(plugin.IsAnyPanelOpen());

            plugin.OpenPanel(player);
            Assert.True(plugin.IsAnyPanelOpen());
        }

        [Fact]
        public void Performance_IsAnyPanelOpen_FalseWhenAllPanelsClosed()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);
            plugin.ClosePanel(player);

            Assert.False(plugin.IsAnyPanelOpen());
        }

        [Fact]
        public void Performance_GlobalCounter_AccurateAfterMultipleOpenClose()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            perm.Grant("76561198000000002", "sentinel.panel");
            plugin.permission = perm;

            var p1 = CreateTestPlayer("76561198000000001", "A");
            var p2 = CreateTestPlayer("76561198000000002", "B");

            plugin.OpenPanel(p1);
            plugin.OpenPanel(p2);
            Assert.True(plugin.IsAnyPanelOpen());

            plugin.ClosePanel(p1);
            Assert.True(plugin.IsAnyPanelOpen());

            plugin.ClosePanel(p2);
            Assert.False(plugin.IsAnyPanelOpen());
        }

        [Fact]
        public void Performance_SwitchView_MaintainsCursorFlag()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateFlagTrackingPlayer("76561198000000001", "TestPlayer");
            plugin.SwitchView(player, "players");

            Assert.True(player.Flags.TryGetValue("NeedsCursor", out var cursor) && cursor,
                "SwitchView must set NeedsCursor=true after mounting the new panel.");
        }

        [Fact]
        public void Performance_SearchCommand_MaintainsCursorFlag()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateFlagTrackingPlayer("76561198000000001", "TestPlayer");
            var method = GetCommandMethod("CCmdPlayerSearch");
            Assert.NotNull(method);

            var arg = BuildArg(new[] { "test" }, player);
            method!.Invoke(plugin, new object[] { arg });

            Assert.True(player.Flags.TryGetValue("NeedsCursor", out var cursor) && cursor,
                "Player search command must set NeedsCursor=true after mounting the panel.");
        }

        [Fact]
        public void Performance_ShouldProcessCuiWork_MatchesAnyPanelOpen()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            Assert.Equal(plugin.IsAnyPanelOpen(), plugin.ShouldProcessCuiWork());

            plugin.OpenPanel(player);
            Assert.True(plugin.ShouldProcessCuiWork());

            plugin.ClosePanel(player);
            Assert.False(plugin.ShouldProcessCuiWork());
        }

        [Fact]
        public void Performance_ReOpenAfterClose_SetsCursorFlagAgain()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateFlagTrackingPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);
            plugin.ClosePanel(player);
            plugin.OpenPanel(player);

            Assert.True(player.Flags.TryGetValue("NeedsCursor", out var cursor) && cursor,
                "Re-opening panel after close must restore NeedsCursor=true.");
        }

        [Fact]
        public void Performance_CloseIdempotent_DoesNotUnderflowCounter()
        {
            var plugin = new PanelTestableSentinel();
            var perm = new PanelMockPermission();
            perm.Grant("76561198000000001", "sentinel.panel");
            plugin.permission = perm;

            var player = CreateTestPlayer("76561198000000001", "TestPlayer");
            plugin.OpenPanel(player);
            plugin.ClosePanel(player);
            plugin.ClosePanel(player); // idempotent
            plugin.ClosePanel(player); // idempotent

            Assert.False(plugin.IsAnyPanelOpen());
        }
    }
}
