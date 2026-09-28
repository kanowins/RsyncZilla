using System;
using System.IO;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;
using Xunit;

namespace RsyncZilla.Tests
{
    public class TerminalTabTests
    {
        [Fact]
        public void RemoteSessionViewModel_IsTerminalTab_SetsProperTitle()
        {
            var session = new RemoteSessionViewModel
            {
                Host = "myserver.com",
                Username = "root",
                Port = 22,
                IsTerminalTab = true
            };

            Assert.True(session.IsTerminalTab);
            Assert.Equal("Terminal: root@myserver.com", session.Title);

            session.Username = "";
            Assert.Equal("Terminal: myserver.com", session.Title);

            session.Host = "";
            Assert.Equal("Terminal", session.Title);
        }

        [Fact]
        public void RemoteSessionViewModel_NonTerminalTab_KeepsOriginalTitleFormat()
        {
            var session = new RemoteSessionViewModel
            {
                Host = "myserver.com",
                Username = "developer",
                Port = 22,
                IsTerminalTab = false
            };

            Assert.False(session.IsTerminalTab);
            Assert.Equal("developer@myserver.com", session.Title);
        }

        [Fact]
        public void SshTerminalSession_DefaultValuesAndSafeDisconnectedMethods()
        {
            using var session = new SshTerminalSession("myserver.com", 0, "root", "secret", "/var/www");

            Assert.Equal("myserver.com", session.Host);
            Assert.Equal(22, session.Port);
            Assert.Equal("root", session.Username);
            Assert.Equal("/var/www", session.InitialPath);
            Assert.False(session.IsConnected);

            // Calling SendInput or ChangeSize when not connected should be completely safe and not throw
            session.SendInput("ls -la\n");
            session.ChangeSize(120, 40);
        }

        [Fact]
        public void MainViewModel_OpenTerminalTab_CreatesActiveTerminalSession()
        {
            var vm = new MainViewModel();
            var initialCount = vm.RemoteSessions.Count;

            var termSession = vm.OpenTerminalTab("test.server.com", 2222, "admin", "pwd123", "/etc/nginx", "My Site");

            Assert.NotNull(termSession);
            Assert.True(termSession.IsTerminalTab);
            Assert.Equal("test.server.com", termSession.Host);
            Assert.Equal(2222, termSession.Port);
            Assert.Equal("admin", termSession.Username);
            Assert.Equal("/etc/nginx", termSession.InitialTerminalPath);
            Assert.Equal("My Site", termSession.SiteName);
            Assert.Equal(initialCount + 1, vm.RemoteSessions.Count);
            Assert.Same(termSession, vm.ActiveSession);
            Assert.Equal("Terminal: admin@test.server.com", termSession.Title);

            // Now close the tab
            vm.CloseTab(termSession);
            Assert.Equal(initialCount, vm.RemoteSessions.Count);
            Assert.NotSame(termSession, vm.ActiveSession);
        }

        [Fact]
        public void MainViewModel_CloseTab_OnSingleRemainingTab_ResetsTerminalFlag()
        {
            var vm = new MainViewModel();
            Assert.Single(vm.RemoteSessions);

            var session = vm.RemoteSessions[0];
            session.IsTerminalTab = true;
            session.Host = "temp.com";
            session.Username = "user";

            vm.CloseTab(session);

            Assert.Single(vm.RemoteSessions);
            Assert.False(session.IsTerminalTab);
            Assert.Equal("Disconnected", session.StatusText);
            Assert.Equal("New Connection", session.Title);
        }
    }
}
