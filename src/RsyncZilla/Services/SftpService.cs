using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using RsyncZilla.Models;

namespace RsyncZilla.Services
{
    public class SftpService : IDisposable
    {
        private SftpClient? _client;
        private ConnectionInfo? _connectionInfo;
        public bool IsConnected => _client != null && _client.IsConnected;
        public string CurrentPath { get; private set; } = "/";

        public event Action<string, bool>? LogMessageReceived; // (message, isError)

        public async Task<(bool success, string? error)> ConnectAsync(string host, int port, string username, string password)
        {
            Disconnect();

            return await Task.Run(() =>
            {
                try
                {
                    LogMessageReceived?.Invoke($"Connecting to {username}@{host}:{port} via SFTP...", false);
                    var connectionInfo = new ConnectionInfo(
                        host,
                        port,
                        username,
                        new PasswordAuthenticationMethod(username, password)
                    )
                    {
                        Timeout = TimeSpan.FromSeconds(15)
                    };

                    _client = new SftpClient(connectionInfo);
                    _client.Connect();
                    _connectionInfo = connectionInfo;

                    CurrentPath = _client.WorkingDirectory;
                    LogMessageReceived?.Invoke($"Connected successfully. Initial directory: {CurrentPath}", false);
                    return (true, (string?)null);
                }
                catch (Exception ex)
                {
                    var errorMsg = ex.InnerException != null 
                        ? $"{ex.Message} ({ex.InnerException.Message})" 
                        : ex.Message;
                    LogMessageReceived?.Invoke($"SFTP connection error: {errorMsg}", true);
                    Disconnect();
                    return (false, (string?)errorMsg);
                }
            });
        }

        public void Disconnect()
        {
            _connectionInfo = null;
            if (_client != null)
            {
                try
                {
                    if (_client.IsConnected)
                    {
                        _client.Disconnect();
                    }
                    _client.Dispose();
                }
                catch
                {
                    // Ignore errors during disconnect
                }
                finally
                {
                    _client = null;
                    LogMessageReceived?.Invoke("Disconnected from SFTP server.", false);
                }
            }
        }

        public async Task<(List<FileItem> items, string? error)> GetDirectoryContentsAsync(string remotePath)
        {
            var results = new List<FileItem>();
            if (_client == null || !_client.IsConnected)
            {
                return (results, "No active connection to SFTP server.");
            }

            return await Task.Run<(List<FileItem> items, string? error)>(() =>
            {
                try
                {
                    // Normalise path
                    if (string.IsNullOrWhiteSpace(remotePath))
                    {
                        remotePath = _client.WorkingDirectory;
                    }

                    _client.ChangeDirectory(remotePath);
                    CurrentPath = _client.WorkingDirectory;

                    // Add parent directory item '..' if not root
                    if (CurrentPath != "/" && !string.IsNullOrEmpty(CurrentPath))
                    {
                        var parentPath = GetParentDirectory(CurrentPath);
                        results.Add(new FileItem
                        {
                            Name = "..",
                            FullPath = parentPath,
                            IsDirectory = true,
                            IsParent = true,
                            LastWriteTime = DateTime.Now
                        });
                    }

                    var files = _client.ListDirectory(CurrentPath);

                    var dirItems = files
                        .Where(f => f.IsDirectory && f.Name != "." && f.Name != "..")
                        .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(f => new FileItem
                        {
                            Name = f.Name,
                            FullPath = f.FullName,
                            IsDirectory = true,
                            Length = 0,
                            LastWriteTime = f.LastWriteTime,
                            Permissions = FormatPermissions(f)
                        });

                    var regularFiles = files
                        .Where(f => !f.IsDirectory && f.Name != "." && f.Name != "..")
                        .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(f => new FileItem
                        {
                            Name = f.Name,
                            FullPath = f.FullName,
                            IsDirectory = false,
                            Length = f.Length,
                            LastWriteTime = f.LastWriteTime,
                            Permissions = FormatPermissions(f)
                        });

                    results.AddRange(dirItems);
                    results.AddRange(regularFiles);

                    LogMessageReceived?.Invoke($"Remote directory listed: {CurrentPath} ({results.Count} items)", false);
                    return (results, null);
                }
                catch (Exception ex)
                {
                    var msg = $"Error listing remote directory '{remotePath}': {ex.Message}";
                    LogMessageReceived?.Invoke(msg, true);
                    return (results, msg);
                }
            });
        }

        public async Task<bool> CreateDirectoryAsync(string path)
        {
            if (_client == null || !_client.IsConnected) return false;
            return await Task.Run(() =>
            {
                try
                {
                    _client.CreateDirectory(path);
                    LogMessageReceived?.Invoke($"Remote folder created: {path}", false);
                    return true;
                }
                catch (Exception ex)
                {
                    LogMessageReceived?.Invoke($"Error creating remote folder '{path}': {ex.Message}", true);
                    return false;
                }
            });
        }

