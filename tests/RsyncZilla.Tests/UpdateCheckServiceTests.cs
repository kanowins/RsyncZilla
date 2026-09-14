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
        public void UpdateBannerText_ReflectsLatestVersion()
        {
            var service = new UpdateCheckService();
            Assert.Equal("🚀 Update Available!", service.UpdateBannerText);

            service.LatestVersion = "v1.2.0";
            Assert.Equal("🚀 Update Available (v1.2.0)", service.UpdateBannerText);
        }
    }
}
