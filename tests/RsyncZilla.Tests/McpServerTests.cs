using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RsyncZilla.Models;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;
using Xunit;

namespace RsyncZilla.Tests
{
    public class McpServerTests
    {
        [Fact]
        public void NewSessionAndTerminalTab_ShouldDefaultToAiOff()
        {
            var vm = new MainViewModel();
            Assert.Equal(AiPermissionLevel.Off, vm.ActiveSession.AiPermission);
            Assert.True(vm.ActiveSession.IsAiOff);
            Assert.Equal("🔒 AI: Off", vm.ActiveSession.AiPermissionBadgeText);

            var termTab = vm.OpenTerminalTab("prod.example.com", 22, "root", "secret", "/var/www");
            Assert.Equal(AiPermissionLevel.Off, termTab.AiPermission);
            Assert.True(termTab.IsAiOff);
        }

        [Fact]
        public void CycleAiPermissionCommand_ShouldCycleThroughThreeModesAndResetOnDisconnect()
        {
            var session = new RemoteSessionViewModel();
            Assert.Equal(AiPermissionLevel.Off, session.AiPermission);

            session.CycleAiPermissionCommand.Execute(null);
            Assert.Equal(AiPermissionLevel.ReadAndConfirm, session.AiPermission);
            Assert.True(session.IsAiReadAndConfirm);
            Assert.Equal("🛡️ AI: Confirm", session.AiPermissionBadgeText);

            session.CycleAiPermissionCommand.Execute(null);
            Assert.Equal(AiPermissionLevel.Auto, session.AiPermission);
            Assert.True(session.IsAiAuto);
            Assert.Equal("⚡ AI: Auto", session.AiPermissionBadgeText);

            session.CycleAiPermissionCommand.Execute(null);
            Assert.Equal(AiPermissionLevel.Off, session.AiPermission);
            Assert.True(session.IsAiOff);

            // Disconnect always resets permission back to Off
            session.AiPermission = AiPermissionLevel.Auto;
            session.Disconnect();
            Assert.Equal(AiPermissionLevel.Off, session.AiPermission);
        }

        [Fact]
        public async Task McpServer_InitializeAndToolsList_ShouldReturnStandardMcpSchema()
        {
            var vm = new MainViewModel();
            var initResp = await vm.McpServer.HandleJsonRpcMessageAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
            Assert.NotNull(initResp);

            var initJson = JsonNode.Parse(initResp!) as JsonObject;
            Assert.NotNull(initJson);
            Assert.Equal("2.0", initJson!["jsonrpc"]?.GetValue<string>());
            Assert.Equal("2024-11-05", initJson["result"]?["protocolVersion"]?.GetValue<string>());

            var toolsResp = await vm.McpServer.HandleJsonRpcMessageAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
            Assert.NotNull(toolsResp);

            var toolsJson = JsonNode.Parse(toolsResp!) as JsonObject;
            var toolsArray = toolsJson?["result"]?["tools"] as JsonArray;
            Assert.True(toolsArray != null, $"toolsResp was: {toolsResp}");
            Assert.Equal(7, toolsArray!.Count);
        }

