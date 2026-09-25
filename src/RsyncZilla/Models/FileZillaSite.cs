using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RsyncZilla.Models
{
    public class FileZillaSite : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _name = string.Empty;
        private string _folder = string.Empty;
        private string _host = string.Empty;
        private int _port = 22;
        private string _username = string.Empty;
        private string _protocol = "SFTP";
        private string _remotePath = string.Empty;
        private string _localPath = string.Empty;
        private string _comments = string.Empty;
        private bool _isDuplicate;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Folder
        {
            get => _folder;
            set => SetProperty(ref _folder, value);
        }

        public string Host
        {
            get => _host;
            set => SetProperty(ref _host, value);
        }

        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public string Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        public string RemotePath
        {
            get => _remotePath;
            set => SetProperty(ref _remotePath, value);
        }

        public string LocalPath
        {
            get => _localPath;
            set => SetProperty(ref _localPath, value);
        }

        public string Comments
        {
            get => _comments;
            set => SetProperty(ref _comments, value);
        }

        public bool IsDuplicate
        {
            get => _isDuplicate;
            set
            {
                if (SetProperty(ref _isDuplicate, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string StatusText => IsDuplicate ? "Update existing" : "New";

        public SavedConnection ToSavedConnection()
        {
            var displayName = !string.IsNullOrWhiteSpace(Name)
                ? Name.Trim()
                : (!string.IsNullOrWhiteSpace(Folder) ? $"{Folder} / {Host.Trim()}" : $"{Username.Trim()}@{Host.Trim()}");

            return new SavedConnection
            {
                Id = Guid.NewGuid(),
                Name = displayName,
                Host = Host.Trim(),
                Port = Port == 21 ? 22 : (Port > 0 ? Port : 22),
                Username = Username.Trim(),
                LastLocalPath = LocalPath?.Trim() ?? string.Empty,
                LastRemotePath = RemotePath?.Trim() ?? string.Empty,
                LastUsed = DateTime.Now
            };
        }

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
