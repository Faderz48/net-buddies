using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using NetBuddies.App.ViewModels;

namespace NetBuddies.App.Views;

public partial class ChatRoomWindow : Window
{
    private ChatRoomViewModel? _hookedViewModel;

    public ChatRoomWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookViewModel();
        Closed += ChatRoomWindow_Closed;
    }

    private void HookViewModel()
    {
        if (_hookedViewModel is not null)
        {
            _hookedViewModel.Messages.CollectionChanged -= Messages_CollectionChanged;
        }

        _hookedViewModel = DataContext as ChatRoomViewModel;
        if (_hookedViewModel is not null)
        {
            _hookedViewModel.Messages.CollectionChanged += Messages_CollectionChanged;
        }
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
        {
            Dispatcher.UIThread.Post(() => MessageScrollViewer.ScrollToEnd(), DispatcherPriority.Background);
        }
    }

    private void MessageInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        if (DataContext is ChatRoomViewModel viewModel && viewModel.SendMessageCommand.CanExecute(null))
        {
            e.Handled = true;
            viewModel.SendMessageCommand.Execute(null);
        }
    }

    private async void ChatRoomWindow_Closed(object? sender, EventArgs e)
    {
        if (_hookedViewModel is not null)
        {
            _hookedViewModel.Messages.CollectionChanged -= Messages_CollectionChanged;
            _hookedViewModel = null;
        }

        if (DataContext is ChatRoomViewModel viewModel)
        {
            await viewModel.LeaveRoomAsync();
        }
    }
}
