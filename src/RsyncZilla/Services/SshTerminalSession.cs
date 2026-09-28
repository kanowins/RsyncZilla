using System;
using System.Collections.Generic;
using System.Text;
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

        public string Host { get; }
        public int Port { get; }
        public string Username { get; }
        private readonly string _password;
        public string? InitialPath { get; set; }

        public bool IsConnected => _client != null && _client.IsConnected && _shellStream != null;

        public event Action<string>? OutputReceived;
        public event Action? Disconnected;
        public event Action<string>? ErrorOccurred;

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
                            var text = Encoding.UTF8.GetString(e.Data);
                            OutputReceived?.Invoke(text);
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

        public void SendInput(string data)
        {
            if (_shellStream == null || !IsConnected) return;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(data);
                _shellStream.Write(bytes, 0, bytes.Length);
                _shellStream.Flush();
            }
            catch { }
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
