using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
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

        [Fact]
        public void MarkdownFlowDocumentBuilder_ShouldFormatHeadingsListsBoldCodeAndLinks()
        {
            Exception? threadEx = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var md = """
                    # RsyncZilla Release Notes

                    ## Version 1.7.4 (2026-11-18)

                    ### ✨ Improvements
                    - **Formatted Notes**: Renders `Markdown` with *emphasis* and [GitHub](https://github.com/kanowins/RsyncZilla).
                      - Sub-item with <kbd>Enter</kbd> key.

                    ---

                    ```json
                    { "ok": true }
                    ```
                    """;

                    var doc = MarkdownFlowDocumentBuilder.Build(md);
                    Assert.NotNull(doc);
                    Assert.True(doc.Blocks.Count >= 6);

                    // Verify there are no raw markdown headings or bold markers left in rendered text
                    var fullText = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
                    Assert.DoesNotContain("## Version", fullText);
                    Assert.DoesNotContain("### ✨", fullText);
                    Assert.DoesNotContain("**Formatted", fullText);
                    Assert.Contains("Version 1.7.4", fullText);
                    Assert.Contains("Formatted Notes", fullText);
                    Assert.Contains("Markdown", fullText);
                    Assert.Contains("GitHub", fullText);
                    Assert.Contains("Enter", fullText);
                    Assert.Contains("{ \"ok\": true }", fullText);

                    // Verify hyperlink was created
                    var hasHyperlink = doc.Blocks
                        .OfType<Paragraph>()
                        .SelectMany(p => p.Inlines)
                        .OfType<Hyperlink>()
                        .Any(h => h.NavigateUri?.AbsoluteUri == "https://github.com/kanowins/RsyncZilla");
                    Assert.True(hasHyperlink);

                    // Verify empty/null markdown is handled safely
                    var emptyDoc = MarkdownFlowDocumentBuilder.Build(null);
                    Assert.Single(emptyDoc.Blocks);
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (threadEx != null)
            {
                throw new Xunit.Sdk.XunitException($"STA test failed: {threadEx}");
            }
        }
    }
}
