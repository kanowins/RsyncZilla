using System;
using System.IO;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;
using Xunit;

namespace RsyncZilla.Tests
{
    public class TerminalServiceTests
    {
        [Fact]
        public void BuildSshArguments_StandardPath_ShouldIncludeCdAndExecShell()
        {
            var args = TerminalService.BuildSshArguments("myserver.com", 22, "debian", "/var/www/html");

            Assert.Contains("-t debian@myserver.com", args);
            Assert.Contains("cd '/var/www/html'", args);
            Assert.Contains("exec bash -l", args);
        }

        [Fact]
        public void BuildSshArguments_CustomPortAndSpacesInPath_ShouldIncludePortAndEscapedPath()
        {
            var args = TerminalService.BuildSshArguments("myserver.com", 2222, "admin", "/home/admin/my folder");

            Assert.Contains("-t -p 2222 admin@myserver.com", args);
            Assert.Contains("cd '/home/admin/my folder'", args);
        }

        [Fact]
        public void BuildSshArguments_PathWithSingleQuotes_ShouldSafelyEscapeSingleQuotes()
        {
            var args = TerminalService.BuildSshArguments("myserver.com", 22, "root", "/var/user's data");

            Assert.Contains("cd '/var/user'\\''s data'", args);
        }

        [Fact]
        public void BuildSshArguments_EmptyOrRootPath_ShouldLaunchInteractiveShell()
        {
            var argsEmpty = TerminalService.BuildSshArguments("myserver.com", 22, "debian", "");
            Assert.Equal("debian@myserver.com", argsEmpty);

            var argsDot = TerminalService.BuildSshArguments("myserver.com", 22, "debian", ".");
            Assert.Equal("debian@myserver.com", argsDot);

            var argsTilde = TerminalService.BuildSshArguments("myserver.com", 22, "debian", "~");
            Assert.Equal("debian@myserver.com", argsTilde);
        }

        [Fact]
        public void BuildTerminalCommand_ShouldGenerateValidTerminalArguments()
        {
            var service = new TerminalService();
            var (fileName, arguments) = service.BuildTerminalCommand("example.com", 22, "user1", "/home/user1");

            Assert.Equal("cmd.exe", fileName);
            Assert.Contains("open_terminal.cmd", arguments);

            var scriptPath = Path.Combine(System.IO.Path.GetTempPath(), "RsyncZilla", "open_terminal.cmd");
            Assert.True(System.IO.File.Exists(scriptPath));
            var content = System.IO.File.ReadAllText(scriptPath);
            Assert.Contains("user1@example.com", content);
            Assert.Contains("/home/user1", content);
        }

        [Fact]
        public void OpenRemoteTerminalCommand_ShouldRequireConnection()
        {
            var vm = new MainViewModel();

            // When disconnected, OpenRemoteTerminalCommand should not be executable
            Assert.False(vm.IsConnected);
            Assert.False(vm.OpenRemoteTerminalCommand.CanExecute(null));
        }

        [Fact]
        public void ProcessStart_CmdWithUseShellExecuteFalse_ShouldStartSuccessfully()
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c exit 0",
                UseShellExecute = false,
                CreateNoWindow = false
            };
            var p = System.Diagnostics.Process.Start(psi);
            Assert.NotNull(p);
            p.WaitForExit(3000);
            Assert.Equal(0, p.ExitCode);
        }

        [Fact]
        public void BuildKittyArguments_WithPasswordAndPath_ShouldGenerateCorrectOptions()
        {
            var args = TerminalService.BuildKittyArguments("myserver.com", 2222, "debian", "pass123", "/var/www/html");

            Assert.Contains("-ssh debian@myserver.com", args);
            Assert.Contains("-P 2222", args);
            Assert.Contains("-pw \"pass123\"", args);
            Assert.Contains("-cmd \"cd '/var/www/html'\\n\"", args);
        }

        [Fact]
        public void FindKittyBinary_ShouldFindBundledExecutable()
        {
            var service = new TerminalService();
            var kittyPath = service.FindKittyBinary();

            Assert.NotNull(kittyPath);
            Assert.True(File.Exists(kittyPath));
        }
    }
}
