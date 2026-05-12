using System.Text.Json;

namespace NetBuddies.App;

public sealed record ProfileSettings
{
    public string DisplayName { get; init; } = "";
    public string Status { get; init; } = "Online";
    public string PersonalMessage { get; init; } = "Stay connected. Always.";
    public string ProfileImageBase64 { get; init; } = "";
    public string AppTheme { get; init; } = "Light";
    public string LastHostAddress { get; init; } = "";
    public int LastPort { get; init; } = 5050;
    public bool LastUseSecureTls { get; init; }
    public bool LastTrustSelfSignedCertificate { get; init; } = true;
    public string LastServerCertificateFingerprint { get; init; } = "";
    public string LastServerInviteCode { get; init; } = "";
}

public static class ProfileSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NetBuddies",
        "profile.json");

    public static ProfileSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new ProfileSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<ProfileSettings>(json, JsonOptions) ?? new ProfileSettings();
        }
        catch
        {
            return new ProfileSettings();
        }
    }

    public static void Save(ProfileSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
