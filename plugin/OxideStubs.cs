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

    public class BasePlayer
    {
        public string UserIDString { get; set; } = "0";
        public string displayName { get; set; } = "Unknown";
        public string? Address { get; set; }
        public virtual Vector3 Position { get; set; } = new Vector3(0, 0, 0);
        public virtual Vector3 Rotation { get; set; } = new Vector3(0, 0, 0);
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
