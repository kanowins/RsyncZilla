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
        private readonly SftpService _sftpService;
        private readonly RsyncService _rsyncService;
        private readonly ConnectionManagerService _connectionManagerService;

        private string _host = "";
        public string Host
        {
            get => _host;
            set => SetProperty(ref _host, value);
        }

        private string _username = "";
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        private string _cachedPassword = "";

        private int _port = 22;
        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(ConnectionButtonText));
                    OnPropertyChanged(nameof(ConnectionStatusIndicator));
                }
            }
        }

        private bool _isConnecting;
        public bool IsConnecting
        {
            get => _isConnecting;
            set => SetProperty(ref _isConnecting, value);
        }

        private string _statusText = "Disconnected";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string ConnectionButtonText => IsConnected ? "Disconnect" : "Quick Connect";
        public string ConnectionStatusIndicator => IsConnected ? "🟢 Connected" : "⚪ Disconnected";

        public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        public string FooterInfo => $"RsyncZilla v{AppVersion} | Engine: rsync 3.3.0 portable (Cygwin64) + SSH.NET";

        public string ActiveTabHeader => $"🚀 Queue ({ActiveTransfers.Count})";
        public string FailedTabHeader => $"❌ Failed ({FailedTransfers.Count})";
        public string CompletedTabHeader => $"✅ Completed ({CompletedTransfers.Count})";

        public FileBrowserViewModel LocalBrowser { get; }
        public FileBrowserViewModel RemoteBrowser { get; }

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
            _sftpService = new SftpService();
            _rsyncService = new RsyncService();
            _connectionManagerService = new ConnectionManagerService();

            LocalBrowser = new FileBrowserViewModel(_localService);
            RemoteBrowser = new FileBrowserViewModel(_sftpService);

            // Hook up logging
            _sftpService.LogMessageReceived += (msg, isErr) => AddLog(msg, isErr);
            _rsyncService.LogMessageReceived += (msg, isErr) => AddLog(msg, isErr);

            ConnectCommand = new RelayCommand(async (param) => await ToggleConnectionAsync(param));
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

            // Persist paths immediately on change when connected to a site
            LocalBrowser.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FileBrowserViewModel.CurrentPath))
                {
                    OnBrowserPathChanged();
                }
            };

            RemoteBrowser.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FileBrowserViewModel.CurrentPath))
                {
                    OnBrowserPathChanged();
                }
            };

            AddLog($"RsyncZilla v{AppVersion} initialized. Ready to connect.", false);
            var rsyncPath = _rsyncService.FindRsyncBinary();
            AddLog($"rsync engine detected at: {rsyncPath}", false);
        }

        private void OnBrowserPathChanged()
        {
            if (IsConnected && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username))
            {
                _connectionManagerService.UpdatePaths(Host.Trim(), Username.Trim(), Port, LocalBrowser.CurrentPath, RemoteBrowser.CurrentPath);
            }
        }

        private async Task ToggleConnectionAsync(object? param)
        {
            if (IsConnected)
            {
                _sftpService.Disconnect();
                IsConnected = false;
                StatusText = "Disconnected";
                RunOnUi(() =>
                {
                    RemoteBrowser.Items.Clear();
                    RemoteBrowser.CurrentPath = "";
                });
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

            IsConnecting = true;
            StatusText = "Connecting to server...";

            try
            {
                var (success, error) = await _sftpService.ConnectAsync(Host.Trim(), Port, Username.Trim(), _cachedPassword);
                if (success)
                {
                    // Look up if this connection has previously saved paths
                    var saved = _connectionManagerService.FindConnection(Host.Trim(), Username.Trim(), Port);

                    if (saved != null && !string.IsNullOrWhiteSpace(saved.LastLocalPath) && 
                        (Directory.Exists(saved.LastLocalPath) || saved.LastLocalPath.Equals("This PC", StringComparison.OrdinalIgnoreCase)))
                    {
                        await LocalBrowser.NavigateToAsync(saved.LastLocalPath);
                    }

                    // Navigate to user's remote home directory or saved last remote path
                    var initialPath = (saved != null && !string.IsNullOrWhiteSpace(saved.LastRemotePath))
                        ? saved.LastRemotePath
                        : (string.IsNullOrWhiteSpace(_sftpService.CurrentPath) ? "." : _sftpService.CurrentPath);

                    await RemoteBrowser.NavigateToAsync(initialPath);

                    IsConnected = true;
                    StatusText = $"Connected to {Username}@{Host}:{Port}";

                    // Save or update to connection manager (without password) and record current active paths
                    _connectionManagerService.SaveOrUpdate(
                        Host.Trim(), 
                        Username.Trim(), 
                        Port, 
                        localPath: LocalBrowser.CurrentPath, 
                        remotePath: RemoteBrowser.CurrentPath);
                }
                else
                {
                    IsConnected = false;
                    StatusText = "Connection error.";
                    var msg = !string.IsNullOrWhiteSpace(error)
                        ? $"Could not connect to SFTP server:\n\n{error}"
                        : "Could not connect to SFTP server. Check logs below for details.";
                    MessageBox.Show(msg, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusText = "Connection error.";
                var errorMsg = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
                MessageBox.Show($"Connection error:\n\n{errorMsg}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsConnecting = false;
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
                Host = conn.Host;
                Username = !string.IsNullOrWhiteSpace(dialog.ConnectionUsername) ? dialog.ConnectionUsername : conn.Username;
                Port = conn.Port;

                if (!string.IsNullOrWhiteSpace(conn.LastLocalPath) && 
                    (Directory.Exists(conn.LastLocalPath) || conn.LastLocalPath.Equals("This PC", StringComparison.OrdinalIgnoreCase)))
                {
                    _ = LocalBrowser.NavigateToAsync(conn.LastLocalPath);
                }

                if (dialog.ConnectionPassword != null)
                {
                    _cachedPassword = dialog.ConnectionPassword;
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

            RunOnUi(() =>
            {
                foreach (var t in list)
                {
                    t.Status = TransferStatus.Pending;
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
                    TransferTask? nextTask = null;
                    RunOnUi(() =>
                    {
                        nextTask = ActiveTransfers.FirstOrDefault(t => t.Status == TransferStatus.Pending);
                    });

                    if (nextTask == null) break;

                    _currentTransferCts = new CancellationTokenSource();
                    var connection = CreateConnectionProfile();

                    try
                    {
                        var success = await _rsyncService.ExecuteTransferAsync(nextTask, connection, _currentTransferCts.Token);
                        RunOnUi(() =>
                        {
                            ActiveTransfers.Remove(nextTask);
                            if (success)
                            {
                                CompletedTransfers.Insert(0, nextTask);
                            }
                            else
                            {
                                FailedTransfers.Insert(0, nextTask);
                            }
                        });

                        if (success)
                        {
                            if (nextTask.Direction == TransferDirection.Upload)
                                await RemoteBrowser.RefreshAsync();
                            else
                                await LocalBrowser.RefreshAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        nextTask.Status = TransferStatus.Failed;
                        nextTask.ErrorMessage = ex.Message;
                        RunOnUi(() =>
                        {
                            ActiveTransfers.Remove(nextTask);
                            FailedTransfers.Insert(0, nextTask);
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
                    t.ErrorMessage = "Cancelled by user.";
                    ActiveTransfers.Remove(t);
                    FailedTransfers.Insert(0, t);
                }
            });

            AddLog("All active and pending transfers have been cancelled.", true);
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
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(action);
                }
            }
            else
            {
                action();
            }
        }
    }
}
