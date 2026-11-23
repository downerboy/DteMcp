using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DteMcp.Mcp;
using DteMcp.Vs;

namespace DteMcp.Tools
{
    /// <summary>Solution, project, project-item and project-property access.</summary>
    internal sealed class SolutionTools : IToolProvider
    {
        private const string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";
        private const int MaxItems = 500;
        private const int MaxSearchedItems = 20000;
        private const int MaxProperties = 300;

        private readonly DteSession session;

        public SolutionTools(DteSession session)
        {
            this.session = session;
        }

        private sealed class ProjectEntry
        {
            public string Name { get; set; }

            public string FullPath { get; set; }

            public string TreePath { get; set; }

            public object Item { get; set; }
        }

        public IEnumerable<ToolDefinition> Tools()
        {
            SchemaProperty project = ToolSchema.String("project", "Project name or unique name, as listed by vs_solution.");

            yield return ToolDefinition.ReadOnly("vs_solution", "Open solution: path, projects (including solution folders), startup projects, active configuration.", ToolSchema.Object(), _ => Solution());

            yield return ToolDefinition.ReadOnly(
                "vs_project_items",
                "Lists the items (files, folders) of a project as a tree path with full path. Capped at 500 items.",
                ToolSchema.Object(new[] { "project" }, project),
                ProjectItems);

            yield return ToolDefinition.ReadOnly(
                "vs_project_properties",
                "Reads project properties (TargetFramework, OutputPath, ...). Pass names to read specific ones, otherwise the first 300 are listed.",
                ToolSchema.Object(new[] { "project" }, project, ToolSchema.StringList("names", "Property names to read.")),
                ProjectProperties);

            yield return ToolDefinition.Mutating(
                "vs_solution_open",
                "Opens a solution or project file (.sln, .slnx, .csproj).",
                ToolSchema.Object(new[] { "path" }, ToolSchema.String("path", "Full path of the solution file.")),
                args => OpenSolution(args.RequireString("path")));

            yield return ToolDefinition.Mutating(
                "vs_solution_close",
                "Closes the solution. Saves it first unless save=false.",
                ToolSchema.Object(ToolSchema.Boolean("save", "Save before closing. Default true.")),
                args => CloseSolution(args.GetBool("save", true)));

            yield return ToolDefinition.Mutating(
                "vs_project_item_add",
                "Adds an existing file to a project (needed for old-style projects; SDK-style projects include files automatically).",
                ToolSchema.Object(new[] { "project", "path" }, project, ToolSchema.String("path", "Full path of the file to add.")),
                AddItem);

            yield return ToolDefinition.Mutating(
                "vs_project_item_remove",
                "Removes an item from a project. The file stays on disk unless deleteFromDisk=true, which deletes it permanently.",
                ToolSchema.Object(
                    new[] { "project", "path" },
                    project,
                    ToolSchema.String("path", "Full path of the item, as listed by vs_project_items."),
                    ToolSchema.Boolean("deleteFromDisk", "Also delete the file from disk. Default false.")),
                RemoveItem);

            yield return ToolDefinition.Mutating(
                "vs_project_property_set",
                "Sets a project property. Call vs_save afterwards to write the project file.",
                ToolSchema.Object(new[] { "project", "name", "value" }, project, ToolSchema.String("name", "Property name."), ToolSchema.Value("value", "New value.")),
                SetProperty);
        }

        // ---------------------------------------------------------------- Tools

        private object Solution()
        {
            dynamic dte = session.Dte;
            dynamic solution = Com.Run(() => dte.Solution);

            if (!(Com.Try(() => (bool?)solution.IsOpen) ?? false))
            {
                return new Dictionary<string, object> { { "isOpen", false } };
            }

            dynamic build = Com.Run(() => solution.SolutionBuild);
            dynamic configuration = Com.Try(() => (object)build.ActiveConfiguration);

            return new Dictionary<string, object>
            {
                { "isOpen", true },
                { "path", Com.Try(() => (string)solution.FullName) },
                { "projects", AllProjects().Select(DescribeProject).ToList() },
                { "startupProjects", Com.Try(() => ((Array)build.StartupProjects).Cast<object>().Select(Convert.ToString).ToList()) },
                { "activeConfiguration", Com.Try(() => (string)configuration.Name) },
                { "activePlatform", Com.Try(() => (string)configuration.PlatformName) }
            };
        }

        private object ProjectItems(ToolArguments args)
        {
            object project = FindProject(args.RequireString("project"));
            List<ProjectEntry> entries = ItemsOf(project, MaxItems + 1);

            return new Dictionary<string, object>
            {
                { "truncated", entries.Count > MaxItems },
                { "items", entries.Take(MaxItems).Select(entry => new Dictionary<string, object> { { "path", entry.TreePath }, { "fullPath", entry.FullPath } }).ToList() }
            };
        }

        private object ProjectProperties(ToolArguments args)
        {
            dynamic properties = Com.Run(() => ((dynamic)FindProject(args.RequireString("project"))).Properties);
            IReadOnlyList<string> names = args.GetStringList("names");

            List<object> selected = names.Count > 0
                ? names.Select(name => Com.Try(() => (object)properties.Item(name))).Where(property => property != null).ToList()
                : Com.Run(() => ((IEnumerable)properties).Cast<object>().Take(MaxProperties).ToList());

