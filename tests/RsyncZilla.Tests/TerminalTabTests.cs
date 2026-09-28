using System;
using System.IO;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;
using Xunit;

namespace RsyncZilla.Tests
{
    public class TerminalTabTests
    {
        [Fact]
        public void RemoteSessionViewModel_IsTerminalTab_SetsProperTitle()
        {
            var session = new RemoteSessionViewModel
            {
                Host = "myserver.com",
                Username = "root",
                Port = 22,
                IsTerminalTab = true
            };

            Assert.True(session.IsTerminalTab);
            Assert.Equal("Terminal: root@myserver.com", session.Title);

            session.Username = "";
            Assert.Equal("Terminal: myserver.com", session.Title);

            session.Host = "";
            Assert.Equal("Terminal", session.Title);
        }

        [Fact]
        public void RemoteSessionViewModel_NonTerminalTab_KeepsOriginalTitleFormat()
        {
            var session = new RemoteSessionViewModel
            {
                Host = "myserver.com",
                Username = "developer",
                Port = 22,
                IsTerminalTab = false
            };

            Assert.False(session.IsTerminalTab);
            Assert.Equal("developer@myserver.com", session.Title);
        }

        [Fact]
        public void SshTerminalSession_DefaultValuesAndSafeDisconnectedMethods()
        {
            using var session = new SshTerminalSession("myserver.com", 0, "root", "secret", "/var/www");

            Assert.Equal("myserver.com", session.Host);
            Assert.Equal(22, session.Port);
            Assert.Equal("root", session.Username);
            Assert.Equal("/var/www", session.InitialPath);
            Assert.False(session.IsConnected);

            // Calling SendInput or ChangeSize when not connected should be completely safe and not throw
            session.SendInput("ls -la\n");
            session.ChangeSize(120, 40);
        }

        [Fact]
        public void MainViewModel_OpenTerminalTab_CreatesActiveTerminalSession()
        {
            var vm = new MainViewModel();
            var initialCount = vm.RemoteSessions.Count;

            var termSession = vm.OpenTerminalTab("test.server.com", 2222, "admin", "pwd123", "/etc/nginx", "My Site");

            Assert.NotNull(termSession);
            Assert.True(termSession.IsTerminalTab);
            Assert.Equal("test.server.com", termSession.Host);
            Assert.Equal(2222, termSession.Port);
            Assert.Equal("admin", termSession.Username);
            Assert.Equal("/etc/nginx", termSession.InitialTerminalPath);
            Assert.Equal("My Site", termSession.SiteName);
            Assert.Equal(initialCount + 1, vm.RemoteSessions.Count);
            Assert.Same(termSession, vm.ActiveSession);
            Assert.Equal("Terminal: admin@test.server.com", termSession.Title);

            // Now close the tab
            vm.CloseTab(termSession);
            Assert.Equal(initialCount, vm.RemoteSessions.Count);
            Assert.NotSame(termSession, vm.ActiveSession);
        }

        [Fact]
        public void MainViewModel_CloseTab_OnSingleRemainingTab_ResetsTerminalFlag()
        {
            var vm = new MainViewModel();
            Assert.Single(vm.RemoteSessions);

            var session = vm.RemoteSessions[0];
            session.IsTerminalTab = true;
            session.Host = "temp.com";
            session.Username = "user";

            vm.CloseTab(session);

            Assert.Single(vm.RemoteSessions);
            Assert.False(session.IsTerminalTab);
            Assert.Equal("Disconnected", session.StatusText);
            Assert.Equal("New Connection", session.Title);
        }

        [Fact]
        public void MainViewModel_Disconnect_OnTerminalTab_ClosesTab()
        {
            var vm = new MainViewModel();
            var initialCount = vm.RemoteSessions.Count;

            var termSession = vm.OpenTerminalTab("test.server.com", 22, "admin", "pwd123", "/var/www");
            Assert.Equal(initialCount + 1, vm.RemoteSessions.Count);
            Assert.Same(termSession, vm.ActiveSession);
            Assert.True(vm.ActiveSession.IsTerminalTab);
            Assert.Equal("Disconnect", vm.ConnectionButtonText);

            // Execute ConnectCommand (the Disconnect button on the QuickConnect bar)
            vm.ConnectCommand.Execute(null);

            Assert.Equal(initialCount, vm.RemoteSessions.Count);
            Assert.NotSame(termSession, vm.ActiveSession);
            Assert.False(vm.ActiveSession.IsTerminalTab);
        }

        [Fact]
        public void MainViewModel_DisconnectCommand_OnTerminalTab_ClosesTab()
        {
            var vm = new MainViewModel();
            var initialCount = vm.RemoteSessions.Count;

            var termSession = vm.OpenTerminalTab("test.server.com", 22, "admin", "pwd123", "/var/www");
            Assert.Equal(initialCount + 1, vm.RemoteSessions.Count);
            Assert.Same(termSession, vm.ActiveSession);

            Assert.True(vm.DisconnectCommand.CanExecute(null));
            vm.DisconnectCommand.Execute(null);

            Assert.Equal(initialCount, vm.RemoteSessions.Count);
            Assert.NotSame(termSession, vm.ActiveSession);
        }

