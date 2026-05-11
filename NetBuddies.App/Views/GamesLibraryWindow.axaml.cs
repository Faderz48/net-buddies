using System.Diagnostics;
using Avalonia.Controls;
using NetBuddies.App.Services;

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

    private void Refresh_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RefreshGames();
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
