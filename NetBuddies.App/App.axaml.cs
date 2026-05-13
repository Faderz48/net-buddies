using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using NetBuddies.App.Services;
using NetBuddies.App.ViewModels;
using NetBuddies.App.Views;

namespace NetBuddies.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += UiThread_UnhandledException;
        AppThemeService.ApplySavedTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void UiThread_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (!IsWebViewStartupFailure(e.Exception))
        {
            return;
        }

        e.Handled = true;
        ShowWebViewFallback(e.Exception);
    }

    private static bool IsWebViewStartupFailure(Exception exception)
    {
        return exception is UnauthorizedAccessException
            && (exception.StackTrace?.Contains("Avalonia.Controls.WebView", StringComparison.Ordinal) ?? false);
    }

    private static void ShowWebViewFallback(Exception exception)
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        foreach (var window in desktop.Windows.OfType<WebGameWindow>())
        {
            window.ShowWebViewStartupFailure(exception.Message);
        }
    }
}
