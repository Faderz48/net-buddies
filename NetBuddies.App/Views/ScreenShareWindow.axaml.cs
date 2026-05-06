using Avalonia.Controls;
using NetBuddies.App.ViewModels;

namespace NetBuddies.App.Views;

public partial class ScreenShareWindow : Window
{
    public ScreenShareWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            if (DataContext is ScreenShareViewModel viewModel)
            {
                _ = viewModel.StopAsync();
                viewModel.Dispose();
            }
        };
    }
}
