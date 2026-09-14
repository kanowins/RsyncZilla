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
            Assert.Equal("1.0.0", service.CurrentVersion);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_DetectsUpdateWhenManifestIsNewer()
        {
            var service = new UpdateCheckService();
            var result = await service.CheckForUpdatesAsync();

            Assert.True(result.hasUpdate);
            Assert.Contains("1.0.1", result.version);
            Assert.True(service.IsUpdateAvailable);
            Assert.Contains("1.0.1", service.UpdateBannerText);
            Assert.NotEmpty(service.ReleaseNotes);
        }
    }
}
