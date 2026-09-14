using System;
using System.IO;
using RsyncZilla.Services;
using Xunit;

namespace RsyncZilla.Tests
{
    public class ConnectionManagerTests
    {
        [Fact]
        public void SaveOrUpdate_ShouldPersistConnectionWithoutPassword()
        {
            var service = new ConnectionManagerService();
            var testHost = "test-server.org";
            var testUser = "deployuser";
            var testPort = 2222;

            service.SaveOrUpdate(testHost, testUser, testPort);

            var list = service.LoadConnections();
            var found = list.Find(c => c.Host == testHost && c.Username == testUser && c.Port == testPort);

            Assert.NotNull(found);
            Assert.Equal(testHost, found.Host);
            Assert.Equal(testUser, found.Username);
            Assert.Equal(testPort, found.Port);

            // Clean up
            service.DeleteConnection(found.Id);
            var afterDelete = service.LoadConnections();
            Assert.DoesNotContain(afterDelete, c => c.Id == found.Id);
        }
    }
}
