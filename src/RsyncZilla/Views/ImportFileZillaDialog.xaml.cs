using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RsyncZilla.Models;
using RsyncZilla.Services;

namespace RsyncZilla.Views
{
    public partial class ImportFileZillaDialog : Window
    {
        private readonly ConnectionManagerService _connectionManager;
        private readonly FileZillaImportService _importService;

        public ObservableCollection<FileZillaSite> Sites { get; } = new();
        public int ImportedCount { get; private set; }

        public ImportFileZillaDialog(ConnectionManagerService connectionManager)
        {
            InitializeComponent();

            _connectionManager = connectionManager;
            _importService = new FileZillaImportService();

            SitesGrid.ItemsSource = Sites;

            Loaded += (s, e) => AutoDetectAndLoad();
        }

        private void AutoDetectAndLoad()
        {
            var detectedPath = _importService.DetectFileZillaConfigPath();
            if (!string.IsNullOrEmpty(detectedPath) && File.Exists(detectedPath))
            {
                FilePathTextBox.Text = detectedPath;
                StatusMessageTextBlock.Text = "FileZilla configuration detected";
                StatusMessageTextBlock.Foreground = System.Windows.Media.Brushes.ForestGreen;
                LoadFile(detectedPath);
            }
            else
            {
                StatusMessageTextBlock.Text = "No standard configuration found. Click 'Browse XML...' to locate your file.";
                StatusMessageTextBlock.Foreground = System.Windows.Media.Brushes.DarkOrange;
                UpdateSummary();
            }
        }

        private void LoadFile(string path)
        {
            try
            {
                Sites.Clear();
                var existing = _connectionManager.LoadConnections();
                var parsed = _importService.ParseFile(path, existing);

                foreach (var site in parsed)
                {
                    site.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(FileZillaSite.IsSelected))
                        {
                            UpdateSummary();
                        }
                    };
                    Sites.Add(site);
                }

                StatusMessageTextBlock.Text = $"{Sites.Count} site(s) detected in file";
                StatusMessageTextBlock.Foreground = System.Windows.Media.Brushes.ForestGreen;
            }
            catch (Exception ex)
            {
                StatusMessageTextBlock.Text = "Error reading XML file";
                StatusMessageTextBlock.Foreground = System.Windows.Media.Brushes.Crimson;
                MessageBox.Show($"Failed to parse FileZilla configuration file:\n\n{ex.Message}", "Error Reading File", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int total = Sites.Count;
            int selected = Sites.Count(s => s.IsSelected);

            SelectedCountTextBlock.Text = $"({total} site{(total == 1 ? "" : "s")} found, {selected} selected)";
            ImportButtonText.Text = $"Import Selected ({selected})";
            ImportButton.IsEnabled = selected > 0;

            // Sync header checkbox
            if (HeaderCheckBox != null)
            {
                if (selected == 0)
                {
                    HeaderCheckBox.IsChecked = false;
                }
                else if (selected == total)
                {
                    HeaderCheckBox.IsChecked = true;
                }
                else
                {
                    HeaderCheckBox.IsChecked = null; // Indeterminate
                }
            }
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select FileZilla Configuration or Export File",
                Filter = "FileZilla XML (*.xml)|*.xml|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            // Set initial directory to FileZilla appdata if exists
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var fzDir = Path.Combine(appData, "FileZilla");
            if (Directory.Exists(fzDir))
            {
                dialog.InitialDirectory = fzDir;
            }

            if (dialog.ShowDialog() == true)
            {
                FilePathTextBox.Text = dialog.FileName;
                LoadFile(dialog.FileName);
            }
        }

        private void HeaderCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool isChecked = HeaderCheckBox.IsChecked == true;
            foreach (var site in Sites)
            {
                site.IsSelected = isChecked;
            }
            UpdateSummary();
        }

        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSummary();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var site in Sites)
            {
                site.IsSelected = true;
            }
            UpdateSummary();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var site in Sites)
            {
                site.IsSelected = false;
            }
            UpdateSummary();
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var toImport = Sites.Where(s => s.IsSelected).ToList();
            if (toImport.Count == 0)
            {
                MessageBox.Show("Please select at least one site to import.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool overwrite = OverwriteCheckBox.IsChecked == true;
            ImportedCount = _importService.Import(toImport, _connectionManager, overwrite);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
