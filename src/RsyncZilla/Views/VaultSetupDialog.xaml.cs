using System.Windows;
using System.Windows.Input;

namespace RsyncZilla.Views
{
    public partial class VaultSetupDialog : Window
    {
        public string MasterPassword { get; private set; } = string.Empty;

        public VaultSetupDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => MasterPasswordBox.Focus();
        }

        private void EnableButton_Click(object sender, RoutedEventArgs e)
        {
            var pass = MasterPasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;

            if (string.IsNullOrEmpty(pass))
            {
                ShowError("Please enter a master password.");
                MasterPasswordBox.Focus();
                return;
            }

            if (pass.Length < 4)
            {
                ShowError("Password must be at least 4 characters long.");
                MasterPasswordBox.Focus();
                return;
            }

            if (pass != confirm)
            {
                ShowError("Passwords do not match. Please re-enter.");
                ConfirmPasswordBox.Focus();
                return;
            }

            MasterPassword = pass;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ConfirmPasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                EnableButton_Click(sender, e);
            }
        }

        private void ShowError(string msg)
        {
            ErrorMessageTextBlock.Text = msg;
            ErrorMessageTextBlock.Visibility = Visibility.Visible;
        }
    }
}
