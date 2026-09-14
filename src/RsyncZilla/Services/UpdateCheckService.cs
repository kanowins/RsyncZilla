using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RsyncZilla.ViewModels;

namespace RsyncZilla.Services
{
    public class UpdateCheckService : ViewModelBase
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        static UpdateCheckService()
        {
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "RsyncZilla-App");
        }

        public string CurrentVersion { get; set; } = typeof(UpdateCheckService).Assembly.GetName().Version?.ToString(3) ?? "1.0.1";

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetProperty(ref _isUpdateAvailable, value);
        }

        private string _latestVersion = string.Empty;
        public string LatestVersion
        {
            get => _latestVersion;
            set
            {
                if (SetProperty(ref _latestVersion, value))
                {
                    OnPropertyChanged(nameof(UpdateBannerText));
                }
            }
        }

        public string UpdateBannerText => string.IsNullOrWhiteSpace(LatestVersion) ? "🚀 Update Available!" : $"🚀 Update Available ({LatestVersion})";

        private string _releaseNotes = string.Empty;
        public string ReleaseNotes
        {
            get => _releaseNotes;
            set => SetProperty(ref _releaseNotes, value);
        }

        private string _releaseUrl = "https://github.com/kanowins/RsyncZilla/releases";
        public string ReleaseUrl
        {
            get => _releaseUrl;
            set => SetProperty(ref _releaseUrl, value);
        }

        private bool _isChecking;
        public bool IsChecking
        {
            get => _isChecking;
            set => SetProperty(ref _isChecking, value);
        }

        public event Action<string, bool>? LogMessageReceived;

        public async Task<(bool hasUpdate, string? version, string? notes, string? url)> CheckForUpdatesAsync()
        {
            if (IsChecking) return (IsUpdateAvailable, LatestVersion, ReleaseNotes, ReleaseUrl);
            IsChecking = true;

            try
            {
                // 1. Try GitHub Releases API
                try
                {
                    var response = await HttpClient.GetStringAsync("https://api.github.com/repos/kanowins/RsyncZilla/releases/latest");
                    using var doc = JsonDocument.Parse(response);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("tag_name", out var tagElem))
                    {
                        var tag = tagElem.GetString() ?? "";
                        var candidate = tag.TrimStart('v', 'V');
                        var body = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() : "";
                        var htmlUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() : ReleaseUrl;

                        if (IsNewerVersion(CurrentVersion, candidate))
                        {
                            LatestVersion = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag : $"v{tag}";
                            ReleaseNotes = body ?? "";
                            ReleaseUrl = htmlUrl ?? ReleaseUrl;
                            IsUpdateAvailable = true;

                            LogMessageReceived?.Invoke($"[Update] New version available: {LatestVersion}!", false);
                            return (true, LatestVersion, ReleaseNotes, ReleaseUrl);
                        }
                    }
                }
                catch
                {
                    // Fallback to raw version.json if API is rate-limited or unavailable
                }

                // 2. Fallback: Raw manifest in repository
                try
                {
                    var rawJson = await HttpClient.GetStringAsync("https://raw.githubusercontent.com/kanowins/RsyncZilla/main/dist/version.json");
                    using var doc = JsonDocument.Parse(rawJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("version", out var verElem))
                    {
                        var candidate = verElem.GetString() ?? "";
                        var notesUrl = root.TryGetProperty("release_notes_url", out var nUrl) ? nUrl.GetString() : ReleaseUrl;
                        var relUrl = root.TryGetProperty("github_release_url", out var rUrl) ? rUrl.GetString() : ReleaseUrl;

                        if (IsNewerVersion(CurrentVersion, candidate))
                        {
                            LatestVersion = candidate.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? candidate : $"v{candidate}";
                            ReleaseNotes = $"A new version ({LatestVersion}) is available on GitHub.";
                            ReleaseUrl = relUrl ?? notesUrl ?? ReleaseUrl;
                            IsUpdateAvailable = true;

                            LogMessageReceived?.Invoke($"[Update] New version available: {LatestVersion}!", false);
                            return (true, LatestVersion, ReleaseNotes, ReleaseUrl);
                        }
                    }
                }
                catch
                {
                    // Repo raw file unavailable (e.g. offline)
                }

                // 3. Fallback: Local dist/version.json (allows local verification before pushing to GitHub)
                try
                {
                    string? localManifest = FindLocalManifest();
                    if (localManifest != null)
                    {
                        var rawJson = File.ReadAllText(localManifest);
                        using var doc = JsonDocument.Parse(rawJson);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("version", out var verElem))
                        {
                            var candidate = verElem.GetString() ?? "";
                            var notesUrl = root.TryGetProperty("release_notes_url", out var nUrl) ? nUrl.GetString() : ReleaseUrl;
                            var relUrl = root.TryGetProperty("github_release_url", out var rUrl) ? rUrl.GetString() : ReleaseUrl;

                            if (IsNewerVersion(CurrentVersion, candidate))
                            {
                                LatestVersion = candidate.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? candidate : $"v{candidate}";

                                var dir = Path.GetDirectoryName(localManifest);
                                var notesFile = dir != null ? Path.Combine(dir, "RELEASE_NOTES.md") : null;
                                if (notesFile != null && File.Exists(notesFile))
                                {
                                    ReleaseNotes = File.ReadAllText(notesFile);
                                }
                                else
                                {
                                    ReleaseNotes = $"A new version ({LatestVersion}) is available.";
                                }

                                ReleaseUrl = relUrl ?? notesUrl ?? ReleaseUrl;
                                IsUpdateAvailable = true;

                                LogMessageReceived?.Invoke($"[Update] New version available: {LatestVersion}!", false);
                                return (true, LatestVersion, ReleaseNotes, ReleaseUrl);
                            }
                        }
                    }
                }
                catch
                {
                }

                IsUpdateAvailable = false;
                return (false, null, null, null);
            }
            finally
            {
                IsChecking = false;
            }
        }

        public static bool IsNewerVersion(string currentVersion, string candidateVersion)
        {
            if (string.IsNullOrWhiteSpace(candidateVersion)) return false;

            var cleanCurrent = currentVersion.TrimStart('v', 'V').Trim();
            var cleanCandidate = candidateVersion.TrimStart('v', 'V').Trim();

            if (Version.TryParse(cleanCurrent, out var curVer) &&
                Version.TryParse(cleanCandidate, out var candVer))
            {
                return candVer > curVer;
            }

            return string.Compare(cleanCandidate, cleanCurrent, StringComparison.OrdinalIgnoreCase) > 0;
        }

        private static string? FindLocalManifest()
        {
            try
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                for (int i = 0; i < 7 && dir != null; i++)
                {
                    var p1 = Path.Combine(dir.FullName, "version.json");
                    if (File.Exists(p1)) return p1;
                    var p2 = Path.Combine(dir.FullName, "dist", "version.json");
                    if (File.Exists(p2)) return p2;
                    dir = dir.Parent;
                }

                var cwd = Directory.GetCurrentDirectory();
                var cwdManifest = Path.Combine(cwd, "dist", "version.json");
                if (File.Exists(cwdManifest)) return cwdManifest;
            }
            catch { }
            return null;
        }
    }
}
