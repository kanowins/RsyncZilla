using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using RsyncZilla.Services;

namespace RsyncZilla.Views
{
    public partial class ReleaseNotesDialog : Window
    {
        private const string GitHubReleasesUrl = "https://github.com/kanowins/RsyncZilla/releases";
        private const string RawReleaseNotesUrl = "https://raw.githubusercontent.com/kanowins/RsyncZilla/main/dist/RELEASE_NOTES.md";

        public ReleaseNotesDialog(string currentVersion)
        {
            InitializeComponent();
            TxtCurrentVersion.Text = $"Installed version: {currentVersion}";
            Loaded += ReleaseNotesDialog_Loaded;
        }

        private async void ReleaseNotesDialog_Loaded(object sender, RoutedEventArgs e)
        {
            var localMarkdown = TryLoadLocalReleaseNotes();
            if (!string.IsNullOrWhiteSpace(localMarkdown))
            {
                NotesViewer.Document = MarkdownFlowDocumentBuilder.Build(localMarkdown);
                return;
            }

            NotesViewer.Document = MarkdownFlowDocumentBuilder.Build("Loading release notes from GitHub...");
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("RsyncZilla-Updater");
                var remoteMarkdown = await http.GetStringAsync(RawReleaseNotesUrl);
                NotesViewer.Document = MarkdownFlowDocumentBuilder.Build(remoteMarkdown);
            }
            catch
            {
                NotesViewer.Document = MarkdownFlowDocumentBuilder.Build(
                    "Could not load local or online release notes. Click **View Releases on GitHub** below to view the full changelog.");
            }
        }

        private static string? TryLoadLocalReleaseNotes()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(baseDir, "RELEASE_NOTES.md"),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "RELEASE_NOTES.md")),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "dist", "RELEASE_NOTES.md"))
                };

                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        return File.ReadAllText(path);
                    }
                }
            }
            catch { }

            return null;
        }

        private void ViewGitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = GitHubReleasesUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
