using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace NetBuddies.App.Services;

public sealed record AppThemeInfo(string Name, string DisplayName);
public sealed record AppThemeColorInfo(string Key, string DisplayName);

public static class AppThemeService
{
    public const string LightTheme = "Light";
    public const string DarkTheme = "Dark";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IReadOnlyList<AppThemeColorInfo> EditableColors { get; } =
    [
        new("NbPageBrush", "Page background"),
        new("NbPanelBrush", "Panels and menus"),
        new("NbCardBrush", "Cards"),
        new("NbCardBorderBrush", "Card borders"),
        new("NbMessageBrush", "Message bubbles"),
        new("NbMessageBorderBrush", "Message borders"),
        new("NbHeaderBrush", "Header"),
        new("NbHeaderAccentBrush", "Header accent"),
        new("NbHeaderBorderBrush", "Header border"),
        new("NbPrimaryBrush", "Primary buttons"),
        new("NbTextBrush", "Main text"),
        new("NbStrongTextBrush", "Strong text"),
        new("NbSubtleTextBrush", "Subtle text"),
        new("NbLinkBrush", "Links"),
        new("NbInputBrush", "Inputs"),
        new("NbInputBorderBrush", "Input borders"),
        new("NbAvatarBrush", "Avatar background"),
        new("NbAvatarBorderBrush", "Avatar border"),
        new("NbInviteBrush", "Invite panels"),
        new("NbInviteBorderBrush", "Invite borders"),
        new("NbInviteTextBrush", "Invite text")
    ];

