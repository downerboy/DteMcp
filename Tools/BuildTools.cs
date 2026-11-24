using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DteMcp.Mcp;
using DteMcp.Vs;

namespace DteMcp.Tools
{
    /// <summary>Build, clean, and read build state and the Error List.</summary>
    internal sealed class BuildTools : IToolProvider
    {
        private const int MaxErrorItemsScanned = 2000;
        private const int DefaultMaxErrors = 50;

        private readonly DteSession session;

        public BuildTools(DteSession session)
        {
            this.session = session;
        }

        public IEnumerable<ToolDefinition> Tools()
        {
            yield return ToolDefinition.Mutating(
                "vs_build",
                "Builds, rebuilds, cleans or cancels. 'project' (unique name) only applies to action=build. With wait=false returns at once; poll vs_build_status. Returns state and the first errors.",
                ToolSchema.Object(
                    ToolSchema.String("action", "Default build.", "build", "rebuild", "clean", "cancel"),
                    ToolSchema.String("configuration", "Solution configuration to activate first, e.g. Debug or Release."),
                    ToolSchema.String("platform", "Platform of that configuration, e.g. Any CPU."),
                    ToolSchema.String("project", "Unique name of a single project to build."),
                    ToolSchema.Boolean("wait", "Wait for the build to finish. Default true.")),
                Build);

            yield return ToolDefinition.ReadOnly("vs_build_status", "Build state (notStarted, inProgress, done), failed project count, and the first errors.", ToolSchema.Object(), _ => BuildResult());

            yield return ToolDefinition.ReadOnly(
                "vs_errors",
                "Error List items: severity, description, file, line, column, project.",
                ToolSchema.Object(
                    ToolSchema.String("severity", "Only this severity. Default all.", "error", "warning", "message"),
                    ToolSchema.Integer("max", "Maximum items to return. Default 50.")),
                args => ErrorList(args.GetString("severity"), args.GetInt("max", DefaultMaxErrors)));
        }

        private object Build(ToolArguments args)
        {
            string action = args.GetString("action") ?? "build";
            string project = args.GetString("project");
            bool wait = args.GetBool("wait", true);

            if (project != null && action != "build")
            {
                throw new ArgumentException("'project' only applies to action=build.");
            }

            dynamic dte = session.Dte;
            dynamic build = Com.Run(() => dte.Solution.SolutionBuild);

            if (action == "cancel")
            {
                Com.Do(() => dte.ExecuteCommand("Build.Cancel"));
                return BuildResult();
            }

            ActivateConfiguration(build, args.GetString("configuration"), args.GetString("platform"));

            if (action == "clean" || action == "rebuild")
            {
                Com.Do(() => build.Clean(true));
            }

            if (action == "build" || action == "rebuild")
            {
                StartBuild(build, project, wait);
            }

            return BuildResult();
        }

        private static void StartBuild(dynamic build, string project, bool wait)
        {
            if (project == null)
            {
                Com.Do(() => build.Build(wait));
                return;
            }

            string configurationName = Com.Run(() => (string)build.ActiveConfiguration.Name);
            Com.Do(() => build.BuildProject(configurationName, project, wait));
        }

        private static void ActivateConfiguration(dynamic build, string name, string platform)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            dynamic match = Com.Run(() => ((IEnumerable)build.SolutionConfigurations).Cast<object>().ToList())
                .FirstOrDefault(configuration => IsConfiguration(configuration, name, platform))
                ?? throw new InvalidOperationException("No solution configuration named " + name + (platform == null ? string.Empty : "|" + platform));

            Com.Do(() => match.Activate());
        }

        private static bool IsConfiguration(object configuration, string name, string platform)
        {
            dynamic typed = configuration;
            bool sameName = string.Equals(Com.Try(() => (string)typed.Name), name, StringComparison.OrdinalIgnoreCase);
            bool samePlatform = platform == null || string.Equals(Com.Try(() => (string)typed.PlatformName), platform, StringComparison.OrdinalIgnoreCase);
            return sameName && samePlatform;
        }

        private object BuildResult()
        {
            dynamic dte = session.Dte;
            dynamic build = Com.Run(() => dte.Solution.SolutionBuild);

            return new Dictionary<string, object>
            {
                { "state", BuildStateName(Com.Try(() => (int?)build.BuildState) ?? 0) },
                { "failedProjects", Com.Try(() => (int?)build.LastBuildInfo) },
                { "errors", ErrorList("error", DefaultMaxErrors) }
            };
        }

        private object ErrorList(string severity, int max)
        {
            dynamic dte = session.Dte;
            ToolWindows.IErrorItems items = Com.Run(() => ToolWindows.ErrorItems(dte));
            int count = Math.Min(Com.Run(() => items.GetCount()), MaxErrorItemsScanned);

            List<Dictionary<string, object>> matching = Enumerable.Range(1, count)
                .Select(index => DescribeError(Com.Run(() => (ToolWindows.IErrorItem)items.GetItem(index))))
                .Where(error => severity == null || (string)error["severity"] == severity)
                .Take(max)
                .ToList();

            return new Dictionary<string, object> { { "returned", matching.Count }, { "items", matching } };
        }

        private static Dictionary<string, object> DescribeError(ToolWindows.IErrorItem item)
        {
            return new Dictionary<string, object>
            {
                { "severity", SeverityName(Com.Try(() => (int?)item.GetErrorLevel()) ?? 0) },
                { "description", Com.Try(() => item.GetDescription()) },
                { "file", Com.Try(() => item.GetFileName()) },
                { "line", Com.Try(() => (int?)item.GetLine()) },
                { "column", Com.Try(() => (int?)item.GetColumn()) },
                { "project", Com.Try(() => item.GetProject()) }
            };
        }

        private static string SeverityName(int level)
        {
            switch (level)
            {
                case 4:
                    return "error";
                case 2:
                    return "warning";
                default:
                    return "message";
            }
        }

        private static string BuildStateName(int state)
        {
            switch (state)
            {
                case 1:
                    return "notStarted";
                case 2:
                    return "inProgress";
                case 3:
                    return "done";
                default:
                    return "unknown(" + state + ")";
            }
        }
    }
}
