using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RsyncZilla.Models;
using RsyncZilla.ViewModels;

namespace RsyncZilla
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        
        // Right-click rubber-band marquee selection tracking
        private Point _rightDragStartPoint;
        private DataGrid? _rightDragGrid;
        private bool _isRightDragCandidate;
        private bool _isRightDragSelecting;
        private readonly HashSet<FileItem> _rightDragInitialSelectedItems = new();

        // Left-click rubber-band selection & drag-and-drop tracking
        private Point _leftDragStartPoint;
        private DataGrid? _leftDragGrid;
        private bool _isLeftDragCandidate;
        private bool _isLeftDragSelecting;
        private bool _isDragDropCandidate;
        private DataGridRow? _draggedRow;
        private readonly HashSet<FileItem> _leftDragInitialSelectedItems = new();

        private SelectionAdorner? _selectionAdorner;

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
        // QUICK CONNECT ENTER KEY TRIGGER
        // ==========================================

        private void QuickConnect_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_viewModel.ConnectCommand.CanExecute(PasswordInput))
                {
                    _viewModel.ConnectCommand.Execute(PasswordInput);
                }
            }
        }

        // =========================================================================
        // UNIFIED MOUSE SELECTION (LEFT & RIGHT BUTTON RUBBER-BAND) & DRAG-AND-DROP
        // =========================================================================

        private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid) return;

            // Ignore column headers and scrollbars
            var dep = e.OriginalSource as DependencyObject;
            var testObj = dep;
            while (testObj != null && testObj != grid)
            {
                if (testObj is DataGridColumnHeader || testObj is ScrollBar)
                {
                    return;
                }
                testObj = VisualTreeHelper.GetParent(testObj);
            }

            _rightDragStartPoint = e.GetPosition(grid);
            _rightDragGrid = grid;
            _isRightDragCandidate = true;
            _isRightDragSelecting = false;

            while (dep != null && dep is not DataGridRow)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridRow row)
            {
                if (row.IsSelected)
                {
                    // Row is already selected as part of a multi-selection: preserve selection
                    row.Focus();
                    e.Handled = true;
                }
                else
                {
                    if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        grid.SelectedItems.Clear();
                    }
                    row.IsSelected = true;
                    row.Focus();
                }
            }
            else
            {
                // Clicked on empty area of the grid
                if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    grid.SelectedItems.Clear();
                }
            }
        }

        private void DataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid) return;
            if (e.ClickCount > 1) return; // Allow double-click to pass through to navigation

            // Ignore column headers and scrollbars
            var dep = e.OriginalSource as DependencyObject;
            var testObj = dep;
            while (testObj != null && testObj != grid)
            {
                if (testObj is DataGridColumnHeader || testObj is ScrollBar)
                {
                    return;
                }
                testObj = VisualTreeHelper.GetParent(testObj);
            }

            _leftDragStartPoint = e.GetPosition(grid);
            _leftDragGrid = grid;
            _isLeftDragSelecting = false;

            DataGridRow? row = null;
            DataGridCell? cell = null;
            var curr = dep;
            while (curr != null && curr != grid)
            {
                if (curr is DataGridCell c && cell == null) cell = c;
                if (curr is DataGridRow r) { row = r; break; }
                curr = VisualTreeHelper.GetParent(curr);
            }

            bool isCtrlOrShift = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl) ||
                                 Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if (row == null)
            {
                // Clicked on empty area (whitespace) below or beside items
                if (!isCtrlOrShift)
                {
                    grid.SelectedItems.Clear();
                }
                _isDragDropCandidate = false;
                _isLeftDragCandidate = true;
                _draggedRow = null;
                grid.Focus();
                e.Handled = true;
            }
            else if (row.IsSelected)
            {
                // Row is already selected:
                // If clicked on the Name/icon column without Ctrl/Shift, this is a potential drag & drop operation
                if (cell?.Column is DataGridTemplateColumn && !isCtrlOrShift)
                {
                    _isDragDropCandidate = true;
                    _isLeftDragCandidate = false;
                    _draggedRow = row;
                    row.Focus();
                    e.Handled = true; // Wait to determine whether user drags or simply clicks
                }
                else
                {
                    // Clicked on Size, Type, Date or with Ctrl/Shift: candidate for rubber-band selection box
                    _isDragDropCandidate = false;
                    _isLeftDragCandidate = true;
                    _draggedRow = null;
                }
            }
            else
            {
                // Row is not currently selected
                _isDragDropCandidate = false;
                _isLeftDragCandidate = true;
                _draggedRow = null;

                if (!isCtrlOrShift)
                {
                    grid.SelectedItems.Clear();
                }
                row.IsSelected = true;
                row.Focus();
            }
        }

        private void DataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // 1. Right-button marquee selection
            if (e.RightButton == MouseButtonState.Pressed && _isRightDragCandidate && _rightDragGrid != null)
            {
                var currentPoint = e.GetPosition(_rightDragGrid);
                var diff = currentPoint - _rightDragStartPoint;

                if (!_isRightDragSelecting)
                {
                    if (Math.Abs(diff.X) < 4 && Math.Abs(diff.Y) < 4) return;

                    _isRightDragSelecting = true;
                    _rightDragGrid.CaptureMouse();

                    var adornerLayer = AdornerLayer.GetAdornerLayer(_rightDragGrid) ?? AdornerLayer.GetAdornerLayer(this);
                    if (adornerLayer != null)
                    {
                        _selectionAdorner = new SelectionAdorner(_rightDragGrid);
                        adornerLayer.Add(_selectionAdorner);
                    }

                    if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        _rightDragInitialSelectedItems.Clear();
                    }
                    else
                    {
                        _rightDragInitialSelectedItems.Clear();
                        foreach (var sel in _rightDragGrid.SelectedItems.Cast<FileItem>())
                        {
                            _rightDragInitialSelectedItems.Add(sel);
                        }
                    }
                }

                _selectionAdorner?.UpdateRect(_rightDragStartPoint, currentPoint);

                var selectionRect = new Rect(
                    Math.Min(_rightDragStartPoint.X, currentPoint.X),
                    Math.Min(_rightDragStartPoint.Y, currentPoint.Y),
                    Math.Max(1, Math.Abs(_rightDragStartPoint.X - currentPoint.X)),
                    Math.Max(1, Math.Abs(_rightDragStartPoint.Y - currentPoint.Y))
                );

                UpdateSelectionFromRect(_rightDragGrid, selectionRect, _rightDragInitialSelectedItems);
                return;
            }

            // 2. Left-button interactions (Marquee selection OR Drag & Drop)
            if (e.LeftButton == MouseButtonState.Pressed && _leftDragGrid != null)
            {
                var currentPoint = e.GetPosition(_leftDragGrid);
                var diff = currentPoint - _leftDragStartPoint;

                // Branch A: Drag & Drop candidate (clicked on Name column of an already selected item)
                if (_isDragDropCandidate)
                {
                    if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        _isDragDropCandidate = false;
                        _draggedRow = null;

                        var isLocal = _leftDragGrid == LocalDataGrid;
                        var selected = _leftDragGrid.SelectedItems.Cast<FileItem>()
                            .Where(i => i != null && !i.IsParent && !i.IsDrive).ToList();

                        if (selected.Any())
                        {
                            var data = new DataObject();
                            if (isLocal)
                            {
                                var paths = selected.Select(i => i.FullPath).ToArray();
                                data.SetData(DataFormats.FileDrop, paths);
                                data.SetData("RsyncZilla.Source", "Local");
                            }
                            else
                            {
                                data.SetData("RsyncZilla.Source", "Remote");
                            }
                            data.SetData("RsyncZilla.Items", selected);

                            DragDrop.DoDragDrop(_leftDragGrid, data, DragDropEffects.Copy);
                        }
                    }
                    return;
                }

                // Branch B: Rubber-band marquee selection with left button
                if (_isLeftDragCandidate)
                {
                    if (!_isLeftDragSelecting)
                    {
                        if (Math.Abs(diff.X) < 4 && Math.Abs(diff.Y) < 4) return;

                        _isLeftDragSelecting = true;
                        _leftDragGrid.CaptureMouse();

                        var adornerLayer = AdornerLayer.GetAdornerLayer(_leftDragGrid) ?? AdornerLayer.GetAdornerLayer(this);
                        if (adornerLayer != null)
                        {
                            _selectionAdorner = new SelectionAdorner(_leftDragGrid);
                            adornerLayer.Add(_selectionAdorner);
                        }

                        if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl) &&
                            !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
                        {
                            _leftDragInitialSelectedItems.Clear();
                        }
                        else
                        {
                            _leftDragInitialSelectedItems.Clear();
                            foreach (var sel in _leftDragGrid.SelectedItems.Cast<FileItem>())
                            {
                                _leftDragInitialSelectedItems.Add(sel);
                            }
                        }
                    }

                    _selectionAdorner?.UpdateRect(_leftDragStartPoint, currentPoint);

                    var selectionRect = new Rect(
                        Math.Min(_leftDragStartPoint.X, currentPoint.X),
                        Math.Min(_leftDragStartPoint.Y, currentPoint.Y),
                        Math.Max(1, Math.Abs(_leftDragStartPoint.X - currentPoint.X)),
                        Math.Max(1, Math.Abs(_leftDragStartPoint.Y - currentPoint.Y))
                    );

                    UpdateSelectionFromRect(_leftDragGrid, selectionRect, _leftDragInitialSelectedItems);
                }
            }
        }

        private void DataGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isRightDragSelecting)
            {
                if (_selectionAdorner != null && _rightDragGrid != null)
                {
                    var adornerLayer = AdornerLayer.GetAdornerLayer(_rightDragGrid) ?? AdornerLayer.GetAdornerLayer(this);
                    adornerLayer?.Remove(_selectionAdorner);
                    _selectionAdorner = null;
                }

                _rightDragGrid?.ReleaseMouseCapture();
                _isRightDragSelecting = false;
                _isRightDragCandidate = false;
                _rightDragGrid = null;

                // Suppress context menu after rubber-band dragging!
                e.Handled = true;
                return;
            }

            _isRightDragCandidate = false;
            _rightDragGrid = null;
        }

        private void DataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isLeftDragSelecting)
            {
                if (_selectionAdorner != null && _leftDragGrid != null)
                {
                    var adornerLayer = AdornerLayer.GetAdornerLayer(_leftDragGrid) ?? AdornerLayer.GetAdornerLayer(this);
                    adornerLayer?.Remove(_selectionAdorner);
                    _selectionAdorner = null;
                }

                _leftDragGrid?.ReleaseMouseCapture();
                _isLeftDragSelecting = false;
                _isLeftDragCandidate = false;
                _leftDragGrid = null;
                e.Handled = true;
                return;
            }

            if (_isDragDropCandidate && _draggedRow != null && _leftDragGrid != null)
            {
                // User clicked on an already selected row's Name column, but did NOT drag it: select only this row
                bool isCtrlOrShift = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl) ||
                                     Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                if (!isCtrlOrShift)
                {
                    _leftDragGrid.SelectedItems.Clear();
                    _draggedRow.IsSelected = true;
                    _draggedRow.Focus();
                }
                _isDragDropCandidate = false;
                _draggedRow = null;
                _leftDragGrid = null;
                return;
            }

            _isLeftDragCandidate = false;
            _isDragDropCandidate = false;
            _draggedRow = null;
            _leftDragGrid = null;
        }

        private void DataGrid_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_selectionAdorner != null)
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(this);
                adornerLayer?.Remove(_selectionAdorner);
                _selectionAdorner = null;
            }

            if (_rightDragGrid != null)
            {
                _isRightDragSelecting = false;
                _isRightDragCandidate = false;
                _rightDragGrid = null;
            }

            if (_leftDragGrid != null)
            {
                _isLeftDragSelecting = false;
                _isLeftDragCandidate = false;
                _isDragDropCandidate = false;
                _draggedRow = null;
                _leftDragGrid = null;
            }
        }

        private void UpdateSelectionFromRect(DataGrid grid, Rect selectionRect, HashSet<FileItem> initialSelected)
        {
            bool ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl) ||
                               Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            foreach (var item in grid.Items)
            {
                if (item is not FileItem fileItem || fileItem.IsParent)
                    continue;

                if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                {
                    try
                    {
                        GeneralTransform transform = row.TransformToAncestor(grid);
                        Point rowTopLeft = transform.Transform(new Point(0, 0));
                        Rect rowRect = new Rect(rowTopLeft.X, rowTopLeft.Y, row.ActualWidth, row.ActualHeight);

                        bool intersects = selectionRect.IntersectsWith(rowRect);

                        if (intersects)
                        {
                            if (!row.IsSelected)
                            {
                                row.IsSelected = true;
                            }
                        }
                        else
                        {
                            if (!ctrlPressed && !initialSelected.Contains(fileItem))
                            {
                                if (row.IsSelected)
                                {
                                    row.IsSelected = false;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
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
        // DROP TARGET HANDLING (LOCAL & REMOTE)
        // ==========================================

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

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                // Do not intercept Delete when editing text in a TextBox or PasswordBox
                if (Keyboard.FocusedElement is TextBox or PasswordBox)
                {
                    return;
                }

                if (RemoteDataGrid.IsKeyboardFocusWithin)
                {
                    e.Handled = true;
                    await DeleteRemoteSelectedItemsAsync();
                }
                else if (LocalDataGrid.IsKeyboardFocusWithin)
                {
                    e.Handled = true;
                    await DeleteLocalSelectedItemsAsync();
                }
                else if (RemoteDataGrid.SelectedItems.Count > 0 && LocalDataGrid.SelectedItems.Count == 0)
                {
                    e.Handled = true;
                    await DeleteRemoteSelectedItemsAsync();
                }
                else if (LocalDataGrid.SelectedItems.Count > 0 && RemoteDataGrid.SelectedItems.Count == 0)
                {
                    e.Handled = true;
                    await DeleteLocalSelectedItemsAsync();
                }
            }
            else if (e.Key == Key.F2)
            {
                // Do not intercept F2 when editing text in a TextBox or PasswordBox
                if (Keyboard.FocusedElement is TextBox or PasswordBox)
                {
                    return;
                }

                if (RemoteDataGrid.IsKeyboardFocusWithin)
                {
                    e.Handled = true;
                    await RenameRemoteSelectedItemAsync();
                }
                else if (LocalDataGrid.IsKeyboardFocusWithin)
                {
                    e.Handled = true;
                    await RenameLocalSelectedItemAsync();
                }
                else if (RemoteDataGrid.SelectedItems.Count > 0 && LocalDataGrid.SelectedItems.Count == 0)
                {
                    e.Handled = true;
                    await RenameRemoteSelectedItemAsync();
                }
                else if (LocalDataGrid.SelectedItems.Count > 0 && RemoteDataGrid.SelectedItems.Count == 0)
                {
                    e.Handled = true;
                    await RenameLocalSelectedItemAsync();
                }
            }
            else if (e.Key == Key.F5)
            {
                if (RemoteDataGrid.IsKeyboardFocusWithin || RemotePathTextBox.IsKeyboardFocusWithin)
                {
                    e.Handled = true;
                    if (_viewModel.IsConnected)
                    {
                        await _viewModel.RemoteBrowser.RefreshAsync();
                    }
                }
                else if (LocalDataGrid.IsKeyboardFocusWithin || LocalPathTextBox.IsKeyboardFocusWithin)
                {
                    e.Handled = true;
                    await _viewModel.LocalBrowser.RefreshAsync();
                }
                else
                {
                    e.Handled = true;
                    await _viewModel.LocalBrowser.RefreshAsync();
                    if (_viewModel.IsConnected)
                    {
                        await _viewModel.RemoteBrowser.RefreshAsync();
                    }
                }
            }
            else if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (_viewModel.OpenRemoteTerminalCommand.CanExecute(null))
                {
                    e.Handled = true;
                    _viewModel.OpenRemoteTerminalCommand.Execute(null);
                }
            }
            else if (e.Key == Key.F4)
            {
                var item = RemoteDataGrid.SelectedItem as FileItem;
                if (item != null && !item.IsDirectory && !item.IsParent)
                {
                    if (_viewModel.EditRemoteFileCommand.CanExecute(item))
                    {
                        e.Handled = true;
                        _viewModel.EditRemoteFileCommand.Execute(item);
                    }
                }
            }
        }

        private async void RenameLocalItem_Click(object sender, RoutedEventArgs e)
        {
            await RenameLocalSelectedItemAsync();
        }

        private async Task RenameLocalSelectedItemAsync()
        {
            var item = LocalDataGrid.SelectedItem as FileItem;
            if (item == null || item.IsParent || item.IsDrive)
            {
                return;
            }

            var dlg = new Views.InputDialog("Rename Local Item", "Enter new name:", item.Name)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResponseText))
            {
                var newName = dlg.ResponseText.Trim();
                if (!string.Equals(newName, item.Name, StringComparison.Ordinal))
                {
                    await _viewModel.LocalBrowser.RenameItemAsync(item, newName);
                }
            }
        }

        private async void RenameRemoteItem_Click(object sender, RoutedEventArgs e)
        {
            await RenameRemoteSelectedItemAsync();
        }

        private async Task RenameRemoteSelectedItemAsync()
        {
            var item = RemoteDataGrid.SelectedItem as FileItem;
            if (item == null || item.IsParent)
            {
                return;
            }

            var dlg = new Views.InputDialog("Rename Remote Item", "Enter new name:", item.Name)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResponseText))
            {
                var newName = dlg.ResponseText.Trim();
                if (!string.Equals(newName, item.Name, StringComparison.Ordinal))
                {
                    await _viewModel.RemoteBrowser.RenameItemAsync(item, newName);
                }
            }
        }

        private async void DeleteLocalItem_Click(object sender, RoutedEventArgs e)
        {
            await DeleteLocalSelectedItemsAsync();
        }

        private async Task DeleteLocalSelectedItemsAsync()
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
            await DeleteRemoteSelectedItemsAsync();
        }

        private async Task DeleteRemoteSelectedItemsAsync()
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

    // ==========================================
    // SELECTION MARQUEE ADORNER
    // ==========================================

    public class SelectionAdorner : Adorner
    {
        private Rect _rect;
        private readonly Pen _pen;
        private readonly Brush _brush;

        public SelectionAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
            var strokeBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            strokeBrush.Freeze();
            _pen = new Pen(strokeBrush, 1.5)
            {
                DashStyle = DashStyles.Dash
            };
            _pen.Freeze();
            _brush = new SolidColorBrush(Color.FromArgb(50, 0, 120, 215));
            _brush.Freeze();
        }

        public void UpdateRect(Point p1, Point p2)
        {
            _rect = new Rect(
                Math.Min(p1.X, p2.X),
                Math.Min(p1.Y, p2.Y),
                Math.Max(1, Math.Abs(p1.X - p2.X)),
                Math.Max(1, Math.Abs(p1.Y - p2.Y))
            );
            InvalidateVisual();
        }

        public Rect SelectionRect => _rect;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (_rect.Width > 0 && _rect.Height > 0)
            {
                dc.DrawRectangle(_brush, _pen, _rect);
            }
        }
    }
}