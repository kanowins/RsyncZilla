using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RsyncZilla.Models;

namespace RsyncZilla.Services
{
    public enum RsyncFailureReason
    {
        PermissionDenied,
        AuthenticationFailed,
        ConnectionError,
        Other
    }

    public class RsyncService
    {
        private static readonly Regex ProgressRegex = new Regex(
            @"\s*([0-9,]+)\s+([0-9]{1,3})%\s+([0-9\.]+[kMG]?B/s)\s+([0-9:]+)",
            RegexOptions.Compiled
        );

        public event Action<string, bool>? LogMessageReceived; // (message, isError)

        public string FindRsyncBinary()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "tools", "cygwin64", "rsync.exe"),
                Path.Combine(baseDir, "cygwin64", "rsync.exe"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "RsyncZilla", "tools", "cygwin64", "rsync.exe")),
                Path.Combine(baseDir, "rsync.exe")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return "rsync.exe"; // Fallback to PATH
        }

        public string FindSshBinary()
        {
            var rsyncPath = FindRsyncBinary();
            if (File.Exists(rsyncPath))
            {
                var dir = Path.GetDirectoryName(rsyncPath);
                var sshPath = Path.Combine(dir ?? "", "ssh.exe");
                if (File.Exists(sshPath)) return sshPath;
            }

            return "ssh.exe";
        }

        public string FindAskPassBinary()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "RsyncAskPass.exe"),
                Path.Combine(baseDir, "tools", "RsyncAskPass.exe"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "RsyncAskPass", "bin", "Release", "net8.0", "win-x64", "RsyncAskPass.exe")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "RsyncAskPass", "bin", "Debug", "net8.0", "RsyncAskPass.exe"))
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path)) return path;
            }

            return Path.Combine(baseDir, "RsyncAskPass.exe");
        }

        public static string ToCygwinPath(string windowsPath)
        {
            if (string.IsNullOrWhiteSpace(windowsPath)) return windowsPath;
            var fullPath = Path.GetFullPath(windowsPath).Replace('\\', '/');
            if (fullPath.Length >= 2 && fullPath[1] == ':')
            {
                char drive = char.ToLowerInvariant(fullPath[0]);
                string rest = fullPath.Substring(2);
                return $"/cygdrive/{drive}{rest}";
            }
            return fullPath;
        }

        public static RsyncFailureReason ClassifyError(int exitCode, string outputAndError)
        {
            var text = outputAndError ?? "";

            // 1. Permission Denied (File system permissions)
            if (text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("read-only file system", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            {
                return RsyncFailureReason.PermissionDenied;
            }

            // 2. Authentication failure
            if (text.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Host key verification failed", StringComparison.OrdinalIgnoreCase))
            {
                return RsyncFailureReason.AuthenticationFailed;
            }

            // 3. Network / Connection errors (Transient, should retry)
            if (exitCode == 10 || exitCode == 12 || exitCode == 30 || exitCode == 35 || exitCode == 255 ||
                text.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Connection reset", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Connection timed out", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Broken pipe", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Host is down", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("safe_read failed", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("safe_write failed", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("No route to host", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Could not resolve hostname", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase))
            {
                return RsyncFailureReason.ConnectionError;
            }

            return RsyncFailureReason.Other;
        }

        public async Task<bool> ExecuteTransferAsync(
            TransferTask task,
            ConnectionProfile connection,
            CancellationToken cancellationToken = default)
        {
            const int maxRetries = 3;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                cancellationToken.ThrowIfCancellationRequested();

                var (success, reason, errorDetail) = await RunSingleTransferAttemptAsync(task, connection, cancellationToken);

                if (success)
                {
                    return true;
                }

                // If user cancelled, don't retry
                if (cancellationToken.IsCancellationRequested || task.Status == TransferStatus.Cancelled)
                {
                    return false;
                }

                // If error is PERMISSION DENIED or AUTHENTICATION FAILED, do NOT retry!
                if (reason == RsyncFailureReason.PermissionDenied)
                {
                    task.ErrorMessage = $"Permission error: cannot write or read file at destination. {errorDetail}";
                    LogMessageReceived?.Invoke($"[rsync] 🚫 Permanent permission error on '{task.FileName}'. Will not retry.", true);
                    return false;
                }

                if (reason == RsyncFailureReason.AuthenticationFailed)
                {
                    task.ErrorMessage = $"SSH authentication error. Please verify credentials. {errorDetail}";
                    LogMessageReceived?.Invoke($"[rsync] 🚫 Authentication failure on '{task.FileName}'. Will not retry.", true);
                    return false;
                }

                // If it's a CONNECTION ERROR and we have attempts left: retry!
                if (attempt < maxRetries)
                {
                    LogMessageReceived?.Invoke($"[rsync] ⚠️ Connection failure on '{task.FileName}'. Automatically retrying ({attempt}/{maxRetries}) in 2 seconds...", true);
                    task.ErrorMessage = $"Connection failure. Retrying ({attempt}/{maxRetries})...";
                    try
                    {
                        await Task.Delay(2000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                }
                else
                {
                    task.ErrorMessage = $"Connection failure after {maxRetries} attempts: {errorDetail}";
                    LogMessageReceived?.Invoke($"[rsync] ❌ Exhausted {maxRetries} connection retries for '{task.FileName}'.", true);
                    return false;
                }
            }

            return false;
        }

        private async Task<(bool success, RsyncFailureReason reason, string errorDetail)> RunSingleTransferAttemptAsync(
            TransferTask task,
            ConnectionProfile connection,
            CancellationToken cancellationToken)
        {
            var rsyncPath = FindRsyncBinary();
            if (!File.Exists(rsyncPath))
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = $"rsync.exe not found at: {rsyncPath}";
                return (false, RsyncFailureReason.Other, task.ErrorMessage);
            }

            task.Status = TransferStatus.Running;
            task.StartTime = DateTime.Now;

            var rsyncCygwinDir = Path.GetDirectoryName(rsyncPath) ?? "";
            var askPassBinary = FindAskPassBinary();
            var askPassCygwinPath = ToCygwinPath(askPassBinary);

            // Clean SSH command without nested double quote conflicts
            string sshCommand = $"ssh -p {connection.Port} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=15 -o ServerAliveInterval=10 -o ServerAliveCountMax=3 -o LogLevel=ERROR";

            string sourceArg;
            string destArg;

            if (task.Direction == TransferDirection.Upload)
            {
                sourceArg = ToCygwinPath(task.SourcePath);
                var remoteDest = task.DestinationPath.EndsWith("/") ? task.DestinationPath : task.DestinationPath + "/";
                destArg = $"{connection.Username}@{connection.Host}:{remoteDest}";
            }
            else
            {
                sourceArg = $"{connection.Username}@{connection.Host}:{task.SourcePath}";
                var localDest = ToCygwinPath(task.DestinationPath);
                if (!localDest.EndsWith("/")) localDest += "/";
                destArg = localDest;
            }

            // Args with -s (protect-args) to prevent remote shell splitting filenames with spaces
            var args = $"-avzP -s --stats --update -e \"{sshCommand}\" \"{sourceArg}\" \"{destArg}\"";

            LogMessageReceived?.Invoke($"[rsync] Transferring: {task.FileName}", false);

            var startInfo = new ProcessStartInfo
            {
                FileName = rsyncPath,
                Arguments = args,
                WorkingDirectory = rsyncCygwinDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Set up environment variables
            startInfo.EnvironmentVariables["RSYNC_PASSWORD"] = connection.Password;
            startInfo.EnvironmentVariables["SSH_ASKPASS"] = askPassCygwinPath;
            startInfo.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "force";
            startInfo.EnvironmentVariables["DISPLAY"] = "dummy:0";

            var currentPath = startInfo.EnvironmentVariables["PATH"] ?? "";
            startInfo.EnvironmentVariables["PATH"] = $"{rsyncCygwinDir};{currentPath}";

            var errorOutput = new StringBuilder();

            try
            {
                using var process = new Process { StartInfo = startInfo };

                process.OutputDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    var line = e.Data.Trim();
                    LogMessageReceived?.Invoke($"[rsync] {line}", false);

                    var match = ProgressRegex.Match(line);
                    if (match.Success)
                    {
                        var bytesStr = match.Groups[1].Value;
                        if (int.TryParse(match.Groups[2].Value, out int percent))
                        {
                            task.ProgressPercentage = percent;
                        }
                        task.Speed = match.Groups[3].Value;
                        task.Eta = match.Groups[4].Value;
                        task.TransferredInfo = $"{bytesStr} bytes ({task.Speed}, {task.Eta} remaining)";
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorOutput.AppendLine(e.Data);
                        LogMessageReceived?.Invoke($"[rsync stderr] {e.Data}", true);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                            task.Status = TransferStatus.Cancelled;
                        }
                    }
                    catch { }
                }))
                {
                    await process.WaitForExitAsync();
                }

                task.ExitCode = process.ExitCode;
                task.EndTime = DateTime.Now;

                if (cancellationToken.IsCancellationRequested || task.Status == TransferStatus.Cancelled)
                {
                    task.Status = TransferStatus.Cancelled;
                    task.ErrorMessage = "Transfer cancelled by user.";
                    return (false, RsyncFailureReason.Other, task.ErrorMessage);
                }

                if (process.ExitCode == 0)
                {
                    task.Status = TransferStatus.Completed;
                    task.ProgressPercentage = 100;
                    task.Eta = "0:00:00";
                    LogMessageReceived?.Invoke($"[rsync] ✅ {task.FileName} transferred successfully (ExitCode: 0)", false);
                    return (true, RsyncFailureReason.Other, "");
                }
                else
                {
                    task.Status = TransferStatus.Failed;
                    var err = errorOutput.ToString().Trim();
                    task.ErrorMessage = string.IsNullOrEmpty(err) ? $"rsync returned exit code {process.ExitCode}" : err;
                    var reason = ClassifyError(process.ExitCode, err);
                    return (false, reason, task.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = ex.Message;
                var reason = ClassifyError(-1, ex.Message);
                return (false, reason, ex.Message);
            }
        }
    }
}
