using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetBuddies.App.Services;

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
    public event Action<WebGameViewModel>? CloseRequested;

    [ObservableProperty]
    private string _statusText = "";

    [RelayCommand]
    private void EndGame()
    {
        CloseRequested?.Invoke(this);
    }

    private Uri BuildSource(GameCatalogItem game)
    {
        if (!File.Exists(game.ClientEntryPath))
        {
            StatusText = $"Missing web game entry file: {game.ClientEntryPath}";
            return new Uri("about:blank");
        }

        var relayUrl = BuildRelayUrl();
        var builder = new UriBuilder(new Uri(game.ClientEntryPath))
        {
            Query = string.Join('&',
                $"gameId={Uri.EscapeDataString(CatalogGameId)}",
                $"roomId={Uri.EscapeDataString(GameId)}",
                $"playerName={Uri.EscapeDataString(LocalPlayerName)}",
                $"buddyName={Uri.EscapeDataString(BuddyName)}",
                $"side={(IsHost ? "host" : "guest")}",
                $"colyseus={Uri.EscapeDataString(ServerUrl)}",
                $"room={Uri.EscapeDataString(RoomName)}",
                $"relay={Uri.EscapeDataString(relayUrl)}")
        };
        return builder.Uri;
    }

    private string BuildRelayUrl()
    {
        var server = ServerUrl.TrimEnd('/');
        return $"{server}/netbuddies/webgame/{Uri.EscapeDataString(CatalogGameId)}/{Uri.EscapeDataString(GameId)}";
    }
}
