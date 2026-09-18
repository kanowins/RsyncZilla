using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RsyncZilla.Models;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;
using RsyncZilla.Views;

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
        private List<FileItem>? _activeRemoteDragItems;
        private DataGrid? _lastActiveFileGrid;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            _lastActiveFileGrid = RemoteDataGrid;

            LocalDataGrid.GotKeyboardFocus += (s, e) => _lastActiveFileGrid = LocalDataGrid;
            RemoteDataGrid.GotKeyboardFocus += (s, e) => _lastActiveFileGrid = RemoteDataGrid;
            LocalDataGrid.SelectionChanged += (s, e) => { if (LocalDataGrid.SelectedItems.Count > 0) _lastActiveFileGrid = LocalDataGrid; };
            RemoteDataGrid.SelectionChanged += (s, e) => { if (RemoteDataGrid.SelectedItems.Count > 0) _lastActiveFileGrid = RemoteDataGrid; };

            // Link multiple selection extractors
            _viewModel.GetLocalSelectedItemsFunc = () => LocalDataGrid.SelectedItems.Cast<FileItem>().ToList();
            _viewModel.GetRemoteSelectedItemsFunc = () => RemoteDataGrid.SelectedItems.Cast<FileItem>().ToList();

            // Dialog actions
            _viewModel.ShowAboutAction = () =>
            {
                var dlg = new AboutDialog { Owner = this };
                dlg.ShowDialog();
            };

            _viewModel.ShowUpdateAction = () =>
            {
                var dlg = new UpdateDialog(_viewModel.UpdateService)
                {
                    Owner = this
                };
                dlg.ShowDialog();
            };

            _viewModel.ExitAction = () => Close();

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

        private void FocusGridCellOrGrid(DataGrid grid, DataGridCell? cell)
        {
            _lastActiveFileGrid = grid;
            if (cell != null && cell.Focusable)
            {
                cell.Focus();
                Keyboard.Focus(cell);
            }
            else
            {
                grid.Focus();
                Keyboard.Focus(grid);
            }
        }

        private void LocalPanel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastActiveFileGrid = LocalDataGrid;
        }

        private void RemotePanel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastActiveFileGrid = RemoteDataGrid;
        }

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

            DataGridRow? row = null;
            DataGridCell? cell = null;
            var curr = dep;
            while (curr != null && curr != grid)
            {
                if (curr is DataGridCell c && cell == null) cell = c;
                if (curr is DataGridRow r) { row = r; break; }
                curr = VisualTreeHelper.GetParent(curr);
            }

            if (row != null)
            {
                if (row.IsSelected)
                {
                    // Row is already selected as part of a multi-selection: preserve selection
                    FocusGridCellOrGrid(grid, cell);
                    e.Handled = true;
                }
                else
                {
                    if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        grid.SelectedItems.Clear();
                    }
                    row.IsSelected = true;
                    FocusGridCellOrGrid(grid, cell);
                }
            }
            else
            {
                // Clicked on empty area of the grid
                if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    grid.SelectedItems.Clear();
                }
                FocusGridCellOrGrid(grid, null);
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

            bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool isShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            bool isCtrlOrShift = isCtrl || isShift;

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
                FocusGridCellOrGrid(grid, null);
                e.Handled = true;
            }
            else
            {
                // Clicked ON a file: marquee selection is NEVER allowed when clicking on a file
                _isLeftDragCandidate = false;
                _draggedRow = row;

                if (isCtrl)
                {
                    // Standard Windows Explorer Ctrl+Click: toggle selection of the clicked row without unselecting others
                    _isDragDropCandidate = true;
                    row.IsSelected = !row.IsSelected;
                    grid.CurrentItem = row.Item;
                    FocusGridCellOrGrid(grid, cell);
                    e.Handled = true;
                }
                else if (isShift)
                {
                    // Standard Windows Explorer Shift+Click: range selection between anchor and clicked item
                    _isDragDropCandidate = true;
                    var anchor = grid.CurrentItem as FileItem ?? grid.SelectedItems.Cast<FileItem>().FirstOrDefault();
                    var target = row.Item as FileItem;

                    if (anchor != null && target != null)
                    {
                        var itemsList = grid.Items.Cast<FileItem>().ToList();
                        int idx1 = itemsList.IndexOf(anchor);
                        int idx2 = itemsList.IndexOf(target);
                        if (idx1 >= 0 && idx2 >= 0)
                        {
                            int start = Math.Min(idx1, idx2);
                            int end = Math.Max(idx1, idx2);

                            grid.SelectedItems.Clear();
                            for (int i = start; i <= end; i++)
                            {
                                grid.SelectedItems.Add(itemsList[i]);
                            }
                        }
                    }
                    else
                    {
                        row.IsSelected = true;
                    }

                    FocusGridCellOrGrid(grid, cell);
                    e.Handled = true;
                }
                else
                {
                    // Normal Click (no Ctrl, no Shift)
                    if (row.IsSelected)
                    {
                        // Row is already selected: mark candidate for Drag & Drop
                        // Do NOT clear selection yet (wait for mouse move to drag, or mouse up to isolate single row)
                        _isDragDropCandidate = true;
                        grid.CurrentItem = row.Item;
                        FocusGridCellOrGrid(grid, cell);
                        e.Handled = true;
                    }
                    else
                    {
                        // Row is not currently selected: clear other selections and select this row
                        grid.SelectedItems.Clear();
                        row.IsSelected = true;
                        grid.CurrentItem = row.Item;
                        FocusGridCellOrGrid(grid, cell);
                        _isDragDropCandidate = true;
                        e.Handled = true;
                    }
                }
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
                            if (isLocal)
                            {
                                var data = new DataObject();
                                var paths = selected.Select(i => i.FullPath).ToArray();
                                data.SetData(DataFormats.FileDrop, paths);
                                data.SetData("RsyncZilla.Source", "Local");
                                data.SetData("RsyncZilla.Items", selected);

                                DragDrop.DoDragDrop(_leftDragGrid, data, DragDropEffects.Copy);
                            }
                            else
                            {
                                var sftp = _viewModel.SftpService;
                                var descriptors = new List<VirtualFileDataObject.VirtualFileDataObject.FileDescriptor>();

                                if (sftp != null && sftp.IsConnected)
                                {
                                    foreach (var item in selected)
                                    {
                                        if (item.IsDirectory)
                                        {
                                            var files = sftp.GetFilesRecursive(item.FullPath, item.Name);
                                            foreach (var f in files)
                                            {
                                                descriptors.Add(new VirtualFileDataObject.VirtualFileDataObject.FileDescriptor
                                                {
                                                    Name = f.relativePath,
                                                    Length = f.size,
                                                    ChangeTimeUtc = f.modified,
                                                    StreamContents = stream => sftp.DownloadFileToStream(f.fullPath, stream)
                                                });
                                            }
                                        }
                                        else
                                        {
                                            descriptors.Add(new VirtualFileDataObject.VirtualFileDataObject.FileDescriptor
                                            {
                                                Name = item.Name,
                                                Length = item.Length,
                                                ChangeTimeUtc = item.LastWriteTime,
                                                StreamContents = stream => sftp.DownloadFileToStream(item.FullPath, stream)
                                            });
                                        }
                                    }
                                }

                                var vfdo = new VirtualFileDataObject.VirtualFileDataObject();
                                if (descriptors.Count > 0)
                                {
                                    vfdo.SetData(descriptors);
                                }

                                var format = DataFormats.GetDataFormat("RsyncZilla.Source");
                                vfdo.SetData((short)format.Id, Encoding.UTF8.GetBytes("Remote"));

                                _activeRemoteDragItems = selected;
                                try
                                {
                                    DragDrop.DoDragDrop(_leftDragGrid, vfdo, DragDropEffects.Copy);
                                }
                                finally
                                {
                                    _activeRemoteDragItems = null;
                                }
                            }
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

                var targetGrid = _rightDragGrid;
                _rightDragGrid?.ReleaseMouseCapture();
                _isRightDragSelecting = false;
                _isRightDragCandidate = false;
                _rightDragGrid = null;

                if (targetGrid != null)
                {
                    FocusGridCellOrGrid(targetGrid, null);
                }

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

                var targetGrid = _leftDragGrid;
                _leftDragGrid?.ReleaseMouseCapture();
                _isLeftDragSelecting = false;
                _isLeftDragCandidate = false;
                _leftDragGrid = null;

                if (targetGrid != null)
                {
                    FocusGridCellOrGrid(targetGrid, null);
                }

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
                    FocusGridCellOrGrid(_leftDragGrid, null);
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
                if (item.IsDirectory)
                {
                    await _viewModel.LocalBrowser.OpenItemAsync(item);
                }
                else if (!item.IsParent && !item.IsDrive)
                {
                    _viewModel.OpenLocalFile(item);
                }
            }
        }

        private async void RemoteDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item)
            {
                if (item.IsDirectory)
                {
                    await _viewModel.RemoteBrowser.OpenItemAsync(item);
                }
                else if (!item.IsParent)
                {
                    if (_viewModel.EditRemoteFileCommand.CanExecute(item))
                    {
                        _viewModel.EditRemoteFileCommand.Execute(item);
                    }
                }
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
            if (_activeRemoteDragItems != null ||
                (e.Data.GetDataPresent("RsyncZilla.Source") && IsSourceRemote(e.Data)))
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
            var items = _activeRemoteDragItems;
            if (items == null && e.Data.GetDataPresent("RsyncZilla.Items"))
            {
                items = e.Data.GetData("RsyncZilla.Items") as List<FileItem>;
            }

            if (items != null && items.Any())
            {
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

        private static bool IsSourceRemote(IDataObject data)
        {
            try
            {
                var src = data.GetData("RsyncZilla.Source");
                if (src is string s) return s == "Remote";
                if (src is MemoryStream ms) return Encoding.UTF8.GetString(ms.ToArray()) == "Remote";
                if (src is byte[] b) return Encoding.UTF8.GetString(b) == "Remote";
            }
            catch { }
            return false;
        }


        private void RemoteDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (!_viewModel.IsConnected)
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            var formats = e.Data.GetFormats();
            if ((e.Data.GetDataPresent("RsyncZilla.Source") && e.Data.GetData("RsyncZilla.Source") as string == "Local") ||
                DropDataHelper.HasDroppableFiles(e.Data) ||
                (formats != null && formats.Length > 0))
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

            // Case 2: Dropped from external sources (Visual Studio Code, Windows Explorer, Chromium, etc.)
            var externalPaths = DropDataHelper.ExtractLocalPaths(e.Data);
            if (externalPaths.Count > 0)
            {
                _ = _viewModel.UploadPathsAsync(externalPaths, targetPath);
                e.Handled = true;
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

        private DataGrid? GetTargetFileGridForAction()
        {
            if (RemoteDataGrid.IsKeyboardFocusWithin) return RemoteDataGrid;
            if (LocalDataGrid.IsKeyboardFocusWithin) return LocalDataGrid;
            if (RemotePathTextBox.IsKeyboardFocusWithin) return RemoteDataGrid;
            if (LocalPathTextBox.IsKeyboardFocusWithin) return LocalDataGrid;

            if (_lastActiveFileGrid == RemoteDataGrid && RemoteDataGrid.SelectedItems.Count > 0) return RemoteDataGrid;
            if (_lastActiveFileGrid == LocalDataGrid && LocalDataGrid.SelectedItems.Count > 0) return LocalDataGrid;

            if (RemoteDataGrid.SelectedItems.Count > 0 && LocalDataGrid.SelectedItems.Count == 0) return RemoteDataGrid;
            if (LocalDataGrid.SelectedItems.Count > 0 && RemoteDataGrid.SelectedItems.Count == 0) return LocalDataGrid;

            if (_lastActiveFileGrid != null && _lastActiveFileGrid.SelectedItems.Count > 0) return _lastActiveFileGrid;

            if (RemoteDataGrid.SelectedItems.Count > 0) return RemoteDataGrid;
            if (LocalDataGrid.SelectedItems.Count > 0) return LocalDataGrid;

            return _lastActiveFileGrid;
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

                var targetGrid = GetTargetFileGridForAction();
                if (targetGrid == RemoteDataGrid && RemoteDataGrid.SelectedItems.Count > 0)
                {
                    e.Handled = true;
                    await DeleteRemoteSelectedItemsAsync();
                }
                else if (targetGrid == LocalDataGrid && LocalDataGrid.SelectedItems.Count > 0)
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

                var targetGrid = GetTargetFileGridForAction();
                if (targetGrid == RemoteDataGrid && RemoteDataGrid.SelectedItem != null)
                {
                    e.Handled = true;
                    await RenameRemoteSelectedItemAsync();
                }
                else if (targetGrid == LocalDataGrid && LocalDataGrid.SelectedItem != null)
                {
                    e.Handled = true;
                    await RenameLocalSelectedItemAsync();
                }
            }
            else if (e.Key == Key.F5)
            {
                var targetGrid = GetTargetFileGridForAction();
                if (targetGrid == RemoteDataGrid)
                {
                    e.Handled = true;
                    if (_viewModel.IsConnected)
                    {
                        await _viewModel.RemoteBrowser.RefreshAsync();
                    }
                }
                else if (targetGrid == LocalDataGrid)
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
                var targetGrid = GetTargetFileGridForAction();
                if (targetGrid == LocalDataGrid)
                {
                    var item = LocalDataGrid.SelectedItem as FileItem;
                    if (item != null && !item.IsDirectory && !item.IsParent && !item.IsDrive)
                    {
                        e.Handled = true;
                        _viewModel.OpenLocalFile(item);
                    }
                }
                else if (targetGrid == RemoteDataGrid)
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
            else if (e.Key == Key.Enter)
            {
                if (Keyboard.FocusedElement is TextBox or PasswordBox)
                {
                    return;
                }

                var targetGrid = GetTargetFileGridForAction();
                if (targetGrid == LocalDataGrid && LocalDataGrid.SelectedItem is FileItem localItem)
                {
                    e.Handled = true;
                    if (localItem.IsDirectory)
                    {
                        await _viewModel.LocalBrowser.OpenItemAsync(localItem);
                    }
                    else if (!localItem.IsParent && !localItem.IsDrive)
                    {
                        _viewModel.OpenLocalFile(localItem);
                    }
                }
                else if (targetGrid == RemoteDataGrid && RemoteDataGrid.SelectedItem is FileItem remoteItem)
                {
                    e.Handled = true;
                    if (remoteItem.IsDirectory)
                    {
                        await _viewModel.RemoteBrowser.OpenItemAsync(remoteItem);
                    }
                    else if (!remoteItem.IsParent)
                    {
                        if (_viewModel.EditRemoteFileCommand.CanExecute(remoteItem))
                        {
                            _viewModel.EditRemoteFileCommand.Execute(remoteItem);
                        }
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
            FocusGridCellOrGrid(LocalDataGrid, null);
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
            FocusGridCellOrGrid(RemoteDataGrid, null);
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
            FocusGridCellOrGrid(LocalDataGrid, null);
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
            FocusGridCellOrGrid(RemoteDataGrid, null);
        }

        // ==========================================
        // TRANSFER QUEUE INTEGRATED TREE EXPAND / COLLAPSE
        // ==========================================

        private void ToggleExpandFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton tb && tb.DataContext is TransferTask task && task.IsDirectory && !task.IsChild)
            {
                var grid = FindVisualParent<DataGrid>(tb);
                if (grid?.ItemsSource is ObservableCollection<TransferTask> collection)
                {
                    if (tb.IsChecked == true)
                    {
                        _viewModel.ExpandDirectoryTask(task, collection);
                    }
                    else
                    {
                        _viewModel.CollapseDirectoryTask(task, collection);
                    }
                }
            }
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;

            if (sender is DataGridRow row && row.Item is TransferTask task && task.IsDirectory && !task.IsChild)
            {
                var grid = FindVisualParent<DataGrid>(row);
                if (grid?.ItemsSource is ObservableCollection<TransferTask> collection)
                {
                    _viewModel.ToggleDirectoryTask(task, collection);
                    e.Handled = true;
                }
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
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