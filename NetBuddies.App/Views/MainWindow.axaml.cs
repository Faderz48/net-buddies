using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using NetBuddies.App.Services;
using NetBuddies.App.ViewModels;

namespace NetBuddies.App.Views;

public partial class MainWindow : Window
{
    private BuddyListWindow? _buddyListWindow;

    public MainWindow()
    {
        InitializeComponent();
        PopulateThemeMenu();
        ThemeMenu.PointerEntered += (_, _) => PopulateThemeMenu();
        DataContextChanged += (_, _) => HookViewModel();
    }

    private void HookViewModel()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenBuddyListRequested -= OpenBuddyList;
            viewModel.OpenBuddyListRequested += OpenBuddyList;
        }
    }

    private void OpenBuddyList()
    {
        if (_buddyListWindow is not null)
        {
            _buddyListWindow.Activate();
            return;
        }

        _buddyListWindow = new BuddyListWindow(this)
        {
            DataContext = DataContext
        };

        _buddyListWindow.Closed += (_, _) => _buddyListWindow = null;
        _buddyListWindow.Show();
        Hide();
    }

    private async void ChangePicture_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a profile picture",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.SetProfilePictureAsync(path);
        }
    }

    private async void SelectCertificate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose TLS certificate",
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
            viewModel.SetCertificatePath(path);
        }
    }

    private void GenerateCertificate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.GenerateTlsCertificate();
        }
    }

    private void PopulateThemeMenu()
    {
        ThemeMenu.ItemsSource = AppThemeService.DiscoverThemes()
            .Select(theme =>
            {
                var menuItem = new MenuItem
                {
                    Header = theme.Name.Equals(AppThemeService.CurrentThemeName, StringComparison.OrdinalIgnoreCase)
                        ? $"{theme.DisplayName} ✓"
                        : theme.DisplayName,
                    Tag = theme.Name
                };
                menuItem.Click += Theme_Click;
                return menuItem;
            })
            .ToList();
    }

    private void Theme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string themeName })
        {
            AppThemeService.SetTheme(themeName);
            PopulateThemeMenu();
        }
    }

    private async void ThemeCreator_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var creator = new ThemeCreatorWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        await creator.ShowDialog<bool?>(this);
        PopulateThemeMenu();
    }
}
