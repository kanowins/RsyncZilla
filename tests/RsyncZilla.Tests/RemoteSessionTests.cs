using System;
using System.IO;
using System.Threading.Tasks;
using RsyncZilla.Models;
using RsyncZilla.ViewModels;
using Xunit;

namespace RsyncZilla.Tests
{
    public class RemoteSessionTests
    {
        [Fact]
        public void InitialState_ShouldHaveOneRemoteSessionSelected()
        {
            var vm = new MainViewModel();

            Assert.Single(vm.RemoteSessions);
            Assert.NotNull(vm.ActiveSession);
            Assert.Same(vm.RemoteSessions[0], vm.ActiveSession);
            Assert.False(vm.IsConnected);
            Assert.Equal("New Connection", vm.ActiveSession.Title);
        }

        [Fact]
        public void AddNewTab_ShouldAddAndSelectNewSession()
        {
            var vm = new MainViewModel();

            var newTab = vm.AddNewTab("test.server.com", "myuser", 2222);

            Assert.Equal(2, vm.RemoteSessions.Count);
            Assert.Same(newTab, vm.ActiveSession);
            Assert.Equal("test.server.com", vm.Host);
            Assert.Equal("myuser", vm.Username);
            Assert.Equal(2222, vm.Port);
            Assert.Equal("myuser@test.server.com", newTab.Title);
        }

        [Fact]
        public void CloseTab_WhenMultipleTabs_ShouldRemoveAndSelectAdjacent()
        {
            var vm = new MainViewModel();
            var tab1 = vm.ActiveSession;
            tab1.Host = "tab1.com";

            var tab2 = vm.AddNewTab("tab2.com", "user2");
            var tab3 = vm.AddNewTab("tab3.com", "user3");

            Assert.Equal(3, vm.RemoteSessions.Count);
            Assert.Same(tab3, vm.ActiveSession);

            // Close tab 3
            vm.CloseTab(tab3);

            Assert.Equal(2, vm.RemoteSessions.Count);
            Assert.Same(tab2, vm.ActiveSession);

            // Close tab 1 (non-active)
            vm.CloseTab(tab1);

            Assert.Single(vm.RemoteSessions);
            Assert.Same(tab2, vm.ActiveSession);
        }

        [Fact]
        public void CloseTab_WhenOnlyOneTab_ShouldResetInsteadOfDeleting()
        {
            var vm = new MainViewModel();
            vm.Host = "alone.server.com";
            vm.Username = "user";
            vm.Port = 2200;

            vm.CloseTab(vm.ActiveSession);

            Assert.Single(vm.RemoteSessions);
            Assert.NotNull(vm.ActiveSession);
            Assert.Equal("", vm.Host);
            Assert.Equal("", vm.Username);
            Assert.Equal(22, vm.Port);
            Assert.Equal("New Connection", vm.ActiveSession.Title);
        }

        [Fact]
        public void CloseTab_ShouldDisconnectSession()
        {
            var vm = new MainViewModel();
            var tab = vm.ActiveSession;
            tab.Host = "test.server.com";

            vm.CloseTab(tab);

            Assert.False(tab.IsConnected);
            Assert.Equal("Disconnected", tab.StatusText);
        }

        [Fact]
        public async Task SwitchToSession_ShouldSaveOutgoingLocalPathAndRestoreIncomingLocalPath()
        {
            var vm = new MainViewModel();
            var tab1 = vm.ActiveSession;
            tab1.Host = "host1.com";
            tab1.Username = "user1";

            // Set local browser to C:\
            await vm.LocalBrowser.NavigateToAsync(@"C:\");
            Assert.Equal(@"C:\", vm.LocalBrowser.CurrentPath);

            // Create and switch to tab 2
            var tab2 = vm.AddNewTab("host2.com", "user2");

            // Verify tab 1 saved C:\
            Assert.Equal(@"C:\", tab1.LastLocalPath);

            // In tab 2, navigate to "This PC"
            await vm.LocalBrowser.NavigateToAsync("This PC");
            Assert.Equal("This PC", vm.LocalBrowser.CurrentPath);

            // Switch back to tab 1
            await vm.SwitchToSessionAsync(tab1);

            // Verify tab 2 saved "This PC"
            Assert.Equal("This PC", tab2.LastLocalPath);

            // Verify local browser was restored to tab 1's saved path (C:\)
            Assert.Equal(@"C:\", vm.LocalBrowser.CurrentPath);
            Assert.Equal("host1.com", vm.Host);
            Assert.Equal("user1", vm.Username);
        }

        [Fact]
        public void EnqueueTransfers_ShouldAttachActiveSessionProfile()
        {
            var vm = new MainViewModel();
            vm.Host = "remote.host.org";
            vm.Username = "sftpuser";
            vm.Port = 2222;

            var task = new TransferTask
            {
                FileName = "test.txt",
                SourcePath = @"C:\test.txt",
                DestinationPath = "/home/sftpuser"
            };

            vm.EnqueueTransfers(new[] { task });

            Assert.NotNull(task.ConnectionProfile);
            Assert.Equal("remote.host.org", task.ConnectionProfile.Host);
            Assert.Equal("sftpuser", task.ConnectionProfile.Username);
            Assert.Equal(2222, task.ConnectionProfile.Port);
            Assert.Equal(vm.ActiveSession.Id, task.SessionId);
        }

        [Fact]
        public void SessionTitle_ShouldAlwaysDisplayUserAtHost()
        {
            var session = new RemoteSessionViewModel();
            Assert.Equal("New Connection", session.Title);

            session.Host = "sumalab.com";
            Assert.Equal("sumalab.com", session.Title);

            session.Username = "debian";
            Assert.Equal("debian@sumalab.com", session.Title);

            session.SiteName = "sumalab.com";
            Assert.Equal("debian@sumalab.com", session.Title);
        }

        [Fact]
        public void Disconnect_ShouldResetTabTitleToNewConnection()
        {
            var session = new RemoteSessionViewModel
            {
                Host = "myserver.com",
                Username = "admin"
            };

            Assert.Equal("admin@myserver.com", session.Title);

            // Disconnect should reset tab title
            session.Disconnect();
            Assert.True(session.IsTabNameReset);
            Assert.Equal("New Connection", session.Title);

            // Updating host or username should clear reset flag and recompute title
            session.Host = "other.com";
            Assert.False(session.IsTabNameReset);
            Assert.Equal("admin@other.com", session.Title);
        }
    }
}
