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
        
        // Local drag tracking
        private Point _localDragStart;
        private bool _isLocalDragCandidate;
        private DataGridRow? _localDraggedRow;

        // Remote drag tracking
        private Point _remoteDragStart;
        private bool _isRemoteDragCandidate;
        private DataGridRow? _remoteDraggedRow;

        // Right-click rubber-band marquee selection tracking
        private Point _rightDragStartPoint;
        private DataGrid? _rightDragGrid;
        private bool _isRightDragCandidate;
        private bool _isRightDragSelecting;
        private SelectionAdorner? _selectionAdorner;
        private readonly HashSet<FileItem> _rightDragInitialSelectedItems = new();

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

        // ==========================================
        // RIGHT-CLICK RUBBER-BAND SELECTION & CONTEXT MENU
        // ==========================================

        private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid) return;

            // Check if user clicked on column headers or scrollbars
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
                    // Row is already selected as part of a multi-selection:
                    // DO NOT let WPF clear other selected rows!
                    row.Focus();
                    e.Handled = true;
                }
                else
                {
                    // Clicked on an unselected row:
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

        private void DataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.RightButton != MouseButtonState.Pressed || !_isRightDragCandidate || _rightDragGrid == null)
            {
                return;
            }

            var currentPoint = e.GetPosition(_rightDragGrid);
            var diff = currentPoint - _rightDragStartPoint;

            if (!_isRightDragSelecting)
            {
                // Must move at least 4 pixels to initiate rubber-band drag
                if (Math.Abs(diff.X) < 4 && Math.Abs(diff.Y) < 4)
                {
                    return;
                }

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

            UpdateSelectionFromRect(_rightDragGrid, selectionRect);
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

                // Crucial: Suppress context menu after rubber-band dragging!
                e.Handled = true;
                return;
            }

            _isRightDragCandidate = false;
            _rightDragGrid = null;
        }

        private void UpdateSelectionFromRect(DataGrid grid, Rect selectionRect)
        {
            bool ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

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
                            if (!ctrlPressed && !_rightDragInitialSelectedItems.Contains(fileItem))
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