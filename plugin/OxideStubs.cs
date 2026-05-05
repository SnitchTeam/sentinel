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
        public virtual void Kick(string reason) { }
        public virtual void ChatMessage(string message) { }

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
