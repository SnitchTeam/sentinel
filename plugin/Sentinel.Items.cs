using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;

namespace Oxide.Plugins
{
    public class ItemSearchResult
    {
        public string Shortname { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public partial class Sentinel
    {
        // -------------------------------------------------------------
        // Virtual helpers for testability / runtime abstraction
        // -------------------------------------------------------------
        protected virtual List<ItemDefinition> GetAllItemDefinitions()
        {
            return ItemManager.itemList;
        }

        protected virtual Item? CreateItemByName(string shortname, int amount)
        {
            return ItemManager.CreateByName(shortname, amount);
        }

        protected virtual bool GiveItemToInventory(BasePlayer player, Item item)
        {
            return player.inventory.GiveItem(item);
        }

        protected virtual void DropItemAtPlayerFeet(BasePlayer player, Item item)
        {
            item.Drop(player.Position, new Vector3(0, 0, 0));
        }

        protected virtual int GetInventoryCapacity(BasePlayer player)
        {
            return player.inventory.containerMain.capacity;
        }

        protected virtual int GetInventoryOccupancy(BasePlayer player)
        {
            return player.inventory.containerMain.itemList.Count;
        }

        // -------------------------------------------------------------
        // Item Search
        // -------------------------------------------------------------
        public List<ItemSearchResult> SearchItems(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<ItemSearchResult>();

            query = query.Trim();
            var all = GetAllItemDefinitions();

            return all
                .Where(d => d.shortname.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(d => new ItemSearchResult
                {
                    Shortname = d.shortname,
                    DisplayName = d.displayName
                })
                .ToList();
        }

        // -------------------------------------------------------------
        // Give Item (inventory preferred, drop excess)
        // -------------------------------------------------------------
        public bool ExecuteGiveItem(BasePlayer? admin, string targetIdentifier, string shortname, int quantity, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.items"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "item_give", $"{shortname} x{quantity}", null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var target = ResolveTarget(targetIdentifier);
            if (target == null)
            {
                error = "Player not found";
                LogAuditAction(actorId, actorName, null, null, "item_give", $"{shortname} x{quantity}", null, false);
                return false;
            }

            var definition = GetAllItemDefinitions()
                .FirstOrDefault(d => d.shortname.Equals(shortname, StringComparison.OrdinalIgnoreCase));

            if (definition == null)
            {
                error = $"Item '{shortname}' not found.";
                LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "item_give", $"{shortname} x{quantity}", null, false);
                return false;
            }

            if (quantity <= 0)
            {
                error = "Quantity must be greater than 0.";
                LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "item_give", $"{shortname} x{quantity}", null, false);
                return false;
            }

            var maxStack = Math.Max(1, definition.stackable);
            var remaining = quantity;
            int given = 0;
            int dropped = 0;

            while (remaining > 0)
            {
                var chunk = Math.Min(maxStack, remaining);
                var item = CreateItemByName(definition.shortname, chunk);
                if (item == null)
                {
                    error = "Failed to create item.";
                    LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "item_give", $"{shortname} x{quantity}", null, false,
                        $"{{\"given\":{given},\"dropped\":{dropped},\"error\":\"create_failed\"}}");
                    return false;
                }

                if (GiveItemToInventory(target, item))
                {
                    given += chunk;
                }
                else
                {
                    DropItemAtPlayerFeet(target, item);
                    dropped += chunk;
                }

                remaining -= chunk;
            }

            var detailJson = $"{{\"shortname\":\"{shortname}\",\"quantity\":{quantity},\"given\":{given},\"dropped\":{dropped},\"maxStack\":{maxStack}}}";
            LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "item_give", $"{shortname} x{quantity}", null, true, detailJson);

            if (dropped > 0)
            {
                target.ChatMessage($"[Sentinel] You received {given} {shortname} in your inventory and {dropped} were dropped at your feet.");
            }
            else
            {
                target.ChatMessage($"[Sentinel] You received {quantity} {shortname} in your inventory.");
            }

            return true;
        }

