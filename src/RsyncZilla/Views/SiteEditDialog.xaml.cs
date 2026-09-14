using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RsyncZilla.Models;

namespace RsyncZilla.Views
{
    public partial class SiteEditDialog : Window
    {
        public string Host => HostTextBox.Text.Trim();
        public int Port => int.TryParse(PortTextBox.Text.Trim(), out var p) ? p : 22;
        public string Username => UsernameTextBox.Text.Trim();
        public string LocalPath => LocalPathTextBox.Text.Trim();
        public string RemotePath => RemotePathTextBox.Text.Trim();

        public SiteEditDialog(SavedConnection? existing = null)
        {
            InitializeComponent();

            if (existing != null)
            {
                Title = "Edit Site - RsyncZilla";
                HostTextBox.Text = existing.Host;
                PortTextBox.Text = existing.Port.ToString();
                UsernameTextBox.Text = existing.Username;
                LocalPathTextBox.Text = existing.LastLocalPath;
                RemotePathTextBox.Text = existing.LastRemotePath;
            }

            Loaded += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(HostTextBox.Text))
                {
                    HostTextBox.Focus();
                }
                else if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
                {
                    UsernameTextBox.Focus();
                }
            };
        }

        private void BrowseLocalPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Initial Local Folder",
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(LocalPathTextBox.Text) && Directory.Exists(LocalPathTextBox.Text))
            {
                dialog.InitialDirectory = LocalPathTextBox.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                LocalPathTextBox.Text = dialog.FolderName;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                MessageBox.Show("Please enter a Host / Server address.", "Host Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                HostTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show("Please enter a Username.", "Username Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                UsernameTextBox.Focus();
                return;
            }

            if (Port <= 0 || Port > 65535)
            {
                MessageBox.Show("Please enter a valid port number between 1 and 65535.", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
                PortTextBox.Focus();
                return;
            }

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
