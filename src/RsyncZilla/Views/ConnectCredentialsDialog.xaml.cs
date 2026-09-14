using System.Windows;
using System.Windows.Input;

namespace RsyncZilla.Views
{
    public partial class ConnectCredentialsDialog : Window
    {
        public string Host { get; }
        public int Port { get; }
        public string Username => UsernameTextBox.Text.Trim();
        public string Password => PasswordBoxInput.Password;

        public ConnectCredentialsDialog(string host, string username, int port, string? siteName = null)
        {
            InitializeComponent();
            Host = host;
            Port = port;

            SiteTitleTextBlock.Text = !string.IsNullOrWhiteSpace(siteName) 
                ? $"Conectar a {siteName}" 
                : $"Conectar a {host}";
            HostInfoTextBlock.Text = $"Servidor: {host} (Puerto: {port})";
            UsernameTextBox.Text = username;

            Loaded += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
                {
                    UsernameTextBox.Focus();
                }
                else
                {
                    PasswordBoxInput.Focus();
                }
            };
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show("Por favor, ingrese un nombre de usuario.", "Usuario requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                UsernameTextBox.Focus();
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

        private void PasswordBoxInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ConnectButton_Click(sender, e);
            }
        }
    }
}
