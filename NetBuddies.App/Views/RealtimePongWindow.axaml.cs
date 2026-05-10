using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using NetBuddies.App.ViewModels;

namespace NetBuddies.App.Views;

public partial class RealtimePongWindow : Window
{
    public static readonly IValueConverter PaddlePositionConverter = new PercentPositionConverter(360, 92);
    public static readonly IValueConverter BallXConverter = new PercentPositionConverter(700, 22);
    public static readonly IValueConverter BallYConverter = new PercentPositionConverter(360, 22);

    public RealtimePongWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is RealtimePongViewModel viewModel)
            {
                await viewModel.ConnectCommand.ExecuteAsync(null);
            }

            Focus();
        };
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Closed += async (_, _) =>
        {
            if (DataContext is RealtimePongViewModel viewModel)
            {
                await viewModel.DisposeAsync();
            }
        };
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not RealtimePongViewModel viewModel)
        {
            return;
        }

        if (e.Key is Key.W or Key.Up)
        {
            await viewModel.SetMoveAsync(-1);
        }
        else if (e.Key is Key.S or Key.Down)
        {
            await viewModel.SetMoveAsync(1);
        }
    }

    private async void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is RealtimePongViewModel viewModel
            && e.Key is Key.W or Key.Up or Key.S or Key.Down)
        {
            await viewModel.SetMoveAsync(0);
        }
    }

    private sealed class PercentPositionConverter(double boardSize, double objectSize) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var percent = value is double numeric ? numeric : 50;
            return Math.Clamp(percent / 100 * boardSize - objectSize / 2, 0, boardSize - objectSize);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
