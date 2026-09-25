using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RsyncZilla.Models;

namespace RsyncZilla.Services
{
    public class FileZillaImportService
    {
        /// <summary>
        /// Attempts to locate the FileZilla configuration file on the local machine.
        /// Searches common standard paths for sitemanager.xml and filezilla.xml.
        /// </summary>
        public string? DetectFileZillaConfigPath()
        {
            var candidates = new List<string>();

            // Windows AppData (Roaming)
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                candidates.Add(Path.Combine(appData, "FileZilla", "sitemanager.xml"));
                candidates.Add(Path.Combine(appData, "FileZilla", "filezilla.xml"));
            }

            // Windows LocalAppData
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                candidates.Add(Path.Combine(localAppData, "FileZilla", "sitemanager.xml"));
                candidates.Add(Path.Combine(localAppData, "FileZilla", "filezilla.xml"));
            }

            // UserProfile (.config/filezilla or .filezilla)
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                candidates.Add(Path.Combine(userProfile, ".config", "filezilla", "sitemanager.xml"));
                candidates.Add(Path.Combine(userProfile, ".filezilla", "sitemanager.xml"));
                candidates.Add(Path.Combine(userProfile, ".config", "filezilla", "filezilla.xml"));
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>
        /// Reads and parses a FileZilla XML file (sitemanager.xml, filezilla.xml, or exported XML).
        /// </summary>
        public List<FileZillaSite> ParseFile(string filePath, IEnumerable<SavedConnection>? existingConnections = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
            }

