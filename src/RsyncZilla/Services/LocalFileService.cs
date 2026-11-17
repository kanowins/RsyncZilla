using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using RsyncZilla.Models;

namespace RsyncZilla.Services
{
    public class LocalFileService
    {
        private static readonly Guid DownloadsKnownFolderGuid = new("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        public List<string> GetDrives()
        {
            try
            {
                return DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();
            }
            catch
            {
                return new List<string> { "C:\\" };
            }
        }

        public List<FileItem> GetDrivesItems()
        {
            var results = new List<FileItem>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        string label = "";
                        long totalSize = 0;
                        if (drive.IsReady)
                        {
                            label = !string.IsNullOrWhiteSpace(drive.VolumeLabel) ? $" ({drive.VolumeLabel})" : "";
                            totalSize = drive.TotalSize;
                        }

                        string driveType = drive.DriveType switch
                        {
                            DriveType.Fixed => "Local Disk",
                            DriveType.Removable => "Removable Disk",
                            DriveType.Network => "Network Drive",
                            DriveType.CDRom => "CD/DVD Drive",
                            _ => "Drive"
                        };

                        results.Add(new FileItem
                        {
                            Name = $"{drive.Name.TrimEnd('\\')}{label}",
                            FullPath = drive.RootDirectory.FullName,
                            IsDirectory = true,
                            IsDrive = true,
                            UseRealShellIcon = true,
                            CustomType = driveType,
                            Category = "Drives",
                            Length = totalSize,
                            LastWriteTime = DateTime.Now,
                            Permissions = driveType
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return results;
        }

        /// <summary>
        /// Discovers cloud & app folders registered in Windows Explorer's Navigation Pane at the "This PC" level
        /// (HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{CLSID}), such as Dropbox, OneDrive, iCloud, Google Drive, Nextcloud.
        /// </summary>
        public List<FileItem> GetCloudNamespaceItems(HashSet<string>? seenPaths = null)
        {
            seenPaths ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<FileItem>();

            try
            {
                using var nsKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace");
                if (nsKey != null)
                {
                    foreach (var clsid in nsKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = nsKey.OpenSubKey(clsid);
                            var nsDefaultName = subKey?.GetValue(null)?.ToString();

                            using var clsidKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\CLSID\{clsid}")
                                              ?? Registry.LocalMachine.OpenSubKey($@"Software\Classes\CLSID\{clsid}");
                            if (clsidKey == null) continue;

                            var pinnedObj = clsidKey.GetValue("System.IsPinnedToNamespaceTree")
                                         ?? clsidKey.GetValue("System.IsPinnedToNameSpaceTree");
                            if (pinnedObj is int pinnedInt && pinnedInt == 0)
                            {
                                continue;
                            }

                            using var bagKey = clsidKey.OpenSubKey(@"Instance\InitPropertyBag");
                            var targetPath = bagKey?.GetValue("TargetFolderPath")?.ToString()
                                          ?? bagKey?.GetValue("TargetKnownFolderPath")?.ToString();

                            if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
                            {
                                continue;
                            }

                            var fullPath = Path.GetFullPath(targetPath);
                            if (!seenPaths.Add(fullPath.TrimEnd('\\')))
                            {
                                continue;
                            }

                            var displayName = clsidKey.GetValue(null)?.ToString();
                            if (string.IsNullOrWhiteSpace(displayName))
                            {
                                displayName = !string.IsNullOrWhiteSpace(nsDefaultName) && !nsDefaultName.Contains('!')
                                    ? nsDefaultName
                                    : Path.GetFileName(fullPath.TrimEnd('\\'));
                            }

                            var dirInfo = new DirectoryInfo(fullPath);
                            results.Add(new FileItem
                            {
                                Name = displayName!,
                                FullPath = fullPath,
                                IsDirectory = true,
                                IsSpecialShortcut = true,
                                UseRealShellIcon = true,
                                CustomType = "Cloud / App Folder",
                                Category = "Cloud & Apps",
                                LastWriteTime = dirInfo.Exists ? dirInfo.LastWriteTime : DateTime.Now,
                                Permissions = fullPath
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // Also check OneDrive environment variables if not already discovered via CLSID
            try
            {
                string[] envVars = { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" };
                foreach (var envVar in envVars)
                {
                    var path = Environment.GetEnvironmentVariable(envVar);
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    {
                        var fullPath = Path.GetFullPath(path);
                        if (seenPaths.Add(fullPath.TrimEnd('\\')))
                        {
                            var dirInfo = new DirectoryInfo(fullPath);
                            results.Add(new FileItem
                            {
                                Name = Path.GetFileName(fullPath.TrimEnd('\\')) is { Length: > 0 } n ? n : "OneDrive",
                                FullPath = fullPath,
                                IsDirectory = true,
                                IsSpecialShortcut = true,
                                UseRealShellIcon = true,
                                CustomType = "Cloud / App Folder",
                                Category = "Cloud & Apps",
                                LastWriteTime = dirInfo.Exists ? dirInfo.LastWriteTime : DateTime.Now,
                                Permissions = fullPath
                            });
                        }
                    }
                }
            }
            catch { }

            return results;
        }

        /// <summary>
        /// Returns standard Windows user folders (Desktop, Downloads, Documents, Pictures, Music, Videos)
        /// and custom folders pinned to Windows Explorer's Quick Access.
        /// </summary>
        public List<FileItem> GetSystemAndQuickAccessItems(HashSet<string>? seenPaths = null)
        {
            seenPaths ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<FileItem>();

            void TryAddFolder(string name, string? path, string customType, string category)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (!seenPaths.Add(fullPath.TrimEnd('\\'))) return;

                    var dirInfo = new DirectoryInfo(fullPath);
                    results.Add(new FileItem
                    {
                        Name = name,
                        FullPath = fullPath,
                        IsDirectory = true,
                        IsSpecialShortcut = true,
                        UseRealShellIcon = true,
                        CustomType = customType,
                        Category = category,
                        LastWriteTime = dirInfo.Exists ? dirInfo.LastWriteTime : DateTime.Now,
                        Permissions = fullPath
                    });
                }
                catch { }
            }

            // 1. Standard User / System Folders
            TryAddFolder("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "System Folder", "System Folders");
            TryAddFolder("Downloads", GetDownloadsFolderPath(), "System Folder", "System Folders");
            TryAddFolder("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "System Folder", "System Folders");
            TryAddFolder("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "System Folder", "System Folders");
            TryAddFolder("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "System Folder", "System Folders");
            TryAddFolder("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "System Folder", "System Folders");

            // 2. Windows Explorer Quick Access pinned folders (shell:::{679f85cb-0220-4080-b29b-5540cc05aab6})
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    dynamic? folder = shell?.NameSpace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
                    dynamic? items = folder?.Items();
                    if (items != null)
                    {
                        foreach (dynamic item in items)
                        {
                            try
                            {
                                string? itemPath = item?.Path as string;
                                string? itemName = item?.Name as string;
                                if (!string.IsNullOrWhiteSpace(itemPath) && Directory.Exists(itemPath))
                                {
                                    var displayName = !string.IsNullOrWhiteSpace(itemName)
                                        ? itemName!
                                        : Path.GetFileName(itemPath.TrimEnd('\\'));
                                    TryAddFolder(displayName, itemPath, "Quick Access", "Quick Access");
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            return results;
        }

        private static string? GetDownloadsFolderPath()
        {
            try
            {
                if (SHGetKnownFolderPath(DownloadsKnownFolderGuid, 0, IntPtr.Zero, out IntPtr pPath) == 0 && pPath != IntPtr.Zero)
                {
                    try
                    {
                        return Marshal.PtrToStringUni(pPath);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pPath);
                    }
                }
            }
            catch { }

            try
            {
                var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(fallback)) return fallback;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Returns all items displayed at the root "This PC" level:
        /// 1. Cloud & App Namespace folders (Dropbox, OneDrive, etc.)
        /// 2. Drives (C:, E:, etc.)
        /// 3. System Folders & Quick Access folders (Desktop, Downloads, Documents, pinned folders)
        /// </summary>
        public List<FileItem> GetThisPcItems()
        {
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var all = new List<FileItem>();

            all.AddRange(GetCloudNamespaceItems(seenPaths));
            all.AddRange(GetDrivesItems());
            all.AddRange(GetSystemAndQuickAccessItems(seenPaths));

            return all;
        }

        public (List<FileItem> items, string? error) GetDirectoryContents(string path)
        {
            var results = new List<FileItem>();
            try
            {
                if (string.Equals(path?.Trim(), "This PC", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path?.Trim(), "My PC", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path?.Trim(), "Drives", StringComparison.OrdinalIgnoreCase))
                {
                    return (GetThisPcItems(), null);
                }

                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    path = Environment.CurrentDirectory;
                    if (!Directory.Exists(path))
                    {
                        path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    }
                }

                var dirInfo = new DirectoryInfo(path);

                // Parent directory navigation '..'
                try
                {
                    if (dirInfo.Parent != null)
                    {
                        results.Add(new FileItem
                        {
                            Name = "..",
                            FullPath = dirInfo.Parent.FullName,
                            IsDirectory = true,
                            IsParent = true,
                            LastWriteTime = dirInfo.Parent.LastWriteTime
                        });
                    }
                    else
                    {
                        // Root drive (e.g. C:\) -> Parent is "This PC" (Drives list)
                        results.Add(new FileItem
                        {
                            Name = "..",
                            FullPath = "This PC",
                            IsDirectory = true,
                            IsParent = true,
                            LastWriteTime = DateTime.Now
                        });
                    }
                }
                catch { }

                // Subdirectories (safe enumeration)
                var dirsList = new List<FileItem>();
                try
                {
                    foreach (var d in dirInfo.EnumerateDirectories())
                    {
                        try
                        {
                            // Skip hidden or system folders (and junctions with restricted access)
                            if ((d.Attributes & FileAttributes.Hidden) != 0 || (d.Attributes & FileAttributes.System) != 0)
                                continue;

                            dirsList.Add(new FileItem
                            {
                                Name = d.Name,
                                FullPath = d.FullName,
                                IsDirectory = true,
                                Length = 0,
                                LastWriteTime = d.LastWriteTime,
                                Permissions = d.Attributes.ToString()
                            });
                        }
                        catch { }
                    }
                }
                catch { }

                results.AddRange(dirsList.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase));

                // Files (safe enumeration)
                var filesList = new List<FileItem>();
                try
                {
                    foreach (var f in dirInfo.EnumerateFiles())
                    {
                        try
                        {
                            if ((f.Attributes & FileAttributes.Hidden) != 0 || (f.Attributes & FileAttributes.System) != 0)
                                continue;

                            filesList.Add(new FileItem
                            {
                                Name = f.Name,
                                FullPath = f.FullName,
                                IsDirectory = false,
                                Length = f.Length,
                                LastWriteTime = f.LastWriteTime,
                                Permissions = f.Attributes.ToString()
                            });
                        }
                        catch { }
                    }
                }
                catch { }

                results.AddRange(filesList.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase));

                return (results, null);
            }
            catch (Exception ex)
            {
                return (results, ex.Message);
            }
        }

        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public void RenameItem(string oldPath, string newName, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newName)) return;
            var parent = Path.GetDirectoryName(oldPath);
            if (string.IsNullOrEmpty(parent)) return;
            var newPath = Path.Combine(parent, newName.Trim());
            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) return;

            if (isDirectory)
            {
                if (Directory.Exists(oldPath))
                {
                    Directory.Move(oldPath, newPath);
                }
            }
            else
            {
                if (File.Exists(oldPath))
                {
                    File.Move(oldPath, newPath);
                }
            }
        }

        public void DeleteItem(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                if (Directory.Exists(path))
                {
                    DeleteDirectoryRobust(path);
                }
            }
            else
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                    }
                    catch { }
                    File.Delete(path);
                }
            }
        }

        private static void DeleteDirectoryRobust(string path)
        {
            const int maxRetries = 6;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    NormalizeAttributesRecursive(new DirectoryInfo(path));
                    Directory.Delete(path, true);
                    return;
                }
                catch (Exception) when (i < maxRetries - 1)
                {
                    System.Threading.Thread.Sleep(80 * (i + 1));
                }
            }

            NormalizeAttributesRecursive(new DirectoryInfo(path));
            Directory.Delete(path, true);
        }

        private static void NormalizeAttributesRecursive(DirectoryInfo dir)
        {
            if (!dir.Exists) return;
            try
            {
                dir.Attributes = FileAttributes.Normal;
                foreach (var file in dir.EnumerateFiles())
                {
                    try
                    {
                        file.Attributes = FileAttributes.Normal;
                    }
                    catch { }
                }
                foreach (var subDir in dir.EnumerateDirectories())
                {
                    try
                    {
                        NormalizeAttributesRecursive(subDir);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
