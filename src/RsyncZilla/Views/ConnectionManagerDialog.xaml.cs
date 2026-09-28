using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RsyncZilla.Models;
using RsyncZilla.Services;

namespace RsyncZilla.Views
{
    public partial class ConnectionManagerDialog : Window
    {
        private readonly ConnectionManagerService _service;
        private readonly VaultService _vaultService;

        public ObservableCollection<SavedConnection> Connections { get; } = new();
        public SavedConnection? SelectedConnection { get; private set; }
        public string? ConnectionPassword { get; private set; }
        public string? ConnectionUsername { get; private set; }

        public ConnectionManagerDialog(ConnectionManagerService service, VaultService? vaultService = null)
        {
            InitializeComponent();
            _service = service;
            _vaultService = vaultService ?? new VaultService();

            ConnectionsGrid.ItemsSource = Connections;

            if (_vaultService.IsConfigured)
            {
                _vaultService.Lock();
            }

            UpdateVaultUiState();
            LoadConnections();

            Loaded += (s, e) =>
            {
                if (_vaultService.IsConfigured && !_vaultService.IsUnlocked)
                {
                    var unlockDialog = new VaultUnlockDialog(_vaultService)
                    {
                        Owner = this
                    };
                    unlockDialog.ShowDialog();
                    UpdateVaultUiState();
                    LoadConnections();
                }
            };

            Closed += (s, e) =>
            {
                _vaultService.Lock();
            };
        }

        private void LoadConnections()
        {
            Connections.Clear();
            var list = _service.LoadConnections();
            foreach (var item in list)
            {
                item.HasSavedPassword = _vaultService.IsUnlocked && _vaultService.GetPassword(item.Id) != null;
                Connections.Add(item);
            }
            if (Connections.Count > 0 && ConnectionsGrid.SelectedIndex < 0)
            {
                ConnectionsGrid.SelectedIndex = 0;
            }
        }

        private void UpdateVaultUiState()
        {
            if (!_vaultService.IsConfigured)
            {
                EnableVaultButton.Visibility = Visibility.Visible;
                VaultControlsContainer.Visibility = Visibility.Collapsed;
                VaultSubtitleTextBlock.Text = "Passwords are not stored. Enable Vault to save server credentials.";
                return;
            }

            EnableVaultButton.Visibility = Visibility.Collapsed;
            VaultControlsContainer.Visibility = Visibility.Visible;
            DeleteVaultButton.Visibility = Visibility.Visible;

            if (!_vaultService.IsUnlocked)
            {
                VaultStatusLabel.Text = "Vault Locked";
                VaultStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                VaultStatusIcon.Source = Application.Current.TryFindResource("FluentLockIcon") as ImageSource;
                UnlockVaultButton.Visibility = Visibility.Visible;
                LockVaultButton.Visibility = Visibility.Collapsed;
                ChangePasswordButton.Visibility = Visibility.Collapsed;
                VaultSubtitleTextBlock.Text = "Vault is locked. Unlock to automatically use and save passwords.";
            }
            else
            {
                VaultStatusLabel.Text = "Vault Active";
                VaultStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                VaultStatusIcon.Source = Application.Current.TryFindResource("FluentUnlockIcon") as ImageSource;
                UnlockVaultButton.Visibility = Visibility.Collapsed;
                LockVaultButton.Visibility = Visibility.Visible;
                ChangePasswordButton.Visibility = Visibility.Visible;
                VaultSubtitleTextBlock.Text = "Encrypted Vault is active. Passwords are saved safely with AES-256.";
            }
        }

        private void EnableVaultButton_Click(object sender, RoutedEventArgs e)
        {
            var setup = new VaultSetupDialog
            {
                Owner = this
            };

            if (setup.ShowDialog() == true && !string.IsNullOrEmpty(setup.MasterPassword))
            {
                _vaultService.Setup(setup.MasterPassword);
                UpdateVaultUiState();
                LoadConnections();
            }
        }

        private void UnlockVaultButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VaultUnlockDialog(_vaultService)
            {
                Owner = this
            };

            dialog.ShowDialog();
            UpdateVaultUiState();
            LoadConnections();
        }

        private void LockVaultButton_Click(object sender, RoutedEventArgs e)
        {
            _vaultService.Lock();
            UpdateVaultUiState();
            LoadConnections();
        }

        private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VaultChangePasswordDialog(_vaultService)
            {
                Owner = this
            };

