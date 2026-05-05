using System;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;
using Oxide.Plugins;

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
    }
}
