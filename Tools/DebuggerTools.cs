using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DteMcp.Mcp;
using DteMcp.Vs;

namespace DteMcp.Tools
{
    /// <summary>Inspect and control the debugger of the selected Visual Studio instance.</summary>
    internal sealed class DebuggerTools : IToolProvider
    {
        private const int MaxStackFrames = 50;
        private const int MaxLocals = 100;
        private const int MaxBreakpoints = 100;
        private const int MaxValueLength = 500;
        private const int DefaultExpressionTimeoutMs = 2000;
        private const int ConditionWhenTrue = 1;

        private readonly DteSession session;

        public DebuggerTools(DteSession session)
        {
            this.session = session;
        }

        public IEnumerable<ToolDefinition> Tools()
        {
            SchemaProperty frameIndex = ToolSchema.Integer("frameIndex", "Zero-based stack frame index. Default 0 (top frame).");
            SchemaProperty breakpointIndex = ToolSchema.Integer("index", "Zero-based index as listed by debugger_breakpoints.");

            yield return ToolDefinition.ReadOnly("debugger_status", "Lists running Visual Studio instances and the selected instance's debugger mode, solution, and current location.", ToolSchema.Object(), _ => Status());
            yield return ToolDefinition.ReadOnly("debugger_call_stack", "Call stack of the current thread (debugger must be in break mode).", ToolSchema.Object(), _ => CallStack());
            yield return ToolDefinition.ReadOnly("debugger_locals", "Locals of a stack frame, values shown as the debugger displays them, not expanded.", ToolSchema.Object(frameIndex), args => Locals(args.GetInt("frameIndex", 0)));
            yield return ToolDefinition.ReadOnly("debugger_exception", "The current exception ($exception): type, message, stack trace. Reads Message and StackTrace via property evaluation in the debuggee.", ToolSchema.Object(), _ => CurrentException());
            yield return ToolDefinition.ReadOnly("debugger_breakpoints", "Lists breakpoints with index, file, line, condition, enabled state, and hit count.", ToolSchema.Object(), _ => Breakpoints());

            yield return ToolDefinition.Mutating(
                "debugger_control",
                "Drives the debugger. start/continue = F5, start_without_debugging = Ctrl+F5, break, stop, restart, step_into/step_over/step_out (wait until the step ends), run_to_cursor. Returns the new status.",
                ToolSchema.Object(
                    new[] { "action" },
                    ToolSchema.String("action", "What to do.", "start", "continue", "start_without_debugging", "break", "stop", "restart", "step_into", "step_over", "step_out", "run_to_cursor")),
                args => Control(args.RequireString("action")));

            yield return ToolDefinition.Mutating(
                "debugger_evaluate",
                "Evaluates an expression in break mode, like the Immediate window. Side effects and assignments (x = 5) run in the debuggee.",
                ToolSchema.Object(
                    new[] { "expression" },
                    ToolSchema.String("expression", "Expression to evaluate."),
                    frameIndex,
                    ToolSchema.Integer("timeoutMs", "Evaluation timeout. Default 2000.")),
                Evaluate);

            yield return ToolDefinition.Mutating(
                "debugger_breakpoint_add",
                "Adds a line breakpoint, optionally conditional. Returns the breakpoint list.",
                ToolSchema.Object(
                    new[] { "file", "line" },
                    ToolSchema.String("file", "Full path of the source file."),
                    ToolSchema.Integer("line", "One-based line number."),
                    ToolSchema.String("condition", "Optional condition; the breakpoint hits when it is true.")),
                AddBreakpoint);

            yield return ToolDefinition.Mutating(
                "debugger_breakpoint_remove",
                "Removes one breakpoint by index, or all of them with all=true. Returns the remaining breakpoints.",
                ToolSchema.Object(breakpointIndex, ToolSchema.Boolean("all", "Remove every breakpoint.")),
                RemoveBreakpoints);

            yield return ToolDefinition.Mutating(
                "debugger_breakpoint_enable",
                "Enables or disables a breakpoint by index. Returns the breakpoint list.",
                ToolSchema.Object(new[] { "index", "enabled" }, breakpointIndex, ToolSchema.Boolean("enabled", "True to enable, false to disable.")),
                SetBreakpointEnabled);
        }

        // ---------------------------------------------------------------- Inspect

        private object Status()
        {
            List<DteInstance> instances = session.Instances();
            DteInstance selected = session.Select(instances);
            dynamic debugger = session.Debugger;
            int mode = Com.Run(() => (int)debugger.CurrentMode);

