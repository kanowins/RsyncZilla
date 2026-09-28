using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using RsyncZilla.Models;
using RsyncZilla.Services;
using RsyncZilla.ViewModels;

namespace RsyncZilla.Views
{
    public partial class TerminalView : UserControl, IDisposable
    {
        private readonly RemoteSessionViewModel _session;
        private readonly MainViewModel _mainViewModel;
        private bool _isWebViewInitialized;
        private bool _isDisposed;
        private readonly System.Threading.SemaphoreSlim _treeLock = new(1, 1);
        private uint _currentCols = 80;
        private uint _currentRows = 24;

        // Directory synchronization state
        private bool _isSyncEnabled = true;
        private volatile bool _isSyncing;
        private bool _initialNavigationPending;
        private string? _lastKnownTerminalPath;
        private string _rollingOutputBuffer = "";
        private readonly object _bufferLock = new();

        public TerminalView(RemoteSessionViewModel session, MainViewModel mainViewModel)
        {
            InitializeComponent();
            _session = session;
            _mainViewModel = mainViewModel;
            DataContext = _session;

            if (!string.IsNullOrWhiteSpace(_session.RemoteBrowser.CurrentPath))
            {
                _lastKnownTerminalPath = _session.RemoteBrowser.CurrentPath;
            }

            if (!string.IsNullOrWhiteSpace(_session.InitialTerminalPath) && _session.InitialTerminalPath != "~")
            {
                _initialNavigationPending = true;
                _ = Task.Delay(3500).ContinueWith(_ => _initialNavigationPending = false);
            }

            _session.RemoteBrowser.PropertyChanged += OnRemoteBrowserPropertyChanged;

            Loaded += TerminalView_Loaded;
            Unloaded += TerminalView_Unloaded;
        }

