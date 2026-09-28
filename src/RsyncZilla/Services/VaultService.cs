using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RsyncZilla.Models;

namespace RsyncZilla.Services
{
    public class VaultService
    {
        private const string MagicHeader = "RZVLT";
        private const byte CurrentVersion = 1;
        private const int SaltSize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        public const int DefaultIterations = 100_000;

        private readonly string _filePath;
        private readonly SettingsService? _settingsService;
        private readonly int _iterations;

        private VaultData? _unlockedData;
        private string? _masterPassword;
        private bool _inMemoryEnabled = true;

        public bool IsConfigured => File.Exists(_filePath);
        public bool IsUnlocked => _unlockedData != null;

        public bool IsEnabled
        {
            get => _settingsService?.Current.VaultEnabled ?? _inMemoryEnabled;
            set
            {
                _inMemoryEnabled = value;
                _settingsService?.SaveVaultEnabled(value);
            }
        }

        public VaultService(SettingsService? settingsService = null, string? customFilePath = null, int iterations = DefaultIterations)
        {
            _settingsService = settingsService;
            _iterations = iterations;

            if (!string.IsNullOrWhiteSpace(customFilePath))
            {
                _filePath = customFilePath;
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var folder = Path.Combine(appData, "RsyncZilla");
                Directory.CreateDirectory(folder);
                _filePath = Path.Combine(folder, "vault.dat");
            }
        }

        public bool Setup(string masterPassword)
        {
            if (string.IsNullOrEmpty(masterPassword)) return false;

            _unlockedData = new VaultData();
            _masterPassword = masterPassword;
            IsEnabled = true;
            SaveVault();
            return true;
        }

        public bool Unlock(string masterPassword)
        {
            if (string.IsNullOrEmpty(masterPassword) || !File.Exists(_filePath)) return false;

            try
            {
                byte[] fileBytes = File.ReadAllBytes(_filePath);
                // Minimum size: 5 (magic) + 1 (version) + 4 (iterations) + 32 (salt) + 12 (nonce) + 16 (tag) = 70 bytes
                if (fileBytes.Length < 70) return false;

                int offset = 0;
                string magic = Encoding.ASCII.GetString(fileBytes, 0, 5);
                if (magic != MagicHeader) return false;
                offset += 5;

                byte version = fileBytes[offset++];
                if (version != CurrentVersion) return false;

                int iterations = BitConverter.ToInt32(fileBytes, offset);
                offset += 4;
                if (iterations <= 0) iterations = _iterations;

                byte[] salt = new byte[SaltSize];
                Array.Copy(fileBytes, offset, salt, 0, SaltSize);
                offset += SaltSize;

                byte[] nonce = new byte[NonceSize];
                Array.Copy(fileBytes, offset, nonce, 0, NonceSize);
                offset += NonceSize;

                byte[] tag = new byte[TagSize];
                Array.Copy(fileBytes, offset, tag, 0, TagSize);
                offset += TagSize;

                int ciphertextLength = fileBytes.Length - offset;
                byte[] ciphertext = new byte[ciphertextLength];
                Array.Copy(fileBytes, offset, ciphertext, 0, ciphertextLength);

                byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(masterPassword),
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    32);

                byte[] plaintext = new byte[ciphertextLength];
                using (var aes = new AesGcm(key, TagSize))
                {
                    aes.Decrypt(nonce, ciphertext, tag, plaintext);
                }

                var data = JsonSerializer.Deserialize<VaultData>(plaintext);
                _unlockedData = data ?? new VaultData();
                _masterPassword = masterPassword;
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        public void Lock()
        {
            _unlockedData = null;
            _masterPassword = null;
        }

        public bool ChangeMasterPassword(string currentPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword)) return false;

            if (!IsUnlocked)
            {
                if (!Unlock(currentPassword)) return false;
            }
            else
            {
                if (_masterPassword != currentPassword) return false;
            }

            _masterPassword = newPassword;
            SaveVault();
            return true;
        }

        public void DeleteVault()
        {
            Lock();
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch { }
        }

        public string? GetPassword(Guid siteId)
        {
            if (!IsEnabled || !IsUnlocked || _unlockedData == null) return null;

            if (_unlockedData.Entries.TryGetValue(siteId, out var entry))
            {
                return entry.Password;
            }

            return null;
        }

        public void SetPassword(Guid siteId, string password)
        {
            if (!IsEnabled || !IsUnlocked || _unlockedData == null) return;

            _unlockedData.Entries[siteId] = new VaultEntry { Password = password };
            SaveVault();
        }

        public void RemovePassword(Guid siteId)
        {
            if (!IsUnlocked || _unlockedData == null) return;

            if (_unlockedData.Entries.Remove(siteId))
            {
                SaveVault();
            }
        }

        private void SaveVault()
        {
            if (_unlockedData == null || string.IsNullOrEmpty(_masterPassword)) return;

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] tag = new byte[TagSize];

            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(_masterPassword),
                salt,
                _iterations,
                HashAlgorithmName.SHA256,
                32);

            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(_unlockedData);
            byte[] ciphertext = new byte[plaintext.Length];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            byte[] magicBytes = Encoding.ASCII.GetBytes(MagicHeader);
            byte[] iterBytes = BitConverter.GetBytes(_iterations);

            using var ms = new MemoryStream();
            ms.Write(magicBytes, 0, magicBytes.Length);
            ms.WriteByte(CurrentVersion);
            ms.Write(iterBytes, 0, iterBytes.Length);
            ms.Write(salt, 0, salt.Length);
            ms.Write(nonce, 0, nonce.Length);
            ms.Write(tag, 0, tag.Length);
            ms.Write(ciphertext, 0, ciphertext.Length);

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tempFile = _filePath + ".tmp";
            File.WriteAllBytes(tempFile, ms.ToArray());
            File.Move(tempFile, _filePath, overwrite: true);
        }
    }
}
