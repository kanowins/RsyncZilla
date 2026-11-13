using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RsyncZilla.Models;
using RsyncZilla.Services;

namespace RsyncZilla.ViewModels
{
    public class RemoteSessionViewModel : ViewModelBase, IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();

        private bool _isTabNameReset;
        public bool IsTabNameReset
        {
            get => _isTabNameReset;
            set
            {
                if (SetProperty(ref _isTabNameReset, value))
                {
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        private string? _siteName;
        public string? SiteName
        {
            get => _siteName;
            set
            {
                if (SetProperty(ref _siteName, value))
                {
                    _isTabNameReset = false;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        private string _host = "";
        public string Host
        {
            get => _host;
            set
            {
                if (SetProperty(ref _host, value))
                {
                    _isTabNameReset = false;
                    OnPropertyChanged(nameof(Title));
                    OnPropertyChanged(nameof(DisplayHost));
                }
            }
        }

        private string _username = "";
        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                {
                    _isTabNameReset = false;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public string Password { get; set; } = "";

        private int _port = 22;
        public int Port
        {
            get => _port;
            set
            {
                if (SetProperty(ref _port, value))
                {
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        private string? _lastLocalPath;
        public string? LastLocalPath
        {
            get => _lastLocalPath;
            set => SetProperty(ref _lastLocalPath, value);
        }

        public LocalFileService LocalService { get; }
        public SftpService SftpService { get; }
        public FileBrowserViewModel LocalBrowser { get; }
        public FileBrowserViewModel RemoteBrowser { get; }

        private bool _isConnecting;
        public bool IsConnecting
        {
            get => _isConnecting;
            set
            {
                if (SetProperty(ref _isConnecting, value))
                {
                    OnPropertyChanged(nameof(StatusIndicator));
                }
            }
        }

        private string _statusText = "Disconnected";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public bool IsConnected => SftpService.IsConnected || (_isTerminalTab && _terminalSession != null && _terminalSession.IsConnected);

        public string StatusIndicator => IsConnected ? "🟢" : (IsConnecting ? "🟡" : "⚪");

        public string DisplayHost => !string.IsNullOrWhiteSpace(Host) ? $"{Host}:{Port}" : "Disconnected";

        private bool _isTerminalTab;
        public bool IsTerminalTab
        {
            get => _isTerminalTab;
            set
            {
                if (SetProperty(ref _isTerminalTab, value))
                {
                    OnPropertyChanged(nameof(Title));
                    NotifyConnectionChanged();
                }
            }
        }

        private SshTerminalSession? _terminalSession;
        public SshTerminalSession? TerminalSession
        {
            get => _terminalSession;
            set
            {
                if (SetProperty(ref _terminalSession, value))
                {
                    if (_terminalSession != null && !string.IsNullOrWhiteSpace(_mcpName))
                    {
                        _terminalSession.McpLabel = _mcpName;
                    }
                    NotifyConnectionChanged();
                }
            }
        }

        public string? InitialTerminalPath { get; set; }

        private string? _mcpName;
        public string? McpName
        {
            get => _mcpName;
            set
            {
                if (SetProperty(ref _mcpName, value))
                {
                    if (_terminalSession != null)
                    {
                        _terminalSession.McpLabel = _mcpName;
                    }
                    OnPropertyChanged(nameof(Title));
                    OnPropertyChanged(nameof(AiPermissionBadgeText));
                    OnPropertyChanged(nameof(AiPermissionTooltip));
                }
            }
        }

        private AiPermissionLevel _aiPermission = AiPermissionLevel.Off;
        public AiPermissionLevel AiPermission
        {
            get => _aiPermission;
            set
            {
                if (SetProperty(ref _aiPermission, value))
                {
                    OnPropertyChanged(nameof(IsAiOff));
                    OnPropertyChanged(nameof(IsAiReadAndConfirm));
                    OnPropertyChanged(nameof(IsAiAuto));
                    OnPropertyChanged(nameof(AiPermissionBadgeText));
                    OnPropertyChanged(nameof(AiPermissionTooltip));

                    if (value == AiPermissionLevel.Off)
                    {
                        ResolvePendingConfirmation(false);
                    }

                    var modeText = value switch
                    {
                        AiPermissionLevel.ReadAndConfirm => "🛡️ Read & Confirm (AI can read freely; asks before executing commands)",
                        AiPermissionLevel.Auto => "⚡ Auto (AI has full terminal execution access)",
                        _ => "🔒 Off (AI access disabled for this tab)"
                    };
                    _terminalSession?.WriteAiNoticeToTerminal($"Permission mode changed to: {modeText}");
                }
            }
        }

        public bool IsAiOff => _aiPermission == AiPermissionLevel.Off;
        public bool IsAiReadAndConfirm => _aiPermission == AiPermissionLevel.ReadAndConfirm;
        public bool IsAiAuto => _aiPermission == AiPermissionLevel.Auto;

        public string AiPermissionBadgeText => _aiPermission switch
        {
            AiPermissionLevel.ReadAndConfirm => "🛡️ AI: Confirm",
            AiPermissionLevel.Auto => "⚡ AI: Auto",
            _ => "🔒 AI: Off"
        };

        public string AiPermissionTooltip => _aiPermission switch
        {
            AiPermissionLevel.ReadAndConfirm => "AI MCP Mode: Read & Confirm (Click to switch to Auto)",
            AiPermissionLevel.Auto => "AI MCP Mode: Auto / Full Control (Click to switch to Off)",
            _ => "AI MCP Mode: Off (Click to switch to Read & Confirm)"
        };

        private bool _hasPendingAiConfirmation;
        public bool HasPendingAiConfirmation
        {
            get => _hasPendingAiConfirmation;
            set => SetProperty(ref _hasPendingAiConfirmation, value);
        }

        private string _pendingAiActionTitle = "";
        public string PendingAiActionTitle
        {
            get => _pendingAiActionTitle;
            set => SetProperty(ref _pendingAiActionTitle, value);
        }

        private string _pendingAiCommandPreview = "";
        public string PendingAiCommandPreview
        {
            get => _pendingAiCommandPreview;
            set => SetProperty(ref _pendingAiCommandPreview, value);
        }

        private TaskCompletionSource<bool>? _pendingConfirmationTcs;
        private readonly SemaphoreSlim _confirmationQueueLock = new(1, 1);

        public ICommand CycleAiPermissionCommand { get; }
        public ICommand SetAiPermissionCommand { get; }
        public ICommand ApproveAiActionCommand { get; }
        public ICommand RejectAiActionCommand { get; }

        public string Title
        {
            get
            {
                if (_isTerminalTab)
                {
                    var suffix = !string.IsNullOrWhiteSpace(_mcpName) ? $" ({_mcpName})" : "";
                    var h = !string.IsNullOrWhiteSpace(Host) ? Host.Trim() : (SiteName?.Trim() ?? "");
                    if (!string.IsNullOrWhiteSpace(h))
                    {
                        if (!string.IsNullOrWhiteSpace(Username))
                        {
                            return $"Terminal: {Username.Trim()}@{h}{suffix}";
                        }
                        return $"Terminal: {h}{suffix}";
                    }
                    return $"Terminal{suffix}";
                }

                if (_isTabNameReset)
                    return "New Connection";

                var hNormal = !string.IsNullOrWhiteSpace(Host) ? Host.Trim() : (SiteName?.Trim() ?? "");

                if (!string.IsNullOrWhiteSpace(hNormal))
                {
                    if (!string.IsNullOrWhiteSpace(Username))
                    {
                        return $"{Username.Trim()}@{hNormal}";
                    }
                    return hNormal;
                }

                return "New Connection";
            }
        }

        public event Action<RemoteSessionViewModel>? CloseRequested;

        public ICommand CloseCommand { get; }

        public RemoteSessionViewModel() : this(new LocalFileService(), new SftpService())
        {
        }

        public RemoteSessionViewModel(LocalFileService localService, SftpService sftpService)
        {
            LocalService = localService;
            SftpService = sftpService;
            LocalBrowser = new FileBrowserViewModel(localService);
            RemoteBrowser = new FileBrowserViewModel(SftpService);

            SftpService.ConnectionStateChanged += () => NotifyConnectionChanged();

            CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this));
            CycleAiPermissionCommand = new RelayCommand(() =>
            {
                AiPermission = AiPermission switch
                {
                    AiPermissionLevel.Off => AiPermissionLevel.ReadAndConfirm,
                    AiPermissionLevel.ReadAndConfirm => AiPermissionLevel.Auto,
                    _ => AiPermissionLevel.Off
                };
            });
            SetAiPermissionCommand = new RelayCommand(param =>
            {
                if (param is AiPermissionLevel level)
                {
                    AiPermission = level;
                }
                else if (param is string s && Enum.TryParse<AiPermissionLevel>(s, true, out var parsed))
                {
                    AiPermission = parsed;
                }
            });
            ApproveAiActionCommand = new RelayCommand(() => ResolvePendingConfirmation(true));
            RejectAiActionCommand = new RelayCommand(() => ResolvePendingConfirmation(false));
        }

        public async Task<bool> RequestAiConfirmationAsync(string actionTitle, string commandPreview, CancellationToken cancellationToken = default)
        {
            if (AiPermission == AiPermissionLevel.Auto) return true;
            if (AiPermission == AiPermissionLevel.Off) return false;

            await _confirmationQueueLock.WaitAsync(cancellationToken);
            try
            {
                if (AiPermission == AiPermissionLevel.Auto) return true;
                if (AiPermission == AiPermissionLevel.Off) return false;

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingConfirmationTcs = tcs;

                _terminalSession?.WriteAiNoticeToTerminal($"\x1b[1;33m⏳ Waiting for user confirmation:\x1b[0m {actionTitle} -> {commandPreview}", "1;33");

                RunOnUi(() =>
                {
                    PendingAiActionTitle = actionTitle;
                    PendingAiCommandPreview = commandPreview;
                    HasPendingAiConfirmation = true;
                });

                try
                {
                    using (cancellationToken.Register(() => tcs.TrySetResult(false)))
                    {
                        var approved = await tcs.Task;
                        if (approved)
                        {
                            _terminalSession?.WriteAiNoticeToTerminal("\x1b[1;32m✅ Approved by user.\x1b[0m", "1;32");
                        }
                        else
                        {
                            _terminalSession?.WriteAiNoticeToTerminal("\x1b[1;31m❌ Rejected by user.\x1b[0m", "1;31");
                        }
                        return approved;
                    }
                }
                finally
                {
                    _pendingConfirmationTcs = null;
                    RunOnUi(() =>
                    {
                        HasPendingAiConfirmation = false;
                        PendingAiActionTitle = "";
                        PendingAiCommandPreview = "";
                    });
                }
            }
            finally
            {
                _confirmationQueueLock.Release();
            }
        }

        public void ResolvePendingConfirmation(bool approved)
        {
            _pendingConfirmationTcs?.TrySetResult(approved);
        }

        private static void RunOnUi(Action action)
        {
            var app = Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.HasShutdownStarted && app.Dispatcher.Thread.IsAlive)
            {
                if (app.Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    try
                    {
                        app.Dispatcher.Invoke(action, TimeSpan.FromMilliseconds(200));
                    }
                    catch
                    {
                        action();
                    }
                }
            }
            else
            {
                action();
            }
        }

        public void NotifyConnectionChanged()
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsConnecting));
            OnPropertyChanged(nameof(StatusIndicator));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayHost));
        }

        public ConnectionProfile CreateConnectionProfile()
        {
            return new ConnectionProfile
            {
                Host = Host.Trim(),
                Username = Username.Trim(),
                Password = Password,
                Port = Port,
                RemoteInitialPath = RemoteBrowser.CurrentPath
            };
        }

        public void Disconnect()
        {
            ResolvePendingConfirmation(false);
            AiPermission = AiPermissionLevel.Off;

            try
            {
                _terminalSession?.Dispose();
                _terminalSession = null;
            }
            catch { }

            try
            {
                SftpService.Disconnect();
            }
            catch { }

            _isTabNameReset = true;
            _siteName = null;
            NotifyConnectionChanged();
        }

        public void Dispose()
        {
            Disconnect();
            SftpService.Dispose();
        }
    }
}
