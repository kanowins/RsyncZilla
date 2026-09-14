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
        public bool IsConnected => _client != null && _client.IsConnected;
        public string CurrentPath { get; private set; } = "/";

        public event Action<string, bool>? LogMessageReceived; // (message, isError)

        public async Task<bool> ConnectAsync(string host, int port, string username, string password)
        {
            Disconnect();

            return await Task.Run(() =>
            {
                try
                {
                    LogMessageReceived?.Invoke($"Conectando a {username}@{host}:{port} via SFTP...", false);
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

                    CurrentPath = _client.WorkingDirectory;
                    LogMessageReceived?.Invoke($"Conectado con éxito. Directorio inicial: {CurrentPath}", false);
                    return true;
                }
                catch (Exception ex)
                {
                    LogMessageReceived?.Invoke($"Error al conectar por SFTP: {ex.Message}", true);
                    Disconnect();
                    return false;
                }
            });
        }

        public void Disconnect()
        {
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
                    LogMessageReceived?.Invoke("Desconectado del servidor SFTP.", false);
                }
            }
        }

        public async Task<(List<FileItem> items, string? error)> GetDirectoryContentsAsync(string remotePath)
        {
            var results = new List<FileItem>();
            if (_client == null || !_client.IsConnected)
            {
                return (results, "No hay conexión activa con el servidor SFTP.");
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

                    LogMessageReceived?.Invoke($"Directorio remoto listado: {CurrentPath} ({results.Count} elementos)", false);
                    return (results, null);
                }
                catch (Exception ex)
                {
                    var msg = $"Error al listar directorio remoto '{remotePath}': {ex.Message}";
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
                    LogMessageReceived?.Invoke($"Carpeta remota creada: {path}", false);
                    return true;
                }
                catch (Exception ex)
                {
                    LogMessageReceived?.Invoke($"Error creando carpeta remota '{path}': {ex.Message}", true);
                    return false;
                }
            });
        }

        public async Task<bool> DeleteItemAsync(string path, bool isDirectory)
        {
            if (_client == null || !_client.IsConnected) return false;
            return await Task.Run(() =>
            {
                try
                {
                    if (isDirectory)
                    {
                        DeleteDirectoryRecursive(_client, path);
                        LogMessageReceived?.Invoke($"Carpeta remota eliminada: {path}", false);
                    }
                    else
                    {
                        _client.DeleteFile(path);
                        LogMessageReceived?.Invoke($"Archivo remoto eliminado: {path}", false);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    LogMessageReceived?.Invoke($"Error eliminando elemento remoto '{path}': {ex.Message}", true);
                    return false;
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
