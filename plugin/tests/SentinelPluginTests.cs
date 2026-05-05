using System;
using System.Reflection;
using Oxide.Core.Plugins;
using Xunit;

namespace Sentinel.Tests
{
    public class SentinelPluginTests
    {
        [Fact]
        public void PluginClass_HasInfoAttribute_WithCorrectMetadata()
        {
            var type = typeof(Oxide.Plugins.Sentinel);
            var attr = type.GetCustomAttribute<InfoAttribute>();

            Assert.NotNull(attr);
            Assert.Equal("Sentinel", attr.Title);
            Assert.False(string.IsNullOrEmpty(attr.Author));
            Assert.False(string.IsNullOrEmpty(attr.Version));
            Assert.Matches(@"^\d+\.\d+\.\d+.*$", attr.Version);
        }

        [Fact]
        public void PluginClass_IsPublicClass()
        {
            var type = typeof(Oxide.Plugins.Sentinel);
            Assert.True(type.IsClass);
            Assert.True(type.IsPublic);
        }

        [Fact]
        public void PluginClass_DerivesFromRustPlugin()
        {
            var type = typeof(Oxide.Plugins.Sentinel);
            Assert.True(typeof(Oxide.Plugins.RustPlugin).IsAssignableFrom(type));
        }
    }
}