        [Fact]
        public async Task McpServer_ExecuteCommand_WhenTabIsOff_ShouldBlockAccess()
        {
            var vm = new MainViewModel();
            var tab = vm.OpenTerminalTab("srv.example.com", 22, "debian", "pwd", "/home/debian");
            tab.TerminalSession = new SshTerminalSession("srv.example.com", 22, "debian", "pwd")
            {
                CustomCommandExecutor = (cmd, timeout, ct) => Task.FromResult((true, (int?)0, "ok", (string?)"/home/debian"))
            };

            // Default is Off
            Assert.Equal(AiPermissionLevel.Off, tab.AiPermission);

            var req = """
            {
              "jsonrpc": "2.0",
              "id": 10,
              "method": "tools/call",
              "params": {
                "name": "execute_terminal_command",
                "arguments": { "command": "whoami" }
              }
            }
            """;

            var resp = await vm.McpServer.HandleJsonRpcMessageAsync(req);
            var json = JsonNode.Parse(resp!) as JsonObject;
            var isError = json?["result"]?["isError"]?.GetValue<bool>() ?? false;
            var text = json?["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? "";

            Assert.True(isError);
            Assert.Contains("AI: Off", text);
        }

        [Fact]
        public async Task McpServer_ExecuteCommand_InAutoMode_ShouldExecuteDirectlyAndEchoToTerminal()
        {
            var vm = new MainViewModel();
            var tab = vm.OpenTerminalTab("srv.example.com", 22, "debian", "pwd", "/home/debian");
            var terminalOutputs = new List<string>();
            string? detectedPwd = null;

            var termSession = new SshTerminalSession("srv.example.com", 22, "debian", "pwd")
            {
                CustomCommandExecutor = (cmd, timeout, ct) =>
                    Task.FromResult((true, (int?)0, "nginx is running", (string?)"/etc/nginx"))
            };
            termSession.OutputReceived += text => terminalOutputs.Add(text);
            termSession.WorkingDirectoryDetected += pwd => detectedPwd = pwd;
            tab.TerminalSession = termSession;
            tab.AiPermission = AiPermissionLevel.Auto;

            var req = """
            {
              "jsonrpc": "2.0",
              "id": 11,
              "method": "tools/call",
              "params": {
                "name": "execute_terminal_command",
                "arguments": { "command": "systemctl status nginx" }
              }
            }
            """;

            var resp = await vm.McpServer.HandleJsonRpcMessageAsync(req);
            var json = JsonNode.Parse(resp!) as JsonObject;
            var isError = json?["result"]?["isError"]?.GetValue<bool>() ?? true;
            var text = json?["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? "";

            Assert.False(isError);
            Assert.Contains("Exit Code: 0", text);
            Assert.Contains("nginx is running", text);
            Assert.Equal("/etc/nginx", detectedPwd);

            // Verify the command and output were visibly echoed into the terminal tab
            Assert.Contains(terminalOutputs, o => o.Contains("systemctl status nginx"));
            Assert.Contains(terminalOutputs, o => o.Contains("nginx is running"));
        }

        [Fact]
        public async Task McpServer_ExecuteCommand_InReadAndConfirmMode_ShouldAwaitUserApprovalOrRejection()
        {
            var vm = new MainViewModel();
            var tab = vm.OpenTerminalTab("srv.example.com", 22, "debian", "pwd", "/home/debian");
            bool executed = false;

            tab.TerminalSession = new SshTerminalSession("srv.example.com", 22, "debian", "pwd")
            {
                CustomCommandExecutor = (cmd, timeout, ct) =>
                {
                    executed = true;
                    return Task.FromResult((true, (int?)0, "restarted", (string?)"/home/debian"));
                }
            };
            tab.AiPermission = AiPermissionLevel.ReadAndConfirm;

            var req = """
            {
              "jsonrpc": "2.0",
              "id": 12,
              "method": "tools/call",
              "params": {
                "name": "execute_terminal_command",
                "arguments": { "command": "sudo systemctl restart nginx" }
              }
            }
            """;

            // 1. Test Rejection
            var callTask1 = vm.McpServer.HandleJsonRpcMessageAsync(req);
            await Task.Delay(50);

            Assert.True(tab.HasPendingAiConfirmation);
            Assert.Contains("sudo systemctl restart nginx", tab.PendingAiCommandPreview);
            Assert.False(executed);

            tab.RejectAiActionCommand.Execute(null);
            var resp1 = await callTask1;
            var json1 = JsonNode.Parse(resp1!) as JsonObject;
            Assert.True(json1?["result"]?["isError"]?.GetValue<bool>());
            Assert.False(executed);
            Assert.False(tab.HasPendingAiConfirmation);

            // 2. Test Approval
            var callTask2 = vm.McpServer.HandleJsonRpcMessageAsync(req);
            await Task.Delay(50);

            Assert.True(tab.HasPendingAiConfirmation);
            tab.ApproveAiActionCommand.Execute(null);

            var resp2 = await callTask2;
            var json2 = JsonNode.Parse(resp2!) as JsonObject;
            Assert.False(json2?["result"]?["isError"]?.GetValue<bool>());
            Assert.True(executed);
            Assert.False(tab.HasPendingAiConfirmation);
        }

        [Fact]
        public async Task McpServer_ReadTerminalOutput_ShouldWorkWithoutConfirmationInReadAndConfirmMode()
        {
            var vm = new MainViewModel();
            var tab = vm.OpenTerminalTab("srv.example.com", 22, "debian", "pwd", "/var/log");
            var termSession = new SshTerminalSession("srv.example.com", 22, "debian", "pwd")
            {
                CustomCommandExecutor = (cmd, timeout, ct) => Task.FromResult((true, (int?)0, "", (string?)null))
            };
            termSession.AppendToScreenBuffer("line 1: server started\r\nline 2: listening on port 8080\r\n");
            tab.TerminalSession = termSession;
            tab.AiPermission = AiPermissionLevel.ReadAndConfirm;

            var req = """
            {
              "jsonrpc": "2.0",
              "id": 13,
              "method": "tools/call",
              "params": {
                "name": "read_terminal_output",
                "arguments": { "max_lines": 10 }
              }
            }
            """;

            var resp = await vm.McpServer.HandleJsonRpcMessageAsync(req);
            var json = JsonNode.Parse(resp!) as JsonObject;
            var isError = json?["result"]?["isError"]?.GetValue<bool>() ?? true;
            var text = json?["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? "";

            Assert.False(isError);
            Assert.Contains("listening on port 8080", text);
        }

        [Fact]
        public void SshTerminalSession_SentinelMarkerFiltering_ShouldHideInternalMarkersFromDisplay()
        {
            var raw = "ls -la; printf \"\\n__RZ_DONE_a1b2c3d4_%d_%s__\\n\" \"$?\" \"$(pwd)\"\r\ntotal 8\r\ndrwxr-xr-x 2 root root 4096 Nov 13 .\r\n\n__RZ_DONE_a1b2c3d4_0_/var/www__\n";
            var display = SshTerminalSession.CleanInternalMcpMarkersForDisplay(raw);

            Assert.DoesNotContain("__RZ_DONE_", display);
            Assert.DoesNotContain("printf", display);
            Assert.Contains("total 8", display);

            var regex = new Regex(@"__RZ_DONE_a1b2c3d4_(-?\d+)_([^\r\n]*?)__");
            var cleanOut = SshTerminalSession.ExtractCleanCommandOutput(raw, "ls -la", regex);
            Assert.DoesNotContain("__RZ_DONE_", cleanOut);
            Assert.Contains("total 8", cleanOut);
        }

        [Fact]
        public async Task McpServer_NamedPipeTransport_ShouldHandleClientRequestsEndToEnd()
        {
            var pipeName = $"RsyncZilla_TestPipe_{Guid.NewGuid():N}";
            var session = new RemoteSessionViewModel
            {
                IsTerminalTab = true,
                Host = "node1.internal",
                Username = "ubuntu",
                AiPermission = AiPermissionLevel.Auto
            };

            using var server = new McpServerService(
                () => new[] { session },
                () => session,
                pipeName: pipeName);

            server.Start();
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(2000);

                var utf8 = new UTF8Encoding(false);
                using var writer = new StreamWriter(client, utf8, 4096, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, utf8, false, 4096, leaveOpen: true);

                await writer.WriteLineAsync("""{"jsonrpc":"2.0","id":99,"method":"tools/call","params":{"name":"list_terminal_sessions","arguments":{}}}""");
                var responseLine = await reader.ReadLineAsync();

                Assert.NotNull(responseLine);
                Assert.Contains("ubuntu@node1.internal", responseLine);
                Assert.Contains("Auto", responseLine);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task McpServer_MultipleTabs_CanTargetSpecificTabByMcpName()
        {
            var vm = new MainViewModel();
            var tab1 = vm.OpenTerminalTab("srv1.example.com", 22, "root", "pwd", "/srv1");
            var tab2 = vm.OpenTerminalTab("srv2.example.com", 22, "root", "pwd", "/srv2");

            Assert.Equal("MCP1", tab1.McpName);
            Assert.Equal("MCP2", tab2.McpName);

            tab1.AiPermission = AiPermissionLevel.Auto;
            tab2.AiPermission = AiPermissionLevel.Auto;

            tab1.TerminalSession = new SshTerminalSession("srv1.example.com", 22, "root", "pwd")
            {
                CustomCommandExecutor = (cmd, timeout, ct) => Task.FromResult((true, (int?)0, $"from_tab1:{cmd}", (string?)"/srv1"))
            };
            tab2.TerminalSession = new SshTerminalSession("srv2.example.com", 22, "root", "pwd")
            {
                CustomCommandExecutor = (cmd, timeout, ct) => Task.FromResult((true, (int?)0, $"from_tab2:{cmd}", (string?)"/srv2"))
            };

            // Target MCP1 explicitly even though tab2 is currently active
            var resp1 = await vm.McpServer.HandleJsonRpcMessageAsync(
                """{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"execute_terminal_command","arguments":{"command":"hostname","session_id":"MCP1"}}}""");
            Assert.Contains("from_tab1:hostname", resp1);

            // Target MCP2 using lowercase or number
            var resp2 = await vm.McpServer.HandleJsonRpcMessageAsync(
                """{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"execute_terminal_command","arguments":{"command":"hostname","session_id":"mcp2"}}}""");
            Assert.Contains("from_tab2:hostname", resp2);
        }
    }
}
