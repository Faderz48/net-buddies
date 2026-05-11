using System.Text.Json;
using Avalonia.Media.Imaging;

namespace NetBuddies.App.Services;

public sealed class GameCatalogItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Runtime { get; init; } = "";
    public string BuiltInType { get; init; } = "";
    public string Room { get; init; } = "";
    public string ClientKind { get; init; } = "";
    public int ServerPortOffset { get; init; }
    public string FolderPath { get; init; } = "";
    public string IconPath { get; init; } = "icon.png";
    public bool IsUserGame { get; init; }
    public bool IsPlayable => Runtime is "builtin" or "realtime";
    public string SourceLabel => IsUserGame ? "User game folder" : "Built in";
    public string PlayModeLabel => Runtime switch
    {
        "builtin" => "Built-in Net Buddies game",
        "realtime" => string.IsNullOrWhiteSpace(Room)
            ? "Real-time server game"
            : $"Real-time server game: {Room}",
        "asset-pack" => "Asset pack / custom folder",
        _ => Runtime
    };
    public Bitmap? Icon => GameAssetService.Load($"{Id}/{IconPath}");
}

public static class GameCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static IReadOnlyList<GameCatalogItem> LoadGames()
    {
        Directory.CreateDirectory(GameAssetService.UserGamesFolder);

        var games = new Dictionary<string, GameCatalogItem>(StringComparer.OrdinalIgnoreCase);
        LoadFromFolder(GameAssetService.ShippedGamesFolder, isUserGame: false, games);
        LoadFromFolder(GameAssetService.UserGamesFolder, isUserGame: true, games);
        return games.Values
            .OrderByDescending(game => game.IsPlayable)
            .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void CreateExampleManifest(string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        var manifestPath = Path.Combine(folderPath, "game.json");
        if (File.Exists(manifestPath))
        {
            return;
        }

        var folderName = Path.GetFileName(folderPath);
        var manifest = new GameManifest
        {
            Id = folderName,
            Name = folderName,
            Description = "Custom Net Buddies game folder.",
            Runtime = "asset-pack",
            BuiltInType = "",
            Room = "",
            ClientKind = "",
            ServerPortOffset = 0,
            Icon = "icon.png"
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static void LoadFromFolder(string root, bool isUserGame, Dictionary<string, GameCatalogItem> games)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var folderName = Path.GetFileName(directory);
            if (folderName.StartsWith('_'))
            {
                continue;
            }

            var manifest = ReadManifest(Path.Combine(directory, "game.json"));
            var id = Clean(manifest?.Id) ?? folderName;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            games[id] = new GameCatalogItem
            {
                Id = id,
                Name = Clean(manifest?.Name) ?? id,
                Description = Clean(manifest?.Description) ?? "Custom game folder detected.",
                Runtime = Clean(manifest?.Runtime) ?? "asset-pack",
                BuiltInType = Clean(manifest?.BuiltInType) ?? "",
                Room = Clean(manifest?.Room) ?? "",
                ClientKind = Clean(manifest?.ClientKind) ?? "",
                ServerPortOffset = manifest?.ServerPortOffset ?? 0,
                IconPath = Clean(manifest?.Icon) ?? "icon.png",
                FolderPath = directory,
                IsUserGame = isUserGame
            };
        }
    }

    private static GameManifest? ReadManifest(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GameManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class GameManifest
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Runtime { get; init; } = "";
        public string BuiltInType { get; init; } = "";
        public string Room { get; init; } = "";
        public string ClientKind { get; init; } = "";
        public int ServerPortOffset { get; init; }
        public string Icon { get; init; } = "";
    }
}
