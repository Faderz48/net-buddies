using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NetBuddies.Core;

namespace NetBuddies.Server.Gui;

public partial class MainWindow : Window
{
    private const string LinuxStunnelInstallCommand = "sudo apt update && sudo apt install stunnel4";
    private const string LinuxNodeInstallCommand = "sudo apt update && sudo apt install nodejs npm";
    private const string WindowsStunnelInstallHint = "Install stunnel for Windows from https://www.stunnel.org/downloads.html, then reopen Net Buddies Server.";
    private const string WindowsNodeInstallHint = "Install Node.js 22+ for Windows from https://nodejs.org/, then reopen Net Buddies Server.";
    private readonly BuddyServer _server = new();
    private Process? _stunnelProcess;
    private Process? _realtimeProcess;
    private string _lastStunnelConfigPath = "";
    private bool _isInitialized;

    public MainWindow()
    {
        InitializeComponent();
        _isInitialized = true;
        _server.StatusChanged += message => Dispatcher.UIThread.Post(() => AddLog(message));
        Closed += async (_, _) => await StopEverythingAsync();
        UpdateStunnelPreview();
        AddLog("Net Buddies Server GUI ready.");
        CheckStunnelAvailability(logSuccess: false);
        RefreshRealtimeGamesList(logSuccess: false);
    }

    private async void StartServer_Click(object? sender, RoutedEventArgs e)
    {
        if (_server.IsRunning)
        {
            AddLog("Server is already running.");
            return;
        }

        try
        {
            var mode = CurrentTlsMode;
            var serverPort = ReadPort(PortBox, 5050);
            var publicTlsPort = ReadPort(StunnelPortBox, 5051);
            var options = new BuddyServerOptions
            {
                Certificate = mode == ServerTlsMode.DotNetTls ? LoadPfxCertificate() : null,
                InviteCode = InviteCodeBox.Text?.Trim() ?? ""
            };

            await _server.StartAsync(serverPort, options);

            if (mode == ServerTlsMode.Stunnel)
            {
                StartStunnel(serverPort, publicTlsPort);
            }

            if (RealtimeEnabledBox.IsChecked == true)
            {
                StartRealtimeGameServer();
            }

            HeaderStatusText.Text = mode switch
            {
                ServerTlsMode.DotNetTls => $"Secure server running on port {serverPort}",
                ServerTlsMode.Stunnel => $"Server running on {serverPort}, stunnel on {publicTlsPort}",
                _ => $"Server running on port {serverPort}"
            };
            ConnectionHintText.Text = mode switch
            {
                ServerTlsMode.DotNetTls => $"Clients should connect to your server address on port {serverPort} with Use TLS enabled.",
                ServerTlsMode.Stunnel => $"Clients should connect to your server address on port {publicTlsPort} with Use TLS enabled. stunnel forwards to local port {serverPort}.",
                _ => $"Clients should connect to your server address on port {serverPort} without TLS, unless you are using a private tunnel."
            };
            if (RealtimeEnabledBox.IsChecked == true)
            {
                ConnectionHintText.Text += $" Real-time games use {RealtimePublicUrlBox.Text?.Trim()}.";
            }
        }
        catch (Exception ex)
        {
            AddLog($"Could not start server: {ex.Message}");
            await StopEverythingAsync();
        }
    }

    private async void StopServer_Click(object? sender, RoutedEventArgs e)
    {
        await StopEverythingAsync();
    }

