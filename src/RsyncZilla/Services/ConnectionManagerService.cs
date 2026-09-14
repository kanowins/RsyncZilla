using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RsyncZilla.Models;

namespace RsyncZilla.Services
{
    public class ConnectionManagerService
    {
        private readonly string _filePath;

        public ConnectionManagerService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "RsyncZilla");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "connections.json");
        }

        public List<SavedConnection> LoadConnections()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var list = JsonSerializer.Deserialize<List<SavedConnection>>(json);
                    return list?.OrderByDescending(c => c.LastUsed).ToList() ?? new List<SavedConnection>();
                }
            }
            catch { }

            return new List<SavedConnection>();
        }

        public void SaveOrUpdate(string host, string username, int port, string? customName = null)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username)) return;

            var list = LoadConnections();
            var existing = list.FirstOrDefault(c => 
                c.Host.Equals(host.Trim(), StringComparison.OrdinalIgnoreCase) &&
                c.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase) &&
                c.Port == port);

            if (existing != null)
            {
                existing.LastUsed = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(customName))
                {
                    existing.Name = customName.Trim();
                }
            }
            else
            {
                list.Add(new SavedConnection
                {
                    Host = host.Trim(),
                    Username = username.Trim(),
                    Port = port,
                    Name = customName?.Trim() ?? $"{username.Trim()}@{host.Trim()}",
                    LastUsed = DateTime.Now
                });
            }

            SaveToFile(list);
        }

        public void DeleteConnection(Guid id)
        {
            var list = LoadConnections();
            var toRemove = list.FirstOrDefault(c => c.Id == id);
            if (toRemove != null)
            {
                list.Remove(toRemove);
                SaveToFile(list);
            }
        }

        private void SaveToFile(List<SavedConnection> list)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(list, options);
                File.WriteAllText(_filePath, json);
            }
            catch { }
        }
    }
}
