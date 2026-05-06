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
    private readonly Dictionary<ChatRoomViewModel, ChatRoomWindow> _roomWindows = [];
    private readonly Dictionary<ScreenShareViewModel, ScreenShareWindow> _screenShareWindows = [];
    private Window? _signInWindow;

    public BuddyListWindow()
    {
        InitializeComponent();
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

    private void SendMessage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && TryGetBuddy(sender, out var buddy))
        {
            viewModel.SelectedBuddy = buddy;
            viewModel.OpenChatCommand.Execute(buddy);
        }
    }

    private void Nudge_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && TryGetBuddy(sender, out var buddy))
        {
            viewModel.SelectedBuddy = buddy;
            viewModel.SendNudgeToCommand.Execute(buddy);
        }
    }

    private async void SendFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !TryGetBuddy(sender, out var buddy))
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

    private void LightTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AppThemeService.SetTheme(AppThemeService.LightTheme);
    }

    private void DarkTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AppThemeService.SetTheme(AppThemeService.DarkTheme);
    }

    private async Task StartGameFromMenuAsync(object? sender, NetBuddiesGameType gameType)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var buddy = TryGetBuddy(sender, out var menuBuddy)
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
        buddy = null!;

        if (sender is Control { DataContext: BuddyViewModel senderBuddy })
        {
            buddy = senderBuddy;
            return true;
        }

        if (sender is MenuItem menuItem
            && menuItem.Parent is ContextMenu { PlacementTarget.DataContext: BuddyViewModel targetBuddy })
        {
            buddy = targetBuddy;
            return true;
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

        foreach (var roomWindow in _roomWindows.Values.ToArray())
        {
            roomWindow.Close();
        }

        foreach (var screenShareWindow in _screenShareWindows.Values.ToArray())
        {
            screenShareWindow.Close();
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenConversationRequested -= OpenConversation;
            viewModel.OpenGameRequested -= OpenGame;
            viewModel.OpenRoomRequested -= OpenRoom;
            viewModel.OpenScreenShareRequested -= OpenScreenShare;
            await viewModel.DisconnectCommand.ExecuteAsync(null);
        }

        _signInWindow?.Show();
        _signInWindow?.Activate();
    }
}
