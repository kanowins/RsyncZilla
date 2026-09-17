using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RsyncZilla.Models;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;
using Xunit;

namespace RsyncZilla.Tests
{
    public class BatchTransferTests
    {
        [Fact]
        public void DisplayPath_Upload_ShouldFormatRemoteDestinationWithUserAndHost()
        {
            var task = new TransferTask
            {
                FileName = "photo.jpg",
                SourcePath = @"C:\photos\photo.jpg",
                DestinationPath = "/var/www/uploads",
                Direction = TransferDirection.Upload,
                ConnectionProfile = new ConnectionProfile
                {
                    Host = "sumalab.com",
                    Username = "debian",
                    Port = 22
                }
            };

            Assert.Equal(@"C:\photos\photo.jpg", task.DisplaySource);
            Assert.Equal("debian@sumalab.com:/var/www/uploads", task.DisplayDestination);
        }

        [Fact]
        public void DisplayPath_Download_ShouldFormatRemoteSourceWithUserAndHost()
        {
            var task = new TransferTask
            {
                FileName = "backup.tar.gz",
                SourcePath = "/home/debian/backup.tar.gz",
                DestinationPath = @"C:\backups",
                Direction = TransferDirection.Download,
                ConnectionProfile = new ConnectionProfile
                {
                    Host = "storage.cloud.net",
                    Username = "admin",
                    Port = 2222
                }
            };

            Assert.Equal("admin@storage.cloud.net:2222:/home/debian/backup.tar.gz", task.DisplaySource);
            Assert.Equal(@"C:\backups", task.DisplayDestination);
        }

        [Fact]
        public void EnqueueTransfers_ShouldPreserveIndividualConnectionProfilesForMultipleServers()
        {
            var vm = new MainViewModel();

            var server1Profile = new ConnectionProfile
            {
                Host = "server1.com",
                Username = "user1",
                Port = 22
            };

            var server2Profile = new ConnectionProfile
            {
                Host = "server2.com",
                Username = "user2",
                Port = 2200
            };

            var task1 = new TransferTask
            {
                FileName = "file1.txt",
                SourcePath = @"C:\file1.txt",
                DestinationPath = "/data",
                Direction = TransferDirection.Upload,
                ConnectionProfile = server1Profile
            };

            var task2 = new TransferTask
            {
                FileName = "file2.txt",
                SourcePath = @"C:\file2.txt",
                DestinationPath = "/data",
                Direction = TransferDirection.Upload,
                ConnectionProfile = server2Profile
            };

            vm.EnqueueTransfers(new[] { task1, task2 });

            Assert.Equal(2, vm.ActiveTransfers.Count);
            Assert.Equal("user1@server1.com:/data", vm.ActiveTransfers[0].DisplayDestination);
            Assert.Equal("user2@server2.com:2200:/data", vm.ActiveTransfers[1].DisplayDestination);
        }

        [Fact]
        public void RetryAllFailed_ShouldPreserveOriginalServerCredentials()
        {
            var vm = new MainViewModel();

            var failedTask1 = new TransferTask
            {
                FileName = "f1.txt",
                SourcePath = @"C:\f1.txt",
                DestinationPath = "/remote1",
                Direction = TransferDirection.Upload,
                Status = TransferStatus.Failed,
                ErrorMessage = "Permission denied",
                ConnectionProfile = new ConnectionProfile { Host = "hostA.com", Username = "userA" }
            };

            var failedTask2 = new TransferTask
            {
                FileName = "f2.txt",
                SourcePath = @"C:\f2.txt",
                DestinationPath = "/remote2",
                Direction = TransferDirection.Upload,
                Status = TransferStatus.Failed,
                ErrorMessage = "Timeout",
                ConnectionProfile = new ConnectionProfile { Host = "hostB.com", Username = "userB" }
            };

            vm.FailedTransfers.Add(failedTask1);
            vm.FailedTransfers.Add(failedTask2);

            Assert.Equal(2, vm.FailedTransfers.Count);

            vm.RetryAllFailed();

            Assert.Empty(vm.FailedTransfers);
            Assert.Equal(2, vm.ActiveTransfers.Count);
            Assert.Equal("hostA.com", vm.ActiveTransfers[0].ConnectionProfile?.Host);
            Assert.Equal("hostB.com", vm.ActiveTransfers[1].ConnectionProfile?.Host);
            Assert.Equal("userA@hostA.com:/remote1", vm.ActiveTransfers[0].DisplayDestination);
            Assert.Equal("userB@hostB.com:/remote2", vm.ActiveTransfers[1].DisplayDestination);
        }

        [Fact]
        public void DirectoryTransferTask_PropertiesAndExpandability_ShouldWorkCorrectly()
        {
            var dirTask = new TransferTask
            {
                FileName = "my_project",
                SourcePath = @"C:\my_project",
                DestinationPath = "/var/www/my_project",
                Direction = TransferDirection.Upload,
                IsDirectory = true,
                TotalItemsCount = 3,
                CompletedItemsCount = 1,
                ProgressPercentage = 33
            };

            Assert.True(dirTask.IsExpandable); // Directory tasks are always expandable
            Assert.Equal("📁 my_project (1/3)", dirTask.DisplayName);
            Assert.Equal("1/3 files (33%)", dirTask.ProgressSummary);

            var child1 = new TransferTask { FileName = "index.html", Status = TransferStatus.Completed };
            var child2 = new TransferTask { FileName = "style.css", Status = TransferStatus.Running };
            var child3 = new TransferTask { FileName = "sub/app.js", Status = TransferStatus.Pending };

            dirTask.Children.Add(child1);
            dirTask.Children.Add(child2);
            dirTask.Children.Add(child3);

            Assert.True(dirTask.IsExpandable);
            Assert.Equal(3, dirTask.ChildrenCount);

            dirTask.CurrentSubFile = "style.css (50%)";
            dirTask.TransferredInfo = "10 KB/s";
            Assert.Equal("📄 style.css (50%) | 10 KB/s", dirTask.LiveStatusDetail);
        }

