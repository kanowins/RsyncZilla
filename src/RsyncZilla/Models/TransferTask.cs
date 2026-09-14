using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RsyncZilla.Models
{
    public enum TransferDirection
    {
        Upload,
        Download
    }

    public enum TransferStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public class TransferTask : INotifyPropertyChanged
    {
        public Guid Id { get; } = Guid.NewGuid();

        private string _fileName = string.Empty;
        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value);
        }

        private string _sourcePath = string.Empty;
        public string SourcePath
        {
            get => _sourcePath;
            set
            {
                if (SetProperty(ref _sourcePath, value))
                {
                    OnPropertyChanged(nameof(DisplaySource));
                }
            }
        }

        private string _destinationPath = string.Empty;
        public string DestinationPath
        {
            get => _destinationPath;
            set
            {
                if (SetProperty(ref _destinationPath, value))
                {
                    OnPropertyChanged(nameof(DisplayDestination));
                }
            }
        }

        private TransferDirection _direction;
        public TransferDirection Direction
        {
            get => _direction;
            set
            {
                if (SetProperty(ref _direction, value))
                {
                    OnPropertyChanged(nameof(DirectionIcon));
                    OnPropertyChanged(nameof(DisplaySource));
                    OnPropertyChanged(nameof(DisplayDestination));
                }
            }
        }

        private TransferStatus _status = TransferStatus.Pending;
        public TransferStatus Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged(nameof(StatusBadge));
                    OnPropertyChanged(nameof(IsRunning));
                }
            }
        }

        private int _progressPercentage;
        public int ProgressPercentage
        {
            get => _progressPercentage;
            set => SetProperty(ref _progressPercentage, value);
        }

        private string _speed = string.Empty;
        public string Speed
        {
            get => _speed;
            set => SetProperty(ref _speed, value);
        }

        private string _transferredInfo = string.Empty;
        public string TransferredInfo
        {
            get => _transferredInfo;
            set => SetProperty(ref _transferredInfo, value);
        }

        private string _eta = string.Empty;
        public string Eta
        {
            get => _eta;
            set => SetProperty(ref _eta, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }
        public int? ExitCode { get; set; }

        private ConnectionProfile? _connectionProfile;
        public ConnectionProfile? ConnectionProfile
        {
            get => _connectionProfile;
            set
            {
                if (SetProperty(ref _connectionProfile, value))
                {
                    OnPropertyChanged(nameof(DisplaySource));
                    OnPropertyChanged(nameof(DisplayDestination));
                }
            }
        }

        public Guid? SessionId { get; set; }

        public bool IsRunning => Status == TransferStatus.Running;

        public string StatusBadge => Status switch
        {
            TransferStatus.Pending => "⏳ Pending",
            TransferStatus.Running => "🚀 Transferring",
            TransferStatus.Completed => "✅ Completed",
            TransferStatus.Failed => "❌ Failed",
            TransferStatus.Cancelled => "⏹ Cancelled",
            _ => Status.ToString()
        };

        public string DirectionIcon => Direction == TransferDirection.Upload ? "⬆️ Upload" : "⬇️ Download";

        public string DisplaySource
        {
            get
            {
                if (Direction == TransferDirection.Upload)
                {
                    return SourcePath;
                }
                return FormatRemotePath(SourcePath);
            }
        }

        public string DisplayDestination
        {
            get
            {
                if (Direction == TransferDirection.Upload)
                {
                    return FormatRemotePath(DestinationPath);
                }
                return DestinationPath;
            }
        }

        private string FormatRemotePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path ?? "";
            if (ConnectionProfile != null && !string.IsNullOrWhiteSpace(ConnectionProfile.Host))
            {
                var userPrefix = !string.IsNullOrWhiteSpace(ConnectionProfile.Username) ? $"{ConnectionProfile.Username}@" : "";
                var portSuffix = (ConnectionProfile.Port != 22 && ConnectionProfile.Port > 0) ? $":{ConnectionProfile.Port}" : "";
                return $"{userPrefix}{ConnectionProfile.Host}{portSuffix}:{path}";
            }
            return path;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
