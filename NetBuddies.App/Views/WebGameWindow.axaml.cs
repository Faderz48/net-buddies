using Avalonia.Controls;
using NetBuddies.App.ViewModels;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace NetBuddies.App.Views;

public partial class WebGameWindow : Window
{
    private WebGameViewModel? _currentViewModel;

    public WebGameWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WebGameViewModel viewModel)
            {
                LoadGame(viewModel);
            }
        };
    }

    private void LoadGame(WebGameViewModel viewModel)
    {
        _currentViewModel = viewModel;
        GameHost.Children.Clear();
        FallbackPanel.IsVisible = false;
        InstallRuntimeButton.IsVisible = false;
        FallbackActionText.Text = "";

        if (ShouldForceExternalWebGames())
        {
            ShowFallback("Net Buddies is set to use external web games on this machine.", canInstallRuntime: false);
            return;
        }

        var unavailableReason = GetWebViewUnavailableReason();
        if (!string.IsNullOrWhiteSpace(unavailableReason))
        {
            ShowFallback(unavailableReason, canInstallRuntime: OperatingSystem.IsWindows());
            return;
        }

        try
        {
            var webView = new NativeWebView
            {
                Source = viewModel.Source
            };
            webView.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    ShowFallback("The in-app game renderer could not load the game page.", canInstallRuntime: false);
                }
            };
            GameHost.Children.Add(webView);
        }
        catch (Exception ex)
        {
            ShowFallback($"The in-app game renderer could not start: {ex.Message}", canInstallRuntime: OperatingSystem.IsWindows());
        }
    }

    private static bool ShouldForceExternalWebGames()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("NETBUDDIES_EXTERNAL_WEB_GAMES"),
            "1",
            StringComparison.Ordinal);
    }

    private void ShowFallback(string reason, bool canInstallRuntime)
    {
        FallbackReasonText.Text = reason;
        InstallRuntimeButton.IsVisible = canInstallRuntime;
        FallbackPanel.IsVisible = true;
        GameHost.Children.Clear();
        GameHost.Children.Add(FallbackPanel);
    }

    public void ShowWebViewStartupFailure(string reason)
    {
        ShowFallback($"The in-app game renderer could not start: {reason}", canInstallRuntime: OperatingSystem.IsWindows());
    }

    private static string GetWebViewUnavailableReason()
    {
        if (OperatingSystem.IsWindows() && !IsWebView2RuntimeInstalled())
        {
            return "Microsoft Edge WebView2 Runtime is not installed. Install it, then click Try Again to run the game inside Net Buddies.";
        }

        return "";
    }

    private void TryAgain_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_currentViewModel is not null)
        {
            LoadGame(_currentViewModel);
        }
    }

    private void InstallRuntime_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            FallbackActionText.Text = "The WebView2 runtime installer is only used on Windows.";
            return;
        }

        var installer = FindBundledWebView2Installer();
        if (string.IsNullOrWhiteSpace(installer))
        {
            FallbackActionText.Text = "This build does not include the WebView2 runtime installer. Install Net Buddies with the latest setup EXE, then try the game again.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installer,
                Arguments = "/silent /install",
                UseShellExecute = true,
                Verb = "runas"
            });
            FallbackActionText.Text = "WebView2 Runtime installer started. When it finishes, click Try Again.";
        }
        catch (Exception ex)
        {
            FallbackActionText.Text = $"Could not start the WebView2 installer: {ex.Message}";
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWebView2RuntimeInstalled()
    {
        if (File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft",
                "EdgeWebView",
                "Application",
                "msedgewebview2.exe")))
        {
            return true;
        }

        if (File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft",
                "EdgeWebView",
                "Application",
                "msedgewebview2.exe")))
        {
            return true;
        }

        return IsWebView2RuntimeRegistered();
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWebView2RuntimeRegistered()
    {
        return RegistryKeyExists(@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}")
            || RegistryKeyExists(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}")
            || RegistryKeyExists(@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F1E7DD7E-2C2E-4F7B-8D74-D2F34F8E5E3A}")
            || RegistryKeyExists(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F1E7DD7E-2C2E-4F7B-8D74-D2F34F8E5E3A}");
    }

    [SupportedOSPlatform("windows")]
    private static bool RegistryKeyExists(string path)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key is not null;
    }

    private static string FindBundledWebView2Installer()
    {
        foreach (var relativePath in new[] { "MicrosoftEdgeWebview2Setup.exe", Path.Combine("WebView2", "MicrosoftEdgeWebview2Setup.exe") })
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return "";
    }
}
