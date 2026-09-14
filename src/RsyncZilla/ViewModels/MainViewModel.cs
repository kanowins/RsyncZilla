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

        private string _statusText = "Desconectado";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string ConnectionButtonText => IsConnected ? "Desconectar" : "Conexión rápida";
        public string ConnectionStatusIndicator => IsConnected ? "🟢 Conectado" : "⚪ Desconectado";

        public FileBrowserViewModel LocalBrowser { get; }
        public FileBrowserViewModel RemoteBrowser { get; }

        public ObservableCollection<TransferTask> ActiveTransfers { get; } = new();
        public ObservableCollection<TransferTask> CompletedTransfers { get; } = new();
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        private CancellationTokenSource? _transferCts;
        private readonly SemaphoreSlim _transferSemaphore = new(1, 1);

        public ICommand ConnectCommand { get; }
        public ICommand UploadSelectedCommand { get; }
        public ICommand DownloadSelectedCommand { get; }
        public ICommand CancelTransferCommand { get; }
        public ICommand ClearCompletedCommand { get; }
        public ICommand ClearLogsCommand { get; }

        public Func<IEnumerable<FileItem>>? GetLocalSelectedItemsFunc { get; set; }
        public Func<IEnumerable<FileItem>>? GetRemoteSelectedItemsFunc { get; set; }

        public MainViewModel()
        {
            _localService = new LocalFileService();
            _sftpService = new SftpService();
            _rsyncService = new RsyncService();

            LocalBrowser = new FileBrowserViewModel(_localService);
            RemoteBrowser = new FileBrowserViewModel(_sftpService);

            // Hook up logging
            _sftpService.LogMessageReceived += (msg, isErr) => AddLog(msg, isErr);
            _rsyncService.LogMessageReceived += (msg, isErr) => AddLog(msg, isErr);

            ConnectCommand = new RelayCommand(async (param) => await ToggleConnectionAsync(param));
            UploadSelectedCommand = new RelayCommand(async () => await UploadSelectedAsync(), () => IsConnected);
            DownloadSelectedCommand = new RelayCommand(async () => await DownloadSelectedAsync(), () => IsConnected);
            CancelTransferCommand = new RelayCommand(CancelActiveTransfer);
            ClearCompletedCommand = new RelayCommand(() => CompletedTransfers.Clear());
            ClearLogsCommand = new RelayCommand(() => LogEntries.Clear());

            AddLog("RsyncZilla inicializado. Listo para conectar.", false);
            var rsyncPath = _rsyncService.FindRsyncBinary();
            AddLog($"Motor rsync detectado en: {rsyncPath}", false);
        }

        private async Task ToggleConnectionAsync(object? param)
        {
            if (IsConnected)
            {
                _sftpService.Disconnect();
                IsConnected = false;
                StatusText = "Desconectado";
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
                MessageBox.Show("Por favor, ingrese Host y Nombre de usuario.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsConnecting = true;
            StatusText = "Conectando al servidor...";

            try
            {
                var success = await _sftpService.ConnectAsync(Host.Trim(), Port, Username.Trim(), _cachedPassword);
                if (success)
                {
                    IsConnected = true;
                    StatusText = $"Conectado a {Username}@{Host}:{Port}";
                    await RemoteBrowser.NavigateToAsync("/");
                }
                else
                {
                    IsConnected = false;
                    StatusText = "Error al conectar.";
                    MessageBox.Show("No se pudo conectar al servidor SFTP. Revise los registros inferiores para más detalles.", "Error de conexión", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                IsConnecting = false;
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

        public async Task UploadItemsAsync(IEnumerable<FileItem> items, string? targetRemotePath = null)
        {
            if (!IsConnected) return;
            var destPath = string.IsNullOrWhiteSpace(targetRemotePath) ? RemoteBrowser.CurrentPath : targetRemotePath;

            var validItems = items.Where(i => i != null && !i.IsParent).ToList();
            if (!validItems.Any()) return;

            var connection = CreateConnectionProfile();

            _ = Task.Run(async () =>
            {
                bool anySuccess = false;
                foreach (var item in validItems)
                {
                    var task = new TransferTask
                    {
                        FileName = item.Name,
                        SourcePath = item.FullPath,
                        DestinationPath = destPath,
                        Direction = TransferDirection.Upload
                    };

                    var success = await ExecuteTransferWithQueueAsync(task, connection);
                    if (success) anySuccess = true;
                }

                if (anySuccess)
                {
                    await RemoteBrowser.RefreshAsync();
                }
            });
        }

        public async Task UploadPathsAsync(IEnumerable<string> localPaths, string? targetRemotePath = null)
        {
            if (!IsConnected) return;
            var destPath = string.IsNullOrWhiteSpace(targetRemotePath) ? RemoteBrowser.CurrentPath : targetRemotePath;

            var validPaths = localPaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (!validPaths.Any()) return;

            var connection = CreateConnectionProfile();

            _ = Task.Run(async () =>
            {
                bool anySuccess = false;
                foreach (var path in validPaths)
                {
                    var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var task = new TransferTask
                    {
                        FileName = string.IsNullOrEmpty(name) ? path : name,
                        SourcePath = path,
                        DestinationPath = destPath,
                        Direction = TransferDirection.Upload
                    };

                    var success = await ExecuteTransferWithQueueAsync(task, connection);
                    if (success) anySuccess = true;
                }

                if (anySuccess)
                {
                    await RemoteBrowser.RefreshAsync();
                }
            });
        }

        public async Task DownloadItemsAsync(IEnumerable<FileItem> items, string? targetLocalPath = null)
        {
            if (!IsConnected) return;
            var destPath = string.IsNullOrWhiteSpace(targetLocalPath) ? LocalBrowser.CurrentPath : targetLocalPath;

            var validItems = items.Where(i => i != null && !i.IsParent).ToList();
            if (!validItems.Any()) return;

            var connection = CreateConnectionProfile();

            _ = Task.Run(async () =>
            {
                bool anySuccess = false;
                foreach (var item in validItems)
                {
                    var task = new TransferTask
                    {
                        FileName = item.Name,
                        SourcePath = item.FullPath,
                        DestinationPath = destPath,
                        Direction = TransferDirection.Download
                    };

                    var success = await ExecuteTransferWithQueueAsync(task, connection);
                    if (success) anySuccess = true;
                }

                if (anySuccess)
                {
                    await LocalBrowser.RefreshAsync();
                }
            });
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

        private async Task<bool> ExecuteTransferWithQueueAsync(TransferTask task, ConnectionProfile connection)
        {
            await _transferSemaphore.WaitAsync();
            RunOnUi(() => ActiveTransfers.Add(task));
            _transferCts = new CancellationTokenSource();

            try
            {
                var success = await _rsyncService.ExecuteTransferAsync(task, connection, _transferCts.Token);
                RunOnUi(() =>
                {
                    ActiveTransfers.Remove(task);
                    CompletedTransfers.Insert(0, task);
                });
                return success;
            }
            catch (Exception ex)
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = ex.Message;
                RunOnUi(() =>
                {
                    ActiveTransfers.Remove(task);
                    CompletedTransfers.Insert(0, task);
                });
                return false;
            }
            finally
            {
                _transferCts?.Dispose();
                _transferCts = null;
                _transferSemaphore.Release();
            }
        }

        private void CancelActiveTransfer()
        {
            _transferCts?.Cancel();
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
