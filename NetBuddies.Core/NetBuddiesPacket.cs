using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetBuddies.Core;

public enum PacketKind
{
    Hello,
    Presence,
    Chat,
    Typing,
    Nudge,
    FileData,
    Profile,
    Game,
    Room,
    Voice,
    ScreenShare,
    System
}

public sealed record NetBuddiesPacket
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public PacketKind Kind { get; init; }
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Text { get; init; } = "";
    public string InviteCode { get; init; } = "";
    public IReadOnlyList<string> Users { get; init; } = [];
    public IReadOnlyList<BuddyProfile> Profiles { get; init; } = [];
    public IReadOnlyList<ChatRoomInfo> Rooms { get; init; } = [];
    public string FileName { get; init; } = "";
    public string FileAction { get; init; } = "";
    public long FileSize { get; init; }
    public string TransferId { get; init; } = "";
    public string PayloadBase64 { get; init; } = "";
    public string GameId { get; init; } = "";
    public string GameType { get; init; } = "";
    public string GameAction { get; init; } = "";
    public string RoomName { get; init; } = "";
    public string RoomAction { get; init; } = "";
    public DateTimeOffset SentAt { get; init; } = DateTimeOffset.UtcNow;

    public string ToJsonLine() => JsonSerializer.Serialize(this, JsonOptions);

    public static NetBuddiesPacket FromJsonLine(string line)
    {
        return JsonSerializer.Deserialize<NetBuddiesPacket>(line, JsonOptions)
               ?? throw new InvalidOperationException("The incoming packet was empty.");
    }
}
