using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DteMcp
{
    /// <summary>
    /// Minimal, read-only MCP server (stdio, newline-delimited JSON-RPC) that attaches to a running
    /// Visual Studio 2026 instance through the COM Running Object Table and reads debugger state.
    /// No VSIX, no network listener, no files written, no third-party packages.
    /// </summary>
    internal static class Program
    {
        private const string DteProgIdPrefix = "!VisualStudio.DTE.18.0:";
        private const string PidEnvironmentVariable = "DTEMCP_PID";
        private const int MaxStackFrames = 50;
        private const int MaxLocals = 100;
        private const int MaxBreakpoints = 100;
        private const int MaxValueLength = 500;
        private const int ExpressionTimeoutMs = 2000;
        private const int ComRetryCount = 20;
        private const int ComRetryDelayMs = 250;

        private const int DesignMode = 1;
        private const int BreakMode = 2;
        private const int RunMode = 3;

        private static readonly int RpcCallRejected = unchecked((int)0x80010001);
        private static readonly int RpcRetryLater = unchecked((int)0x8001010A);

        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable runningObjectTable);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx bindContext);

        // STA keeps COM calls into devenv on the apartment model it expects.
        [STAThread]
        private static int Main(string[] args)
        {
            Stream output = Console.OpenStandardOutput();

            using (StreamReader reader = new StreamReader(Console.OpenStandardInput(), Utf8))
            {
                string line = reader.ReadLine();
                while (line != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        HandleLine(line, output);
                    }

                    line = reader.ReadLine();
                }
            }

            return 0;
        }

        // ---------------------------------------------------------------- JSON-RPC

        private static void HandleLine(string line, Stream output)
        {
            object id = null;
            bool hasId = false;

            try
            {
                Dictionary<string, object> request = Serializer.Deserialize<Dictionary<string, object>>(line);
                hasId = request.ContainsKey("id");
                id = hasId ? request["id"] : null;

                if (!hasId)
                {
                    return; // notifications (e.g. notifications/initialized) need no reply
                }

                string method = GetString(request, "method");
                Dictionary<string, object> parameters = GetDictionary(request, "params");

                object result = Dispatch(method, parameters);
                Write(output, new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "result", result } });
            }
            catch (Exception ex)
            {
                if (hasId)
                {
                    Write(output, ErrorResponse(id, -32603, ex.Message));
                }
            }
        }

        private static object Dispatch(string method, Dictionary<string, object> parameters)
        {
            switch (method)
            {
                case "initialize":
                    return new Dictionary<string, object>
                    {
                        { "protocolVersion", GetString(parameters, "protocolVersion") ?? "2025-06-18" },
                        { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object>() } } },
                        { "serverInfo", new Dictionary<string, object> { { "name", "dtemcp" }, { "version", "0.1.0" } } }
                    };
                case "ping":
                    return new Dictionary<string, object>();
                case "tools/list":
                    return new Dictionary<string, object> { { "tools", ToolDefinitions() } };
                case "tools/call":
                    return CallTool(parameters);
                default:
                    throw new InvalidOperationException("Method not supported: " + method);
            }
        }

        private static Dictionary<string, object> ErrorResponse(object id, int code, string message)
        {
            return new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" },
                { "id", id },
                { "error", new Dictionary<string, object> { { "code", code }, { "message", message } } }
            };
        }

        private static void Write(Stream output, object message)
        {
            byte[] bytes = Utf8.GetBytes(Serializer.Serialize(message) + "\n");
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            return source == null || !source.ContainsKey(key) ? null : source[key] as string;
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            return source == null || !source.ContainsKey(key) ? null : source[key] as Dictionary<string, object>;
        }

        private static int GetInt(Dictionary<string, object> source, string key, int fallback)
        {
            return source == null || !source.ContainsKey(key) || source[key] == null ? fallback : Convert.ToInt32(source[key]);
        }

        // ---------------------------------------------------------------- Tools

        private static object[] ToolDefinitions()
        {
            return new object[]
            {
                Tool("debugger_status", "Read-only. Lists running Visual Studio instances and the selected instance's debugger mode, solution, and current location.", NoArguments()),
                Tool("debugger_call_stack", "Read-only. Call stack of the current thread (debugger must be in break mode).", NoArguments()),
                Tool("debugger_locals", "Read-only. Locals of a stack frame, values shown as the debugger displays them, not expanded.", FrameArguments()),
                Tool("debugger_exception", "Read-only. The current exception ($exception): type, message, stack trace. Reads Message and StackTrace via property evaluation in the debuggee.", NoArguments()),
                Tool("debugger_breakpoints", "Read-only. Lists breakpoints with file, line, condition, enabled state, and hit count.", NoArguments())
            };
        }

        private static Dictionary<string, object> Tool(string name, string description, Dictionary<string, object> schema)
        {
            return new Dictionary<string, object> { { "name", name }, { "description", description }, { "inputSchema", schema } };
        }

        private static Dictionary<string, object> NoArguments()
        {
            return new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() } };
        }

        private static Dictionary<string, object> FrameArguments()
        {
            Dictionary<string, object> frameIndex = new Dictionary<string, object>
            {
                { "type", "integer" },
                { "description", "Zero-based stack frame index. Default 0 (top frame)." }
            };

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object> { { "frameIndex", frameIndex } } }
            };
        }

        private static Dictionary<string, object> CallTool(Dictionary<string, object> parameters)
        {
            string name = GetString(parameters, "name");
            Dictionary<string, object> arguments = GetDictionary(parameters, "arguments");

            try
            {
                object payload = RunTool(name, arguments);
                return ToolResult(Serializer.Serialize(payload), false);
            }
            catch (Exception ex)
            {
                return ToolResult(ex.Message, true);
            }
        }

        private static Dictionary<string, object> ToolResult(string text, bool isError)
        {
            Dictionary<string, object> content = new Dictionary<string, object> { { "type", "text" }, { "text", text } };
            return new Dictionary<string, object> { { "content", new object[] { content } }, { "isError", isError } };
        }

        private static object RunTool(string name, Dictionary<string, object> arguments)
        {
            switch (name)
            {
                case "debugger_status":
                    return Status();
                case "debugger_call_stack":
                    return CallStack();
                case "debugger_locals":
                    return Locals(GetInt(arguments, "frameIndex", 0));
                case "debugger_exception":
                    return CurrentException();
                case "debugger_breakpoints":
                    return Breakpoints();
                default:
                    throw new InvalidOperationException("Unknown tool: " + name);
            }
        }

        private static object Status()
        {
            List<DteInstance> instances = FindInstances();
            DteInstance selected = SelectInstance(instances);
            dynamic dte = selected.Dte;
            dynamic debugger = Com(() => dte.Debugger);
            int mode = Com(() => (int)debugger.CurrentMode);

            Dictionary<string, object> status = new Dictionary<string, object>
            {
                { "instances", instances.Select(Describe).ToList() },
                { "selected", selected.Name },
                { "mode", ModeName(mode) }
            };

            if (mode == BreakMode)
            {
                dynamic frame = Com(() => debugger.CurrentStackFrame);
                status["currentFrame"] = DescribeFrame(frame, 0);
            }

            return status;
        }

        private static object CallStack()
        {
            dynamic debugger = RequireBreakMode();
            List<object> frames = EnumerateFrames(debugger).Take(MaxStackFrames).ToList();
            return frames.Select((frame, index) => DescribeFrame(frame, index)).ToList();
        }

        private static object Locals(int frameIndex)
        {
            dynamic debugger = RequireBreakMode();
            object frame = EnumerateFrames(debugger).Skip(frameIndex).FirstOrDefault();
            if (frame == null)
            {
                throw new InvalidOperationException("No stack frame at index " + frameIndex);
            }

            dynamic locals = Com(() => ((dynamic)frame).Locals);
            List<object> items = Com(() => ((IEnumerable)locals).Cast<object>().Take(MaxLocals).ToList());

            return items.Select(DescribeExpression).ToList();
        }

        private static object CurrentException()
        {
            dynamic debugger = RequireBreakMode();
            dynamic exception = Com(() => debugger.GetExpression("$exception", false, ExpressionTimeoutMs));

            return !Com(() => (bool)exception.IsValidValue)
                ? new Dictionary<string, object> { { "hasException", false } }
                : new Dictionary<string, object>
            {
                { "hasException", true },
                { "type", Trim(Safe(() => (string)exception.Type)) },
                { "message", EvaluateText(debugger, "$exception.Message") },
                { "stackTrace", EvaluateText(debugger, "$exception.StackTrace") }
            };
        }

        private static object Breakpoints()
        {
            dynamic dte = SelectInstance(FindInstances()).Dte;
            dynamic debugger = Com(() => dte.Debugger);
            dynamic breakpoints = Com(() => debugger.Breakpoints);
            List<object> items = Com(() => ((IEnumerable)breakpoints).Cast<object>().Take(MaxBreakpoints).ToList());

            return items.Select(DescribeBreakpoint).ToList();
        }

        // ---------------------------------------------------------------- Describing COM objects

        private static Dictionary<string, object> DescribeFrame(dynamic frame, int index)
        {
            return new Dictionary<string, object>
            {
                { "index", index },
                { "function", Safe(() => (string)frame.FunctionName) },
                { "module", Safe(() => (string)frame.Module) },
                { "file", Safe(() => (string)frame.FileName) },
                { "line", Safe(() => (int?)frame.LineNumber) }
            };
        }

        private static Dictionary<string, object> DescribeExpression(object item)
        {
            dynamic expression = item;
            return new Dictionary<string, object>
            {
                { "name", Safe(() => (string)expression.Name) },
                { "type", Safe(() => (string)expression.Type) },
                { "value", Trim(Safe(() => (string)expression.Value)) }
            };
        }

        private static Dictionary<string, object> DescribeBreakpoint(object item)
        {
            dynamic breakpoint = item;
            return new Dictionary<string, object>
            {
                { "file", Safe(() => (string)breakpoint.File) },
                { "line", Safe(() => (int?)breakpoint.FileLine) },
                { "function", Safe(() => (string)breakpoint.FunctionName) },
                { "enabled", Safe(() => (bool?)breakpoint.Enabled) },
                { "condition", Safe(() => (string)breakpoint.Condition) },
                { "hitCount", Safe(() => (int?)breakpoint.CurrentHits) }
            };
        }

        private static string EvaluateText(dynamic debugger, string expressionText)
        {
            dynamic expression = Com(() => debugger.GetExpression(expressionText, false, ExpressionTimeoutMs));
            bool valid = Safe(() => (bool?)expression.IsValidValue) ?? false;
            return valid ? Trim(Safe(() => (string)expression.Value)) : null;
        }

        private static string Trim(string value)
        {
            return value != null && value.Length > MaxValueLength ? value.Substring(0, MaxValueLength) + "...(truncated)" : value;
        }

        private static string ModeName(int mode)
        {
            switch (mode)
            {
                case DesignMode:
                    return "design";
                case BreakMode:
                    return "break";
                case RunMode:
                    return "run";
                default:
                    return "unknown(" + mode + ")";
            }
        }

        private static IEnumerable<object> EnumerateFrames(dynamic debugger)
        {
            dynamic thread = Com(() => debugger.CurrentThread);
            dynamic frames = Com(() => thread.StackFrames);
            return Com(() => ((IEnumerable)frames).Cast<object>().ToList());
        }

        private static dynamic RequireBreakMode()
        {
            dynamic dte = SelectInstance(FindInstances()).Dte;
            dynamic debugger = Com(() => dte.Debugger);
            int mode = Com(() => (int)debugger.CurrentMode);

            return mode != BreakMode
                ? throw new InvalidOperationException("Debugger is in " + ModeName(mode) + " mode; break mode is required.")
                : debugger;
        }

        // ---------------------------------------------------------------- COM helpers

        /// <summary>Retries when Visual Studio is busy and rejects the call.</summary>
        private static T Com<T>(Func<T> action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return action();
                }
                catch (COMException ex) when (attempt < ComRetryCount && (ex.HResult == RpcCallRejected || ex.HResult == RpcRetryLater))
                {
                    Thread.Sleep(ComRetryDelayMs);
                }
            }
        }

        /// <summary>Property reads that may legitimately fail (no source, optimized frame) return null.</summary>
        private static T Safe<T>(Func<T> action)
        {
            try
            {
                return Com(action);
            }
            catch (Exception)
            {
                return default(T);
            }
        }

        private sealed class DteInstance
        {
            public string Name
            {
                get; set;
            }
            public int ProcessId
            {
                get; set;
            }
            public object Dte
            {
                get; set;
            }
        }

        private static Dictionary<string, object> Describe(DteInstance instance)
        {
            dynamic dte = instance.Dte;
            dynamic debugger = Safe(() => (object)dte.Debugger);

            return new Dictionary<string, object>
            {
                { "name", instance.Name },
                { "processId", instance.ProcessId },
                { "solution", Safe(() => (string)dte.Solution.FullName) },
                { "mode", ModeName(Safe(() => (int?)debugger.CurrentMode) ?? 0) }
            };
        }

        private static DteInstance SelectInstance(List<DteInstance> instances)
        {
            if (instances.Count == 0)
            {
                throw new InvalidOperationException("No running Visual Studio 2026 (DTE 18.0) instance found. Visual Studio and this tool must run at the same elevation level.");
            }

            string forcedPid = Environment.GetEnvironmentVariable(PidEnvironmentVariable);
            if (!string.IsNullOrEmpty(forcedPid))
            {
                DteInstance forced = instances.FirstOrDefault(i => i.ProcessId.ToString() == forcedPid);
                return forced == null
                    ? throw new InvalidOperationException(PidEnvironmentVariable + "=" + forcedPid + " does not match a running instance.")
                    : forced;
            }

            // Prefer an instance that is actually debugging.
            DteInstance debugging = instances.FirstOrDefault(i => (Safe(() => (int?)((dynamic)i.Dte).Debugger.CurrentMode) ?? DesignMode) != DesignMode);
            return debugging ?? instances[0];
        }

        private static List<DteInstance> FindInstances()
        {
            List<DteInstance> instances = new List<DteInstance>();

            IRunningObjectTable table = OpenRunningObjectTable();
            IEnumMoniker monikers = EnumerateMonikers(table);
            IMoniker[] current = new IMoniker[1];

            while (monikers.Next(1, current, IntPtr.Zero) == 0)
            {
                string displayName = DisplayName(current[0]);
                if (displayName.StartsWith(DteProgIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    instances.Add(CreateInstance(table, current[0], displayName));
                }
            }

            return instances;
        }

        private static DteInstance CreateInstance(IRunningObjectTable table, IMoniker moniker, string displayName)
        {
            object dte = GetRunningObject(table, moniker);
            int.TryParse(displayName.Substring(DteProgIdPrefix.Length), out int processId);
            return new DteInstance { Name = displayName.TrimStart('!'), ProcessId = processId, Dte = dte };
        }

        // The out parameters below are required by the COM interop signatures.
        private static IRunningObjectTable OpenRunningObjectTable()
        {
            Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out IRunningObjectTable table));
            return table;
        }

        private static IEnumMoniker EnumerateMonikers(IRunningObjectTable table)
        {
            table.EnumRunning(out IEnumMoniker monikers);
            monikers.Reset();
            return monikers;
        }

        private static string DisplayName(IMoniker moniker)
        {
            Marshal.ThrowExceptionForHR(CreateBindCtx(0, out IBindCtx context));
            moniker.GetDisplayName(context, null, out string name);
            return name ?? string.Empty;
        }

        private static object GetRunningObject(IRunningObjectTable table, IMoniker moniker)
        {
            table.GetObject(moniker, out object instance);
            return instance;
        }
    }
}
