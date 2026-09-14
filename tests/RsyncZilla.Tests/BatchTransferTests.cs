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
    }
}
