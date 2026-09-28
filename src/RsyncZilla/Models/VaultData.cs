using System;
using System.Collections.Generic;

namespace RsyncZilla.Models
{
    public class VaultEntry
    {
        public string? Password { get; set; }
    }

    public class VaultData
    {
        public int Version { get; set; } = 1;
        public Dictionary<Guid, VaultEntry> Entries { get; set; } = new();
    }
}