            Dictionary<string, object> status = new Dictionary<string, object>
            {
                { "instances", instances.Select(Describe).ToList() },
                { "selected", selected.Name },
                { "mode", DebuggerModes.Name(mode) }
            };

            if (mode == DebuggerModes.Break)
            {
                dynamic frame = Com.Run(() => debugger.CurrentStackFrame);
                status["currentFrame"] = DescribeFrame(frame, 0);
            }

            return status;
        }

        private object CallStack()
        {
            object debugger = RequireBreakMode();
            return EnumerateFrames(debugger).Take(MaxStackFrames).Select((frame, index) => DescribeFrame(frame, index)).ToList();
        }

        private object Locals(int frameIndex)
        {
            object debugger = RequireBreakMode();
            object frame = FrameAt(debugger, frameIndex);

            dynamic locals = Com.Run(() => ((dynamic)frame).Locals);
            List<object> items = Com.Run(() => ((IEnumerable)locals).Cast<object>().Take(MaxLocals).ToList());

            return items.Select(DescribeExpression).ToList();
        }

        private object CurrentException()
        {
            object debuggerObject = RequireBreakMode();
            dynamic debugger = debuggerObject;
            dynamic exception = Com.Run(() => debugger.GetExpression("$exception", false, DefaultExpressionTimeoutMs));

            return !Com.Run(() => (bool)exception.IsValidValue)
                ? new Dictionary<string, object> { { "hasException", false } }
                : new Dictionary<string, object>
                {
                    { "hasException", true },
                    { "type", Trim(Com.Try(() => (string)exception.Type)) },
                    { "message", EvaluateText(debuggerObject, "$exception.Message") },
                    { "stackTrace", EvaluateText(debuggerObject, "$exception.StackTrace") }
                };
        }

        private object Breakpoints()
        {
            return BreakpointItems().Select((item, index) => DescribeBreakpoint(item, index)).ToList();
        }

        // ---------------------------------------------------------------- Control

        private object Control(string action)
        {
            dynamic dte = session.Dte;
            dynamic debugger = Com.Run(() => dte.Debugger);

            switch (action)
            {
                case "start":
                case "continue":
                    Com.Do(() => debugger.Go(false));
                    break;
                case "start_without_debugging":
                    Com.Do(() => dte.ExecuteCommand("Debug.StartWithoutDebugging"));
                    break;
                case "break":
                    Com.Do(() => debugger.Break(false));
                    break;
                case "stop":
                    Com.Do(() => debugger.Stop(false));
                    break;
                case "restart":
                    Com.Do(() => dte.ExecuteCommand("Debug.Restart"));
                    break;
                case "step_into":
                    Com.Do(() => debugger.StepInto(true));
                    break;
                case "step_over":
                    Com.Do(() => debugger.StepOver(true));
                    break;
                case "step_out":
                    Com.Do(() => debugger.StepOut(true));
                    break;
                case "run_to_cursor":
                    Com.Do(() => debugger.RunToCursor(true));
                    break;
                default:
                    throw new ArgumentException("Unknown debugger action: " + action);
            }

            return Status();
        }

        private object Evaluate(ToolArguments args)
        {
            string expressionText = args.RequireString("expression");
            int timeout = args.GetInt("timeoutMs", DefaultExpressionTimeoutMs);
            object debuggerObject = RequireBreakMode();
            dynamic debugger = debuggerObject;

            if (args.Has("frameIndex"))
            {
                object frame = FrameAt(debuggerObject, args.GetInt("frameIndex", 0));
                Com.Do(() => debugger.CurrentStackFrame = frame);
            }

            dynamic result = Com.Run(() => debugger.GetExpression(expressionText, false, timeout));

            return new Dictionary<string, object>
            {
                { "expression", expressionText },
                { "isValid", Com.Try(() => (bool?)result.IsValidValue) ?? false },
                { "type", Com.Try(() => (string)result.Type) },
                { "value", Trim(Com.Try(() => (string)result.Value)) }
            };
        }

        private object AddBreakpoint(ToolArguments args)
        {
            string file = args.RequireString("file");
            int line = args.RequireInt("line");
            string condition = args.GetString("condition");
            dynamic breakpoints = Com.Run(() => session.Debugger.Breakpoints);

            if (string.IsNullOrEmpty(condition))
            {
                Com.Do(() => breakpoints.Add(File: file, Line: line));
            }
            else
            {
                Com.Do(() => breakpoints.Add(File: file, Line: line, Condition: condition, ConditionType: ConditionWhenTrue));
            }

            return Breakpoints();
        }

