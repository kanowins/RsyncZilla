using System;
using System.Diagnostics;
using System.Windows;

namespace RsyncZilla.Views
{
    public partial class UpdateDialog : Window
    {
        private readonly string _releaseUrl;

        public UpdateDialog(string currentVersion, string latestVersion, string releaseNotes, string releaseUrl)
        {
            InitializeComponent();
            _releaseUrl = string.IsNullOrWhiteSpace(releaseUrl) ? "https://github.com/kanowins/filezilla/releases" : releaseUrl;

            TxtTitle.Text = $"RsyncZilla {latestVersion} is available!";
            TxtVersions.Text = $"Installed version: {currentVersion}  |  Latest version: {latestVersion}";
            TxtNotes.Text = string.IsNullOrWhiteSpace(releaseNotes)
                ? "No detailed release notes provided. Click below to view release assets on GitHub."
                : releaseNotes;
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _releaseUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open release URL: {ex.Message}", "RsyncZilla", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