        public async Task<bool> DeleteItemsAsync(IEnumerable<(string path, bool isDirectory)> items)
        {
            if (_client == null || !_client.IsConnected) return false;
            var itemList = items.ToList();
            if (!itemList.Any()) return true;

            return await Task.Run(() =>
            {
                // 1. Try fast batch deletion via SSH command (rm -rf)
                if (_connectionInfo != null)
                {
                    try
                    {
                        using var sshClient = new SshClient(_connectionInfo);
                        sshClient.Connect();
                        if (sshClient.IsConnected)
                        {
                            var escapedPaths = string.Join(" ", itemList.Select(i => $"'{i.path.Replace("'", "'\\''")}'"));
                            var cmd = sshClient.RunCommand($"rm -rf -- {escapedPaths}");
                            if (cmd.ExitStatus == 0)
                            {
                                LogMessageReceived?.Invoke($"[SSH] Successfully deleted {itemList.Count} items via rm -rf", false);
                                return true;
                            }
                            else if (!string.IsNullOrEmpty(cmd.Error))
                            {
                                LogMessageReceived?.Invoke($"[SSH] rm -rf note: {cmd.Error.Trim()}", false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessageReceived?.Invoke($"[SSH] Direct shell unavailable ({ex.Message}). Using SFTP batch deletion...", false);
                    }
                }

                // 2. Fallback: Batch delete directly over SFTP
                bool allSuccess = true;
                foreach (var item in itemList)
                {
                    try
                    {
                        if (item.isDirectory)
                        {
                            DeleteDirectoryRecursive(_client, item.path);
                            LogMessageReceived?.Invoke($"Remote folder deleted: {item.path}", false);
                        }
                        else
                        {
                            _client.DeleteFile(item.path);
                            LogMessageReceived?.Invoke($"Remote file deleted: {item.path}", false);
                        }
                    }
                    catch (Exception ex)
                    {
                        allSuccess = false;
                        LogMessageReceived?.Invoke($"Error deleting remote item '{item.path}': {ex.Message}", true);
                    }
                }

                return allSuccess;
            });
        }

        public async Task<bool> DeleteItemAsync(string path, bool isDirectory)
        {
            return await DeleteItemsAsync(new[] { (path, isDirectory) });
        }

        public async Task<bool> RenameItemAsync(string oldPath, string newName)
        {
            if (_client == null || !_client.IsConnected) return false;
            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newName)) return false;

            var parent = GetParentDirectory(oldPath);
            var newPath = parent == "/" ? $"/{newName.Trim()}" : $"{parent}/{newName.Trim()}";
            if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) return true;

            return await Task.Run(() =>
            {
                try
                {
                    _client.RenameFile(oldPath, newPath);
                    LogMessageReceived?.Invoke($"Renamed remote item '{oldPath}' to '{newPath}'", false);
                    return true;
                }
                catch (Exception ex)
                {
                    LogMessageReceived?.Invoke($"Error renaming remote item '{oldPath}' to '{newPath}': {ex.Message}", true);
                    return false;
                }
            });
        }

        public async Task<(bool success, string? error)> DownloadFileAsync(string remotePath, string localPath)
        {
            if (_client == null || !_client.IsConnected) return (false, "Not connected to SFTP server.");
            return await Task.Run<(bool success, string? error)>(() =>
            {
                try
                {
                    var localDir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(localDir)) Directory.CreateDirectory(localDir);

                    using var fs = File.Create(localPath);
                    _client.DownloadFile(remotePath, fs);
                    LogMessageReceived?.Invoke($"Downloaded '{remotePath}' to '{localPath}'", false);
                    return (true, null);
                }
                catch (Exception ex)
                {
                    var msg = $"Error downloading '{remotePath}': {ex.Message}";
                    LogMessageReceived?.Invoke(msg, true);
                    return (false, msg);
                }
            });
        }

        public async Task<(bool success, string? error)> UploadFileAsync(string localPath, string remotePath)
        {
            if (_client == null || !_client.IsConnected) return (false, "Not connected to SFTP server.");
            return await Task.Run<(bool success, string? error)>(() =>
            {
                try
                {
                    using var fs = File.OpenRead(localPath);
                    _client.UploadFile(fs, remotePath, true);
                    LogMessageReceived?.Invoke($"Uploaded '{localPath}' to '{remotePath}'", false);
                    return (true, null);
                }
                catch (Exception ex)
                {
                    var msg = $"Error uploading '{localPath}' to '{remotePath}': {ex.Message}";
                    LogMessageReceived?.Invoke(msg, true);
                    return (false, msg);
                }
            });
        }

        private static void DeleteDirectoryRecursive(SftpClient client, string path)
        {
            foreach (var item in client.ListDirectory(path))
            {
                if (item.Name == "." || item.Name == "..") continue;
                if (item.IsDirectory)
                {
                    DeleteDirectoryRecursive(client, item.FullName);
                }
                else
                {
                    client.DeleteFile(item.FullName);
                }
            }
            client.DeleteDirectory(path);
        }

        private static string GetParentDirectory(string path)
        {
            if (path == "/" || string.IsNullOrEmpty(path)) return "/";
            var trimmed = path.TrimEnd('/');
            var lastSlash = trimmed.LastIndexOf('/');
            if (lastSlash <= 0) return "/";
            return trimmed.Substring(0, lastSlash);
        }

        private static string FormatPermissions(ISftpFile file)
        {
            try
            {
                var p = file.OwnerCanRead ? "r" : "-";
                p += file.OwnerCanWrite ? "w" : "-";
                p += file.OwnerCanExecute ? "x" : "-";
                p += file.GroupCanRead ? "r" : "-";
                p += file.GroupCanWrite ? "w" : "-";
                p += file.GroupCanExecute ? "x" : "-";
                p += file.OthersCanRead ? "r" : "-";
                p += file.OthersCanWrite ? "w" : "-";
                p += file.OthersCanExecute ? "x" : "-";
                return (file.IsDirectory ? "d" : "-") + p;
            }
            catch
            {
                return file.IsDirectory ? "drwxr-xr-x" : "-rw-r--r--";
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
