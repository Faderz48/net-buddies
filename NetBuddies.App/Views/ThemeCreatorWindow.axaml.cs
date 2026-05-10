using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NetBuddies.App.Services;

namespace NetBuddies.App.Views;

public partial class ThemeCreatorWindow : Window
{
    private readonly Dictionary<string, TextBox> _colorInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _swatches = new(StringComparer.OrdinalIgnoreCase);
    private string _savedThemeName = "";
    private bool _isInitialized;

    public ThemeCreatorWindow()
    {
        InitializeComponent();
        _isInitialized = true;
        BuildColorRows();
        LoadBasePalette();
    }

    public string SavedThemeName => _savedThemeName;

    private void BuildColorRows()
    {
        ColorRows.Children.Clear();
        foreach (var color in AppThemeService.EditableColors)
        {
            var swatch = new Border
            {
                Width = 34,
                Height = 26,
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = Brushes.Gray,
                CornerRadius = new Avalonia.CornerRadius(4)
            };
            var input = new TextBox
            {
                PlaceholderText = "#RRGGBB",
                MinWidth = 110
            };
            input.TextChanged += (_, _) => UpdatePreview();

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("170,38,*"),
                ColumnSpacing = 8
            };
            row.Children.Add(new TextBlock
            {
                Text = color.DisplayName,
                Foreground = (IBrush?)Application.Current?.Resources["NbTextBrush"] ?? Brushes.Black,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
            Grid.SetColumn(swatch, 1);
            row.Children.Add(swatch);
            Grid.SetColumn(input, 2);
            row.Children.Add(input);

            _swatches[color.Key] = swatch;
            _colorInputs[color.Key] = input;
            ColorRows.Children.Add(row);
        }
    }

    private void LoadBasePalette()
    {
        var palette = AppThemeService.GetPalette(GetBaseTheme());
        foreach (var color in AppThemeService.EditableColors)
        {
            if (_colorInputs.TryGetValue(color.Key, out var input)
                && palette.TryGetValue(color.Key, out var value))
            {
                input.Text = value;
            }
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!_isInitialized
            || PreviewPage is null
            || PreviewHeader is null
            || PreviewCard is null
            || PreviewMessage is null
            || PreviewInvite is null
            || PreviewButton is null)
        {
            return;
        }

        var palette = ReadPalette();
        foreach (var (key, input) in _colorInputs)
        {
            if (_swatches.TryGetValue(key, out var swatch))
            {
                swatch.Background = TryBrush(input.Text, Brushes.Transparent);
            }
        }

        PreviewPage.Background = BrushFor(palette, "NbPageBrush");
        PreviewHeader.Background = BrushFor(palette, "NbHeaderBrush");
        PreviewHeader.BorderBrush = BrushFor(palette, "NbHeaderBorderBrush");
        PreviewCard.Background = BrushFor(palette, "NbCardBrush");
        PreviewCard.BorderBrush = BrushFor(palette, "NbCardBorderBrush");
        PreviewMessage.Background = BrushFor(palette, "NbMessageBrush");
        PreviewMessage.BorderBrush = BrushFor(palette, "NbMessageBorderBrush");
        PreviewInvite.Background = BrushFor(palette, "NbInviteBrush");
        PreviewInvite.BorderBrush = BrushFor(palette, "NbInviteBorderBrush");
        PreviewButton.Background = BrushFor(palette, "NbPrimaryBrush");
        PreviewMainText.Foreground = BrushFor(palette, "NbTextBrush");
        PreviewStrongText.Foreground = BrushFor(palette, "NbStrongTextBrush");
        PreviewSubtleText.Foreground = BrushFor(palette, "NbSubtleTextBrush");
        PreviewInviteText.Foreground = BrushFor(palette, "NbInviteTextBrush");
    }

    private Dictionary<string, string> ReadPalette()
    {
        var palette = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, input) in _colorInputs)
        {
            if (!string.IsNullOrWhiteSpace(input.Text))
            {
                palette[key] = input.Text.Trim();
            }
        }

        return palette;
    }

    private string GetBaseTheme()
    {
        return BaseThemeBox.SelectedIndex == 1 ? AppThemeService.DarkTheme : AppThemeService.LightTheme;
    }

    private static IBrush BrushFor(IReadOnlyDictionary<string, string> palette, string key)
    {
        return palette.TryGetValue(key, out var color)
            ? TryBrush(color, Brushes.Transparent)
            : Brushes.Transparent;
    }

    private static IBrush TryBrush(string? color, IBrush fallback)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return fallback;
        }

        try
        {
            return SolidColorBrush.Parse(color.Trim());
        }
        catch
        {
            return fallback;
        }
    }

    private void BaseThemeBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        LoadBasePalette();
    }

    private void SaveTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var name = string.IsNullOrWhiteSpace(ThemeNameBox.Text) ? "My Theme" : ThemeNameBox.Text.Trim();
            _savedThemeName = AppThemeService.SaveTheme(name, GetBaseTheme(), ReadPalette());
            AppThemeService.SetTheme(_savedThemeName);
            StatusText.Text = $"Saved theme to {Path.Combine(AppThemeService.ThemeRootDirectory, _savedThemeName)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save theme: {ex.Message}";
        }
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(!string.IsNullOrWhiteSpace(_savedThemeName));
    }
}
