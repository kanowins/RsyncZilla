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

        public static bool IsFailedToSetTimesError(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            return line.Contains("failed to set times", StringComparison.OrdinalIgnoreCase);
        }

        public static RsyncFailureReason ClassifyError(int exitCode, string outputAndError)
        {
            var text = outputAndError ?? "";

            // Filter out lines that are only timestamp warnings (e.g. "failed to set times on ...: Operation not permitted")
            // so they don't cause false positive PermissionDenied classification.
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(l => !IsFailedToSetTimesError(l));
            var filteredText = string.Join("\n", lines);

            // 1. Permission Denied (File system permissions)
            if (filteredText.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                filteredText.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ||
                filteredText.Contains("read-only file system", StringComparison.OrdinalIgnoreCase) ||
                filteredText.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
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
            return await ExecuteBatchTransferAsync(new[] { task }, connection, cancellationToken);
        }

        public async Task<bool> ExecuteBatchTransferAsync(
            IReadOnlyList<TransferTask> tasks,
            ConnectionProfile connection,
            CancellationToken cancellationToken = default)
        {
            if (tasks == null || tasks.Count == 0) return true;

            const int maxRetries = 3;
            int attempt = 0;

            var currentBatch = tasks.ToList();

            while (attempt < maxRetries)
            {
                attempt++;
                cancellationToken.ThrowIfCancellationRequested();

                var (success, reason, errorDetail) = await RunSingleBatchAttemptAsync(currentBatch, connection, cancellationToken);

                // Any files that completed successfully are done
                var uncompleted = currentBatch.Where(t => t.Status != TransferStatus.Completed).ToList();
                if (!uncompleted.Any())
                {
                    return true;
                }

                // If user cancelled, don't retry
                if (cancellationToken.IsCancellationRequested || uncompleted.Any(t => t.Status == TransferStatus.Cancelled))
                {
                    return false;
                }

                // If error is PERMISSION DENIED or AUTHENTICATION FAILED, do NOT retry!
                if (reason == RsyncFailureReason.PermissionDenied)
                {
                    foreach (var t in uncompleted.Where(t => string.IsNullOrEmpty(t.ErrorMessage)))
                    {
                        t.ErrorMessage = $"Permission error: cannot write or read file at destination. {errorDetail}";
                    }
                    LogMessageReceived?.Invoke($"[rsync] 🚫 Permanent permission error on batch. Will not retry.", true);
                    return false;
                }

                if (reason == RsyncFailureReason.AuthenticationFailed)
                {
                    foreach (var t in uncompleted)
                    {
                        t.ErrorMessage = $"SSH authentication error. Please verify credentials. {errorDetail}";
                    }
                    LogMessageReceived?.Invoke($"[rsync] 🚫 Authentication failure on batch. Will not retry.", true);
                    return false;
                }

                // If some files failed individually (e.g. exit code 23 partial errors), but it was NOT a connection drop
                if (reason != RsyncFailureReason.ConnectionError)
                {
                    return false;
                }

                // If it's a CONNECTION ERROR and we have attempts left: retry remaining files!
                if (attempt < maxRetries)
                {
                    LogMessageReceived?.Invoke($"[rsync] ⚠️ Connection failure on batch. Automatically retrying ({attempt}/{maxRetries}) in 2 seconds...", true);
                    foreach (var t in uncompleted)
                    {
                        t.ErrorMessage = $"Connection failure. Retrying ({attempt}/{maxRetries})...";
                    }
                    try
                    {
                        await Task.Delay(2000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                    currentBatch = uncompleted;
                }
                else
                {
                    foreach (var t in uncompleted)
                    {
                        if (string.IsNullOrEmpty(t.ErrorMessage))
                            t.ErrorMessage = $"Connection failure after {maxRetries} attempts: {errorDetail}";
                    }
                    LogMessageReceived?.Invoke($"[rsync] ❌ Exhausted {maxRetries} connection retries for batch.", true);
                    return false;
                }
            }

            return false;
        }

        private async Task<(bool success, RsyncFailureReason reason, string errorDetail)> RunSingleBatchAttemptAsync(
            IReadOnlyList<TransferTask> tasks,
            ConnectionProfile connection,
            CancellationToken cancellationToken)
        {
            var rsyncPath = FindRsyncBinary();
            if (!File.Exists(rsyncPath))
            {
                foreach (var t in tasks)
                {
                    t.Status = TransferStatus.Failed;
                    t.ErrorMessage = $"rsync.exe not found at: {rsyncPath}";
                }
                return (false, RsyncFailureReason.Other, $"rsync.exe not found at: {rsyncPath}");
            }

            foreach (var t in tasks)
            {
                t.Status = TransferStatus.Running;
                t.StartTime = DateTime.Now;
            }

            var rsyncCygwinDir = Path.GetDirectoryName(rsyncPath) ?? "";
            var askPassBinary = FindAskPassBinary();
            var askPassCygwinPath = ToCygwinPath(askPassBinary);

            string sshCommand = $"ssh -p {connection.Port} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=15 -o ServerAliveInterval=10 -o ServerAliveCountMax=3 -o LogLevel=ERROR";

            var firstTask = tasks[0];
            var direction = firstTask.Direction;

            var sourceArgsBuilder = new StringBuilder();
            string destArg;

            if (direction == TransferDirection.Upload)
            {
                foreach (var t in tasks)
                {
                    sourceArgsBuilder.Append($"\"{ToCygwinPath(t.SourcePath)}\" ");
                }
                var remoteDest = firstTask.DestinationPath.EndsWith("/") ? firstTask.DestinationPath : firstTask.DestinationPath + "/";
                destArg = $"\"{connection.Username}@{connection.Host}:{remoteDest}\"";
            }
            else
            {
                foreach (var t in tasks)
                {
                    sourceArgsBuilder.Append($"\"{connection.Username}@{connection.Host}:{t.SourcePath}\" ");
                }
                var localDest = ToCygwinPath(firstTask.DestinationPath);
                if (!localDest.EndsWith("/")) localDest += "/";
                destArg = $"\"{localDest}\"";
            }

            var args = $"-avzP -s --stats --update -e \"{sshCommand}\" {sourceArgsBuilder.ToString().TrimEnd()} {destArg}";

            var batchDescription = tasks.Count == 1 ? tasks[0].FileName : $"{tasks.Count} files ({tasks[0].FileName}, ...)";
            LogMessageReceived?.Invoke($"[rsync] Transferring batch: {batchDescription}", false);

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

            startInfo.EnvironmentVariables["RSYNC_PASSWORD"] = connection.Password;
            startInfo.EnvironmentVariables["SSH_ASKPASS"] = askPassCygwinPath;
            startInfo.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "force";
            startInfo.EnvironmentVariables["DISPLAY"] = "dummy:0";

            var currentPath = startInfo.EnvironmentVariables["PATH"] ?? "";
            startInfo.EnvironmentVariables["PATH"] = $"{rsyncCygwinDir};{currentPath}";

            var errorOutput = new StringBuilder();

            // Fast lookup table by filename and basename
            var taskLookup = new Dictionary<string, TransferTask>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tasks)
            {
                taskLookup[t.FileName] = t;
                var baseName = Path.GetFileName(t.SourcePath.TrimEnd('/', '\\'));
                if (!string.IsNullOrEmpty(baseName)) taskLookup[baseName] = t;
            }

            TransferTask? activeTask = tasks.Count == 1 ? tasks[0] : null;

            try
            {
                using var process = new Process { StartInfo = startInfo };

                process.OutputDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    var line = e.Data.Trim();
                    LogMessageReceived?.Invoke($"[rsync] {line}", false);

                    // 1. Check if line announces a new file
                    var candidateName = line;
                    if (taskLookup.TryGetValue(candidateName, out var foundTask) ||
                        tasks.FirstOrDefault(t => candidateName.EndsWith("/" + t.FileName, StringComparison.OrdinalIgnoreCase) || candidateName.Equals(t.FileName, StringComparison.OrdinalIgnoreCase)) is { } matchedTask && (foundTask = matchedTask) != null)
                    {
                        if (activeTask != null && activeTask != foundTask && activeTask.Status == TransferStatus.Running)
                        {
                            activeTask.Status = TransferStatus.Completed;
                            activeTask.ProgressPercentage = 100;
                            activeTask.EndTime = DateTime.Now;
                        }

                        activeTask = foundTask;
                        if (activeTask.Status != TransferStatus.Completed && activeTask.Status != TransferStatus.Failed)
                        {
                            activeTask.Status = TransferStatus.Running;
                        }
                        return;
                    }

                    // 2. Check for progress matching
                    var match = ProgressRegex.Match(line);
                    if (match.Success && activeTask != null)
                    {
                        var bytesStr = match.Groups[1].Value;
                        if (int.TryParse(match.Groups[2].Value, out int percent))
                        {
                            activeTask.ProgressPercentage = percent;
                            if (percent == 100 && line.Contains("(xfr#"))
                            {
                                activeTask.Status = TransferStatus.Completed;
                                activeTask.EndTime = DateTime.Now;
                            }
                        }
                        activeTask.Speed = match.Groups[3].Value;
                        activeTask.Eta = match.Groups[4].Value;
                        activeTask.TransferredInfo = $"{bytesStr} bytes ({activeTask.Speed}, {activeTask.Eta} remaining)";
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        var errLine = e.Data.Trim();
                        errorOutput.AppendLine(errLine);

                        var isSetTimes = IsFailedToSetTimesError(errLine);
                        if (isSetTimes)
                        {
                            LogMessageReceived?.Invoke($"[rsync warning] ⚠️ {errLine} (File transferred successfully; remote server did not permit changing modification time).", false);
                        }
                        else
                        {
                            LogMessageReceived?.Invoke($"[rsync stderr] {errLine}", true);
                        }

                        // Check if error mentions any specific task in the batch
                        foreach (var t in tasks)
                        {
                            if (errLine.Contains(t.FileName, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(t.SourcePath) && errLine.Contains(Path.GetFileName(t.SourcePath.TrimEnd('/', '\\')), StringComparison.OrdinalIgnoreCase)))
                            {
                                if (isSetTimes)
                                {
                                    // Do not mark as failed: the file content transferred successfully!
                                    if (t.Status != TransferStatus.Failed)
                                    {
                                        t.ProgressPercentage = 100;
                                    }
                                }
                                else
                                {
                                    t.Status = TransferStatus.Failed;
                                    t.ErrorMessage = errLine;
                                }
                                break;
                            }
                        }
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
                            foreach (var t in tasks)
                            {
                                if (t.Status != TransferStatus.Completed)
                                    t.Status = TransferStatus.Cancelled;
                            }
                        }
                    }
                    catch { }
                }))
                {
                    await process.WaitForExitAsync();
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    foreach (var t in tasks)
                    {
                        if (t.Status != TransferStatus.Completed)
                        {
                            t.Status = TransferStatus.Cancelled;
                            t.ErrorMessage = "Transfer cancelled by user.";
                        }
                    }
                    return (false, RsyncFailureReason.Other, "Cancelled by user");
                }

                if (process.ExitCode == 0)
                {
                    foreach (var t in tasks)
                    {
                        t.ExitCode = 0;
                        t.EndTime = DateTime.Now;
                        if (t.Status != TransferStatus.Failed)
                        {
                            t.Status = TransferStatus.Completed;
                            t.ProgressPercentage = 100;
                            t.Eta = "0:00:00";
                        }
                    }
                    LogMessageReceived?.Invoke($"[rsync] ✅ Batch completed successfully ({tasks.Count} items)", false);
                    return (true, RsyncFailureReason.Other, "");
                }
                else if (process.ExitCode == 23 || process.ExitCode == 24)
                {
                    // Partial transfer due to error - some files succeeded, some failed
                    foreach (var t in tasks)
                    {
                        t.EndTime = DateTime.Now;
                        if (t.Status != TransferStatus.Failed)
                        {
                            t.Status = TransferStatus.Completed;
                            t.ProgressPercentage = 100;
                            t.Eta = "0:00:00";
                            t.ExitCode = 0; // Completed from the user's perspective
                        }
                        else
                        {
                            t.ExitCode = process.ExitCode;
                        }
                    }
                    var failedCount = tasks.Count(t => t.Status == TransferStatus.Failed);
                    var successCount = tasks.Count(t => t.Status == TransferStatus.Completed);
                    if (failedCount == 0)
                    {
                        LogMessageReceived?.Invoke($"[rsync] ✅ Batch completed successfully ({successCount} items). Note: timestamp warnings were logged.", false);
                    }
                    else
                    {
                        LogMessageReceived?.Invoke($"[rsync] ⚠️ Batch completed with partial errors: {successCount} succeeded, {failedCount} failed.", true);
                    }
                    return (true, RsyncFailureReason.Other, errorOutput.ToString().Trim());
                }
                else
                {
                    var err = errorOutput.ToString().Trim();
                    var reason = ClassifyError(process.ExitCode, err);

                    // If every task transferred without fatal error and errorOutput only contained timestamp warnings
                    var nonFailedTasks = tasks.Where(t => t.Status != TransferStatus.Failed).ToList();
                    var onlySetTimesWarnings = !string.IsNullOrEmpty(err) &&
                        err.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).All(IsFailedToSetTimesError);

                    if (onlySetTimesWarnings && nonFailedTasks.Count == tasks.Count)
                    {
                        foreach (var t in tasks)
                        {
                            t.ExitCode = 0;
                            t.EndTime = DateTime.Now;
                            t.Status = TransferStatus.Completed;
                            t.ProgressPercentage = 100;
                            t.Eta = "0:00:00";
                        }
                        LogMessageReceived?.Invoke($"[rsync] ✅ Batch completed successfully ({tasks.Count} items). Note: timestamp warnings were logged.", false);
                        return (true, RsyncFailureReason.Other, "");
                    }

                    foreach (var t in tasks)
                    {
                        t.ExitCode = process.ExitCode;
                        t.EndTime = DateTime.Now;
                        if (t.Status != TransferStatus.Completed)
                        {
                            t.Status = TransferStatus.Failed;
                            if (string.IsNullOrEmpty(t.ErrorMessage))
                            {
                                t.ErrorMessage = string.IsNullOrEmpty(err) ? $"rsync returned exit code {process.ExitCode}" : err;
                            }
                        }
                    }
                    return (false, reason, err);
                }
            }
            catch (Exception ex)
            {
                foreach (var t in tasks)
                {
                    if (t.Status != TransferStatus.Completed)
                    {
                        t.Status = TransferStatus.Failed;
                        t.ErrorMessage = ex.Message;
                    }
                }
                var reason = ClassifyError(-1, ex.Message);
                return (false, reason, ex.Message);
            }
        }
    }
}
