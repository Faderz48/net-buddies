using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace NetBuddies.App.Services;

public static class GameAssetService
{
    private const string AssetRoot = "Assets/Games";
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? Load(string relativePath)
    {
        var normalized = relativePath
            .Replace('\\', '/')
            .TrimStart('/');

        if (Cache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        var loosePath = Path.Combine(
            AppContext.BaseDirectory,
            AssetRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar));

        Bitmap? bitmap = null;
        if (File.Exists(loosePath))
        {
            bitmap = new Bitmap(loosePath);
        }
        else
        {
            var resourceUri = new Uri($"avares://NetBuddies.App/{AssetRoot}/{normalized}");
            if (AssetLoader.Exists(resourceUri))
            {
                using var stream = AssetLoader.Open(resourceUri);
                bitmap = new Bitmap(stream);
            }
        }

        Cache[normalized] = bitmap;
        return bitmap;
    }

    public static string ExternalGamesFolder => Path.Combine(AppContext.BaseDirectory, AssetRoot);
}
