using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using RsyncZilla.Models;
using RsyncZilla.Services;

namespace RsyncZilla.Views
{
    public partial class ConnectionManagerDialog : Window
    {
        private readonly ConnectionManagerService _service;
        public ObservableCollection<SavedConnection> Connections { get; } = new();
        public SavedConnection? SelectedConnection { get; private set; }

        public ConnectionManagerDialog(ConnectionManagerService service)
        {
            InitializeComponent();
            _service = service;
            ConnectionsGrid.ItemsSource = Connections;
            LoadConnections();
        }

        private void LoadConnections()
        {
            Connections.Clear();
            var list = _service.LoadConnections();
            foreach (var item in list)
            {
                Connections.Add(item);
            }
            if (Connections.Count > 0)
            {
                ConnectionsGrid.SelectedIndex = 0;
            }
        }

        public string? ConnectionPassword { get; private set; }
        public string? ConnectionUsername { get; private set; }

        private void NewSiteButton_Click(object sender, RoutedEventArgs e)
        {
            var editDialog = new SiteEditDialog
            {
                Owner = this
            };

            if (editDialog.ShowDialog() == true)
            {
                _service.SaveOrUpdate(
                    editDialog.Host, 
                    editDialog.Username, 
                    editDialog.Port, 
                    editDialog.SiteName, 
                    editDialog.LocalPath, 
                    editDialog.RemotePath);

                LoadConnections();

                // Select the newly added or updated connection
                var created = Connections.FirstOrDefault(c => 
                    c.Host.Equals(editDialog.Host, System.StringComparison.OrdinalIgnoreCase) &&
                    c.Username.Equals(editDialog.Username, System.StringComparison.OrdinalIgnoreCase) &&
                    c.Port == editDialog.Port);

                if (created != null)
                {
                    ConnectionsGrid.SelectedItem = created;
                    ConnectionsGrid.ScrollIntoView(created);
                }
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsGrid.SelectedItem is SavedConnection conn)
            {
                var credDialog = new ConnectCredentialsDialog(conn.Host, conn.Username, conn.Port, conn.Name)
                {
                    Owner = this
                };

                if (credDialog.ShowDialog() == true)
                {
                    SelectedConnection = conn;
                    ConnectionUsername = credDialog.Username;
                    ConnectionPassword = credDialog.Password;
                    DialogResult = true;
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