        private async void TerminalView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isWebViewInitialized)
            {
                await InitializeWebViewAsync();
            }
            else
            {
                // Trigger re-fit when tab becomes visible again
                try
                {
                    await TerminalWebView.CoreWebView2.ExecuteScriptAsync("if (typeof reportResize === 'function') reportResize();");
                    TerminalWebView.Focus();
                }
                catch { }
            }
        }

        private void TerminalView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Keep WebView2 alive while in visual tree
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                ShowConnecting($"Initializing terminal for {_session.Username}@{_session.Host}...");

                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RsyncZilla",
                    "WebView2");
                Directory.CreateDirectory(userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await TerminalWebView.EnsureCoreWebView2Async(env);

                TerminalWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                TerminalWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                TerminalWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                TerminalWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

                var terminalFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Terminal");
                if (Directory.Exists(terminalFolder))
                {
                    TerminalWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "terminal.rsynczilla",
                        terminalFolder,
                        CoreWebView2HostResourceAccessKind.Allow);
                    TerminalWebView.CoreWebView2.Navigate("https://terminal.rsynczilla/terminal.html");
                }
                else
                {
                    ShowError("Terminal resources folder was not found.");
                    return;
                }

                TerminalWebView.WebMessageReceived += OnWebMessageReceived;
                _isWebViewInitialized = true;
            }
            catch (Exception ex)
            {
                ShowError($"Failed to initialize terminal engine: {ex.Message}");
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp)) return;
                var type = typeProp.GetString();

                switch (type)
                {
                    case "ready":
                        _currentCols = root.TryGetProperty("cols", out var c) ? c.GetUInt32() : 80;
                        _currentRows = root.TryGetProperty("rows", out var r) ? r.GetUInt32() : 24;
                        _ = ConnectSshAsync(_currentCols, _currentRows);
                        break;

                    case "resize":
                        _currentCols = root.TryGetProperty("cols", out var rc) ? rc.GetUInt32() : 80;
                        _currentRows = root.TryGetProperty("rows", out var rr) ? rr.GetUInt32() : 24;
                        _session.TerminalSession?.ChangeSize(_currentCols, _currentRows);
                        break;

                    case "input":
                        if (root.TryGetProperty("data", out var dataProp))
                        {
                            var data = dataProp.GetString();
                            if (!string.IsNullOrEmpty(data))
                            {
                                _session.TerminalSession?.SendInput(data);
                            }
                        }
                        break;

                    case "copy_selection":
                        if (root.TryGetProperty("text", out var textProp))
                        {
                            var text = textProp.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                Clipboard.SetText(text);
                            }
                        }
                        break;

                    case "request_paste":
                        if (Clipboard.ContainsText())
                        {
                            var text = Clipboard.GetText();
                            if (!string.IsNullOrEmpty(text))
                            {
                                _session.TerminalSession?.SendInput(text);
                            }
                        }
                        break;

                    case "dir_changed":
                        if (root.TryGetProperty("path", out var pathProp))
                        {
                            var rawPath = pathProp.GetString();
                            _ = HandleTerminalDirectoryChangedAsync(rawPath);
                        }
                        break;
                }
            }
            catch { }
        }

        private async Task ConnectSshAsync(uint cols, uint rows)
        {
            if (_session == null) return;

            cols = Math.Max(cols > 0 ? cols : 80u, 20u);
            rows = Math.Max(rows > 0 ? rows : 24u, 5u);

            ShowConnecting($"Connecting SSH terminal to {_session.Username}@{_session.Host}:{_session.Port}...");
            _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] Connecting SSH shell ({cols}x{rows})...", false);

            try
            {
                _session.TerminalSession?.Dispose();

                var terminal = new SshTerminalSession(
                    _session.Host,
                    _session.Port,
                    _session.Username,
                    _session.Password,
                    _session.InitialTerminalPath);

                terminal.OutputReceived += text =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            TerminalWebView.CoreWebView2?.PostWebMessageAsString(text);
                        }
                        catch { }
                    });

                    ProcessTerminalOutputForDirectoryChange(text);
                };

                terminal.Disconnected += () =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        StatusDot.Text = "🔴";
                        TerminalTitleText.Text = $"{_session.Username}@{_session.Host} (Disconnected)";
                        _session.NotifyConnectionChanged();
                        _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] SSH shell disconnected.", false);
                    });
                };

                terminal.ErrorOccurred += err =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        _mainViewModel.AddLog($"[Terminal Error: {_session.Username}@{_session.Host}] {err}", true);
                    });
                };

                var (success, error, diagnostic) = await terminal.ConnectAsync(cols, rows);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (success)
                    {
                        _session.TerminalSession = terminal;
                        StatusDot.Text = "🟢";
                        TerminalTitleText.Text = $"{_session.Username}@{_session.Host}:{_session.Port}";
                        HideConnecting();
                        _session.NotifyConnectionChanged();
                        _mainViewModel.OnSessionStateChanged();
                        _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] SSH shell connected successfully ({cols}x{rows}).", false);

                        TerminalWebView.Focus();
                        try
                        {
                            TerminalWebView.CoreWebView2?.ExecuteScriptAsync("term.focus();");
                        }
                        catch { }

                        // Terminal connected successfully! Now load and verify the remote tree
                        _ = EnsureTreeLoadedAsync(forceReload: false);
                    }
                    else
                    {
                        var diag = diagnostic ?? new SshDiagnosticResult
                        {
                            IsServerError = true,
                            Category = "Connection Error",
                            Summary = error ?? "Failed to connect",
                            Diagnosis = "Could not establish SSH terminal connection.",
                            RawMessage = error ?? "Unknown error",
                            SuggestedAction = "Click '🔄 Reconnect' in the toolbar to retry."
                        };

                        _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] SSH connection failed: {diag.Summary}. Origin: {(diag.IsServerError ? "Remote Server" : "Local Client / Network")}. Diagnosis: {diag.Diagnosis} ({diag.RawMessage})", true);
                        ShowError(diag);
                    }
                });
            }
            catch (Exception ex)
            {
                var diag = SshDiagnostics.Analyze(ex, _session.Host, _session.Port, _session.Username);
                await Dispatcher.InvokeAsync(() =>
                {
                    _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] SSH connection exception: {diag.Summary}. Origin: {(diag.IsServerError ? "Remote Server" : "Local Client / Network")}. Diagnosis: {diag.Diagnosis} ({diag.RawMessage})", true);
                    ShowError(diag);
                });
            }
        }

        private void ShowConnecting(string message)
        {
            ConnectingText.Text = message;
            ConnectingProgressBar.Visibility = Visibility.Visible;
            RetryButton.Visibility = Visibility.Collapsed;
            StatusDot.Text = "🟡";
            TerminalTitleText.Text = "Connecting...";

            if (!_isWebViewInitialized)
            {
                TerminalWebView.Visibility = Visibility.Collapsed;
                ConnectingOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                try
                {
                    var notice = $"\r\n\x1b[1;36m[RsyncZilla] {message}\x1b[0m\r\n";
                    var jsonText = JsonSerializer.Serialize(notice);
                    TerminalWebView.CoreWebView2?.ExecuteScriptAsync($"term.write({jsonText});");
                }
                catch { }
            }
        }

        private void HideConnecting()
        {
            ConnectingOverlay.Visibility = Visibility.Collapsed;
            TerminalWebView.Visibility = Visibility.Visible;
        }

        private void ShowError(string message)
        {
            var diag = new SshDiagnosticResult
            {
                IsServerError = false,
                Category = "Initialization",
                Summary = message,
                Diagnosis = message,
                RawMessage = message,
                SuggestedAction = "Check application resources and retry."
            };
            ShowError(diag);
        }

        private void ShowError(SshDiagnosticResult diag)
        {
            StatusDot.Text = "🔴";
            TerminalTitleText.Text = $"{_session.Username}@{_session.Host} (Error)";

            ConnectingText.Text = $"{diag.Summary}\n\nOrigin: {(diag.IsServerError ? "Remote Server" : "Local Client / Network")}\n\n{diag.Diagnosis}";
            ConnectingProgressBar.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Visible;

            if (_isWebViewInitialized && TerminalWebView.CoreWebView2 != null)
            {
                HideConnecting();
                var formatted = diag.FormatForTerminal();
                var jsonText = JsonSerializer.Serialize(formatted);
                try
                {
                    TerminalWebView.CoreWebView2.ExecuteScriptAsync($"term.clear(); term.write({jsonText}); term.focus();");
                }
                catch { }
            }
            else
            {
                TerminalWebView.Visibility = Visibility.Collapsed;
                ConnectingOverlay.Visibility = Visibility.Visible;
            }
        }

        // ==========================================
        // TREE VERIFICATION & RECONNECTION
        // ==========================================

        public async Task EnsureTreeLoadedAsync(string? targetPath = null, bool forceReload = false)
        {
            if (_session == null) return;

            await _treeLock.WaitAsync();
            try
            {
                // 1. Verify or establish SFTP connection
                if (!_session.SftpService.IsConnected || forceReload)
                {
                    UpdateTreeOverlay(isLoading: true, "Connecting to SFTP server...");
                    _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] Connecting SFTP for remote tree...", false);
                    var (sftpOk, sftpErr) = await _session.SftpService.ConnectAsync(_session.Host, _session.Port, _session.Username, _session.Password);
                    if (!sftpOk)
                    {
                        var diag = SshDiagnostics.Analyze(new Exception(sftpErr ?? "SFTP connection failed"), _session.Host, _session.Port, _session.Username);
                        _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] SFTP connection failed: {diag.Summary}. Origin: {(diag.IsServerError ? "Remote Server" : "Local Client / Network")}. Diagnosis: {diag.Diagnosis}", true);
                        UpdateTreeOverlay(isLoading: false, "SFTP Connection Failed", diag.Diagnosis);
                        return;
                    }
                    _session.NotifyConnectionChanged();
                    _mainViewModel.OnSessionStateChanged();
                }

                // 2. Determine target path
                var path = !string.IsNullOrWhiteSpace(targetPath)
                    ? targetPath
                    : (!string.IsNullOrWhiteSpace(_session.RemoteBrowser.CurrentPath)
                        ? _session.RemoteBrowser.CurrentPath
                        : (!string.IsNullOrWhiteSpace(_session.InitialTerminalPath)
                            ? _session.InitialTerminalPath
                            : (!string.IsNullOrWhiteSpace(_session.SftpService.CurrentPath)
                                ? _session.SftpService.CurrentPath
                                : "/")));

                // If already on that path with items loaded and not forcing reload, done
                if (!forceReload && _session.RemoteBrowser.Items.Count > 0 &&
                    string.Equals(_session.RemoteBrowser.CurrentPath, path, StringComparison.Ordinal))
                {
                    HideTreeOverlay();
                    return;
                }

                UpdateTreeOverlay(isLoading: true, $"Loading {path}...");
                _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] Loading tree: {path}...", false);
                await _session.RemoteBrowser.NavigateToAsync(path);

                // 3. Fallback if navigation to initial path failed
                if (!string.IsNullOrWhiteSpace(_session.RemoteBrowser.ErrorMessage))
                {
                    var fallback = !string.IsNullOrWhiteSpace(_session.SftpService.CurrentPath)
                        ? _session.SftpService.CurrentPath
                        : "/";

                    if (!string.Equals(path, fallback, StringComparison.Ordinal))
                    {
                        _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] Notice: Could not list '{path}', retrying with home '{fallback}'...", false, isWarning: true);
                        await _session.RemoteBrowser.NavigateToAsync(fallback);
                    }
                }

                // 4. Update UI state based on result
                if (!string.IsNullOrWhiteSpace(_session.RemoteBrowser.ErrorMessage))
                {
                    _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] Remote tree failed to load: {_session.RemoteBrowser.ErrorMessage}", true);
                    UpdateTreeOverlay(isLoading: false, "Tree Load Error", _session.RemoteBrowser.ErrorMessage);
                }
                else
                {
                    _lastKnownTerminalPath = _session.RemoteBrowser.CurrentPath;
                    _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] Remote tree verified: {_session.RemoteBrowser.Items.Count} item(s) in '{_session.RemoteBrowser.CurrentPath}'.", false);
                    HideTreeOverlay();
                }
            }
            catch (Exception ex)
            {
                _mainViewModel.AddLog($"[Terminal: {_session.Username}@{_session.Host}] Tree error: {ex.Message}", true);
                UpdateTreeOverlay(isLoading: false, "Tree Error", ex.Message);
            }
            finally
            {
                _treeLock.Release();
            }
        }

        private void UpdateTreeOverlay(bool isLoading, string title, string? subtitle = null)
        {
            TreeProgressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

            if (isLoading)
            {
                if (_session.RemoteBrowser.Items.Count == 0)
                {
                    TreeStatusIcon.Text = "⏳";
                    TreeStatusTitle.Text = title;
                    TreeStatusSubtitle.Text = subtitle ?? "Connecting to server...";
                    TreeStatusOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    TreeStatusOverlay.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                TreeStatusIcon.Text = "⚠️";
                TreeStatusTitle.Text = title;
                TreeStatusSubtitle.Text = subtitle ?? "Could not load files.";
                TreeStatusOverlay.Visibility = Visibility.Visible;
            }
        }

        private void HideTreeOverlay()
        {
            TreeProgressBar.Visibility = Visibility.Collapsed;
            TreeStatusOverlay.Visibility = Visibility.Collapsed;
        }

        private async void RefreshTreeButton_Click(object sender, RoutedEventArgs e)
        {
            await EnsureTreeLoadedAsync(targetPath: null, forceReload: true);
        }

        // ==========================================
        // TERMINAL TOOLBAR ACTIONS
        // ==========================================

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TerminalWebView.CoreWebView2 != null)
                {
                    await TerminalWebView.CoreWebView2.ExecuteScriptAsync(
                        "if (term.hasSelection()) { window.chrome.webview.postMessage({ type: 'copy_selection', text: term.getSelection() }); }");
                }
            }
            catch { }
        }

        private void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    _session.TerminalSession?.SendInput(text);
                    TerminalWebView.Focus();
                }
            }
            catch { }
        }

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TerminalWebView.CoreWebView2 != null)
                {
                    await TerminalWebView.CoreWebView2.ExecuteScriptAsync("term.clear();");
                    TerminalWebView.Focus();
                }
            }
            catch { }
        }

        public async Task ReconnectAllAsync()
        {
            await ConnectSshAsync(_currentCols, _currentRows);
            await EnsureTreeLoadedAsync(targetPath: null, forceReload: true);
        }

        private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
        {
            await ReconnectAllAsync();
        }

        // ==========================================
        // DIRECTORY SYNCHRONIZATION
        // ==========================================

        private void SyncDirToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isSyncEnabled = SyncDirToggleButton.IsChecked == true;
            SyncDirToggleButton.ToolTip = _isSyncEnabled
                ? "Directory Synchronization: ON (Tree and Terminal sync automatically)"
                : "Directory Synchronization: OFF (Click to sync tree & terminal directories)";

            if (_isSyncEnabled && !string.IsNullOrWhiteSpace(_session.RemoteBrowser.CurrentPath))
            {
                var escaped = _session.RemoteBrowser.CurrentPath.Replace("'", "'\\''");
                _session.TerminalSession?.SendInput($"cd '{escaped}'\r");
                TerminalWebView.Focus();
            }
        }

        private void OnRemoteBrowserPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileBrowserViewModel.CurrentPath))
            {
                var newPath = _session.RemoteBrowser.CurrentPath;
                if (string.IsNullOrWhiteSpace(newPath)) return;

                if (_isSyncEnabled && !_isSyncing)
                {
                    if (!string.Equals(_lastKnownTerminalPath, newPath, StringComparison.Ordinal))
                    {
                        _isSyncing = true;
                        _lastKnownTerminalPath = newPath;
                        try
                        {
                            var escaped = newPath.Replace("'", "'\\''");
                            _session.TerminalSession?.SendInput($"cd '{escaped}'\r");
                        }
                        catch { }
                        finally
                        {
                            _ = Task.Delay(600).ContinueWith(_ => _isSyncing = false);
                        }
                    }
                }
            }
        }

        public string GetEffectiveHomeDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_session.SftpService.HomeDirectory) && _session.SftpService.HomeDirectory != "/")
            {
                return _session.SftpService.HomeDirectory;
            }
            return _session.Username == "root" ? "/root" : $"/home/{_session.Username}";
        }

        public static string ResolveRemotePath(string rawPath, string? homeDirectory, string? currentDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return "";
            var resolved = rawPath.Trim();

            // Handle ~
            if (resolved == "~")
            {
                return string.IsNullOrEmpty(homeDirectory) ? "/" : homeDirectory.TrimEnd('/');
            }
            if (resolved.StartsWith("~/"))
            {
                var home = string.IsNullOrEmpty(homeDirectory) ? "" : homeDirectory.TrimEnd('/');
                return home + resolved.Substring(1);
            }

            // Absolute path (/...)
            if (resolved.StartsWith("/"))
            {
                return resolved;
            }

            // Relative path fallback (e.g. "subdir" or "../subdir")
            if (!string.IsNullOrEmpty(currentDirectory) && currentDirectory.StartsWith("/"))
            {
                return $"{currentDirectory.TrimEnd('/')}/{resolved}";
            }

            return "/" + resolved;
        }

        private static readonly System.Text.RegularExpressions.Regex AnsiEscapeRegex = new(
            @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\].*?(?:\x07|\x1B\\))",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex PromptRegex1 = new(
            @"(?:^|[\r\n])(?:\([^\)]+\)\s*)?[\w.-]+@[\w.-]+[:\s]+([~/][^\$#>]*?)[\$#>]\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex PromptRegex2 = new(
            @"(?:^|[\r\n])\[(?:\([^\)]+\)\s*)?[\w.-]+@[\w.-]+\s+([~/][^\]\$#>]*?)\]\s*[\$#>]\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex PromptRegex3 = new(
            @"(?:^|[\r\n])(?:\([^\)]+\)\s*)?([~/][^\s\$#>]+)\s*[\$#>]\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string? TryExtractDirectoryFromPrompt(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Strip ANSI color and OSC escape codes
            var clean = AnsiEscapeRegex.Replace(text, "");

            // Examine the last line of the buffer
            var lastNewline = clean.LastIndexOfAny(new[] { '\r', '\n' });
            var lastLine = lastNewline >= 0 ? clean.Substring(lastNewline + 1) : clean;

            if (string.IsNullOrWhiteSpace(lastLine)) return null;

            var trimmedEnd = lastLine.TrimEnd();
            if (!trimmedEnd.EndsWith("$") && !trimmedEnd.EndsWith("#") && !trimmedEnd.EndsWith(">"))
            {
                return null;
            }

            var m1 = PromptRegex1.Match(lastLine);
            if (m1.Success && m1.Groups[1].Length > 0)
            {
                return m1.Groups[1].Value.Trim();
            }

            var m2 = PromptRegex2.Match(lastLine);
            if (m2.Success && m2.Groups[1].Length > 0)
            {
                return m2.Groups[1].Value.Trim();
            }

            var m3 = PromptRegex3.Match(lastLine);
            if (m3.Success && m3.Groups[1].Length > 0)
            {
                return m3.Groups[1].Value.Trim();
            }

            return null;
        }

        private void ProcessTerminalOutputForDirectoryChange(string chunk)
        {
            if (!_isSyncEnabled || string.IsNullOrEmpty(chunk)) return;

            string bufferToAnalyze;
            lock (_bufferLock)
            {
                _rollingOutputBuffer += chunk;
                if (_rollingOutputBuffer.Length > 2048)
                {
                    _rollingOutputBuffer = _rollingOutputBuffer.Substring(_rollingOutputBuffer.Length - 2048);
                }
                bufferToAnalyze = _rollingOutputBuffer;
            }

            var detectedPath = TryExtractDirectoryFromPrompt(bufferToAnalyze);
            if (!string.IsNullOrWhiteSpace(detectedPath))
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    await HandleTerminalDirectoryChangedAsync(detectedPath);
                });
            }
        }

        public async Task HandleTerminalDirectoryChangedAsync(string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath) || !_isSyncEnabled) return;

            var home = GetEffectiveHomeDirectory();
            var resolvedPath = ResolveRemotePath(rawPath, home, _session.RemoteBrowser.CurrentPath);
            if (!resolvedPath.StartsWith("/")) return;

            // When opening the terminal in a specific directory, ignore the shell's default startup prompt in home
            if (_initialNavigationPending)
            {
                if (string.Equals(resolvedPath, home, StringComparison.Ordinal) || rawPath == "~")
                {
                    return;
                }
                _initialNavigationPending = false;
            }

            if (_isSyncing) return;

            if (string.Equals(_session.RemoteBrowser.CurrentPath, resolvedPath, StringComparison.Ordinal) && _session.RemoteBrowser.Items.Count > 0)
            {
                _lastKnownTerminalPath = resolvedPath;
                return;
            }

            try
            {
                _isSyncing = true;
                _lastKnownTerminalPath = resolvedPath;
                _mainViewModel.AddLog($"[Terminal Sync] Terminal changed directory to '{resolvedPath}', syncing tree...", false);
                await EnsureTreeLoadedAsync(resolvedPath, forceReload: false);
            }
            catch (Exception ex)
            {
                _mainViewModel.AddLog($"[Terminal Sync] Failed to sync tree to '{resolvedPath}': {ex.Message}", true);
            }
            finally
            {
                _ = Task.Delay(600).ContinueWith(_ => _isSyncing = false);
            }
        }

        // ==========================================
        // COMPACT REMOTE BROWSER ACTIONS
        // ==========================================

        private async void RemotePathTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await EnsureTreeLoadedAsync(RemotePathTextBox.Text, forceReload: !_session.SftpService.IsConnected);
            }
        }

        private async void RemoteDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item)
            {
                if (item.IsDirectory)
                {
                    await EnsureTreeLoadedAsync(item.FullPath, forceReload: !_session.SftpService.IsConnected);
                }
                else if (!item.IsParent)
                {
                    _mainViewModel.EditRemoteFileCommand.Execute(item);
                }
            }
        }

        private void RemoteDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void RemoteDataGrid_DragEnter(object sender, DragEventArgs e)
        {
            RemoteDataGrid_DragOver(sender, e);
        }

        private void RemoteDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (!_session.IsConnected) return;

            var pos = e.GetPosition(RemoteDataGrid);
            var targetItem = GetItemAtPosition(RemoteDataGrid, pos);

            string targetPath = _session.RemoteBrowser.CurrentPath;
            if (targetItem != null && targetItem.IsDirectory && !targetItem.IsParent)
            {
                targetPath = targetItem.FullPath;
            }

            var paths = DropDataHelper.ExtractLocalPaths(e.Data);
            if (paths.Count > 0)
            {
                _ = _mainViewModel.UploadPathsAsync(paths, targetPath);
                e.Handled = true;
            }
        }

        private static FileItem? GetItemAtPosition(DataGrid grid, Point position)
        {
            var element = grid.InputHitTest(position) as DependencyObject;
            while (element != null && element != grid)
            {
                if (element is DataGridRow row && row.Item is FileItem item)
                {
                    return item;
                }
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private void UploadFilesButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Files to Upload",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
            {
                _ = _mainViewModel.UploadPathsAsync(dlg.FileNames, _session.RemoteBrowser.CurrentPath);
            }
        }

        private void UploadFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select Folder to Upload"
            };

            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            {
                _ = _mainViewModel.UploadPathsAsync(new[] { dlg.FolderName }, _session.RemoteBrowser.CurrentPath);
            }
        }

        private async void DownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = RemoteDataGrid.SelectedItems.Cast<FileItem>()
                .Where(i => i != null && !i.IsParent).ToList();

            if (!selected.Any()) return;

            var dlg = new OpenFolderDialog
            {
                Title = "Select Local Destination Folder"
            };

            if (!string.IsNullOrWhiteSpace(_session.LastLocalPath) && Directory.Exists(_session.LastLocalPath))
            {
                dlg.InitialDirectory = _session.LastLocalPath;
            }

            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            {
                await _mainViewModel.DownloadItemsAsync(selected, dlg.FolderName);
            }
        }

        private void CdInTerminal_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item && item.IsDirectory && !item.IsParent)
            {
                var escapedPath = item.FullPath.Replace("'", "'\\''");
                _session.TerminalSession?.SendInput($"cd '{escapedPath}'\r");
                TerminalWebView.Focus();
                TerminalWebView.CoreWebView2?.ExecuteScriptAsync("term.focus();");
            }
        }

        private void EditRemoteFile_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item && !item.IsDirectory && !item.IsParent)
            {
                _mainViewModel.ActiveSession = _session;
                _mainViewModel.EditRemoteFileCommand.Execute(item);
            }
        }

        private void EditRemoteFileWith_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item && !item.IsDirectory && !item.IsParent)
            {
                _mainViewModel.ActiveSession = _session;
                _mainViewModel.EditRemoteFileWithCommand.Execute(item);
            }
        }

        private void RemoteDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F4)
            {
                bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                if (isShift)
                {
                    EditRemoteFileWith_Click(sender, e);
                }
                else
                {
                    EditRemoteFile_Click(sender, e);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteRemoteItem_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                RenameRemoteItem_Click(sender, e);
                e.Handled = true;
            }
        }

        private async void NewRemoteFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("New Remote Folder", "Enter name for the new remote folder:", "new_folder")
            {
                Owner = Application.Current.MainWindow
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResponseText))
            {
                await _session.RemoteBrowser.CreateFolderAsync(dlg.ResponseText.Trim());
            }
        }

        private async void RenameRemoteItem_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item && !item.IsParent)
            {
                var dlg = new InputDialog("Rename Remote Item", "Enter new name:", item.Name)
                {
                    Owner = Application.Current.MainWindow
                };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResponseText))
                {
                    var newName = dlg.ResponseText.Trim();
                    if (!string.Equals(newName, item.Name, StringComparison.Ordinal))
                    {
                        await _session.RemoteBrowser.RenameItemAsync(item, newName);
                    }
                }
            }
        }

        private async void DeleteRemoteItem_Click(object sender, RoutedEventArgs e)
        {
            var selected = RemoteDataGrid.SelectedItems.Cast<FileItem>()
                .Where(i => i != null && !i.IsParent).ToList();

            if (!selected.Any()) return;

            string prompt = selected.Count == 1
                ? $"Are you sure you want to delete '{selected[0].Name}' from the remote server?"
                : $"Are you sure you want to delete the {selected.Count} selected items from the remote server?";

            var confirm = MessageBox.Show(prompt, "Confirm Remote Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                await _session.RemoteBrowser.DeleteItemsAsync(selected);
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteDataGrid.SelectedItem is FileItem item && !item.IsParent)
            {
                Clipboard.SetText(item.FullPath);
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                try
                {
                    _session.RemoteBrowser.PropertyChanged -= OnRemoteBrowserPropertyChanged;
                }
                catch { }

                try
                {
                    TerminalWebView.WebMessageReceived -= OnWebMessageReceived;
                    TerminalWebView.Dispose();
                }
                catch { }

                try
                {
                    _session.TerminalSession?.Dispose();
                }
                catch { }

                try
                {
                    _treeLock.Dispose();
                }
                catch { }
            }
        }
    }
}