            dialog.ShowDialog();
        }



        private void DeleteVault_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Are you sure you want to permanently delete the password vault?\n\nAll saved passwords will be deleted and the vault will be removed.",
                "Confirm Delete Vault",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.Yes)
            {
                _vaultService.DeleteVault();
                UpdateVaultUiState();
                LoadConnections();
            }
        }

        private void NewSiteButton_Click(object sender, RoutedEventArgs e)
        {
            var editDialog = new SiteEditDialog(existing: null, initialPassword: null, isVaultUnlocked: _vaultService.IsUnlocked)
            {
                Owner = this
            };

            if (editDialog.ShowDialog() == true)
            {
                _service.SaveOrUpdate(
                    editDialog.Host, 
                    editDialog.Username, 
                    editDialog.Port, 
                    customName: editDialog.SiteName, 
                    localPath: editDialog.LocalPath, 
                    remotePath: editDialog.RemotePath);

                LoadConnections();

                var created = Connections.FirstOrDefault(c => 
                    c.Host.Equals(editDialog.Host, StringComparison.OrdinalIgnoreCase) &&
                    c.Username.Equals(editDialog.Username, StringComparison.OrdinalIgnoreCase) &&
                    c.Port == editDialog.Port);

                if (created != null)
                {
                    if (_vaultService.IsUnlocked && editDialog.IsPasswordModified && !string.IsNullOrEmpty(editDialog.Password))
                    {
                        _vaultService.SetPassword(created.Id, editDialog.Password);
                        LoadConnections();
                    }

                    ConnectionsGrid.SelectedItem = created;
                    ConnectionsGrid.ScrollIntoView(created);
                }
            }
        }

        private void EditSiteButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsGrid.SelectedItem is SavedConnection conn)
            {
                string? initialPassword = _vaultService.IsUnlocked ? _vaultService.GetPassword(conn.Id) : null;
                var editDialog = new SiteEditDialog(conn, initialPassword: initialPassword, isVaultUnlocked: _vaultService.IsUnlocked)
                {
                    Owner = this
                };

                if (editDialog.ShowDialog() == true)
                {
                    _service.SaveOrUpdate(
                        editDialog.Host,
                        editDialog.Username,
                        editDialog.Port,
                        customName: editDialog.SiteName,
                        localPath: editDialog.LocalPath,
                        remotePath: editDialog.RemotePath);

                    if (_vaultService.IsUnlocked && editDialog.IsPasswordModified)
                    {
                        if (string.IsNullOrEmpty(editDialog.Password))
                        {
                            _vaultService.RemovePassword(conn.Id);
                        }
                        else
                        {
                            _vaultService.SetPassword(conn.Id, editDialog.Password);
                        }
                    }

                    LoadConnections();

                    var updated = Connections.FirstOrDefault(c => 
                        c.Host.Equals(editDialog.Host, StringComparison.OrdinalIgnoreCase) &&
                        c.Username.Equals(editDialog.Username, StringComparison.OrdinalIgnoreCase) &&
                        c.Port == editDialog.Port);

                    if (updated != null)
                    {
                        ConnectionsGrid.SelectedItem = updated;
                        ConnectionsGrid.ScrollIntoView(updated);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a site to edit.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ImportFileZillaButton_Click(object sender, RoutedEventArgs e)
        {
            var importDialog = new ImportFileZillaDialog(_service)
            {
                Owner = this
            };

            if (importDialog.ShowDialog() == true)
            {
                LoadConnections();
                if (importDialog.ImportedCount > 0)
                {
                    MessageBox.Show(
                        $"Successfully imported {importDialog.ImportedCount} site(s) from FileZilla.",
                        "Import Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsGrid.SelectedItem is SavedConnection conn)
            {
                // Check if password already exists in unlocked vault
                if (_vaultService.IsUnlocked)
                {
                    var savedPassword = _vaultService.GetPassword(conn.Id);
                    if (!string.IsNullOrEmpty(savedPassword))
                    {
                        SelectedConnection = conn;
                        ConnectionUsername = conn.Username;
                        ConnectionPassword = savedPassword;
                        DialogResult = true;
                        Close();
                        return;
                    }
                }

                // If not saved or vault not unlocked, ask via credentials dialog
                var credDialog = new ConnectCredentialsDialog(conn.Host, conn.Username, conn.Port, conn.DisplayName)
                {
                    Owner = this
                };

                if (credDialog.ShowDialog() == true)
                {
                    SelectedConnection = conn;
                    ConnectionUsername = credDialog.Username;
                    ConnectionPassword = credDialog.Password;

                    // If vault is active and unlocked, save the entered password!
                    if (_vaultService.IsUnlocked && !string.IsNullOrEmpty(credDialog.Password))
                    {
                        _vaultService.SetPassword(conn.Id, credDialog.Password);
                        LoadConnections();
                    }

                    DialogResult = true;
                    Close();
                }
            }
            else
            {
                MessageBox.Show("Please select a site from the list.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsGrid.SelectedItem is SavedConnection conn)
            {
                var confirm = MessageBox.Show($"Are you sure you want to delete the site '{conn.DisplayName}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    _service.DeleteConnection(conn.Id);
                    if (_vaultService.IsUnlocked)
                    {
                        _vaultService.RemovePassword(conn.Id);
                    }
                    LoadConnections();
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ConnectionsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ConnectButton_Click(sender, e);
        }
    }
}
