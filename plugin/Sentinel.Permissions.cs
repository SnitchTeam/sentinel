namespace Oxide.Plugins
{
    public partial class Sentinel
    {
        private readonly string[] _sentinelPermissions = new[]
        {
            "sentinel.kick",
            "sentinel.ban",
            "sentinel.warn",
            "sentinel.mute",
            "sentinel.freeze",
            "sentinel.spectate",
            "sentinel.teleport",
            "sentinel.groups.manage",
            "sentinel.items",
            "sentinel.world",
            "sentinel.plugins",
            "sentinel.console",
            "sentinel.audit",
            "sentinel.panel"
        };

        public void RegisterPermissions()
        {
            if (permission == null) return;
            foreach (var perm in _sentinelPermissions)
            {
                permission.RegisterPermission(perm, this);
            }
        }

        public virtual bool HasPermission(BasePlayer? player, string permissionNode)
        {
            if (player == null) return true; // Console/server has permission
            if (permission == null) return false;
            return permission.UserHasPermission(player.UserIDString, permissionNode);
        }

        public void NotifyNoPermission(BasePlayer player)
        {
            player.ChatMessage("You don't have permission to use this command.");
            _runtimeBridge?.LogWarning($"[Sentinel] {player.displayName} ({player.UserIDString}) attempted an action without {nameof(NotifyNoPermission)}.");
        }
    }
}
