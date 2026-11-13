using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace RsyncZilla.Services
{
    public class SshTerminalSession : IDisposable
    {
        private SshClient? _client;
        private ShellStream? _shellStream;
        private bool _disposed;
        private readonly SemaphoreSlim _aiCommandLock = new(1, 1);
        private readonly object _screenBufferLock = new();
        private readonly StringBuilder _screenBuffer = new();
        private const int MaxScreenBufferChars = 65536;

        private static readonly Regex AnsiEscapeRegex = new(
            @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\].*?(?:\x07|\x1B\\))",
            RegexOptions.Compiled);

        private static readonly Regex InternalSentinelEchoRegex = new(
            @";\s*printf\s+""\\n__RZ_DONE_[0-9a-f]+_%d_%s__\\n""\s+""\$\?""\s+""\$\(pwd\)""",
            RegexOptions.Compiled);

        private static readonly Regex InternalSentinelOutputRegex = new(
            @"\r?\n?__RZ_DONE_[0-9a-f]+_-?\d+_[^\r\n]*?__\r?\n?",
            RegexOptions.Compiled);

        public string Host { get; }
        public int Port { get; }
        public string Username { get; }
        private readonly string _password;
        public string? InitialPath { get; set; }
        public string? McpLabel { get; set; }

        public bool IsConnected => (_client != null && _client.IsConnected && _shellStream != null) || CustomCommandExecutor != null;

        public event Action<string>? OutputReceived;
        public event Action<string>? RawOutputReceived;
        public event Action<string>? WorkingDirectoryDetected;
        public event Action? Disconnected;
        public event Action<string>? ErrorOccurred;

        /// <summary>
        /// Optional hook for unit testing AI command execution without a live SSH server.
        /// </summary>
        public Func<string, int, CancellationToken, Task<(bool completed, int? exitCode, string output, string? workingDirectory)>>? CustomCommandExecutor { get; set; }

        public SshTerminalSession(string host, int port, string username, string password, string? initialPath = null)
        {
            Host = host;
            Port = port > 0 ? port : 22;
            Username = username;
            _password = password;
            InitialPath = initialPath;
        }

        public SshDiagnosticResult? LastDiagnostic { get; private set; }

        public async Task<(bool success, string? error, SshDiagnosticResult? diagnostic)> ConnectAsync(uint cols = 80, uint rows = 24)
        {
            Disconnect();

            cols = Math.Max(cols > 0 ? cols : 80u, 20u);
            rows = Math.Max(rows > 0 ? rows : 24u, 5u);

            return await Task.Run(() =>
            {
                try
                {
                    var connectionInfo = new ConnectionInfo(
                        Host,
                        Port,
                        Username,
                        new PasswordAuthenticationMethod(Username, _password)
                    )
                    {
                        Timeout = TimeSpan.FromSeconds(15)
                    };

                    _client = new SshClient(connectionInfo);
                    _client.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    _client.Connect();

                    var modes = new Dictionary<TerminalModes, uint>();
                    _shellStream = _client.CreateShellStream("xterm-256color", cols, rows, 0, 0, 8192, modes);

                    _shellStream.DataReceived += (s, e) =>
                    {
                        if (e.Data != null && e.Data.Length > 0)
                        {
                            var rawText = Encoding.UTF8.GetString(e.Data);
                            RawOutputReceived?.Invoke(rawText);

                            var displayText = CleanInternalMcpMarkersForDisplay(rawText);
                            if (!string.IsNullOrEmpty(displayText))
                            {
                                AppendToScreenBuffer(displayText);
                                OutputReceived?.Invoke(displayText);
                            }
                        }
                    };

                    _shellStream.ErrorOccurred += (s, e) =>
                    {
                        ErrorOccurred?.Invoke(e.Exception.Message);
                    };

                    _shellStream.Closed += (s, e) =>
                    {
                        Disconnected?.Invoke();
                    };

                    if (!string.IsNullOrWhiteSpace(InitialPath) && InitialPath != "~")
                    {
                        // Navigate to initial path once shell starts
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(200);
                            SendInput($"cd '{InitialPath.Replace("'", "'\\''")}'\r");
                        });
                    }

                    LastDiagnostic = null;
                    return (true, (string?)null, (SshDiagnosticResult?)null);
                }
                catch (Exception ex)
                {
                    var diag = SshDiagnostics.Analyze(ex, Host, Port, Username);
                    LastDiagnostic = diag;
                    Disconnect();
                    return (false, diag.Summary, diag);
                }
            });
        }

        public static string CleanInternalMcpMarkersForDisplay(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var cleaned = InternalSentinelEchoRegex.Replace(text, "");
            cleaned = InternalSentinelOutputRegex.Replace(cleaned, "\r\n");
            return cleaned;
        }

        public static string StripAnsiCodes(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return AnsiEscapeRegex.Replace(text, "");
        }

        public void AppendToScreenBuffer(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_screenBufferLock)
            {
                _screenBuffer.Append(text);
                if (_screenBuffer.Length > MaxScreenBufferChars)
                {
                    _screenBuffer.Remove(0, _screenBuffer.Length - (MaxScreenBufferChars / 2));
                }
            }
        }

        public string GetRecentOutput(int maxLines = 100)
        {
            maxLines = Math.Clamp(maxLines, 1, 2000);
            string raw;
            lock (_screenBufferLock)
            {
                raw = _screenBuffer.ToString();
            }

            var clean = StripAnsiCodes(raw);
            var lines = clean.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length <= maxLines)
            {
                return string.Join("\n", lines).Trim();
            }

            return string.Join("\n", lines.Skip(lines.Length - maxLines)).Trim();
        }

        public void WriteAiNoticeToTerminal(string notice, string ansiColor = "1;35")
        {
            var tag = !string.IsNullOrWhiteSpace(McpLabel) ? $"🤖 AI {McpLabel}" : "🤖 AI MCP";
            var formatted = $"\r\n\x1b[{ansiColor}m[{tag}]\x1b[0m {notice}\r\n";
            AppendToScreenBuffer(formatted);
            OutputReceived?.Invoke(formatted);
        }

        public void SendInput(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            if (_shellStream == null || !IsConnected)
            {
                if (CustomCommandExecutor != null)
                {
                    AppendToScreenBuffer(data);
                    OutputReceived?.Invoke(data);
                }
                return;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(data);
                _shellStream.Write(bytes, 0, bytes.Length);
                _shellStream.Flush();
            }
            catch { }
        }

        public async Task<(bool completed, int? exitCode, string output, string? workingDirectory)> ExecuteCommandAsync(
            string command,
            int timeoutSeconds = 30,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return (true, 0, string.Empty, null);
            }

            timeoutSeconds = Math.Clamp(timeoutSeconds, 2, 600);

            await _aiCommandLock.WaitAsync(cancellationToken);
            try
            {
                var trimmedCommand = command.TrimEnd('\r', '\n');
                WriteAiNoticeToTerminal($"\x1b[1;33m$ {trimmedCommand}\x1b[0m");

                if (CustomCommandExecutor != null)
                {
                    var customRes = await CustomCommandExecutor(trimmedCommand, timeoutSeconds, cancellationToken);
                    if (!string.IsNullOrEmpty(customRes.output))
                    {
                        AppendToScreenBuffer(customRes.output + "\r\n");
                        OutputReceived?.Invoke(customRes.output + "\r\n");
                    }
                    if (!string.IsNullOrWhiteSpace(customRes.workingDirectory))
                    {
                        WorkingDirectoryDetected?.Invoke(customRes.workingDirectory!);
                    }
                    return customRes;
                }

                if (_shellStream == null || !IsConnected)
                {
                    return (false, null, "SSH terminal session is not connected.", null);
                }

                var token = Guid.NewGuid().ToString("N")[..8];
                var sentinelPattern = new Regex($@"__RZ_DONE_{token}_(-?\d+)_([^\r\n]*?)__", RegexOptions.Compiled);
                var collectedRaw = new StringBuilder();
                var tcs = new TaskCompletionSource<(int exitCode, string pwd)>(TaskCreationOptions.RunContinuationsAsynchronously);

                void OnRawData(string chunk)
                {
                    lock (collectedRaw)
                    {
                        collectedRaw.Append(chunk);
                        var m = sentinelPattern.Match(collectedRaw.ToString());
                        if (m.Success && int.TryParse(m.Groups[1].Value, out int code))
                        {
                            var pwd = m.Groups[2].Value.Trim();
                            tcs.TrySetResult((code, pwd));
                        }
                    }
                }

                RawOutputReceived += OnRawData;
                try
                {
                    var payload = $"{trimmedCommand}; printf \"\\n__RZ_DONE_{token}_%d_%s__\\n\" \"$?\" \"$(pwd)\"\r";
                    SendInput(payload);

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                    try
                    {
                        using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
                        {
                            var (exitCode, pwd) = await tcs.Task;
                            var rawOutput = collectedRaw.ToString();
                            var cleanOutput = ExtractCleanCommandOutput(rawOutput, trimmedCommand, sentinelPattern);

                            if (!string.IsNullOrWhiteSpace(pwd))
                            {
                                WorkingDirectoryDetected?.Invoke(pwd);
                            }

                            return (true, exitCode, cleanOutput, pwd);
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        string partialRaw;
                        lock (collectedRaw)
                        {
                            partialRaw = collectedRaw.ToString();
                        }
                        var partialClean = ExtractCleanCommandOutput(partialRaw, trimmedCommand, sentinelPattern);
                        return (false, null, partialClean, null);
                    }
                }
                finally
                {
                    RawOutputReceived -= OnRawData;
                }
            }
            finally
            {
                _aiCommandLock.Release();
            }
        }

        public static string ExtractCleanCommandOutput(string rawOutput, string command, Regex sentinelPattern)
        {
            if (string.IsNullOrEmpty(rawOutput)) return string.Empty;

            // Remove the sentinel output and everything after it
            var m = sentinelPattern.Match(rawOutput);
            var beforeSentinel = m.Success ? rawOutput.Substring(0, m.Index) : rawOutput;

            // Remove internal sentinel echo if present
            beforeSentinel = InternalSentinelEchoRegex.Replace(beforeSentinel, "");

            // Strip ANSI escape sequences
            var stripped = StripAnsiCodes(beforeSentinel).Replace("\r\n", "\n").Replace('\r', '\n');

            // Remove echoed command on first line if present
            var lines = stripped.Split('\n').ToList();
            if (lines.Count > 0 && lines[0].Contains(command, StringComparison.Ordinal))
            {
                lines.RemoveAt(0);
            }

            return string.Join("\n", lines).Trim();
        }

        private static readonly System.Reflection.FieldInfo? ChannelField =
            typeof(ShellStream).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        public void ChangeSize(uint cols, uint rows)
        {
            if (_shellStream == null || !IsConnected) return;

            try
            {
                if (cols > 0 && rows > 0)
                {
                    var channel = ChannelField?.GetValue(_shellStream);
                    if (channel != null)
                    {
                        var method = channel.GetType().GetMethod("SendWindowChangeRequest", new[] { typeof(uint), typeof(uint), typeof(uint), typeof(uint) });
                        method?.Invoke(channel, new object[] { cols, rows, 0u, 0u });
                    }
                }
            }
            catch { }
        }

        public void Disconnect()
        {
            try
            {
                _shellStream?.Dispose();
                _shellStream = null;
            }
            catch { }

            try
            {
                if (_client != null && _client.IsConnected)
                {
                    _client.Disconnect();
                }
                _client?.Dispose();
                _client = null;
            }
            catch { }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Disconnect();
            }
        }
    }
}
