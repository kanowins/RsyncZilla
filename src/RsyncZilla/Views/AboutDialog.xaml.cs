using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Navigation;

namespace RsyncZilla.Views
{
    public partial class AboutDialog : Window
    {
        public string AppVersion => typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "1.0.1";

        public AboutDialog()
        {
            InitializeComponent();
            TxtAppTitle.Text = $"RsyncZilla {AppVersion}";
            PopulateSystemDetails();
        }

        private void PopulateSystemDetails()
        {
            try
            {
                TxtOsVersion.Text = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
                TxtPlatform.Text = Environment.Is64BitOperatingSystem ? "64-bit operating system" : "32-bit operating system";
                TxtCpuFeatures.Text = $"{Environment.ProcessorCount} logical processor cores";
                TxtAppDir.Text = AppDomain.CurrentDomain.BaseDirectory;
            }
            catch
            {
                TxtOsVersion.Text = Environment.OSVersion.ToString();
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch { }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"RsyncZilla {AppVersion}");
                sb.AppendLine("Copyright (C) 2026 Sumalab");
                sb.AppendLine("Homepage: https://github.com/kanowins/filezilla");
                sb.AppendLine();
                sb.AppendLine("Build information:");
                sb.AppendLine($"  Compiled for: win-x64 (.NET {Environment.Version})");
                sb.AppendLine("  Sync Engine: rsync 3.x (SSH delta-transfer)");
                sb.AppendLine("  SSH & SFTP: SSH.NET / OpenSSH");
                sb.AppendLine();
                sb.AppendLine("System details:");
                sb.AppendLine($"  Operating System: {TxtOsVersion.Text}");
                sb.AppendLine($"  Platform: {TxtPlatform.Text}");
                sb.AppendLine($"  CPU: {TxtCpuFeatures.Text}");
                sb.AppendLine($"  Directory: {TxtAppDir.Text}");
                sb.AppendLine();
                sb.AppendLine("Created by Sumalab: https://sumalab.com");

                Clipboard.SetText(sb.ToString());
                MessageBox.Show(this, "System and build details copied to clipboard.", "RsyncZilla", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
