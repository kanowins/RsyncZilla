using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace RsyncAskPass
{
    internal class Program
    {
        private const string DefaultPipeName = "RsyncZilla_MCP_Pipe";

        static async Task<int> Main(string[] args)
        {
            if (args != null && args.Any(a => string.Equals(a, "--mcp", StringComparison.OrdinalIgnoreCase)))
            {
                var pipeName = Environment.GetEnvironmentVariable("RSYNCZILLA_MCP_PIPE");
                if (string.IsNullOrWhiteSpace(pipeName))
                {
                    pipeName = DefaultPipeName;
                }
                return await RunMcpStdioBridgeAsync(pipeName);
            }

            var password = Environment.GetEnvironmentVariable("RSYNC_PASSWORD");
            if (!string.IsNullOrEmpty(password))
            {
                Console.WriteLine(password);
                return 0;
            }
            return 1;
        }

        private static async Task<int> RunMcpStdioBridgeAsync(string pipeName)
        {
            var utf8NoBom = new UTF8Encoding(false);
            Console.InputEncoding = utf8NoBom;
            Console.OutputEncoding = utf8NoBom;

            using var stdinReader = new StreamReader(Console.OpenStandardInput(), utf8NoBom);
            await using var stdoutWriter = new StreamWriter(Console.OpenStandardOutput(), utf8NoBom) { AutoFlush = true };

            while (true)
            {
                string? line;
                try
                {
                    line = await stdinReader.ReadLineAsync();
                }
                catch
                {
                    break;
                }

                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var response = await ForwardOrHandleMessageAsync(line, pipeName, utf8NoBom);
                if (!string.IsNullOrEmpty(response))
                {
                    await stdoutWriter.WriteLineAsync(response);
                }
            }

            return 0;
        }

        private static async Task<string?> ForwardOrHandleMessageAsync(string jsonLine, string pipeName, Encoding encoding)
        {
            // First try forwarding to the running RsyncZilla instance over the Named Pipe
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipeClient.ConnectAsync(1500);

                using var writer = new StreamWriter(pipeClient, encoding, 4096, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipeClient, encoding, detectEncodingFromByteOrderMarks: false, 4096, leaveOpen: true);

                await writer.WriteLineAsync(jsonLine);

                // Parse to check if it's a notification (no "id")
                var node = JsonNode.Parse(jsonLine) as JsonObject;
                if (node != null && node["id"] == null)
                {
                    return null;
                }

                return await reader.ReadLineAsync();
            }
            catch
            {
                // Fallback when RsyncZilla GUI is not currently running so MCP client initialization still succeeds
                return HandleOfflineFallback(jsonLine);
            }
        }

        private static string? HandleOfflineFallback(string jsonLine)
        {
            try
            {
                var obj = JsonNode.Parse(jsonLine) as JsonObject;
                if (obj == null) return null;

                var idNode = obj["id"]?.DeepClone();
                var method = obj["method"]?.GetValue<string>();
                if (idNode == null) return null;

                if (method == "initialize")
                {
                    var res = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = idNode,
                        ["result"] = new JsonObject
                        {
                            ["protocolVersion"] = "2024-11-05",
                            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                            ["serverInfo"] = new JsonObject
                            {
                                ["name"] = "RsyncZilla-Terminal-MCP",
                                ["version"] = "1.7.0"
                            }
                        }
                    };
                    return res.ToJsonString();
                }

                if (method == "ping")
                {
                    var res = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = idNode,
                        ["result"] = new JsonObject()
                    };
                    return res.ToJsonString();
                }

                if (method == "tools/list")
                {
                    var res = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = idNode,
                        ["result"] = new JsonObject
                        {
                            ["tools"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "list_terminal_sessions",
                                    ["description"] = "Lists all open SSH terminal tabs in RsyncZilla that have AI access enabled ('ReadAndConfirm' or 'Auto').",
                                    ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
                                },
                                new JsonObject
                                {
                                    ["name"] = "execute_terminal_command",
                                    ["description"] = "Executes a shell command inside a live RsyncZilla SSH terminal tab.",
                                    ["inputSchema"] = new JsonObject
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new JsonObject
                                        {
                                            ["command"] = new JsonObject { ["type"] = "string" },
                                            ["session_id"] = new JsonObject { ["type"] = "string" },
                                            ["timeout_seconds"] = new JsonObject { ["type"] = "integer" }
                                        },
                                        ["required"] = new JsonArray(JsonValue.Create("command"))
                                    }
                                },
                                new JsonObject
                                {
                                    ["name"] = "read_terminal_output",
                                    ["description"] = "Reads the recent screen output from the live SSH terminal buffer.",
                                    ["inputSchema"] = new JsonObject
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new JsonObject
                                        {
                                            ["max_lines"] = new JsonObject { ["type"] = "integer" },
                                            ["session_id"] = new JsonObject { ["type"] = "string" }
                                        }
                                    }
                                },
                                new JsonObject
                                {
                                    ["name"] = "send_terminal_input",
                                    ["description"] = "Sends raw keystrokes or control signals (e.g. CTRL+C) to the live SSH terminal.",
                                    ["inputSchema"] = new JsonObject
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new JsonObject
                                        {
                                            ["input"] = new JsonObject { ["type"] = "string" },
                                            ["session_id"] = new JsonObject { ["type"] = "string" }
                                        },
                                        ["required"] = new JsonArray(JsonValue.Create("input"))
                                    }
                                },
                                new JsonObject
                                {
                                    ["name"] = "list_remote_directory",
                                    ["description"] = "Lists files and directories in a remote path via SFTP.",
                                    ["inputSchema"] = new JsonObject
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new JsonObject
                                        {
                                            ["path"] = new JsonObject { ["type"] = "string" },
                                            ["session_id"] = new JsonObject { ["type"] = "string" }
                                        }
                                    }
                                },
                                new JsonObject
                                {
                                    ["name"] = "read_remote_file",
                                    ["description"] = "Reads the text contents of a remote file via SFTP.",
                                    ["inputSchema"] = new JsonObject
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new JsonObject
                                        {
                                            ["path"] = new JsonObject { ["type"] = "string" },
                                            ["session_id"] = new JsonObject { ["type"] = "string" }
                                        },
                                        ["required"] = new JsonArray(JsonValue.Create("path"))
                                    }
                                },
                                new JsonObject
                                {
                                    ["name"] = "write_remote_file",
                                    ["description"] = "Writes text content to a remote file via SFTP.",
                                    ["inputSchema"] = new JsonObject
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new JsonObject
                                        {
                                            ["path"] = new JsonObject { ["type"] = "string" },
                                            ["content"] = new JsonObject { ["type"] = "string" },
                                            ["session_id"] = new JsonObject { ["type"] = "string" }
                                        },
                                        ["required"] = new JsonArray(JsonValue.Create("path"), JsonValue.Create("content"))
                                    }
                                }
                            }
                        }
                    };
                    return res.ToJsonString();
                }

                var offlineMsg = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = idNode,
                    ["result"] = new JsonObject
                    {
                        ["content"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "text",
                                ["text"] = "RsyncZilla is not currently open. Please launch RsyncZilla, open an SSH Terminal tab, and switch its AI mode from '🔒 AI: Off' to '🛡️ AI: Confirm' or '⚡ AI: Auto'."
                            }
                        },
                        ["isError"] = true
                    }
                };
                return offlineMsg.ToJsonString();
            }
            catch
            {
                return null;
            }
        }
    }
}
