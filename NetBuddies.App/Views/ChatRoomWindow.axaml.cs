using Avalonia.Controls;
using NetBuddies.App.ViewModels;

namespace NetBuddies.App.Views;

public partial class ChatRoomWindow : Window
{
    public ChatRoomWindow()
    {
        InitializeComponent();
        Closed += ChatRoomWindow_Closed;
    }

    private async void ChatRoomWindow_Closed(object? sender, EventArgs e)
    {
        if (DataContext is ChatRoomViewModel viewModel)
        {
            await viewModel.LeaveRoomAsync();
        }
    }
}
