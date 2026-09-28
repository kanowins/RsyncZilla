using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using RsyncZilla.Models;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;

namespace RsyncZilla.Views
{
    public partial class TerminalView : UserControl, IDisposable
    {
        private readonly RemoteSessionViewModel _session;
        private readonly MainViewModel _mainViewModel;
        private bool _isWebViewInitialized;
        private bool _isDisposed;
        private uint _currentCols = 80;
        private uint _currentRows = 24;

        // Directory synchronization state
        private bool _isSyncEnabled = true;
        private bool _isSyncing;
        private string? _homeDirectory;
        private string? _lastKnownTerminalPath;

        public TerminalView(RemoteSessionViewModel session, MainViewModel mainViewModel)
        {
            InitializeComponent();
            _session = session;
            _mainViewModel = mainViewModel;
            DataContext = _session;

            if (!string.IsNullOrWhiteSpace(_session.RemoteBrowser.CurrentPath))
            {
                _homeDirectory = _session.RemoteBrowser.CurrentPath;
                _lastKnownTerminalPath = _session.RemoteBrowser.CurrentPath;
            }

            _session.RemoteBrowser.PropertyChanged += OnRemoteBrowserPropertyChanged;

            Loaded += TerminalView_Loaded;
            Unloaded += TerminalView_Unloaded;
        }

