using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RsyncZilla.Models;
using RsyncZilla.Services;
using Xunit;

namespace RsyncZilla.Tests
{
    public class FileZillaImportServiceTests
    {
        private readonly FileZillaImportService _service = new();

        [Theory]
        [InlineData("1 0 4 home 6 debian", "/home/debian")]
        [InlineData("1 0", "/")]
        [InlineData("1 0 9 web sites 4 logs", "/web sites/logs")]
        [InlineData("1 1 2 C: 7 Windows", @"C:\Windows")]
        [InlineData("/var/www/html", "/var/www/html")]
        [InlineData(@"C:\Projects\Test", @"C:\Projects\Test")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void DecodeRemotePath_ShouldDecodeCorrectly(string input, string expected)
        {
            var result = FileZillaImportService.DecodeRemotePath(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ParseXml_StandardSiteManagerXml_ShouldParseServers()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<FileZilla3 version=""3.59.0"" platform=""windows"">
    <Servers>
        <Server>
            <Host>applabgamelist.com</Host>
            <Port>21</Port>
            <Protocol>0</Protocol>
            <Type>0</Type>
            <User>debian</User>
            <Name>sumalab.com</Name>
            <RemotePath>1 0 4 home 6 debian</RemotePath>
            <LocalPath>E:\tmp\applab\</LocalPath>
            <Comments>Main production server</Comments>
        </Server>
    </Servers>
</FileZilla3>";

            var sites = _service.ParseXml(xml);

            Assert.Single(sites);
            var site = sites[0];
            Assert.Equal("applabgamelist.com", site.Host);
            Assert.Equal(22, site.Port); // Automatically converted from port 21 to 22 for SSH/SFTP
            Assert.Equal("debian", site.Username);
            Assert.Equal("sumalab.com", site.Name);
            Assert.Equal("SFTP (from FTP)", site.Protocol);
            Assert.Equal("/home/debian", site.RemotePath);
            Assert.Equal(@"E:\tmp\applab\", site.LocalPath);
            Assert.Equal("Main production server", site.Comments);
            Assert.True(site.IsSelected);
            Assert.False(site.IsDuplicate);
        }

        [Fact]
        public void ParseXml_NestedFolders_ShouldTrackFolderPath()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<FileZilla3 version=""3.59.0"" platform=""windows"">
    <Servers>
        <Folder expanded=""1"">Clients
            <Folder expanded=""1"">Acme Corp
                <Server>
                    <Host>acme.example.com</Host>
                    <Port>2222</Port>
                    <Protocol>1</Protocol>
                    <User>deploy</User>
                    <Name>Acme Web</Name>
                    <RemoteDir>/var/www/acme</RemoteDir>
                    <LocalDir>C:\Dev\Acme</LocalDir>
                </Server>
            </Folder>
            <Server>
                <Host>shared.example.com</Host>
                <Port>22</Port>
                <Protocol>1</Protocol>
                <User>root</User>
                <Name>Shared Client Host</Name>
            </Server>
        </Folder>
    </Servers>
</FileZilla3>";

            var sites = _service.ParseXml(xml);

            Assert.Equal(2, sites.Count);

            var acme = sites.FirstOrDefault(s => s.Name == "Acme Web");
            Assert.NotNull(acme);
            Assert.Equal("Clients / Acme Corp", acme.Folder);
            Assert.Equal(2222, acme.Port);
            Assert.Equal("deploy", acme.Username);
            Assert.Equal("SFTP (SSH)", acme.Protocol);
            Assert.Equal("/var/www/acme", acme.RemotePath);
            Assert.Equal(@"C:\Dev\Acme", acme.LocalPath);

            var shared = sites.FirstOrDefault(s => s.Name == "Shared Client Host");
            Assert.NotNull(shared);
            Assert.Equal("Clients", shared.Folder);
        }

        [Fact]
        public void ParseXml_MissingPort_ShouldDefaultBasedOnProtocol()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<FileZilla3 version=""3.59.0"">
    <Servers>
        <Server>
            <Host>sftp.server.com</Host>
            <Protocol>1</Protocol>
            <User>admin</User>
        </Server>
        <Server>
            <Host>ftp.server.com</Host>
            <Protocol>0</Protocol>
            <User>anonymous</User>
        </Server>
    </Servers>
</FileZilla3>";

            var sites = _service.ParseXml(xml);

            Assert.Equal(2, sites.Count);
            Assert.Equal(22, sites[0].Port);
            Assert.Equal(22, sites[1].Port); // FTP defaults/converts to 22 for RsyncZilla
        }

        [Fact]
        public void ParseXml_Port21_ShouldConvertAutomaticallyToPort22()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<FileZilla3 version=""3.59.0"">
    <Servers>
        <Server>
            <Host>ftp.myhost.com</Host>
            <Port>21</Port>
            <Protocol>0</Protocol>
            <User>myuser</User>
        </Server>
    </Servers>
</FileZilla3>";

            var sites = _service.ParseXml(xml);

            Assert.Single(sites);
            Assert.Equal(22, sites[0].Port);
            Assert.Equal("SFTP (from FTP)", sites[0].Protocol);

            var savedConn = sites[0].ToSavedConnection();
            Assert.Equal(22, savedConn.Port);
        }

