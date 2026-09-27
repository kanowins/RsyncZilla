using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
        private readonly RsyncService _rsyncService;

        public event Action<string, bool>? LogMessageReceived;
        public event Action<RemoteSessionViewModel, string, long, DateTime>? FileUploaded;
        public event Action<RemoteSessionViewModel, string, string, string>? FileUploadFailed; // (session, remotePath, fileName, error)

        public Action<string>? EditorOpener { get; set; }
        public Action<string>? OpenWithOpener { get; set; }

        public RemoteEditService(RsyncService? rsyncService = null)
        {
            _rsyncService = rsyncService ?? new RsyncService();
        }

        public string GetLocalTempPath(string host, int port, string username, string remoteFullPath)
        {
            var cleanHost = $"{username}@{host}_{port}".Replace(':', '_').Replace('/', '_').Replace('\\', '_');
            var cleanRelative = remoteFullPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

            var tempBase = Path.Combine(Path.GetTempPath(), "RsyncZilla", "RemoteEdit", cleanHost);
            return Path.Combine(tempBase, cleanRelative);
        }

        public async Task<(bool success, string? localPath, string? error)> OpenFileForEditingAsync(RemoteSessionViewModel session, FileItem item, bool openWith = false)
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

            LogMessageReceived?.Invoke($"[Remote Edit] Downloading '{item.Name}' for {(openWith ? "opening" : "editing")}...", false);

            var (ok, err) = await session.SftpService.DownloadFileAsync(item.FullPath, localPath);
            if (!ok)
            {
                var msg = $"Failed to download '{item.Name}' for {(openWith ? "opening" : "editing")}: {err}";
                LogMessageReceived?.Invoke($"[Remote Edit] {msg}", true);
                return (false, null, msg);
            }

            var hash = ComputeFileHash(localPath);

            // Register or update watcher
            RegisterFileWatcher(localPath, item.FullPath, session, hash);

            if (openWith)
            {
                if (OpenWithOpener != null)
                {
                    OpenWithOpener(localPath);
                }
                else
                {
                    OpenWith(localPath);
                }
                LogMessageReceived?.Invoke($"[Remote Edit] Opened 'Open with' for '{item.Name}'.", false);
            }
            else
            {
                if (EditorOpener != null)
                {
                    EditorOpener(localPath);
                }
                else
                {
                    OpenInEditor(localPath);
                }
                LogMessageReceived?.Invoke($"[Remote Edit] Opened '{item.Name}' in local editor.", false);
            }

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

                var fileName = Path.GetFileName(tracker.LocalPath);

                if (tracker.Session == null || !tracker.Session.IsConnected)
                {
                    var errMsg = "SFTP session is not connected. Reconnect to the server and save again.";
                    LogMessageReceived?.Invoke($"[Remote Edit] Error uploading '{fileName}': {errMsg}", true);
                    FileUploadFailed?.Invoke(tracker.Session!, tracker.RemotePath, fileName, errMsg);
                    return;
                }

                LogMessageReceived?.Invoke($"[Remote Edit] File '{fileName}' changed locally. Uploading to server via rsync...", false);

                var connection = tracker.Session.CreateConnectionProfile();
                var remoteDir = tracker.RemotePath.Contains('/') 
                    ? tracker.RemotePath.Substring(0, tracker.RemotePath.LastIndexOf('/')) 
                    : "/";
                if (string.IsNullOrEmpty(remoteDir)) remoteDir = "/";

                var task = new TransferTask
                {
                    FileName = fileName,
                    SourcePath = tracker.LocalPath,
                    DestinationPath = remoteDir,
                    Direction = TransferDirection.Upload,
                    ConnectionProfile = connection,
                    SessionId = tracker.Session.Id
                };

                var success = await _rsyncService.ExecuteBatchTransferAsync(new[] { task }, connection);
                if (success && task.Status == TransferStatus.Completed)
                {
                    tracker.LastHash = currentHash;
                    var fi = new FileInfo(tracker.LocalPath);
                    LogMessageReceived?.Invoke($"[Remote Edit] File '{fileName}' uploaded and verified successfully via rsync ({FormatBytes(fi.Length)}).", false);
                    FileUploaded?.Invoke(tracker.Session, tracker.RemotePath, fi.Length, fi.LastWriteTime);
                }
                else
                {
                    var errMsg = !string.IsNullOrEmpty(task.ErrorMessage) ? task.ErrorMessage : "rsync upload failed.";
                    LogMessageReceived?.Invoke($"[Remote Edit] Error uploading '{fileName}': {errMsg}", true);
                    FileUploadFailed?.Invoke(tracker.Session, tracker.RemotePath, fileName, errMsg);
                }
            }
            catch (Exception ex)
            {
                var fileName = Path.GetFileName(tracker.LocalPath);
                LogMessageReceived?.Invoke($"[Remote Edit] Error processing saved file: {ex.Message}", true);
                FileUploadFailed?.Invoke(tracker.Session, tracker.RemotePath, fileName, ex.Message);
            }
            finally
            {
                tracker.IsUploading = false;
            }
        }

        public static Action<string>? CustomEditorAction { get; set; }
        public static Action<string>? CustomOpenWithAction { get; set; }

        public static void OpenInEditor(string filePath)
        {
            if (CustomEditorAction != null)
            {
                CustomEditorAction(filePath);
                return;
            }

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

        [DllImport("shell32.dll", EntryPoint = "SHOpenWithDialog", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHOpenWithDialog(IntPtr hWndParent, ref OPENASINFO oOAI);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENASINFO
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string cszFile;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? cszClass;

            public OPENASINFOFLAGS oaifInFlags;
        }

        [Flags]
        private enum OPENASINFOFLAGS
        {
            OAIF_ALLOW_REGISTRATION = 0x00000001,
            OAIF_REGISTER_EXT = 0x00000002,
            OAIF_EXEC = 0x00000004,
            OAIF_FORCE_REGISTRATION = 0x00000008,
            OAIF_HIDE_REGISTRATION = 0x00000020,
            OAIF_URL_PROTOCOL = 0x00000040,
            OAIF_FILE_IS_URI = 0x00000080
        }

        public static void OpenWith(string filePath)
        {
            if (CustomOpenWithAction != null)
            {
                CustomOpenWithAction(filePath);
                return;
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            IntPtr parentHwnd = IntPtr.Zero;
            try
            {
                var window = System.Windows.Application.Current?.MainWindow;
                if (window != null)
                {
                    parentHwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                }
            }
            catch { }

            var thread = new Thread(() =>
            {
                try
                {
                    var info = new OPENASINFO
                    {
                        cszFile = filePath,
                        cszClass = null,
                        oaifInFlags = OPENASINFOFLAGS.OAIF_ALLOW_REGISTRATION | OPENASINFOFLAGS.OAIF_EXEC
                    };

                    int hr = SHOpenWithDialog(parentHwnd, ref info);
                    // If SHOpenWithDialog failed with error (hr < 0 and not cancelled)
                    if (hr < 0 && hr != unchecked((int)0x800704C7) && hr != unchecked((int)0x80004004))
                    {
                        OpenInEditor(filePath);
                    }
                }
                catch
                {
                    OpenInEditor(filePath);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
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

        internal async Task<bool> TriggerProcessFileChangeAsync(string localPath)
        {
            if (_trackedFiles.TryGetValue(localPath, out var tracker))
            {
                await ProcessFileChangeAsync(tracker);
                return true;
            }
            return false;
        }

        internal void RegisterTestTracker(string localPath, string remotePath, RemoteSessionViewModel session, byte[] initialHash)
        {
            _trackedFiles[localPath] = new TrackedFile
            {
                LocalPath = localPath,
                RemotePath = remotePath,
                Session = session,
                LastHash = initialHash
            };
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
