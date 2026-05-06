using Avalonia.Controls;
using NetBuddies.App.Services;

namespace NetBuddies.App.Views;

public partial class ScreenSharePickerWindow : Window
{
    public ScreenShareSource? SelectedSource { get; private set; }
    public int SelectedQuality { get; private set; } = 720;
    public int SelectedFrameRate { get; private set; } = 15;
    public int SelectedJpegQuality { get; private set; } = 72;

    public ScreenSharePickerWindow()
    {
        InitializeComponent();
        SourceBox.ItemsSource = ScreenCaptureService.GetSources();
        SourceBox.SelectedIndex = 0;
        QualityBox.SelectedIndex = 0;
        StatusText.Text = OperatingSystem.IsWindows()
            ? "The other buddy must accept before sharing starts."
            : "Screen capture currently needs a Windows client.";
    }

    private void SendInvite_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SelectedSource = SourceBox.SelectedItem as ScreenShareSource;
        if (QualityBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            var parts = tag.Split('|');
            if (parts.Length >= 3
                && int.TryParse(parts[0], out var quality)
                && int.TryParse(parts[1], out var frameRate)
                && int.TryParse(parts[2], out var jpegQuality))
            {
                SelectedQuality = quality;
                SelectedFrameRate = frameRate;
                SelectedJpegQuality = jpegQuality;
            }
        }

        if (SelectedSource is null)
        {
            StatusText.Text = "Select a source first.";
            return;
        }

        Close(true);
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }
}