        [Theory]
        [InlineData("~", "/home/debian", "/var/www", "/home/debian")]
        [InlineData("~/projects/app", "/home/debian", "/var/www", "/home/debian/projects/app")]
        [InlineData("~/my folder", "/home/user", "/var/www", "/home/user/my folder")]
        [InlineData("/var/www/html", "/home/debian", "/home/debian", "/var/www/html")]
        [InlineData("  /etc/nginx/sites-available  ", "/root", "/var/www", "/etc/nginx/sites-available")]
        [InlineData("config", "/home/debian", "/var/www", "/var/www/config")]
        [InlineData("sub/folder", "/home/debian", "/etc", "/etc/sub/folder")]
        public void TerminalView_ResolveRemotePath_ResolvesProperly(string input, string home, string current, string expected)
        {
            var resolved = RsyncZilla.Views.TerminalView.ResolveRemotePath(input, home, current);
            Assert.Equal(expected, resolved);
        }

        [Fact]
        public void SshDiagnostics_Analyze_AuthenticationFailure_IdentifiesAsServerError()
        {
            var ex = new Renci.SshNet.Common.SshAuthenticationException("Permission denied (password).");
            var result = SshDiagnostics.Analyze(ex, "test.server.com", 22, "debian");

            Assert.True(result.IsServerError);
            Assert.Equal("Authentication", result.Category);
            Assert.Contains("rejected", result.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sshd_config", result.Diagnosis);
        }

        [Fact]
        public void SshDiagnostics_Analyze_ConnectionRefused_IdentifiesAsServerError()
        {
            var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused);
            var ex = new Exception("Connection error", socketEx);
            var result = SshDiagnostics.Analyze(ex, "test.server.com", 22, "debian");

            Assert.True(result.IsServerError);
            Assert.Equal("Connection Refused", result.Category);
            Assert.Contains("refused", result.Summary, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SshDiagnostics_Analyze_HostNotFound_IdentifiesAsClientError()
        {
            var socketEx = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
            var ex = new Exception("Name resolution failure", socketEx);
            var result = SshDiagnostics.Analyze(ex, "invalid.domain.unknown", 22, "debian");

            Assert.False(result.IsServerError);
            Assert.Equal("DNS Resolution", result.Category);
            Assert.Contains("resolved", result.Summary, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SshDiagnostics_Analyze_PtyChannelRequestFailed_IdentifiesPtyIssue()
        {
            var ex = new Renci.SshNet.Common.SshException("Channel request failed: pty-req rejected");
            var result = SshDiagnostics.Analyze(ex, "test.server.com", 22, "debian");

            Assert.True(result.IsServerError);
            Assert.Equal("PTY / Shell Allocation", result.Category);
            Assert.Contains("shell", result.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("MaxSessions", result.Diagnosis);
        }

        [Fact]
        public void SshDiagnosticResult_FormatForTerminal_ProducesAnsiFormattedOutput()
        {
            var result = new SshDiagnosticResult
            {
                IsServerError = true,
                Category = "Authentication",
                Summary = "Authentication failed",
                Diagnosis = "Password was incorrect",
                RawMessage = "Permission denied",
                SuggestedAction = "Check password"
            };

            var formatted = result.FormatForTerminal();
            Assert.Contains("[SSH Connection Failed]", formatted);
            Assert.Contains("Remote Server", formatted);
            Assert.Contains("Authentication", formatted);
            Assert.Contains("Password was incorrect", formatted);
            Assert.Contains("Check password", formatted);
        }

        [Theory]
        [InlineData("debian@sumalab:~$ ", "~")]
        [InlineData("debian@sumalab:/etc$ ", "/etc")]
        [InlineData("root@sumalab:/var/log/nginx# ", "/var/log/nginx")]
        [InlineData("\r\n\x1b[01;32mdebian@sumalab\x1b[00m:\x1b[01;34m/var/www\x1b[00m$ ", "/var/www")]
        [InlineData("[debian@sumalab ~]$ ", "~")]
        [InlineData("[root@sumalab /etc/systemd]# ", "/etc/systemd")]
        [InlineData("debian@sumalab:~/My Documents$ ", "~/My Documents")]
        [InlineData("user@host:/$ ", "/")]
        [InlineData("/var/log $ ", "/var/log")]
        public void TerminalView_TryExtractDirectoryFromPrompt_ExtractsExpectedPath(string promptText, string expected)
        {
            var detected = RsyncZilla.Views.TerminalView.TryExtractDirectoryFromPrompt(promptText);
            Assert.Equal(expected, detected);
        }

        [Theory]
        [InlineData("debian@sumalab:~$ cd /etc")]
        [InlineData("total 48\r\ndrwxr-xr-x 2 root root 4096")]
        [InlineData("hello world")]
        [InlineData("")]
        [InlineData(null)]
        public void TerminalView_TryExtractDirectoryFromPrompt_IgnoresNonPrompts(string? input)
        {
            var detected = RsyncZilla.Views.TerminalView.TryExtractDirectoryFromPrompt(input!);
            Assert.Null(detected);
        }
    }
}
