using System;
using System.Collections.Generic;
using System.Linq;
using DteMcp.Mcp;
using DteMcp.Vs;

namespace DteMcp.Tools
{
    /// <summary>Generic access to the whole EnvDTE object model, for everything the dedicated tools do not cover.</summary>
    internal sealed class ObjectModelTools : IToolProvider
    {
        private const int DefaultDepth = 1;
        private const int MaxDepth = 3;

        private readonly DteSession session;

        public ObjectModelTools(DteSession session)
        {
            this.session = session;
        }

        public IEnumerable<ToolDefinition> Tools()
        {
            yield return ToolDefinition.Mutating(
                "vs_invoke",
                "Reads, sets or calls anything in the EnvDTE object model, starting at DTE. Path members are separated by dots; method calls and indexers take literals: Solution.Projects.Item(1).Name, Documents.Item(\"C:\\\\a.cs\").Save(), Windows.Count, ActiveWindow.Caption. operation=get (default) reads a property or calls a method; operation=set assigns 'value' to the last member. COM objects are returned as type plus Name/FullName/Kind/Count and, with depth>0, their first 50 items.",
                ToolSchema.Object(
                    new[] { "path" },
                    ToolSchema.String("path", "Member path from DTE."),
                    ToolSchema.String("operation", "get (default) or set.", "get", "set"),
                    ToolSchema.Value("value", "Value to assign when operation=set."),
                    ToolSchema.Integer("depth", "How deep to expand returned collections, 0-3. Default 1.")),
                Invoke);
        }

        private object Invoke(ToolArguments args)
        {
            string operation = args.GetString("operation") ?? "get";
            int depth = Math.Max(0, Math.Min(MaxDepth, args.GetInt("depth", DefaultDepth)));
            List<PathSegment> segments = WithoutLeadingDte(ObjectPath.Parse(args.RequireString("path")));

            object target = session.Dte;
            if (segments.Count == 0)
            {
                return ObjectFormatter.Format(target, depth);
            }

            PathSegment last = segments.Last();
            object owner = segments.Take(segments.Count - 1).Aggregate(target, (current, segment) => Get(current, segment));

            if (operation == "set")
            {
                object value = args.Has("value") ? args.GetValue("value") : throw new ArgumentException("Missing required argument: value");
                ComReflection.Set(owner, last.Name, last.Arguments.ToArray(), value);
                return new Dictionary<string, object> { { "set", args.GetString("path") } };
            }

            if (operation != "get")
            {
                throw new ArgumentException("Unknown operation: " + operation);
            }

            return ObjectFormatter.Format(Get(owner, last), depth);
        }

        private static object Get(object target, PathSegment segment)
        {
            return ComReflection.Get(target, segment.Name, segment.Arguments.ToArray());
        }

        private static List<PathSegment> WithoutLeadingDte(IReadOnlyList<PathSegment> segments)
        {
            bool startsWithDte = segments.Count > 0 && !segments[0].HasArguments && string.Equals(segments[0].Name, "DTE", StringComparison.OrdinalIgnoreCase);
            return (startsWithDte ? segments.Skip(1) : segments).ToList();
        }
    }
}
