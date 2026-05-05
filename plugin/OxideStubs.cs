using System;

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
}

namespace Oxide.Plugins
{
    public class RustPlugin
    {
        public void Puts(string message) { }
        public void PrintWarning(string message) { }
        public void PrintError(string message) { }
    }
}
