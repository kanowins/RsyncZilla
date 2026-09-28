using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RsyncZilla.Services
{
    public static class ExplorerDropHelper
    {
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        private const uint GA_PARENT = 1;
        private const uint GA_ROOT = 2;
        private const uint GA_ROOTOWNER = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public static bool IsCursorOverExplorerOrDesktop()
        {
            try
            {
                if (!GetCursorPos(out POINT pt)) return false;
                IntPtr hwnd = WindowFromPoint(pt);
                if (hwnd == IntPtr.Zero) return false;

                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return false;

                using var proc = Process.GetProcessById((int)pid);
                return string.Equals(proc.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string? GetDropTargetDirectory()
        {
            try
            {
                if (!GetCursorPos(out POINT pt)) return null;
                return GetDropTargetDirectoryAtPoint(pt);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExplorerDropHelper] Error: {ex.Message}");
                return null;
            }
        }

        public static string? GetDropTargetDirectoryAtPoint(POINT pt)
        {
            try
            {
                IntPtr targetHwnd = WindowFromPoint(pt);
                if (targetHwnd == IntPtr.Zero) return null;

                GetWindowThreadProcessId(targetHwnd, out uint pid);
                if (pid == 0) return null;

                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    if (!string.Equals(proc.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }
                catch
                {
                    return null;
                }

                // Check Desktop
                var sbClass = new StringBuilder(256);
                GetClassName(targetHwnd, sbClass, 256);
                string className = sbClass.ToString();

                if (targetHwnd == GetShellWindow() ||
                    className == "Progman" ||
                    className == "WorkerW")
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }

                IntPtr rootHwnd = GetAncestor(targetHwnd, GA_ROOT);
                if (rootHwnd == IntPtr.Zero) rootHwnd = targetHwnd;

                var sbRoot = new StringBuilder(256);
                GetClassName(rootHwnd, sbRoot, 256);
                string rootClass = sbRoot.ToString();
                if (rootClass == "Progman" || rootClass == "WorkerW")
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }

                // Query Shell.Application for open Windows Explorer windows
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic windows = shell.Windows();
                int count = windows.Count;

                // Pass 1: Direct HWND match against root window or target window
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic w = windows.Item(i);
                        if (w != null)
                        {
                            long hwndVal = (long)w.HWND;
                            if (hwndVal == (long)rootHwnd || hwndVal == (long)targetHwnd)
                            {
                                string? path = ExtractPathFromWindow(w);
                                if (!string.IsNullOrWhiteSpace(path)) return path;
                            }
                        }
                    }
                    catch { }
                }

                // Pass 2: Match by checking if w.HWND is ancestor of targetHwnd or vice-versa
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic w = windows.Item(i);
                        if (w != null)
                        {
                            long hwndVal = (long)w.HWND;
                            IntPtr wHwnd = (IntPtr)hwndVal;
                            IntPtr wRoot = GetAncestor(wHwnd, GA_ROOT);

                            if (wRoot == rootHwnd || wHwnd == rootHwnd)
                            {
                                string? path = ExtractPathFromWindow(w);
                                if (!string.IsNullOrWhiteSpace(path)) return path;
                            }
                        }
                    }
                    catch { }
                }

                // Pass 3: Check thread/process ID match for active explorer window under point
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic w = windows.Item(i);
                        if (w != null)
                        {
                            GetWindowThreadProcessId((IntPtr)(long)w.HWND, out uint wPid);
                            if (wPid == pid)
                            {
                                string? path = ExtractPathFromWindow(w);
                                if (!string.IsNullOrWhiteSpace(path)) return path;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExplorerDropHelper] Error: {ex.Message}");
            }

            return null;
        }

        private static string? ExtractPathFromWindow(dynamic w)
        {
            try
            {
                string? path = w.Document?.Folder?.Self?.Path;
                if (!string.IsNullOrWhiteSpace(path) && (Directory.Exists(path) || path.StartsWith("::")))
                {
                    if (path.StartsWith("::"))
                    {
                        // Special shell virtual folder (like Desktop or Downloads)
                        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    }
                    return path;
                }

                // Fallback: check LocationURL (e.g. file:///C:/Users/...)
                string? url = w.LocationURL;
                if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(url);
                    var local = uri.LocalPath;
                    if (Directory.Exists(local)) return local;
                }
            }
            catch { }

            return null;
        }
    }
}
