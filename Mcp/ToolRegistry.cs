using System;
using System.Collections.Generic;
using System.Linq;

namespace DteMcp.Mcp
{
    internal interface IToolProvider
    {
        IEnumerable<ToolDefinition> Tools();
    }

    /// <summary>Holds the exposed tools. In read-only mode mutating tools are neither listed nor callable.</summary>
    internal sealed class ToolRegistry
    {
        private readonly Dictionary<string, ToolDefinition> tools;
        private readonly bool readOnlyMode;

        public ToolRegistry(IEnumerable<ToolDefinition> definitions, bool readOnlyMode)
        {
            this.readOnlyMode = readOnlyMode;
            tools = definitions
                .Where(definition => !readOnlyMode || definition.IsReadOnly)
                .ToDictionary(definition => definition.Name, StringComparer.Ordinal);
        }

        public IEnumerable<Dictionary<string, object>> List()
        {
            return tools.Values.Select(definition => definition.ToMcp()).ToList();
        }

        public object Call(string name, ToolArguments arguments)
        {
            if (name == null || !tools.ContainsKey(name))
            {
                string hint = readOnlyMode ? " (read-only mode: DTEMCP_READONLY=1)" : string.Empty;
                throw new InvalidOperationException("Unknown tool: " + name + hint);
            }

            return tools[name].Handler(arguments);
        }
    }
}
