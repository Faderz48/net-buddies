using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetBuddies.App.Services;

public static class ElectronGameHostService
{
    public static ElectronGameHostLaunchResult Launch(Uri source, string title)
    {
        var host = FindHost();
        if (host is null)
        {
            return ElectronGameHostLaunchResult.Failed(
                "The Net Buddies Electron game host is not installed. Build it with npm in the electron-game-host folder, or install a client package that includes ElectronGameHost.");
        }

        try
        {
            Process.Start(host.CreateStartInfo(source.ToString(), title));
            return ElectronGameHostLaunchResult.Started("Game launched in the Net Buddies Electron game host.");
        }
        catch (Exception ex)
        {
            return ElectronGameHostLaunchResult.Failed($"Could not start the Electron game host: {ex.Message}");
        }
    }

    private static ElectronGameHost? FindHost()
    {
        var overridePath = Environment.GetEnvironmentVariable("NETBUDDIES_ELECTRON_GAME_HOST");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var host = BuildHostFromPath(overridePath);
            if (host is not null)
            {
                return host;
            }
        }

        var packagedHost = FindPackagedHost();
        if (packagedHost is not null)
        {
            return packagedHost;
        }

        return FindDevelopmentHost();
    }

    private static ElectronGameHost? BuildHostFromPath(string path)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (Directory.Exists(fullPath))
        {
            foreach (var executableName in GetPackagedExecutableNames())
            {
                var executablePath = Path.Combine(fullPath, executableName);
                if (File.Exists(executablePath))
                {
                    return ElectronGameHost.Packaged(executablePath);
                }
            }

            var developmentHost = BuildDevelopmentHost(fullPath);
            if (developmentHost is not null)
            {
                return developmentHost;
            }
        }

        return File.Exists(fullPath)
            ? ElectronGameHost.Packaged(fullPath)
            : null;
    }

    private static ElectronGameHost? FindPackagedHost()
    {
        foreach (var executableName in GetPackagedExecutableNames())
        {
            var executablePath = Path.Combine(AppContext.BaseDirectory, "ElectronGameHost", executableName);
            if (File.Exists(executablePath))
            {
                return ElectronGameHost.Packaged(executablePath);
            }
        }

        return null;
    }

    private static ElectronGameHost? FindDevelopmentHost()
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(startPath);
            while (directory is not null)
            {
                var hostDirectory = Path.Combine(directory.FullName, "electron-game-host");
                var host = BuildDevelopmentHost(hostDirectory);
                if (host is not null)
                {
                    return host;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static ElectronGameHost? BuildDevelopmentHost(string hostDirectory)
    {
        var mainScript = Path.Combine(hostDirectory, "main.js");
        if (!File.Exists(mainScript))
        {
            return null;
        }

        foreach (var executablePath in GetDevelopmentElectronPaths(hostDirectory))
        {
            if (File.Exists(executablePath))
            {
                return ElectronGameHost.Development(executablePath, mainScript, hostDirectory);
            }
        }

        return null;
    }

    private static IEnumerable<string> GetDevelopmentElectronPaths(string hostDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(hostDirectory, "node_modules", "electron", "dist", "electron.exe");
            yield return Path.Combine(hostDirectory, "node_modules", ".bin", "electron.cmd");
        }
        else
        {
            yield return Path.Combine(hostDirectory, "node_modules", ".bin", "electron");
            yield return Path.Combine(hostDirectory, "node_modules", "electron", "dist", "electron");
        }
    }

    private static IEnumerable<string> GetPackagedExecutableNames()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return "NetBuddies Game Host.exe";
            yield return "Net Buddies Game Host.exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return Path.Combine("NetBuddies Game Host.app", "Contents", "MacOS", "NetBuddies Game Host");
            yield return Path.Combine("Net Buddies Game Host.app", "Contents", "MacOS", "Net Buddies Game Host");
        }
        else
        {
            yield return "NetBuddies Game Host";
            yield return "Net Buddies Game Host";
        }
    }

    private sealed record ElectronGameHost(string FileName, string ArgumentsPrefix, string WorkingDirectory)
    {
        public static ElectronGameHost Packaged(string executablePath)
        {
            return new ElectronGameHost(executablePath, "", Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory);
        }

        public static ElectronGameHost Development(string electronPath, string mainScript, string hostDirectory)
        {
            return new ElectronGameHost(electronPath, Quote(mainScript), hostDirectory);
        }

        public ProcessStartInfo CreateStartInfo(string url, string title)
        {
            var arguments = string.Join(
                ' ',
                new[]
                {
                    ArgumentsPrefix,
                    $"--url={Quote(url)}",
                    $"--title={Quote(title)}"
                }.Where(value => !string.IsNullOrWhiteSpace(value)));

            return new ProcessStartInfo
            {
                FileName = FileName,
                Arguments = arguments,
                WorkingDirectory = WorkingDirectory,
                UseShellExecute = false
            };
        }

        private static string Quote(string value)
        {
            return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }
    }
}

public sealed record ElectronGameHostLaunchResult(bool Success, string Message)
{
    public static ElectronGameHostLaunchResult Started(string message) => new(true, message);

    public static ElectronGameHostLaunchResult Failed(string message) => new(false, message);
}
