using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    public class SentinelGroupsTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;
        private readonly MockPermission _mockPermission;
        private readonly List<TestPlayer> _localPlayers = new();

        public SentinelGroupsTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_groups_test_{Guid.NewGuid()}.db");
            _plugin = new TestableSentinel();
            _plugin.LocalPlayers = _localPlayers;
            _mockPermission = new MockPermission();
            _plugin.permission = _mockPermission;
            _plugin.InitializeDatabase(_dbPath);
        }

        public void Dispose()
        {
            _plugin.CloseDatabase();
            CleanupDbFiles(_dbPath);
        }

        private static void CleanupDbFiles(string dbPath)
        {
            try { File.Delete(dbPath); } catch { }
            try { File.Delete(dbPath + "-shm"); } catch { }
            try { File.Delete(dbPath + "-wal"); } catch { }
        }

        private TestPlayer CreatePlayer(ulong steamId, string name)
        {
            var p = new TestPlayer
            {
                UserIDString = steamId.ToString(),
                displayName = name
            };
            _localPlayers.Add(p);
            return p;
        }

        private List<AuditRow> GetAuditRows(string? actionType = null)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = actionType != null
                ? "SELECT actor_steam_id, target_steam_id, action_type, success FROM sentinel_actions WHERE action_type = @type ORDER BY id;"
                : "SELECT actor_steam_id, target_steam_id, action_type, success FROM sentinel_actions ORDER BY id;";
            if (actionType != null)
                command.Parameters.AddWithValue("@type", actionType);

            var rows = new List<AuditRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new AuditRow
                {
                    ActorSteamId = reader.GetString(0),
                    TargetSteamId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    ActionType = reader.GetString(2),
                    Success = reader.GetInt32(3) == 1
                });
            }
            return rows;
        }

        private class AuditRow
        {
            public string ActorSteamId { get; set; } = "";
            public string? TargetSteamId { get; set; }
            public string ActionType { get; set; } = "";
            public bool Success { get; set; }
        }

        private class TestableSentinel : SentinelPlugin
        {
            public List<TestPlayer> LocalPlayers { get; set; } = new();
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }

            protected override BasePlayer? ResolveTargetInternal(string identifier)
            {
                if (string.IsNullOrWhiteSpace(identifier)) return null;
                foreach (var p in LocalPlayers)
                {
                    if (p.UserIDString == identifier) return p;
                }
                foreach (var p in LocalPlayers)
                {
                    if (p.displayName.Contains(identifier, StringComparison.OrdinalIgnoreCase)) return p;
                }
                return null;
            }
        }

        private class TestPlayer : BasePlayer
        {
            public override void Kick(string reason) { }
            public override void ChatMessage(string message) { }
        }

        private class MockPermission : Oxide.Core.Libraries.Permission
        {
            private readonly Dictionary<string, HashSet<string>> _userPerms = new();
            private readonly Dictionary<string, HashSet<string>> _groupPerms = new();
            private readonly Dictionary<string, HashSet<string>> _userGroups = new();
            private readonly Dictionary<string, GroupInfo> _groups = new();

            public IReadOnlyDictionary<string, GroupInfo> Groups => _groups;
            public IReadOnlyDictionary<string, HashSet<string>> UserGroups => _userGroups;
            public IReadOnlyDictionary<string, HashSet<string>> GroupPermissions => _groupPerms;

            public class GroupInfo
            {
                public string Title { get; set; } = "";
                public int Rank { get; set; }
                public string? Parent { get; set; }
            }

            public void Grant(string userId, string perm)
            {
                if (!_userPerms.TryGetValue(userId, out var set))
                {
                    set = new HashSet<string>();
                    _userPerms[userId] = set;
                }
                set.Add(perm);
            }

            public void Revoke(string userId, string perm)
            {
                if (_userPerms.TryGetValue(userId, out var set))
                    set.Remove(perm);
            }

            public override bool UserHasPermission(string id, string perm)
            {
                if (_userPerms.TryGetValue(id, out var set))
                {
                    if (set.Contains(perm)) return true;
                    if (set.Contains("sentinel.*"))
                    {
                        return perm.StartsWith("sentinel.", StringComparison.OrdinalIgnoreCase);
                    }
                }
                return false;
            }

            public override bool CreateGroup(string group, string title, int rank)
            {
                _groups[group] = new GroupInfo { Title = title, Rank = rank };
                if (!_groupPerms.ContainsKey(group))
                    _groupPerms[group] = new HashSet<string>();
                return true;
            }

            public override bool RemoveGroup(string group)
            {
                _groups.Remove(group);
                _groupPerms.Remove(group);
                return true;
            }

            public override bool GroupExists(string group)
            {
                return _groups.ContainsKey(group);
            }

            public override string[] GetGroups()
            {
                return _groups.Keys.ToArray();
            }

            public override string[] GetGroupPermissions(string group)
            {
                return _groupPerms.TryGetValue(group, out var perms) ? perms.ToArray() : Array.Empty<string>();
            }

            public override void GrantGroupPermission(string group, string perm, Oxide.Plugins.RustPlugin owner)
            {
                if (!_groupPerms.TryGetValue(group, out var set))
                {
                    set = new HashSet<string>();
                    _groupPerms[group] = set;
                }
                set.Add(perm);
            }

            public override void RevokeGroupPermission(string group, string perm)
            {
                if (_groupPerms.TryGetValue(group, out var set))
                    set.Remove(perm);
            }

            public override void AddUserGroup(string id, string group)
            {
                if (!_userGroups.TryGetValue(id, out var set))
                {
                    set = new HashSet<string>();
                    _userGroups[id] = set;
                }
                set.Add(group);
            }

            public override void RemoveUserGroup(string id, string group)
            {
                if (_userGroups.TryGetValue(id, out var set))
                    set.Remove(group);
            }

            public override string[] GetUsersInGroup(string group)
            {
                var users = new List<string>();
                foreach (var kvp in _userGroups)
                {
                    if (kvp.Value.Contains(group))
                        users.Add(kvp.Key);
                }
                return users.ToArray();
            }

            public override string[] GetUserGroups(string id)
            {
                return _userGroups.TryGetValue(id, out var groups) ? groups.ToArray() : Array.Empty<string>();
            }

            public override bool SetGroupTitle(string group, string title)
            {
                if (!_groups.TryGetValue(group, out var info)) return false;
                info.Title = title;
                return true;
            }

            public override bool SetGroupParent(string group, string parent)
            {
                if (!_groups.TryGetValue(group, out var info)) return false;
                info.Parent = string.IsNullOrEmpty(parent) ? null : parent;
                return true;
            }
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-018: Default groups exist on first install
        // ---------------------------------------------------------
        [Fact]
        public void DefaultGroups_AreCreatedOnInit()
        {
            var plugin = new TestableSentinel();
            plugin.permission = new MockPermission();
            plugin.InitializeDatabase(_dbPath);
            plugin.InitializeDefaultGroups();

            Assert.True(plugin.GroupExistsInDb("sentinel_admin"));
            Assert.True(plugin.GroupExistsInDb("sentinel_moderator"));
            Assert.True(plugin.GroupExistsInDb("sentinel_trial_mod"));
        }

        [Fact]
        public void DefaultGroups_AreSystemProtected()
        {
            var plugin = new TestableSentinel();
            plugin.permission = new MockPermission();
            plugin.InitializeDatabase(_dbPath);
            plugin.InitializeDefaultGroups();

            var admin = plugin.GetGroupFromDb("sentinel_admin");
            var mod = plugin.GetGroupFromDb("sentinel_moderator");
            var trial = plugin.GetGroupFromDb("sentinel_trial_mod");

            Assert.NotNull(admin);
            Assert.NotNull(mod);
            Assert.NotNull(trial);
            Assert.True(admin!.SystemProtected);
            Assert.True(mod!.SystemProtected);
            Assert.True(trial!.SystemProtected);
        }

        [Fact]
        public void DefaultGroups_HaveCorrectTitles()
        {
            var plugin = new TestableSentinel();
            plugin.permission = new MockPermission();
            plugin.InitializeDatabase(_dbPath);
            plugin.InitializeDefaultGroups();

            var admin = plugin.GetGroupFromDb("sentinel_admin");
            var mod = plugin.GetGroupFromDb("sentinel_moderator");

            Assert.Equal("Sentinel Admin", admin?.Title);
            Assert.Equal("Sentinel Moderator", mod?.Title);
        }

        [Fact]
        public void DefaultGroups_AreCreatedInOxide()
        {
            var mock = new MockPermission();
            var plugin = new TestableSentinel();
            plugin.permission = mock;
            plugin.InitializeDatabase(_dbPath);
            plugin.InitializeDefaultGroups();

            Assert.True(mock.GroupExists("sentinel_admin"));
            Assert.True(mock.GroupExists("sentinel_moderator"));
            Assert.True(mock.GroupExists("sentinel_trial_mod"));
        }

        [Fact]
        public void DefaultGroups_HavePermissionsInOxide()
        {
            var mock = new MockPermission();
            var plugin = new TestableSentinel();
            plugin.permission = mock;
            plugin.InitializeDatabase(_dbPath);
            plugin.InitializeDefaultGroups();

            var adminPerms = mock.GetGroupPermissions("sentinel_admin");
            var modPerms = mock.GetGroupPermissions("sentinel_moderator");
            var trialPerms = mock.GetGroupPermissions("sentinel_trial_mod");

            Assert.Contains("sentinel.*", adminPerms ?? Array.Empty<string>());
            Assert.Contains("sentinel.kick", modPerms ?? Array.Empty<string>());
            Assert.Contains("sentinel.kick", trialPerms ?? Array.Empty<string>());
        }

        [Fact]
        public void DefaultGroups_AreIdempotent_OnRepeatedInit()
        {
            var plugin = new TestableSentinel();
            plugin.permission = new MockPermission();
            plugin.InitializeDatabase(_dbPath);
            plugin.InitializeDefaultGroups();
            plugin.InitializeDefaultGroups();
            plugin.InitializeDefaultGroups();

            var groups = plugin.GetAllGroups();
            Assert.Equal(3, groups.Count);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-019: Permission groups can be created
        // ---------------------------------------------------------
        [Fact]
        public void CreateGroup_WithNameAndTitle_Succeeds()
        {
            var result = _plugin.CreateGroup("test_group", "Test Group", null, out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.True(_plugin.GroupExistsInDb("test_group"));
        }

        [Fact]
        public void CreateGroup_WithParent_Succeeds()
        {
            _plugin.CreateGroup("parent_group", "Parent", null, out _);
            var result = _plugin.CreateGroup("child_group", "Child", "parent_group", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var child = _plugin.GetGroupFromDb("child_group");
            Assert.Equal("parent_group", child?.ParentGroup);
        }

        [Fact]
        public void CreateGroup_DuplicateName_Fails()
        {
            _plugin.CreateGroup("dup_group", "Dup", null, out _);
            var result = _plugin.CreateGroup("dup_group", "Dup2", null, out var error);

            Assert.False(result);
            Assert.Contains("already exists", error);
        }

        [Fact]
        public void CreateGroup_EmptyName_Fails()
        {
            var result = _plugin.CreateGroup("", "Empty", null, out var error);

            Assert.False(result);
            Assert.Contains("name is required", error);
        }

        [Fact]
        public void CreateGroup_IsCreatedInOxide()
        {
            _plugin.CreateGroup("oxide_group", "Oxide Group", null, out _);
            Assert.True(_mockPermission.GroupExists("oxide_group"));
        }

        [Fact]
        public void CreateGroup_IsNotSystemProtected()
        {
            _plugin.CreateGroup("normal_group", "Normal", null, out _);
            var group = _plugin.GetGroupFromDb("normal_group");
            Assert.False(group?.SystemProtected);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-020: Permission groups can be deleted
        // ---------------------------------------------------------
        [Fact]
        public void DeleteGroup_ExistingGroup_Succeeds()
        {
            _plugin.CreateGroup("del_group", "To Delete", null, out _);
            var result = _plugin.DeleteGroup("del_group", out var error);

            Assert.True(result);
            Assert.Empty(error);
            Assert.False(_plugin.GroupExistsInDb("del_group"));
        }

        [Fact]
        public void DeleteGroup_RemovesFromOxide()
        {
            _plugin.CreateGroup("del_oxide", "To Delete", null, out _);
            _plugin.DeleteGroup("del_oxide", out _);

            Assert.False(_mockPermission.GroupExists("del_oxide"));
        }

        [Fact]
        public void DeleteGroup_SystemProtected_Fails()
        {
            var plugin = new TestableSentinel();
            plugin.permission = new MockPermission();
            plugin.InitializeDatabase(_dbPath);
            plugin.InitializeDefaultGroups();

            var result = plugin.DeleteGroup("sentinel_admin", out var error);

            Assert.False(result);
            Assert.Contains("system-protected", error);
            Assert.True(plugin.GroupExistsInDb("sentinel_admin"));
        }

        [Fact]
        public void DeleteGroup_NonExistent_Fails()
        {
            var result = _plugin.DeleteGroup("no_exist", out var error);

            Assert.False(result);
            Assert.Contains("does not exist", error);
        }

        [Fact]
        public void DeleteGroup_RemovesMembers()
        {
            _plugin.CreateGroup("del_members", "Del Members", null, out _);
            _plugin.AddUserToGroup("del_members", "76561190000000001", out _);
            _plugin.DeleteGroup("del_members", out _);

            var members = _plugin.GetGroupMembers("del_members");
            Assert.Empty(members);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-021: Permission groups can be updated (title/parent)
        // ---------------------------------------------------------
        [Fact]
        public void UpdateGroupTitle_Succeeds()
        {
            _plugin.CreateGroup("upd_title", "Old Title", null, out _);
            var result = _plugin.UpdateGroupTitle("upd_title", "New Title", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var group = _plugin.GetGroupFromDb("upd_title");
            Assert.Equal("New Title", group?.Title);
        }

        [Fact]
        public void UpdateGroupTitle_UpdatesOxide()
        {
            _plugin.CreateGroup("upd_oxide_title", "Old", null, out _);
            _plugin.UpdateGroupTitle("upd_oxide_title", "New", out _);

            Assert.Equal("New", _mockPermission.Groups["upd_oxide_title"].Title);
        }

        [Fact]
        public void UpdateGroupParent_Succeeds()
        {
            _plugin.CreateGroup("upd_parent", "Parent", null, out _);
            _plugin.CreateGroup("upd_child", "Child", null, out _);
            var result = _plugin.UpdateGroupParent("upd_child", "upd_parent", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var child = _plugin.GetGroupFromDb("upd_child");
            Assert.Equal("upd_parent", child?.ParentGroup);
        }

        [Fact]
        public void UpdateGroupParent_ToNone_Succeeds()
        {
            _plugin.CreateGroup("upd_parent2", "Parent", null, out _);
            _plugin.CreateGroup("upd_child2", "Child", "upd_parent2", out _);
            var result = _plugin.UpdateGroupParent("upd_child2", null, out var error);

            Assert.True(result);
            Assert.Empty(error);

            var child = _plugin.GetGroupFromDb("upd_child2");
            Assert.Null(child?.ParentGroup);
        }

        [Fact]
        public void UpdateGroupParent_InvalidParent_Fails()
        {
            _plugin.CreateGroup("upd_child3", "Child", null, out _);
            var result = _plugin.UpdateGroupParent("upd_child3", "nonexistent", out var error);

            Assert.False(result);
            Assert.Contains("does not exist", error);
        }

        [Fact]
        public void UpdateGroup_NonExistent_Fails()
        {
            var result = _plugin.UpdateGroupTitle("no_exist", "Title", out var error);
            Assert.False(result);
            Assert.Contains("does not exist", error);
        }

        [Fact]
        public void UpdateGroup_PersistsAfterReload()
        {
            _plugin.CreateGroup("persist_group", "Original", null, out _);
            _plugin.UpdateGroupTitle("persist_group", "Updated", out _);
            _plugin.UpdateGroupParent("persist_group", null, out _);

            // Simulate reload: close and reopen database
            _plugin.CloseDatabase();
            _plugin.InitializeDatabase(_dbPath);

            var group = _plugin.GetGroupFromDb("persist_group");
            Assert.NotNull(group);
            Assert.Equal("Updated", group!.Title);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-022: Users can be assigned to and removed from groups
        // ---------------------------------------------------------
        [Fact]
        public void AddUserToGroup_Succeeds()
        {
            _plugin.CreateGroup("member_group", "Member Group", null, out _);
            var result = _plugin.AddUserToGroup("member_group", "76561190000000001", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var members = _plugin.GetGroupMembers("member_group");
            Assert.Single(members);
            Assert.Equal("76561190000000001", members[0].SteamId);
        }

        [Fact]
        public void AddUserToGroup_ByPlayerName_Succeeds()
        {
            var player = CreatePlayer(76561190000000002, "TestPlayer");
            _plugin.CreateGroup("name_group", "Name Group", null, out _);
            var result = _plugin.AddUserToGroup("name_group", "TestPlayer", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var members = _plugin.GetGroupMembers("name_group");
            Assert.Single(members);
            Assert.Equal("76561190000000002", members[0].SteamId);
        }

        [Fact]
        public void AddUserToGroup_AddsToOxide()
        {
            _plugin.CreateGroup("oxide_member", "Oxide Member", null, out _);
            _plugin.AddUserToGroup("oxide_member", "76561190000000003", out _);

            var groups = _mockPermission.GetUserGroups("76561190000000003");
            Assert.Contains("oxide_member", groups);
        }

        [Fact]
        public void AddUserToGroup_Duplicate_Fails()
        {
            _plugin.CreateGroup("dup_member", "Dup Member", null, out _);
            _plugin.AddUserToGroup("dup_member", "76561190000000001", out _);
            var result = _plugin.AddUserToGroup("dup_member", "76561190000000001", out var error);

            Assert.False(result);
            Assert.Contains("already in group", error);
        }

        [Fact]
        public void AddUserToGroup_InvalidGroup_Fails()
        {
            var result = _plugin.AddUserToGroup("no_group", "76561190000000001", out var error);

            Assert.False(result);
            Assert.Contains("does not exist", error);
        }

        [Fact]
        public void RemoveUserFromGroup_Succeeds()
        {
            _plugin.CreateGroup("rem_group", "Remove Group", null, out _);
            _plugin.AddUserToGroup("rem_group", "76561190000000001", out _);
            var result = _plugin.RemoveUserFromGroup("rem_group", "76561190000000001", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var members = _plugin.GetGroupMembers("rem_group");
            Assert.Empty(members);
        }

        [Fact]
        public void RemoveUserFromGroup_RemovesFromOxide()
        {
            _plugin.CreateGroup("rem_oxide", "Rem Oxide", null, out _);
            _plugin.AddUserToGroup("rem_oxide", "76561190000000004", out _);
            _plugin.RemoveUserFromGroup("rem_oxide", "76561190000000004", out _);

            var groups = _mockPermission.GetUserGroups("76561190000000004");
            Assert.DoesNotContain("rem_oxide", groups);
        }

        [Fact]
        public void RemoveUserFromGroup_InvalidGroup_Fails()
        {
            var result = _plugin.RemoveUserFromGroup("no_group", "76561190000000001", out var error);

            Assert.False(result);
            Assert.Contains("does not exist", error);
        }

        [Fact]
        public void UserAssignment_IsImmediate()
        {
            var player = CreatePlayer(76561190000000005, "Immediate");
            _plugin.CreateGroup("imm_group", "Immediate", null, out _);
            _plugin.GrantGroupPermission("imm_group", "sentinel.kick", out _);

            _plugin.AddUserToGroup("imm_group", "76561190000000005", out _);

            var userGroups = _mockPermission.GetUserGroups("76561190000000005");
            Assert.Contains("imm_group", userGroups);
        }

        [Fact]
        public void UserRemoval_IsImmediate()
        {
            _plugin.CreateGroup("rem_imm", "Rem Immediate", null, out _);
            _plugin.AddUserToGroup("rem_imm", "76561190000000006", out _);
            _plugin.RemoveUserFromGroup("rem_imm", "76561190000000006", out _);

            var userGroups = _mockPermission.GetUserGroups("76561190000000006");
            Assert.DoesNotContain("rem_imm", userGroups);
        }

        // ---------------------------------------------------------
        // VAL-ADMIN-023: Group permissions can be granted and revoked
        // ---------------------------------------------------------
        [Fact]
        public void GrantGroupPermission_Succeeds()
        {
            _plugin.CreateGroup("perm_group", "Perm Group", null, out _);
            var result = _plugin.GrantGroupPermission("perm_group", "sentinel.kick", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var group = _plugin.GetGroupFromDb("perm_group");
            Assert.Contains("sentinel.kick", group?.Permissions ?? new List<string>());
        }

        [Fact]
        public void GrantGroupPermission_GrantsInOxide()
        {
            _plugin.CreateGroup("oxide_perm", "Oxide Perm", null, out _);
            _plugin.GrantGroupPermission("oxide_perm", "sentinel.ban", out _);

            var perms = _mockPermission.GetGroupPermissions("oxide_perm");
            Assert.Contains("sentinel.ban", perms);
        }

        [Fact]
        public void GrantGroupPermission_Duplicate_IsIdempotent()
        {
            _plugin.CreateGroup("dup_perm", "Dup Perm", null, out _);
            _plugin.GrantGroupPermission("dup_perm", "sentinel.kick", out _);
            _plugin.GrantGroupPermission("dup_perm", "sentinel.kick", out _);

            var group = _plugin.GetGroupFromDb("dup_perm");
            Assert.Single(group?.Permissions.Where(p => p == "sentinel.kick") ?? Array.Empty<string>());
        }

        [Fact]
        public void GrantGroupPermission_InvalidGroup_Fails()
        {
            var result = _plugin.GrantGroupPermission("no_group", "sentinel.kick", out var error);

            Assert.False(result);
            Assert.Contains("does not exist", error);
        }

        [Fact]
        public void RevokeGroupPermission_Succeeds()
        {
            _plugin.CreateGroup("rev_group", "Rev Group", null, out _);
            _plugin.GrantGroupPermission("rev_group", "sentinel.kick", out _);
            var result = _plugin.RevokeGroupPermission("rev_group", "sentinel.kick", out var error);

            Assert.True(result);
            Assert.Empty(error);

            var group = _plugin.GetGroupFromDb("rev_group");
            Assert.DoesNotContain("sentinel.kick", group?.Permissions ?? new List<string>());
        }

        [Fact]
        public void RevokeGroupPermission_RevokesInOxide()
        {
            _plugin.CreateGroup("rev_oxide", "Rev Oxide", null, out _);
            _plugin.GrantGroupPermission("rev_oxide", "sentinel.ban", out _);
            _plugin.RevokeGroupPermission("rev_oxide", "sentinel.ban", out _);

            var perms = _mockPermission.GetGroupPermissions("rev_oxide");
            Assert.DoesNotContain("sentinel.ban", perms);
        }

        [Fact]
        public void RevokeGroupPermission_InvalidGroup_Fails()
        {
            var result = _plugin.RevokeGroupPermission("no_group", "sentinel.kick", out var error);

            Assert.False(result);
            Assert.Contains("does not exist", error);
        }

        [Fact]
        public void MemberEffectivePermissions_ReflectGroupPermissions()
        {
            _plugin.CreateGroup("eff_group", "Effective", null, out _);
            _plugin.GrantGroupPermission("eff_group", "sentinel.kick", out _);
            _plugin.GrantGroupPermission("eff_group", "sentinel.ban", out _);
            _plugin.AddUserToGroup("eff_group", "76561190000000007", out _);

            var userGroups = _mockPermission.GetUserGroups("76561190000000007");
            Assert.Contains("eff_group", userGroups);

            var groupPerms = _mockPermission.GetGroupPermissions("eff_group");
            Assert.Contains("sentinel.kick", groupPerms);
            Assert.Contains("sentinel.ban", groupPerms);
        }

        [Fact]
        public void RevokePermission_RemovesFromMembersEffectivePermissions()
        {
            _plugin.CreateGroup("rev_eff", "Rev Effective", null, out _);
            _plugin.GrantGroupPermission("rev_eff", "sentinel.kick", out _);
            _plugin.RevokeGroupPermission("rev_eff", "sentinel.kick", out _);

            var groupPerms = _mockPermission.GetGroupPermissions("rev_eff");
            Assert.DoesNotContain("sentinel.kick", groupPerms);
        }

        // ---------------------------------------------------------
        // Query / List Tests
        // ---------------------------------------------------------
        [Fact]
        public void GetAllGroups_ReturnsAllGroups()
        {
            _plugin.CreateGroup("g1", "Group 1", null, out _);
            _plugin.CreateGroup("g2", "Group 2", null, out _);
            _plugin.CreateGroup("g3", "Group 3", null, out _);

            var groups = _plugin.GetAllGroups();
            Assert.Equal(3, groups.Count);
        }

        [Fact]
        public void GetGroupMembers_ReturnsCorrectMembers()
        {
            _plugin.CreateGroup("mem_group", "Member Group", null, out _);
            _plugin.AddUserToGroup("mem_group", "76561190000000001", out _);
            _plugin.AddUserToGroup("mem_group", "76561190000000002", out _);

            var members = _plugin.GetGroupMembers("mem_group");
            Assert.Equal(2, members.Count);
        }

        [Fact]
        public void GetUserGroupNames_ReturnsUserGroups()
        {
            _plugin.CreateGroup("ug1", "User Group 1", null, out _);
            _plugin.CreateGroup("ug2", "User Group 2", null, out _);
            _plugin.AddUserToGroup("ug1", "76561190000000010", out _);
            _plugin.AddUserToGroup("ug2", "76561190000000010", out _);

            var groups = _plugin.GetUserGroupNames("76561190000000010");
            Assert.Equal(2, groups.Count);
            Assert.Contains("ug1", groups);
            Assert.Contains("ug2", groups);
        }

        [Fact]
        public void GroupExistsInDb_ReturnsFalseForMissing()
        {
            Assert.False(_plugin.GroupExistsInDb("nonexistent"));
        }

        [Fact]
        public void GetGroupFromDb_ReturnsNullForMissing()
        {
            Assert.Null(_plugin.GetGroupFromDb("nonexistent"));
        }

        // ---------------------------------------------------------
        // Persistence across reload
        // ---------------------------------------------------------
        [Fact]
        public void GroupMembership_PersistsAfterReload()
        {
            _plugin.CreateGroup("persist_member", "Persist Member", null, out _);
            _plugin.AddUserToGroup("persist_member", "76561190000000020", out _);

            _plugin.CloseDatabase();
            _plugin.InitializeDatabase(_dbPath);

            var members = _plugin.GetGroupMembers("persist_member");
            Assert.Single(members);
            Assert.Equal("76561190000000020", members[0].SteamId);
        }

        [Fact]
        public void GroupPermissions_PersistAfterReload()
        {
            _plugin.CreateGroup("persist_perm", "Persist Perm", null, out _);
            _plugin.GrantGroupPermission("persist_perm", "sentinel.kick", out _);
            _plugin.GrantGroupPermission("persist_perm", "sentinel.warn", out _);

            _plugin.CloseDatabase();
            _plugin.InitializeDatabase(_dbPath);

            var group = _plugin.GetGroupFromDb("persist_perm");
            Assert.Contains("sentinel.kick", group?.Permissions ?? new List<string>());
            Assert.Contains("sentinel.warn", group?.Permissions ?? new List<string>());
        }
    }
}
