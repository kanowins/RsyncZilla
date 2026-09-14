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

        public (List<FileItem> items, string? error) GetDirectoryContents(string path)
        {
            var results = new List<FileItem>();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    path = Environment.CurrentDirectory;
                    if (!Directory.Exists(path))
                    {
                        path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    }
                }

                var dirInfo = new DirectoryInfo(path);

                // Parent directory navigation '..' if not root drive
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

        public void DeleteItem(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            else
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
