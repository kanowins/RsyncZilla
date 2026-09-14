using System;

namespace RsyncZilla.Models
{
    public class FileItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public bool IsDrive { get; set; }
        public long Length { get; set; }
        public DateTime LastWriteTime { get; set; }
        public string Permissions { get; set; } = string.Empty;
        public bool IsParent { get; set; }

        public string DisplaySize
        {
            get
            {
                if (IsDrive) return FormatBytes(Length);
                if (IsDirectory) return "<DIR>";
                return FormatBytes(Length);
            }
        }

        public string DisplayType
        {
            get
            {
                if (IsParent) return "Up one level";
                if (IsDrive) return "Drive";
                if (IsDirectory) return "File folder";
                var ext = System.IO.Path.GetExtension(Name).ToLowerInvariant();
                return string.IsNullOrEmpty(ext) ? "File" : $"{ext.TrimStart('.').ToUpper()} File";
            }
        }

        public string IconEmoji
        {
            get
            {
                if (IsParent) return "📁 ⬆";
                if (IsDrive) return "💾";
                if (IsDirectory) return "📁";
                var ext = System.IO.Path.GetExtension(Name).ToLowerInvariant();
                return ext switch
                {
                    ".zip" or ".tar" or ".gz" or ".rar" or ".7z" => "📦",
                    ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" => "🖼️",
                    ".cs" or ".js" or ".ts" or ".html" or ".css" or ".py" or ".php" or ".json" or ".xml" => "📜",
                    ".exe" or ".bat" or ".cmd" or ".sh" => "⚙️",
                    ".pdf" => "📕",
                    ".txt" or ".md" or ".log" => "📝",
                    _ => "📄"
                };
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dBytes = bytes;
            while (dBytes >= 1024 && i < suffixes.Length - 1)
            {
                dBytes /= 1024;
                i++;
            }
            return $"{dBytes:0.##} {suffixes[i]}";
        }
    }
}