        private object RemoveBreakpoints(ToolArguments args)
        {
            if (args.GetBool("all", false))
            {
                dynamic debugger = session.Debugger;
                Com.Do(() => debugger.DeleteAllBreakpoints());
                return Breakpoints();
            }

            dynamic breakpoint = BreakpointAt(args.RequireInt("index"));
            Com.Do(() => breakpoint.Delete());
            return Breakpoints();
        }

        private object SetBreakpointEnabled(ToolArguments args)
        {
            dynamic breakpoint = BreakpointAt(args.RequireInt("index"));
            bool enabled = args.GetBool("enabled", true);
            Com.Do(() => breakpoint.Enabled = enabled);
            return Breakpoints();
        }

        // ---------------------------------------------------------------- Helpers

        private List<object> BreakpointItems()
        {
            dynamic breakpoints = Com.Run(() => session.Debugger.Breakpoints);
            return Com.Run(() => ((IEnumerable)breakpoints).Cast<object>().Take(MaxBreakpoints).ToList());
        }

        private object BreakpointAt(int index)
        {
            return BreakpointItems().Skip(index).FirstOrDefault() ?? throw new ArgumentException("No breakpoint at index " + index);
        }

        private object RequireBreakMode()
        {
            dynamic debugger = session.Debugger;
            int mode = Com.Run(() => (int)debugger.CurrentMode);

            return mode != DebuggerModes.Break
                ? throw new InvalidOperationException("Debugger is in " + DebuggerModes.Name(mode) + " mode; break mode is required.")
                : (object)debugger;
        }

        private static IEnumerable<object> EnumerateFrames(object debuggerObject)
        {
            dynamic debugger = debuggerObject;
            dynamic thread = Com.Run(() => debugger.CurrentThread);
            dynamic frames = Com.Run(() => thread.StackFrames);
            return Com.Run(() => ((IEnumerable)frames).Cast<object>().ToList());
        }

        private static object FrameAt(object debugger, int frameIndex)
        {
            return EnumerateFrames(debugger).Skip(frameIndex).FirstOrDefault() ?? throw new InvalidOperationException("No stack frame at index " + frameIndex);
        }

        private static Dictionary<string, object> Describe(DteInstance instance)
        {
            dynamic dte = instance.Dte;
            dynamic debugger = Com.Try(() => (object)dte.Debugger);

            return new Dictionary<string, object>
            {
                { "name", instance.Name },
                { "processId", instance.ProcessId },
                { "solution", Com.Try(() => (string)dte.Solution.FullName) },
                { "mode", DebuggerModes.Name(Com.Try(() => (int?)debugger.CurrentMode) ?? 0) }
            };
        }

        private static Dictionary<string, object> DescribeFrame(dynamic frame, int index)
        {
            return new Dictionary<string, object>
            {
                { "index", index },
                { "function", Com.Try(() => (string)frame.FunctionName) },
                { "module", Com.Try(() => (string)frame.Module) },
                { "file", Com.Try(() => (string)frame.FileName) },
                { "line", Com.Try(() => (int?)frame.LineNumber) }
            };
        }

        private static Dictionary<string, object> DescribeExpression(object item)
        {
            dynamic expression = item;
            return new Dictionary<string, object>
            {
                { "name", Com.Try(() => (string)expression.Name) },
                { "type", Com.Try(() => (string)expression.Type) },
                { "value", Trim(Com.Try(() => (string)expression.Value)) }
            };
        }

        private static Dictionary<string, object> DescribeBreakpoint(object item, int index)
        {
            dynamic breakpoint = item;
            return new Dictionary<string, object>
            {
                { "index", index },
                { "file", Com.Try(() => (string)breakpoint.File) },
                { "line", Com.Try(() => (int?)breakpoint.FileLine) },
                { "function", Com.Try(() => (string)breakpoint.FunctionName) },
                { "enabled", Com.Try(() => (bool?)breakpoint.Enabled) },
                { "condition", Com.Try(() => (string)breakpoint.Condition) },
                { "hitCount", Com.Try(() => (int?)breakpoint.CurrentHits) }
            };
        }

        private static string EvaluateText(object debuggerObject, string expressionText)
        {
            dynamic debugger = debuggerObject;
            dynamic expression = Com.Run(() => debugger.GetExpression(expressionText, false, DefaultExpressionTimeoutMs));
            bool valid = Com.Try(() => (bool?)expression.IsValidValue) ?? false;
            return valid ? Trim(Com.Try(() => (string)expression.Value)) : null;
        }

        private static string Trim(string value)
        {
            return value != null && value.Length > MaxValueLength ? value.Substring(0, MaxValueLength) + "...(truncated)" : value;
        }
    }
}
