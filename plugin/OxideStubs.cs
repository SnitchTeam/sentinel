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
            public Oxide.Plugins.BasePlayer? _player { get; set; }
            public Oxide.Plugins.BasePlayer? Player() => _player;
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

    // -------------------------------------------------------------
    // CUI (Coherent UI) Stubs
    // -------------------------------------------------------------
    [System.Text.Json.Serialization.JsonConverter(typeof(CuiComponentConverter))]
    public interface ICuiComponent { }

    public class CuiRawImageComponent : ICuiComponent
    {
        [System.Text.Json.Serialization.JsonPropertyName("color")] public string Color { get; set; } = "1 1 1 1";
        [System.Text.Json.Serialization.JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("material")] public string Material { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("png")] public string Png { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("url")] public string Url { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("fadein")] public float? FadeIn { get; set; }
    }

    public class CuiImageComponent : ICuiComponent
    {
        [System.Text.Json.Serialization.JsonPropertyName("color")] public string Color { get; set; } = "1 1 1 1";
        [System.Text.Json.Serialization.JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("material")] public string Material { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("imagetype")] public int ImageType { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("png")] public string Png { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("fadein")] public float? FadeIn { get; set; }
    }

    public class CuiTextComponent : ICuiComponent
    {
        [System.Text.Json.Serialization.JsonPropertyName("text")] public string Text { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("fontsize")] public string FontSize { get; set; } = "12";
        [System.Text.Json.Serialization.JsonPropertyName("font")] public string Font { get; set; } = "robotocondensed-regular.ttf";
        [System.Text.Json.Serialization.JsonPropertyName("align")] public string Align { get; set; } = "UpperLeft";
        [System.Text.Json.Serialization.JsonPropertyName("color")] public string Color { get; set; } = "1 1 1 1";
        [System.Text.Json.Serialization.JsonPropertyName("fadein")] public float? FadeIn { get; set; }
    }

    public class CuiButtonComponent : ICuiComponent
    {
        [System.Text.Json.Serialization.JsonPropertyName("color")] public string Color { get; set; } = "1 1 1 1";
        [System.Text.Json.Serialization.JsonPropertyName("command")] public string Command { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("close")] public string Close { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("material")] public string Material { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("fadein")] public float? FadeIn { get; set; }
    }

    public class CuiInputFieldComponent : ICuiComponent
    {
        [System.Text.Json.Serialization.JsonPropertyName("text")] public string Text { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("fontsize")] public string FontSize { get; set; } = "12";
        [System.Text.Json.Serialization.JsonPropertyName("font")] public string Font { get; set; } = "robotocondensed-regular.ttf";
        [System.Text.Json.Serialization.JsonPropertyName("align")] public string Align { get; set; } = "UpperLeft";
        [System.Text.Json.Serialization.JsonPropertyName("color")] public string Color { get; set; } = "1 1 1 1";
        [System.Text.Json.Serialization.JsonPropertyName("charslimit")] public int CharsLimit { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("command")] public string Command { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("readonly")] public bool ReadOnly { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("password")] public bool Password { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("hudmenuinput")] public bool HudMenuInput { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("needskeyboard")] public bool NeedsKeyboard { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("linetype")] public string? LineType { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("placeholder")] public string? PlaceHolder { get; set; }
    }

    public class CuiRectTransformComponent : ICuiComponent
    {
        [System.Text.Json.Serialization.JsonPropertyName("anchormin")] public string AnchorMin { get; set; } = "0 0";
        [System.Text.Json.Serialization.JsonPropertyName("anchormax")] public string AnchorMax { get; set; } = "1 1";
        [System.Text.Json.Serialization.JsonPropertyName("offsetmin")] public string OffsetMin { get; set; } = "0 0";
        [System.Text.Json.Serialization.JsonPropertyName("offsetmax")] public string OffsetMax { get; set; } = "0 0";
    }

    public class CuiElement
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("parent")] public string Parent { get; set; } = "Hud";
        [System.Text.Json.Serialization.JsonPropertyName("components")] public System.Collections.Generic.List<ICuiComponent> Components { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("fadeout")] public float? FadeOut { get; set; }
    }

    public class CuiElementContainer : System.Collections.Generic.List<CuiElement>
    {
        public new string Add(CuiElement element)
        {
            base.Add(element);
            return element.Name;
        }
    }

    public enum BUTTON
    {
        FORWARD = 1 << 0,
        BACKWARD = 1 << 1,
        LEFT = 1 << 2,
        RIGHT = 1 << 3,
        JUMP = 1 << 4,
        DUCK = 1 << 5,
        SPRINT = 1 << 6,
        USE = 1 << 7,
        FIRE_PRIMARY = 1 << 8,
        FIRE_SECONDARY = 1 << 9,
        RELOAD = 1 << 10,
        FIRE_THIRD = 1 << 11,
        MAP = 1 << 20
    }

    public class InputState
    {
        public int current { get; set; }
        public int previous { get; set; }

        public bool IsDown(BUTTON button) => (current & (int)button) != 0;
        public bool WasDown(BUTTON button) => (previous & (int)button) != 0;
        public bool WasJustPressed(BUTTON button) => IsDown(button) && !WasDown(button);
    }

    public static class CuiHelper
    {
        private static readonly System.Text.Json.JsonSerializerOptions _options = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static string ToJson(CuiElementContainer container) => System.Text.Json.JsonSerializer.Serialize(container, _options);
        public static string GetGuid() => Guid.NewGuid().ToString("N")[..8];
        public static void AddUi(BasePlayer player, CuiElementContainer container) { }
        public static void DestroyUi(BasePlayer player, string name) { }
    }

    public class CuiComponentConverter : System.Text.Json.Serialization.JsonConverter<ICuiComponent>
    {
        public override ICuiComponent Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
            => throw new System.NotSupportedException();

        public override void Write(System.Text.Json.Utf8JsonWriter writer, ICuiComponent value, System.Text.Json.JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            string typeName = value switch
            {
                CuiRawImageComponent => "UnityEngine.UI.RawImage",
                CuiImageComponent => "UnityEngine.UI.Image",
                CuiTextComponent => "UnityEngine.UI.Text",
                CuiButtonComponent => "UnityEngine.UI.Button",
                CuiInputFieldComponent => "UnityEngine.UI.InputField",
                CuiRectTransformComponent => "RectTransform",
                _ => value.GetType().Name
            };
            writer.WriteString("type", typeName);
            foreach (var prop in value.GetType().GetProperties())
            {
                var propValue = prop.GetValue(value);
                if (propValue == null) continue;
                var jsonProp = prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false);
                string propName = jsonProp.Length > 0 ? ((System.Text.Json.Serialization.JsonPropertyNameAttribute)jsonProp[0]).Name : prop.Name.ToLowerInvariant();
                writer.WritePropertyName(propName);
                System.Text.Json.JsonSerializer.Serialize(writer, propValue, options);
            }
            writer.WriteEndObject();
        }
    }
}
