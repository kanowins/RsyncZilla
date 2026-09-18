using System;
using System.Collections.ObjectModel;
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

        public TransferTask()
        {
            Children.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(IsExpandable));
                OnPropertyChanged(nameof(ChildrenCount));
                OnPropertyChanged(nameof(HasChildren));
            };
        }

        private string _fileName = string.Empty;
        public string FileName
        {
            get => _fileName;
            set
            {
                if (SetProperty(ref _fileName, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(CleanDisplayName));
                }
            }
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
                    OnPropertyChanged(nameof(IsUpload));
                    OnPropertyChanged(nameof(DirectionText));
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
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(IsRunning));
                    OnPropertyChanged(nameof(IsExpandable));
                    OnPropertyChanged(nameof(ProgressSummary));
                }
            }
        }

        private int _progressPercentage;
        public int ProgressPercentage
        {
            get => _progressPercentage;
            set
            {
                if (SetProperty(ref _progressPercentage, value))
                {
                    OnPropertyChanged(nameof(ProgressSummary));
                }
            }
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
            set
            {
                if (SetProperty(ref _transferredInfo, value))
                {
                    OnPropertyChanged(nameof(LiveStatusDetail));
                    OnPropertyChanged(nameof(CleanLiveStatusDetail));
                }
            }
        }

        private bool _isDirectory;
        public bool IsDirectory
        {
            get => _isDirectory;
            set
            {
                if (SetProperty(ref _isDirectory, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(CleanDisplayName));
                    OnPropertyChanged(nameof(IsExpandable));
                    OnPropertyChanged(nameof(DisplaySize));
                    OnPropertyChanged(nameof(ProgressSummary));
                }
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        private string? _currentSubFile;
        public string? CurrentSubFile
        {
            get => _currentSubFile;
            set
            {
                if (SetProperty(ref _currentSubFile, value))
                {
                    OnPropertyChanged(nameof(LiveStatusDetail));
                    OnPropertyChanged(nameof(CleanLiveStatusDetail));
                }
            }
        }

        private int _totalItemsCount;
        public int TotalItemsCount
        {
            get => _totalItemsCount;
            set
            {
                if (SetProperty(ref _totalItemsCount, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(CleanDisplayName));
                    OnPropertyChanged(nameof(ProgressSummary));
                }
            }
        }

        private int _completedItemsCount;
        public int CompletedItemsCount
        {
            get => _completedItemsCount;
            set
            {
                if (SetProperty(ref _completedItemsCount, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(CleanDisplayName));
                    OnPropertyChanged(nameof(ProgressSummary));
                }
            }
        }

        private long _totalBytes;
        public long TotalBytes
        {
            get => _totalBytes;
            set
            {
                if (SetProperty(ref _totalBytes, value))
                {
                    OnPropertyChanged(nameof(DisplaySize));
                }
            }
        }

        private long _transferredBytes;
        public long TransferredBytes
        {
            get => _transferredBytes;
            set => SetProperty(ref _transferredBytes, value);
        }

        private long _fileSize;
        public long FileSize
        {
            get => _fileSize;
            set
            {
                if (SetProperty(ref _fileSize, value))
                {
                    OnPropertyChanged(nameof(DisplaySize));
                }
            }
        }

        private bool _isChild;
        public bool IsChild
        {
            get => _isChild;
            set
            {
                if (SetProperty(ref _isChild, value))
                {
                    OnPropertyChanged(nameof(IsExpandable));
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(CleanDisplayName));
                    OnPropertyChanged(nameof(ProgressSummary));
                    OnPropertyChanged(nameof(LiveStatusDetail));
                    OnPropertyChanged(nameof(CleanLiveStatusDetail));
                    OnPropertyChanged(nameof(DisplaySize));
                }
            }
        }

        public TransferTask? ParentTask { get; set; }

        public ObservableCollection<TransferTask> Children { get; } = new();
        public int ChildrenCount => Children.Count;
        public bool HasChildren => Children.Count > 0;
        public bool IsExpandable => IsDirectory && !IsChild && (Status == TransferStatus.Pending || Status == TransferStatus.Running || Children.Count > 0);

        public string DisplayName
        {
            get
            {
                if (IsDirectory && !IsChild)
                {
                    if (TotalItemsCount > 0)
                    {
                        return $"📁 {FileName} ({CompletedItemsCount}/{TotalItemsCount})";
                    }
                    return $"📁 {FileName}";
                }
                return FileName;
            }
        }

        public string CleanDisplayName
        {
            get
            {
                if (IsDirectory && !IsChild)
                {
                    if (TotalItemsCount > 0)
                    {
                        return $"{FileName} ({CompletedItemsCount}/{TotalItemsCount})";
                    }
                    return FileName;
                }
                return FileName;
            }
        }

        public string ProgressSummary
        {
            get
            {
                if (IsDirectory && !IsChild && TotalItemsCount > 0)
                {
                    return $"{CompletedItemsCount}/{TotalItemsCount} files ({ProgressPercentage}%)";
                }
                return $"{ProgressPercentage}%";
            }
        }

        public string LiveStatusDetail
        {
            get
            {
                if (IsDirectory && !IsChild && !string.IsNullOrWhiteSpace(CurrentSubFile))
                {
                    if (!string.IsNullOrWhiteSpace(TransferredInfo))
                        return $"📄 {CurrentSubFile} | {TransferredInfo}";
                    return $"📄 {CurrentSubFile}";
                }
                return TransferredInfo;
            }
        }

        public string CleanLiveStatusDetail
        {
            get
            {
                if (IsDirectory && !IsChild && !string.IsNullOrWhiteSpace(CurrentSubFile))
                {
                    if (!string.IsNullOrWhiteSpace(TransferredInfo))
                        return $"{CurrentSubFile} | {TransferredInfo}";
                    return CurrentSubFile;
                }
                return TransferredInfo;
            }
        }

        public string DisplaySize
        {
            get
            {
                if (IsDirectory && !IsChild && TotalBytes > 0) return FormatBytes(TotalBytes);
                if (FileSize > 0) return FormatBytes(FileSize);
                return string.Empty;
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
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

        public string StatusText => Status switch
        {
            TransferStatus.Pending => "Pending",
            TransferStatus.Running => "Transferring",
            TransferStatus.Completed => "Completed",
            TransferStatus.Failed => "Failed",
            TransferStatus.Cancelled => "Cancelled",
            _ => Status.ToString()
        };

        public string DirectionIcon => Direction == TransferDirection.Upload ? "➡️ Upload" : "⬅️ Download";
        public bool IsUpload => Direction == TransferDirection.Upload;
        public string DirectionText => IsUpload ? "Upload" : "Download";

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
