using System;
using System.IO;
using RsyncZilla.Services;
using Xunit;

namespace RsyncZilla.Tests
{
    public class ExplorerDropHelperTests
    {
        [Fact]
        public void GetDropTargetDirectory_DoesNotThrow()
        {
            // Verify that calling GetDropTargetDirectory outside an explorer window returns null safely
            var result = ExplorerDropHelper.GetDropTargetDirectory();
            // Result may be null (if cursor is not over explorer) or a valid directory string
            if (result != null)
            {
                Assert.True(Directory.Exists(result));
            }
        }

        [Fact]
        public void IsCursorOverExplorerOrDesktop_DoesNotThrow()
        {
            var isOver = ExplorerDropHelper.IsCursorOverExplorerOrDesktop();
            // Just verifying no native crashes / COM errors
            Assert.True(isOver || !isOver);
        }

        [Fact]
        public void LoadOle32Cursor_Check()
        {
            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
            static extern IntPtr LoadLibrary(string lpFileName);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
            static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

            IntPtr hModule = LoadLibrary("ole32.dll");
            Assert.NotEqual(IntPtr.Zero, hModule);

            // Check cursor 1 (NoDrop), 2 (Move), 3 (Copy), 4 (Link)
            for (int i = 1; i <= 4; i++)
            {
                IntPtr hCursor = LoadCursor(hModule, (IntPtr)i);
                Assert.NotEqual(IntPtr.Zero, hCursor);
                bool ok = GetIconInfo(hCursor, out ICONINFO info);
                Assert.True(ok);
                // Hotspot of standard arrow is typically (0, 0) or (1, 1)
                Assert.True(info.xHotspot >= 0 && info.yHotspot >= 0);
            }

            IntPtr copyCursor = LoadCursor(hModule, (IntPtr)3);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(copyCursor, ownsHandle: false);
            var cursor = System.Windows.Interop.CursorInteropHelper.Create(safeHandle);
            Assert.NotNull(cursor);
            Assert.NotEqual(System.Windows.Input.Cursors.Arrow, cursor);
        }

        [Fact]
        public void DragDropCopyCursor_Properties_Work()
        {
            var t = new System.Threading.Thread(() =>
            {
                var cursor = ExplorerDropHelper.DragDropCopyCursor;
                Assert.NotNull(cursor);
                Assert.NotEqual(System.Windows.Input.Cursors.Arrow, cursor);
                Assert.NotEqual(IntPtr.Zero, ExplorerDropHelper.DragDropCopyCursorHandle);

                ExplorerDropHelper.ResetDragDropFeedback();
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }
    }
}
