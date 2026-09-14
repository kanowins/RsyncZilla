using System;
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

        public string CurrentVersion { get; } = "1.0.0";

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

        private string _releaseUrl = "https://github.com/kanowins/filezilla/releases";
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
                    var response = await HttpClient.GetStringAsync("https://api.github.com/repos/kanowins/filezilla/releases/latest");
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
                    var rawJson = await HttpClient.GetStringAsync("https://raw.githubusercontent.com/kanowins/filezilla/main/dist/version.json");
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
    }
}
