using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RsyncZilla.Models;
using RsyncZilla.Services;

namespace RsyncZilla.ViewModels
{
    public class FileBrowserViewModel : ViewModelBase
    {
        private readonly LocalFileService? _localService;
        private readonly SftpService? _sftpService;
        public bool IsRemote { get; }

        private string _currentPath = string.Empty;
        public string CurrentPath
        {
            get => _currentPath;
            set => SetProperty(ref _currentPath, value);
        }

        public ObservableCollection<FileItem> Items { get; } = new();

        private FileItem? _selectedItem;
        public FileItem? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string? _errorMessage;
        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public ICommand NavigateUpCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenItemCommand { get; }

        // Local constructor
        public FileBrowserViewModel(LocalFileService localService)
        {
            _localService = localService;
            IsRemote = false;

            NavigateUpCommand = new RelayCommand(NavigateUp, () => !IsLoading && CanNavigateUp());
            RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsLoading);
            OpenItemCommand = new RelayCommand(async (param) =>
            {
                if (param is FileItem item)
                {
                    await OpenItemAsync(item);
                }
            });

            // Initialize to current project / working directory
            var initial = Environment.CurrentDirectory;
            if (!Directory.Exists(initial))
            {
                initial = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            _ = NavigateToAsync(initial);
        }

        // Remote constructor
        public FileBrowserViewModel(SftpService sftpService)
        {
            _sftpService = sftpService;
            IsRemote = true;

            NavigateUpCommand = new RelayCommand(NavigateUp, () => !IsLoading && CanNavigateUp());
            RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsLoading && (_sftpService?.IsConnected == true));
            OpenItemCommand = new RelayCommand(async (param) =>
            {
                if (param is FileItem item)
                {
                    await OpenItemAsync(item);
                }
            });
        }

        public bool CanNavigateUp()
        {
            if (string.IsNullOrWhiteSpace(CurrentPath)) return false;
            if (IsRemote)
            {
                return CurrentPath != "/" && CurrentPath != "";
            }
            else
            {
                try
                {
                    var dir = new DirectoryInfo(CurrentPath);
                    return dir.Parent != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void NavigateUp()
        {
            if (!CanNavigateUp()) return;

            if (IsRemote)
            {
                var parent = GetRemoteParent(CurrentPath);
                _ = NavigateToAsync(parent);
            }
            else
            {
                try
                {
                    var dir = new DirectoryInfo(CurrentPath);
                    if (dir.Parent != null)
                    {
                        _ = NavigateToAsync(dir.Parent.FullName);
                    }
                }
                catch { }
            }
        }

        public async Task OpenItemAsync(FileItem item)
        {
            if (item.IsDirectory)
            {
                await NavigateToAsync(item.FullPath);
            }
        }

        public async Task NavigateToAsync(string path)
        {
            IsLoading = true;
            ErrorMessage = null;

            try
            {
                List<FileItem> result;
                string? err;

                if (IsRemote)
                {
                    if (_sftpService == null || !_sftpService.IsConnected)
                    {
                        RunOnUi(() => Items.Clear());
                        ErrorMessage = "No conectado.";
                        return;
                    }

                    (result, err) = await _sftpService.GetDirectoryContentsAsync(path);
                    if (err == null)
                    {
                        CurrentPath = _sftpService.CurrentPath;
                    }
                }
                else
                {
                    if (_localService == null) return;
                    (result, err) = await Task.Run(() => _localService.GetDirectoryContents(path));
                    if (err == null)
                    {
                        CurrentPath = Path.GetFullPath(path);
                    }
                }

                if (err != null)
                {
                    ErrorMessage = err;
                }
                else
                {
                    RunOnUi(() =>
                    {
                        Items.Clear();
                        foreach (var item in result)
                        {
                            Items.Add(item);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task RefreshAsync()
        {
            await NavigateToAsync(CurrentPath);
        }

        public async Task CreateFolderAsync(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return;

            if (IsRemote)
            {
                if (_sftpService == null || !_sftpService.IsConnected) return;
                var newPath = CurrentPath.TrimEnd('/') + "/" + folderName.Trim();
                var success = await _sftpService.CreateDirectoryAsync(newPath);
                if (success) await RefreshAsync();
            }
            else
            {
                if (_localService == null) return;
                var newPath = Path.Combine(CurrentPath, folderName.Trim());
                _localService.CreateDirectory(newPath);
                await RefreshAsync();
            }
        }

        public async Task DeleteItemAsync(FileItem item)
        {
            if (item.IsParent) return;

            if (IsRemote)
            {
                if (_sftpService == null || !_sftpService.IsConnected) return;
                var success = await _sftpService.DeleteItemAsync(item.FullPath, item.IsDirectory);
                if (success) await RefreshAsync();
            }
            else
            {
                if (_localService == null) return;
                _localService.DeleteItem(item.FullPath, item.IsDirectory);
                await RefreshAsync();
            }
        }

        private static string GetRemoteParent(string path)
        {
            if (path == "/" || string.IsNullOrEmpty(path)) return "/";
            var trimmed = path.TrimEnd('/');
            var lastSlash = trimmed.LastIndexOf('/');
            if (lastSlash <= 0) return "/";
            return trimmed.Substring(0, lastSlash);
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