            var content = File.ReadAllText(filePath);
            return ParseXml(content, existingConnections);
        }

        /// <summary>
        /// Parses XML content from FileZilla format into a list of FileZillaSite items.
        /// </summary>
        public List<FileZillaSite> ParseXml(string xmlContent, IEnumerable<SavedConnection>? existingConnections = null)
        {
            var sites = new List<FileZillaSite>();
            if (string.IsNullOrWhiteSpace(xmlContent)) return sites;

            var doc = XDocument.Parse(xmlContent);
            var root = doc.Root;
            if (root == null) return sites;

            var existingList = existingConnections?.ToList() ?? new List<SavedConnection>();

            // 1. Try finding <Servers> container(s)
            var serversContainers = doc.Descendants("Servers").ToList();
            if (serversContainers.Count > 0)
            {
                foreach (var container in serversContainers)
                {
                    TraverseContainer(container, string.Empty, sites);
                }
            }
            else
            {
                // Fallback: check if root or direct children contain Server or Folder elements
                TraverseContainer(root, string.Empty, sites);
            }

            // 2. If no servers found via tree traversal, search for any <Server> descendants directly
            if (sites.Count == 0)
            {
                foreach (var serverElem in doc.Descendants("Server"))
                {
                    var site = ParseServerElement(serverElem, string.Empty);
                    if (site != null)
                    {
                        sites.Add(site);
                    }
                }
            }

            // 3. Fallback: check for <Tab> elements in <Tabs> (found in filezilla.xml recent sessions)
            if (sites.Count == 0)
            {
                foreach (var tabElem in doc.Descendants("Tab"))
                {
                    var site = ParseServerElement(tabElem, "Recent Tabs");
                    if (site != null && !sites.Any(s => s.Host.Equals(site.Host, StringComparison.OrdinalIgnoreCase) && s.Username.Equals(site.Username, StringComparison.OrdinalIgnoreCase) && s.Port == site.Port))
                    {
                        sites.Add(site);
                    }
                }
            }

            // 4. Mark duplicates against existing connections
            foreach (var site in sites)
            {
                site.IsDuplicate = existingList.Any(e =>
                    e.Host.Equals(site.Host, StringComparison.OrdinalIgnoreCase) &&
                    e.Username.Equals(site.Username, StringComparison.OrdinalIgnoreCase) &&
                    e.Port == site.Port);
            }

            return sites;
        }

        /// <summary>
        /// Recursively traverses XML nodes, capturing folder hierarchy and server entries.
        /// </summary>
        private void TraverseContainer(XElement container, string currentFolderPath, List<FileZillaSite> results)
        {
            foreach (var node in container.Elements())
            {
                var localName = node.Name.LocalName;

                if (localName.Equals("Folder", StringComparison.OrdinalIgnoreCase))
                {
                    // In FileZilla, folder name is often the text node inside <Folder> or an attribute
                    var folderText = node.Nodes().OfType<XText>()
                        .Select(t => t.Value?.Trim())
                        .FirstOrDefault(t => !string.IsNullOrEmpty(t));

                    var folderName = folderText ?? node.Attribute("name")?.Value?.Trim() ?? "Folder";

                    var nextPath = string.IsNullOrWhiteSpace(currentFolderPath)
                        ? folderName
                        : $"{currentFolderPath} / {folderName}";

                    TraverseContainer(node, nextPath, results);
                }
                else if (localName.Equals("Server", StringComparison.OrdinalIgnoreCase))
                {
                    var site = ParseServerElement(node, currentFolderPath);
                    if (site != null)
                    {
                        results.Add(site);
                    }
                }
            }
        }

        /// <summary>
        /// Extracts server properties from an XML element (<Server> or <Tab>).
        /// </summary>
        public FileZillaSite? ParseServerElement(XElement element, string folderPath)
        {
            var host = element.Element("Host")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(host)) return null;

            // Protocol: 0 = FTP, 1 = SFTP, 2 = FTPS, 3 = FTPES, 4 = Storj, 5 = WebDAV
            var protocolStr = element.Element("Protocol")?.Value?.Trim() ?? "1";
            string protocolDisplay = protocolStr switch
            {
                "0" => "SFTP (from FTP)",
                "1" => "SFTP (SSH)",
                "2" => "SFTP (from FTPS)",
                "3" => "SFTP (from FTPES)",
                "4" => "Storj",
                "5" => "WebDAV",
                _ => "SFTP (SSH)"
            };

            // Port: RsyncZilla connects via SSH/SFTP (standard port 22).
            // If the port in FileZilla is 21 (FTP standard) or not specified, automatically convert to 22.
            int port = 22;
            if (int.TryParse(element.Element("Port")?.Value?.Trim(), out var p) && p > 0)
            {
                port = (p == 21) ? 22 : p;
            }

            // Username
            var username = element.Element("User")?.Value?.Trim()
                ?? element.Element("Username")?.Value?.Trim()
                ?? string.Empty;

            // Site Name
            var name = element.Element("Name")?.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = !string.IsNullOrWhiteSpace(username) ? $"{username}@{host}" : host;
            }

            // Local Path
            var localPath = element.Element("LocalDir")?.Value?.Trim()
                ?? element.Element("LocalPath")?.Value?.Trim()
                ?? string.Empty;

            // Remote Path (SafePath format or direct path)
            var remotePathRaw = element.Element("RemoteDir")?.Value?.Trim()
                ?? element.Element("RemotePath")?.Value?.Trim()
                ?? string.Empty;
            var remotePath = DecodeRemotePath(remotePathRaw);

            // Comments
            var comments = element.Element("Comments")?.Value?.Trim() ?? string.Empty;

            return new FileZillaSite
            {
                Name = name,
                Folder = folderPath,
                Host = host,
                Port = port,
                Username = username,
                Protocol = protocolDisplay,
                LocalPath = localPath,
                RemotePath = remotePath,
                Comments = comments,
                IsSelected = true
            };
        }

        /// <summary>
        /// Decodes FileZilla's CServerPath::GetSafePath format into a standard UNIX/Windows path.
        /// Example input: "1 0 4 home 6 debian" -> "/home/debian"
        /// If input is already a normal path (e.g. "/var/www" or "C:\dir"), returns it untouched.
        /// </summary>
        public static string DecodeRemotePath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
            var s = rawPath.Trim();

            // SafePath format starts with: [format_version] [path_style] ...
            if (!Regex.IsMatch(s, @"^\d+\s+\d+"))
            {
                return s;
            }

            try
            {
                int idx = 0;
                // Skip format version (e.g. "1")
                while (idx < s.Length && !char.IsWhiteSpace(s[idx])) idx++;
                while (idx < s.Length && char.IsWhiteSpace(s[idx])) idx++;

                // Read path style (0 = UNIX, 1 = DOS, etc.)
                int styleStart = idx;
                while (idx < s.Length && !char.IsWhiteSpace(s[idx])) idx++;
                string style = s.Substring(styleStart, idx - styleStart);
                while (idx < s.Length && char.IsWhiteSpace(s[idx])) idx++;

                var segments = new List<string>();
                while (idx < s.Length)
                {
                    int lenStart = idx;
                    while (idx < s.Length && char.IsDigit(s[idx])) idx++;
                    if (lenStart == idx) break;

                    if (!int.TryParse(s.Substring(lenStart, idx - lenStart), out int segLen))
                        break;

                    // Skip the single space separator after the length digit
                    if (idx < s.Length && s[idx] == ' ') idx++;

                    if (idx + segLen <= s.Length)
                    {
                        segments.Add(s.Substring(idx, segLen));
                        idx += segLen;
                    }
                    else
                    {
                        segments.Add(s.Substring(idx));
                        break;
                    }

                    // Skip whitespace between segments
                    while (idx < s.Length && char.IsWhiteSpace(s[idx])) idx++;
                }

                if (segments.Count == 0) return "/";

                if (style == "1") // DOS style
                {
                    if (segments[0].EndsWith(":"))
                    {
                        return string.Join("\\", segments);
                    }
                    return "\\" + string.Join("\\", segments);
                }

                // Default UNIX style
                return "/" + string.Join("/", segments);
            }
            catch
            {
                return s;
            }
        }

        /// <summary>
        /// Imports a collection of FileZillaSite items into the ConnectionManagerService.
        /// </summary>
        public int Import(IEnumerable<FileZillaSite> sites, ConnectionManagerService connectionManager, bool overwriteExisting = true)
        {
            var connections = sites
                .Where(s => s.IsSelected && !string.IsNullOrWhiteSpace(s.Host))
                .Select(s => s.ToSavedConnection());

            return connectionManager.ImportConnections(connections, overwriteExisting);
        }
    }
}
