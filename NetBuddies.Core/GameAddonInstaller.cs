using System.IO.Compression;
using System.Text.Json;

namespace NetBuddies.Core;

public sealed record GameAddonInstallResult(
    string Id,
    string Name,
    string InstalledPath,
    bool HasClient,
    bool HasServer);

public static class GameAddonInstaller
{
    public static GameAddonInstallResult InstallFromZip(string zipPath, string targetGamesRoot)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Game add-on zip was not found.", zipPath);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "NetBuddiesGameAddon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempRoot);
            var source = FindAddonRoot(tempRoot);
            return InstallFromFolder(source, targetGamesRoot);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    public static GameAddonInstallResult InstallFromFolder(string sourceFolder, string targetGamesRoot)
    {
        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException($"Game add-on folder was not found: {sourceFolder}");
        }

        var manifestPath = Path.Combine(sourceFolder, "game.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("Game add-on needs a game.json file at the folder root.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var folderName = Path.GetFileName(sourceFolder);
        var id = CleanId(ReadJsonString(root, "id", folderName));
        var name = ReadJsonString(root, "name", id);
        var clientEntry = ReadJsonString(root, "clientEntry", "client/index.html")
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var serverEntry = ReadJsonString(root, "serverEntry", "server/room.js")
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var hasClient = File.Exists(Path.Combine(sourceFolder, clientEntry));
        var hasServer = File.Exists(Path.Combine(sourceFolder, serverEntry));
        if (!hasClient && !hasServer)
        {
            throw new InvalidOperationException("Game add-on needs at least a client entry or a server room entry.");
        }

        Directory.CreateDirectory(targetGamesRoot);
        var targetFolder = Path.Combine(targetGamesRoot, id);
        var backupFolder = targetFolder + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss");
        if (Directory.Exists(targetFolder))
        {
            Directory.Move(targetFolder, backupFolder);
        }

        try
        {
            CopyDirectory(sourceFolder, targetFolder);
            TryDeleteDirectory(backupFolder);
            return new GameAddonInstallResult(id, name, targetFolder, hasClient, hasServer);
        }
        catch
        {
            TryDeleteDirectory(targetFolder);
            if (Directory.Exists(backupFolder))
            {
                Directory.Move(backupFolder, targetFolder);
            }

            throw;
        }
    }

    private static string FindAddonRoot(string extractionRoot)
    {
        if (File.Exists(Path.Combine(extractionRoot, "game.json")))
        {
            return extractionRoot;
        }

        var candidates = Directory.EnumerateDirectories(extractionRoot)
            .Where(directory => File.Exists(Path.Combine(directory, "game.json")))
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException("Game add-on zip must contain game.json at the root or inside one top-level folder."),
            _ => throw new InvalidOperationException("Game add-on zip contains multiple game folders. Import one game add-on at a time.")
        };
    }

    private static string CleanId(string value)
    {
        var cleaned = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            .ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new InvalidOperationException("Game add-on id must contain letters or numbers.");
        }

        return cleaned;
    }

    private static string ReadJsonString(JsonElement root, string propertyName, string fallback)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
