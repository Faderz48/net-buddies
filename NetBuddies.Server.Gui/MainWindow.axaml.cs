using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
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
    private readonly BuddyServer _server = new();
    private Process? _stunnelProcess;
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
                TlsSetupStatusText.Text = $"Install stunnel first on MX/Linux Mint: {LinuxStunnelInstallCommand}";
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
            await Clipboard.SetTextAsync(LinuxStunnelInstallCommand);
            AddLog("Copied stunnel install command.");
            TlsSetupStatusText.Text = $"Copied: {LinuxStunnelInstallCommand}";
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

    private void StartStunnel(int serverPort, int publicTlsPort)
    {
        var stunnelExecutable = FindStunnelExecutable();
        if (string.IsNullOrWhiteSpace(stunnelExecutable))
        {
            throw new InvalidOperationException($"stunnel is not installed or not on PATH. On MX/Linux Mint run: {LinuxStunnelInstallCommand}");
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

    private async Task StopEverythingAsync()
    {
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
                : $"stunnel not found. Install it with: {LinuxStunnelInstallCommand}";
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

        TlsSetupStatusText.Text = $"stunnel not found. On MX/Linux Mint run: {LinuxStunnelInstallCommand}";
        AddLog(TlsSetupStatusText.Text);
        return false;
    }

    private static string FindStunnelExecutable()
    {
        foreach (var name in new[] { "stunnel", "stunnel4" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "where" : "which",
                    ArgumentList = { name },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (process is null)
                {
                    continue;
                }

                process.WaitForExit(1000);
                var path = process.StandardOutput.ReadLine();
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(path))
                {
                    return name;
                }
            }
            catch
            {
            }
        }

        return "";
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

    private enum ServerTlsMode
    {
        None,
        DotNetTls,
        Stunnel
    }
}
