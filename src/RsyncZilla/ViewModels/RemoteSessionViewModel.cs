using System;
using System.Windows.Input;
using RsyncZilla.Models;
using RsyncZilla.Services;

namespace RsyncZilla.ViewModels
{
    public class RemoteSessionViewModel : ViewModelBase, IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();

        private string? _siteName;
        public string? SiteName
        {
            get => _siteName;
            set
            {
                if (SetProperty(ref _siteName, value))
                {
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

        public bool IsConnected => SftpService.IsConnected;

        public string StatusIndicator => IsConnected ? "🟢" : (IsConnecting ? "🟡" : "⚪");

        public string DisplayHost => !string.IsNullOrWhiteSpace(Host) ? $"{Host}:{Port}" : "Disconnected";

        public string Title
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(SiteName))
                    return SiteName;

                if (!string.IsNullOrWhiteSpace(Host))
                {
                    var userPrefix = !string.IsNullOrWhiteSpace(Username) ? $"{Username}@" : "";
                    return $"{userPrefix}{Host}";
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

            CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this));
        }

        public void NotifyConnectionChanged()
        {
            OnPropertyChanged(nameof(IsConnected));
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
            try
            {
                SftpService.Disconnect();
            }
            catch { }

            NotifyConnectionChanged();
        }

        public void Dispose()
        {
            Disconnect();
            SftpService.Dispose();
        }
    }
}