        // -------------------------------------------------------------
        // Drop Item (directly at feet)
        // -------------------------------------------------------------
        public bool ExecuteDropItem(BasePlayer? admin, string targetIdentifier, string shortname, int quantity, out string error)
        {
            error = "";
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (!HasPermission(admin, "sentinel.items"))
            {
                error = "No permission";
                LogAuditAction(actorId, actorName, null, null, "item_drop", $"{shortname} x{quantity}", null, false);
                if (admin != null) NotifyNoPermission(admin);
                return false;
            }

            var target = ResolveTarget(targetIdentifier);
            if (target == null)
            {
                error = "Player not found";
                LogAuditAction(actorId, actorName, null, null, "item_drop", $"{shortname} x{quantity}", null, false);
                return false;
            }

            var definition = GetAllItemDefinitions()
                .FirstOrDefault(d => d.shortname.Equals(shortname, StringComparison.OrdinalIgnoreCase));

            if (definition == null)
            {
                error = $"Item '{shortname}' not found.";
                LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "item_drop", $"{shortname} x{quantity}", null, false);
                return false;
            }

            if (quantity <= 0)
            {
                error = "Quantity must be greater than 0.";
                LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "item_drop", $"{shortname} x{quantity}", null, false);
                return false;
            }

            var maxStack = Math.Max(1, definition.stackable);
            var remaining = quantity;
            int dropped = 0;

            while (remaining > 0)
            {
                var chunk = Math.Min(maxStack, remaining);
                var item = CreateItemByName(definition.shortname, chunk);
                if (item == null)
                {
                    error = "Failed to create item.";
                    LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "item_drop", $"{shortname} x{quantity}", null, false,
                        $"{{\"dropped\":{dropped},\"error\":\"create_failed\"}}");
                    return false;
                }

                DropItemAtPlayerFeet(target, item);
                dropped += chunk;
                remaining -= chunk;
            }

            var detailJson = $"{{\"shortname\":\"{shortname}\",\"quantity\":{quantity},\"dropped\":{dropped},\"maxStack\":{maxStack}}}";
            LogAuditAction(actorId, actorName, target.UserIDString, target.displayName, "item_drop", $"{shortname} x{quantity}", null, true, detailJson);

            target.ChatMessage($"[Sentinel] {quantity} {shortname} were dropped at your feet.");
            return true;
        }

        // -------------------------------------------------------------
        // Console Commands
        // -------------------------------------------------------------
        [ConsoleCommand("sentinel.item.search")]
        void CCmdItemSearch(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.item.search <query>");
                return;
            }

            var query = arg.Args[0];
            var results = SearchItems(query);

            if (results.Count == 0)
            {
                Puts($"[Sentinel] No items found for '{query}'.");
                return;
            }

            Puts($"[Sentinel] Items matching '{query}':");
            foreach (var r in results)
            {
                Puts($"  - {r.Shortname} ({r.DisplayName})");
            }
        }

        [ConsoleCommand("sentinel.item.give")]
        void CCmdItemGive(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2)
            {
                Puts("Usage: sentinel.item.give <player> <shortname> [quantity]");
                return;
            }

            var targetId = arg.Args[0];
            var shortname = arg.Args[1];
            var quantity = arg.Args.Length > 2 && int.TryParse(arg.Args[2], out var q) && q > 0 ? q : 1;
            var admin = arg.Player();

            if (!ExecuteGiveItem(admin, targetId, shortname, quantity, out var error))
            {
                Puts($"[Sentinel] Give failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Gave {quantity} {shortname} to {targetId}.");
            }
        }

        [ConsoleCommand("sentinel.item.drop")]
        void CCmdItemDrop(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2)
            {
                Puts("Usage: sentinel.item.drop <player> <shortname> [quantity]");
                return;
            }

            var targetId = arg.Args[0];
            var shortname = arg.Args[1];
            var quantity = arg.Args.Length > 2 && int.TryParse(arg.Args[2], out var q) && q > 0 ? q : 1;
            var admin = arg.Player();

            if (!ExecuteDropItem(admin, targetId, shortname, quantity, out var error))
            {
                Puts($"[Sentinel] Drop failed: {error}");
            }
            else
            {
                Puts($"[Sentinel] Dropped {quantity} {shortname} at {targetId}'s feet.");
            }
        }
    }
}
