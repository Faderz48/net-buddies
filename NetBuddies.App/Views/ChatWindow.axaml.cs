using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using NetBuddies.App.Services;
using NetBuddies.App.ViewModels;

namespace NetBuddies.App.Views;

public partial class ChatWindow : Window
{
    public ChatWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookViewModel();
        Activated += (_, _) => ChatAttentionService.StopFlashing(this);
        Closed += (_, _) =>
        {
            UnhookViewModel();
            if (DataContext is ConversationViewModel viewModel)
            {
                viewModel.Dispose();
            }
        };
    }

    private void HookViewModel()
    {
        if (DataContext is ConversationViewModel viewModel)
        {
            viewModel.NudgeReceived -= ShakeWindow;
            viewModel.NudgeReceived += ShakeWindow;
            viewModel.AttentionRequested -= HandleAttentionRequested;
            viewModel.AttentionRequested += HandleAttentionRequested;

            Dispatcher.UIThread.Post(() =>
            {
                var kind = viewModel.ConsumePendingAttention();
                if (kind is not null)
                {
                    HandleAttentionRequested(kind.Value);
                }
            }, DispatcherPriority.Background);
        }
    }

    private void UnhookViewModel()
    {
        if (DataContext is ConversationViewModel viewModel)
        {
            viewModel.NudgeReceived -= ShakeWindow;
            viewModel.AttentionRequested -= HandleAttentionRequested;
        }
    }

    private void HandleAttentionRequested(ChatAttentionKind kind)
    {
        if (kind == ChatAttentionKind.Nudge)
        {
            ChatAttentionService.PlayNudgeSound();
        }
        else
        {
            ChatAttentionService.PlayMessageSound();
        }

        ChatAttentionService.StartFlashing(this);
    }

    private async void ShakeWindow()
    {
        var original = Position;
        var offsets = new[] { -12, 12, -9, 9, -6, 6, 0 };

        foreach (var offset in offsets)
        {
            Position = original.WithX(original.X + offset);
            await Task.Delay(35);
        }

        Position = original;
        Dispatcher.UIThread.Post(Activate);
    }

    private async void SendFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Send a file",
            AllowMultiple = false
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.SendFileAsync(path);
        }
    }

    private async void AttachImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Send an image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.SendImageAsync(path);
        }
    }

    private async void Gif_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel viewModel)
        {
            return;
        }

        var picker = new GifPickerWindow();
        var accepted = await picker.ShowDialog<bool?>(this);
        if (accepted == true && picker.SelectedGifBytes is { Length: > 0 } gifBytes)
        {
            await viewModel.SendGifAsync(picker.SelectedGifTitle, gifBytes);
        }
    }

    private async void ScreenShare_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel viewModel)
        {
            return;
        }

        var picker = new ScreenSharePickerWindow();
        var accepted = await picker.ShowDialog<bool?>(this);
        if (accepted == true && picker.SelectedSource is not null)
        {
            viewModel.RequestScreenShare(
                picker.SelectedSource,
                picker.SelectedQuality,
                picker.SelectedFrameRate,
                picker.SelectedJpegQuality);
        }
    }
}
