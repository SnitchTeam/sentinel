using System;
using System.Collections.Generic;
using System.Globalization;

namespace Oxide.Plugins
{
    public partial class Sentinel
    {
        // ---------------------------------------------------------------
        // Design Tokens
        // ---------------------------------------------------------------
        public const string CUI_COLOR_BACKGROUND = "#0a0a0a";
        public const string CUI_COLOR_PRIMARY_TEXT = "#fafafa";
        public const string CUI_COLOR_SECONDARY_TEXT = "#aab2c0";
        public const string CUI_COLOR_ACCENT = "#2563eb";
        public const string CUI_COLOR_SURFACE = "#141414";
        public const string CUI_COLOR_BORDER = "#1f1f1f";
        public const string CUI_COLOR_DANGER = "#dc2626";
        public const string CUI_COLOR_SUCCESS = "#16a34a";

        // ---------------------------------------------------------------
        // CUI Element Builders
        // ---------------------------------------------------------------
        public CuiElementContainer NewCuiContainer() => new CuiElementContainer();

        public string AddPanel(CuiElementContainer container, string name, string parent,
            string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax,
            string? sprite = null)
        {
            var element = new CuiElement
            {
                Name = name,
                Parent = parent,
                Components = new List<ICuiComponent>
                {
                    new CuiRawImageComponent
                    {
                        Color = color,
                        Sprite = sprite ?? ""
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax
                    }
                }
            };
            return container.Add(element);
        }

        public string AddLabel(CuiElementContainer container, string name, string parent,
            string text, string color, int fontSize, string anchorMin, string anchorMax,
            string offsetMin, string offsetMax, string align = "MiddleLeft")
        {
            var element = new CuiElement
            {
                Name = name,
                Parent = parent,
                Components = new List<ICuiComponent>
                {
                    new CuiTextComponent
                    {
                        Text = text,
                        FontSize = fontSize.ToString(),
                        Color = color,
                        Align = align,
                        Font = "robotocondensed-bold.ttf"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax
                    }
                }
            };
            return container.Add(element);
        }

        public string AddButton(CuiElementContainer container, string name, string parent,
            string text, string buttonColor, string textColor, string command,
            string anchorMin, string anchorMax, string offsetMin, string offsetMax,
            int fontSize = 12)
        {
            var element = new CuiElement
            {
                Name = name,
                Parent = parent,
                Components = new List<ICuiComponent>
                {
                    new CuiButtonComponent
                    {
                        Color = buttonColor,
                        Command = command
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax
                    }
                }
            };
            container.Add(element);

            var textName = name + "_t";
            AddLabel(container, textName, name, text, textColor, fontSize,
                "0 0", "1 1", "0 0", "0 0", "MiddleCenter");
            return name;
        }

        public string AddInputField(CuiElementContainer container, string name, string parent,
            string placeholder, string textColor, string command,
            string anchorMin, string anchorMax, string offsetMin, string offsetMax,
            int fontSize = 12, int charsLimit = 32)
        {
            var element = new CuiElement
            {
                Name = name,
                Parent = parent,
                Components = new List<ICuiComponent>
                {
                    new CuiInputFieldComponent
                    {
                        Text = "",
                        PlaceHolder = placeholder,
                        FontSize = fontSize.ToString(),
                        Color = textColor,
                        Command = command,
                        CharsLimit = charsLimit,
                        Align = "MiddleLeft",
                        NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax
                    }
                }
            };
            return container.Add(element);
        }

        // ---------------------------------------------------------------
        // View Builders (skeleton layouts for size validation)
        // ---------------------------------------------------------------
        public CuiElementContainer BuildDashboardView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_d_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");
            AddLabel(c, "t", root, "DASHBOARD", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.9", "1 1", "10 0", "0 0");
            AddLabel(c, "s", root, "Threats: 0", CUI_COLOR_SECONDARY_TEXT, 12, "0 0.75", "0.3 0.85", "10 0", "0 0");
            AddLabel(c, "a", root, "Status: Online", CUI_COLOR_SECONDARY_TEXT, 12, "0.35 0.75", "0.65 0.85", "10 0", "0 0");
            AddButton(c, "b1", root, "Scan", CUI_COLOR_ACCENT, CUI_COLOR_PRIMARY_TEXT, "sentinel.scan", "0.7 0.75", "0.95 0.85", "0 0", "0 0");
            AddButton(c, "b2", root, "Close", CUI_COLOR_SURFACE, CUI_COLOR_PRIMARY_TEXT, "sentinel.close", "0.85 0.9", "0.98 0.98", "0 0", "0 0");
            return c;
        }

