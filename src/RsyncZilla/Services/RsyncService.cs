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
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "cygwin64", "rsync.exe")),
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
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "RsyncAskPass", "bin", "Debug", "net8.0", "RsyncAskPass.exe")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "RsyncAskPass", "bin", "Release", "net8.0", "RsyncAskPass.exe"))
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

        public async Task<bool> ExecuteTransferAsync(
            TransferTask task,
            ConnectionProfile connection,
            CancellationToken cancellationToken = default)
        {
            var rsyncPath = FindRsyncBinary();
            if (!File.Exists(rsyncPath))
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = $"No se encontró el ejecutable de rsync en: {rsyncPath}";
                LogMessageReceived?.Invoke(task.ErrorMessage, true);
                return false;
            }

            var sshBinary = FindSshBinary();
            var askPassBinary = FindAskPassBinary();

            task.Status = TransferStatus.Running;
            task.StartTime = DateTime.Now;

            var rsyncCygwinDir = Path.GetDirectoryName(rsyncPath) ?? "";
            var sshCygwinPath = ToCygwinPath(sshBinary);
            var askPassCygwinPath = ToCygwinPath(askPassBinary);

            string sshCommand = $"\"{sshCygwinPath}\" -p {connection.Port} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR";

            string sourceArg;
            string destArg;

            if (task.Direction == TransferDirection.Upload)
            {
                // Local to Remote
                sourceArg = ToCygwinPath(task.SourcePath);
                // Ensure target directory ends with slash so rsync puts items inside it
                var remoteDest = task.DestinationPath.EndsWith("/") ? task.DestinationPath : task.DestinationPath + "/";
                destArg = $"{connection.Username}@{connection.Host}:\"{remoteDest}\"";
            }
            else
            {
                // Remote to Local
                sourceArg = $"{connection.Username}@{connection.Host}:\"{task.SourcePath}\"";
                var localDest = ToCygwinPath(task.DestinationPath);
                if (!localDest.EndsWith("/")) localDest += "/";
                destArg = $"\"{localDest}\"";
            }

            // Arguments: -avzP --stats --update -e "..."
            var args = $"-avzP --stats --update -e \"{sshCommand}\" \"{sourceArg}\" {destArg}";

            LogMessageReceived?.Invoke($"[rsync] Iniciando transferencia: {task.FileName}", false);
            LogMessageReceived?.Invoke($"[rsync comando] rsync {args}", false);

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

            // Set up SSH_ASKPASS environment variables
            startInfo.EnvironmentVariables["RSYNC_PASSWORD"] = connection.Password;
            startInfo.EnvironmentVariables["SSH_ASKPASS"] = askPassCygwinPath;
            startInfo.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "force";
            startInfo.EnvironmentVariables["DISPLAY"] = "dummy:0";
            
            // Add cygwin bin directory to PATH for DLL resolution
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

                    // Parse progress line: 72,123,456  45%    3.24MB/s    0:00:15
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
                        task.TransferredInfo = $"{bytesStr} bytes transferidos ({task.Speed}, restan {task.Eta})";
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
                            LogMessageReceived?.Invoke($"[rsync] Transferencia cancelada por el usuario: {task.FileName}", true);
                        }
                    }
                    catch { }
                }))
                {
                    await process.WaitForExitAsync();
                }

                task.ExitCode = process.ExitCode;
                task.EndTime = DateTime.Now;

                if (task.Status == TransferStatus.Cancelled)
                {
                    return false;
                }

                if (process.ExitCode == 0)
                {
                    task.Status = TransferStatus.Completed;
                    task.ProgressPercentage = 100;
                    task.Eta = "0:00:00";
                    LogMessageReceived?.Invoke($"[rsync] ✅ Transferencia finalizada con ÉXITO: {task.FileName} (ExitCode: 0)", false);
                    return true;
                }
                else
                {
                    task.Status = TransferStatus.Failed;
                    task.ErrorMessage = $"rsync falló con código de salida {process.ExitCode}. {errorOutput.ToString().Trim()}";
                    LogMessageReceived?.Invoke($"[rsync] ❌ ERROR en transferencia: {task.ErrorMessage}", true);
                    return false;
                }
            }
            catch (Exception ex)
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = $"Error de ejecución rsync: {ex.Message}";
                LogMessageReceived?.Invoke(task.ErrorMessage, true);
                return false;
            }
        }
    }
}