        [Fact]
        public void ParseXml_DuplicateDetection_ShouldFlagExistingConnections()
        {
            var existing = new List<SavedConnection>
            {
                new SavedConnection
                {
                    Host = "existing.server.com",
                    Username = "alice",
                    Port = 22,
                    Name = "Existing Site"
                }
            };

            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<FileZilla3 version=""3.59.0"">
    <Servers>
        <Server>
            <Host>existing.server.com</Host>
            <Port>22</Port>
            <User>alice</User>
            <Name>FileZilla Existing</Name>
        </Server>
        <Server>
            <Host>new.server.com</Host>
            <Port>22</Port>
            <User>bob</User>
            <Name>Brand New</Name>
        </Server>
    </Servers>
</FileZilla3>";

            var sites = _service.ParseXml(xml, existing);

            Assert.Equal(2, sites.Count);
            var dup = sites.First(s => s.Host == "existing.server.com");
            Assert.True(dup.IsDuplicate);
            Assert.Equal("Update existing", dup.StatusText);

            var newSite = sites.First(s => s.Host == "new.server.com");
            Assert.False(newSite.IsDuplicate);
            Assert.Equal("New", newSite.StatusText);
        }

        [Fact]
        public void Import_ShouldSaveSitesToConnectionManager()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"rsynczilla_test_{Guid.NewGuid():N}.json");

            try
            {
                var connManager = new ConnectionManagerService(tempFile);

                var sites = new List<FileZillaSite>
                {
                    new FileZillaSite
                    {
                        Name = "Test Server 1",
                        Host = "10.0.0.1",
                        Username = "ubuntu",
                        Port = 22,
                        RemotePath = "/home/ubuntu/app",
                        LocalPath = @"C:\TestApp",
                        IsSelected = true
                    },
                    new FileZillaSite
                    {
                        Name = "Test Server 2 (Unselected)",
                        Host = "10.0.0.2",
                        Username = "ubuntu",
                        Port = 22,
                        IsSelected = false
                    }
                };

                int imported = _service.Import(sites, connManager, overwriteExisting: true);

                Assert.Equal(1, imported);

                var loaded = connManager.LoadConnections();
                Assert.Single(loaded);
                Assert.Equal("10.0.0.1", loaded[0].Host);
                Assert.Equal("ubuntu", loaded[0].Username);
                Assert.Equal("Test Server 1", loaded[0].Name);
                Assert.Equal("/home/ubuntu/app", loaded[0].LastRemotePath);
                Assert.Equal(@"C:\TestApp", loaded[0].LastLocalPath);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}