        private async void TerminalView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isWebViewInitialized)
            {
                await InitializeWebViewAsync();
            }
            else
            {
                // Trigger re-fit when tab becomes visible again
                try
                {
                    await TerminalWebView.CoreWebView2.ExecuteScriptAsync("if (typeof reportResize === 'function') reportResize();");
                    TerminalWebView.Focus();
                }
                catch { }
            }
        }

        private void TerminalView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Keep WebView2 alive while in visual tree
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                ShowConnecting($"Initializing terminal for {_session.Username}@{_session.Host}...");

                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RsyncZilla",
                    "WebView2");
                Directory.CreateDirectory(userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await TerminalWebView.EnsureCoreWebView2Async(env);

                TerminalWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                TerminalWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                TerminalWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                TerminalWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

                var terminalFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Terminal");
                if (Directory.Exists(terminalFolder))
                {
                    TerminalWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "terminal.rsynczilla",
                        terminalFolder,
                        CoreWebView2HostResourceAccessKind.Allow);
                    TerminalWebView.CoreWebView2.Navigate("https://terminal.rsynczilla/terminal.html");
                }
                else
                {
                    ShowError("Terminal resources folder was not found.");
                    return;
                }

                TerminalWebView.WebMessageReceived += OnWebMessageReceived;
                _isWebViewInitialized = true;
            }
            catch (Exception ex)
            {
                ShowError($"Failed to initialize terminal engine: {ex.Message}");
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp)) return;
                var type = typeProp.GetString();

                switch (type)
                {
                    case "ready":
                        _currentCols = root.TryGetProperty("cols", out var c) ? c.GetUInt32() : 80;
                        _currentRows = root.TryGetProperty("rows", out var r) ? r.GetUInt32() : 24;
                        _ = ConnectSshAsync(_currentCols, _currentRows);
                        break;

                    case "resize":
                        _currentCols = root.TryGetProperty("cols", out var rc) ? rc.GetUInt32() : 80;
                        _currentRows = root.TryGetProperty("rows", out var rr) ? rr.GetUInt32() : 24;
                        _session.TerminalSession?.ChangeSize(_currentCols, _currentRows);
                        break;

                    case "input":
                        if (root.TryGetProperty("data", out var dataProp))
                        {
                            var data = dataProp.GetString();
                            if (!string.IsNullOrEmpty(data))
                            {
                                _session.TerminalSession?.SendInput(data);
                            }
                        }
                        break;

                    case "copy_selection":
                        if (root.TryGetProperty("text", out var textProp))
                        {
                            var text = textProp.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                Clipboard.SetText(text);
                            }
                        }
                        break;

                    case "request_paste":
                        if (Clipboard.ContainsText())
                        {
                            var text = Clipboard.GetText();
                            if (!string.IsNullOrEmpty(text))
                            {
                                _session.TerminalSession?.SendInput(text);
                            }
                        }
                        break;

                    case "dir_changed":
                        if (root.TryGetProperty("path", out var pathProp))
                        {
                            var rawPath = pathProp.GetString();
                            HandleTerminalDirectoryChanged(rawPath);
                        }
                        break;
                }
            }
            catch { }
        }

        private async Task ConnectSshAsync(uint cols, uint rows)
        {
            if (_session == null) return;

            ShowConnecting($"Connecting SSH terminal to {_session.Username}@{_session.Host}:{_session.Port}...");

            try
            {
                _session.TerminalSession?.Dispose();

                var terminal = new SshTerminalSession(
                    _session.Host,
                    _session.Port,
                    _session.Username,
                    _session.Password,
                    _session.InitialTerminalPath);

                terminal.OutputReceived += text =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            TerminalWebView.CoreWebView2?.PostWebMessageAsString(text);
                        }
                        catch { }
                    });
                };

                terminal.Disconnected += () =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        StatusDot.Text = "🔴";
                        TerminalTitleText.Text = $"{_session.Username}@{_session.Host} (Disconnected)";
                        _session.NotifyConnectionChanged();
                    });
                };

                terminal.ErrorOccurred += err =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        _mainViewModel.AddLog($"[Terminal Error] {err}", true);
                    });
                };

                var (success, error) = await terminal.ConnectAsync(cols, rows);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (success)
                    {
                        _session.TerminalSession = terminal;
                        StatusDot.Text = "🟢";
                        TerminalTitleText.Text = $"{_session.Username}@{_session.Host}:{_session.Port}";
                        HideConnecting();
                        _session.NotifyConnectionChanged();
                        _mainViewModel.OnSessionStateChanged();

                        TerminalWebView.Focus();
                        TerminalWebView.CoreWebView2?.ExecuteScriptAsync("term.focus();");
                    }
                    else
                    {
                        ShowError($"Failed to connect to {_session.Host}: {error}");
                    }
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowError($"SSH connection exception: {ex.Message}");
                });
            }
        }

        private void ShowConnecting(string message)
        {
            ConnectingText.Text = message;
            ConnectingProgressBar.Visibility = Visibility.Visible;
            RetryButton.Visibility = Visibility.Collapsed;
            ConnectingOverlay.Visibility = Visibility.Visible;
            StatusDot.Text = "🟡";
            TerminalTitleText.Text = "Connecting...";
        }

        private void HideConnecting()
        {
            ConnectingOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            ConnectingText.Text = message;
            ConnectingProgressBar.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Visible;
            ConnectingOverlay.Visibility = Visibility.Visible;
            StatusDot.Text = "🔴";
            TerminalTitleText.Text = $"{_session.Username}@{_session.Host} (Error)";
        }

        // ==========================================
        // TERMINAL TOOLBAR ACTIONS
        // ==========================================

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TerminalWebView.CoreWebView2 != null)
                {
                    await TerminalWebView.CoreWebView2.ExecuteScriptAsync(
                        "if (term.hasSelection()) { window.chrome.webview.postMessage({ type: 'copy_selection', text: term.getSelection() }); }");
                }
            }
            catch { }
        }

        private void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    _session.TerminalSession?.SendInput(text);
                    TerminalWebView.Focus();
                }
            }
            catch { }
        }

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TerminalWebView.CoreWebView2 != null)
                {
                    await TerminalWebView.CoreWebView2.ExecuteScriptAsync("term.clear();");
                    TerminalWebView.Focus();
                }
            }
            catch { }
        }

        private void ReconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ConnectSshAsync(_currentCols, _currentRows);
        }

        // ==========================================
        // DIRECTORY SYNCHRONIZATION
        // ==========================================

        private void SyncDirToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isSyncEnabled = SyncDirToggleButton.IsChecked == true;
            SyncDirToggleButton.ToolTip = _isSyncEnabled
                ? "Directory Synchronization: ON (Tree and Terminal sync automatically)"
                : "Directory Synchronization: OFF (Click to sync tree & terminal directories)";

            if (_isSyncEnabled && !string.IsNullOrWhiteSpace(_session.RemoteBrowser.CurrentPath))
            {
                var escaped = _session.RemoteBrowser.CurrentPath.Replace("'", "'\\''");
                _session.TerminalSession?.SendInput($"cd '{escaped}'\r");
                TerminalWebView.Focus();
            }
        }

        private void OnRemoteBrowserPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileBrowserViewModel.CurrentPath))
            {
                var newPath = _session.RemoteBrowser.CurrentPath;
                if (string.IsNullOrWhiteSpace(newPath)) return;

                if (string.IsNullOrEmpty(_homeDirectory))
                {
                    _homeDirectory = newPath;
                }

                if (_isSyncEnabled && !_isSyncing)
                {
                    if (!string.Equals(_lastKnownTerminalPath, newPath, StringComparison.Ordinal))
                    {
                        _isSyncing = true;
                        _lastKnownTerminalPath = newPath;
                        try
                        {
                            var escaped = newPath.Replace("'", "'\\''");
                            _session.TerminalSession?.SendInput($"cd '{escaped}'\r");
                        }
                        catch { }
                        finally
                        {
                            _ = Task.Delay(600).ContinueWith(_ => _isSyncing = false);
                        }
                    }
                }
            }
        }

        public static string ResolveRemotePath(string rawPath, string? homeDirectory)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return "";
            var resolved = rawPath.Trim();
            if (resolved == "~" && !string.IsNullOrEmpty(homeDirectory))
            {
                return homeDirectory;
            }
            if (resolved.StartsWith("~/") && !string.IsNullOrEmpty(homeDirectory))
            {
                return homeDirectory.TrimEnd('/') + resolved.Substring(1);
            }
            return resolved;
        }

        private async void HandleTerminalDirectoryChanged(string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath) || !_isSyncEnabled) return;

            var resolvedPath = ResolveRemotePath(rawPath, _homeDirectory);
            if (!resolvedPath.StartsWith("/")) return;

            _lastKnownTerminalPath = resolvedPath;

            if (_isSyncing) return;

            if (string.Equals(_session.RemoteBrowser.CurrentPath, resolvedPath, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                _isSyncing = true;
                await _session.RemoteBrowser.NavigateToAsync(resolvedPath);
            }
            catch { }
            finally
            {
                _ = Task.Delay(600).ContinueWith(_ => _isSyncing = false);
            }
        }

        // ==========================================
        // COMPACT REMOTE BROWSER ACTIONS
        // ==========================================

        private async void RemotePathTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await _session.RemoteBrowser.NavigateToAsync(RemotePathTextBox.Text);
            }
        }

        private async void RemoteDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item)
            {
                if (item.IsDirectory)
                {
                    await _session.RemoteBrowser.NavigateToAsync(item.FullPath);
                }
                else if (!item.IsParent)
                {
                    _mainViewModel.EditRemoteFileCommand.Execute(item);
                }
            }
        }

        private void RemoteDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void RemoteDataGrid_DragEnter(object sender, DragEventArgs e)
        {
            RemoteDataGrid_DragOver(sender, e);
        }

        private void RemoteDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (!_session.IsConnected) return;

            var pos = e.GetPosition(RemoteDataGrid);
            var targetItem = GetItemAtPosition(RemoteDataGrid, pos);

            string targetPath = _session.RemoteBrowser.CurrentPath;
            if (targetItem != null && targetItem.IsDirectory && !targetItem.IsParent)
            {
                targetPath = targetItem.FullPath;
            }

            var paths = DropDataHelper.ExtractLocalPaths(e.Data);
            if (paths.Count > 0)
            {
                _ = _mainViewModel.UploadPathsAsync(paths, targetPath);
                e.Handled = true;
            }
        }

        private static FileItem? GetItemAtPosition(DataGrid grid, Point position)
        {
            var element = grid.InputHitTest(position) as DependencyObject;
            while (element != null && element != grid)
            {
                if (element is DataGridRow row && row.Item is FileItem item)
                {
                    return item;
                }
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private void UploadFilesButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Files to Upload",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
            {
                _ = _mainViewModel.UploadPathsAsync(dlg.FileNames, _session.RemoteBrowser.CurrentPath);
            }
        }

        private void UploadFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select Folder to Upload"
            };

            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            {
                _ = _mainViewModel.UploadPathsAsync(new[] { dlg.FolderName }, _session.RemoteBrowser.CurrentPath);
            }
        }

        private async void DownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = RemoteDataGrid.SelectedItems.Cast<FileItem>()
                .Where(i => i != null && !i.IsParent).ToList();

            if (!selected.Any()) return;

            var dlg = new OpenFolderDialog
            {
                Title = "Select Local Destination Folder"
            };

            if (!string.IsNullOrWhiteSpace(_session.LastLocalPath) && Directory.Exists(_session.LastLocalPath))
            {
                dlg.InitialDirectory = _session.LastLocalPath;
            }

            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            {
                await _mainViewModel.DownloadItemsAsync(selected, dlg.FolderName);
            }
        }

        private void CdInTerminal_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item && item.IsDirectory && !item.IsParent)
            {
                var escapedPath = item.FullPath.Replace("'", "'\\''");
                _session.TerminalSession?.SendInput($"cd '{escapedPath}'\r");
                TerminalWebView.Focus();
                TerminalWebView.CoreWebView2?.ExecuteScriptAsync("term.focus();");
            }
        }

        private void EditRemoteFile_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item && !item.IsDirectory && !item.IsParent)
            {
                _mainViewModel.EditRemoteFileCommand.Execute(item);
            }
        }

        private async void NewRemoteFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("New Remote Folder", "Enter name for the new remote folder:", "new_folder")
            {
                Owner = Application.Current.MainWindow
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResponseText))
            {
                await _session.RemoteBrowser.CreateFolderAsync(dlg.ResponseText.Trim());
            }
        }

        private async void RenameRemoteItem_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item && !item.IsParent)
            {
                var dlg = new InputDialog("Rename Remote Item", "Enter new name:", item.Name)
                {
                    Owner = Application.Current.MainWindow
                };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResponseText))
                {
                    var newName = dlg.ResponseText.Trim();
                    if (!string.Equals(newName, item.Name, StringComparison.Ordinal))
                    {
                        await _session.RemoteBrowser.RenameItemAsync(item, newName);
                    }
                }
            }
        }

        private async void DeleteRemoteItem_Click(object sender, RoutedEventArgs e)
        {
            var selected = RemoteDataGrid.SelectedItems.Cast<FileItem>()
                .Where(i => i != null && !i.IsParent).ToList();

            if (!selected.Any()) return;

            string prompt = selected.Count == 1
                ? $"Are you sure you want to delete '{selected[0].Name}' from the remote server?"
                : $"Are you sure you want to delete the {selected.Count} selected items from the remote server?";

            var confirm = MessageBox.Show(prompt, "Confirm Remote Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                await _session.RemoteBrowser.DeleteItemsAsync(selected);
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item && !item.IsParent)
            {
                Clipboard.SetText(item.FullPath);
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                try
                {
                    _session.RemoteBrowser.PropertyChanged -= OnRemoteBrowserPropertyChanged;
                }
                catch { }

                try
                {
                    TerminalWebView.WebMessageReceived -= OnWebMessageReceived;
                    TerminalWebView.Dispose();
                }
                catch { }

                try
                {
                    _session.TerminalSession?.Dispose();
                }
                catch { }
            }
        }
    }
}
