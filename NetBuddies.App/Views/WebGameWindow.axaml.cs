using Avalonia.Controls;
using NetBuddies.App.Services;
using NetBuddies.App.ViewModels;

namespace NetBuddies.App.Views;

public partial class WebGameWindow : Window
{
    private WebGameViewModel? _currentViewModel;

    public WebGameWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WebGameViewModel viewModel)
            {
                LoadGame(viewModel);
            }
        };
    }

    private void LoadGame(WebGameViewModel viewModel)
    {
        _currentViewModel = viewModel;
        viewModel.LaunchStatusText = "";
        GameHost.Children.Clear();
        FallbackPanel.IsVisible = false;

        var result = ElectronGameHostService.Launch(
            viewModel.Source,
            viewModel.Title,
            viewModel.ServerUrl,
            viewModel.AllowUntrustedGameTls);
        if (result.Success)
        {
            ShowStatus(result.Message);
            return;
        }

        ShowStatus(result.Message);
    }

    private void TryAgain_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_currentViewModel is not null)
        {
            LoadGame(_currentViewModel);
        }
    }

    private void ShowStatus(string message)
    {
        FallbackReasonText.Text = message;
        FallbackPanel.IsVisible = true;
        GameHost.Children.Clear();
        if (FallbackPanel.Parent is not null)
        {
            return;
        }

        GameHost.Children.Add(FallbackPanel);
    }
}
