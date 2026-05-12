using Avalonia.Controls;
using NetBuddies.App.ViewModels;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace NetBuddies.App.Views;

public partial class WebGameWindow : Window
{
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
        GameHost.Children.Clear();
        FallbackPanel.IsVisible = false;

        if (!ShouldUseEmbeddedWebView())
        {
            ShowFallback("Net Buddies is using the safer external game window mode to avoid an embedded browser crash.");
            viewModel.OpenExternallyCommand.Execute(null);
            return;
        }

        var unavailableReason = GetWebViewUnavailableReason();
        if (!string.IsNullOrWhiteSpace(unavailableReason))
        {
            ShowFallback(unavailableReason);
            viewModel.OpenExternallyCommand.Execute(null);
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
                    ShowFallback("The embedded browser could not load the game page.");
                }
            };
            GameHost.Children.Add(webView);
        }
        catch (Exception ex)
        {
            ShowFallback(ex.Message);
        }
    }

    private static bool ShouldUseEmbeddedWebView()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("NETBUDDIES_EMBEDDED_WEB_GAMES"),
            "1",
            StringComparison.Ordinal);
    }

    private void ShowFallback(string reason)
    {
        FallbackReasonText.Text = reason;
        FallbackPanel.IsVisible = true;
        GameHost.Children.Clear();
        GameHost.Children.Add(FallbackPanel);
    }

    private static string GetWebViewUnavailableReason()
    {
        if (OperatingSystem.IsWindows() && !IsWebView2RuntimeInstalled())
        {
            return "Microsoft Edge WebView2 Runtime is not installed. Install WebView2 Runtime, or open the game externally.";
        }

        return "";
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
        return RegistryKeyExists(@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F1E7DD7E-2C2E-4F7B-8D74-D2F34F8E5E3A}")
            || RegistryKeyExists(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F1E7DD7E-2C2E-4F7B-8D74-D2F34F8E5E3A}");
    }

    [SupportedOSPlatform("windows")]
    private static bool RegistryKeyExists(string path)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key is not null;
    }
}
