using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RsyncZilla.Models;
using RsyncZilla.ViewModels;

namespace RsyncZilla.Services
{
    public class McpServerService : IDisposable
    {
        public const string DefaultPipeName = "RsyncZilla_MCP_Pipe";

        private readonly Func<IReadOnlyList<RemoteSessionViewModel>> _getSessions;
        private readonly Func<RemoteSessionViewModel?> _getActiveSession;
        private readonly Action<string, bool>? _logger;
        private readonly string _pipeName;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public string PipeName => _pipeName;
        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public McpServerService(
            Func<IReadOnlyList<RemoteSessionViewModel>> getSessions,
            Func<RemoteSessionViewModel?> getActiveSession,
            Action<string, bool>? logger = null,
            string pipeName = DefaultPipeName)
        {
            _getSessions = getSessions ?? throw new ArgumentNullException(nameof(getSessions));
            _getActiveSession = getActiveSession ?? throw new ArgumentNullException(nameof(getActiveSession));
            _logger = logger;
            _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;
        }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => ListenLoopAsync(token), token);
            _logger?.Invoke($"[MCP] AI Terminal MCP Server listening on pipe '{_pipeName}'.", false);
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            catch { }
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? serverStream = null;
                try
                {
                    serverStream = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await serverStream.WaitForConnectionAsync(token);
                    var connectedClient = serverStream;
                    serverStream = null; // Hand off ownership to client handler task
                    _ = Task.Run(() => HandleClientPipeAsync(connectedClient, token), token);
                }
                catch (OperationCanceledException)
                {
                    serverStream?.Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    serverStream?.Dispose();
                    if (!token.IsCancellationRequested)
                    {
                        _logger?.Invoke($"[MCP] Pipe listener error: {ex.Message}", true);
                        try { await Task.Delay(1000, token); } catch { break; }
                    }
                }
            }
        }

        private async Task HandleClientPipeAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            using (pipe)
            {
                try
                {
                    var utf8NoBom = new UTF8Encoding(false);
                    using var reader = new StreamReader(pipe, utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

                    while (pipe.IsConnected && !token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(token);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var response = await HandleJsonRpcMessageAsync(line, token);
                        if (!string.IsNullOrEmpty(response) && pipe.IsConnected)
                        {
                            await writer.WriteLineAsync(response);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger?.Invoke($"[MCP] Client connection error: {ex.Message}", true);
                }
            }
        }

        public async Task<string?> HandleJsonRpcMessageAsync(string jsonMessage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(jsonMessage)) return null;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(jsonMessage);
            }
            catch (Exception ex)
            {
                return BuildJsonRpcError(null, -32700, $"Parse error: {ex.Message}");
            }

            if (root is not JsonObject obj)
            {
                return BuildJsonRpcError(null, -32600, "Invalid Request");
            }

            var idNode = obj["id"]?.DeepClone();
            var method = obj["method"]?.GetValue<string>();
            var paramsNode = obj["params"] as JsonObject;

            // Notifications have no "id" and expect no response
            if (idNode == null && method != null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(method))
            {
                return BuildJsonRpcError(idNode, -32600, "Missing method name");
            }

            try
            {
                switch (method)
                {
                    case "initialize":
                        return BuildJsonRpcResult(idNode, GetInitializeResult());

                    case "ping":
                        return BuildJsonRpcResult(idNode, new JsonObject());

                    case "tools/list":
                        return BuildJsonRpcResult(idNode, GetToolsListResult());

                    case "tools/call":
                        var callResult = await HandleToolCallAsync(paramsNode, cancellationToken);
                        return BuildJsonRpcResult(idNode, callResult);

                    default:
                        return BuildJsonRpcError(idNode, -32601, $"Method not found: {method}");
                }
            }
            catch (Exception ex)
            {
                return BuildJsonRpcError(idNode, -32603, $"Internal error: {ex.Message}");
            }
        }

        public static JsonObject GetInitializeResult()
        {
            return new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject()
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "RsyncZilla-Terminal-MCP",
                    ["version"] = "1.7.0"
                }
            };
        }

        public static JsonObject GetToolsListResult()
        {
            var tools = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "list_terminal_sessions",
                    ["description"] = "Lists all open SSH terminal tabs in RsyncZilla that have AI access enabled ('ReadAndConfirm' or 'Auto'), including their tab alias (MCP1, MCP2, ...), server host, username, current working directory, and permission mode.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject(),
                        ["required"] = new JsonArray()
                    }
                },
                new JsonObject
                {
                    ["name"] = "execute_terminal_command",
                    ["description"] = "Executes a shell command inside a live RsyncZilla SSH terminal tab. The user sees the command and its output streaming in real time in the terminal. In 'ReadAndConfirm' mode, RsyncZilla prompts the user for approval before running the command. In 'Auto' mode, it runs immediately.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["command"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The shell command to execute on the remote server (e.g. 'systemctl status nginx', 'df -h', 'cd /var/www && ls -la')."
                            },
                            ["session_id"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Optional tab name such as 'MCP1', 'MCP2' (shown in parentheses in the tab title), or session_id from list_terminal_sessions. If omitted, uses the active AI-enabled terminal tab."
                            },
                            ["timeout_seconds"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "Optional timeout in seconds (default: 30, max: 600)."
                            }
                        },
                        ["required"] = new JsonArray(JsonValue.Create("command"))
                    }
                },
                new JsonObject
                {
                    ["name"] = "read_terminal_output",
                    ["description"] = "Reads the recent screen output from the live SSH terminal buffer. Useful for inspecting ongoing command output, logs, or interactive prompts. Allowed without confirmation in both 'ReadAndConfirm' and 'Auto' modes.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["max_lines"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "Maximum number of recent lines to return (default: 100, max: 2000)."
                            },
                            ["session_id"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Optional tab name such as 'MCP1', 'MCP2', or session_id from list_terminal_sessions."
                            }
                        },
                        ["required"] = new JsonArray()
                    }
                },
                new JsonObject
                {
                    ["name"] = "send_terminal_input",
                    ["description"] = "Sends raw keystrokes or control signals to the live SSH terminal (e.g. 'CTRL+C' to interrupt a running process, or 'y\\n' to answer an interactive prompt). Requires user confirmation in 'ReadAndConfirm' mode.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["input"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Raw input text or 'CTRL+C' (\\u0003) to send to the terminal."
                            },
                            ["session_id"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Optional tab name such as 'MCP1', 'MCP2', or session_id from list_terminal_sessions."
                            }
                        },
                        ["required"] = new JsonArray(JsonValue.Create("input"))
                    }
                },
                new JsonObject
                {
                    ["name"] = "list_remote_directory",
                    ["description"] = "Lists files and directories in a remote path via SFTP. Allowed without confirmation in both 'ReadAndConfirm' and 'Auto' modes.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["path"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Remote directory path to list. Defaults to the session's current directory."
                            },
                            ["session_id"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Optional tab name such as 'MCP1', 'MCP2', or session_id from list_terminal_sessions."
                            }
                        },
                        ["required"] = new JsonArray()
                    }
                },
                new JsonObject
                {
                    ["name"] = "read_remote_file",
                    ["description"] = "Reads the text contents of a remote file via SFTP. Allowed without confirmation in both 'ReadAndConfirm' and 'Auto' modes.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["path"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Remote file path to read (absolute, or relative to current directory)."
                            },
                            ["session_id"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Optional tab name such as 'MCP1', 'MCP2', or session_id from list_terminal_sessions."
                            }
                        },
                        ["required"] = new JsonArray(JsonValue.Create("path"))
                    }
                },
                new JsonObject
                {
                    ["name"] = "write_remote_file",
                    ["description"] = "Writes text content to a remote file via SFTP. In 'ReadAndConfirm' mode, prompts the user for approval in the terminal UI before writing.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["path"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Remote file path to write (absolute, or relative to current directory)."
                            },
                            ["content"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Full text content to write to the remote file."
                            },
                            ["session_id"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Optional tab name such as 'MCP1', 'MCP2', or session_id from list_terminal_sessions."
                            }
                        },
                        ["required"] = new JsonArray(JsonValue.Create("path"), JsonValue.Create("content"))
                    }
                }
            };

            return new JsonObject
            {
                ["tools"] = tools
            };
        }

        private async Task<JsonObject> HandleToolCallAsync(JsonObject? paramsNode, CancellationToken ct)
        {
            var toolName = paramsNode?["name"]?.GetValue<string>();
            var args = paramsNode?["arguments"] as JsonObject;

            if (string.IsNullOrWhiteSpace(toolName))
            {
                return BuildToolCallText("Missing tool name.", isError: true);
            }

            switch (toolName)
            {
                case "list_terminal_sessions":
                    return HandleListSessions();

                case "execute_terminal_command":
                    return await HandleExecuteCommandAsync(args, ct);

                case "read_terminal_output":
                    return HandleReadTerminalOutput(args);

                case "send_terminal_input":
                    return await HandleSendTerminalInputAsync(args, ct);

                case "list_remote_directory":
                    return await HandleListRemoteDirectoryAsync(args);

                case "read_remote_file":
                    return await HandleReadRemoteFileAsync(args);

                case "write_remote_file":
                    return await HandleWriteRemoteFileAsync(args, ct);

                default:
                    return BuildToolCallText($"Unknown tool: '{toolName}'.", isError: true);
            }
        }

        private JsonObject HandleListSessions()
        {
            var allSessions = _getSessions();
            var active = _getActiveSession();

            var terminalTabs = allSessions.Where(s => s.IsTerminalTab).ToList();
            var enabledSessions = allSessions.Where(s => s.AiPermission != AiPermissionLevel.Off).ToList();

            if (enabledSessions.Count == 0)
            {
                var offTabs = terminalTabs.Where(s => s.AiPermission == AiPermissionLevel.Off).ToList();
                var tabNames = string.Join(", ", offTabs.Select(t => !string.IsNullOrWhiteSpace(t.McpName) ? t.McpName : t.Title));
                var msg = offTabs.Count > 0
                    ? $"There are {offTabs.Count} SSH terminal tab(s) open in RsyncZilla ({tabNames}), but all of them currently have AI access set to 'Off' (default). Please ask the user to click the '🔒 AI: Off' button in the terminal toolbar to switch it to '🛡️ AI: Confirm' or '⚡ AI: Auto'."
                    : "No SSH terminal tabs are currently open with AI access enabled in RsyncZilla. Please ask the user to open an SSH terminal tab (Ctrl+T) and set the AI button in the toolbar to '🛡️ AI: Confirm' or '⚡ AI: Auto'.";
                return BuildToolCallText(msg, isError: false);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"AI-Enabled Sessions in RsyncZilla ({enabledSessions.Count}):");
            foreach (var s in enabledSessions)
            {
                var isActive = active == s ? " [ACTIVE TAB]" : "";
                var mcpAlias = !string.IsNullOrWhiteSpace(s.McpName) ? s.McpName : s.Id.ToString();
                var currentDir = !string.IsNullOrWhiteSpace(s.RemoteBrowser.CurrentPath)
                    ? s.RemoteBrowser.CurrentPath
                    : (s.InitialTerminalPath ?? "/");
                sb.AppendLine($"- mcp_name: {mcpAlias}{isActive}");
                sb.AppendLine($"  session_id: {s.Id}");
                sb.AppendLine($"  title: {s.Title}");
                sb.AppendLine($"  server: {s.Username}@{s.Host}:{s.Port}");
                sb.AppendLine($"  is_terminal_tab: {s.IsTerminalTab}");
                sb.AppendLine($"  is_connected: {s.IsConnected}");
                sb.AppendLine($"  current_directory: {currentDir}");
                sb.AppendLine($"  ai_permission: {s.AiPermission}");
            }

            return BuildToolCallText(sb.ToString().TrimEnd(), isError: false);
        }

        private (RemoteSessionViewModel? session, string? error) ResolveTargetSession(string? sessionId)
        {
            var allSessions = _getSessions();
            var active = _getActiveSession();

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var normalized = sessionId.Trim().Trim('(', ')').Replace("-", "").Replace(" ", "");
                if (int.TryParse(normalized, out var numOnly))
                {
                    normalized = $"MCP{numOnly}";
                }

                var match = allSessions.FirstOrDefault(s =>
                    (!string.IsNullOrWhiteSpace(s.McpName) && string.Equals(s.McpName.Trim(), normalized, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(s.Id.ToString(), sessionId.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Title, sessionId.Trim(), StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    return (null, $"Session '{sessionId}' was not found in RsyncZilla.");
                }

                if (match.AiPermission == AiPermissionLevel.Off)
                {
                    return (null, $"AI access is set to 'Off' for session '{match.Title}'. Ask the user to switch the AI button in that tab's toolbar to 'AI: Confirm' or 'AI: Auto'.");
                }

                return (match, null);
            }

            // 1. Prefer the currently active tab if it has AI enabled
            if (active != null && active.AiPermission != AiPermissionLevel.Off)
            {
                return (active, null);
            }

            // 2. Otherwise pick the first terminal tab with AI enabled
            var enabledTerminal = allSessions.FirstOrDefault(s => s.IsTerminalTab && s.AiPermission != AiPermissionLevel.Off)
                                  ?? allSessions.FirstOrDefault(s => s.AiPermission != AiPermissionLevel.Off);

            if (enabledTerminal != null)
            {
                return (enabledTerminal, null);
            }

            return (null, "No tab in RsyncZilla currently has AI access enabled (all tabs default to 'AI: Off'). Ask the user to enable '🛡️ AI: Confirm' or '⚡ AI: Auto' in the terminal toolbar.");
        }

        private async Task<JsonObject> HandleExecuteCommandAsync(JsonObject? args, CancellationToken ct)
        {
            var command = args?["command"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(command))
            {
                return BuildToolCallText("Parameter 'command' is required.", isError: true);
            }

            var sessionId = args?["session_id"]?.GetValue<string>();
            int timeoutSeconds = 30;
            if (args?["timeout_seconds"] != null)
            {
                try { timeoutSeconds = args["timeout_seconds"]!.GetValue<int>(); } catch { }
            }

            var (session, err) = ResolveTargetSession(sessionId);
            if (session == null)
            {
                return BuildToolCallText(err!, isError: true);
            }

            if (session.TerminalSession == null || !session.TerminalSession.IsConnected)
            {
                return BuildToolCallText($"Session '{session.Title}' does not have an active connected SSH terminal.", isError: true);
            }

            if (session.AiPermission == AiPermissionLevel.ReadAndConfirm)
            {
                _logger?.Invoke($"[MCP] Waiting for user confirmation on {session.Title} to run: {command}", false);
                var approved = await session.RequestAiConfirmationAsync("Execute terminal command", command, ct);
                if (!approved)
                {
                    _logger?.Invoke($"[MCP] User rejected command on {session.Title}: {command}", true);
                    return BuildToolCallText($"Command rejected by user in RsyncZilla: {command}", isError: true);
                }
            }

            _logger?.Invoke($"[MCP] Executing command on {session.Title}: {command}", false);
            var (completed, exitCode, output, workingDir) = await session.TerminalSession.ExecuteCommandAsync(command, timeoutSeconds, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"Session: {session.Title}");
            sb.AppendLine($"Command: {command}");
            sb.AppendLine($"Status: {(completed ? "Completed" : $"Timed out / still running after {timeoutSeconds}s (use read_terminal_output or send_terminal_input)")}");
            if (exitCode.HasValue)
            {
                sb.AppendLine($"Exit Code: {exitCode.Value}");
            }
            if (!string.IsNullOrWhiteSpace(workingDir))
            {
                sb.AppendLine($"Working Directory: {workingDir}");
            }
            sb.AppendLine("--- Output ---");
            sb.AppendLine(string.IsNullOrWhiteSpace(output) ? "(no output)" : output);

            return BuildToolCallText(sb.ToString().TrimEnd(), isError: exitCode.HasValue && exitCode.Value != 0);
        }

        private JsonObject HandleReadTerminalOutput(JsonObject? args)
        {
            var sessionId = args?["session_id"]?.GetValue<string>();
            int maxLines = 100;
            if (args?["max_lines"] != null)
            {
                try { maxLines = args["max_lines"]!.GetValue<int>(); } catch { }
            }

            var (session, err) = ResolveTargetSession(sessionId);
            if (session == null)
            {
                return BuildToolCallText(err!, isError: true);
            }

            if (session.TerminalSession == null)
            {
                return BuildToolCallText($"Session '{session.Title}' has no terminal buffer.", isError: true);
            }

            var output = session.TerminalSession.GetRecentOutput(maxLines);
            session.TerminalSession.WriteAiNoticeToTerminal($"👁️ Read terminal screen output (last {maxLines} lines)");
            _logger?.Invoke($"[MCP] Read terminal output from {session.Title} (last {maxLines} lines)", false);

            return BuildToolCallText(string.IsNullOrWhiteSpace(output) ? "(terminal screen buffer is empty)" : output, isError: false);
        }

        private async Task<JsonObject> HandleSendTerminalInputAsync(JsonObject? args, CancellationToken ct)
        {
            var input = args?["input"]?.GetValue<string>();
            if (string.IsNullOrEmpty(input))
            {
                return BuildToolCallText("Parameter 'input' is required.", isError: true);
            }

            var sessionId = args?["session_id"]?.GetValue<string>();
            var (session, err) = ResolveTargetSession(sessionId);
            if (session == null)
            {
                return BuildToolCallText(err!, isError: true);
            }

            if (session.TerminalSession == null || !session.TerminalSession.IsConnected)
            {
                return BuildToolCallText($"Session '{session.Title}' does not have an active connected SSH terminal.", isError: true);
            }

            var actualInput = input.Equals("CTRL+C", StringComparison.OrdinalIgnoreCase) || input.Equals("^C", StringComparison.OrdinalIgnoreCase)
                ? "\x03"
                : input;

            var displayInput = actualInput == "\x03" ? "Ctrl+C (Interrupt)" : input.Replace("\r", "\\r").Replace("\n", "\\n");

            if (session.AiPermission == AiPermissionLevel.ReadAndConfirm)
            {
                var approved = await session.RequestAiConfirmationAsync("Send raw terminal input", displayInput, ct);
                if (!approved)
                {
                    return BuildToolCallText($"Terminal input rejected by user: {displayInput}", isError: true);
                }
            }

            session.TerminalSession.WriteAiNoticeToTerminal($"⌨️ Sent terminal input: {displayInput}");
            session.TerminalSession.SendInput(actualInput);
            _logger?.Invoke($"[MCP] Sent raw input to {session.Title}: {displayInput}", false);

            return BuildToolCallText($"Sent input '{displayInput}' to {session.Title}.", isError: false);
        }

        private async Task<JsonObject> HandleListRemoteDirectoryAsync(JsonObject? args)
        {
            var sessionId = args?["session_id"]?.GetValue<string>();
            var rawPath = args?["path"]?.GetValue<string>();

            var (session, err) = ResolveTargetSession(sessionId);
            if (session == null)
            {
                return BuildToolCallText(err!, isError: true);
            }

            var basePath = !string.IsNullOrWhiteSpace(session.RemoteBrowser.CurrentPath)
                ? session.RemoteBrowser.CurrentPath
                : (session.InitialTerminalPath ?? "/");

            var targetPath = string.IsNullOrWhiteSpace(rawPath)
                ? basePath
                : Views.TerminalView.ResolveRemotePath(rawPath, session.SftpService.HomeDirectory, basePath);

            session.TerminalSession?.WriteAiNoticeToTerminal($"📂 Listing remote directory: {targetPath}");
            _logger?.Invoke($"[MCP] Listing remote directory on {session.Title}: {targetPath}", false);

            var (items, sftpErr) = await session.SftpService.GetDirectoryContentsAsync(targetPath);
            if (sftpErr != null)
            {
                return BuildToolCallText(sftpErr, isError: true);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Directory: {session.SftpService.CurrentPath}");
            foreach (var item in items.Where(i => !i.IsParent))
            {
                var typeTag = item.IsDirectory ? "[DIR] " : "[FILE]";
                sb.AppendLine($"{item.Permissions ?? "---------"}  {typeTag}  {item.DisplaySize,10}  {item.LastWriteTime:yyyy-MM-dd HH:mm}  {item.Name}");
            }

            return BuildToolCallText(sb.ToString().TrimEnd(), isError: false);
        }

        private async Task<JsonObject> HandleReadRemoteFileAsync(JsonObject? args)
        {
            var rawPath = args?["path"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return BuildToolCallText("Parameter 'path' is required.", isError: true);
            }

            var sessionId = args?["session_id"]?.GetValue<string>();
            var (session, err) = ResolveTargetSession(sessionId);
            if (session == null)
            {
                return BuildToolCallText(err!, isError: true);
            }

            var basePath = !string.IsNullOrWhiteSpace(session.RemoteBrowser.CurrentPath)
                ? session.RemoteBrowser.CurrentPath
                : (session.InitialTerminalPath ?? "/");
            var resolvedPath = Views.TerminalView.ResolveRemotePath(rawPath, session.SftpService.HomeDirectory, basePath);

            session.TerminalSession?.WriteAiNoticeToTerminal($"📄 Reading remote file: {resolvedPath}");
            _logger?.Invoke($"[MCP] Reading remote file on {session.Title}: {resolvedPath}", false);

            var (ok, content, sftpErr) = await session.SftpService.ReadAllTextAsync(resolvedPath);
            if (!ok)
            {
                return BuildToolCallText(sftpErr ?? $"Failed to read '{resolvedPath}'.", isError: true);
            }

            return BuildToolCallText(content, isError: false);
        }

        private async Task<JsonObject> HandleWriteRemoteFileAsync(JsonObject? args, CancellationToken ct)
        {
            var rawPath = args?["path"]?.GetValue<string>();
            var content = args?["content"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(rawPath) || content == null)
            {
                return BuildToolCallText("Parameters 'path' and 'content' are required.", isError: true);
            }

            var sessionId = args?["session_id"]?.GetValue<string>();
            var (session, err) = ResolveTargetSession(sessionId);
            if (session == null)
            {
                return BuildToolCallText(err!, isError: true);
            }

            var basePath = !string.IsNullOrWhiteSpace(session.RemoteBrowser.CurrentPath)
                ? session.RemoteBrowser.CurrentPath
                : (session.InitialTerminalPath ?? "/");
            var resolvedPath = Views.TerminalView.ResolveRemotePath(rawPath, session.SftpService.HomeDirectory, basePath);

            if (session.AiPermission == AiPermissionLevel.ReadAndConfirm)
            {
                var approved = await session.RequestAiConfirmationAsync("Write remote file", $"{resolvedPath} ({content.Length} bytes)", ct);
                if (!approved)
                {
                    _logger?.Invoke($"[MCP] User rejected writing remote file on {session.Title}: {resolvedPath}", true);
                    return BuildToolCallText($"File write rejected by user in RsyncZilla: {resolvedPath}", isError: true);
                }
            }

            session.TerminalSession?.WriteAiNoticeToTerminal($"💾 Writing remote file: {resolvedPath} ({content.Length} bytes)");
            _logger?.Invoke($"[MCP] Writing remote file on {session.Title}: {resolvedPath} ({content.Length} bytes)", false);

            var (ok, sftpErr) = await session.SftpService.WriteAllTextAsync(resolvedPath, content);
            if (!ok)
            {
                return BuildToolCallText(sftpErr ?? $"Failed to write '{resolvedPath}'.", isError: true);
            }

            _ = session.RemoteBrowser.RefreshAsync();
            return BuildToolCallText($"Successfully wrote {content.Length} bytes to '{resolvedPath}'.", isError: false);
        }

        public static string GetMcpBridgeBinaryPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "RsyncZillaMcp.exe"),
                Path.Combine(baseDir, "RsyncAskPass.exe")
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c)) return Path.GetFullPath(c);
            }

            return Path.GetFullPath(Path.Combine(baseDir, "RsyncZillaMcp.exe"));
        }

        public static string GetMcpConfigJsonSnippet()
        {
            var exePath = GetMcpBridgeBinaryPath().Replace("\\", "\\\\");
            bool isAskPass = exePath.EndsWith("RsyncAskPass.exe", StringComparison.OrdinalIgnoreCase);
            var argsJson = isAskPass ? "[\"--mcp\"]" : "[]";

            return $$"""
            {
              "mcpServers": {
                "rsynczilla": {
                  "command": "{{exePath}}",
                  "args": {{argsJson}}
                }
              }
            }
            """;
        }

        private static JsonObject BuildToolCallText(string text, bool isError)
        {
            return new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = text
                    }
                },
                ["isError"] = isError
            };
        }

        private static string BuildJsonRpcResult(JsonNode? id, JsonNode result)
        {
            var response = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["result"] = result
            };
            return response.ToJsonString();
        }

        private static string BuildJsonRpcError(JsonNode? id, int code, string message)
        {
            var response = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message
                }
            };
            return response.ToJsonString();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
            }
        }
    }
}
