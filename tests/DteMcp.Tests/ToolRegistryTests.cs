using System;
using System.Collections.Generic;
using System.Linq;
using DteMcp.Mcp;
using Xunit;

namespace DteMcp.Tests
{
    public class ToolRegistryTests
    {
        private static IEnumerable<ToolDefinition> Tools()
        {
            yield return ToolDefinition.ReadOnly("read", "reads", ToolSchema.Object(), _ => "read-result");
            yield return ToolDefinition.Mutating("write", "writes", ToolSchema.Object(), _ => "write-result");
        }

        private static ToolArguments NoArguments()
        {
            return new ToolArguments(null);
        }

        [Fact]
        public void ReadWriteMode_ListsAndCallsEveryTool()
        {
            var registry = new ToolRegistry(Tools(), false);

            Assert.Equal(new[] { "read", "write" }, registry.List().Select(t => (string)t["name"]).OrderBy(n => n));
            Assert.Equal("write-result", registry.Call("write", NoArguments()));
        }

        [Fact]
        public void ReadOnlyMode_HidesAndRejectsMutatingTools()
        {
            var registry = new ToolRegistry(Tools(), true);

            Assert.Equal(new[] { "read" }, registry.List().Select(t => (string)t["name"]));
            Assert.Equal("read-result", registry.Call("read", NoArguments()));
            Assert.Throws<InvalidOperationException>(() => registry.Call("write", NoArguments()));
        }

        [Fact]
        public void Call_UnknownToolThrows()
        {
            Assert.Throws<InvalidOperationException>(() => new ToolRegistry(Tools(), false).Call("nope", NoArguments()));
        }

        [Fact]
        public void List_MarksReadOnlyHints()
        {
            var tools = new ToolRegistry(Tools(), false).List().ToDictionary(t => (string)t["name"]);

            Assert.True((bool)((Dictionary<string, object>)tools["read"]["annotations"])["readOnlyHint"]);
            Assert.False((bool)((Dictionary<string, object>)tools["write"]["annotations"])["readOnlyHint"]);
        }

        [Fact]
        public void Schema_BuildsRequiredAndEnum()
        {
            var schema = ToolSchema.Object(new[] { "a" }, ToolSchema.String("a", "d", "x", "y"));

            Assert.Equal(new[] { "a" }, (string[])schema["required"]);
            var props = (Dictionary<string, object>)schema["properties"];
            Assert.Equal(new[] { "x", "y" }, (string[])((Dictionary<string, object>)props["a"])["enum"]);
        }
    }
}
