using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NetBuddies.App.ViewModels;

public sealed partial class RealtimePongViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _shutdown = new();
    private int _lastMove;

    public RealtimePongViewModel(
        string gameId,
        string buddyName,
        string displayName,
        string serverUrl,
        bool isHost)
    {
        GameId = gameId;
        BuddyName = buddyName;
        DisplayName = displayName;
        ServerUrl = serverUrl;
        IsHost = isHost;
        Title = $"Buddy Pong with {buddyName}";
        StatusText = "Connecting to real-time game server...";
    }

    public string GameId { get; }
    public string BuddyName { get; }
    public string DisplayName { get; }
    public string ServerUrl { get; }
    public bool IsHost { get; }
    public string Title { get; }

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private double _ballX = 50;

    [ObservableProperty]
    private double _ballY = 50;

    [ObservableProperty]
    private double _leftPaddleY = 50;

    [ObservableProperty]
    private double _rightPaddleY = 50;

    [ObservableProperty]
    private string _leftName = "Waiting";

    [ObservableProperty]
    private string _rightName = "Waiting";

    [ObservableProperty]
    private int _leftScore;

    [ObservableProperty]
    private int _rightScore;

    [ObservableProperty]
    private string _sideText = "";

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.Connecting)
        {
            return;
        }

        try
        {
            var side = IsHost ? "left" : "right";
            var baseUri = new Uri(ServerUrl.TrimEnd('/'));
            var builder = new UriBuilder(baseUri)
            {
                Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/netbuddies/pong/{Uri.EscapeDataString(GameId)}",
                Query = $"name={Uri.EscapeDataString(DisplayName)}&side={side}"
            };
            await _socket.ConnectAsync(builder.Uri, _shutdown.Token);
            StatusText = "Connected. Use W/S or Up/Down to move.";
            _ = Task.Run(ReceiveLoopAsync);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not connect: {ex.Message}";
        }
    }

    public async Task SetMoveAsync(int move)
    {
        move = Math.Clamp(move, -1, 1);
        if (move == _lastMove || _socket.State != WebSocketState.Open)
        {
            return;
        }

        _lastMove = move;
        await SendAsync(new { type = "input", move });
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];
        while (!_shutdown.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            try
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, _shutdown.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        PostToUi(() => StatusText = "Real-time game disconnected.");
                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(message.ToArray());
                PostToUi(() => ApplyMessage(json));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                PostToUi(() => StatusText = $"Real-time game error: {ex.Message}");
                return;
            }
        }
    }

    private void ApplyMessage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : "";
        if (type == "hello")
        {
            var side = root.TryGetProperty("side", out var sideValue) ? sideValue.GetString() : "";
            SideText = side == "left" ? "You are left paddle" : "You are right paddle";
            return;
        }

        if (type != "state")
        {
            return;
        }

        if (root.TryGetProperty("ball", out var ball))
        {
            BallX = ReadPercent(ball, "x", 0.5);
            BallY = ReadPercent(ball, "y", 0.5);
        }

        if (!root.TryGetProperty("players", out var players))
        {
            return;
        }

        var hasLeft = false;
        var hasRight = false;
        foreach (var player in players.EnumerateArray())
        {
            var side = player.TryGetProperty("side", out var sideValue) ? sideValue.GetString() : "";
            var name = player.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "Buddy" : "Buddy";
            var y = ReadPercent(player, "y", 0.5);
            var score = player.TryGetProperty("score", out var scoreValue) ? scoreValue.GetInt32() : 0;
            if (side == "left")
            {
                hasLeft = true;
                LeftName = name;
                LeftPaddleY = y;
                LeftScore = score;
            }
            else if (side == "right")
            {
                hasRight = true;
                RightName = name;
                RightPaddleY = y;
                RightScore = score;
            }
        }

        if (!hasLeft)
        {
            LeftName = "Waiting";
            LeftScore = 0;
        }

        if (!hasRight)
        {
            RightName = "Waiting";
            RightScore = 0;
        }
    }

    private async Task SendAsync<T>(T message)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, _shutdown.Token);
    }

    private static double ReadPercent(JsonElement element, string property, double fallback)
    {
        return element.TryGetProperty(property, out var value)
            ? Math.Clamp(value.GetDouble() * 100, 0, 100)
            : fallback * 100;
    }

    private static void PostToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_socket.State == WebSocketState.Open)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }

        _socket.Dispose();
        _shutdown.Dispose();
    }
}
