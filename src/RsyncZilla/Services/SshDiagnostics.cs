using System;
using System.Net.Sockets;
using Renci.SshNet.Common;

namespace RsyncZilla.Services
{
    public class SshDiagnosticResult
    {
        public bool IsServerError { get; set; }
        public string Category { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Diagnosis { get; set; } = "";
        public string RawMessage { get; set; } = "";
        public string SuggestedAction { get; set; } = "";

        public string FormatForTerminal()
        {
            var origin = IsServerError ? "Remote Server" : "Local Client / Network";
            return $"\r\n\x1b[1;31m════════════════════════════════════════════════════════════════\x1b[0m\r\n" +
                   $"\x1b[1;31m[SSH Connection Failed]\x1b[0m {Summary}\r\n" +
                   $"\x1b[1;33mOrigin:    \x1b[0m {origin}\r\n" +
                   $"\x1b[1;36mCategory:  \x1b[0m {Category}\r\n" +
                   $"\x1b[1;37mDiagnosis: \x1b[0m {Diagnosis}\r\n" +
                   $"\x1b[90mDetails:   {RawMessage}\x1b[0m\r\n" +
                   $"\x1b[1;32mHint:      {SuggestedAction}\x1b[0m\r\n" +
                   $"\x1b[90mPress '🔄 Reconnect' in the toolbar to retry.\x1b[0m\r\n" +
                   $"\x1b[1;31m════════════════════════════════════════════════════════════════\x1b[0m\r\n";
        }
    }

    public static class SshDiagnostics
    {
        private static T? FindInnerException<T>(Exception ex) where T : Exception
        {
            Exception? cur = ex;
            while (cur != null)
            {
                if (cur is T match) return match;
                cur = cur.InnerException;
            }
            return null;
        }

        public static SshDiagnosticResult Analyze(Exception ex, string host, int port, string username)
        {
            var raw = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            var socketEx = FindInnerException<SocketException>(ex);

            if (string.IsNullOrWhiteSpace(host))
            {
                return new SshDiagnosticResult
                {
                    IsServerError = false,
                    Category = "Configuration",
                    Summary = "Host is empty",
                    Diagnosis = "No server hostname or IP address was specified.",
                    RawMessage = raw,
                    SuggestedAction = "Enter a valid server host and port."
                };
            }

            if (ex is SshAuthenticationException || raw.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new SshDiagnosticResult
                {
                    IsServerError = true,
                    Category = "Authentication",
                    Summary = $"Authentication rejected for user '{username}'",
                    Diagnosis = $"The SSH server at {host}:{port} rejected the credentials. Possible causes: incorrect password, user does not exist on the server, or password authentication is disabled in server's /etc/ssh/sshd_config (PasswordAuthentication no).",
                    RawMessage = raw,
                    SuggestedAction = "Verify username and password in Site Manager / Quick Connect bar."
                };
            }

            if (socketEx != null)
            {
                if (socketEx.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    return new SshDiagnosticResult
                    {
                        IsServerError = true,
                        Category = "Connection Refused",
                        Summary = $"Server actively refused connection on port {port}",
                        Diagnosis = $"The host {host} is reachable, but no SSH service is accepting connections on port {port}. The SSH daemon (sshd) may be stopped, or the server is listening on a different port.",
                        RawMessage = raw,
                        SuggestedAction = "Check if sshd service is running on the server ('systemctl status ssh') or verify port number."
                    };
                }

                if (socketEx.SocketErrorCode == SocketError.TimedOut || 
                    socketEx.SocketErrorCode == SocketError.HostUnreachable || 
                    socketEx.SocketErrorCode == SocketError.NetworkUnreachable)
                {
                    return new SshDiagnosticResult
                    {
                        IsServerError = true,
                        Category = "Network / Timeout",
                        Summary = $"Connection to {host}:{port} timed out",
                        Diagnosis = $"Could not establish network connection to {host}:{port}. The server may be offline, the IP address unreachable, or a cloud/host firewall (e.g. AWS Security Group, UFW, iptables) is silently dropping incoming packets on port {port}.",
                        RawMessage = raw,
                        SuggestedAction = "Verify server status and firewall rules allowing incoming TCP port " + port + "."
                    };
                }

                if (socketEx.SocketErrorCode == SocketError.HostNotFound)
                {
                    return new SshDiagnosticResult
                    {
                        IsServerError = false,
                        Category = "DNS Resolution",
                        Summary = $"Host '{host}' could not be resolved",
                        Diagnosis = $"DNS lookup failed on the local client machine for '{host}'. The domain name might be misspelled or the local DNS server is unreachable.",
                        RawMessage = raw,
                        SuggestedAction = "Check domain spelling or test connecting by server IP address."
                    };
                }

                if (socketEx.SocketErrorCode == SocketError.ConnectionReset || 
                    socketEx.SocketErrorCode == SocketError.ConnectionAborted)
                {
                    return new SshDiagnosticResult
                    {
                        IsServerError = true,
                        Category = "Connection Reset",
                        Summary = "Connection reset by server or intermediate network",
                        Diagnosis = $"The TCP connection was established, but the server or an intermediate firewall/proxy closed it immediately. This frequently occurs if fail2ban blocked your IP address or sshd closed the connection due to MaxStartups limits.",
                        RawMessage = raw,
                        SuggestedAction = "Wait a few seconds and try reconnecting, or check /var/log/auth.log on the server."
                    };
                }
            }

            if (ex is SshOperationTimeoutException || raw.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new SshDiagnosticResult
                {
                    IsServerError = true,
                    Category = "Timeout",
                    Summary = $"SSH operation timed out connecting to {host}:{port}",
                    Diagnosis = $"The server did not respond to the SSH handshake within the timeout period. The server or network connection may be experiencing high latency or packet loss.",
                    RawMessage = raw,
                    SuggestedAction = "Click Reconnect to try again."
                };
            }

            if (raw.IndexOf("Channel open failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("pty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("subsystem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("Channel request failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new SshDiagnosticResult
                {
                    IsServerError = true,
                    Category = "PTY / Shell Allocation",
                    Summary = "Server rejected interactive shell/PTY channel",
                    Diagnosis = $"SSH connection and authentication succeeded, but the server rejected opening the interactive terminal channel. Possible causes: OpenSSH MaxStartups or MaxSessions limit reached on the server, user '{username}' has no interactive login shell (e.g. /bin/false or /usr/sbin/nologin), or server resource exhaustion.",
                    RawMessage = raw,
                    SuggestedAction = "Check user shell on the server ('getent passwd " + username + "') and verify sshd MaxSessions."
                };
            }

            if (raw.IndexOf("Key exchange negotiation failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("Algorithm negotiation failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new SshDiagnosticResult
                {
                    IsServerError = true,
                    Category = "Cipher Negotiation",
                    Summary = "Cipher / Key exchange algorithm mismatch",
                    Diagnosis = "Client and server could not agree on compatible cryptographic algorithms (KEX, Ciphers, or HostKeys).",
                    RawMessage = raw,
                    SuggestedAction = "Check server sshd_config KexAlgorithms and Ciphers."
                };
            }

            return new SshDiagnosticResult
            {
                IsServerError = raw.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0 || raw.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0,
                Category = "General Connection Error",
                Summary = $"Failed to connect to {host}: {ex.Message}",
                Diagnosis = $"An unexpected error occurred during connection: {raw}",
                RawMessage = raw,
                SuggestedAction = "Click Reconnect to try again. Check server and client logs."
            };
        }
    }
}
