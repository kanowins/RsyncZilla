using System.Windows;
using System.Windows.Input;
using RsyncZilla.Services;

namespace RsyncZilla.Views
{
    public partial class VaultChangePasswordDialog : Window
    {
        private readonly VaultService _vaultService;

        public VaultChangePasswordDialog(VaultService vaultService)
        {
            InitializeComponent();
            _vaultService = vaultService;
            Loaded += (s, e) => CurrentPasswordBox.Focus();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var current = CurrentPasswordBox.Password;
            var newPass = NewPasswordBox.Password;
            var confirm = ConfirmNewPasswordBox.Password;

            if (string.IsNullOrEmpty(current))
            {
                ShowError("Please enter your current password.");
                CurrentPasswordBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(newPass))
            {
                ShowError("Please enter a new password.");
                NewPasswordBox.Focus();
                return;
            }

            if (newPass.Length < 4)
            {
                ShowError("New password must be at least 4 characters long.");
                NewPasswordBox.Focus();
                return;
            }

            if (newPass != confirm)
            {
                ShowError("New passwords do not match.");
                ConfirmNewPasswordBox.Focus();
                return;
            }

            if (!_vaultService.ChangeMasterPassword(current, newPass))
            {
                ShowError("Current password is incorrect. Please check and try again.");
                CurrentPasswordBox.SelectAll();
                CurrentPasswordBox.Focus();
                return;
            }

            MessageBox.Show("Master password changed successfully.", "Password Changed", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ConfirmNewPasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SaveButton_Click(sender, e);
            }
        }

        private void ShowError(string msg)
        {
            ErrorMessageTextBlock.Text = msg;
            ErrorMessageTextBlock.Visibility = Visibility.Visible;
        }
    }
}
