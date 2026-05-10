using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using NetBuddies.App.Services;
using NetBuddies.App.ViewModels;

namespace NetBuddies.App.Views;

public partial class BuddyListWindow : Window
{
    private readonly Dictionary<ConversationViewModel, ChatWindow> _chatWindows = [];
    private readonly Dictionary<GameSessionViewModel, GameWindow> _gameWindows = [];
    private readonly Dictionary<RealtimePongViewModel, RealtimePongWindow> _realtimePongWindows = [];
    private readonly Dictionary<ChatRoomViewModel, ChatRoomWindow> _roomWindows = [];
    private readonly Dictionary<ScreenShareViewModel, ScreenShareWindow> _screenShareWindows = [];
    private Window? _signInWindow;
    private GamesLibraryWindow? _gamesLibraryWindow;

    public BuddyListWindow()
    {
        InitializeComponent();
        PopulateThemeMenu();
        ThemeMenu.PointerEntered += (_, _) => PopulateThemeMenu();
        DataContextChanged += (_, _) => HookViewModel();
        Closed += BuddyListWindow_Closed;
    }

    public BuddyListWindow(Window signInWindow)
        : this()
    {
        _signInWindow = signInWindow;
    }

    private void HookViewModel()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenConversationRequested -= OpenConversation;
            viewModel.OpenConversationRequested += OpenConversation;
            viewModel.OpenGameRequested -= OpenGame;
            viewModel.OpenGameRequested += OpenGame;
            viewModel.OpenRealtimePongRequested -= OpenRealtimePong;
            viewModel.OpenRealtimePongRequested += OpenRealtimePong;
            viewModel.OpenRoomRequested -= OpenRoom;
            viewModel.OpenRoomRequested += OpenRoom;
            viewModel.OpenScreenShareRequested -= OpenScreenShare;
            viewModel.OpenScreenShareRequested += OpenScreenShare;
        }
    }

    private void OpenConversation(ConversationViewModel conversation)
    {
        var needsAttention = conversation.HasPendingAttention;
        if (_chatWindows.TryGetValue(conversation, out var existing))
        {
            if (!needsAttention)
            {
                existing.Activate();
            }

            return;
        }

        var window = new ChatWindow
        {
            DataContext = conversation
        };

        window.Closed += (_, _) => _chatWindows.Remove(conversation);
        _chatWindows[conversation] = window;
        window.Show();
        if (!needsAttention)
        {
            window.Activate();
        }
    }

    private void OpenRoom(ChatRoomViewModel room)
    {
        if (_roomWindows.TryGetValue(room, out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new ChatRoomWindow
        {
            DataContext = room
        };

        window.Closed += (_, _) => _roomWindows.Remove(room);
        _roomWindows[room] = window;
        window.Show();
        window.Activate();
    }

    private void OpenGame(GameSessionViewModel game)
    {
        if (_gameWindows.TryGetValue(game, out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new GameWindow
        {
            DataContext = game
        };

        void CloseRequested(GameSessionViewModel closingGame)
        {
            if (_gameWindows.TryGetValue(closingGame, out var openWindow))
            {
                openWindow.Close();
            }
        }

        game.CloseRequested += CloseRequested;
        window.Closed += (_, _) =>
        {
            game.CloseRequested -= CloseRequested;
            _gameWindows.Remove(game);
        };
        _gameWindows[game] = window;
        window.Show();
        window.Activate();
    }

    private void OpenRealtimePong(RealtimePongViewModel game)
    {
        if (_realtimePongWindows.TryGetValue(game, out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new RealtimePongWindow
        {
            DataContext = game
        };
        window.Closed += (_, _) => _realtimePongWindows.Remove(game);
        _realtimePongWindows[game] = window;
        window.Show();
        window.Activate();
    }

    private void OpenScreenShare(ScreenShareViewModel screenShare)
    {
        if (_screenShareWindows.TryGetValue(screenShare, out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new ScreenShareWindow
        {
            DataContext = screenShare
        };

        window.Closed += (_, _) => _screenShareWindows.Remove(screenShare);
        _screenShareWindows[screenShare] = window;
        window.Show();
        window.Activate();
    }

    private void BuddyList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenChatCommand.Execute(viewModel.SelectedBuddy);
        }
    }

    private void BuddyItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is Control { DataContext: BuddyViewModel buddy })
        {
            viewModel.SelectedBuddy = buddy;
            BuddyList.SelectedItem = buddy;
        }
    }

    private async void CreateChatRoom_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.CreateChatRoomAsync();
        }
    }

    private async void JoinRoom_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var room = sender is Control { DataContext: ChatRoomListItemViewModel item }
                ? item
                : viewModel.SelectedRoom;
            viewModel.SelectedRoom = room;
            await viewModel.JoinRoomAsync(room);
        }
    }

    private async void RoomList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.JoinRoomAsync(viewModel.SelectedRoom);
        }
    }

    private void GamesLibrary_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_gamesLibraryWindow is not null)
        {
            _gamesLibraryWindow.Activate();
            return;
        }

        _gamesLibraryWindow = new GamesLibraryWindow();
        _gamesLibraryWindow.Closed += (_, _) => _gamesLibraryWindow = null;
        _gamesLibraryWindow.Show(this);
        _gamesLibraryWindow.Activate();
    }

    private void SendMessage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && TryGetBuddy(sender, viewModel, out var buddy))
        {
            viewModel.SelectedBuddy = buddy;
            viewModel.OpenChatCommand.Execute(buddy);
        }
    }

    private void Nudge_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && TryGetBuddy(sender, viewModel, out var buddy))
        {
            viewModel.SelectedBuddy = buddy;
            viewModel.SendNudgeToCommand.Execute(buddy);
        }
    }

    private async void SendFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !TryGetBuddy(sender, viewModel, out var buddy))
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Send a file to {buddy.Name}",
            AllowMultiple = false
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            viewModel.SelectedBuddy = buddy;
            await viewModel.SendFileToAsync(buddy, path);
        }
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

    private async void PlayTicTacToe_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await StartGameFromMenuAsync(sender, NetBuddiesGameType.TicTacToe);
    }

    private async void PlayCheckers_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await StartGameFromMenuAsync(sender, NetBuddiesGameType.Checkers);
    }

    private async void PlayMinesweeper_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await StartGameFromMenuAsync(sender, NetBuddiesGameType.MinesweeperFlags);
    }

    private async void PlayBuddyPong_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await StartGameFromMenuAsync(sender, NetBuddiesGameType.BuddyPong);
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

    private async Task StartGameFromMenuAsync(object? sender, NetBuddiesGameType gameType)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var buddy = TryGetBuddy(sender, viewModel, out var menuBuddy)
            ? menuBuddy
            : viewModel.SelectedBuddy;

        if (buddy is null)
        {
            return;
        }

        viewModel.SelectedBuddy = buddy;
        await viewModel.StartGameAsync(buddy, gameType);
    }

    private static bool TryGetBuddy(object? sender, out BuddyViewModel buddy)
    {
        return TryGetBuddy(sender, null, out buddy);
    }

    private static bool TryGetBuddy(object? sender, MainWindowViewModel? viewModel, out BuddyViewModel buddy)
    {
        buddy = null!;

        if (sender is Control { DataContext: BuddyViewModel senderBuddy })
        {
            buddy = senderBuddy;
            return true;
        }

        if (sender is MenuItem menuItem
            && TryGetBuddyFromMenuItem(menuItem, out var targetBuddy))
        {
            buddy = targetBuddy;
            return true;
        }

        if (viewModel?.SelectedBuddy is not null)
        {
            buddy = viewModel.SelectedBuddy;
            return true;
        }

        return false;
    }

    private static bool TryGetBuddyFromMenuItem(MenuItem menuItem, out BuddyViewModel buddy)
    {
        buddy = null!;
        var current = menuItem.Parent;
        while (current is not null)
        {
            switch (current)
            {
                case ContextMenu { PlacementTarget.DataContext: BuddyViewModel targetBuddy }:
                    buddy = targetBuddy;
                    return true;
                case MenuItem parentMenu:
                    current = parentMenu.Parent;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    private async void BuddyListWindow_Closed(object? sender, EventArgs e)
    {
        foreach (var chatWindow in _chatWindows.Values.ToArray())
        {
            chatWindow.Close();
        }

        foreach (var gameWindow in _gameWindows.Values.ToArray())
        {
            gameWindow.Close();
        }

        foreach (var pongWindow in _realtimePongWindows.Values.ToArray())
        {
            pongWindow.Close();
        }

        foreach (var roomWindow in _roomWindows.Values.ToArray())
        {
            roomWindow.Close();
        }

        foreach (var screenShareWindow in _screenShareWindows.Values.ToArray())
        {
            screenShareWindow.Close();
        }

        _gamesLibraryWindow?.Close();

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenConversationRequested -= OpenConversation;
            viewModel.OpenGameRequested -= OpenGame;
            viewModel.OpenRealtimePongRequested -= OpenRealtimePong;
            viewModel.OpenRoomRequested -= OpenRoom;
            viewModel.OpenScreenShareRequested -= OpenScreenShare;
            await viewModel.DisconnectCommand.ExecuteAsync(null);
        }

        _signInWindow?.Show();
        _signInWindow?.Activate();
    }
}