        public CuiElementContainer BuildPlayersView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_p_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");
            AddLabel(c, "t", root, "PLAYERS", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.9", "1 1", "10 0", "0 0");
            AddLabel(c, "st", root, "Online: 2", CUI_COLOR_SECONDARY_TEXT, 10, "0.85 0.9", "0.98 0.98", "0 0", "0 0");
            AddInputField(c, "q", root, "Search...", CUI_COLOR_PRIMARY_TEXT, "sentinel.search ", "0.02 0.82", "0.6 0.88", "10 0", "-10 0");
            AddButton(c, "w", root, "Warn", CUI_COLOR_SURFACE, CUI_COLOR_PRIMARY_TEXT, "sentinel.warn", "0.62 0.82", "0.72 0.88", "0 0", "0 0");
            for (int i = 0; i < 2; i++)
            {
                var yMin = 0.70 - i * 0.22;
                var yMax = yMin + 0.20;
                AddPanel(c, "r" + i, root, CUI_COLOR_SURFACE, "0.02 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.98 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "0 0", "0 0");
                AddLabel(c, "n" + i, "r" + i, "P" + i, CUI_COLOR_PRIMARY_TEXT, 12, "0 0", "0.5 1", "5 0", "0 0");
                AddButton(c, "i" + i, "r" + i, ">", CUI_COLOR_ACCENT, CUI_COLOR_PRIMARY_TEXT, "sentinel.inspect " + i, "0.9 0.1", "0.98 0.9", "0 0", "0 0", 10);
            }
            return c;
        }

        public CuiElementContainer BuildLogsView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_l_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");
            AddLabel(c, "t", root, "LOGS", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.9", "1 1", "10 0", "0 0");
            for (int i = 0; i < 3; i++)
            {
                var yMin = 0.70 - i * 0.22;
                var yMax = yMin + 0.20;
                AddPanel(c, "r" + i, root, CUI_COLOR_SURFACE, "0.02 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.98 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "0 0", "0 0");
                AddLabel(c, "d" + i, "r" + i, "05-06", CUI_COLOR_SECONDARY_TEXT, 10, "0 0", "0.15 1", "5 0", "0 0");
                AddLabel(c, "m" + i, "r" + i, "Action", CUI_COLOR_PRIMARY_TEXT, 11, "0.17 0", "0.9 1", "0 0", "0 0");
            }
            return c;
        }

        public CuiElementContainer BuildBansView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_b_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");
            AddLabel(c, "t", root, "BANS", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.9", "1 1", "10 0", "0 0");
            for (int i = 0; i < 2; i++)
            {
                var yMin = 0.70 - i * 0.22;
                var yMax = yMin + 0.20;
                AddPanel(c, "r" + i, root, CUI_COLOR_SURFACE, "0.02 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.98 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "0 0", "0 0");
                AddLabel(c, "n" + i, "r" + i, "P" + i, CUI_COLOR_PRIMARY_TEXT, 12, "0 0", "0.4 1", "5 0", "0 0");
                AddLabel(c, "e" + i, "r" + i, "7d", CUI_COLOR_SECONDARY_TEXT, 10, "0.42 0", "0.55 1", "0 0", "0 0");
                AddButton(c, "u" + i, "r" + i, "Unban", CUI_COLOR_SUCCESS, CUI_COLOR_PRIMARY_TEXT, "sentinel.unban " + i, "0.88 0.1", "0.98 0.9", "0 0", "0 0", 10);
            }
            return c;
        }

        public CuiElementContainer BuildConfigView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_c_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");
            AddLabel(c, "t", root, "CONFIG", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.9", "1 1", "10 0", "0 0");
            AddLabel(c, "st", root, "v1.0.0", CUI_COLOR_SECONDARY_TEXT, 10, "0.85 0.9", "0.98 0.98", "0 0", "0 0");
            var settings = new[] { "AuditLog", "Discord" };
            for (int i = 0; i < settings.Length; i++)
            {
                var yMin = 0.78 - i * 0.20;
                var yMax = yMin + 0.18;
                AddPanel(c, "r" + i, root, CUI_COLOR_SURFACE, "0.02 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.98 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "0 0", "0 0");
                AddLabel(c, "l" + i, "r" + i, settings[i], CUI_COLOR_PRIMARY_TEXT, 12, "0 0", "0.5 1", "5 0", "0 0");
                AddButton(c, "tg" + i, "r" + i, "Toggle", CUI_COLOR_ACCENT, CUI_COLOR_PRIMARY_TEXT, "sentinel.togglecfg " + settings[i], "0.8 0.2", "0.96 0.8", "0 0", "0 0", 10);
            }
            AddButton(c, "sv", root, "Save", CUI_COLOR_SUCCESS, CUI_COLOR_PRIMARY_TEXT, "sentinel.savecfg", "0.4 0.05", "0.6 0.12", "0 0", "0 0");
            return c;
        }

        public CuiElementContainer BuildAiView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_a_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");
            AddLabel(c, "t", root, "AI", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.9", "1 1", "10 0", "0 0");
            AddLabel(c, "st", root, "Model: Online", CUI_COLOR_SECONDARY_TEXT, 12, "0 0.82", "0.5 0.88", "10 0", "0 0");
            // Suggestion card
            AddPanel(c, "cd", root, CUI_COLOR_SURFACE, "0.02 0.55", "0.98 0.78", "0 0", "0 0");
            AddLabel(c, "cdt", "cd", "Suggestion", CUI_COLOR_PRIMARY_TEXT, 14, "0 0.7", "1 1", "5 0", "0 0");
            AddLabel(c, "cdd", "cd", "Behavior: aim | 85%", CUI_COLOR_SECONDARY_TEXT, 11, "0 0.35", "1 0.65", "5 0", "0 0");
            AddButton(c, "cda", "cd", "Accept", CUI_COLOR_SUCCESS, CUI_COLOR_PRIMARY_TEXT, "sentinel.ai accept", "0.02 0.05", "0.32 0.30", "0 0", "0 0", 11);
            AddButton(c, "cdr", "cd", "Reject", CUI_COLOR_DANGER, CUI_COLOR_PRIMARY_TEXT, "sentinel.ai reject", "0.35 0.05", "0.65 0.30", "0 0", "0 0", 11);
            return c;
        }

        public CuiElementContainer BuildPermissionsView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_r_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");
            AddLabel(c, "t", root, "PERMISSIONS", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.9", "1 1", "10 0", "0 0");
            AddLabel(c, "st", root, "Groups", CUI_COLOR_SECONDARY_TEXT, 10, "0.02 0.84", "0.15 0.88", "5 0", "0 0");
            AddPanel(c, "g0", root, CUI_COLOR_SURFACE, "0.02 0.55", "0.98 0.78", "0 0", "0 0");
            AddLabel(c, "gn0", "g0", "admin", CUI_COLOR_PRIMARY_TEXT, 13, "0 0.6", "0.4 1", "5 0", "0 0");
            AddButton(c, "ga0", "g0", "+", CUI_COLOR_ACCENT, CUI_COLOR_PRIMARY_TEXT, "sentinel.perm add admin", "0.85 0.55", "0.95 1", "0 0", "0 0", 12);
            AddButton(c, "gr0", "g0", "-", CUI_COLOR_DANGER, CUI_COLOR_PRIMARY_TEXT, "sentinel.perm remove admin", "0.85 0.05", "0.95 0.45", "0 0", "0 0", 12);
            return c;
        }

        // ---------------------------------------------------------------
        // Payload validation
        // ---------------------------------------------------------------
        public int GetCuiPayloadSize(CuiElementContainer container) => CuiHelper.ToJson(container).Length;
    }
}
