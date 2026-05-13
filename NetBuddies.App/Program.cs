using Avalonia;
using System;
using System.IO;

namespace NetBuddies.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureWebView2UserDataFolder();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static void ConfigureWebView2UserDataFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBuddies",
            "WebView2");
        Directory.CreateDirectory(profilePath);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", profilePath);
    }
}
