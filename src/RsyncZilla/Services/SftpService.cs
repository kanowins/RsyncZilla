using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using RsyncZilla.Models;

namespace RsyncZilla.Services
{
    public class SftpService : IDisposable
    {
        private SftpClient? _client;
        private ConnectionInfo? _connectionInfo;
        private readonly System.Threading.SemaphoreSlim _connectionLock = new(1, 1);

        private string? _lastHost;
        private int _lastPort = 22;
        private string? _lastUsername;
        private string? _lastPassword;
        private bool _hasActiveSession;

        public bool HasActiveClient => _client != null && _client.IsConnected;
        public bool CanReconnect => _hasActiveSession && !string.IsNullOrWhiteSpace(_lastHost) && !string.IsNullOrWhiteSpace(_lastUsername);
        public bool IsConnected => _hasActiveSession || HasActiveClient;
        public string CurrentPath { get; private set; } = "/";
        public string? HomeDirectory { get; private set; }

        public event Action<string, bool>? LogMessageReceived; // (message, isError)
        public event Action? ConnectionStateChanged;

        public async Task<(bool success, string? error)> ConnectAsync(string host, int port, string username, string password)
        {
            await _connectionLock.WaitAsync();
            try
            {
                if (HasActiveClient && _connectionInfo != null &&
                    string.Equals(_connectionInfo.Host, host, StringComparison.OrdinalIgnoreCase) &&
                    _connectionInfo.Port == port &&
                    string.Equals(_connectionInfo.Username, username, StringComparison.Ordinal))
                {
                    _lastHost = host;
                    _lastPort = port;
                    _lastUsername = username;
                    _lastPassword = password;
                    _hasActiveSession = true;
                    return (true, null);
                }

                DisconnectInternal(clearSessionCredentials: false);

                return await Task.Run(() =>
                {
                    var (ok, err) = ConnectInternal(host, port, username, password, isReconnect: false);
                    if (ok)
                    {
                        _lastHost = host;
                        _lastPort = port;
                        _lastUsername = username;
                        _lastPassword = password;
                        _hasActiveSession = true;
                    }
                    else
                    {
                        _hasActiveSession = false;
                    }
                    ConnectionStateChanged?.Invoke();
                    return (ok, err);
                });
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private (bool success, string? error) ConnectInternal(string host, int port, string username, string password, bool isReconnect)
        {
            try
            {
                if (!isReconnect)
                {
                    LogMessageReceived?.Invoke($"Connecting to {username}@{host}:{port} via SFTP...", false);
                }

                var connectionInfo = new ConnectionInfo(
                    host,
                    port,
                    username,
                    new PasswordAuthenticationMethod(username, password)
                )
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };

                var client = new SftpClient(connectionInfo)
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(15),
                    OperationTimeout = TimeSpan.FromSeconds(15)
                };

                client.ErrorOccurred += (s, e) =>
                {
                    if (_hasActiveSession)
                    {
                        LogMessageReceived?.Invoke($"[SFTP] Idle connection dropped ({e.Exception?.Message ?? "socket closed"}). Will auto-reconnect on next operation.", false);
                    }
                };

                client.Connect();
                _client = client;
                _connectionInfo = connectionInfo;

                if (isReconnect && !string.IsNullOrWhiteSpace(CurrentPath) && CurrentPath != "/")
                {
                    try
                    {
                        _client.ChangeDirectory(CurrentPath);
                    }
                    catch
                    {
                        CurrentPath = _client.WorkingDirectory;
                    }
                }
                else
                {
                    CurrentPath = _client.WorkingDirectory;
                }

                if (string.IsNullOrEmpty(HomeDirectory))
                {
                    HomeDirectory = _client.WorkingDirectory;
                }

                if (isReconnect)
                {
                    LogMessageReceived?.Invoke($"Reconnected to {username}@{host}:{port}.", false);
                }
                else
                {
                    LogMessageReceived?.Invoke($"Connected successfully. Initial directory: {CurrentPath}", false);
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                var errorMsg = ex.InnerException != null
                    ? $"{ex.Message} ({ex.InnerException.Message})"
                    : ex.Message;
                LogMessageReceived?.Invoke($"SFTP connection error: {errorMsg}", true);
                DisposeClientOnly();
                return (false, errorMsg);
            }
        }

        private (bool success, string? error) EnsureConnectedInternal()
        {
            if (HasActiveClient)
            {
                return (true, null);
            }

            if (!CanReconnect)
            {
                return (false, "No active connection to SFTP server.");
            }

            DisposeClientOnly();
            LogMessageReceived?.Invoke($"Connection lost. Reconnecting to {_lastUsername}@{_lastHost}:{_lastPort}...", false);
            return ConnectInternal(_lastHost!, _lastPort, _lastUsername!, _lastPassword ?? "", isReconnect: true);
        }

        public void Disconnect()
        {
            _hasActiveSession = false;
            _lastHost = null;
            _lastUsername = null;
            _lastPassword = null;

            // Abort any in-flight socket operation immediately so Disconnect never blocks UI thread
            DisposeClientOnly(logDisconnect: true);

            if (_connectionLock.Wait(TimeSpan.FromMilliseconds(500)))
            {
                try
                {
                    DisconnectInternal(clearSessionCredentials: true);
                }
                finally
                {
                    _connectionLock.Release();
                }
            }
            ConnectionStateChanged?.Invoke();
        }

        private void DisposeClientOnly(bool logDisconnect = false)
        {
            var client = System.Threading.Interlocked.Exchange(ref _client, null);
            if (client != null)
            {
                try
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                    client.Dispose();
                }
                catch
                {
                    // Ignore errors during disconnect
                }
                finally
                {
                    if (logDisconnect)
                    {
                        LogMessageReceived?.Invoke("Disconnected from SFTP server.", false);
                    }
                }
            }
        }

