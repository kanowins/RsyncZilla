using System;
using System.IO;
using RsyncZilla.Models;
using RsyncZilla.Services;
using Xunit;

namespace RsyncZilla.Tests
{
    public class RsyncServiceTests
    {
        [Theory]
        [InlineData(@"C:\Users\Test\file.txt", "/cygdrive/c/Users/Test/file.txt")]
        [InlineData(@"E:\Dropbox\projects\site", "/cygdrive/e/Dropbox/projects/site")]
        [InlineData(@"D:\My Folder\Data.csv", "/cygdrive/d/My Folder/Data.csv")]
        public void ToCygwinPath_ShouldConvertDriveLetterProperly(string input, string expected)
        {
            var result = RsyncService.ToCygwinPath(input);
            Assert.Equal(expected.Replace('\\', '/'), result);
        }

        [Fact]
        public void FindRsyncBinary_ShouldFindValidExecutable()
        {
            var service = new RsyncService();
            var path = service.FindRsyncBinary();

            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.True(File.Exists(path), $"El ejecutable de rsync debe existir en disco. Ruta encontrada: {path}");
        }

        [Fact]
        public void FindSshBinary_ShouldFindValidExecutable()
        {
            var service = new RsyncService();
            var path = service.FindSshBinary();

            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.True(File.Exists(path), $"El ejecutable de ssh debe existir en disco. Ruta encontrada: {path}");
        }

        [Fact]
        public void FileItem_DisplaySize_ShouldFormatCorrectly()
        {
            var dirItem = new FileItem { Name = "docs", IsDirectory = true, Length = 0 };
            Assert.Equal("<CARPETA>", dirItem.DisplaySize);

            var file1 = new FileItem { Name = "test.txt", IsDirectory = false, Length = 1024 };
            Assert.Equal("1 KB", file1.DisplaySize);

            var file2 = new FileItem { Name = "big.zip", IsDirectory = false, Length = 10485760 }; // 10 MB
            Assert.Equal("10 MB", file2.DisplaySize);
        }

        [Fact]
        public void TransferTask_StatusTransitions_ShouldWork()
        {
            var task = new TransferTask
            {
                FileName = "test.zip",
                Status = TransferStatus.Pending
            };

            Assert.False(task.IsRunning);
            Assert.Contains("Pendiente", task.StatusBadge);

            task.Status = TransferStatus.Running;
            Assert.True(task.IsRunning);
            Assert.Contains("Transfiriendo", task.StatusBadge);

            task.Status = TransferStatus.Completed;
            Assert.False(task.IsRunning);
            Assert.Contains("Completado", task.StatusBadge);
        }

        [Theory]
        [InlineData(23, "rsync: [receiver] mkstemp: Permission denied (13)", RsyncFailureReason.PermissionDenied)]
        [InlineData(1, "Operation not permitted on destination", RsyncFailureReason.PermissionDenied)]
        [InlineData(255, "Permission denied (publickey,password)", RsyncFailureReason.PermissionDenied)]
        [InlineData(255, "ssh: connect to host 127.0.0.1 port 22: Connection refused", RsyncFailureReason.ConnectionError)]
        [InlineData(12, "rsync: error in rsync protocol data stream (code 12)", RsyncFailureReason.ConnectionError)]
        [InlineData(30, "Timeout in data send/receive (code 30)", RsyncFailureReason.ConnectionError)]
        [InlineData(255, "Connection reset by peer", RsyncFailureReason.ConnectionError)]
        public void ClassifyError_ShouldDistinguishPermissionsAndConnectionErrors(int exitCode, string output, RsyncFailureReason expected)
        {
            var result = RsyncService.ClassifyError(exitCode, output);
            Assert.Equal(expected, result);
        }
    }
}
