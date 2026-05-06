using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using NetBuddies.App.Services;

namespace NetBuddies.App.Views;

public partial class GifPickerWindow : Window
{
    private readonly GiphyGifService _gifService = new();
    private readonly ObservableCollection<GifPickerItem> _items = [];

    public byte[]? SelectedGifBytes { get; private set; }
    public string SelectedGifTitle { get; private set; } = "";

    public GifPickerWindow()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _items;
        ApiKeyPanel.IsVisible = !_gifService.HasApiKey;
        StatusText.Text = _gifService.HasApiKey
            ? "Search GIPHY and pick a GIF to send."
            : "Paste your GIPHY API key once, then search.";
    }

    private async void Search_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            StatusText.Text = "Type something to search.";
            return;
        }

        _items.Clear();
        StatusText.Text = "Searching GIPHY...";

        try
        {
            var results = await _gifService.SearchAsync(query);
            foreach (var result in results)
            {
                _items.Add(new GifPickerItem(result, null));
            }

            StatusText.Text = results.Count == 0
                ? "No GIFs found."
                : "Pick a GIF to send.";
            _ = LoadPreviewsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task LoadPreviewsAsync()
    {
        foreach (var item in _items.ToArray())
        {
            try
            {
                var bytes = await _gifService.DownloadPreviewAsync(item.Source);
                using var stream = new MemoryStream(bytes);
                item.PreviewImage = new Bitmap(stream);
            }
            catch
            {
                item.PreviewImage = null;
            }
        }
    }

    private async void SendGif_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GifPickerItem item })
        {
            return;
        }

        StatusText.Text = "Downloading GIF...";
        try
        {
            SelectedGifBytes = await _gifService.DownloadGifAsync(item.Source);
            SelectedGifTitle = item.Title;
            Close(true);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not download GIF: {ex.Message}";
        }
    }

    private void SaveApiKey_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText.Text = "Paste a Tenor API key first.";
            return;
        }

        _gifService.SaveApiKey(apiKey);
        ApiKeyBox.Text = "";
        ApiKeyPanel.IsVisible = false;
        StatusText.Text = "GIPHY API key saved. Search for a GIF.";
    }
}

public sealed class GifPickerItem(GiphyGifItem source, Bitmap? previewImage) : ViewModels.ViewModelBase
{
    public GiphyGifItem Source { get; } = source;
    public string Title => Source.Title;

    private Bitmap? _previewImage = previewImage;
    public Bitmap? PreviewImage
    {
        get => _previewImage;
        set => SetProperty(ref _previewImage, value);
    }
}
