namespace NetBuddies.Core;

public sealed record BuddyProfile
{
    public string Name { get; init; } = "";
    public string Status { get; init; } = "Online";
    public string PersonalMessage { get; init; } = "";
    public string ProfileImageBase64 { get; init; } = "";
}
