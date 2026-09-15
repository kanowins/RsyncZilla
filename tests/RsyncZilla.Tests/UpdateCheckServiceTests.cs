using RsyncZilla.Services;
using Xunit;

namespace RsyncZilla.Tests
{
    public class UpdateCheckServiceTests
    {
        [Theory]
        [InlineData("1.0.0", "1.0.1", true)]
        [InlineData("1.0.0", "1.1.0", true)]
        [InlineData("1.0.0", "2.0.0", true)]
        [InlineData("1.0.0", "v1.0.1", true)]
        [InlineData("v1.0.0", "v1.2.0", true)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("1.0.0", "0.9.0", false)]
        [InlineData("1.2.0", "1.1.9", false)]
        [InlineData("1.0.0", "", false)]
        [InlineData("1.0.0", null, false)]
        public void IsNewerVersion_EvaluatesCorrectly(string current, string? candidate, bool expected)
        {
            var result = UpdateCheckService.IsNewerVersion(current, candidate!);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void InitialState_ShouldNotHaveUpdateAvailable()
        {
            var service = new UpdateCheckService();
            Assert.False(service.IsUpdateAvailable);
            Assert.False(string.IsNullOrWhiteSpace(service.CurrentVersion));
        }

        [Fact]
        public async Task CheckForUpdatesAsync_DetectsUpdateWhenManifestIsNewer()
        {
            var service = new UpdateCheckService();
            service.CurrentVersion = "0.0.1"; // Simulate an older installed version
            string? logged = null;
            service.LogMessageReceived += (msg, err) => logged = msg;

            var result = await service.CheckForUpdatesAsync();

            Assert.True(result.hasUpdate);
            Assert.False(string.IsNullOrWhiteSpace(result.version));
            Assert.True(service.IsUpdateAvailable);
            Assert.False(string.IsNullOrWhiteSpace(service.UpdateBannerText));
            Assert.NotEmpty(service.ReleaseNotes);
            Assert.False(string.IsNullOrWhiteSpace(service.InstallerUrl));
            Assert.Contains(".exe", service.InstallerUrl);
        }

        [Fact]
        public void InstallerUrl_Property_ShouldNotifyChanges()
        {
            var service = new UpdateCheckService();
            string? changedProp = null;
            service.PropertyChanged += (s, e) => changedProp = e.PropertyName;

            service.InstallerUrl = "https://example.com/Setup.exe";

            Assert.Equal("InstallerUrl", changedProp);
            Assert.Equal("https://example.com/Setup.exe", service.InstallerUrl);
        }
    }
}
