using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using RsyncZilla.Services;

namespace RsyncZilla.Views
{
    public partial class UpdateDialog : Window
    {
        private readonly string _releaseUrl;
        private readonly string? _installerUrl;
        private readonly UpdateCheckService? _updateService;
        private CancellationTokenSource? _downloadCts;

        public UpdateDialog(
            string currentVersion, 
            string latestVersion, 
            string releaseNotes, 
            string releaseUrl, 
            string? installerUrl = null, 
            UpdateCheckService? updateService = null)
        {
            InitializeComponent();
            _releaseUrl = string.IsNullOrWhiteSpace(releaseUrl) ? "https://github.com/kanowins/RsyncZilla/releases" : releaseUrl;
            _installerUrl = installerUrl;
            _updateService = updateService;

            TxtTitle.Text = $"RsyncZilla {latestVersion} is available!";
            TxtVersions.Text = $"Installed version: {currentVersion}  |  Latest version: {latestVersion}";
            TxtNotes.Text = string.IsNullOrWhiteSpace(releaseNotes)
                ? "No detailed release notes provided. Click below to view release assets on GitHub."
                : releaseNotes;

            if (string.IsNullOrWhiteSpace(_installerUrl) && _updateService?.InstallerUrl != null)
            {
                _installerUrl = _updateService.InstallerUrl;
            }
        }

        public UpdateDialog(UpdateCheckService updateService)
            : this(
                updateService.CurrentVersion,
                updateService.LatestVersion,
                updateService.ReleaseNotes,
                updateService.ReleaseUrl,
                updateService.InstallerUrl,
                updateService)
        {
        }

        private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
        {
            var url = _installerUrl ?? _updateService?.InstallerUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                // Fallback to opening GitHub release page
                ViewGitHubButton_Click(sender, e);
                return;
            }

            var service = _updateService ?? new UpdateCheckService();

            BtnUpdateNow.IsEnabled = false;
            BtnViewGitHub.IsEnabled = false;
            DownloadProgressPanel.Visibility = Visibility.Visible;
            TxtDownloadStatus.Text = "Downloading installer...";
            DownloadProgressBar.Value = 0;

            _downloadCts = new CancellationTokenSource();
            var progress = new Progress<double>(percent =>
            {
                DownloadProgressBar.Value = percent;
                TxtDownloadStatus.Text = $"Downloading installer... {percent:0}%";
            });

            try
            {
                var (success, localPath, error) = await service.DownloadInstallerAsync(url, progress, _downloadCts.Token);
                if (success && !string.IsNullOrWhiteSpace(localPath))
                {
                    TxtDownloadStatus.Text = "Launching installer...";
                    UpdateCheckService.LaunchInstallerAndShutdown(localPath);
                }
                else
                {
                    MessageBox.Show(
                        this,
                        $"Could not download the update automatically:\n\n{error}\n\nYou will be redirected to the GitHub Release page to download it manually.",
                        "Update Download Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    ViewGitHubButton_Click(sender, e);
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Failed to download installer: {ex.Message}",
                    "Update Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                BtnUpdateNow.IsEnabled = true;
                BtnViewGitHub.IsEnabled = true;
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ViewGitHubButton_Click(object sender, RoutedEventArgs e)
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
            _downloadCts?.Cancel();
            Close();
        }
    }
}
