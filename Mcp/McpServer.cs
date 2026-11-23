using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DteMcp.Mcp
{
    /// <summary>Newline-delimited JSON-RPC 2.0 over stdio, speaking just enough MCP to list and call tools.</summary>
    internal sealed class McpServer
    {
        private const string ServerName = "dtemcp";
        private const string ServerVersion = "0.2.0";

        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly ToolRegistry registry;

        public McpServer(ToolRegistry registry)
        {
            this.registry = registry;
        }

        public void Run(Stream input, Stream output)
        {
            using (StreamReader reader = new StreamReader(input, Utf8))
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
        }

        private void HandleLine(string line, Stream output)
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

                object result = Dispatch(GetString(request, "method"), GetDictionary(request, "params"));
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

        private object Dispatch(string method, Dictionary<string, object> parameters)
        {
            switch (method)
            {
                case "initialize":
                    return new Dictionary<string, object>
                    {
                        { "protocolVersion", GetString(parameters, "protocolVersion") ?? "2025-06-18" },
                        { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object>() } } },
                        { "serverInfo", new Dictionary<string, object> { { "name", ServerName }, { "version", ServerVersion } } }
                    };
                case "ping":
                    return new Dictionary<string, object>();
                case "tools/list":
                    return new Dictionary<string, object> { { "tools", registry.List() } };
                case "tools/call":
                    return CallTool(parameters);
                default:
                    throw new InvalidOperationException("Method not supported: " + method);
            }
        }

        private Dictionary<string, object> CallTool(Dictionary<string, object> parameters)
        {
            try
            {
                object payload = registry.Call(GetString(parameters, "name"), new ToolArguments(GetDictionary(parameters, "arguments")));
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
    }
}
