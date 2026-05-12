using Avalonia.Controls;
using NetBuddies.App.ViewModels;

namespace NetBuddies.App.Views;

public partial class WebGameWindow : Window
{
    public WebGameWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WebGameViewModel viewModel)
            {
                GameWebView.Source = viewModel.Source;
            }
        };
    }
}
