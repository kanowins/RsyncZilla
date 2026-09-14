using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RsyncZilla.Models;
using RsyncZilla.ViewModels;

namespace RsyncZilla
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        
        // Local drag tracking
        private Point _localDragStart;
        private bool _isLocalDragCandidate;
        private DataGridRow? _localDraggedRow;

        // Remote drag tracking
        private Point _remoteDragStart;
        private bool _isRemoteDragCandidate;
        private DataGridRow? _remoteDraggedRow;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Link multiple selection extractors
            _viewModel.GetLocalSelectedItemsFunc = () => LocalDataGrid.SelectedItems.Cast<FileItem>().ToList();
            _viewModel.GetRemoteSelectedItemsFunc = () => RemoteDataGrid.SelectedItems.Cast<FileItem>().ToList();

            // Update password field when a site is loaded from manager
            _viewModel.ApplySavedConnectionAction = (conn, pwd) =>
            {
                PasswordInput.Password = pwd ?? "";
            };

            // Auto-scroll logs safely
            ((INotifyCollectionChanged)_viewModel.LogEntries).CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (LogListBox != null && LogListBox.IsVisible && LogListBox.Items.Count > 0)
                            {
                                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
                            }
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            };
        }

        // ==========================================
        // PRESERVE MULTI-SELECTION ON RIGHT CLICK
        // ==========================================

        private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridRow row)
            {
                var grid = sender as DataGrid;
                if (row.IsSelected)
                {
                    // Row is already selected as part of a multi-selection:
                    // DO NOT let WPF clear other selected rows!
                    row.Focus();
                    e.Handled = true;
                }
                else if (grid != null)
                {
                    // Clicked on an unselected row: select only this row
                    grid.SelectedItems.Clear();
                    row.IsSelected = true;
                    row.Focus();
                }
            }
        }

        // ==========================================
        // DOUBLE CLICK NAVIGATION
        // ==========================================

        private async void LocalDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LocalDataGrid.SelectedItem is FileItem item)
            {
                await _viewModel.LocalBrowser.OpenItemAsync(item);
            }
        }

        private async void RemoteDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item)
            {
                await _viewModel.RemoteBrowser.OpenItemAsync(item);
            }
        }

        private async void LocalPathTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await _viewModel.LocalBrowser.NavigateToAsync(LocalPathTextBox.Text);
            }
        }

        private async void RemotePathTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await _viewModel.RemoteBrowser.NavigateToAsync(RemotePathTextBox.Text);
            }
        }

        // ==========================================
        // DRAG & DROP: LOCAL PANEL
        // ==========================================

        private void LocalDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            _localDragStart = e.GetPosition(null);

            if (dep is DataGridRow row && row.IsSelected && LocalDataGrid.SelectedItems.Count > 1)
            {
                // Clicking on an already selected row among multiple selected rows:
                // Don't deselect yet; wait to see if it's a drag or a simple click
                _isLocalDragCandidate = true;
                _localDraggedRow = row;
                e.Handled = true;
                return;
            }

            _isLocalDragCandidate = false;
            _localDraggedRow = null;
        }

        private void LocalDataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isLocalDragCandidate && _localDraggedRow != null)
            {
                _isLocalDragCandidate = false;
                // If the user clicked without dragging, select only this row (unless Ctrl/Shift held)
                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                {
                    LocalDataGrid.SelectedItems.Clear();
                    _localDraggedRow.IsSelected = true;
                }
                _localDraggedRow = null;
            }
        }

        private void LocalDataGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(null);
                var diff = _localDragStart - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isLocalDragCandidate = false;

                    var selected = LocalDataGrid.SelectedItems.Cast<FileItem>()
                        .Where(i => i != null && !i.IsParent).ToList();

                    if (!selected.Any()) return;

                    var data = new DataObject();
                    var paths = selected.Select(i => i.FullPath).ToArray();
                    data.SetData(DataFormats.FileDrop, paths);
                    data.SetData("RsyncZilla.Source", "Local");
                    data.SetData("RsyncZilla.Items", selected);

                    DragDrop.DoDragDrop(LocalDataGrid, data, DragDropEffects.Copy);
                }
            }
        }

        private void LocalDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("RsyncZilla.Source") &&
                e.Data.GetData("RsyncZilla.Source") as string == "Remote")
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void LocalDataGrid_DragEnter(object sender, DragEventArgs e)
        {
            LocalDataGrid_DragOver(sender, e);
        }

        private void LocalDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("RsyncZilla.Source") &&
                e.Data.GetData("RsyncZilla.Source") as string == "Remote")
            {
                var items = e.Data.GetData("RsyncZilla.Items") as List<FileItem>;
                if (items == null || !items.Any()) return;

                // Determine target directory (specific hovered folder or current folder)
                var pos = e.GetPosition(LocalDataGrid);
                var targetItem = GetItemAtPosition(LocalDataGrid, pos);

                string targetPath = _viewModel.LocalBrowser.CurrentPath;
                if (targetItem != null && targetItem.IsDirectory && !targetItem.IsParent)
                {
                    targetPath = targetItem.FullPath;
                }

                _ = _viewModel.DownloadItemsAsync(items, targetPath);
                e.Handled = true;
            }
        }

        // ==========================================
        // DRAG & DROP: REMOTE PANEL
        // ==========================================

        private void RemoteDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            _remoteDragStart = e.GetPosition(null);

            if (dep is DataGridRow row && row.IsSelected && RemoteDataGrid.SelectedItems.Count > 1)
            {
                _isRemoteDragCandidate = true;
                _remoteDraggedRow = row;
                e.Handled = true;
                return;
            }

            _isRemoteDragCandidate = false;
            _remoteDraggedRow = null;
        }

        private void RemoteDataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isRemoteDragCandidate && _remoteDraggedRow != null)
            {
                _isRemoteDragCandidate = false;
                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                {
                    RemoteDataGrid.SelectedItems.Clear();
                    _remoteDraggedRow.IsSelected = true;
                }
                _remoteDraggedRow = null;
            }
        }

        private void RemoteDataGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(null);
                var diff = _remoteDragStart - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isRemoteDragCandidate = false;

                    var selected = RemoteDataGrid.SelectedItems.Cast<FileItem>()
                        .Where(i => i != null && !i.IsParent).ToList();

                    if (!selected.Any()) return;

                    var data = new DataObject();
                    data.SetData("RsyncZilla.Source", "Remote");
                    data.SetData("RsyncZilla.Items", selected);

                    DragDrop.DoDragDrop(RemoteDataGrid, data, DragDropEffects.Copy);
                }
            }
        }

        private void RemoteDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (!_viewModel.IsConnected)
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop) ||
                (e.Data.GetDataPresent("RsyncZilla.Source") && e.Data.GetData("RsyncZilla.Source") as string == "Local"))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void RemoteDataGrid_DragEnter(object sender, DragEventArgs e)
        {
            RemoteDataGrid_DragOver(sender, e);
        }

        private void RemoteDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (!_viewModel.IsConnected) return;

            // Determine target remote path (specific hovered folder or current folder)
            var pos = e.GetPosition(RemoteDataGrid);
            var targetItem = GetItemAtPosition(RemoteDataGrid, pos);

            string targetPath = _viewModel.RemoteBrowser.CurrentPath;
            if (targetItem != null && targetItem.IsDirectory && !targetItem.IsParent)
            {
                targetPath = targetItem.FullPath;
            }

            // Case 1: Dropped from internal Local panel
            if (e.Data.GetDataPresent("RsyncZilla.Source") &&
                e.Data.GetData("RsyncZilla.Source") as string == "Local")
            {
                var items = e.Data.GetData("RsyncZilla.Items") as List<FileItem>;
                if (items != null && items.Any())
                {
                    _ = _viewModel.UploadItemsAsync(items, targetPath);
                    e.Handled = true;
                    return;
                }
            }

            // Case 2: Dropped from external Windows File Explorer
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null && paths.Length > 0)
                {
                    _ = _viewModel.UploadPathsAsync(paths, targetPath);
                    e.Handled = true;
                }
            }
        }

        // ==========================================
        // HELPER: HIT TEST FOR ROW
        // ==========================================

        private static FileItem? GetItemAtPosition(DataGrid grid, Point position)
        {
            var element = grid.InputHitTest(position) as DependencyObject;
            while (element != null && element != grid)
            {
                if (element is DataGridRow row && row.Item is FileItem item)
                {
                    return item;
                }
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        // ==========================================
        // CONTEXT MENU ACTIONS
        // ==========================================

        private async void NewLocalFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.Equals(_viewModel.LocalBrowser.CurrentPath.Trim(), "This PC", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Cannot create a folder in 'This PC'. Please select a drive first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Views.InputDialog("New Local Folder", "Enter name for the new local folder:", "New Folder")
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResponseText))
            {
                await _viewModel.LocalBrowser.CreateFolderAsync(dlg.ResponseText);
            }
        }

        private async void NewRemoteFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Views.InputDialog("New Remote Folder", "Enter name for the new remote folder:", "new_folder")
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResponseText))
            {
                await _viewModel.RemoteBrowser.CreateFolderAsync(dlg.ResponseText);
            }
        }

        private async void DeleteLocalItem_Click(object sender, RoutedEventArgs e)
        {
            var selected = LocalDataGrid.SelectedItems.Cast<FileItem>()
                .Where(i => i != null && !i.IsParent && !i.IsDrive).ToList();

            if (!selected.Any()) return;

            string prompt = selected.Count == 1
                ? $"Are you sure you want to delete '{selected[0].Name}'?"
                : $"Are you sure you want to delete the {selected.Count} selected local items?";

            var confirm = MessageBox.Show(prompt, "Confirm Local Deletion", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                await _viewModel.LocalBrowser.DeleteItemsAsync(selected);
            }
        }

        private async void DeleteRemoteItem_Click(object sender, RoutedEventArgs e)
        {
            var selected = RemoteDataGrid.SelectedItems.Cast<FileItem>()
                .Where(i => i != null && !i.IsParent).ToList();

            if (!selected.Any()) return;

            string prompt = selected.Count == 1
                ? $"Are you sure you want to delete '{selected[0].Name}' from the remote server?"
                : $"Are you sure you want to delete the {selected.Count} selected remote items?";

            var confirm = MessageBox.Show(prompt, "Confirm Remote Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                await _viewModel.RemoteBrowser.DeleteItemsAsync(selected);
            }
        }
    }
}