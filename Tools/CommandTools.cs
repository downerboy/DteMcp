using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DteMcp.Mcp;
using DteMcp.Vs;

namespace DteMcp.Tools
{
    /// <summary>Run any Visual Studio command (menu items, Edit.*, Debug.*, View.*, Tools.*) and read the Output window.</summary>
    internal sealed class CommandTools : IToolProvider
    {
        private const int MaxCommands = 100;
        private const int DefaultOutputChars = 20000;

        private readonly DteSession session;

        public CommandTools(DteSession session)
        {
            this.session = session;
        }

        public IEnumerable<ToolDefinition> Tools()
        {
            yield return ToolDefinition.Mutating(
                "vs_execute_command",
                "Runs a Visual Studio command by its canonical name, e.g. File.SaveAll, Edit.FormatDocument, Build.BuildSolution, View.ErrorList, Debug.Restart, Edit.GoTo with args '42'. Find names with vs_find_commands.",
                ToolSchema.Object(
                    new[] { "command" },
                    ToolSchema.String("command", "Canonical command name."),
                    ToolSchema.String("args", "Optional command arguments.")),
                ExecuteCommand);

            yield return ToolDefinition.ReadOnly(
                "vs_find_commands",
                "Searches command names (case-insensitive substring). Returns up to 100 with availability in the current context.",
                ToolSchema.Object(new[] { "filter" }, ToolSchema.String("filter", "Text the command name must contain.")),
                args => FindCommands(args.RequireString("filter")));

            yield return ToolDefinition.ReadOnly(
                "vs_output",
                "Output window. Without 'pane' lists the panes; with 'pane' (e.g. Build, Debug) returns the end of that pane's text.",
                ToolSchema.Object(
                    ToolSchema.String("pane", "Pane name."),
                    ToolSchema.Integer("maxChars", "How many trailing characters to return. Default 20000.")),
                Output);
        }

        private object ExecuteCommand(ToolArguments args)
        {
            string command = args.RequireString("command");
            string commandArgs = args.GetString("args") ?? string.Empty;
            dynamic dte = session.Dte;

            Com.Do(() => dte.ExecuteCommand(command, commandArgs));
            return new Dictionary<string, object> { { "executed", command } };
        }

        private object FindCommands(string filter)
        {
            dynamic dte = session.Dte;
            IEnumerable<object> commands = Com.Run(() => ((IEnumerable)dte.Commands).Cast<object>().ToList());

            return commands
                .Select(command => new { Command = command, Name = Com.Try(() => (string)((dynamic)command).Name) })
                .Where(entry => !string.IsNullOrEmpty(entry.Name) && entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(MaxCommands)
                .Select(entry => new Dictionary<string, object>
                {
                    { "name", entry.Name },
                    { "available", Com.Try(() => (bool?)((dynamic)entry.Command).IsAvailable) }
                })
                .ToList();
        }

        private object Output(ToolArguments args)
        {
            dynamic dte = session.Dte;
            List<object> panes = Com.Run(() => ((IEnumerable)((dynamic)ToolWindows.OutputWindow(dte)).OutputWindowPanes).Cast<object>().ToList());
            List<string> names = panes.Select(PaneName).ToList();

            if (!args.Has("pane"))
            {
                return names;
            }

            string requested = args.RequireString("pane");
            object pane = panes.FirstOrDefault(candidate => string.Equals(PaneName(candidate), requested, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("No output pane named '" + requested + "'. Panes: " + string.Join(", ", names));

            dynamic typed = pane;
            string text = Com.Run(() => (string)typed.TextDocument.StartPoint.CreateEditPoint().GetText(typed.TextDocument.EndPoint)) ?? string.Empty;
            int maxChars = args.GetInt("maxChars", DefaultOutputChars);

            return new Dictionary<string, object>
            {
                { "pane", requested },
                { "truncated", text.Length > maxChars },
                { "text", text.Length > maxChars ? text.Substring(text.Length - maxChars) : text }
            };
        }

        private static string PaneName(object pane)
        {
            dynamic typed = pane;
            return Com.Try(() => (string)typed.Name);
        }
    }
}
