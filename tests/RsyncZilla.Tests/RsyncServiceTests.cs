using System;
using System.IO;
using RsyncZilla.Models;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;
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
            Assert.Equal("<DIR>", dirItem.DisplaySize);

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
            Assert.Contains("Pending", task.StatusBadge);

            task.Status = TransferStatus.Running;
            Assert.True(task.IsRunning);
            Assert.Contains("Transferring", task.StatusBadge);

            task.Status = TransferStatus.Completed;
            Assert.False(task.IsRunning);
            Assert.Contains("Completed", task.StatusBadge);
        }

        [Fact]
        public void TransferTask_ProgressPercentage_ShouldNotifyProgressSummary()
        {
            var task = new TransferTask
            {
                FileName = "demo.mp4",
                ProgressPercentage = 0
            };

            Assert.Equal("0%", task.ProgressSummary);

            var notifiedProperties = new List<string>();
            task.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) notifiedProperties.Add(e.PropertyName);
            };

            task.ProgressPercentage = 35;
            Assert.Contains(nameof(TransferTask.ProgressPercentage), notifiedProperties);
            Assert.Contains(nameof(TransferTask.ProgressSummary), notifiedProperties);
            Assert.Equal("35%", task.ProgressSummary);

            notifiedProperties.Clear();
            task.ProgressPercentage = 100;
            Assert.Contains(nameof(TransferTask.ProgressSummary), notifiedProperties);
            Assert.Equal("100%", task.ProgressSummary);
        }

        [Fact]
        public void DirectoryTransferTask_ProgressSummary_ShouldFormatFilesAndPercentage()
        {
            var dirTask = new TransferTask
            {
                FileName = "myfolder",
                IsDirectory = true,
                TotalItemsCount = 10,
                CompletedItemsCount = 3,
                ProgressPercentage = 30
            };

            Assert.Equal("3/10 files (30%)", dirTask.ProgressSummary);

            var notifiedProperties = new List<string>();
            dirTask.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) notifiedProperties.Add(e.PropertyName);
            };

            dirTask.ProgressPercentage = 40;
            Assert.Contains(nameof(TransferTask.ProgressSummary), notifiedProperties);
            Assert.Equal("3/10 files (40%)", dirTask.ProgressSummary);

            dirTask.CompletedItemsCount = 4;
            Assert.Equal("4/10 files (40%)", dirTask.ProgressSummary);
        }

        [Theory]
        [InlineData(23, "rsync: [receiver] mkstemp: Permission denied (13)", RsyncFailureReason.PermissionDenied)]
        [InlineData(1, "Operation not permitted on destination", RsyncFailureReason.PermissionDenied)]
        [InlineData(255, "Permission denied (publickey,password)", RsyncFailureReason.PermissionDenied)]
        [InlineData(255, "ssh: connect to host 127.0.0.1 port 22: Connection refused", RsyncFailureReason.ConnectionError)]
        [InlineData(12, "rsync: error in rsync protocol data stream (code 12)", RsyncFailureReason.ConnectionError)]
        [InlineData(30, "Timeout in data send/receive (code 30)", RsyncFailureReason.ConnectionError)]
        [InlineData(255, "Connection reset by peer", RsyncFailureReason.ConnectionError)]
        [InlineData(23, "rsync: failed to set times on \"/var/www/file.txt\": Operation not permitted (1)", RsyncFailureReason.Other)]
        public void ClassifyError_ShouldDistinguishPermissionsAndConnectionErrors(int exitCode, string output, RsyncFailureReason expected)
        {
            var result = RsyncService.ClassifyError(exitCode, output);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("rsync: failed to set times on \"file.txt\": Operation not permitted (1)", true)]
        [InlineData("rsync: [receiver] failed to set times on \"/var/www/index.html\": Operation not permitted", true)]
        [InlineData("rsync: [receiver] mkstemp \"file.txt\": Permission denied (13)", false)]
        [InlineData("Connection refused", false)]
        [InlineData("", false)]
        public void IsFailedToSetTimesError_ShouldDetectTimestampWarningsCorrectly(string line, bool expected)
        {
            var result = RsyncService.IsFailedToSetTimesError(line);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void LogEntry_ShouldSupportWarningBrush()
        {
            var warningEntry = new LogEntry { Message = "Warning text", IsWarning = true };
            Assert.Equal("#D97706", warningEntry.ColorBrush);

            var errorEntry = new LogEntry { Message = "Error text", IsError = true };
            Assert.Equal("#C62828", errorEntry.ColorBrush);

            var normalEntry = new LogEntry { Message = "Normal text" };
            Assert.Equal("#2E7D32", normalEntry.ColorBrush);
        }

        [Fact]
        public void BuildRsyncArguments_Default_ShouldNotContainUpdateFlag()
        {
            var args = RsyncService.BuildRsyncArguments("ssh -p 22", "\"/source/file.txt\"", "\"/dest/\"", FileExistsAction.OverwriteIfDifferent);

            Assert.DoesNotContain("--update", args);
            Assert.DoesNotContain("--ignore-times", args);
            Assert.DoesNotContain("--checksum", args);
            Assert.StartsWith("-avzP -s --stats -e", args);
        }

        [Fact]
        public void BuildRsyncArguments_OverwriteIfNewer_ShouldContainUpdateFlag()
        {
            var args = RsyncService.BuildRsyncArguments("ssh -p 22", "\"/source/file.txt\"", "\"/dest/\"", FileExistsAction.OverwriteIfNewer);

            Assert.Contains("--update", args);
            Assert.DoesNotContain("--ignore-times", args);
        }

        [Fact]
        public void BuildRsyncArguments_OverwriteAlways_ShouldContainIgnoreTimesFlag()
        {
            var args = RsyncService.BuildRsyncArguments("ssh -p 22", "\"/source/file.txt\"", "\"/dest/\"", FileExistsAction.OverwriteAlways);

            Assert.Contains("--ignore-times", args);
            Assert.DoesNotContain("--update", args);
        }

        [Fact]
        public void BuildRsyncArguments_CompareChecksum_ShouldContainChecksumFlag()
        {
            var args = RsyncService.BuildRsyncArguments("ssh -p 22", "\"/source/file.txt\"", "\"/dest/\"", FileExistsAction.CompareChecksum);

            Assert.Contains("--checksum", args);
            Assert.DoesNotContain("--update", args);
        }

        [Fact]
        public void SettingsService_SaveAndLoad_ShouldPersistFileExistsAction()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"rsynczilla_settings_{Guid.NewGuid():N}.json");
            try
            {
                var settingsService = new SettingsService(tempFile);
                Assert.Equal(FileExistsAction.OverwriteIfDifferent, settingsService.Current.FileExistsAction);

                settingsService.SaveFileExistsAction(FileExistsAction.OverwriteAlways);

                var reloaded = new SettingsService(tempFile);
                Assert.Equal(FileExistsAction.OverwriteAlways, reloaded.Current.FileExistsAction);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void MainViewModel_SetFileExistsActionCommand_ShouldUpdateActionAndProperties()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"rsynczilla_vm_settings_{Guid.NewGuid():N}.json");
            try
            {
                var settingsService = new SettingsService(tempFile);
                var vm = new MainViewModel(settingsService: settingsService);

                Assert.True(vm.IsOverwriteIfDifferent);
                Assert.False(vm.IsOverwriteIfNewer);

                vm.SetFileExistsActionCommand.Execute(FileExistsAction.OverwriteIfNewer);

                Assert.False(vm.IsOverwriteIfDifferent);
                Assert.True(vm.IsOverwriteIfNewer);
                Assert.Equal(FileExistsAction.OverwriteIfNewer, vm.CurrentFileExistsAction);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void App_EnsureStandardMenuDropAlignment_ShouldForceLeftAlignment()
        {
            App.EnsureStandardMenuDropAlignment();
            Assert.False(System.Windows.SystemParameters.MenuDropAlignment);
        }
    }
}
