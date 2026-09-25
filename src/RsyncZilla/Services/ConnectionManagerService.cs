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

        public ConnectionManagerService(string? customFilePath = null)
        {
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
                _filePath = Path.Combine(folder, "connections.json");
            }
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

        public SavedConnection? FindConnection(string host, string username, int port)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username)) return null;

            var list = LoadConnections();
            return list.FirstOrDefault(c => 
                c.Host.Equals(host.Trim(), StringComparison.OrdinalIgnoreCase) &&
                c.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase) &&
                c.Port == port);
        }

        public void SaveOrUpdate(string host, string username, int port, string? customName = null, string? localPath = null, string? remotePath = null)
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
                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    existing.LastLocalPath = localPath;
                }
                if (!string.IsNullOrWhiteSpace(remotePath))
                {
                    existing.LastRemotePath = remotePath;
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
                    LastLocalPath = localPath ?? string.Empty,
                    LastRemotePath = remotePath ?? string.Empty,
                    LastUsed = DateTime.Now
                });
            }

            SaveToFile(list);
        }

        public void UpdatePaths(string host, string username, int port, string? localPath, string? remotePath)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username)) return;

            var list = LoadConnections();
            var existing = list.FirstOrDefault(c => 
                c.Host.Equals(host.Trim(), StringComparison.OrdinalIgnoreCase) &&
                c.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase) &&
                c.Port == port);

            if (existing != null)
            {
                bool changed = false;
                if (!string.IsNullOrWhiteSpace(localPath) && existing.LastLocalPath != localPath)
                {
                    existing.LastLocalPath = localPath;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(remotePath) && existing.LastRemotePath != remotePath)
                {
                    existing.LastRemotePath = remotePath;
                    changed = true;
                }

                if (changed)
                {
                    existing.LastUsed = DateTime.Now;
                    SaveToFile(list);
                }
            }
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

        public int ImportConnections(IEnumerable<SavedConnection> connectionsToImport, bool overwriteExisting = true)
        {
            var list = LoadConnections();
            int importedCount = 0;

            foreach (var conn in connectionsToImport)
            {
                if (string.IsNullOrWhiteSpace(conn.Host) || string.IsNullOrWhiteSpace(conn.Username))
                    continue;

                var existing = list.FirstOrDefault(c => 
                    c.Host.Equals(conn.Host.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    c.Username.Equals(conn.Username.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    c.Port == conn.Port);

                if (existing != null)
                {
                    if (overwriteExisting)
                    {
                        if (!string.IsNullOrWhiteSpace(conn.Name))
                            existing.Name = conn.Name.Trim();
                        if (!string.IsNullOrWhiteSpace(conn.LastLocalPath))
                            existing.LastLocalPath = conn.LastLocalPath;
                        if (!string.IsNullOrWhiteSpace(conn.LastRemotePath))
                            existing.LastRemotePath = conn.LastRemotePath;
                        existing.LastUsed = DateTime.Now;
                        importedCount++;
                    }
                }
                else
                {
                    list.Add(new SavedConnection
                    {
                        Id = Guid.NewGuid(),
                        Host = conn.Host.Trim(),
                        Username = conn.Username.Trim(),
                        Port = conn.Port,
                        Name = !string.IsNullOrWhiteSpace(conn.Name) ? conn.Name.Trim() : $"{conn.Username.Trim()}@{conn.Host.Trim()}",
                        LastLocalPath = conn.LastLocalPath ?? string.Empty,
                        LastRemotePath = conn.LastRemotePath ?? string.Empty,
                        LastUsed = DateTime.Now
                    });
                    importedCount++;
                }
            }

            if (importedCount > 0)
            {
                SaveToFile(list);
            }

            return importedCount;
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
