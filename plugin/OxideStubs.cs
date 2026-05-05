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

namespace Oxide.Plugins
{
    using Oxide.Core.Plugins;

    public class RustPlugin
    {
        public virtual void Puts(string message) { }
        public virtual void PrintWarning(string message) { }
        public virtual void PrintError(string message) { }

        public DynamicConfigFile? Config { get; set; }

        public virtual void LoadDefaultConfig() { }
    }
}
