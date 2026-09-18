using System;
using System.Windows.Media;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;

namespace RsyncZilla.Models
{
    public class FileItem : ViewModelBase
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                {
                    _icon = null;
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        private string _fullPath = string.Empty;
        public string FullPath
        {
            get => _fullPath;
            set => SetProperty(ref _fullPath, value);
        }

        private bool _isDirectory;
        public bool IsDirectory
        {
            get => _isDirectory;
            set
            {
                if (SetProperty(ref _isDirectory, value))
                {
                    _icon = null;
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        private bool _isDrive;
        public bool IsDrive
        {
            get => _isDrive;
            set
            {
                if (SetProperty(ref _isDrive, value))
                {
                    _icon = null;
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        private long _length;
        public long Length
        {
            get => _length;
            set
            {
                if (SetProperty(ref _length, value))
                {
                    OnPropertyChanged(nameof(DisplaySize));
                }
            }
        }

        private DateTime _lastWriteTime;
        public DateTime LastWriteTime
        {
            get => _lastWriteTime;
            set => SetProperty(ref _lastWriteTime, value);
        }

        private string _permissions = string.Empty;
        public string Permissions
        {
            get => _permissions;
            set => SetProperty(ref _permissions, value);
        }

        private bool _isParent;
        public bool IsParent
        {
            get => _isParent;
            set
            {
                if (SetProperty(ref _isParent, value))
                {
                    _icon = null;
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        private ImageSource? _icon;
        public ImageSource? Icon => _icon ??= ShellIconHelper.GetIcon(IsDirectory, Name, IsDrive, IsParent);

        public string DisplaySize
        {
            get
            {
                if (IsDrive) return FormatBytes(Length);
                if (IsDirectory) return "<DIR>";
                return FormatBytes(Length);
            }
        }

        public string DisplayType
        {
            get
            {
                if (IsParent) return "Up one level";
                if (IsDrive) return "Drive";
                if (IsDirectory) return "File folder";
                var ext = System.IO.Path.GetExtension(Name).ToLowerInvariant();
                return string.IsNullOrEmpty(ext) ? "File" : $"{ext.TrimStart('.').ToUpper()} File";
            }
        }

        public string IconEmoji
        {
            get
            {
                if (IsParent) return "📁 ⬆";
                if (IsDrive) return "💾";
                if (IsDirectory) return "📁";
                var ext = System.IO.Path.GetExtension(Name).ToLowerInvariant();
                return ext switch
                {
                    ".zip" or ".tar" or ".gz" or ".rar" or ".7z" => "📦",
                    ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" => "🖼️",
                    ".cs" or ".js" or ".ts" or ".html" or ".css" or ".py" or ".php" or ".json" or ".xml" => "📜",
                    ".exe" or ".bat" or ".cmd" or ".sh" => "⚙️",
                    ".pdf" => "📕",
                    ".txt" or ".md" or ".log" => "📝",
                    _ => "📄"
                };
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dBytes = bytes;
            while (dBytes >= 1024 && i < suffixes.Length - 1)
            {
                dBytes /= 1024;
                i++;
            }
            return $"{dBytes:0.##} {suffixes[i]}";
        }
    }
}
