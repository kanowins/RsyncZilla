using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using RsyncZilla.Models;
using RsyncZilla.ViewModels;

namespace RsyncZilla.Services
{
    public class RemoteEditService : IDisposable
    {
        private class TrackedFile : IDisposable
        {
            public string LocalPath { get; set; } = "";
            public string RemotePath { get; set; } = "";
            public RemoteSessionViewModel Session { get; set; } = null!;
            public byte[] LastHash { get; set; } = Array.Empty<byte>();
            public FileSystemWatcher? Watcher { get; set; }
            public Timer? DebounceTimer { get; set; }
            public bool IsUploading { get; set; }

            public void Dispose()
            {
                if (Watcher != null)
                {
                    Watcher.EnableRaisingEvents = false;
                    Watcher.Dispose();
                    Watcher = null;
                }
                if (DebounceTimer != null)
                {
                    DebounceTimer.Dispose();
                    DebounceTimer = null;
                }
            }
        }

        private readonly ConcurrentDictionary<string, TrackedFile> _trackedFiles = new(StringComparer.OrdinalIgnoreCase);

        public event Action<string, bool>? LogMessageReceived;
        public event Action<RemoteSessionViewModel, string, long, DateTime>? FileUploaded;

        public string GetLocalTempPath(string host, int port, string username, string remoteFullPath)
        {
            var cleanHost = $"{username}@{host}_{port}".Replace(':', '_').Replace('/', '_').Replace('\\', '_');
            var cleanRelative = remoteFullPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

            var tempBase = Path.Combine(Path.GetTempPath(), "RsyncZilla", "RemoteEdit", cleanHost);
            return Path.Combine(tempBase, cleanRelative);
        }

        public async Task<(bool success, string? localPath, string? error)> OpenFileForEditingAsync(RemoteSessionViewModel session, FileItem item)
        {
            if (item == null || item.IsDirectory || item.IsParent)
            {
                return (false, null, "Cannot edit a directory or parent item.");
            }

            if (!session.IsConnected)
            {
                return (false, null, "SFTP session is not connected.");
            }

            var localPath = GetLocalTempPath(session.Host, session.Port, session.Username, item.FullPath);

            LogMessageReceived?.Invoke($"[Remote Edit] Downloading '{item.Name}' for editing...", false);

            var (ok, err) = await session.SftpService.DownloadFileAsync(item.FullPath, localPath);
            if (!ok)
            {
                var msg = $"Failed to download '{item.Name}' for editing: {err}";
                LogMessageReceived?.Invoke($"[Remote Edit] {msg}", true);
                return (false, null, msg);
            }

            var hash = ComputeFileHash(localPath);

            // Register or update watcher
            RegisterFileWatcher(localPath, item.FullPath, session, hash);

            // Open in default editor (or notepad if no association)
            OpenInEditor(localPath);

            LogMessageReceived?.Invoke($"[Remote Edit] Opened '{item.Name}' in local editor.", false);
            return (true, localPath, null);
        }

        private void RegisterFileWatcher(string localPath, string remotePath, RemoteSessionViewModel session, byte[] initialHash)
        {
            // Remove previous tracker if exists
            if (_trackedFiles.TryRemove(localPath, out var oldTracker))
            {
                oldTracker.Dispose();
            }

            var tracker = new TrackedFile
            {
                LocalPath = localPath,
                RemotePath = remotePath,
                Session = session,
                LastHash = initialHash
            };

            var dir = Path.GetDirectoryName(localPath);
            var file = Path.GetFileName(localPath);

            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                var watcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                FileSystemEventHandler handler = (s, e) =>
                {
                    OnTrackedFileChanged(tracker);
                };

                watcher.Changed += handler;
                watcher.Created += handler;
                tracker.Watcher = watcher;
            }

            _trackedFiles[localPath] = tracker;
        }

        private void OnTrackedFileChanged(TrackedFile tracker)
        {
            // Debounce: reset timer to 450ms
            if (tracker.DebounceTimer == null)
            {
                tracker.DebounceTimer = new Timer(async _ => await ProcessFileChangeAsync(tracker), null, 450, Timeout.Infinite);
            }
            else
            {
                tracker.DebounceTimer.Change(450, Timeout.Infinite);
            }
        }

        private async Task ProcessFileChangeAsync(TrackedFile tracker)
        {
            if (tracker.IsUploading) return;
            tracker.IsUploading = true;

            try
            {
                // Wait briefly if file is locked by the editor saving
                if (!WaitForFileReady(tracker.LocalPath, TimeSpan.FromSeconds(2)))
                {
                    return;
                }

                var currentHash = ComputeFileHash(tracker.LocalPath);
                if (currentHash.Length == 0 || AreHashesEqual(tracker.LastHash, currentHash))
                {
                    // No actual content change
                    return;
                }

                tracker.LastHash = currentHash;

                var fileName = Path.GetFileName(tracker.LocalPath);
                LogMessageReceived?.Invoke($"[Remote Edit] File '{fileName}' changed locally. Uploading to server...", false);

                var (ok, err) = await tracker.Session.SftpService.UploadFileAsync(tracker.LocalPath, tracker.RemotePath);
                if (ok)
                {
                    var fi = new FileInfo(tracker.LocalPath);
                    LogMessageReceived?.Invoke($"[Remote Edit] File '{fileName}' uploaded successfully ({FormatBytes(fi.Length)}).", false);
                    FileUploaded?.Invoke(tracker.Session, tracker.RemotePath, fi.Length, fi.LastWriteTime);
                }
                else
                {
                    LogMessageReceived?.Invoke($"[Remote Edit] Error uploading '{fileName}': {err}", true);
                }
            }
            catch (Exception ex)
            {
                LogMessageReceived?.Invoke($"[Remote Edit] Error processing saved file: {ex.Message}", true);
            }
            finally
            {
                tracker.IsUploading = false;
            }
        }

        public static void OpenInEditor(string filePath)
        {
            try
            {
                // Attempt opening with system associated application
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch
            {
                // Fallback to notepad.exe if no associated application exists
                var psi = new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
        }

        private static bool WaitForFileReady(string filePath, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                try
                {
                    using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (stream.Length >= 0) return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(80);
                }
            }
            return false;
        }

        private static byte[] ComputeFileHash(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return Array.Empty<byte>();
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sha = SHA256.Create();
                return sha.ComputeHash(stream);
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private static bool AreHashesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public void StopTrackingSession(RemoteSessionViewModel session)
        {
            foreach (var kvp in _trackedFiles)
            {
                if (kvp.Value.Session == session)
                {
                    if (_trackedFiles.TryRemove(kvp.Key, out var tracked))
                    {
                        tracked.Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            foreach (var kvp in _trackedFiles)
            {
                kvp.Value.Dispose();
            }
            _trackedFiles.Clear();
        }
    }
}
