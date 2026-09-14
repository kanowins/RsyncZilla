using System;
using System.IO;
using System.Threading.Tasks;
using RsyncZilla.Models;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;
using Xunit;

namespace RsyncZilla.Tests
{
    public class RemoteEditServiceTests
    {
        [Fact]
        public void GetLocalTempPath_ShouldComputeExpectedDirectoryStructure()
        {
            var service = new RemoteEditService();
            var path = service.GetLocalTempPath("myserver.com", 22, "debian", "/var/www/html/index.php");

            Assert.Contains("RsyncZilla", path);
            Assert.Contains("RemoteEdit", path);
            Assert.Contains("debian@myserver.com_22", path);
            Assert.EndsWith(Path.Combine("var", "www", "html", "index.php"), path);
        }

        [Fact]
        public async Task OpenFileForEditingAsync_WithDirectory_ShouldReturnError()
        {
            var service = new RemoteEditService();
            var session = new RemoteSessionViewModel();

            var dirItem = new FileItem
            {
                Name = "myfolder",
                FullPath = "/home/debian/myfolder",
                IsDirectory = true
            };

            var (success, localPath, error) = await service.OpenFileForEditingAsync(session, dirItem);

            Assert.False(success);
            Assert.Null(localPath);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task OpenFileForEditingAsync_WithParentItem_ShouldReturnError()
        {
            var service = new RemoteEditService();
            var session = new RemoteSessionViewModel();

            var parentItem = new FileItem
            {
                Name = "..",
                FullPath = "/home",
                IsParent = true
            };

            var (success, localPath, error) = await service.OpenFileForEditingAsync(session, parentItem);

            Assert.False(success);
            Assert.Null(localPath);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task OpenFileForEditingAsync_WhenDisconnected_ShouldReturnError()
        {
            var service = new RemoteEditService();
            var session = new RemoteSessionViewModel();
            Assert.False(session.IsConnected);

            var fileItem = new FileItem
            {
                Name = "test.txt",
                FullPath = "/home/debian/test.txt",
                IsDirectory = false
            };

            var (success, localPath, error) = await service.OpenFileForEditingAsync(session, fileItem);

            Assert.False(success);
            Assert.Null(localPath);
            Assert.Contains("not connected", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EditRemoteFileCommand_ShouldRequireConnection()
        {
            var vm = new MainViewModel();

            Assert.False(vm.IsConnected);
            Assert.False(vm.EditRemoteFileCommand.CanExecute(null));
        }
    }
}
