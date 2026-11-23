using System;
using System.Linq;
using DteMcp.Mcp;
using DteMcp.Tools;
using DteMcp.Vs;

namespace DteMcp
{
    /// <summary>
    /// MCP server (stdio, newline-delimited JSON-RPC) that attaches to a running Visual Studio 2026
    /// instance through the COM Running Object Table and exposes its DTE automation model as tools.
    /// No VSIX, no network listener, no third-party packages.
    /// Set DTEMCP_READONLY=1 to expose only the read-only tools.
    /// </summary>
    internal static class Program
    {
        private const string ReadOnlyEnvironmentVariable = "DTEMCP_READONLY";

        // STA keeps COM calls into devenv on the apartment model it expects.
        [STAThread]
        private static int Main(string[] args)
        {
            DteSession session = new DteSession();
            IToolProvider[] providers =
            {
                new DebuggerTools(session),
                new DocumentTools(session),
                new SolutionTools(session),
                new BuildTools(session),
                new CommandTools(session),
                new ObjectModelTools(session)
            };

            bool readOnly = Environment.GetEnvironmentVariable(ReadOnlyEnvironmentVariable) == "1";
            ToolRegistry registry = new ToolRegistry(providers.SelectMany(provider => provider.Tools()), readOnly);

            new McpServer(registry).Run(Console.OpenStandardInput(), Console.OpenStandardOutput());
            return 0;
        }
    }
}
