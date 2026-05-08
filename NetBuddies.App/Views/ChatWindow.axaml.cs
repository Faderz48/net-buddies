using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using NetBuddies.App.Services;
using NetBuddies.App.ViewModels;

namespace NetBuddies.App.Views;

public partial class ChatWindow : Window
{
    private ConversationViewModel? _hookedViewModel;

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
        UnhookViewModel();
        if (DataContext is ConversationViewModel viewModel)
        {
            _hookedViewModel = viewModel;
            viewModel.NudgeReceived -= ShakeWindow;
            viewModel.NudgeReceived += ShakeWindow;
            viewModel.AttentionRequested -= HandleAttentionRequested;
            viewModel.AttentionRequested += HandleAttentionRequested;
            viewModel.DownloadSaved -= ShowDownloadSavedPopup;
            viewModel.DownloadSaved += ShowDownloadSavedPopup;
            viewModel.Messages.CollectionChanged += Messages_CollectionChanged;

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
        if (_hookedViewModel is ConversationViewModel viewModel)
        {
            viewModel.NudgeReceived -= ShakeWindow;
            viewModel.AttentionRequested -= HandleAttentionRequested;
            viewModel.DownloadSaved -= ShowDownloadSavedPopup;
            viewModel.Messages.CollectionChanged -= Messages_CollectionChanged;
            _hookedViewModel = null;
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

        if (DataContext is ConversationViewModel viewModel && viewModel.SendMessageCommand.CanExecute(null))
        {
            e.Handled = true;
            viewModel.SendMessageCommand.Execute(null);
        }
    }

    private void ShowDownloadSavedPopup(string path)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var folder = Path.GetDirectoryName(path) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var fileName = Path.GetFileName(path);
            var dialog = new Window
            {
                Title = "Net Buddies download",
                Width = 420,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Saved {fileName}",
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = folder,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            CreateOpenFolderButton(folder),
                            CreateCloseButton(dialog)
                        }
                    }
                }
            };

            await dialog.ShowDialog(this);
        });
    }

    private static Button CreateOpenFolderButton(string folder)
    {
        var button = new Button { Content = "Open Folder" };
        button.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        };
        return button;
    }

    private static Button CreateCloseButton(Window dialog)
    {
        var button = new Button { Content = "Close" };
        button.Click += (_, _) => dialog.Close();
        return button;
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
