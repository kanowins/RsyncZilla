using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace RsyncZilla.Services
{
    public static class DropDataHelper
    {
        private static readonly Regex FileUriRegex = new Regex(@"file:[\\\/]+[^\r\n""'<>|\s\0`]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WinPathRegex = new Regex(@"[a-zA-Z]:[\\\/][^\r\n""'<>|\s\0`]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FsPathRegex = new Regex(@"fsPath[""']?\s*:\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PathRegex = new Regex(@"[""']path[""']?\s*:\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<string> ExtractLocalPaths(IDataObject? dataObject)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (dataObject == null) return results.ToList();

            try
            {
                // 1. Standard Windows Explorer (CF_HDROP)
                if (dataObject.GetDataPresent(DataFormats.FileDrop))
                {
                    if (dataObject.GetData(DataFormats.FileDrop) is string[] files)
                    {
                        foreach (var f in files)
                        {
                            AddIfValid(f, results);
                        }
                    }
                }

                // 2. Specific standard URI & Filename formats
                string[] standardFormats = { "text/uri-list", "UniformResourceLocatorW", "UniformResourceLocator", "FileNameW", "FileName", DataFormats.UnicodeText, DataFormats.Text, "text/plain", "text/html" };
                foreach (var format in standardFormats)
                {
                    if (dataObject.GetDataPresent(format))
                    {
                        try
                        {
                            var obj = dataObject.GetData(format);
                            ExtractFromObject(obj, results);
                        }
                        catch { }
                    }
                }

                // 3. Scan all available formats (e.g. "Chromium Web Custom MIME Data Format", VS Code custom drag formats)
                var allFormats = dataObject.GetFormats();
                if (allFormats != null)
                {
                    foreach (var format in allFormats)
                    {
                        if (standardFormats.Contains(format, StringComparer.OrdinalIgnoreCase) ||
                            string.Equals(format, DataFormats.FileDrop, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        try
                        {
                            var obj = dataObject.GetData(format);
                            ExtractFromObject(obj, results);
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // Ignore any COM/OLE format conversion exceptions
            }

            return results.ToList();
        }

        public static bool HasDroppableFiles(IDataObject? dataObject)
        {
            if (dataObject == null) return false;

            try
            {
                var formats = dataObject.GetFormats();
                if (formats == null || formats.Length == 0) return false;

                // Check for explicit file / URI / VS Code formats
                foreach (var format in formats)
                {
                    if (string.Equals(format, DataFormats.FileDrop, StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(format, "text/uri-list", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(format, "UniformResourceLocatorW", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(format, "UniformResourceLocator", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(format, "FileNameW", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(format, "FileName", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(format, "Chromium Web Custom MIME Data Format", StringComparison.OrdinalIgnoreCase)) return true;

                    var lower = format.ToLowerInvariant();
                    if (lower.Contains("filedrop") || lower.Contains("uri-list") || lower.Contains("codeeditor") ||
                        lower.Contains("vscode") || lower.Contains("vnd.code") || lower.Contains("tree.explorer") ||
                        lower.Contains("tree.filelist") || lower.Contains("dragdrop"))
                    {
                        return true;
                    }
                }

                // If only plain text is available, inspect text safely
                if (dataObject.GetDataPresent(DataFormats.UnicodeText))
                {
                    try
                    {
                        var text = dataObject.GetData(DataFormats.UnicodeText) as string;
                        if (!string.IsNullOrWhiteSpace(text) &&
                            (text.Contains("file:", StringComparison.OrdinalIgnoreCase) || text.Contains(":\\") || text.Contains(":/")))
                        {
                            return true;
                        }
                    }
                    catch { }
                }
                else if (dataObject.GetDataPresent(DataFormats.Text))
                {
                    try
                    {
                        var text = dataObject.GetData(DataFormats.Text) as string;
                        if (!string.IsNullOrWhiteSpace(text) &&
                            (text.Contains("file:", StringComparison.OrdinalIgnoreCase) || text.Contains(":\\") || text.Contains(":/")))
                        {
                            return true;
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static void ExtractFromObject(object? obj, HashSet<string> results)
        {
            if (obj == null) return;

            if (obj is string text)
            {
                ParseContent(text, results);
            }
            else if (obj is string[] array)
            {
                foreach (var item in array)
                {
                    ParseContent(item, results);
                }
            }
            else if (obj is Stream stream)
            {
                try
                {
                    byte[] bytes;
                    if (stream is MemoryStream ms)
                    {
                        bytes = ms.ToArray();
                    }
                    else
                    {
                        using var buffer = new MemoryStream();
                        stream.CopyTo(buffer);
                        bytes = buffer.ToArray();
                    }
                    ExtractFromBytes(bytes, results);
                }
                catch { }
            }
            else if (obj is byte[] bytes)
            {
                ExtractFromBytes(bytes, results);
            }
        }

        private static void ExtractFromBytes(byte[] bytes, HashSet<string> results)
        {
            if (bytes == null || bytes.Length == 0) return;

            // 1. Direct UTF-8
            try
            {
                var utf8 = Encoding.UTF8.GetString(bytes);
                ParseContent(utf8, results);
            }
            catch { }

            // 2. Direct UTF-16 (Unicode)
            try
            {
                var utf16 = Encoding.Unicode.GetString(bytes);
                ParseContent(utf16, results);
            }
            catch { }

            // 3. UTF-16 offset by 1 byte (in case string started at odd offset inside Chromium pickle)
            if (bytes.Length > 2)
            {
                try
                {
                    var utf16Odd = Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1);
                    ParseContent(utf16Odd, results);
                }
                catch { }
            }

            // 4. Strip null bytes: converts any UTF-16 LE ASCII into pure UTF-8 ASCII
            try
            {
                var nonZero = bytes.Where(b => b != 0).ToArray();
                if (nonZero.Length > 0)
                {
                    var stripped = Encoding.UTF8.GetString(nonZero);
                    ParseContent(stripped, results);
                }
            }
            catch { }
        }

        private static void ParseContent(string content, HashSet<string> results)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            // 1. Line-by-line check
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim().Trim('"', '\'', '<', '>');
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                ProcessCandidate(trimmed, results);
            }

            // 2. Regex search for file:// URIs inside JSON or HTML payloads (e.g. VS Code tree items)
            var uriMatches = FileUriRegex.Matches(content);
            foreach (Match match in uriMatches)
            {
                if (match.Success)
                {
                    ProcessCandidate(match.Value, results);
                }
            }

            // 3. Regex search for direct Windows paths inside payloads
            var pathMatches = WinPathRegex.Matches(content);
            foreach (Match match in pathMatches)
            {
                if (match.Success)
                {
                    ProcessCandidate(match.Value, results);
                }
            }

            // 4. Regex search for JSON fields like "fsPath": "..." or "path": "..."
            var fsPathMatches = FsPathRegex.Matches(content);
            foreach (Match match in fsPathMatches)
            {
                if (match.Success && match.Groups.Count > 1)
                {
                    ProcessCandidate(match.Groups[1].Value, results);
                }
            }

            var pathKeyMatches = PathRegex.Matches(content);
            foreach (Match match in pathKeyMatches)
            {
                if (match.Success && match.Groups.Count > 1)
                {
                    ProcessCandidate(match.Groups[1].Value, results);
                }
            }
        }

        private static void ProcessCandidate(string raw, HashSet<string> results)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;

            // Clean common enclosing characters from JSON / HTML
            var cleaned = raw.Replace("\\\\", "\\").Trim().Trim('"', '\'', '<', '>', '{', '}', '[', ']', '`', ',', ';');
            if (string.IsNullOrWhiteSpace(cleaned)) return;

            // Trim leading slash before drive letter (e.g. "/C:/Users/..." -> "C:/Users/...")
            if ((cleaned.StartsWith("/") || cleaned.StartsWith("\\")) && cleaned.Length >= 3 && cleaned[2] == ':')
            {
                cleaned = cleaned.Substring(1);
            }

            // Handle URL-encoded characters (e.g. file:///c%3A/... -> file:///c:/...)
            var unescaped = cleaned.Contains('%') ? Uri.UnescapeDataString(cleaned) : cleaned;
            if ((unescaped.StartsWith("/") || unescaped.StartsWith("\\")) && unescaped.Length >= 3 && unescaped[2] == ':')
            {
                unescaped = unescaped.Substring(1);
            }

            // 1. Try URI parsing
            if (unescaped.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                cleaned.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var uriStr = unescaped.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? unescaped : cleaned;
                if (Uri.TryCreate(uriStr, UriKind.Absolute, out var uri) && uri.IsFile)
                {
                    AddIfValid(uri.LocalPath, results);
                    return;
                }
            }

            // 2. Direct path
            AddIfValid(unescaped, results);
            if (!string.Equals(unescaped, cleaned, StringComparison.Ordinal))
            {
                AddIfValid(cleaned, results);
            }
        }

        private static void AddIfValid(string? path, HashSet<string> results)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var trimmed = path.Trim().Trim('"', '\'');
                if ((trimmed.StartsWith("/") || trimmed.StartsWith("\\")) && trimmed.Length >= 3 && trimmed[2] == ':')
                {
                    trimmed = trimmed.Substring(1);
                }

                // Normalize slashes
                var normalized = trimmed.Replace('/', '\\').TrimEnd('\\');
                if ((normalized.Length >= 2 && normalized[1] == ':') || normalized.StartsWith(@"\\"))
                {
                    var full = Path.GetFullPath(normalized);
                    if (File.Exists(full) || Directory.Exists(full))
                    {
                        results.Add(full);
                    }
                }
            }
            catch
            {
                // Invalid path format
            }
        }
    }
}
