using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DteMcp.Mcp
{
    /// <summary>Typed access to the "arguments" object of a tools/call request.</summary>
    internal sealed class ToolArguments
    {
        private readonly Dictionary<string, object> values;

        public ToolArguments(Dictionary<string, object> values)
        {
            this.values = values ?? new Dictionary<string, object>();
        }

        public bool Has(string key)
        {
            return values.ContainsKey(key) && values[key] != null;
        }

        public object GetValue(string key)
        {
            return Has(key) ? values[key] : null;
        }

        public string GetString(string key)
        {
            return Has(key) ? Convert.ToString(values[key], CultureInfo.InvariantCulture) : null;
        }

        public string RequireString(string key)
        {
            string value = GetString(key);
            return string.IsNullOrEmpty(value)
                ? throw new ArgumentException("Missing required argument: " + key)
                : value;
        }

        public int GetInt(string key, int fallback)
        {
            return Has(key) ? Convert.ToInt32(values[key], CultureInfo.InvariantCulture) : fallback;
        }

        public int RequireInt(string key)
        {
            return Has(key)
                ? GetInt(key, 0)
                : throw new ArgumentException("Missing required argument: " + key);
        }

        public bool GetBool(string key, bool fallback)
        {
            return Has(key) ? Convert.ToBoolean(values[key], CultureInfo.InvariantCulture) : fallback;
        }

        public IReadOnlyList<string> GetStringList(string key)
        {
            IEnumerable items = GetValue(key) as IEnumerable;
            return items == null || GetValue(key) is string
                ? new List<string>()
                : items.Cast<object>().Select(item => Convert.ToString(item, CultureInfo.InvariantCulture)).ToList();
        }
    }
}