        [Fact]
        public async Task UploadPathsAsync_Directory_ShouldPreEnumerateChildren()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "rsynczilla_test_" + Guid.NewGuid().ToString("N"));
            var subDir = Path.Combine(tempDir, "assets");
            Directory.CreateDirectory(subDir);

            var file1 = Path.Combine(tempDir, "test1.txt");
            var file2 = Path.Combine(subDir, "test2.css");
            File.WriteAllText(file1, "hello");
            File.WriteAllText(file2, "body { color: red; }");

            try
            {
                var vm = new MainViewModel();
                vm.ActiveSession!.Host = "srv.com";
                vm.ActiveSession!.Username = "root";

                await vm.UploadPathsAsync(new[] { tempDir }, "/remote/target");

                Assert.Single(vm.ActiveTransfers);
                var dirTask = vm.ActiveTransfers[0];
                Assert.True(dirTask.IsDirectory);
                Assert.Equal(2, dirTask.Children.Count);
                Assert.Equal(2, dirTask.TotalItemsCount);
                Assert.True(dirTask.IsExpandable);

                var relNames = dirTask.Children.Select(c => c.FileName).OrderBy(n => n).ToList();
                Assert.Contains(relNames, n => n.Equals("test1.txt", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(relNames, n => n.Replace('\\', '/').Equals("assets/test2.css", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void RetrySelectedFailed_DirectoryTask_ShouldResetChildrenStatuses()
        {
            var vm = new MainViewModel();
            var dirTask = new TransferTask
            {
                FileName = "failed_folder",
                SourcePath = @"C:\failed_folder",
                DestinationPath = "/remote",
                Direction = TransferDirection.Upload,
                IsDirectory = true,
                Status = TransferStatus.Failed,
                ErrorMessage = "Some error",
                CompletedItemsCount = 2,
                TotalItemsCount = 2,
                ConnectionProfile = new ConnectionProfile { Host = "host.com", Username = "user" }
            };

            var child1 = new TransferTask
            {
                FileName = "f1.txt",
                Status = TransferStatus.Failed,
                ErrorMessage = "Permission denied",
                ProgressPercentage = 50
            };
            var child2 = new TransferTask
            {
                FileName = "f2.txt",
                Status = TransferStatus.Failed,
                ErrorMessage = "Timeout",
                ProgressPercentage = 80
            };

            dirTask.Children.Add(child1);
            dirTask.Children.Add(child2);
            vm.FailedTransfers.Add(dirTask);

            vm.RetrySelectedFailed(dirTask);

            Assert.Empty(vm.FailedTransfers);
            Assert.Single(vm.ActiveTransfers);

            var retriedDir = vm.ActiveTransfers[0];
            Assert.Contains(retriedDir.Status, new[] { TransferStatus.Pending, TransferStatus.Running });
            Assert.Equal(0, retriedDir.CompletedItemsCount);
            Assert.Empty(retriedDir.ErrorMessage);

            Assert.All(retriedDir.Children, c =>
            {
                Assert.Contains(c.Status, new[] { TransferStatus.Pending, TransferStatus.Running });
                Assert.Equal(0, c.ProgressPercentage);
                Assert.Empty(c.ErrorMessage);
            });
        }

        [Fact]
        public void DirectoryTask_IntegratedTree_ExpandCollapse_ShouldInsertAndRemoveChildren()
        {
            var vm = new MainViewModel();
            var dirTask = new TransferTask
            {
                FileName = "my_folder",
                SourcePath = @"C:\my_folder",
                DestinationPath = "/remote",
                Direction = TransferDirection.Upload,
                IsDirectory = true,
                Status = TransferStatus.Pending
            };

            var child1 = new TransferTask { FileName = "a.txt", Status = TransferStatus.Pending };
            var child2 = new TransferTask { FileName = "b.txt", Status = TransferStatus.Pending };
            dirTask.Children.Add(child1);
            dirTask.Children.Add(child2);

            vm.ActiveTransfers.Add(dirTask);
            Assert.Single(vm.ActiveTransfers);
            Assert.Contains("(1)", vm.ActiveTabHeader);

            // 1. Expand
            vm.ToggleDirectoryTask(dirTask, vm.ActiveTransfers);
            Assert.True(dirTask.IsExpanded);
            Assert.Equal(3, vm.ActiveTransfers.Count);
            Assert.Equal(dirTask, vm.ActiveTransfers[0]);
            Assert.Equal(child1, vm.ActiveTransfers[1]);
            Assert.Equal(child2, vm.ActiveTransfers[2]);
            Assert.True(vm.ActiveTransfers[1].IsChild);
            Assert.True(vm.ActiveTransfers[2].IsChild);
            Assert.Equal(dirTask, vm.ActiveTransfers[1].ParentTask);
            // Header count should still reflect 1 primary task
            Assert.Contains("(1)", vm.ActiveTabHeader);

            // 2. Collapse
            vm.ToggleDirectoryTask(dirTask, vm.ActiveTransfers);
            Assert.False(dirTask.IsExpanded);
            Assert.Single(vm.ActiveTransfers);
            Assert.Equal(dirTask, vm.ActiveTransfers[0]);
            Assert.Contains("(1)", vm.ActiveTabHeader);
        }
    }
}
