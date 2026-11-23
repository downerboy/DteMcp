using System;
using System.Collections.Generic;

namespace DteMcp.Mcp
{
    /// <summary>One MCP tool: its metadata and the handler that runs it.</summary>
    internal sealed class ToolDefinition
    {
        private ToolDefinition(string name, string description, Dictionary<string, object> inputSchema, bool isReadOnly, Func<ToolArguments, object> handler)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
            IsReadOnly = isReadOnly;
            Handler = handler;
        }

        public string Name { get; }

        public string Description { get; }

        public Dictionary<string, object> InputSchema { get; }

        /// <summary>True when the tool only reads state. Mutating tools are hidden when DTEMCP_READONLY=1.</summary>
        public bool IsReadOnly { get; }

        public Func<ToolArguments, object> Handler { get; }

        public static ToolDefinition ReadOnly(string name, string description, Dictionary<string, object> inputSchema, Func<ToolArguments, object> handler)
        {
            return new ToolDefinition(name, "Read-only. " + description, inputSchema, true, handler);
        }

        public static ToolDefinition Mutating(string name, string description, Dictionary<string, object> inputSchema, Func<ToolArguments, object> handler)
        {
            return new ToolDefinition(name, "Changes Visual Studio state. " + description, inputSchema, false, handler);
        }

        public Dictionary<string, object> ToMcp()
        {
            return new Dictionary<string, object>
            {
                { "name", Name },
                { "description", Description },
                { "inputSchema", InputSchema },
                { "annotations", new Dictionary<string, object> { { "readOnlyHint", IsReadOnly } } }
            };
        }
    }
}
