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
        private string _currentDisplayLine = string.Empty;
        private string? _lastPromptAnsi;
        private volatile bool _isAtPrompt;
        private TaskCompletionSource<bool>? _promptArrivedTcs;
        private const int MaxScreenBufferChars = 65536;

        private static readonly Regex AnsiEscapeRegex = new(
            @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\].*?(?:\x07|\x1B\\))",
            RegexOptions.Compiled);

        private static readonly Regex OscSequenceRegex = new(
            @"\x1B\].*?(?:\x07|\x1B\\)",
            RegexOptions.Compiled);

        private static readonly Regex InternalSentinelEchoRegex = new(
            @"[;\r\n]?\s*printf\s+""\\n__RZ_DONE_[0-9a-f]+_%d_%s__\\n""\s+""\$\?""\s+""\$\(pwd\)""",
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
                                UpdatePromptTracking(displayText);
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

        public static bool IsLikelyShellPrompt(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            var plain = StripAnsiCodes(line);
            var trimmedEnd = plain.TrimEnd();
            if (trimmedEnd.Length == 0 || trimmedEnd.Length > 200) return false;
            if (trimmedEnd.StartsWith("[🤖 AI", StringComparison.Ordinal)) return false;

            char last = trimmedEnd[^1];
            if (last != '#' && last != '$' && last != '>' && last != '%') return false;

            // Typical prompts contain user@host, a path separator, ~, or a colon (e.g. "root@host:/dir#", "user@host:~$", "[user@host dir]$")
            return trimmedEnd.Contains('@') || trimmedEnd.Contains(':') || trimmedEnd.Contains('/') || trimmedEnd.Contains('~') || trimmedEnd.Length <= 8;
        }

        internal void UpdatePromptTracking(string displayText)
        {
            TaskCompletionSource<bool>? toNotify = null;
            lock (_screenBufferLock)
            {
                int lastNl = displayText.LastIndexOf('\n');
                if (lastNl >= 0)
                {
                    _currentDisplayLine = displayText.Substring(lastNl + 1);
                }
                else
                {
                    _currentDisplayLine += displayText;
                    if (_currentDisplayLine.Length > 1024)
                    {
                        _currentDisplayLine = _currentDisplayLine.Substring(_currentDisplayLine.Length - 1024);
                    }
                }

                // Handle carriage returns inside the current line (keep text after the last \r unless \r is at the very end)
                int lastCr = _currentDisplayLine.LastIndexOf('\r');
                if (lastCr >= 0 && lastCr < _currentDisplayLine.Length - 1)
                {
                    _currentDisplayLine = _currentDisplayLine.Substring(lastCr + 1);
                }

                var candidate = _currentDisplayLine.TrimStart('\r');
                if (IsLikelyShellPrompt(candidate))
                {
                    _lastPromptAnsi = OscSequenceRegex.Replace(candidate, "");
                    _isAtPrompt = true;
                    toNotify = _promptArrivedTcs;
                    _promptArrivedTcs = null;
                }
                else if (displayText.Contains('\n'))
                {
                    _isAtPrompt = false;
                }
            }

            toNotify?.TrySetResult(true);
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

        public void WriteAiNoticeToTerminal(string notice, string ansiColor = "1;35", bool restorePrompt = true)
        {
            var tag = !string.IsNullOrWhiteSpace(McpLabel) ? $"🤖 AI {McpLabel}" : "🤖 AI MCP";

            string? promptToRestore;
            bool wasAtPrompt;
            lock (_screenBufferLock)
            {
                promptToRestore = _lastPromptAnsi;
                wasAtPrompt = _isAtPrompt;
                if (!restorePrompt)
                {
                    _isAtPrompt = false;
                    _currentDisplayLine = string.Empty;
                }
                else if (!string.IsNullOrEmpty(promptToRestore))
                {
                    _isAtPrompt = true;
                    _currentDisplayLine = promptToRestore;
                }
            }

            string prefix = wasAtPrompt ? "\r\x1b[2K" : "\r\n";
            string suffix = (restorePrompt && !string.IsNullOrEmpty(promptToRestore))
                ? $"\r\n{promptToRestore}"
                : "\r\n";

            var formatted = $"{prefix}\x1b[{ansiColor}m[{tag}]\x1b[0m {notice}{suffix}";
            AppendToScreenBuffer(formatted);
            OutputReceived?.Invoke(formatted);
        }

        public void SendInput(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            _isAtPrompt = false;

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

        private async Task EnsurePromptVisibleAfterCommandAsync(CancellationToken cancellationToken)
        {
            if (_shellStream == null || !IsConnected) return;

            TaskCompletionSource<bool>? waitTcs = null;
            lock (_screenBufferLock)
            {
                if (_isAtPrompt) return;
                waitTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _promptArrivedTcs = waitTcs;
            }

            // Wait briefly for the remote shell's natural prompt to arrive right after the sentinel line
            var completed = await Task.WhenAny(waitTcs.Task, Task.Delay(250, cancellationToken));
            if (completed == waitTcs.Task && _isAtPrompt)
            {
                return;
            }

            // If the prompt still didn't appear on its own, send a clean Enter so the shell prints a fresh prompt
            lock (_screenBufferLock)
            {
                if (_isAtPrompt) return;
                waitTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _promptArrivedTcs = waitTcs;
            }

            SendInput("\r");
            await Task.WhenAny(waitTcs.Task, Task.Delay(250, cancellationToken));
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
                WriteAiNoticeToTerminal($"\x1b[1;33m$ {trimmedCommand}\x1b[0m", restorePrompt: false);

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
                    var sanitizedCmd = trimmedCommand.TrimEnd();
                    string separator;
                    if (sanitizedCmd.Contains('\n') || sanitizedCmd.Contains('#'))
                    {
                        separator = "\n";
                    }
                    else if (sanitizedCmd.EndsWith(";", StringComparison.Ordinal) || sanitizedCmd.EndsWith("&", StringComparison.Ordinal))
                    {
                        separator = " ";
                    }
                    else
                    {
                        separator = "; ";
                    }

                    var payload = $"{sanitizedCmd}{separator}printf \"\\n__RZ_DONE_{token}_%d_%s__\\n\" \"$?\" \"$(pwd)\"\r";
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

                            try
                            {
                                await EnsurePromptVisibleAfterCommandAsync(cancellationToken);
                            }
                            catch { }

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
