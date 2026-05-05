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

        private static CuiElementContainer GetView(TestableSentinel plugin, string viewName)
        {
            return viewName switch
            {
                "Dashboard" => plugin.BuildDashboardView(_pid),
                "Players" => plugin.BuildPlayersView(_pid),
                "Logs" => plugin.BuildLogsView(_pid),
                "Bans" => plugin.BuildBansView(_pid),
                "Config" => plugin.BuildConfigView(_pid),
                "AI" => plugin.BuildAiView(_pid),
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
        public void Builder_ProducesValidPanelWithRawImageAndRectTransform()
        {
            var plugin = new TestableSentinel();
            var c = plugin.NewCuiContainer();
            plugin.AddPanel(c, "p1", "Overlay", "#0a0a0a", "0 0", "1 1", "0 0", "0 0");
            var json = CuiHelper.ToJson(c);
            Assert.Contains("\"type\":\"UnityEngine.UI.RawImage\"", json);
            Assert.Contains("\"type\":\"RectTransform\"", json);
            Assert.Contains("\"name\":\"p1\"", json);
            Assert.Contains("\"parent\":\"Overlay\"", json);
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
    }
}
