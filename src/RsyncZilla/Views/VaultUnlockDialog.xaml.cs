using System.Windows;
using System.Windows.Input;
using RsyncZilla.Services;

namespace RsyncZilla.Views
{
    public partial class VaultUnlockDialog : Window
    {
        private readonly VaultService _vaultService;

        public VaultUnlockDialog(VaultService vaultService)
        {
            InitializeComponent();
            _vaultService = vaultService;
            Loaded += (s, e) => PasswordBoxInput.Focus();
        }

        private void UnlockButton_Click(object sender, RoutedEventArgs e)
        {
            var pass = PasswordBoxInput.Password;
            if (string.IsNullOrEmpty(pass))
            {
                ErrorMessageTextBlock.Text = "Please enter your master password.";
                ErrorMessageTextBlock.Visibility = Visibility.Visible;
                PasswordBoxInput.Focus();
                return;
            }

            if (_vaultService.Unlock(pass))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                ErrorMessageTextBlock.Text = "Incorrect master password. Please try again.";
                ErrorMessageTextBlock.Visibility = Visibility.Visible;
                PasswordBoxInput.SelectAll();
                PasswordBoxInput.Focus();
            }
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
                UnlockButton_Click(sender, e);
            }
        }
    }
}
