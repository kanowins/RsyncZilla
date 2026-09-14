using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RsyncZilla.Models;

namespace RsyncZilla.Services
{
    public class LocalFileService
    {
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

        public (List<FileItem> items, string? error) GetDirectoryContents(string path)
        {
            var results = new List<FileItem>();
            try
            {
                if (string.Equals(path?.Trim(), "This PC", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path?.Trim(), "My PC", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path?.Trim(), "Drives", StringComparison.OrdinalIgnoreCase))
                {
                    return (GetDrivesItems(), null);
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
