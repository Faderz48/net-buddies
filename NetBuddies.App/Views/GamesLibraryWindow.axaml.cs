using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NetBuddies.App.Services;
using NetBuddies.Core;

namespace NetBuddies.App.Views;

public partial class GamesLibraryWindow : Window
{
    public GamesLibraryWindow()
    {
        InitializeComponent();
        RefreshGames();
    }

    private void RefreshGames()
    {
        DataContext = GameCatalogService.LoadGames();
    }

    private void OpenGamesFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Directory.CreateDirectory(GameAssetService.UserGamesFolder);
        GameCatalogService.CreateExampleManifest(Path.Combine(GameAssetService.UserGamesFolder, "_GameFolderTemplate"));
        Process.Start(new ProcessStartInfo
        {
            FileName = GameAssetService.UserGamesFolder,
            UseShellExecute = true
        });
    }

    private async void InstallAddon_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a Net Buddies game add-on zip",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Net Buddies game add-on")
                {
                    Patterns = ["*.zip"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var result = GameAddonInstaller.InstallFromZip(path, GameAssetService.UserGamesFolder);
            RefreshGames();
            await ShowMessageAsync(
                "Game Add-on Installed",
                $"{result.Name} was installed for this client.\n\nInstall the same add-on on the server GUI too if it includes multiplayer server logic.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Game Add-on Failed", ex.Message);
        }
    }

    private void Refresh_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RefreshGames();
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 190,
            MinWidth = 360,
            MinHeight = 160,
            Content = new Border
            {
                Padding = new Avalonia.Thickness(16),
                Background = Avalonia.Application.Current?.Resources["NbPanelBrush"] as Avalonia.Media.IBrush,
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new Button
                        {
                            Content = "OK",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            MinWidth = 80
                        }
                    }
                }
            }
        };
        if (((dialog.Content as Border)?.Child as StackPanel)?.Children.LastOrDefault() is Button ok)
        {
            ok.Click += (_, _) => dialog.Close();
        }

        await dialog.ShowDialog(this);
    }
}