    private static readonly Dictionary<string, string> LightPalette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NbPageBrush"] = "#D8EEFF",
        ["NbPanelBrush"] = "#F7FBFF",
        ["NbCardBrush"] = "#F8FBFF",
        ["NbCardBorderBrush"] = "#BED7F2",
        ["NbMessageBrush"] = "#FFFFFF",
        ["NbMessageBorderBrush"] = "#A9D3F8",
        ["NbHeaderBrush"] = "#086AC2",
        ["NbHeaderAccentBrush"] = "#2B9BF0",
        ["NbHeaderBorderBrush"] = "#04599F",
        ["NbPrimaryBrush"] = "#0B74D1",
        ["NbTextBrush"] = "#173C5D",
        ["NbStrongTextBrush"] = "#111827",
        ["NbSubtleTextBrush"] = "#406A88",
        ["NbLinkBrush"] = "#0048A8",
        ["NbInputBrush"] = "#FFFFFF",
        ["NbInputBorderBrush"] = "#D0E3F6",
        ["NbAvatarBrush"] = "#EBF3FF",
        ["NbAvatarBorderBrush"] = "#B9CDE4",
        ["NbInviteBrush"] = "#FFF8D9",
        ["NbInviteBorderBrush"] = "#E2C45F",
        ["NbInviteTextBrush"] = "#6F5A00"
    };

    private static readonly Dictionary<string, string> DarkPalette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NbPageBrush"] = "#101A26",
        ["NbPanelBrush"] = "#172434",
        ["NbCardBrush"] = "#1D2C3D",
        ["NbCardBorderBrush"] = "#365A79",
        ["NbMessageBrush"] = "#172637",
        ["NbMessageBorderBrush"] = "#3E7097",
        ["NbHeaderBrush"] = "#075A9F",
        ["NbHeaderAccentBrush"] = "#1278C9",
        ["NbHeaderBorderBrush"] = "#0A477A",
        ["NbPrimaryBrush"] = "#1F8FE5",
        ["NbTextBrush"] = "#E8F4FF",
        ["NbStrongTextBrush"] = "#FFFFFF",
        ["NbSubtleTextBrush"] = "#B9D0E4",
        ["NbLinkBrush"] = "#8CC9FF",
        ["NbInputBrush"] = "#0F1925",
        ["NbInputBorderBrush"] = "#456B8D",
        ["NbAvatarBrush"] = "#25384C",
        ["NbAvatarBorderBrush"] = "#5F83A4",
        ["NbInviteBrush"] = "#463C15",
        ["NbInviteBorderBrush"] = "#A88D25",
        ["NbInviteTextBrush"] = "#FFE8A3"
    };

    public static string CurrentThemeName { get; private set; } = LightTheme;

    public static void ApplySavedTheme()
    {
        ApplyTheme(ProfileSettingsStore.Load().AppTheme, persist: false);
    }

    public static void SetTheme(string themeName)
    {
        ApplyTheme(themeName, persist: true);
    }

    public static IReadOnlyList<AppThemeInfo> DiscoverThemes()
    {
        EnsureThemeFolders();

        var themes = new List<AppThemeInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(ThemeRootDirectory).OrderBy(Path.GetFileName))
        {
            var name = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            {
                continue;
            }

            themes.Add(new AppThemeInfo(name, ReadDefinition(name).DisplayNameOrDefault(name)));
        }

        AddFallbackTheme(themes, seen, LightTheme);
        AddFallbackTheme(themes, seen, DarkTheme);
        return themes;
    }

    public static IReadOnlyDictionary<string, string> GetPalette(string baseTheme)
    {
        return new Dictionary<string, string>(
            baseTheme.Equals(DarkTheme, StringComparison.OrdinalIgnoreCase) ? DarkPalette : LightPalette,
            StringComparer.OrdinalIgnoreCase);
    }

    public static string SaveTheme(string themeName, string baseTheme, IReadOnlyDictionary<string, string> colors)
    {
        var safeThemeName = SanitizeThemeName(themeName);
        var directory = Path.Combine(ThemeRootDirectory, safeThemeName);
        Directory.CreateDirectory(directory);

        var definition = new EditableThemeDefinition
        {
            DisplayName = themeName.Trim(),
            Base = baseTheme.Equals(DarkTheme, StringComparison.OrdinalIgnoreCase) ? DarkTheme : LightTheme,
            Colors = colors.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
        };
        var path = Path.Combine(directory, "theme.json");
        File.WriteAllText(path, JsonSerializer.Serialize(definition, JsonOptions));
        return safeThemeName;
    }

    private static void ApplyTheme(string themeName, bool persist)
    {
        EnsureThemeFolders();
        var theme = FindThemeName(themeName);
        var definition = ReadDefinition(theme);
        CurrentThemeName = theme;

        if (Application.Current is not { } app)
        {
            return;
        }

        var isDark = definition.IsDarkBase(theme);
        app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        var palette = new Dictionary<string, string>(
            isDark ? DarkPalette : LightPalette,
            StringComparer.OrdinalIgnoreCase);
        foreach (var color in definition.Colors)
        {
            if (!string.IsNullOrWhiteSpace(color.Value))
            {
                palette[color.Key] = color.Value;
            }
        }

        ApplyPalette(app, palette);

        if (!persist)
        {
            return;
        }

        var settings = ProfileSettingsStore.Load();
        ProfileSettingsStore.Save(settings with { AppTheme = theme });
    }

    private static string FindThemeName(string themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName))
        {
            return LightTheme;
        }

        return DiscoverThemes().FirstOrDefault(
            theme => theme.Name.Equals(themeName, StringComparison.OrdinalIgnoreCase))?.Name
            ?? LightTheme;
    }

    private static EditableThemeDefinition ReadDefinition(string themeName)
    {
        var path = Path.Combine(ThemeRootDirectory, themeName, "theme.json");
        if (!File.Exists(path))
        {
            return new EditableThemeDefinition { DisplayName = themeName, Base = DefaultBaseFor(themeName) };
        }

        try
        {
            return JsonSerializer.Deserialize<EditableThemeDefinition>(File.ReadAllText(path), JsonOptions)
                ?? new EditableThemeDefinition { DisplayName = themeName, Base = DefaultBaseFor(themeName) };
        }
        catch
        {
            return new EditableThemeDefinition { DisplayName = themeName, Base = DefaultBaseFor(themeName) };
        }
    }

    private static void ApplyPalette(Application app, IReadOnlyDictionary<string, string> palette)
    {
        foreach (var (key, color) in palette)
        {
            try
            {
                app.Resources[key] = SolidColorBrush.Parse(color);
            }
            catch
            {
            }
        }
    }

    private static void AddFallbackTheme(List<AppThemeInfo> themes, HashSet<string> seen, string name)
    {
        if (seen.Add(name))
        {
            themes.Add(new AppThemeInfo(name, name));
        }
    }

    private static void EnsureThemeFolders()
    {
        Directory.CreateDirectory(ThemeRootDirectory);
    }

    private static string DefaultBaseFor(string themeName)
    {
        return themeName.Equals(DarkTheme, StringComparison.OrdinalIgnoreCase) ? DarkTheme : LightTheme;
    }

    private static string SanitizeThemeName(string themeName)
    {
        var name = string.IsNullOrWhiteSpace(themeName) ? "New Theme" : themeName.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }

        return string.IsNullOrWhiteSpace(name) ? "New Theme" : name;
    }

    public static string ThemeRootDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Themes");

    private sealed class EditableThemeDefinition
    {
        public string DisplayName { get; init; } = "";
        public string Base { get; init; } = LightTheme;
        public Dictionary<string, string> Colors { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public string DisplayNameOrDefault(string fallback)
        {
            return string.IsNullOrWhiteSpace(DisplayName) ? fallback : DisplayName;
        }

        public bool IsDarkBase(string themeName)
        {
            return Base.Equals(DarkTheme, StringComparison.OrdinalIgnoreCase)
                || themeName.Equals(DarkTheme, StringComparison.OrdinalIgnoreCase);
        }
    }
}