    private async void BrowsePfx_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Net Buddies server PFX certificate",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PFX certificates")
                {
                    Patterns = ["*.pfx", "*.p12"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            PfxPathBox.Text = path;
            AddLog($"Selected PFX certificate: {path}");
        }
    }

    private async void BrowsePem_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose stunnel PEM certificate",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PEM certificates")
                {
                    Patterns = ["*.pem", "*.crt", "*.key"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            StunnelPemPathBox.Text = path;
            UpdateStunnelPreview();
            AddLog($"Selected stunnel PEM: {path}");
        }
    }

    private void GenerateCertificates_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var password = CertificatePasswordBox.Text;
            if (string.IsNullOrWhiteSpace(password))
            {
                password = Guid.NewGuid().ToString("N")[..16];
                CertificatePasswordBox.Text = password;
            }

            var generated = ServerCertificateService.Generate(
                CertificateDirectory,
                password,
                string.IsNullOrWhiteSpace(CertificateHostBox.Text) ? "netbuddies.local" : CertificateHostBox.Text.Trim());
            PfxPathBox.Text = generated.PfxPath;
            StunnelPemPathBox.Text = generated.StunnelPemPath;
            _lastStunnelConfigPath = generated.SuggestedStunnelConfigPath;
            UpdateStunnelPreview();
            AddLog($"Generated PFX certificate: {generated.PfxPath}");
            AddLog($"Generated stunnel PEM: {generated.StunnelPemPath}");
        }
        catch (Exception ex)
        {
            AddLog($"Could not generate certificates: {ex.Message}");
        }
    }

    private void SaveStunnelConfig_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(_lastStunnelConfigPath)
                ? Path.Combine(CertificateDirectory, $"netbuddies-stunnel-{DateTime.Now:yyyyMMdd-HHmmss}.conf")
                : _lastStunnelConfigPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BuildStunnelConfig(ReadPort(PortBox, 5050), ReadPort(StunnelPortBox, 5051)));
            _lastStunnelConfigPath = path;
            AddLog($"Saved stunnel config: {path}");
        }
        catch (Exception ex)
        {
            AddLog($"Could not save stunnel config: {ex.Message}");
        }
    }

    private void QuickTlsSetup_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            TlsModeBox.SelectedIndex = 2;
            if (!CheckStunnelAvailability(logSuccess: true))
            {
                TlsSetupStatusText.Text = $"Install stunnel first. {StunnelInstallHint}";
                return;
            }

            if (string.IsNullOrWhiteSpace(StunnelPemPathBox.Text) || !File.Exists(StunnelPemPathBox.Text.Trim()))
            {
                GenerateCertificates_Click(sender, e);
            }

            SaveStunnelConfig_Click(sender, e);
            TlsSetupStatusText.Text = $"TLS ready. Start Server, then clients connect to port {ReadPort(StunnelPortBox, 5051)} with Use TLS enabled.";
            ConnectionHintText.Text = TlsSetupStatusText.Text;
        }
        catch (Exception ex)
        {
            TlsSetupStatusText.Text = $"Quick TLS setup failed: {ex.Message}";
            AddLog(TlsSetupStatusText.Text);
        }
    }

    private void CheckStunnel_Click(object? sender, RoutedEventArgs e)
    {
        CheckStunnelAvailability(logSuccess: true);
    }

    private async void CopyInstallCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null)
        {
            await Clipboard.SetTextAsync(StunnelInstallClipboardText);
            AddLog("Copied stunnel install instructions.");
            TlsSetupStatusText.Text = $"Copied: {StunnelInstallClipboardText}";
        }
    }

    private void OpenCertificateFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(CertificateDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = CertificateDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AddLog($"Could not open certificate folder: {ex.Message}");
        }
    }

    private async void InstallRealtimeDependencies_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var realtimeDirectory = PrepareWritableRealtimeGamesDirectory();
            if (!Directory.Exists(realtimeDirectory))
            {
                throw new InvalidOperationException($"Real-time game files were not found at {realtimeDirectory}.");
            }

            var npm = FindExecutable("npm");
            if (string.IsNullOrWhiteSpace(npm))
            {
                throw new InvalidOperationException($"npm was not found. {NodeInstallHint}");
            }

            RealtimeStatusText.Text = $"Installing Colyseus dependencies in {realtimeDirectory}...";
            AddLog($"Installing real-time game dependencies in {realtimeDirectory} with npm install...");
            await RunProcessAsync(npm, ["install"], realtimeDirectory, "realtime npm");
            RealtimeStatusText.Text = "Real-time game dependencies are installed.";
            RefreshRealtimeGamesList(logSuccess: true);
        }
        catch (Exception ex)
        {
            RealtimeStatusText.Text = $"Real-time setup failed: {ex.Message}";
            AddLog(RealtimeStatusText.Text);
        }
    }

    private void CheckNode_Click(object? sender, RoutedEventArgs e)
    {
        var node = FindExecutable("node");
        var npm = FindExecutable("npm");
        if (!string.IsNullOrWhiteSpace(node) && !string.IsNullOrWhiteSpace(npm))
        {
            RealtimeStatusText.Text = $"Node found: {node}. npm found: {npm}.";
            AddLog(RealtimeStatusText.Text);
            return;
        }

        RealtimeStatusText.Text = $"Node/npm not found. {NodeInstallHint}";
        AddLog(RealtimeStatusText.Text);
    }

    private void OpenRealtimeFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var realtimeDirectory = PrepareWritableRealtimeGamesDirectory();
            Directory.CreateDirectory(realtimeDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = realtimeDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AddLog($"Could not open real-time game folder: {ex.Message}");
        }
    }

    private void OpenRealtimeGamesFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var realtimeDirectory = PrepareWritableRealtimeGamesDirectory();
            var gamesDirectory = Path.Combine(realtimeDirectory, "games");
            Directory.CreateDirectory(gamesDirectory);
            EnsureRealtimeGameTemplate(gamesDirectory);
            RefreshRealtimeGamesList(logSuccess: false);
            Process.Start(new ProcessStartInfo
            {
                FileName = gamesDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AddLog($"Could not open modular games folder: {ex.Message}");
        }
    }

    private void RefreshRealtimeGames_Click(object? sender, RoutedEventArgs e)
    {
        RefreshRealtimeGamesList(logSuccess: true);
    }

    private void StartStunnel(int serverPort, int publicTlsPort)
    {
        var stunnelExecutable = FindStunnelExecutable();
        if (string.IsNullOrWhiteSpace(stunnelExecutable))
        {
            throw new InvalidOperationException($"stunnel is not installed or not on PATH. {StunnelInstallHint}");
        }

        var pemPath = StunnelPemPathBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(pemPath) || !File.Exists(pemPath))
        {
            throw new InvalidOperationException("stunnel mode needs a generated or selected PEM certificate.");
        }

        var configPath = string.IsNullOrWhiteSpace(_lastStunnelConfigPath)
            ? Path.Combine(CertificateDirectory, $"netbuddies-stunnel-running-{DateTime.Now:yyyyMMdd-HHmmss}.conf")
            : _lastStunnelConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, BuildStunnelConfig(serverPort, publicTlsPort));
        _lastStunnelConfigPath = configPath;
        UpdateStunnelPreview();

        _stunnelProcess = Process.Start(new ProcessStartInfo
        {
            FileName = stunnelExecutable,
            ArgumentList = { configPath },
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        });

        if (_stunnelProcess is null)
        {
            throw new InvalidOperationException("stunnel did not start.");
        }

        _stunnelProcess.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Dispatcher.UIThread.Post(() => AddLog($"stunnel: {args.Data}"));
            }
        };
        _stunnelProcess.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Dispatcher.UIThread.Post(() => AddLog($"stunnel: {args.Data}"));
            }
        };
        _stunnelProcess.BeginOutputReadLine();
        _stunnelProcess.BeginErrorReadLine();
        AddLog($"Started {stunnelExecutable} using {configPath}");
    }

    private void StartRealtimeGameServer()
    {
        if (_realtimeProcess is { HasExited: false })
        {
            AddLog("Real-time game server is already running.");
            return;
        }

        var node = FindExecutable("node");
        if (string.IsNullOrWhiteSpace(node))
        {
            throw new InvalidOperationException($"Node.js is not installed or not on PATH. {NodeInstallHint}");
        }

        var realtimeDirectory = PrepareWritableRealtimeGamesDirectory();
        var games = RefreshRealtimeGamesList(logSuccess: true);
        var serverScript = Path.Combine(realtimeDirectory, "server.js");
        var nodeModules = Path.Combine(realtimeDirectory, "node_modules");
        if (!File.Exists(serverScript))
        {
            throw new InvalidOperationException($"Real-time game server script was not found: {serverScript}");
        }

        if (!Directory.Exists(nodeModules))
        {
            throw new InvalidOperationException("Real-time game dependencies are not installed. Click Install/update in the Real-Time Game Server panel.");
        }

        var port = ReadPort(RealtimePortBox, 2567);
        var bindAddress = string.IsNullOrWhiteSpace(RealtimeBindBox.Text)
            ? "0.0.0.0"
            : RealtimeBindBox.Text.Trim();
        var startInfo = new ProcessStartInfo
        {
            FileName = node,
            WorkingDirectory = realtimeDirectory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add(serverScript);
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add(bindAddress);
        startInfo.ArgumentList.Add((port + 1).ToString());
        startInfo.Environment["NETBUDDIES_REALTIME_PORT"] = port.ToString();
        startInfo.Environment["NETBUDDIES_REALTIME_BIND"] = bindAddress;
        startInfo.Environment["NETBUDDIES_PONG_PORT"] = (port + 1).ToString();
        startInfo.Environment["NETBUDDIES_REALTIME_GAMES_DIR"] = Path.Combine(realtimeDirectory, "games");

        _realtimeProcess = Process.Start(startInfo);
        if (_realtimeProcess is null)
        {
            throw new InvalidOperationException("Real-time game server did not start.");
        }

        _realtimeProcess.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Dispatcher.UIThread.Post(() => AddLog($"realtime: {args.Data}"));
            }
        };
        _realtimeProcess.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Dispatcher.UIThread.Post(() => AddLog($"realtime: {args.Data}"));
            }
        };
        _realtimeProcess.BeginOutputReadLine();
        _realtimeProcess.BeginErrorReadLine();
        var gamesSummary = games.Count == 1 ? "1 game" : $"{games.Count} games";
        RealtimeStatusText.Text = $"Real-time game server running on {bindAddress}:{port}. Buddy Pong uses {bindAddress}:{port + 1}. Detected {gamesSummary}.";
        AddLog($"Started real-time game server on {bindAddress}:{port}; Buddy Pong on {bindAddress}:{port + 1}; detected {gamesSummary}.");
    }

    private async Task StopEverythingAsync()
    {
        if (_realtimeProcess is not null)
        {
            try
            {
                if (!_realtimeProcess.HasExited)
                {
                    _realtimeProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                AddLog($"Could not stop real-time game server cleanly: {ex.Message}");
            }
            finally
            {
                _realtimeProcess.Dispose();
                _realtimeProcess = null;
                RealtimeStatusText.Text = "Real-time game server stopped.";
                AddLog("Real-time game server stopped.");
            }
        }

        if (_stunnelProcess is not null)
        {
            try
            {
                if (!_stunnelProcess.HasExited)
                {
                    _stunnelProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                AddLog($"Could not stop stunnel cleanly: {ex.Message}");
            }
            finally
            {
                _stunnelProcess.Dispose();
                _stunnelProcess = null;
                AddLog("stunnel stopped.");
            }
        }

        await _server.StopAsync();
        HeaderStatusText.Text = "Ready to host";
        ConnectionHintText.Text = "";
    }

    private X509Certificate2 LoadPfxCertificate()
    {
        var path = PfxPathBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException(".NET TLS mode needs a .pfx certificate.");
        }

        return X509CertificateLoader.LoadPkcs12FromFile(path, CertificatePasswordBox.Text ?? "");
    }

    private string BuildStunnelConfig(int serverPort, int publicTlsPort)
    {
        var pemPath = StunnelPemPathBox.Text?.Trim() ?? "";
        var bindAddress = string.IsNullOrWhiteSpace(BindAddressBox.Text)
            ? "0.0.0.0"
            : BindAddressBox.Text.Trim();
        return string.Join(Environment.NewLine,
            "foreground = yes",
            "debug = info",
            "sslVersionMin = TLSv1.2",
            "TIMEOUTclose = 0",
            "",
            "[netbuddies]",
            "client = no",
            $"accept = {bindAddress}:{publicTlsPort}",
            $"connect = 127.0.0.1:{serverPort}",
            $"cert = {pemPath}",
            "");
    }

    private void UpdateStunnelPreview()
    {
        if (!_isInitialized || StunnelConfigBox is null || PortBox is null || StunnelPortBox is null)
        {
            return;
        }

        StunnelConfigBox.Text = BuildStunnelConfig(ReadPort(PortBox, 5050), ReadPort(StunnelPortBox, 5051));
    }

    private void TlsMode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        UpdateStunnelPreview();
        if (CurrentTlsMode == ServerTlsMode.Stunnel)
        {
            TlsSetupStatusText.Text = CheckStunnelAvailability(logSuccess: false)
                ? "stunnel is available. Use Quick TLS Setup, then Start Server."
                : $"stunnel not found. {StunnelInstallHint}";
        }
    }

    private bool CheckStunnelAvailability(bool logSuccess)
    {
        var executable = FindStunnelExecutable();
        if (!string.IsNullOrWhiteSpace(executable))
        {
            TlsSetupStatusText.Text = $"stunnel found: {executable}";
            if (logSuccess)
            {
                AddLog($"stunnel found: {executable}");
            }

            return true;
        }

        TlsSetupStatusText.Text = $"stunnel not found. {StunnelInstallHint}";
        AddLog(TlsSetupStatusText.Text);
        return false;
    }

    private static string FindStunnelExecutable()
    {
        foreach (var name in new[] { "stunnel", "stunnel4" })
        {
            var executable = FindExecutable(name);
            if (!string.IsNullOrWhiteSpace(executable))
            {
                return executable;
            }
        }

        return "";
    }

    private static string FindExecutable(string name)
    {
        var command = OperatingSystem.IsWindows() ? "where" : "which";
        foreach (var candidate in OperatingSystem.IsWindows() && !Path.HasExtension(name)
                     ? new[] { $"{name}.cmd", $"{name}.exe", $"{name}.bat", name }
                     : new[] { name })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = command,
                    ArgumentList = { candidate },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (process is null)
                {
                    continue;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);
                if (process.ExitCode != 0)
                {
                    continue;
                }

                foreach (var path in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = path.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed)
                        && (!OperatingSystem.IsWindows() || IsWindowsExecutablePath(trimmed)))
                    {
                        return trimmed;
                    }
                }
            }
            catch
            {
            }
        }

        return "";
    }

    private static bool IsWindowsExecutablePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".com", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunProcessAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, string logPrefix)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        if (OperatingSystem.IsWindows()
            && (fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            var scriptPath = fileName;
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.ArgumentList.Add("/c");
            process.StartInfo.ArgumentList.Add(scriptPath);
        }

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Dispatcher.UIThread.Post(() => AddLog($"{logPrefix}: {args.Data}"));
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Dispatcher.UIThread.Post(() => AddLog($"{logPrefix}: {args.Data}"));
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}.");
        }
    }

    private static int ReadPort(NumericUpDown? control, int fallback)
    {
        return control?.Value.HasValue == true
            ? Math.Clamp((int)control.Value.Value, 1, 65535)
            : fallback;
    }

    private void AddLog(string message)
    {
        LogBox.Text = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}{LogBox.Text}";
    }

    private ServerTlsMode CurrentTlsMode => TlsModeBox.SelectedIndex switch
    {
        1 => ServerTlsMode.DotNetTls,
        2 => ServerTlsMode.Stunnel,
        _ => ServerTlsMode.None
    };

    private static string CertificateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NetBuddies",
        "ServerCertificates");

    private static string StunnelInstallHint => OperatingSystem.IsWindows()
        ? WindowsStunnelInstallHint
        : $"On MX/Linux Mint run: {LinuxStunnelInstallCommand}";

    private static string StunnelInstallClipboardText => OperatingSystem.IsWindows()
        ? WindowsStunnelInstallHint
        : LinuxStunnelInstallCommand;

    private static string NodeInstallHint => OperatingSystem.IsWindows()
        ? WindowsNodeInstallHint
        : $"On MX/Linux Mint run: {LinuxNodeInstallCommand}";

    private static string RealtimeGamesDirectory => Path.Combine(AppContext.BaseDirectory, "realtime-games");

    private static string WritableRealtimeGamesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NetBuddies",
        "RealtimeGames");

    private List<RealtimeGameInfo> RefreshRealtimeGamesList(bool logSuccess)
    {
        try
        {
            var realtimeDirectory = PrepareWritableRealtimeGamesDirectory();
            var gamesDirectory = Path.Combine(realtimeDirectory, "games");
            EnsureRealtimeGameTemplate(gamesDirectory);
            var games = LoadRealtimeGameManifests(gamesDirectory);
            RealtimeGamesBox.Text = games.Count == 0
                ? $"No modular games found in {gamesDirectory}.{Environment.NewLine}Use Open games to add a game folder with game.json."
                : string.Join(Environment.NewLine, games.Select(game => game.Summary));

            if (logSuccess)
            {
                AddLog(games.Count == 0
                    ? $"No modular real-time games found in {gamesDirectory}."
                    : $"Detected modular real-time games: {string.Join(", ", games.Select(game => game.Name))}");
            }

            return games;
        }
        catch (Exception ex)
        {
            RealtimeGamesBox.Text = $"Could not scan modular games: {ex.Message}";
            if (logSuccess)
            {
                AddLog(RealtimeGamesBox.Text);
            }

            return [];
        }
    }

    private static List<RealtimeGameInfo> LoadRealtimeGameManifests(string gamesDirectory)
    {
        if (!Directory.Exists(gamesDirectory))
        {
            return [];
        }

        var games = new List<RealtimeGameInfo>();
        foreach (var directory in Directory.EnumerateDirectories(gamesDirectory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(folderName) || folderName.StartsWith('_'))
            {
                continue;
            }

            var manifestPath = Path.Combine(directory, "game.json");
            if (!File.Exists(manifestPath))
            {
                games.Add(new RealtimeGameInfo(folderName, folderName, "missing game.json", "", "", directory));
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = document.RootElement;
                var id = ReadJsonString(root, "id", folderName);
                var name = ReadJsonString(root, "name", id);
                var runtime = ReadJsonString(root, "runtime", "realtime");
                var room = ReadJsonString(root, "room", "");
                var clientKind = ReadJsonString(root, "clientKind", "");
                games.Add(new RealtimeGameInfo(id, name, runtime, room, clientKind, directory));
            }
            catch (Exception ex)
            {
                games.Add(new RealtimeGameInfo(folderName, folderName, $"invalid game.json: {ex.Message}", "", "", directory));
            }
        }

        return games;
    }

    private static string ReadJsonString(JsonElement root, string propertyName, string fallback)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
    }

    private static void EnsureRealtimeGameTemplate(string gamesDirectory)
    {
        var templateDirectory = Path.Combine(gamesDirectory, "_GameFolderTemplate");
        var templatePath = Path.Combine(templateDirectory, "game.json");
        if (File.Exists(templatePath))
        {
            return;
        }

        Directory.CreateDirectory(templateDirectory);
        File.WriteAllText(templatePath, """
            {
              "id": "MyRealtimeGame",
              "name": "My Real-Time Game",
              "description": "Shows in the server GUI and client game list.",
              "runtime": "realtime",
              "room": "my_realtime_game",
              "clientKind": "generic-state",
              "serverPortOffset": 0,
              "icon": "icon.png"
            }
            """);
    }

    private static string PrepareWritableRealtimeGamesDirectory()
    {
        var sourceDirectory = RealtimeGamesDirectory;
        var targetDirectory = WritableRealtimeGamesDirectory;
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Bundled real-time game files were not found: {sourceDirectory}");
        }

        Directory.CreateDirectory(targetDirectory);
        CopyIfChanged(Path.Combine(sourceDirectory, "package.json"), Path.Combine(targetDirectory, "package.json"));
        CopyIfChanged(Path.Combine(sourceDirectory, "package-lock.json"), Path.Combine(targetDirectory, "package-lock.json"));
        CopyIfChanged(Path.Combine(sourceDirectory, "server.js"), Path.Combine(targetDirectory, "server.js"));
        CopyDirectoryIfChanged(Path.Combine(sourceDirectory, "games"), Path.Combine(targetDirectory, "games"));
        return targetDirectory;
    }

    private static void CopyDirectoryIfChanged(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            CopyIfChanged(sourcePath, Path.Combine(targetDirectory, relativePath));
        }
    }

    private static void CopyIfChanged(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        if (File.Exists(targetPath)
            && new FileInfo(sourcePath).Length == new FileInfo(targetPath).Length
            && File.GetLastWriteTimeUtc(sourcePath) <= File.GetLastWriteTimeUtc(targetPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private sealed record RealtimeGameInfo(
        string Id,
        string Name,
        string Runtime,
        string Room,
        string ClientKind,
        string FolderPath)
    {
        public string Summary
        {
            get
            {
                var room = string.IsNullOrWhiteSpace(Room) ? "no room" : Room;
                var clientKind = string.IsNullOrWhiteSpace(ClientKind) ? "default client" : ClientKind;
                return $"{Name} [{Id}] - {Runtime}, room: {room}, client: {clientKind}";
            }
        }
    }

    private enum ServerTlsMode
    {
        None,
        DotNetTls,
        Stunnel
    }
}
