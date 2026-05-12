using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetBuddies.App.Services;
using System.Diagnostics;

namespace NetBuddies.App.ViewModels;

public sealed partial class WebGameViewModel : ViewModelBase
{
    public WebGameViewModel(
        string gameId,
        GameCatalogItem game,
        string buddyName,
        string localPlayerName,
        string serverUrl,
        bool isHost)
    {
        GameId = gameId;
        CatalogGameId = game.Id;
        RoomName = string.IsNullOrWhiteSpace(game.Room) ? game.Id : game.Room;
        GameName = game.Name;
        BuddyName = buddyName;
        LocalPlayerName = localPlayerName;
        ServerUrl = serverUrl;
        IsHost = isHost;
        Source = BuildSource(game);
    }

    public string GameId { get; }
    public string CatalogGameId { get; }
    public string RoomName { get; }
    public string GameName { get; }
    public string BuddyName { get; }
    public string LocalPlayerName { get; }
    public string ServerUrl { get; }
    public bool IsHost { get; }
    public string Title => $"{GameName} with {BuddyName}";
    public Uri Source { get; }
    public string SourceText => Source.ToString();
    public event Action<WebGameViewModel>? CloseRequested;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _launchStatusText = "";

    [RelayCommand]
    private void EndGame()
    {
        CloseRequested?.Invoke(this);
    }

    [RelayCommand]
    private void OpenExternally()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SourceText,
                UseShellExecute = true
            });
            LaunchStatusText = "Game opened in your browser.";
        }
        catch (Exception ex)
        {
            LaunchStatusText = $"Could not open game externally: {ex.Message}";
        }
    }

    private Uri BuildSource(GameCatalogItem game)
    {
        if (!File.Exists(game.ClientEntryPath))
        {
            StatusText = $"Missing web game entry file: {game.ClientEntryPath}";
            return new Uri("about:blank");
        }

        var source = new Uri(Path.GetFullPath(game.ClientEntryPath));
        var relayUrl = BuildRelayUrl();
        var builder = new UriBuilder(source)
        {
            Query = string.Join('&',
                $"gameId={Uri.EscapeDataString(CatalogGameId)}",
                $"roomId={Uri.EscapeDataString(GameId)}",
                $"playerName={Uri.EscapeDataString(LocalPlayerName)}",
                $"buddyName={Uri.EscapeDataString(BuddyName)}",
                $"side={(IsHost ? "host" : "guest")}",
                $"colyseus={Uri.EscapeDataString(ServerUrl)}",
                $"room={Uri.EscapeDataString(RoomName)}",
                $"relay={Uri.EscapeDataString(relayUrl)}",
                "embedded=1",
                "uiVersion=2")
        };
        return builder.Uri;
    }

    private string BuildRelayUrl()
    {
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var serverUri))
        {
            var server = ServerUrl.TrimEnd('/');
            return $"{server}/netbuddies/webgame/{Uri.EscapeDataString(CatalogGameId)}/{Uri.EscapeDataString(GameId)}";
        }

        var builder = new UriBuilder(serverUri)
        {
            Port = serverUri.IsDefaultPort ? -1 : serverUri.Port + 1,
            Path = $"netbuddies/webgame/{Uri.EscapeDataString(CatalogGameId)}/{Uri.EscapeDataString(GameId)}",
            Query = ""
        };
        return builder.Uri.ToString();
    }
}
