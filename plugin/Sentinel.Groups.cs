using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Oxide.Core;

namespace Oxide.Plugins
{
    public class SentinelGroup
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string? ParentGroup { get; set; }
        public List<string> Permissions { get; set; } = new();
        public long CreatedAt { get; set; }
        public bool SystemProtected { get; set; }
    }

    public class SentinelGroupMember
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public string SteamId { get; set; } = "";
        public long AddedAt { get; set; }
    }

    public partial class Sentinel
    {
        private readonly HashSet<string> _systemGroupNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "sentinel_admin",
            "sentinel_moderator",
            "sentinel_trial_mod"
        };

        // -------------------------------------------------------------
        // Default Groups Initialization
        // -------------------------------------------------------------
        public void InitializeDefaultGroups()
        {
            var defaultGroups = PluginConfig?.Groups?.DefaultGroups ?? GetDefaultGroupDefinitions();

            foreach (var kvp in defaultGroups)
            {
                var name = kvp.Key;
                var def = kvp.Value;

                if (!GroupExistsInDb(name))
                {
                    CreateGroupInternal(name, def.Title, null, systemProtected: true);
                }
                else
                {
                    // Ensure system-protected flag is set for default groups
                    using var command = _dbConnection!.CreateCommand();
                    command.CommandText = "UPDATE sentinel_groups SET system_protected = 1 WHERE name = @name;";
                    command.Parameters.AddWithValue("@name", name);
                    command.ExecuteNonQuery();
                }

                foreach (var perm in def.Permissions)
                {
                    GrantGroupPermissionInternal(name, perm);
                }
            }
        }

        private static Dictionary<string, GroupDefinition> GetDefaultGroupDefinitions()
        {
            return new Dictionary<string, GroupDefinition>
            {
                ["sentinel_admin"] = new GroupDefinition
                {
                    Title = "Sentinel Admin",
                    Permissions = new List<string> { "sentinel.*" }
                },
                ["sentinel_moderator"] = new GroupDefinition
                {
                    Title = "Sentinel Moderator",
                    Permissions = new List<string>
                    {
                        "sentinel.kick", "sentinel.ban", "sentinel.warn",
                        "sentinel.mute", "sentinel.freeze"
                    }
                },
                ["sentinel_trial_mod"] = new GroupDefinition
                {
                    Title = "Sentinel Trial Mod",
                    Permissions = new List<string> { "sentinel.kick", "sentinel.warn" }
                }
            };
        }

        // -------------------------------------------------------------
        // Group CRUD
        // -------------------------------------------------------------
        public bool CreateGroup(string name, string title, string? parentGroup, out string error)
        {
            error = "";

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Group name is required.";
                return false;
            }

            if (GroupExistsInDb(name))
            {
                error = $"Group '{name}' already exists.";
                return false;
            }

            return CreateGroupInternal(name, title, parentGroup, systemProtected: false);
        }

        private bool CreateGroupInternal(string name, string title, string? parentGroup, bool systemProtected)
        {
            try
            {
                // Insert into SQLite
                using var command = _dbConnection!.CreateCommand();
                command.CommandText = @"
                    INSERT INTO sentinel_groups (name, title, permissions_json, parent_group, created_at, system_protected)
                    VALUES (@name, @title, @perms, @parent, @createdAt, @sysProt);";
                command.Parameters.AddWithValue("@name", name);
                command.Parameters.AddWithValue("@title", title ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@perms", "[]");
                command.Parameters.AddWithValue("@parent", parentGroup ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                command.Parameters.AddWithValue("@sysProt", systemProtected ? 1 : 0);
                command.ExecuteNonQuery();

                // Create in Oxide
                permission?.CreateGroup(name, title ?? name, 0);
                if (!string.IsNullOrEmpty(parentGroup))
                {
                    permission?.SetGroupParent(name, parentGroup);
                }

                return true;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] CreateGroup failed: {ex.Message}");
                return false;
            }
        }

        public bool DeleteGroup(string name, out string error)
        {
            error = "";

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Group name is required.";
                return false;
            }

            var group = GetGroupFromDb(name);
            if (group == null)
            {
                error = $"Group '{name}' does not exist.";
                return false;
            }

            if (group.SystemProtected || _systemGroupNames.Contains(name))
            {
                error = $"Group '{name}' is system-protected and cannot be deleted.";
                return false;
            }

            try
            {
                // Delete members from SQLite
                using (var cmd = _dbConnection!.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM sentinel_group_members WHERE group_id = @groupId;";
                    cmd.Parameters.AddWithValue("@groupId", group.Id);
                    cmd.ExecuteNonQuery();
                }

                // Delete group from SQLite
                using (var cmd = _dbConnection!.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM sentinel_groups WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", group.Id);
                    cmd.ExecuteNonQuery();
                }

                // Remove from Oxide
                permission?.RemoveGroup(name);

                return true;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] DeleteGroup failed: {ex.Message}");
                error = "Database error.";
                return false;
            }
        }

        public bool UpdateGroupTitle(string name, string newTitle, out string error)
        {
            error = "";

            var group = GetGroupFromDb(name);
            if (group == null)
            {
                error = $"Group '{name}' does not exist.";
                return false;
            }

            try
            {
                using var command = _dbConnection!.CreateCommand();
                command.CommandText = "UPDATE sentinel_groups SET title = @title WHERE id = @id;";
                command.Parameters.AddWithValue("@title", newTitle);
                command.Parameters.AddWithValue("@id", group.Id);
                command.ExecuteNonQuery();

                permission?.SetGroupTitle(name, newTitle);
                return true;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] UpdateGroupTitle failed: {ex.Message}");
                error = "Database error.";
                return false;
            }
        }

        public bool UpdateGroupParent(string name, string? newParent, out string error)
        {
            error = "";

            var group = GetGroupFromDb(name);
            if (group == null)
            {
                error = $"Group '{name}' does not exist.";
                return false;
            }

            if (!string.IsNullOrEmpty(newParent) && !GroupExistsInDb(newParent))
            {
                error = $"Parent group '{newParent}' does not exist.";
                return false;
            }

            try
            {
                using var command = _dbConnection!.CreateCommand();
                command.CommandText = "UPDATE sentinel_groups SET parent_group = @parent WHERE id = @id;";
                command.Parameters.AddWithValue("@parent", newParent ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@id", group.Id);
                command.ExecuteNonQuery();

                permission?.SetGroupParent(name, newParent ?? string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] UpdateGroupParent failed: {ex.Message}");
                error = "Database error.";
                return false;
            }
        }

        // -------------------------------------------------------------
        // User Assignment
        // -------------------------------------------------------------
        public bool AddUserToGroup(string groupName, string playerIdentifier, out string error)
        {
            error = "";

            var group = GetGroupFromDb(groupName);
            if (group == null)
            {
                error = $"Group '{groupName}' does not exist.";
                return false;
            }

            var target = ResolveTarget(playerIdentifier);
            string steamId;
            string? playerName;

            if (target != null)
            {
                steamId = target.UserIDString;
                playerName = target.displayName;
            }
            else
            {
                // Assume identifier is a SteamID if no online player matched
                steamId = playerIdentifier;
                playerName = null;
            }

            if (string.IsNullOrWhiteSpace(steamId))
            {
                error = "Player not found.";
                return false;
            }

            try
            {
                // Check if already a member
                using (var cmd = _dbConnection!.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM sentinel_group_members WHERE group_id = @groupId AND steam_id = @steamId;";
                    cmd.Parameters.AddWithValue("@groupId", group.Id);
                    cmd.Parameters.AddWithValue("@steamId", steamId);
                    var count = Convert.ToInt64(cmd.ExecuteScalar());
                    if (count > 0)
                    {
                        error = $"Player is already in group '{groupName}'.";
                        return false;
                    }
                }

                // Insert into SQLite
                using (var cmd = _dbConnection!.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO sentinel_group_members (group_id, steam_id, added_at)
                        VALUES (@groupId, @steamId, @addedAt);";
                    cmd.Parameters.AddWithValue("@groupId", group.Id);
                    cmd.Parameters.AddWithValue("@steamId", steamId);
                    cmd.Parameters.AddWithValue("@addedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    cmd.ExecuteNonQuery();
                }

                // Add to Oxide group
                permission?.AddUserGroup(steamId, groupName);

                return true;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] AddUserToGroup failed: {ex.Message}");
                error = "Database error.";
                return false;
            }
        }

        public bool RemoveUserFromGroup(string groupName, string playerIdentifier, out string error)
        {
            error = "";

            var group = GetGroupFromDb(groupName);
            if (group == null)
            {
                error = $"Group '{groupName}' does not exist.";
                return false;
            }

            var target = ResolveTarget(playerIdentifier);
            string steamId = target?.UserIDString ?? playerIdentifier;

            if (string.IsNullOrWhiteSpace(steamId))
            {
                error = "Player not found.";
                return false;
            }

            try
            {
                // Delete from SQLite
                using (var cmd = _dbConnection!.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM sentinel_group_members WHERE group_id = @groupId AND steam_id = @steamId;";
                    cmd.Parameters.AddWithValue("@groupId", group.Id);
                    cmd.Parameters.AddWithValue("@steamId", steamId);
                    cmd.ExecuteNonQuery();
                }

                // Remove from Oxide group
                permission?.RemoveUserGroup(steamId, groupName);

                return true;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] RemoveUserFromGroup failed: {ex.Message}");
                error = "Database error.";
                return false;
            }
        }

        // -------------------------------------------------------------
        // Group Permissions
        // -------------------------------------------------------------
        public bool GrantGroupPermission(string groupName, string perm, out string error)
        {
            error = "";

            if (string.IsNullOrWhiteSpace(perm))
            {
                error = "Permission is required.";
                return false;
            }

            var group = GetGroupFromDb(groupName);
            if (group == null)
            {
                error = $"Group '{groupName}' does not exist.";
                return false;
            }

            return GrantGroupPermissionInternal(groupName, perm);
        }

        private bool GrantGroupPermissionInternal(string groupName, string perm)
        {
            try
            {
                var group = GetGroupFromDb(groupName);
                if (group == null) return false;

                if (!group.Permissions.Contains(perm, StringComparer.OrdinalIgnoreCase))
                {
                    group.Permissions.Add(perm);

                    using var cmd = _dbConnection!.CreateCommand();
                    cmd.CommandText = "UPDATE sentinel_groups SET permissions_json = @perms WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@perms", JsonSerializer.Serialize(group.Permissions));
                    cmd.Parameters.AddWithValue("@id", group.Id);
                    cmd.ExecuteNonQuery();
                }

                permission?.GrantGroupPermission(groupName, perm, this);
                return true;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GrantGroupPermission failed: {ex.Message}");
                return false;
            }
        }

        public bool RevokeGroupPermission(string groupName, string perm, out string error)
        {
            error = "";

            if (string.IsNullOrWhiteSpace(perm))
            {
                error = "Permission is required.";
                return false;
            }

            var group = GetGroupFromDb(groupName);
            if (group == null)
            {
                error = $"Group '{groupName}' does not exist.";
                return false;
            }

            try
            {
                group.Permissions.RemoveAll(p => p.Equals(perm, StringComparison.OrdinalIgnoreCase));

                using var cmd = _dbConnection!.CreateCommand();
                cmd.CommandText = "UPDATE sentinel_groups SET permissions_json = @perms WHERE id = @id;";
                cmd.Parameters.AddWithValue("@perms", JsonSerializer.Serialize(group.Permissions));
                cmd.Parameters.AddWithValue("@id", group.Id);
                cmd.ExecuteNonQuery();

                permission?.RevokeGroupPermission(groupName, perm);
                return true;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] RevokeGroupPermission failed: {ex.Message}");
                error = "Database error.";
                return false;
            }
        }

        // -------------------------------------------------------------
        // Queries
        // -------------------------------------------------------------
        public bool GroupExistsInDb(string name)
        {
            if (_dbConnection == null || string.IsNullOrWhiteSpace(name)) return false;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sentinel_groups WHERE name = @name;";
                command.Parameters.AddWithValue("@name", name);
                var count = Convert.ToInt64(command.ExecuteScalar());
                return count > 0;
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GroupExistsInDb failed: {ex.Message}");
                return false;
            }
        }

        public SentinelGroup? GetGroupFromDb(string name)
        {
            if (_dbConnection == null || string.IsNullOrWhiteSpace(name)) return null;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT id, name, title, parent_group, permissions_json, created_at, system_protected
                    FROM sentinel_groups WHERE name = @name;";
                command.Parameters.AddWithValue("@name", name);
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    return ReadGroup(reader);
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetGroupFromDb failed: {ex.Message}");
            }
            return null;
        }

        public List<SentinelGroup> GetAllGroups()
        {
            var groups = new List<SentinelGroup>();
            if (_dbConnection == null) return groups;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT id, name, title, parent_group, permissions_json, created_at, system_protected
                    FROM sentinel_groups ORDER BY name;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    groups.Add(ReadGroup(reader));
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetAllGroups failed: {ex.Message}");
            }
            return groups;
        }

        public List<SentinelGroupMember> GetGroupMembers(string groupName)
        {
            var members = new List<SentinelGroupMember>();
            if (_dbConnection == null) return members;

            var group = GetGroupFromDb(groupName);
            if (group == null) return members;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT id, group_id, steam_id, added_at
                    FROM sentinel_group_members WHERE group_id = @groupId ORDER BY added_at;";
                command.Parameters.AddWithValue("@groupId", group.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    members.Add(new SentinelGroupMember
                    {
                        Id = reader.GetInt32(0),
                        GroupId = reader.GetInt32(1),
                        SteamId = reader.GetString(2),
                        AddedAt = reader.GetInt64(3)
                    });
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetGroupMembers failed: {ex.Message}");
            }
            return members;
        }

        public List<string> GetUserGroupNames(string steamId)
        {
            var groupNames = new List<string>();
            if (_dbConnection == null) return groupNames;

            try
            {
                using var command = _dbConnection.CreateCommand();
                command.CommandText = @"
                    SELECT g.name FROM sentinel_groups g
                    INNER JOIN sentinel_group_members m ON m.group_id = g.id
                    WHERE m.steam_id = @steamId
                    ORDER BY g.name;";
                command.Parameters.AddWithValue("@steamId", steamId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    groupNames.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _runtimeBridge?.LogError($"[Sentinel] GetUserGroupNames failed: {ex.Message}");
            }
            return groupNames;
        }

        private static SentinelGroup ReadGroup(SqliteDataReader reader)
        {
            var permsJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4);
            List<string> perms;
            try
            {
                perms = JsonSerializer.Deserialize<List<string>>(permsJson) ?? new List<string>();
            }
            catch
            {
                perms = new List<string>();
            }

            return new SentinelGroup
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Title = reader.IsDBNull(2) ? "" : reader.GetString(2),
                ParentGroup = reader.IsDBNull(3) ? null : reader.GetString(3),
                Permissions = perms,
                CreatedAt = reader.GetInt64(5),
                SystemProtected = !reader.IsDBNull(6) && reader.GetInt32(6) == 1
            };
        }

        // -------------------------------------------------------------
        // Console Commands
        // -------------------------------------------------------------
        [ConsoleCommand("sentinel.group")]
        void CCmdGroup(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 1)
            {
                Puts("Usage: sentinel.group <create|delete|update|add|remove|grant|revoke|list|info> ...");
                return;
            }

            var admin = arg.Player();
            var subcommand = arg.Args[0].ToLowerInvariant();

            switch (subcommand)
            {
                case "create":
                    CCmdGroupCreate(arg, admin);
                    break;
                case "delete":
                    CCmdGroupDelete(arg, admin);
                    break;
                case "update":
                    CCmdGroupUpdate(arg, admin);
                    break;
                case "add":
                    CCmdGroupAdd(arg, admin);
                    break;
                case "remove":
                    CCmdGroupRemove(arg, admin);
                    break;
                case "grant":
                    CCmdGroupGrant(arg, admin);
                    break;
                case "revoke":
                    CCmdGroupRevoke(arg, admin);
                    break;
                case "list":
                    CCmdGroupList(arg, admin);
                    break;
                case "info":
                    CCmdGroupInfo(arg, admin);
                    break;
                default:
                    Puts($"[Sentinel] Unknown group subcommand: {subcommand}");
                    break;
            }
        }

        private void CCmdGroupCreate(ConsoleSystem.Arg arg, BasePlayer? admin)
        {
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (arg.Args == null || arg.Args.Length < 3)
            {
                Puts("Usage: sentinel.group create <name> \"<title>\" [parent]");
                return;
            }

            if (!HasPermission(admin, "sentinel.groups.manage"))
            {
                LogAuditAction(actorId, actorName, null, null, "group_create", null, null, false);
                Puts("[Sentinel] You don't have permission to manage groups.");
                return;
            }

            var name = arg.Args[1];
            var title = arg.Args[2];
            var parent = arg.Args.Length > 3 ? arg.Args[3] : null;

            if (CreateGroup(name, title, parent, out var error))
            {
                LogAuditAction(actorId, actorName, null, null, "group_create", $"Created group '{name}'", null, true,
                    $"{{\"group\":\"{name}\",\"title\":\"{title}\",\"parent\":\"{parent ?? "null"}\"}}");
                Puts($"[Sentinel] Created group '{name}' with title '{title}'.");
            }
            else
            {
                LogAuditAction(actorId, actorName, null, null, "group_create", null, null, false,
                    $"{{\"group\":\"{name}\",\"error\":\"{error}\"}}");
                Puts($"[Sentinel] Failed to create group: {error}");
            }
        }

        private void CCmdGroupDelete(ConsoleSystem.Arg arg, BasePlayer? admin)
        {
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (arg.Args == null || arg.Args.Length < 2)
            {
                Puts("Usage: sentinel.group delete <name>");
                return;
            }

            if (!HasPermission(admin, "sentinel.groups.manage"))
            {
                LogAuditAction(actorId, actorName, null, null, "group_delete", null, null, false);
                Puts("[Sentinel] You don't have permission to manage groups.");
                return;
            }

            var name = arg.Args[1];
            var group = GetGroupFromDb(name);
            var oldTitle = group?.Title;

            if (DeleteGroup(name, out var error))
            {
                LogAuditAction(actorId, actorName, null, null, "group_delete", $"Deleted group '{name}'", null, true,
                    $"{{\"group\":\"{name}\",\"oldTitle\":\"{oldTitle ?? "null"}\"}}");
                Puts($"[Sentinel] Deleted group '{name}'.");
            }
            else
            {
                LogAuditAction(actorId, actorName, null, null, "group_delete", null, null, false,
                    $"{{\"group\":\"{name}\",\"error\":\"{error}\"}}");
                Puts($"[Sentinel] Failed to delete group: {error}");
            }
        }

        private void CCmdGroupUpdate(ConsoleSystem.Arg arg, BasePlayer? admin)
        {
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (arg.Args == null || arg.Args.Length < 4)
            {
                Puts("Usage: sentinel.group update <name> title \"<newTitle>\"");
                Puts("       sentinel.group update <name> parent <newParent|none>");
                return;
            }

            if (!HasPermission(admin, "sentinel.groups.manage"))
            {
                LogAuditAction(actorId, actorName, null, null, "group_update", null, null, false);
                Puts("[Sentinel] You don't have permission to manage groups.");
                return;
            }

            var name = arg.Args[1];
            var field = arg.Args[2].ToLowerInvariant();
            var value = arg.Args[3];
            var group = GetGroupFromDb(name);
            var oldTitle = group?.Title;
            var oldParent = group?.ParentGroup;

            if (field == "title")
            {
                if (UpdateGroupTitle(name, value, out var error))
                {
                    LogAuditAction(actorId, actorName, null, null, "group_update", $"Updated title for '{name}'", null, true,
                        $"{{\"group\":\"{name}\",\"field\":\"title\",\"old\":\"{oldTitle ?? "null"}\",\"new\":\"{value}\"}}");
                    Puts($"[Sentinel] Updated group '{name}' title to '{value}'.");
                }
                else
                {
                    LogAuditAction(actorId, actorName, null, null, "group_update", null, null, false,
                        $"{{\"group\":\"{name}\",\"field\":\"title\",\"error\":\"{error}\"}}");
                    Puts($"[Sentinel] Failed to update group: {error}");
                }
            }
            else if (field == "parent")
            {
                var parent = value.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : value;
                if (UpdateGroupParent(name, parent, out var error))
                {
                    LogAuditAction(actorId, actorName, null, null, "group_update", $"Updated parent for '{name}'", null, true,
                        $"{{\"group\":\"{name}\",\"field\":\"parent\",\"old\":\"{oldParent ?? "null"}\",\"new\":\"{parent ?? "null"}\"}}");
                    Puts($"[Sentinel] Updated group '{name}' parent to '{parent ?? "none"}'.");
                }
                else
                {
                    LogAuditAction(actorId, actorName, null, null, "group_update", null, null, false,
                        $"{{\"group\":\"{name}\",\"field\":\"parent\",\"error\":\"{error}\"}}");
                    Puts($"[Sentinel] Failed to update group: {error}");
                }
            }
            else
            {
                Puts($"[Sentinel] Unknown update field: {field}. Use 'title' or 'parent'.");
            }
        }

        private void CCmdGroupAdd(ConsoleSystem.Arg arg, BasePlayer? admin)
        {
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (arg.Args == null || arg.Args.Length < 3)
            {
                Puts("Usage: sentinel.group add <group> <player>");
                return;
            }

            if (!HasPermission(admin, "sentinel.groups.manage"))
            {
                LogAuditAction(actorId, actorName, null, null, "group_add_user", null, null, false);
                Puts("[Sentinel] You don't have permission to manage groups.");
                return;
            }

            var groupName = arg.Args[1];
            var playerId = arg.Args[2];
            var target = ResolveTarget(playerId);
            var targetSteamId = target?.UserIDString ?? playerId;

            if (AddUserToGroup(groupName, playerId, out var error))
            {
                LogAuditAction(actorId, actorName, targetSteamId, target?.displayName, "group_add_user", $"Added to '{groupName}'", null, true,
                    $"{{\"group\":\"{groupName}\",\"player\":\"{targetSteamId}\"}}");
                Puts($"[Sentinel] Added '{playerId}' to group '{groupName}'.");
            }
            else
            {
                LogAuditAction(actorId, actorName, targetSteamId, target?.displayName, "group_add_user", null, null, false,
                    $"{{\"group\":\"{groupName}\",\"player\":\"{targetSteamId}\",\"error\":\"{error}\"}}");
                Puts($"[Sentinel] Failed to add user to group: {error}");
            }
        }

        private void CCmdGroupRemove(ConsoleSystem.Arg arg, BasePlayer? admin)
        {
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (arg.Args == null || arg.Args.Length < 3)
            {
                Puts("Usage: sentinel.group remove <group> <player>");
                return;
            }

            if (!HasPermission(admin, "sentinel.groups.manage"))
            {
                LogAuditAction(actorId, actorName, null, null, "group_remove_user", null, null, false);
                Puts("[Sentinel] You don't have permission to manage groups.");
                return;
            }

            var groupName = arg.Args[1];
            var playerId = arg.Args[2];
            var target = ResolveTarget(playerId);
            var targetSteamId = target?.UserIDString ?? playerId;

            if (RemoveUserFromGroup(groupName, playerId, out var error))
            {
                LogAuditAction(actorId, actorName, targetSteamId, target?.displayName, "group_remove_user", $"Removed from '{groupName}'", null, true,
                    $"{{\"group\":\"{groupName}\",\"player\":\"{targetSteamId}\"}}");
                Puts($"[Sentinel] Removed '{playerId}' from group '{groupName}'.");
            }
            else
            {
                LogAuditAction(actorId, actorName, targetSteamId, target?.displayName, "group_remove_user", null, null, false,
                    $"{{\"group\":\"{groupName}\",\"player\":\"{targetSteamId}\",\"error\":\"{error}\"}}");
                Puts($"[Sentinel] Failed to remove user from group: {error}");
            }
        }

        private void CCmdGroupGrant(ConsoleSystem.Arg arg, BasePlayer? admin)
        {
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (arg.Args == null || arg.Args.Length < 3)
            {
                Puts("Usage: sentinel.group grant <group> <permission>");
                return;
            }

            if (!HasPermission(admin, "sentinel.groups.manage"))
            {
                LogAuditAction(actorId, actorName, null, null, "group_grant_permission", null, null, false);
                Puts("[Sentinel] You don't have permission to manage groups.");
                return;
            }

            var groupName = arg.Args[1];
            var perm = arg.Args[2];

            if (GrantGroupPermission(groupName, perm, out var error))
            {
                LogAuditAction(actorId, actorName, null, null, "group_grant_permission", $"Granted '{perm}' to '{groupName}'", null, true,
                    $"{{\"group\":\"{groupName}\",\"permission\":\"{perm}\"}}");
                Puts($"[Sentinel] Granted '{perm}' to group '{groupName}'.");
            }
            else
            {
                LogAuditAction(actorId, actorName, null, null, "group_grant_permission", null, null, false,
                    $"{{\"group\":\"{groupName}\",\"permission\":\"{perm}\",\"error\":\"{error}\"}}");
                Puts($"[Sentinel] Failed to grant permission: {error}");
            }
        }

        private void CCmdGroupRevoke(ConsoleSystem.Arg arg, BasePlayer? admin)
        {
            var actorId = admin?.UserIDString ?? "console";
            var actorName = admin?.displayName ?? "Console";

            if (arg.Args == null || arg.Args.Length < 3)
            {
                Puts("Usage: sentinel.group revoke <group> <permission>");
                return;
            }

            if (!HasPermission(admin, "sentinel.groups.manage"))
            {
                LogAuditAction(actorId, actorName, null, null, "group_revoke_permission", null, null, false);
                Puts("[Sentinel] You don't have permission to manage groups.");
                return;
            }

            var groupName = arg.Args[1];
            var perm = arg.Args[2];
            var group = GetGroupFromDb(groupName);
            var hadPerm = group?.Permissions.Contains(perm, StringComparer.OrdinalIgnoreCase) ?? false;

            if (RevokeGroupPermission(groupName, perm, out var error))
            {
                LogAuditAction(actorId, actorName, null, null, "group_revoke_permission", $"Revoked '{perm}' from '{groupName}'", null, true,
                    $"{{\"group\":\"{groupName}\",\"permission\":\"{perm}\",\"hadPermission\":{hadPerm.ToString().ToLower()}}}");
                Puts($"[Sentinel] Revoked '{perm}' from group '{groupName}'.");
            }
            else
            {
                LogAuditAction(actorId, actorName, null, null, "group_revoke_permission", null, null, false,
                    $"{{\"group\":\"{groupName}\",\"permission\":\"{perm}\",\"error\":\"{error}\"}}");
                Puts($"[Sentinel] Failed to revoke permission: {error}");
            }
        }

        private void CCmdGroupList(ConsoleSystem.Arg arg, BasePlayer? admin)
        {
            if (!HasPermission(admin, "sentinel.groups.manage"))
            {
                Puts("[Sentinel] You don't have permission to manage groups.");
                return;
            }

            var groups = GetAllGroups();
            if (groups.Count == 0)
            {
                Puts("[Sentinel] No groups found.");
                return;
            }

            Puts("[Sentinel] Groups:");
            foreach (var g in groups)
            {
                var protectedMarker = g.SystemProtected ? " [SYSTEM]" : "";
                Puts($"  - {g.Name}: {g.Title}{protectedMarker} (Permissions: {g.Permissions.Count}, Parent: {g.ParentGroup ?? "none"})");
            }
        }

        private void CCmdGroupInfo(ConsoleSystem.Arg arg, BasePlayer? admin)
        {
            if (arg.Args == null || arg.Args.Length < 2)
            {
                Puts("Usage: sentinel.group info <name>");
                return;
            }

            if (!HasPermission(admin, "sentinel.groups.manage"))
            {
                Puts("[Sentinel] You don't have permission to manage groups.");
                return;
            }

            var name = arg.Args[1];
            var group = GetGroupFromDb(name);
            if (group == null)
            {
                Puts($"[Sentinel] Group '{name}' not found.");
                return;
            }

            var members = GetGroupMembers(name);
            Puts($"[Sentinel] Group: {group.Name}");
            Puts($"  Title: {group.Title}");
            Puts($"  Parent: {group.ParentGroup ?? "none"}");
            Puts($"  System Protected: {group.SystemProtected}");
            Puts($"  Permissions: {string.Join(", ", group.Permissions)}");
            Puts($"  Members ({members.Count}):");
            foreach (var m in members)
            {
                Puts($"    - {m.SteamId}");
            }
        }
    }
}
