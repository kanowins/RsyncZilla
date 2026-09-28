using System;
using System.Text.Json.Serialization;

namespace RsyncZilla.Models
{
    public class SavedConnection
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string LastLocalPath { get; set; } = string.Empty;
        public string LastRemotePath { get; set; } = string.Empty;
        public DateTime LastUsed { get; set; } = DateTime.Now;

        [JsonIgnore]
        public bool HasSavedPassword { get; set; }

        [JsonIgnore]
        public string PasswordStatus => HasSavedPassword ? "Saved" : "—";

        public string DisplayName => string.IsNullOrWhiteSpace(Name) 
            ? $"{Username}@{Host}:{Port}" 
            : $"{Name} ({Username}@{Host})";
    }
}
