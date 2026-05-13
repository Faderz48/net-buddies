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
    public string ClientEntry { get; init; } = "";
    public bool IsUserGame { get; init; }
    public bool IsPlayable => Runtime is "builtin" or "realtime" or "web-game";
    public string SourceLabel => IsUserGame ? "User game folder" : "Built in";
    public string PlayModeLabel => Runtime switch
    {
        "builtin" => "Built-in Net Buddies game",
        "realtime" => string.IsNullOrWhiteSpace(Room)
            ? "Real-time server game"
            : $"Real-time server game: {Room}",
        "web-game" => "Folder-based web game",
        "asset-pack" => "Asset pack / custom folder",
        _ => Runtime
    };
    public Bitmap? Icon => GameAssetService.Load($"{Id}/{IconPath}");
    public string ClientEntryPath => Path.Combine(FolderPath, string.IsNullOrWhiteSpace(ClientEntry) ? "client/index.html" : ClientEntry);
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

    public static GameCatalogItem? FindGame(string id)
    {
        return LoadGames().FirstOrDefault(game => game.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
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
            Description = "Custom Net Buddies web game folder.",
            Runtime = "web-game",
            BuiltInType = "",
            Room = "",
            ClientKind = "web-game",
            ServerPortOffset = 0,
            Icon = "icon.png",
            ClientEntry = "client/index.html",
            ServerEntry = "server/room.js"
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        var clientDirectory = Path.Combine(folderPath, "client");
        Directory.CreateDirectory(clientDirectory);
        var indexPath = Path.Combine(clientDirectory, "index.html");
        if (!File.Exists(indexPath))
        {
            File.WriteAllText(indexPath, """
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>My Net Buddies Game</title>
                </head>
                <body>
                  <h1>My Net Buddies Game</h1>
                  <p>This folder is detected without rebuilding the app.</p>
                  <script>
                    const params = new URLSearchParams(location.search);
                    const relay = params.get("relay");
                    const socket = new WebSocket(relay);
                    socket.addEventListener("message", event => console.log("relay", event.data));
                  </script>
                </body>
                </html>
                """);
        }
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
                ClientEntry = Clean(manifest?.ClientEntry) ?? "client/index.html",
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
        public string ClientEntry { get; init; } = "";
        public string ServerEntry { get; init; } = "";
    }
}
