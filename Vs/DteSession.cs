using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DteMcp.Vs
{
    internal static class DebuggerModes
    {
        public const int Design = 1;
        public const int Break = 2;
        public const int Run = 3;

        public static string Name(int mode)
        {
            switch (mode)
            {
                case Design:
                    return "design";
                case Break:
                    return "break";
                case Run:
                    return "run";
                default:
                    return "unknown(" + mode + ")";
            }
        }
    }

    internal sealed class DteInstance
    {
        public string Name { get; set; }

        public int ProcessId { get; set; }

        public object Dte { get; set; }
    }

    /// <summary>Finds running Visual Studio 2026 instances through the COM Running Object Table and picks one.</summary>
    internal sealed class DteSession
    {
        private const string DteProgIdPrefix = "!VisualStudio.DTE.18.0:";
        private const string PidEnvironmentVariable = "DTEMCP_PID";

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable runningObjectTable);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx bindContext);

        /// <summary>The selected instance's EnvDTE.DTE (late bound).</summary>
        public dynamic Dte
        {
            get { return Select(Instances()).Dte; }
        }

        /// <summary>The selected instance's EnvDTE.Debugger (late bound).</summary>
        public dynamic Debugger
        {
            get
            {
                dynamic dte = Dte;
                return Com.Run(() => dte.Debugger);
            }
        }

        public List<DteInstance> Instances()
        {
            IRunningObjectTable table = OpenRunningObjectTable();
            return EnumerateMonikers(table)
                .Select(moniker => new { Moniker = moniker, DisplayName = DisplayName(moniker) })
                .Where(entry => entry.DisplayName.StartsWith(DteProgIdPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(entry => CreateInstance(table, entry.Moniker, entry.DisplayName))
                .ToList();
        }

        public DteInstance Select(List<DteInstance> instances)
        {
            if (instances.Count == 0)
            {
                throw new InvalidOperationException("No running Visual Studio 2026 (DTE 18.0) instance found. Visual Studio and this tool must run at the same elevation level.");
            }

            string forcedPid = Environment.GetEnvironmentVariable(PidEnvironmentVariable);
            if (!string.IsNullOrEmpty(forcedPid))
            {
                DteInstance forced = instances.FirstOrDefault(i => i.ProcessId.ToString() == forcedPid);
                return forced ?? throw new InvalidOperationException(PidEnvironmentVariable + "=" + forcedPid + " does not match a running instance.");
            }

            // Prefer an instance that is actually debugging.
            DteInstance debugging = instances.FirstOrDefault(i => (Com.Try(() => (int?)((dynamic)i.Dte).Debugger.CurrentMode) ?? DebuggerModes.Design) != DebuggerModes.Design);
            return debugging ?? instances[0];
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

        private static List<IMoniker> EnumerateMonikers(IRunningObjectTable table)
        {
            table.EnumRunning(out IEnumMoniker monikers);
            monikers.Reset();

            List<IMoniker> result = new List<IMoniker>();
            IMoniker[] current = new IMoniker[1];
            while (monikers.Next(1, current, IntPtr.Zero) == 0)
            {
                result.Add(current[0]);
            }

            return result;
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
