using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RsyncZilla.Services
{
    public class TerminalService
    {
        private readonly RsyncService _rsyncService;

        public TerminalService(RsyncService? rsyncService = null)
        {
            _rsyncService = rsyncService ?? new RsyncService();
        }

        public string? FindKittyBinary()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "tools", "kitty.exe"),
                Path.Combine(baseDir, "kitty.exe"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "RsyncZilla", "tools", "kitty.exe")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "tools", "kitty.exe"))
            };

            foreach (var p in candidates)
            {
                if (File.Exists(p)) return p;
            }

            return null;
        }

        public static string BuildKittyArguments(string host, int port, string username, string? password, string? remotePath)
        {
            var portArg = port > 0 && port != 22 ? $"-P {port}" : "";
            var target = $"{username}@{host}";
            var pwArg = !string.IsNullOrEmpty(password) ? $"-pw \"{password.Replace("\"", "\\\"")}\"" : "";
            var cmdArg = !string.IsNullOrWhiteSpace(remotePath) && remotePath != "." && remotePath != "~"
                ? $"-cmd \"cd '{remotePath.Trim().Replace("'", "'\\''")}'\\n\""
                : "";

            var parts = new[] { "-ssh", target, portArg, pwArg, cmdArg }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            return string.Join(" ", parts);
        }

        public string FindSshBinary()
        {
            // 1. Prefer native Windows OpenSSH if installed
            var winSsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
            if (File.Exists(winSsh)) return winSsh;

            var sys32Ssh = @"C:\Windows\System32\OpenSSH\ssh.exe";
            if (File.Exists(sys32Ssh)) return sys32Ssh;

            // 2. Bundled ssh from rsync/cygwin tools
            return _rsyncService.FindSshBinary();
        }

        public string FindAskPassBinary()
        {
            return _rsyncService.FindAskPassBinary();
        }

        public static string BuildSshArguments(string host, int port, string username, string? remotePath)
        {
            var portArg = port > 0 && port != 22 ? $"-p {port} " : "";
            var target = $"{username}@{host}";

            if (string.IsNullOrWhiteSpace(remotePath) || remotePath == "." || remotePath == "~")
            {
                return $"{portArg}{target}";
            }

            // Clean path and escape single quotes for POSIX bash/sh
            var cleanPath = remotePath.Trim().Replace("'", "'\\''");

            return $"-t {portArg}{target} \"cd '{cleanPath}' 2>/dev/null ; [ -n \\\"$SHELL\\\" ] && exec \\\"$SHELL\\\" -l || exec bash -l\"";
        }

        public (string FileName, string Arguments) BuildTerminalCommand(string host, int port, string username, string? remotePath, string? title = null)
        {
            var sshExe = FindSshBinary();
            var tabTitle = !string.IsNullOrWhiteSpace(title)
                ? title
                : (!string.IsNullOrWhiteSpace(remotePath) ? $"{username}@{host}:{remotePath}" : $"{username}@{host}");

            var sshArgs = BuildSshArguments(host, port, username, remotePath);

            // Universal fallback launcher for Windows Console (cmd.exe)
            var scriptPath = GenerateLauncherScript(tabTitle, sshExe, sshArgs);
            return ("cmd.exe", $"/c \"\"{scriptPath}\"\"");
        }

        public string GenerateLauncherScript(string title, string sshExe, string sshArgs)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "RsyncZilla");
            Directory.CreateDirectory(tempDir);
            var scriptPath = Path.Combine(tempDir, "open_terminal.cmd");

            var script =
                "@echo off\r\n" +
                $"title {title}\r\n" +
                "cls\r\n" +
                $"\"{sshExe}\" {sshArgs}\r\n" +
                "if errorlevel 1 (\r\n" +
                "    echo.\r\n" +
                "    echo ========================================================\r\n" +
                "    echo [RsyncZilla Terminal] SSH connection ended with code %ERRORLEVEL%.\r\n" +
                "    echo ========================================================\r\n" +
                "    pause\r\n" +
                ")\r\n";

            File.WriteAllText(scriptPath, script);
            return scriptPath;
        }

        public bool OpenTerminal(string host, int port, string username, string? password, string? remotePath, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
            {
                errorMessage = "Host and Username are required to open a terminal.";
                return false;
            }

            try
            {
                // Priority 1: KiTTY (bundled in tools/kitty.exe)
                var kittyExe = FindKittyBinary();
                if (!string.IsNullOrEmpty(kittyExe) && File.Exists(kittyExe))
                {
                    var kittyArgs = BuildKittyArguments(host, port, username, password, remotePath);
                    var kittyPsi = new ProcessStartInfo
                    {
                        FileName = kittyExe,
                        Arguments = kittyArgs,
                        UseShellExecute = false
                    };
                    Process.Start(kittyPsi);
                    return true;
                }

                // Priority 2: Fallback to cmd.exe launcher script
                var (fileName, args) = BuildTerminalCommand(host, port, username, remotePath);

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                if (!string.IsNullOrEmpty(password))
                {
                    var askPass = FindAskPassBinary();
                    if (File.Exists(askPass))
                    {
                        psi.EnvironmentVariables["SSH_ASKPASS"] = askPass;
                        psi.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "force";
                        psi.EnvironmentVariables["RSYNC_PASSWORD"] = password;
                        psi.EnvironmentVariables["DISPLAY"] = ":0";
                    }
                }

                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
