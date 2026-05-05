using System;
using System.IO;
using System.Text.Json;

namespace Oxide.Core.Plugins
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class InfoAttribute : Attribute
    {
        public string Title { get; }
        public string Author { get; }
        public string Version { get; }
        public int ResourceId { get; set; }

        public InfoAttribute(string title, string author, string version)
        {
            Title = title;
            Author = author;
            Version = version;
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class DescriptionAttribute : Attribute
    {
        public string Description { get; }
        public DescriptionAttribute(string description) => Description = description;
    }

    public class DynamicConfigFile
    {
        private readonly string _path;

        public DynamicConfigFile(string path) => _path = path;

        public void WriteObject<T>(T obj, bool indent = true)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions
            {
                WriteIndented = indent,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(obj, options));
        }

        public T? ReadObject<T>()
        {
            if (!File.Exists(_path)) return default;
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
        }
    }
}

namespace Oxide.Core.Libraries
{
    public class Permission
    {
        public virtual bool UserHasPermission(string id, string perm) => false;
        public virtual void RegisterPermission(string perm, Oxide.Plugins.RustPlugin owner) { }

        public virtual bool CreateGroup(string group, string title, int rank) => true;
        public virtual bool RemoveGroup(string group) => true;
        public virtual bool GroupExists(string group) => false;
        public virtual string[] GetGroups() => System.Array.Empty<string>();
        public virtual string[] GetGroupPermissions(string group) => System.Array.Empty<string>();
        public virtual void GrantGroupPermission(string group, string perm, Oxide.Plugins.RustPlugin owner) { }
        public virtual void RevokeGroupPermission(string group, string perm) { }
        public virtual void AddUserGroup(string id, string group) { }
        public virtual void RemoveUserGroup(string id, string group) { }
        public virtual string[] GetUsersInGroup(string group) => System.Array.Empty<string>();
        public virtual string[] GetUserGroups(string id) => System.Array.Empty<string>();
        public virtual bool SetGroupTitle(string group, string title) => true;
        public virtual bool SetGroupParent(string group, string parent) => true;
    }
}

namespace Oxide.Core
{
    public class ConsoleSystem
    {
        public class Arg
        {
            public string[] Args { get; set; } = System.Array.Empty<string>();
            public Oxide.Plugins.BasePlayer? Player() => null;
        }
    }
}

namespace Oxide.Plugins
{
    using Oxide.Core.Plugins;

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new(a.x * d, a.y * d, a.z * d);

        public float Magnitude => (float)System.Math.Sqrt(x * x + y * y + z * z);

        public static float Distance(Vector3 a, Vector3 b) => (a - b).Magnitude;
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false)]
    public class ChatCommandAttribute : System.Attribute
    {
        public string Name { get; }
        public ChatCommandAttribute(string name) => Name = name;
    }

    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false)]
    public class ConsoleCommandAttribute : System.Attribute
    {
        public string Name { get; }
        public ConsoleCommandAttribute(string name) => Name = name;
    }

    public class ItemDefinition
    {
        public string shortname { get; set; } = "";
        public string displayName { get; set; } = "";
        public int stackable { get; set; } = 1;
    }

    public class Item
    {
        public ItemDefinition info { get; set; } = new ItemDefinition();
        public int amount { get; set; } = 0;

        public virtual void Drop(Vector3 position, Vector3 velocity) { }
    }

    public class ItemContainer
    {
        public int capacity { get; set; } = 24;
        public System.Collections.Generic.List<Item> itemList { get; set; } = new();
    }

    public class PlayerInventory
    {
        public ItemContainer containerMain { get; set; } = new ItemContainer();

        public virtual bool GiveItem(Item item, ItemContainer? container = null)
        {
            var target = container ?? containerMain;
            if (target.itemList.Count < target.capacity)
            {
                target.itemList.Add(item);
                return true;
            }
            return false;
        }
    }

    public static class ItemManager
    {
        public static System.Collections.Generic.List<ItemDefinition> itemList { get; set; } = new();

        public static Item? CreateByName(string shortname, int amount)
        {
            var def = itemList.Find(d => d.shortname.Equals(shortname, System.StringComparison.OrdinalIgnoreCase));
            if (def == null) return null;
            return new Item { info = def, amount = amount };
        }
    }

    public class BasePlayer
    {
        public string UserIDString { get; set; } = "0";
        public string displayName { get; set; } = "Unknown";
        public string? Address { get; set; }
        public virtual Vector3 Position { get; set; } = new Vector3(0, 0, 0);
        public virtual Vector3 Rotation { get; set; } = new Vector3(0, 0, 0);
        public PlayerInventory inventory { get; set; } = new PlayerInventory();
        public virtual void Kick(string reason) { }
        public virtual void ChatMessage(string message) { }
        public virtual void SendNetworkUpdate() { }
        public virtual void UpdateSpectating() { }
        public virtual void SetPlayerFlag(string flag, bool value) { }

        public static System.Collections.Generic.List<BasePlayer> activePlayerList { get; } = new();
    }

    public class AuthenticationTicketIdentity
    {
        public string Userid { get; set; } = "";
    }

    public class RustPlugin
    {
        public virtual void Puts(string message) { }
        public virtual void PrintWarning(string message) { }
        public virtual void PrintError(string message) { }

        public DynamicConfigFile? Config { get; set; }
        public Oxide.Core.Libraries.Permission? permission { get; set; }

        public virtual void LoadDefaultConfig() { }
    }
}
