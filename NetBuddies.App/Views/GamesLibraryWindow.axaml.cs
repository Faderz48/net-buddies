using Avalonia.Controls;
using NetBuddies.App.Services;

namespace NetBuddies.App.Views;

public partial class GamesLibraryWindow : Window
{
    public GamesLibraryWindow()
    {
        InitializeComponent();
        TicTacToeIcon.Source = GameAssetService.Load("TicTacToe/icon.png");
        CheckersIcon.Source = GameAssetService.Load("Checkers/icon.png");
        MinesweeperFlagsIcon.Source = GameAssetService.Load("MinesweeperFlags/icon.png");
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
