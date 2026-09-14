using System;
using System.IO;
using System.Text;
using System.Windows;
using RsyncZilla.Services;
using Xunit;

namespace RsyncZilla.Tests
{
    public class DropDataHelperTests
    {
        [Fact]
        public void ExtractLocalPaths_WithStandardFileDrop_ShouldReturnPaths()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, new[] { tempFile });

                var results = DropDataHelper.ExtractLocalPaths(data);

                Assert.Single(results);
                Assert.Equal(Path.GetFullPath(tempFile), results[0]);
                Assert.True(DropDataHelper.HasDroppableFiles(data));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ExtractLocalPaths_WithVsCodeUriList_ShouldDecodeAndReturnPaths()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                // VS Code format: file:///c%3A/Users/.../temp.tmp
                var uri = new Uri(tempFile).AbsoluteUri;
                var vsCodeUri = uri.Replace(":", "%3A"); // simulates VS Code encoding drive colon

                var data = new DataObject();
                data.SetData("text/uri-list", vsCodeUri);

                var results = DropDataHelper.ExtractLocalPaths(data);

                Assert.Single(results);
                Assert.Equal(Path.GetFullPath(tempFile), results[0]);
                Assert.True(DropDataHelper.HasDroppableFiles(data));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ExtractLocalPaths_WithMultipleVsCodeUris_ShouldReturnAllPaths()
        {
            var file1 = Path.GetTempFileName();
            var file2 = Path.GetTempFileName();
            try
            {
                var uri1 = new Uri(file1).AbsoluteUri;
                var uri2 = new Uri(file2).AbsoluteUri;
                var payload = $"{uri1}\r\n{uri2}\r\n";

                var data = new DataObject();
                data.SetData("text/uri-list", payload);

                var results = DropDataHelper.ExtractLocalPaths(data);

                Assert.Equal(2, results.Count);
                Assert.Contains(Path.GetFullPath(file1), results);
                Assert.Contains(Path.GetFullPath(file2), results);
            }
            finally
            {
                if (File.Exists(file1)) File.Delete(file1);
                if (File.Exists(file2)) File.Delete(file2);
            }
        }

        [Fact]
        public void ExtractLocalPaths_WithChromiumMemoryStreamUriList_ShouldParseCorrectly()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var uri = new Uri(tempFile).AbsoluteUri;
                var bytes = Encoding.UTF8.GetBytes(uri);
                using var stream = new MemoryStream(bytes);

                var data = new DataObject();
                data.SetData("text/uri-list", stream);

                var results = DropDataHelper.ExtractLocalPaths(data);

