using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic;

namespace DteMcp.Vs
{
    /// <summary>Turns arbitrary values returned by the DTE object model into JSON-friendly data.</summary>
    internal static class ObjectFormatter
    {
        private const int MaxItems = 50;
        private const int MaxStringLength = 20000;

        private static readonly string[] IdentifyingProperties = { "Name", "FullName", "Kind", "Caption", "Count" };

        public static object Format(object value, int depth)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string text)
            {
                return text.Length > MaxStringLength ? text.Substring(0, MaxStringLength) + "...(truncated)" : text;
            }

            if (value is char || value is Enum || value is Guid)
            {
                return value.ToString();
            }

            if (value is DateTime date)
            {
                return date.ToString("o");
            }

            if (IsScalar(value))
            {
                return value;
            }

            if (value is Array array)
            {
                return array.Cast<object>().Take(MaxItems).Select(item => Format(item, depth)).ToList();
            }

            return Marshal.IsComObject(value) ? DescribeComObject(value, depth) : value.ToString();
        }

        public static bool IsScalar(object value)
        {
            return value != null && (value.GetType().IsPrimitive || value is decimal) && !(value is IntPtr) && !(value is UIntPtr);
        }

        private static Dictionary<string, object> DescribeComObject(object value, int depth)
        {
            Dictionary<string, object> description = new Dictionary<string, object>
            {
                { "type", Com.Try(() => Information.TypeName(value)) }
            };

            IEnumerable<KeyValuePair<string, object>> identity = IdentifyingProperties
                .Select(name => new KeyValuePair<string, object>(name, Com.Try(() => ComReflection.Get(value, name, new object[0]))))
                .Where(pair => pair.Value is string || IsScalar(pair.Value));

            foreach (KeyValuePair<string, object> pair in identity)
            {
                description[pair.Key] = Format(pair.Value, 0);
            }

            if (depth > 0 && value is IEnumerable collection)
            {
                List<object> items = Com.Try(() => collection.Cast<object>().Take(MaxItems).ToList());
                if (items != null)
                {
                    description["items"] = items.Select(item => Format(item, depth - 1)).ToList();
                }
            }

            return description;
        }
    }
}
