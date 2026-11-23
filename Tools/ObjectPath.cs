using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DteMcp.Tools
{
    internal sealed class PathSegment
    {
        public PathSegment(string name, bool hasArguments, IReadOnlyList<object> arguments)
        {
            Name = name;
            HasArguments = hasArguments;
            Arguments = arguments;
        }

        public string Name { get; }

        /// <summary>True when the segment was written with parentheses, even if empty: Item() vs Item.</summary>
        public bool HasArguments { get; }

        public IReadOnlyList<object> Arguments { get; }
    }

    /// <summary>
    /// Parses object-model paths such as Solution.Projects.Item(1).Name or Documents.Item("a.cs").Save().
    /// Arguments are JSON-style literals: "text", 42, 1.5, true, false, null.
    /// </summary>
    internal static class ObjectPath
    {
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        public static IReadOnlyList<PathSegment> Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new FormatException("Path is empty.");
            }

            return SplitOutsideBrackets(path, '.').Select(ParseSegment).ToList();
        }

        private static PathSegment ParseSegment(string text)
        {
            string segment = text.Trim();
            int open = segment.IndexOf('(');

            if (open < 0)
            {
                return new PathSegment(ValidName(segment), false, new object[0]);
            }

            if (!segment.EndsWith(")", StringComparison.Ordinal))
            {
                throw new FormatException("Expected ')' at the end of: " + segment);
            }

            string name = ValidName(segment.Substring(0, open));
            string inner = segment.Substring(open + 1, segment.Length - open - 2);
            List<object> arguments = string.IsNullOrWhiteSpace(inner)
                ? new List<object>()
                : SplitOutsideBrackets(inner, ',').Select(ParseLiteral).ToList();

            return new PathSegment(name, true, arguments);
        }

        private static string ValidName(string name)
        {
            string trimmed = name.Trim();
            return Identifier.IsMatch(trimmed) ? trimmed : throw new FormatException("Invalid member name: '" + trimmed + "'");
        }

        private static object ParseLiteral(string token)
        {
            string text = token.Trim();

            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            {
                return Unescape(text.Substring(1, text.Length - 2));
            }

            if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (text.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (text.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
            {
                return integer;
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long wide))
            {
                return wide;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                return number;
            }

            throw new FormatException("Unsupported argument (use \"text\", numbers, true, false, null): " + text);
        }

        private static string Unescape(string text)
        {
            StringBuilder result = new StringBuilder();
            bool escaped = false;

            foreach (char c in text)
            {
                if (!escaped && c == '\\')
                {
                    escaped = true;
                    continue;
                }

                result.Append(escaped ? Unescaped(c) : c);
                escaped = false;
            }

            return result.ToString();
        }

        private static char Unescaped(char c)
        {
            switch (c)
            {
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                default:
                    return c;
            }
        }

        private static List<string> SplitOutsideBrackets(string text, char separator)
        {
            List<string> parts = new List<string>();
            StringBuilder current = new StringBuilder();
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            foreach (char c in text)
            {
                if (inString)
                {
                    current.Append(c);
                    inString = !(c == '"' && !escaped);
                    escaped = !escaped && c == '\\';
                    continue;
                }

                depth += c == '(' ? 1 : c == ')' ? -1 : 0;
                if (depth < 0)
                {
                    throw new FormatException("Unbalanced ')' in: " + text);
                }

                if (c == separator && depth == 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                inString = c == '"';
                current.Append(c);
            }

            if (inString || depth != 0)
            {
                throw new FormatException("Unbalanced quotes or parentheses in: " + text);
            }

            parts.Add(current.ToString());
            return parts;
        }
    }
}