        private void DisconnectInternal(bool clearSessionCredentials = true)
        {
            if (clearSessionCredentials)
            {
                _hasActiveSession = false;
                _lastHost = null;
                _lastUsername = null;
                _lastPassword = null;
                _connectionInfo = null;
                HomeDirectory = null;
            }
            DisposeClientOnly(logDisconnect: clearSessionCredentials);
        }

        private bool IsConnectionLost(Exception ex)
        {
            if (_client == null || !_client.IsConnected)
            {
                return true;
            }

            if (ex is SftpPermissionDeniedException || ex is SftpPathNotFoundException)
            {
                return false;
            }

            if (ex is SshConnectionException ||
                ex is SshOperationTimeoutException ||
                ex is SocketException ||
                ex is IOException ||
                ex is ObjectDisposedException)
            {
                return true;
            }

            if (ex is SshException sshEx &&
                (sshEx.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase) ||
                 sshEx.Message.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
                 sshEx.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                 sshEx.Message.Contains("aborted", StringComparison.OrdinalIgnoreCase) ||
                 sshEx.Message.Contains("reset", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (ex.InnerException != null)
            {
                return IsConnectionLost(ex.InnerException);
            }

            return false;
        }

        private async Task<T> ExecuteWithReconnectAsync<T>(
            Func<SftpClient, T> operation,
            Func<string, T> onError,
            string errorPrefix)
        {
            await _connectionLock.WaitAsync();
            try
            {
                return await Task.Run(() => ExecuteWithReconnectCore(operation, onError, errorPrefix));
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private T ExecuteWithReconnectSync<T>(
            Func<SftpClient, T> operation,
            Func<string, T> onError,
            string errorPrefix)
        {
            _connectionLock.Wait();
            try
            {
                return ExecuteWithReconnectCore(operation, onError, errorPrefix);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private T ExecuteWithReconnectCore<T>(
            Func<SftpClient, T> operation,
            Func<string, T> onError,
            string errorPrefix)
        {
            var (connected, connErr) = EnsureConnectedInternal();
            if (!connected || _client == null)
            {
                var msg = connErr ?? "No active connection to SFTP server.";
                LogMessageReceived?.Invoke($"{errorPrefix}: {msg}", true);
                return onError(msg);
            }

            try
            {
                return operation(_client);
            }
            catch (Exception ex) when (CanReconnect && IsConnectionLost(ex))
            {
                LogMessageReceived?.Invoke($"SFTP connection interrupted ({ex.Message}). Reconnecting...", false);
                DisposeClientOnly();

                var (reconnected, reconnErr) = EnsureConnectedInternal();
                if (!reconnected || _client == null)
                {
                    var msg = $"{errorPrefix}: {reconnErr ?? ex.Message}";
                    LogMessageReceived?.Invoke(msg, true);
                    return onError(msg);
                }

                try
                {
                    return operation(_client);
                }
                catch (Exception retryEx)
                {
                    var msg = $"{errorPrefix}: {retryEx.Message}";
                    LogMessageReceived?.Invoke(msg, true);
                    return onError(msg);
                }
            }
            catch (Exception ex)
            {
                var msg = $"{errorPrefix}: {ex.Message}";
                LogMessageReceived?.Invoke(msg, true);
                return onError(msg);
            }
        }

        public async Task<(List<FileItem> items, string? error)> GetDirectoryContentsAsync(string remotePath)
        {
            if (!IsConnected)
            {
                return (new List<FileItem>(), "No active connection to SFTP server.");
            }

            return await ExecuteWithReconnectAsync<(List<FileItem> items, string? error)>(
                client =>
                {
                    var results = new List<FileItem>();
                    var targetPath = remotePath;
                    if (string.IsNullOrWhiteSpace(targetPath))
                    {
                        targetPath = !string.IsNullOrWhiteSpace(CurrentPath) ? CurrentPath : client.WorkingDirectory;
                    }

                    client.ChangeDirectory(targetPath);
                    CurrentPath = client.WorkingDirectory;

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

                    var files = client.ListDirectory(CurrentPath);

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
                },
                err => (new List<FileItem>(), err),
                $"Error listing remote directory '{remotePath}'");
        }

        public async Task<bool> CreateDirectoryAsync(string path)
        {
            if (!IsConnected) return false;
            return await ExecuteWithReconnectAsync(
                client =>
                {
                    client.CreateDirectory(path);
                    LogMessageReceived?.Invoke($"Remote folder created: {path}", false);
                    return true;
                },
                _ => false,
                $"Error creating remote folder '{path}'");
        }

        public async Task<bool> DeleteItemsAsync(IEnumerable<(string path, bool isDirectory)> items)
        {
            if (!IsConnected) return false;
            var itemList = items.ToList();
            if (!itemList.Any()) return true;

            return await ExecuteWithReconnectAsync(
                client =>
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
                                using var cmd = sshClient.CreateCommand($"rm -rf -- {escapedPaths}");
                                cmd.CommandTimeout = TimeSpan.FromSeconds(15);
                                cmd.Execute();
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
                                DeleteDirectoryRecursive(client, item.path);
                                LogMessageReceived?.Invoke($"Remote folder deleted: {item.path}", false);
                            }
                            else
                            {
                                client.DeleteFile(item.path);
                                LogMessageReceived?.Invoke($"Remote file deleted: {item.path}", false);
                            }
                        }
                        catch (Exception ex) when (CanReconnect && IsConnectionLost(ex))
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            allSuccess = false;
                            LogMessageReceived?.Invoke($"Error deleting remote item '{item.path}': {ex.Message}", true);
                        }
                    }

                    return allSuccess;
                },
                _ => false,
                "Error deleting remote items");
        }

        public async Task<bool> DeleteItemAsync(string path, bool isDirectory)
        {
            return await DeleteItemsAsync(new[] { (path, isDirectory) });
        }

        public async Task<bool> RenameItemAsync(string oldPath, string newName)
        {
            if (!IsConnected) return false;
            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newName)) return false;

