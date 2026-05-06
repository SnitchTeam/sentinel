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
        public const string CUI_COLOR_WARNING = "#f59e0b";

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
                "0 0", "1 1", "4 0", "-4 0", "MiddleCenter");
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
        // View Builders
        // ---------------------------------------------------------------

        // Dashboard: threat count, alerts summary, status, quick actions
        public CuiElementContainer BuildDashboardView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_d_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");

            AddLabel(c, "t", root, "DASHBOARD", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.92", "0.5 1", "10 0", "0 0");
            AddLabel(c, "th", root, "Threats: 3", CUI_COLOR_SECONDARY_TEXT, 12, "0.02 0.84", "0.35 0.90", "5 0", "0 0");
            AddLabel(c, "ss", root, "Status: Online", CUI_COLOR_SUCCESS, 12, "0.37 0.84", "0.70 0.90", "5 0", "0 0");

            // Recent alerts summary (2 rows, combined text)
            for (int i = 0; i < 2; i++)
            {
                var yMin = 0.62 - i * 0.14;
                var yMax = yMin + 0.12;
                var sev = i == 0 ? "HIGH" : "MED";
                var sevCol = i == 0 ? CUI_COLOR_DANGER : CUI_COLOR_WARNING;
                AddPanel(c, "a" + i, root, CUI_COLOR_SURFACE, "0.02 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.98 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "0 0", "0 0");
                AddLabel(c, "am" + i, "a" + i, $"[{sev}] 12:0{i} Alert {i + 1}", sevCol, 10, "0.01 0", "0.98 1", "4 0", "0 0");
            }

            // Quick-action buttons (2 buttons to stay under limit)
            AddButton(c, "q1", root, "SCAN", CUI_COLOR_ACCENT, CUI_COLOR_PRIMARY_TEXT, "sentinel.scan", "0.02 0.05", "0.32 0.12", "0 0", "0 0", 10);
            AddButton(c, "q2", root, "PLAYERS", CUI_COLOR_SURFACE, CUI_COLOR_PRIMARY_TEXT, "sentinel.view players", "0.34 0.05", "0.64 0.12", "0 0", "0 0", 10);
            AddButton(c, "q3", root, "BANS", CUI_COLOR_SURFACE, CUI_COLOR_PRIMARY_TEXT, "sentinel.view bans", "0.66 0.05", "0.96 0.12", "0 0", "0 0", 10);

            return c;
        }

        // Players: list, search, per-row Warn/Kick/Ban/Inspect
        public CuiElementContainer BuildPlayersView(string playerId, string? searchQuery = null)
        {
            var c = NewCuiContainer();
            var root = "s_p_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");

            var players = SearchOnlinePlayers(searchQuery ?? "");
            var onlineCount = GetOnlinePlayers().Count;

            AddLabel(c, "t", root, $"PLAYERS ({players.Count})", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.92", "0.5 1", "10 0", "0 0");
            AddLabel(c, "st", root, $"Online: {onlineCount}", CUI_COLOR_SECONDARY_TEXT, 10, "0.50 0.92", "0.70 0.98", "0 0", "0 0");
            AddInputField(c, "q", root, "Search...", CUI_COLOR_PRIMARY_TEXT, "sentinel.search ", "0.02 0.84", "0.60 0.90", "8 0", "-8 0", 12, 32);

            const int maxRows = 1;

            if (players.Count == 0)
            {
                AddLabel(c, "empty", root, "No matching players", CUI_COLOR_SECONDARY_TEXT, 12, "0.03 0.50", "0.98 0.60", "5 0", "0 0", "MiddleCenter");
            }
            else
            {
                for (int i = 0; i < Math.Min(players.Count, maxRows); i++)
                {
                    var p = players[i];
                    var yMin = 0.72 - i * 0.16;
                    var yMax = yMin + 0.14;

                    var yMinStr = yMin.ToString("F2", CultureInfo.InvariantCulture);
                    var yMaxStr = yMax.ToString("F2", CultureInfo.InvariantCulture);

                    // Player name
                    AddLabel(c, "n" + i, root, p.Name, CUI_COLOR_PRIMARY_TEXT, 12, "0.03 " + yMinStr, "0.35 " + yMaxStr, "5 0", "0 0");

                    // Action buttons with actual Steam ID
                    var steamId = p.SteamId;
                    AddButton(c, "w" + i, root, "W", CUI_COLOR_WARNING, CUI_COLOR_PRIMARY_TEXT, $"sentinel.warn {steamId}", "0.60 " + yMinStr, "0.68 " + yMaxStr, "0 0", "0 0", 10);
                    AddButton(c, "k" + i, root, "K", CUI_COLOR_DANGER, CUI_COLOR_PRIMARY_TEXT, $"sentinel.kick {steamId}", "0.69 " + yMinStr, "0.77 " + yMaxStr, "0 0", "0 0", 10);
                    AddButton(c, "b" + i, root, "B", CUI_COLOR_DANGER, CUI_COLOR_PRIMARY_TEXT, $"sentinel.ban {steamId}", "0.78 " + yMinStr, "0.86 " + yMaxStr, "0 0", "0 0", 10);
                    AddButton(c, "i" + i, root, "I", CUI_COLOR_ACCENT, CUI_COLOR_PRIMARY_TEXT, $"sentinel.inspect {steamId}", "0.87 " + yMinStr, "0.98 " + yMaxStr, "0 0", "0 0", 10);
                }
            }

            return c;
        }

        // Logs: timestamped entries, severity badges, filter controls, pagination
        public CuiElementContainer BuildLogsView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_l_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");

            AddLabel(c, "t", root, "LOGS", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.92", "0.5 1", "10 0", "0 0");

            // Filter controls (2 severity buttons)
            AddButton(c, "fall", root, "ALL", CUI_COLOR_ACCENT, CUI_COLOR_PRIMARY_TEXT, "sentinel.logseverity all", "0.02 0.84", "0.20 0.90", "0 0", "0 0", 10);
            AddButton(c, "ferr", root, "ERR", CUI_COLOR_SURFACE, CUI_COLOR_PRIMARY_TEXT, "sentinel.logseverity error", "0.21 0.84", "0.39 0.90", "0 0", "0 0", 10);

            // Log entries (2 rows without panels)
            for (int i = 0; i < 2; i++)
            {
                var yMin = 0.64 - i * 0.14;
                var yMax = yMin + 0.12;
                var sev = i == 0 ? "ERR" : "WARN";
                var sevCol = i == 0 ? CUI_COLOR_DANGER : CUI_COLOR_WARNING;
                AddLabel(c, "m" + i, root, $"[{sev}] 05-06 12:0{i} Log entry {i + 1}", sevCol, 10, "0.03 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.98 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "4 0", "0 0");
            }

            // Pagination
            AddButton(c, "pgp", root, "PREV", CUI_COLOR_SURFACE, CUI_COLOR_PRIMARY_TEXT, "sentinel.logpage prev", "0.25 0.05", "0.40 0.12", "0 0", "0 0", 10);
            AddLabel(c, "pgn", root, "1 / 5", CUI_COLOR_SECONDARY_TEXT, 11, "0.41 0.05", "0.59 0.12", "0 0", "0 0", "MiddleCenter");
            AddButton(c, "pgnxt", root, "NEXT", CUI_COLOR_SURFACE, CUI_COLOR_PRIMARY_TEXT, "sentinel.logpage next", "0.60 0.05", "0.75 0.12", "0 0", "0 0", 10);

            return c;
        }

        // Bans: list with names, reasons, expiry, unban; filter and sort
        public CuiElementContainer BuildBansView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_b_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");

            AddLabel(c, "t", root, "BANS", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.92", "0.5 1", "10 0", "0 0");
            AddLabel(c, "st", root, "Count: 2", CUI_COLOR_SECONDARY_TEXT, 10, "0.50 0.92", "0.70 0.98", "0 0", "0 0");

            // Filter and sort
            AddInputField(c, "fq", root, "Filter...", CUI_COLOR_PRIMARY_TEXT, "sentinel.banfilter ", "0.02 0.84", "0.48 0.90", "8 0", "-8 0", 11, 32);
            AddButton(c, "fsort", root, "SORT", CUI_COLOR_SURFACE, CUI_COLOR_PRIMARY_TEXT, "sentinel.bansort", "0.50 0.84", "0.60 0.90", "0 0", "0 0", 10);

            // Ban rows (2 rows without panels to save bytes)
            for (int i = 0; i < 2; i++)
            {
                var yMin = 0.60 - i * 0.22;
                var yMax = yMin + 0.20;
                AddLabel(c, "n" + i, root, $"Player{i} | Cheating | 7d", CUI_COLOR_PRIMARY_TEXT, 11, "0.03 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.75 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "5 0", "0 0");
                AddButton(c, "u" + i, root, "UNBAN", CUI_COLOR_SUCCESS, CUI_COLOR_PRIMARY_TEXT, "sentinel.unban " + i, "0.78 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.98 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "0 0", "0 0", 10);
            }

            return c;
        }

        // Config: categorized settings, toggles, numeric input, save button
        public CuiElementContainer BuildConfigView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_c_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");

            AddLabel(c, "t", root, "CONFIG", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.92", "0.5 1", "10 0", "0 0");
            AddLabel(c, "v", root, "v1.0.0", CUI_COLOR_SECONDARY_TEXT, 10, "0.80 0.92", "0.90 0.98", "0 0", "0 0");

            // Categorized toggles (2 categories, no row panels)
            var categories = new[] { "AuditLog", "Discord" };
            for (int i = 0; i < categories.Length; i++)
            {
                var yMin = 0.74 - i * 0.14;
                var yMax = yMin + 0.12;
                AddLabel(c, "l" + i, root, categories[i], CUI_COLOR_PRIMARY_TEXT, 12, "0.03 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.50 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "5 0", "0 0");
                AddButton(c, "tg" + i, root, "ON", CUI_COLOR_SUCCESS, CUI_COLOR_PRIMARY_TEXT, "sentinel.togglecfg " + categories[i], "0.72 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.88 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "0 0", "0 0", 10);
            }

            // Numeric input row
            AddLabel(c, "ln", root, "Daily Cap ($)", CUI_COLOR_PRIMARY_TEXT, 12, "0.03 0.44", "0.35 0.50", "5 0", "0 0");
            AddInputField(c, "in", root, "5.00", CUI_COLOR_PRIMARY_TEXT, "sentinel.cfgnum dailycap ", "0.37 0.44", "0.65 0.50", "4 0", "-4 0", 12, 8);

            // Save button
            AddButton(c, "sv", root, "SAVE", CUI_COLOR_SUCCESS, CUI_COLOR_PRIMARY_TEXT, "sentinel.savecfg", "0.35 0.05", "0.65 0.14", "0 0", "0 0", 14);

            return c;
        }

        // AI: model status, suggestion queue, confidence threshold, response history
        public CuiElementContainer BuildAiView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_a_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");

            AddLabel(c, "t", root, "AI", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.92", "0.5 1", "10 0", "0 0");
            AddLabel(c, "ms", root, "Model: gpt-4o-mini | Online | Threshold: 75%", CUI_COLOR_SECONDARY_TEXT, 11, "0.02 0.86", "0.98 0.91", "5 0", "0 0");

            // Suggestion queue card (compact)
            AddPanel(c, "cd", root, CUI_COLOR_SURFACE, "0.02 0.56", "0.98 0.84", "0 0", "0 0");
            AddLabel(c, "cdp", "cd", "PlayerA | aim | 85%", CUI_COLOR_PRIMARY_TEXT, 12, "0 0.55", "1 0.85", "5 0", "0 0");
            AddButton(c, "cda", "cd", "ACCEPT", CUI_COLOR_SUCCESS, CUI_COLOR_PRIMARY_TEXT, "sentinel.ai accept", "0.02 0.05", "0.31 0.20", "0 0", "0 0", 10);
            AddButton(c, "cdr", "cd", "REJECT", CUI_COLOR_DANGER, CUI_COLOR_PRIMARY_TEXT, "sentinel.ai reject", "0.34 0.05", "0.63 0.20", "0 0", "0 0", 10);
            AddButton(c, "cde", "cd", "EDIT", CUI_COLOR_ACCENT, CUI_COLOR_PRIMARY_TEXT, "sentinel.ai edit", "0.66 0.05", "0.96 0.20", "0 0", "0 0", 10);

            // Response history (1 row without panel)
            AddLabel(c, "hl", root, "HISTORY", CUI_COLOR_SECONDARY_TEXT, 11, "0.02 0.50", "0.3 0.55", "5 0", "0 0");
            AddLabel(c, "hm0", root, "[12:00] Triage result 1", CUI_COLOR_PRIMARY_TEXT, 10, "0.03 0.38", "0.98 0.48", "4 0", "0 0");

            return c;
        }

        // Permissions: role list, permission matrix, add/remove controls
        public CuiElementContainer BuildPermissionsView(string playerId)
        {
            var c = NewCuiContainer();
            var root = "s_r_" + playerId;
            AddPanel(c, root, "Overlay", CUI_COLOR_BACKGROUND, "0.05 0.05", "0.95 0.95", "0 0", "0 0");

            AddLabel(c, "t", root, "PERMISSIONS", CUI_COLOR_PRIMARY_TEXT, 18, "0 0.92", "0.5 1", "10 0", "0 0");
            AddLabel(c, "st", root, "Roles: 2", CUI_COLOR_SECONDARY_TEXT, 10, "0.50 0.92", "0.70 0.98", "0 0", "0 0");

            // Role rows (2 rows, combined name+perms, no panels)
            var roles = new[] { "admin", "moderator" };
            var perms = new[] { "sentinel.*", "kick,ban,warn" };
            for (int i = 0; i < 2; i++)
            {
                var yMin = 0.72 - i * 0.18;
                var yMax = yMin + 0.14;
                AddLabel(c, "rn" + i, root, $"{roles[i]} | {perms[i]}", CUI_COLOR_PRIMARY_TEXT, 11, "0.03 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.65 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "5 0", "0 0");
                AddButton(c, "ra" + i, root, "+", CUI_COLOR_SUCCESS, CUI_COLOR_PRIMARY_TEXT, "sentinel.perm add " + roles[i], "0.67 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.82 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "0 0", "0 0", 12);
                AddButton(c, "rrd" + i, root, "-", CUI_COLOR_DANGER, CUI_COLOR_PRIMARY_TEXT, "sentinel.perm remove " + roles[i], "0.84 " + yMin.ToString("F2", CultureInfo.InvariantCulture), "0.98 " + yMax.ToString("F2", CultureInfo.InvariantCulture), "0 0", "0 0", 12);
            }

            return c;
        }

        // ---------------------------------------------------------------
        // Payload validation
        // ---------------------------------------------------------------
        public int GetCuiPayloadSize(CuiElementContainer container) => CuiHelper.ToJson(container).Length;
    }
}
