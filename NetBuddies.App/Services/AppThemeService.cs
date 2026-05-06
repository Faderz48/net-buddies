using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace NetBuddies.App.Services;

public static class AppThemeService
{
    public const string LightTheme = "Light";
    public const string DarkTheme = "Dark";

    public static string CurrentThemeName { get; private set; } = LightTheme;

    public static void ApplySavedTheme()
    {
        ApplyTheme(ProfileSettingsStore.Load().AppTheme, persist: false);
    }

    public static void SetTheme(string themeName)
    {
        ApplyTheme(themeName, persist: true);
    }

    private static void ApplyTheme(string themeName, bool persist)
    {
        var normalizedTheme = string.Equals(themeName, DarkTheme, StringComparison.OrdinalIgnoreCase)
            ? DarkTheme
            : LightTheme;
        CurrentThemeName = normalizedTheme;

        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = normalizedTheme == DarkTheme
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        ApplyPalette(app, normalizedTheme == DarkTheme);

        if (!persist)
        {
            return;
        }

        var settings = ProfileSettingsStore.Load();
        ProfileSettingsStore.Save(settings with { AppTheme = normalizedTheme });
    }

    private static void ApplyPalette(Application app, bool isDark)
    {
        if (isDark)
        {
            SetBrush(app, "NbPageBrush", "#101A26");
            SetBrush(app, "NbPanelBrush", "#172434");
            SetBrush(app, "NbCardBrush", "#1D2C3D");
            SetBrush(app, "NbCardBorderBrush", "#365A79");
            SetBrush(app, "NbMessageBrush", "#172637");
            SetBrush(app, "NbMessageBorderBrush", "#3E7097");
            SetBrush(app, "NbHeaderBrush", "#075A9F");
            SetBrush(app, "NbHeaderAccentBrush", "#1278C9");
            SetBrush(app, "NbHeaderBorderBrush", "#0A477A");
            SetBrush(app, "NbPrimaryBrush", "#1F8FE5");
            SetBrush(app, "NbTextBrush", "#E8F4FF");
            SetBrush(app, "NbStrongTextBrush", "#FFFFFF");
            SetBrush(app, "NbSubtleTextBrush", "#B9D0E4");
            SetBrush(app, "NbLinkBrush", "#8CC9FF");
            SetBrush(app, "NbInputBrush", "#0F1925");
            SetBrush(app, "NbInputBorderBrush", "#456B8D");
            SetBrush(app, "NbAvatarBrush", "#25384C");
            SetBrush(app, "NbAvatarBorderBrush", "#5F83A4");
            SetBrush(app, "NbInviteBrush", "#463C15");
            SetBrush(app, "NbInviteBorderBrush", "#A88D25");
            SetBrush(app, "NbInviteTextBrush", "#FFE8A3");
        }
        else
        {
            SetBrush(app, "NbPageBrush", "#D8EEFF");
            SetBrush(app, "NbPanelBrush", "#F7FBFF");
            SetBrush(app, "NbCardBrush", "#F8FBFF");
            SetBrush(app, "NbCardBorderBrush", "#BED7F2");
            SetBrush(app, "NbMessageBrush", "#FFFFFF");
            SetBrush(app, "NbMessageBorderBrush", "#A9D3F8");
            SetBrush(app, "NbHeaderBrush", "#086AC2");
            SetBrush(app, "NbHeaderAccentBrush", "#2B9BF0");
            SetBrush(app, "NbHeaderBorderBrush", "#04599F");
            SetBrush(app, "NbPrimaryBrush", "#0B74D1");
            SetBrush(app, "NbTextBrush", "#173C5D");
            SetBrush(app, "NbStrongTextBrush", "#111827");
            SetBrush(app, "NbSubtleTextBrush", "#406A88");
            SetBrush(app, "NbLinkBrush", "#0048A8");
            SetBrush(app, "NbInputBrush", "#FFFFFF");
            SetBrush(app, "NbInputBorderBrush", "#D0E3F6");
            SetBrush(app, "NbAvatarBrush", "#EBF3FF");
            SetBrush(app, "NbAvatarBorderBrush", "#B9CDE4");
            SetBrush(app, "NbInviteBrush", "#FFF8D9");
            SetBrush(app, "NbInviteBorderBrush", "#E2C45F");
            SetBrush(app, "NbInviteTextBrush", "#6F5A00");
        }
    }

    private static void SetBrush(Application app, string key, string color)
    {
        app.Resources[key] = SolidColorBrush.Parse(color);
    }
}
