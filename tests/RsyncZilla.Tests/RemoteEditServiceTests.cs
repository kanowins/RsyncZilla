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

        [Fact]
        public async Task ProcessFileChange_WhenSessionDisconnected_ShouldFireFileUploadFailed()
        {
            var service = new RemoteEditService();
            var session = new RemoteSessionViewModel();
            Assert.False(session.IsConnected);

            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "Hello updated content!");

                service.RegisterTestTracker(tempFile, "/remote/path/test.txt", session, Array.Empty<byte>());

                string? failedPath = null;
                string? failedFile = null;
                string? failedError = null;

                service.FileUploadFailed += (sess, path, file, err) =>
                {
                    failedPath = path;
                    failedFile = file;
                    failedError = err;
                };

                var handled = await service.TriggerProcessFileChangeAsync(tempFile);

                Assert.True(handled);
                Assert.Equal("/remote/path/test.txt", failedPath);
                Assert.Equal(Path.GetFileName(tempFile), failedFile);
                Assert.NotNull(failedError);
                Assert.Contains("not connected", failedError, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ShowInExplorerCommand_WhenFileSelected_ShouldLaunchExplorerWithSelectArgument()
        {
            var vm = new MainViewModel();
            var tempFile = Path.GetTempFileName();
            try
            {
                string? launchedCmd = null;
                string? launchedArgs = null;
                vm.ExplorerLauncher = (cmd, args) =>
                {
                    launchedCmd = cmd;
                    launchedArgs = args;
                };

                var item = new FileItem
                {
                    Name = Path.GetFileName(tempFile),
                    FullPath = tempFile,
                    IsDirectory = false
                };

                vm.ShowInExplorerCommand.Execute(item);

                Assert.Equal("explorer.exe", launchedCmd);
                Assert.NotNull(launchedArgs);
                Assert.StartsWith("/select,", launchedArgs);
                Assert.Contains(tempFile, launchedArgs);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ShowInExplorerCommand_WhenDirectorySelected_ShouldLaunchExplorerWithDirectoryPath()
        {
            var vm = new MainViewModel();
            var tempDir = Path.Combine(Path.GetTempPath(), "RsyncZillaTestDir_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string? launchedCmd = null;
                string? launchedArgs = null;
                vm.ExplorerLauncher = (cmd, args) =>
                {
                    launchedCmd = cmd;
                    launchedArgs = args;
                };

                var item = new FileItem
                {
                    Name = Path.GetFileName(tempDir),
                    FullPath = tempDir,
                    IsDirectory = true
                };

                vm.ShowInExplorerCommand.Execute(item);

                Assert.Equal("explorer.exe", launchedCmd);
                Assert.NotNull(launchedArgs);
                Assert.DoesNotContain("/select,", launchedArgs);
                Assert.Contains(tempDir, launchedArgs);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir);
            }
        }

        [Fact]
        public void OpenLocalFileCommand_WhenFileExists_ShouldInvokeFileOpener()
        {
            var vm = new MainViewModel();
            var tempFile = Path.GetTempFileName();
            try
            {
                string? openedPath = null;
                vm.FileOpener = path => openedPath = path;

                var item = new FileItem
                {
                    Name = Path.GetFileName(tempFile),
                    FullPath = tempFile,
                    IsDirectory = false
                };

                vm.OpenLocalFileCommand.Execute(item);

                Assert.Equal(tempFile, openedPath);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void OpenLocalFileCommand_WhenItemIsDirectory_ShouldNotOpen()
        {
            var vm = new MainViewModel();
            string? openedPath = null;
            vm.FileOpener = path => openedPath = path;

            var item = new FileItem
            {
                Name = "MyFolder",
                FullPath = "C:\\MyFolder",
                IsDirectory = true
            };

            vm.OpenLocalFileCommand.Execute(item);

            Assert.Null(openedPath);
        }
    }
}
