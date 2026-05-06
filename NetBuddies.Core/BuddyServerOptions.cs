using System.Security.Cryptography.X509Certificates;

namespace NetBuddies.Core;

public sealed record BuddyServerOptions
{
    public X509Certificate2? Certificate { get; init; }
    public string InviteCode { get; init; } = "";
    public int MaxConnectionsPerMinutePerAddress { get; init; } = 20;

    public bool UseTls => Certificate is not null;
    public bool RequiresInviteCode => !string.IsNullOrWhiteSpace(InviteCode);
}
