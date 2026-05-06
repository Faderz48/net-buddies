using NetBuddies.Core;

namespace NetBuddies.App.ViewModels;

public sealed class ChatRoomListItemViewModel(ChatRoomInfo room) : ViewModelBase
{
    public string Name { get; } = room.Name;
    public int MemberCount { get; } = room.MemberCount;
    public int VoiceCount { get; } = room.VoiceCount;
    public string Summary => $"{MemberCount} in room, {VoiceCount} in voice";
}
