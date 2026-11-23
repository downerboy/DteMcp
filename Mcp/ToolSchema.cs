using System.Collections.Generic;
using System.Linq;

namespace DteMcp.Mcp
{
    internal sealed class SchemaProperty
    {
        public SchemaProperty(string name, Dictionary<string, object> schema)
        {
            Name = name;
            Schema = schema;
        }

        public string Name { get; }

        public Dictionary<string, object> Schema { get; }
    }

    /// <summary>Small builder for JSON Schema fragments used as tool input schemas.</summary>
    internal static class ToolSchema
    {
        public static Dictionary<string, object> Object(params SchemaProperty[] properties)
        {
            return Object(new string[0], properties);
        }

        public static Dictionary<string, object> Object(string[] required, params SchemaProperty[] properties)
        {
            Dictionary<string, object> schema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties.ToDictionary(p => p.Name, p => (object)p.Schema) }
            };

            if (required.Length > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        public static SchemaProperty String(string name, string description, params string[] allowed)
        {
            Dictionary<string, object> schema = Typed("string", description);
            if (allowed.Length > 0)
            {
                schema["enum"] = allowed;
            }

            return new SchemaProperty(name, schema);
        }

        public static SchemaProperty Integer(string name, string description)
        {
            return new SchemaProperty(name, Typed("integer", description));
        }

        public static SchemaProperty Boolean(string name, string description)
        {
            return new SchemaProperty(name, Typed("boolean", description));
        }

        /// <summary>Any JSON value (string, number, boolean).</summary>
        public static SchemaProperty Value(string name, string description)
        {
            return new SchemaProperty(name, new Dictionary<string, object> { { "description", description } });
        }

        public static SchemaProperty StringList(string name, string description)
        {
            Dictionary<string, object> schema = Typed("array", description);
            schema["items"] = new Dictionary<string, object> { { "type", "string" } };
            return new SchemaProperty(name, schema);
        }

        private static Dictionary<string, object> Typed(string type, string description)
        {
            return new Dictionary<string, object> { { "type", type }, { "description", description } };
        }
    }
}
