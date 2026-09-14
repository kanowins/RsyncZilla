using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RsyncZilla.Models;
using RsyncZilla.Services;

namespace RsyncZilla.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly LocalFileService _localService;
        private readonly RsyncService _rsyncService;
        private readonly ConnectionManagerService _connectionManagerService;

        public ObservableCollection<RemoteSessionViewModel> RemoteSessions { get; } = new();

        private RemoteSessionViewModel _activeSession;
        public RemoteSessionViewModel ActiveSession
        {
            get => _activeSession;
            set => _ = SwitchToSessionAsync(value);
        }

        public FileBrowserViewModel LocalBrowser => ActiveSession?.LocalBrowser ?? _fallbackLocalBrowser;
        public FileBrowserViewModel RemoteBrowser => ActiveSession?.RemoteBrowser ?? _fallbackRemoteBrowser;
        public SftpService SftpService => ActiveSession?.SftpService ?? _fallbackSftpService;

        private readonly FileBrowserViewModel _fallbackLocalBrowser;
        private readonly FileBrowserViewModel _fallbackRemoteBrowser;
        private readonly SftpService _fallbackSftpService;

        public string Host
        {
            get => _activeSession?.Host ?? "";
            set
            {
                if (_activeSession != null && _activeSession.Host != value)
                {
                    _activeSession.Host = value;
                    OnPropertyChanged(nameof(Host));
                }
            }
        }

        public string Username
        {
            get => _activeSession?.Username ?? "";
            set
            {
                if (_activeSession != null && _activeSession.Username != value)
                {
                    _activeSession.Username = value;
                    OnPropertyChanged(nameof(Username));
                }
            }
        }

        public int Port
        {
            get => _activeSession?.Port ?? 22;
            set
            {
                if (_activeSession != null && _activeSession.Port != value)
                {
                    _activeSession.Port = value;
                    OnPropertyChanged(nameof(Port));
                }
            }
        }

        private string _cachedPassword
        {
            get => _activeSession?.Password ?? "";
            set
            {
                if (_activeSession != null)
                {
                    _activeSession.Password = value;
                }
            }
        }

        public bool IsConnected => ActiveSession?.IsConnected ?? false;
        public bool IsConnecting => ActiveSession?.IsConnecting ?? false;
        public string StatusText => ActiveSession?.StatusText ?? "Disconnected";

        public string ConnectionButtonText => IsConnected ? "Disconnect" : "Quick Connect";
        public string ConnectionStatusIndicator => IsConnected ? "🟢 Connected" : (IsConnecting ? "🟡 Connecting" : "⚪ Disconnected");

        public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        public string FooterInfo => $"RsyncZilla v{AppVersion} | Engine: rsync 3.3.0 portable (Cygwin64) + SSH.NET";

        public string ActiveTabHeader => $"🚀 Queue ({ActiveTransfers.Count})";
        public string FailedTabHeader => $"❌ Failed ({FailedTransfers.Count})";
        public string CompletedTabHeader => $"✅ Completed ({CompletedTransfers.Count})";

        public ObservableCollection<TransferTask> ActiveTransfers { get; } = new();
        public ObservableCollection<TransferTask> CompletedTransfers { get; } = new();
        public ObservableCollection<TransferTask> FailedTransfers { get; } = new();
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        private TransferTask? _selectedFailedTransfer;
        public TransferTask? SelectedFailedTransfer
        {
            get => _selectedFailedTransfer;
            set => SetProperty(ref _selectedFailedTransfer, value);
        }

        private CancellationTokenSource? _currentTransferCts;
        private readonly object _queueLock = new();
        private bool _isProcessingQueue = false;

        public ICommand ConnectCommand { get; }
        public ICommand NewTabCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand UploadSelectedCommand { get; }
        public ICommand DownloadSelectedCommand { get; }
        public ICommand CancelAllTransfersCommand { get; }
        public ICommand ClearCompletedCommand { get; }
        public ICommand ClearFailedCommand { get; }
        public ICommand RetrySelectedFailedCommand { get; }
        public ICommand RetryAllFailedCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand OpenSiteManagerCommand { get; }

        public Func<IEnumerable<FileItem>>? GetLocalSelectedItemsFunc { get; set; }
        public Func<IEnumerable<FileItem>>? GetRemoteSelectedItemsFunc { get; set; }
        public Action<SavedConnection, string>? ApplySavedConnectionAction { get; set; }

        public MainViewModel()
        {
            _localService = new LocalFileService();
            _rsyncService = new RsyncService();
            _connectionManagerService = new ConnectionManagerService();

            _fallbackLocalBrowser = new FileBrowserViewModel(_localService);
            _fallbackSftpService = new SftpService();
            _fallbackRemoteBrowser = new FileBrowserViewModel(_fallbackSftpService);

            // Create initial session tab
            var initialSession = CreateNewSession();
            RemoteSessions.Add(initialSession);
            _activeSession = initialSession;

            // Hook up rsync logging
            _rsyncService.LogMessageReceived += (msg, isErr) => AddLog(msg, isErr);

            ConnectCommand = new RelayCommand(async (param) => await ToggleConnectionAsync(param));
            NewTabCommand = new RelayCommand(() => AddNewTab());
            CloseTabCommand = new RelayCommand((param) => CloseTab(param as RemoteSessionViewModel));

            UploadSelectedCommand = new RelayCommand(async () => await UploadSelectedAsync(), () => IsConnected);
            DownloadSelectedCommand = new RelayCommand(async () => await DownloadSelectedAsync(), () => IsConnected);
            CancelAllTransfersCommand = new RelayCommand(CancelAllTransfers);
            ClearCompletedCommand = new RelayCommand(() => CompletedTransfers.Clear());
            ClearFailedCommand = new RelayCommand(() => FailedTransfers.Clear());
            RetrySelectedFailedCommand = new RelayCommand(() => RetrySelectedFailed(SelectedFailedTransfer), () => SelectedFailedTransfer != null);
            RetryAllFailedCommand = new RelayCommand(RetryAllFailed, () => FailedTransfers.Any());
            ClearLogsCommand = new RelayCommand(() => LogEntries.Clear());
            OpenSiteManagerCommand = new RelayCommand(OpenSiteManager);

            // Update tab headers when collection counts change
            ActiveTransfers.CollectionChanged += (s, e) => OnPropertyChanged(nameof(ActiveTabHeader));
            FailedTransfers.CollectionChanged += (s, e) => OnPropertyChanged(nameof(FailedTabHeader));
            CompletedTransfers.CollectionChanged += (s, e) => OnPropertyChanged(nameof(CompletedTabHeader));

            AddLog($"RsyncZilla v{AppVersion} initialized. Ready to connect.", false);
            var rsyncPath = _rsyncService.FindRsyncBinary();
            AddLog($"rsync engine detected at: {rsyncPath}", false);
        }

        private RemoteSessionViewModel CreateNewSession(string? host = null, string? username = null, int port = 22, string? siteName = null, string? password = null, string? initialLocalPath = null)
        {
            var session = new RemoteSessionViewModel(_localService, new SftpService());
            if (!string.IsNullOrWhiteSpace(host)) session.Host = host;
            if (!string.IsNullOrWhiteSpace(username)) session.Username = username;
            session.Port = port > 0 ? port : 22;
            if (!string.IsNullOrWhiteSpace(siteName)) session.SiteName = siteName;
            if (password != null) session.Password = password;

            if (!string.IsNullOrWhiteSpace(initialLocalPath) && (Directory.Exists(initialLocalPath) || initialLocalPath.Equals("This PC", StringComparison.OrdinalIgnoreCase)))
            {
                _ = session.LocalBrowser.NavigateToAsync(initialLocalPath);
            }

            session.CloseRequested += (s) => CloseTab(s);
            session.SftpService.LogMessageReceived += (msg, isErr) => AddLog($"[{session.Title}] {msg}", isErr);
            session.RemoteBrowser.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FileBrowserViewModel.CurrentPath))
                {
                    OnBrowserPathChanged(session);
                }
            };
            session.LocalBrowser.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FileBrowserViewModel.CurrentPath))
                {
                    OnBrowserPathChanged(session);
                }
            };

            return session;
        }

        public RemoteSessionViewModel AddNewTab(string? host = null, string? username = null, int port = 22, string? siteName = null, string? password = null, string? initialLocalPath = null)
        {
            var session = CreateNewSession(host, username, port, siteName, password, initialLocalPath);
            RemoteSessions.Add(session);
            ActiveSession = session;
            return session;
        }

        public async Task SwitchToSessionAsync(RemoteSessionViewModel? newSession)
        {
            if (newSession == null || _activeSession == newSession) return;

            // 1. Save outgoing session's path
            if (_activeSession != null)
            {
                _activeSession.LastLocalPath = _activeSession.LocalBrowser.CurrentPath;
                if (_activeSession.IsConnected && !string.IsNullOrWhiteSpace(_activeSession.Host) && !string.IsNullOrWhiteSpace(_activeSession.Username))
                {
                    _connectionManagerService.SaveOrUpdate(
                        _activeSession.Host.Trim(),
                        _activeSession.Username.Trim(),
                        _activeSession.Port,
                        localPath: _activeSession.LocalBrowser.CurrentPath,
                        remotePath: _activeSession.RemoteBrowser.CurrentPath);
                }
            }

            // 2. Switch active session
            _activeSession = newSession;
            OnSessionStateChanged();
            await Task.CompletedTask;
        }

        public void CloseTab(RemoteSessionViewModel? session)
        {
            session ??= ActiveSession;
            if (session == null) return;

            session.Disconnect();

            if (RemoteSessions.Count <= 1)
            {
                // Reset the single remaining tab rather than leaving 0 tabs
                session.Host = "";
                session.Username = "";
                session.Password = "";
                session.Port = 22;
                session.SiteName = null;
                session.LastLocalPath = null;
                session.StatusText = "Disconnected";
                session.RemoteBrowser.Items.Clear();
                session.RemoteBrowser.CurrentPath = "";
                session.NotifyConnectionChanged();
                OnSessionStateChanged();
                return;
            }

            var index = RemoteSessions.IndexOf(session);
            var isClosingActive = (_activeSession == session);

            RemoteSessions.Remove(session);
            session.Dispose();

            if (isClosingActive)
            {
                var nextIndex = Math.Min(index, RemoteSessions.Count - 1);
                if (nextIndex >= 0 && nextIndex < RemoteSessions.Count)
                {
                    ActiveSession = RemoteSessions[nextIndex];
                }
            }
        }

        public void OnSessionStateChanged()
        {
            OnPropertyChanged(nameof(ActiveSession));
            OnPropertyChanged(nameof(LocalBrowser));
            OnPropertyChanged(nameof(RemoteBrowser));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsConnecting));
            OnPropertyChanged(nameof(ConnectionButtonText));
            OnPropertyChanged(nameof(ConnectionStatusIndicator));
            OnPropertyChanged(nameof(Host));
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(Port));
            OnPropertyChanged(nameof(StatusText));
            CommandManager.InvalidateRequerySuggested();

            if (_activeSession != null)
            {
                ApplySavedConnectionAction?.Invoke(null!, _activeSession.Password);
            }
        }

        private void OnBrowserPathChanged(RemoteSessionViewModel? session = null)
        {
            session ??= ActiveSession;
            if (session != null && session.IsConnected && !string.IsNullOrWhiteSpace(session.Host) && !string.IsNullOrWhiteSpace(session.Username))
            {
                session.LastLocalPath = session.LocalBrowser.CurrentPath;
                _connectionManagerService.UpdatePaths(session.Host.Trim(), session.Username.Trim(), session.Port, session.LocalBrowser.CurrentPath, session.RemoteBrowser.CurrentPath);
            }
        }

        private async Task ToggleConnectionAsync(object? param)
        {
            if (ActiveSession == null) return;

            if (IsConnected)
            {
                ActiveSession.Disconnect();
                ActiveSession.StatusText = "Disconnected";
                RunOnUi(() =>
                {
                    ActiveSession.RemoteBrowser.Items.Clear();
                    ActiveSession.RemoteBrowser.CurrentPath = "";
                });
                OnSessionStateChanged();
                return;
            }

            if (param is PasswordBox pbox)
            {
                _cachedPassword = pbox.Password;
            }

            if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show("Please enter Server (Host) and Username.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ActiveSession.IsConnecting = true;
            ActiveSession.StatusText = "Connecting to server...";
            OnSessionStateChanged();

            try
            {
                var (success, error) = await ActiveSession.SftpService.ConnectAsync(Host.Trim(), Port, Username.Trim(), _cachedPassword);
                if (success)
                {
                    ActiveSession.IsTabNameReset = false;
                    ActiveSession.NotifyConnectionChanged();

                    // Look up if this connection has previously saved paths
                    var saved = _connectionManagerService.FindConnection(Host.Trim(), Username.Trim(), Port);

                    if (saved != null && !string.IsNullOrWhiteSpace(saved.LastLocalPath) && 
                        (Directory.Exists(saved.LastLocalPath) || saved.LastLocalPath.Equals("This PC", StringComparison.OrdinalIgnoreCase)))
                    {
                        ActiveSession.LastLocalPath = saved.LastLocalPath;
                        await ActiveSession.LocalBrowser.NavigateToAsync(saved.LastLocalPath);
                    }
                    else
                    {
                        ActiveSession.LastLocalPath = ActiveSession.LocalBrowser.CurrentPath;
                    }

                    // Navigate to user's remote home directory or saved last remote path
                    var initialPath = (saved != null && !string.IsNullOrWhiteSpace(saved.LastRemotePath))
                        ? saved.LastRemotePath
                        : (string.IsNullOrWhiteSpace(ActiveSession.SftpService.CurrentPath) ? "." : ActiveSession.SftpService.CurrentPath);

                    await ActiveSession.RemoteBrowser.NavigateToAsync(initialPath);

                    ActiveSession.StatusText = $"Connected to {Username}@{Host}:{Port}";
                    OnSessionStateChanged();

                    // Save or update to connection manager (without password) and record current active paths
                    _connectionManagerService.SaveOrUpdate(
                        Host.Trim(), 
                        Username.Trim(), 
                        Port, 
                        localPath: ActiveSession.LocalBrowser.CurrentPath, 
                        remotePath: ActiveSession.RemoteBrowser.CurrentPath);
                }
                else
                {
                    ActiveSession.NotifyConnectionChanged();
                    ActiveSession.StatusText = "Connection error.";
                    OnSessionStateChanged();
                    var msg = !string.IsNullOrWhiteSpace(error)
                        ? $"Could not connect to SFTP server:\n\n{error}"
                        : "Could not connect to SFTP server. Check logs below for details.";
                    MessageBox.Show(msg, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                ActiveSession.NotifyConnectionChanged();
                ActiveSession.StatusText = "Connection error.";
                OnSessionStateChanged();
                var errorMsg = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
                MessageBox.Show($"Connection error:\n\n{errorMsg}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ActiveSession.IsConnecting = false;
                OnSessionStateChanged();
            }
        }

        public void OpenSiteManager()
        {
            var dialog = new Views.ConnectionManagerDialog(_connectionManagerService)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true && dialog.SelectedConnection != null)
            {
                var conn = dialog.SelectedConnection;
                var username = !string.IsNullOrWhiteSpace(dialog.ConnectionUsername) ? dialog.ConnectionUsername : conn.Username;

                // If active tab is already connected, open in a new tab!
                RemoteSessionViewModel sessionToUse;
                if (IsConnected)
                {
                    sessionToUse = AddNewTab(conn.Host, username, conn.Port, siteName: conn.Host, password: dialog.ConnectionPassword, initialLocalPath: conn.LastLocalPath);
                }
                else
                {
                    sessionToUse = ActiveSession;
                    sessionToUse.Host = conn.Host;
                    sessionToUse.Username = username;
                    sessionToUse.Port = conn.Port;
                    sessionToUse.SiteName = conn.Host;
                    if (dialog.ConnectionPassword != null)
                    {
                        sessionToUse.Password = dialog.ConnectionPassword;
                    }
                    if (!string.IsNullOrWhiteSpace(conn.LastLocalPath) && 
                        (Directory.Exists(conn.LastLocalPath) || conn.LastLocalPath.Equals("This PC", StringComparison.OrdinalIgnoreCase)))
                    {
                        sessionToUse.LastLocalPath = conn.LastLocalPath;
                        _ = sessionToUse.LocalBrowser.NavigateToAsync(conn.LastLocalPath);
                    }
                    OnSessionStateChanged();
                }

                if (dialog.ConnectionPassword != null)
                {
                    ApplySavedConnectionAction?.Invoke(conn, dialog.ConnectionPassword);
                    _ = ToggleConnectionAsync(null);
                }
                else
                {
                    ApplySavedConnectionAction?.Invoke(conn, string.Empty);
                }
            }
        }

        public async Task UploadSelectedAsync()
        {
            if (!IsConnected) return;

            var items = GetLocalSelectedItemsFunc?.Invoke() ?? 
                        (LocalBrowser.SelectedItem != null ? new[] { LocalBrowser.SelectedItem } : Array.Empty<FileItem>());

            await UploadItemsAsync(items, RemoteBrowser.CurrentPath);
        }

        public async Task DownloadSelectedAsync()
        {
            if (!IsConnected) return;

            var items = GetRemoteSelectedItemsFunc?.Invoke() ??
                        (RemoteBrowser.SelectedItem != null ? new[] { RemoteBrowser.SelectedItem } : Array.Empty<FileItem>());

            await DownloadItemsAsync(items, LocalBrowser.CurrentPath);
        }

        public Task UploadItemsAsync(IEnumerable<FileItem> items, string? targetRemotePath = null)
        {
            if (!IsConnected) return Task.CompletedTask;
            var destPath = string.IsNullOrWhiteSpace(targetRemotePath) ? RemoteBrowser.CurrentPath : targetRemotePath;

            var validItems = items.Where(i => i != null && !i.IsParent).ToList();
            if (!validItems.Any()) return Task.CompletedTask;

            var tasks = validItems.Select(item => new TransferTask
            {
                FileName = item.Name,
                SourcePath = item.FullPath,
                DestinationPath = destPath,
                Direction = TransferDirection.Upload
            }).ToList();

            EnqueueTransfers(tasks);
            return Task.CompletedTask;
        }

        public Task UploadPathsAsync(IEnumerable<string> localPaths, string? targetRemotePath = null)
        {
            if (!IsConnected) return Task.CompletedTask;
            var destPath = string.IsNullOrWhiteSpace(targetRemotePath) ? RemoteBrowser.CurrentPath : targetRemotePath;

            var validPaths = localPaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (!validPaths.Any()) return Task.CompletedTask;

            var tasks = validPaths.Select(path =>
            {
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return new TransferTask
                {
                    FileName = string.IsNullOrEmpty(name) ? path : name,
                    SourcePath = path,
                    DestinationPath = destPath,
                    Direction = TransferDirection.Upload
                };
            }).ToList();

            EnqueueTransfers(tasks);
            return Task.CompletedTask;
        }

        public Task DownloadItemsAsync(IEnumerable<FileItem> items, string? targetLocalPath = null)
        {
            if (!IsConnected) return Task.CompletedTask;
            var destPath = string.IsNullOrWhiteSpace(targetLocalPath) ? LocalBrowser.CurrentPath : targetLocalPath;

            var validItems = items.Where(i => i != null && !i.IsParent).ToList();
            if (!validItems.Any()) return Task.CompletedTask;

            var tasks = validItems.Select(item => new TransferTask
            {
                FileName = item.Name,
                SourcePath = item.FullPath,
                DestinationPath = destPath,
                Direction = TransferDirection.Download
            }).ToList();

            EnqueueTransfers(tasks);
            return Task.CompletedTask;
        }

        public void EnqueueTransfers(IEnumerable<TransferTask> tasks)
        {
            var list = tasks.ToList();
            if (!list.Any()) return;

            var profile = ActiveSession?.CreateConnectionProfile() ?? CreateConnectionProfile();
            var sessionId = ActiveSession?.Id;

            foreach (var t in list)
            {
                t.ConnectionProfile ??= profile;
                t.SessionId ??= sessionId;
                t.Status = TransferStatus.Pending;
            }

            RunOnUi(() =>
            {
                foreach (var t in list)
                {
                    ActiveTransfers.Add(t);
                }
            });

            _ = ProcessQueueAsync();
        }

        private async Task ProcessQueueAsync()
        {
            lock (_queueLock)
            {
                if (_isProcessingQueue) return;
                _isProcessingQueue = true;
            }

            try
            {
                while (true)
                {
                    List<TransferTask> batch = new();
                    RunOnUi(() =>
                    {
                        var firstPending = ActiveTransfers.FirstOrDefault(t => t.Status == TransferStatus.Pending);
                        if (firstPending != null)
                        {
                            var targetProfile = firstPending.ConnectionProfile ?? CreateConnectionProfile();
                            var targetDirection = firstPending.Direction;
                            var targetDest = firstPending.DestinationPath;

                            // Take up to 100 pending tasks sharing the same connection profile, direction, and destination path
                            batch = ActiveTransfers
                                .Where(t => t.Status == TransferStatus.Pending &&
                                            t.Direction == targetDirection &&
                                            string.Equals(t.DestinationPath, targetDest, StringComparison.OrdinalIgnoreCase) &&
                                            AreProfilesEqual(t.ConnectionProfile ?? CreateConnectionProfile(), targetProfile))
                                .Take(100)
                                .ToList();
                        }
                    });

                    if (!batch.Any()) break;

                    _currentTransferCts = new CancellationTokenSource();
                    var connection = batch[0].ConnectionProfile ?? CreateConnectionProfile();

                    try
                    {
                        var success = await _rsyncService.ExecuteBatchTransferAsync(batch, connection, _currentTransferCts.Token);
                        RunOnUi(() =>
                        {
                            foreach (var t in batch)
                            {
                                ActiveTransfers.Remove(t);
                                if (t.Status == TransferStatus.Completed)
                                {
                                    CompletedTransfers.Insert(0, t);
                                }
                                else if (t.Status == TransferStatus.Failed)
                                {
                                    FailedTransfers.Insert(0, t);
                                }
                            }
                        });

                        var anySuccess = batch.Any(t => t.Status == TransferStatus.Completed);
                        if (anySuccess)
                        {
                            if (batch[0].Direction == TransferDirection.Upload)
                            {
                                var targetSession = RemoteSessions.FirstOrDefault(s => s.Id == batch[0].SessionId) ?? ActiveSession;
                                if (targetSession != null)
                                {
                                    await targetSession.RemoteBrowser.RefreshAsync();
                                }
                            }
                            else
                            {
                                await LocalBrowser.RefreshAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        RunOnUi(() =>
                        {
                            foreach (var t in batch)
                            {
                                t.Status = TransferStatus.Failed;
                                if (string.IsNullOrEmpty(t.ErrorMessage))
                                    t.ErrorMessage = ex.Message;
                                ActiveTransfers.Remove(t);
                                FailedTransfers.Insert(0, t);
                            }
                        });
                    }
                    finally
                    {
                        _currentTransferCts?.Dispose();
                        _currentTransferCts = null;
                    }
                }
            }
            finally
            {
                lock (_queueLock)
                {
                    _isProcessingQueue = false;
                }
            }
        }

        private static bool AreProfilesEqual(ConnectionProfile a, ConnectionProfile b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(a.Username, b.Username, StringComparison.OrdinalIgnoreCase) &&
                   a.Port == b.Port &&
                   string.Equals(a.Password, b.Password, StringComparison.Ordinal);
        }

        public void CancelAllTransfers()
        {
            // Cancel running transfer
            _currentTransferCts?.Cancel();

            // Cancel all pending transfers
            RunOnUi(() =>
            {
                var pending = ActiveTransfers.Where(t => t.Status == TransferStatus.Pending).ToList();
                foreach (var t in pending)
                {
                    t.Status = TransferStatus.Cancelled;
                    ActiveTransfers.Remove(t);
                    FailedTransfers.Insert(0, t);
                }
            });
        }

        public void RetrySelectedFailed(TransferTask? task)
        {
            if (task == null) return;
            RunOnUi(() => FailedTransfers.Remove(task));
            task.ProgressPercentage = 0;
            task.Speed = "";
            task.Eta = "";
            task.TransferredInfo = "";
            task.ErrorMessage = "";
            task.ExitCode = null;
            EnqueueTransfers(new[] { task });
        }

        public void RetryAllFailed()
        {
            var list = FailedTransfers.ToList();
            if (!list.Any()) return;

            RunOnUi(() => FailedTransfers.Clear());
            foreach (var task in list)
            {
                task.ProgressPercentage = 0;
                task.Speed = "";
                task.Eta = "";
                task.TransferredInfo = "";
                task.ErrorMessage = "";
                task.ExitCode = null;
            }
            EnqueueTransfers(list);
        }

        private ConnectionProfile CreateConnectionProfile()
        {
            if (ActiveSession != null)
            {
                return ActiveSession.CreateConnectionProfile();
            }

            return new ConnectionProfile
            {
                Host = Host.Trim(),
                Username = Username.Trim(),
                Password = _cachedPassword,
                Port = Port
            };
        }

        public void AddLog(string message, bool isError)
        {
            RunOnUi(() =>
            {
                LogEntries.Add(new LogEntry { Message = message, IsError = isError });
                if (LogEntries.Count > 1000)
                {
                    LogEntries.RemoveAt(0);
                }
            });
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
    }
}