            return selected.Select(property =>
            {
                dynamic typed = property;
                return new Dictionary<string, object>
                {
                    { "name", Com.Try(() => (string)typed.Name) },
                    { "value", ObjectFormatter.Format(Com.Try(() => (object)typed.Value), 0) }
                };
            }).ToList();
        }

        private object OpenSolution(string path)
        {
            dynamic dte = session.Dte;
            Com.Do(() => dte.Solution.Open(path));
            return Solution();
        }

        private object CloseSolution(bool save)
        {
            dynamic dte = session.Dte;
            Com.Do(() => dte.Solution.Close(save));
            return Solution();
        }

        private object AddItem(ToolArguments args)
        {
            dynamic project = FindProject(args.RequireString("project"));
            string path = args.RequireString("path");
            Com.Do(() => project.ProjectItems.AddFromFile(path));
            return new Dictionary<string, object> { { "added", path } };
        }

        private object RemoveItem(ToolArguments args)
        {
            object project = FindProject(args.RequireString("project"));
            string path = args.RequireString("path");

            ProjectEntry entry = ItemsOf(project, MaxSearchedItems)
                .FirstOrDefault(candidate => string.Equals(candidate.FullPath, path, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.TreePath, path, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Item not found in project: " + path);

            dynamic item = entry.Item;
            bool deleteFromDisk = args.GetBool("deleteFromDisk", false);
            Com.Do(() =>
            {
                if (deleteFromDisk)
                {
                    item.Delete();
                }
                else
                {
                    item.Remove();
                }
            });

            return new Dictionary<string, object> { { "removed", path }, { "deletedFromDisk", deleteFromDisk } };
        }

        private object SetProperty(ToolArguments args)
        {
            dynamic project = FindProject(args.RequireString("project"));
            string name = args.RequireString("name");
            object value = args.GetValue("value") ?? throw new ArgumentException("Missing required argument: value");

            dynamic property = Com.Run(() => project.Properties.Item(name));
            Com.Do(() => property.Value = value);

            return new Dictionary<string, object> { { "name", name }, { "value", ObjectFormatter.Format(Com.Try(() => (object)property.Value), 0) } };
        }

        // ---------------------------------------------------------------- Helpers

        private IEnumerable<object> AllProjects()
        {
            dynamic dte = session.Dte;
            List<object> topLevel = Com.Run(() => ((IEnumerable)dte.Solution.Projects).Cast<object>().ToList());
            return topLevel.SelectMany(WithNestedProjects).ToList();
        }

        private static IEnumerable<object> WithNestedProjects(object project)
        {
            yield return project;

            dynamic typed = project;
            if (Com.Try(() => (string)typed.Kind) != SolutionFolderKind)
            {
                yield break;
            }

            List<object> children = Com.Try(() => ((IEnumerable)typed.ProjectItems).Cast<object>().Select(item => (object)((dynamic)item).SubProject).Where(sub => sub != null).ToList()) ?? new List<object>();
            foreach (object nested in children.SelectMany(WithNestedProjects))
            {
                yield return nested;
            }
        }

        private object FindProject(string nameOrUniqueName)
        {
            return AllProjects().FirstOrDefault(project =>
            {
                dynamic typed = project;
                return string.Equals(Com.Try(() => (string)typed.UniqueName), nameOrUniqueName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Com.Try(() => (string)typed.Name), nameOrUniqueName, StringComparison.OrdinalIgnoreCase);
            }) ?? throw new InvalidOperationException("Project not found: " + nameOrUniqueName);
        }

        private static Dictionary<string, object> DescribeProject(object project)
        {
            dynamic typed = project;
            string kind = Com.Try(() => (string)typed.Kind);

            return new Dictionary<string, object>
            {
                { "name", Com.Try(() => (string)typed.Name) },
                { "uniqueName", Com.Try(() => (string)typed.UniqueName) },
                { "fullName", Com.Try(() => (string)typed.FullName) },
                { "kind", kind },
                { "isSolutionFolder", kind == SolutionFolderKind }
            };
        }

        private static List<ProjectEntry> ItemsOf(object project, int limit)
        {
            dynamic typed = project;
            object rootItems = Com.Try(() => (object)typed.ProjectItems);
            return rootItems == null
                ? new List<ProjectEntry>()
                : Walk(rootItems, string.Empty).Take(limit).ToList();
        }

        private static IEnumerable<ProjectEntry> Walk(object projectItems, string parentPath)
        {
            List<object> items = Com.Try(() => ((IEnumerable)projectItems).Cast<object>().ToList()) ?? new List<object>();

            foreach (object item in items)
            {
                dynamic typed = item;
                string name = Com.Try(() => (string)typed.Name) ?? string.Empty;
                string treePath = parentPath.Length == 0 ? name : parentPath + "/" + name;

                yield return new ProjectEntry
                {
                    Name = name,
                    TreePath = treePath,
                    FullPath = Com.Try(() => (string)typed.Properties.Item("FullPath").Value),
                    Item = item
                };

                object children = Com.Try(() => (object)typed.ProjectItems);
                if (children == null)
                {
                    continue;
                }

                foreach (ProjectEntry child in Walk(children, treePath))
                {
                    yield return child;
                }
            }
        }
    }
}
