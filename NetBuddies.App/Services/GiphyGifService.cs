using System.Text.Json;

namespace NetBuddies.App.Services;

public sealed record GiphyGifItem(string Title, string PreviewUrl, string GifUrl);

public sealed class GiphyGifService
{
    private static readonly HttpClient HttpClient = new();
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NetBuddies",
        "giphy-key.txt");

    public string ApiKey { get; set; } = LoadApiKey();

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<IReadOnlyList<GiphyGifItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var apiKey = ApiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Paste a GIPHY API key to use GIF search.");
        }

        var uri = new UriBuilder("https://api.giphy.com/v1/gifs/search")
        {
            Query = string.Join("&",
                $"api_key={Uri.EscapeDataString(apiKey)}",
                $"q={Uri.EscapeDataString(query)}",
                "limit=16",
                "rating=pg-13",
                "lang=en",
                "bundle=messaging_non_clips")
        }.Uri;

        using var response = await HttpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<GiphyGifItem>();
        foreach (var result in results.EnumerateArray())
        {
            var title = result.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString() ?? "GIPHY GIF"
                : "GIPHY GIF";
            if (!result.TryGetProperty("images", out var images))
            {
                continue;
            }

            var previewUrl = ReadImageUrl(images, "fixed_width_small");
            var gifUrl = ReadImageUrl(images, "downsized");
            if (string.IsNullOrWhiteSpace(gifUrl))
            {
                gifUrl = ReadImageUrl(images, "original");
            }

            if (!string.IsNullOrWhiteSpace(previewUrl) && !string.IsNullOrWhiteSpace(gifUrl))
            {
                items.Add(new GiphyGifItem(title, previewUrl, gifUrl));
            }
        }

        return items;
    }

    public async Task<byte[]> DownloadGifAsync(GiphyGifItem gif, CancellationToken cancellationToken = default)
    {
        return await HttpClient.GetByteArrayAsync(gif.GifUrl, cancellationToken);
    }

    public async Task<byte[]> DownloadPreviewAsync(GiphyGifItem gif, CancellationToken cancellationToken = default)
    {
        return await HttpClient.GetByteArrayAsync(gif.PreviewUrl, cancellationToken);
    }

    public void SaveApiKey(string apiKey)
    {
        ApiKey = apiKey.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, ApiKey);
    }

    private static string LoadApiKey()
    {
        var apiKey = Environment.GetEnvironmentVariable("NETBUDDIES_GIPHY_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey.Trim();
        }

        if (File.Exists(SettingsPath))
        {
            return File.ReadAllText(SettingsPath).Trim();
        }

        return "";
    }

    private static string ReadImageUrl(JsonElement images, string name)
    {
        if (!images.TryGetProperty(name, out var image)
            || !image.TryGetProperty("url", out var url))
        {
            return "";
        }

        return url.GetString() ?? "";
    }
}