            var parent = GetParentDirectory(oldPath);
            var newPath = parent == "/" ? $"/{newName.Trim()}" : $"{parent}/{newName.Trim()}";
            if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) return true;

            return await ExecuteWithReconnectAsync(
                client =>
                {
                    client.RenameFile(oldPath, newPath);
                    LogMessageReceived?.Invoke($"Renamed remote item '{oldPath}' to '{newPath}'", false);
                    return true;
                },
                _ => false,
                $"Error renaming remote item '{oldPath}' to '{newPath}'");
        }

        public async Task<(bool success, string? error)> DownloadFileAsync(string remotePath, string localPath)
        {
            if (!IsConnected) return (false, "Not connected to SFTP server.");
            return await ExecuteWithReconnectAsync<(bool success, string? error)>(
                client =>
                {
                    var localDir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(localDir)) Directory.CreateDirectory(localDir);

                    using var fs = File.Create(localPath);
                    client.DownloadFile(remotePath, fs);
                    LogMessageReceived?.Invoke($"Downloaded '{remotePath}' to '{localPath}'", false);
                    return (true, null);
                },
                err => (false, err),
                $"Error downloading '{remotePath}'");
        }

        public async Task<(bool success, string? error)> UploadFileAsync(string localPath, string remotePath)
        {
            if (!IsConnected) return (false, "Not connected to SFTP server.");
            return await ExecuteWithReconnectAsync<(bool success, string? error)>(
                client =>
                {
                    using var fs = File.OpenRead(localPath);
                    client.UploadFile(fs, remotePath, true);
                    LogMessageReceived?.Invoke($"Uploaded '{localPath}' to '{remotePath}'", false);
                    return (true, null);
                },
                err => (false, err),
                $"Error uploading '{localPath}' to '{remotePath}'");
        }

        public async Task<(bool success, string content, string? error)> ReadAllTextAsync(string remotePath, int maxBytes = 262144)
        {
            if (!IsConnected) return (false, string.Empty, "Not connected to SFTP server.");
            return await ExecuteWithReconnectAsync<(bool success, string content, string? error)>(
                client =>
                {
                    using var ms = new MemoryStream();
                    client.DownloadFile(remotePath, ms);
                    var bytes = ms.ToArray();
                    if (bytes.Length > maxBytes)
                    {
                        var truncated = System.Text.Encoding.UTF8.GetString(bytes, 0, maxBytes) + $"\n\n[... Truncated at {maxBytes} bytes (total {bytes.Length} bytes) ...]";
                        return (true, truncated, null);
                    }
                    var text = System.Text.Encoding.UTF8.GetString(bytes);
                    LogMessageReceived?.Invoke($"Read remote file '{remotePath}' ({bytes.Length} bytes)", false);
                    return (true, text, null);
                },
                err => (false, string.Empty, err),
                $"Error reading remote file '{remotePath}'");
        }

        public async Task<(bool success, string? error)> WriteAllTextAsync(string remotePath, string content)
        {
            if (!IsConnected) return (false, "Not connected to SFTP server.");
            return await ExecuteWithReconnectAsync<(bool success, string? error)>(
                client =>
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(content ?? string.Empty);
                    using var ms = new MemoryStream(bytes);
                    client.UploadFile(ms, remotePath, true);
                    LogMessageReceived?.Invoke($"Wrote remote file '{remotePath}' ({bytes.Length} bytes)", false);
                    return (true, null);
                },
                err => (false, err),
                $"Error writing remote file '{remotePath}'");
        }

        public bool DownloadFileToStream(string remotePath, Stream output)
        {
            if (!IsConnected) return false;
            return ExecuteWithReconnectSync(
                client =>
                {
                    if (output.CanSeek) output.Position = 0;
                    client.DownloadFile(remotePath, output);
                    LogMessageReceived?.Invoke($"Streamed '{remotePath}' successfully", false);
                    return true;
                },
                _ => false,
                $"Error streaming '{remotePath}'");
        }

        public List<(string relativePath, string fullPath, long size, DateTime? modified)> GetFilesRecursive(string remotePath, string baseName)
        {
            if (!IsConnected) return new List<(string relativePath, string fullPath, long size, DateTime? modified)>();
            return ExecuteWithReconnectSync(
                client =>
                {
                    var list = new List<(string relativePath, string fullPath, long size, DateTime? modified)>();
                    CollectFilesRecursive(client, remotePath, baseName, list);
                    return list;
                },
                _ => new List<(string relativePath, string fullPath, long size, DateTime? modified)>(),
                $"Error enumerating files in '{remotePath}'");
        }

        private static void CollectFilesRecursive(SftpClient client, string currentRemotePath, string currentRelativePath, List<(string relativePath, string fullPath, long size, DateTime? modified)> list)
        {
            foreach (var item in client.ListDirectory(currentRemotePath))
            {
                if (item.Name == "." || item.Name == "..") continue;
                var itemRelative = string.IsNullOrEmpty(currentRelativePath) ? item.Name : $"{currentRelativePath}\\{item.Name}";
                if (item.IsDirectory)
                {
                    CollectFilesRecursive(client, item.FullName, itemRelative, list);
                }
                else
                {
                    list.Add((itemRelative, item.FullName, item.Length, item.LastWriteTimeUtc));
                }
            }
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
            _connectionLock.Dispose();
        }
    }
}
