namespace NetBuddies.Core;

public sealed record ChatRoomInfo
{
    public string Name { get; init; } = "";
    public int MemberCount { get; init; }
    public int VoiceCount { get; init; }
}