                Assert.Single(results);
                Assert.Equal(Path.GetFullPath(tempFile), results[0]);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ExtractLocalPaths_WithUnicodeTextContainingPath_ShouldReturnPath()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.UnicodeText, tempFile);

                var results = DropDataHelper.ExtractLocalPaths(data);

                Assert.Single(results);
                Assert.Equal(Path.GetFullPath(tempFile), results[0]);
                Assert.True(DropDataHelper.HasDroppableFiles(data));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void HasDroppableFiles_WithNonFileText_ShouldReturnFalse()
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, "Just some random text without paths");

            Assert.False(DropDataHelper.HasDroppableFiles(data));
        }


        [Fact]
        public void ExtractLocalPaths_WithChromiumCustomMimeFormat_ShouldExtractAndValidate()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var uri = new Uri(tempFile).AbsoluteUri;
                var vsCodePayload = $"something\0application/vnd.code.tree.explorer\0{{\"resource\":\"{uri}\"}}\0";
                var bytes = Encoding.UTF8.GetBytes(vsCodePayload);

                var data = new DataObject();
                data.SetData("Chromium Web Custom MIME Data Format", new MemoryStream(bytes));

                var results = DropDataHelper.ExtractLocalPaths(data);

                Assert.Single(results);
                Assert.Equal(Path.GetFullPath(tempFile), results[0]);
                Assert.True(DropDataHelper.HasDroppableFiles(data));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ExtractLocalPaths_WithUnicodeStream_ShouldExtractAndValidate()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var vsCodePayload = $"[{{\"path\":\"{tempFile}\"}}]";
                var bytes = Encoding.Unicode.GetBytes(vsCodePayload);

                var data = new DataObject();
                data.SetData("codeeditors", new MemoryStream(bytes));

                var results = DropDataHelper.ExtractLocalPaths(data);

                Assert.Single(results);
                Assert.Equal(Path.GetFullPath(tempFile), results[0]);
                Assert.True(DropDataHelper.HasDroppableFiles(data));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ExtractLocalPaths_WithVsCodeTreeJsonFsPath_ShouldExtractAndValidate()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var escapedPath = tempFile.Replace("\\", "\\\\");
                var vsCodePayload = $"{{\"fsPath\":\"{escapedPath}\",\"path\":\"/{tempFile.Replace('\\', '/')}\"}}";

                var data = new DataObject();
                data.SetData("application/vnd.code.tree.explorer", vsCodePayload);

                var results = DropDataHelper.ExtractLocalPaths(data);

                Assert.Single(results);
                Assert.Equal(Path.GetFullPath(tempFile), results[0]);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ExtractLocalPaths_WithLeadingSlashPath_ShouldExtractAndValidate()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var leadingSlashPath = "/" + tempFile.Replace('\\', '/');

                var data = new DataObject();
                data.SetData(DataFormats.UnicodeText, leadingSlashPath);

                var results = DropDataHelper.ExtractLocalPaths(data);

                Assert.Single(results);
                Assert.Equal(Path.GetFullPath(tempFile), results[0]);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ExtractLocalPaths_WithOddByteOffsetUtf16_ShouldExtractAndValidate()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var vsCodePayload = $"file:///{tempFile.Replace('\\', '/')}";
                var rawUtf16 = Encoding.Unicode.GetBytes(vsCodePayload);
                // Prepend 1 dummy byte to make it start at an odd offset
                var oddBuffer = new byte[rawUtf16.Length + 1];
                oddBuffer[0] = 0xAA;
                Array.Copy(rawUtf16, 0, oddBuffer, 1, rawUtf16.Length);

                var data = new DataObject();
                data.SetData("Chromium Web Custom MIME Data Format", new MemoryStream(oddBuffer));

                var results = DropDataHelper.ExtractLocalPaths(data);

                Assert.Single(results);
                Assert.Equal(Path.GetFullPath(tempFile), results[0]);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ExtractLocalPaths_WhenOneFormatThrowsComException_ShouldContinueAndExtractFromOtherFormats()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var mockData = new MockComExceptionDataObject(tempFile);
                var results = DropDataHelper.ExtractLocalPaths(mockData);

                Assert.Single(results);
                Assert.Equal(Path.GetFullPath(tempFile), results[0]);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private class MockComExceptionDataObject : IDataObject
        {
            private readonly string _validPath;
            public MockComExceptionDataObject(string validPath) => _validPath = validPath;

            public object GetData(string format, bool autoConvert) => GetData(format);
            public object GetData(Type format) => throw new NotImplementedException();
            public object GetData(string format)
            {
                if (format == DataFormats.FileDrop)
                {
                    throw new System.Runtime.InteropServices.COMException("Invalid FORMATETC", unchecked((int)0x80040064));
                }
                if (format == DataFormats.UnicodeText || format == DataFormats.Text)
                {
                    return _validPath;
                }
                throw new NotImplementedException();
            }

            public bool GetDataPresent(string format, bool autoConvert) => GetDataPresent(format);
            public bool GetDataPresent(Type format) => false;
            public bool GetDataPresent(string format) => format == DataFormats.FileDrop || format == DataFormats.UnicodeText;

            public string[] GetFormats(bool autoConvert) => GetFormats();
            public string[] GetFormats() => new[] { DataFormats.FileDrop, DataFormats.UnicodeText };

            public void SetData(string format, object data, bool autoConvert) { }
            public void SetData(Type format, object data) { }
            public void SetData(string format, object data) { }
            public void SetData(object data) { }
        }

        [Fact]
        public void HasDroppableFiles_WithNullData_ShouldReturnFalse()
        {
            Assert.False(DropDataHelper.HasDroppableFiles(null));
        }



        [Fact]
        public void VirtualFileDataObject_CreatesFileGroupDescriptorW_Successfully()
        {
            var vfdo = new VirtualFileDataObject.VirtualFileDataObject();
            var desc = new VirtualFileDataObject.VirtualFileDataObject.FileDescriptor
            {
                Name = "test.txt",
                Length = 12,
                StreamContents = stream =>
                {
                    var bytes = Encoding.UTF8.GetBytes("Hello World!");
                    stream.Write(bytes, 0, bytes.Length);
                }
            };
            vfdo.SetData(new[] { desc });

            var wpfData = new DataObject(vfdo);
            Assert.True(wpfData.GetDataPresent("FileGroupDescriptorW"));
        }
    }
}






