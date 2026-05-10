using NetBuddies.App.Services;

namespace NetBuddies.App.ViewModels;

public sealed class RoomMessageLineViewModel(
    string sender,
    string body,
    bool isEvent = false,
    GameImageAsset? avatarImage = null) : ViewModelBase
{
    public string Sender { get; } = sender;
    public string Body { get; } = body;
    public bool IsEvent { get; } = isEvent;
    public GameImageAsset? AvatarImage { get; } = avatarImage;
    public string Stamp { get; } = DateTime.Now.ToString("HH:mm");
}
