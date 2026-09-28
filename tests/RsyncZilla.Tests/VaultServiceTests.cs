using System;
using System.IO;
using RsyncZilla.Services;
using Xunit;

namespace RsyncZilla.Tests
{
    public class VaultServiceTests : IDisposable
    {
        private readonly string _tempFile;

        public VaultServiceTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"vault_test_{Guid.NewGuid():N}.dat");
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_tempFile)) File.Delete(_tempFile);
                if (File.Exists(_tempFile + ".tmp")) File.Delete(_tempFile + ".tmp");
            }
            catch { }
        }

        [Fact]
        public void Setup_And_Unlock_WithCorrectPassword_Succeeds()
        {
            var service = new VaultService(null, _tempFile, iterations: 1000);
            Assert.False(service.IsConfigured);
            Assert.False(service.IsUnlocked);

            bool setupSuccess = service.Setup("MasterKey123!");
            Assert.True(setupSuccess);
            Assert.True(service.IsConfigured);
            Assert.True(service.IsUnlocked);

            // Lock and unlock again
            service.Lock();
            Assert.False(service.IsUnlocked);

            bool unlockSuccess = service.Unlock("MasterKey123!");
            Assert.True(unlockSuccess);
            Assert.True(service.IsUnlocked);
        }

        [Fact]
        public void Unlock_WithIncorrectPassword_Fails()
        {
            var service = new VaultService(null, _tempFile, iterations: 1000);
            service.Setup("CorrectPassword");
            service.Lock();

            bool unlockSuccess = service.Unlock("WrongPassword");
            Assert.False(unlockSuccess);
            Assert.False(service.IsUnlocked);
        }

        [Fact]
        public void SetPassword_And_GetPassword_PersistsAcrossUnlock()
        {
            var service = new VaultService(null, _tempFile, iterations: 1000);
            service.Setup("SecretKey");

            var siteId = Guid.NewGuid();
            service.SetPassword(siteId, "ServerPassword#456");

            Assert.Equal("ServerPassword#456", service.GetPassword(siteId));

            // Lock and reload from a new service instance
            service.Lock();
            var newService = new VaultService(null, _tempFile, iterations: 1000);
            Assert.Null(newService.GetPassword(siteId)); // Not unlocked yet

            bool unlocked = newService.Unlock("SecretKey");
            Assert.True(unlocked);
            Assert.Equal("ServerPassword#456", newService.GetPassword(siteId));
        }

        [Fact]
        public void RemovePassword_RemovesEntry()
        {
            var service = new VaultService(null, _tempFile, iterations: 1000);
            service.Setup("SecretKey");

            var siteId = Guid.NewGuid();
            service.SetPassword(siteId, "P@ss1");
            Assert.Equal("P@ss1", service.GetPassword(siteId));

            service.RemovePassword(siteId);
            Assert.Null(service.GetPassword(siteId));

            // Reload to verify persistence
            service.Lock();
            service.Unlock("SecretKey");
            Assert.Null(service.GetPassword(siteId));
        }

        [Fact]
        public void ChangeMasterPassword_ReEncryptsAndOldPasswordFails()
        {
            var service = new VaultService(null, _tempFile, iterations: 1000);
            service.Setup("OldPass123");

            var siteId = Guid.NewGuid();
            service.SetPassword(siteId, "SitePass");

            bool changed = service.ChangeMasterPassword("OldPass123", "NewPass456");
            Assert.True(changed);

            // Lock
            service.Lock();

            // Old password should fail
            Assert.False(service.Unlock("OldPass123"));

            // New password should succeed and retain data
            Assert.True(service.Unlock("NewPass456"));
            Assert.Equal("SitePass", service.GetPassword(siteId));
        }

        [Fact]
        public void DeleteVault_RemovesFileAndResetsState()
        {
            var service = new VaultService(null, _tempFile, iterations: 1000);
            service.Setup("MyKey");
            Assert.True(File.Exists(_tempFile));

            service.DeleteVault();

            Assert.False(File.Exists(_tempFile));
            Assert.False(service.IsConfigured);
            Assert.False(service.IsUnlocked);
        }

        [Fact]
        public void TamperedVault_FailsGracefully()
        {
            var service = new VaultService(null, _tempFile, iterations: 1000);
            service.Setup("MyKey");
            service.Lock();

            // Tamper with file bytes
            byte[] bytes = File.ReadAllBytes(_tempFile);
            bytes[^1] ^= 0xFF; // Flip bits in ciphertext / tag
            File.WriteAllBytes(_tempFile, bytes);

            Assert.False(service.Unlock("MyKey"));
            Assert.False(service.IsUnlocked);
        }

        [Fact]
        public void IsEnabled_WhenFalse_DoesNotExposeOrSavePasswords()
        {
            var service = new VaultService(null, _tempFile, iterations: 1000);
            service.Setup("MyKey");
            var siteId = Guid.NewGuid();
            service.SetPassword(siteId, "Secret123");
            Assert.Equal("Secret123", service.GetPassword(siteId));

            // Disable vault
            service.IsEnabled = false;
            Assert.Null(service.GetPassword(siteId));

            // Attempting to set password while disabled is ignored
            var siteId2 = Guid.NewGuid();
            service.SetPassword(siteId2, "AnotherPass");
            Assert.Null(service.GetPassword(siteId2));

            // Re-enable vault
            service.IsEnabled = true;
            Assert.Equal("Secret123", service.GetPassword(siteId));
            Assert.Null(service.GetPassword(siteId2));
        }

        [Fact]
        public void EdgeCases_EmptyPasswordsAndInvalidInputs()
        {
            var service = new VaultService(null, _tempFile, iterations: 1000);
            Assert.False(service.Setup(""));
            Assert.False(service.Unlock(""));
            Assert.False(service.Unlock("NonExistentPass"));

            service.Setup("ValidPass");
            Assert.False(service.ChangeMasterPassword("WrongCurrent", "NewPass"));
            Assert.False(service.ChangeMasterPassword("ValidPass", ""));
        }
    }
}
