using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Signet.Core.Themes;
using AvaloniaThemeVariant = Avalonia.Styling.ThemeVariant;

namespace Signet.App.Infrastructure;

/// <summary>A theme with its values resolved (<see cref="ThemeResolver"/>), in Avalonia's types.</summary>
public sealed class ResolvedTheme
{
    /// <summary>Resolves a theme.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="resolver">Resolves its keys (with its parents).</param>
    public ResolvedTheme(Theme theme, ThemeResolver resolver)
    {
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>Signet Light without a theme store: the defaults of the key registry.</summary>
    public static ResolvedTheme Defaults { get; } =
        new(new Theme(ThemeStore.LightId, "Signet Light"), new ThemeResolver(_ => null));

    /// <summary>The theme.</summary>
    public Theme Theme { get; }

    /// <summary>Resolves its keys.</summary>
    public ThemeResolver Resolver { get; }

    /// <summary>The variant of the controls.</summary>
    public AvaloniaThemeVariant Variant =>
        Resolver.Variant(Theme) == InterfaceVariant.Dark ? AvaloniaThemeVariant.Dark : AvaloniaThemeVariant.Light;

    /// <summary>The icon set the theme names; <c>null</c> = the one chosen in Preferences.</summary>
    public string? IconSet => Resolver.IconSet(Theme);

    /// <summary>The color of a color key; <c>null</c> = the platform's.</summary>
    public Color? Color(ThemeKey key) => Resolver.Color(Theme, key) is { } c ? ToColor(c) : null;

    /// <summary>The attributes of a text attributes key.</summary>
    public TextAttributes Attributes(ThemeKey key) => Resolver.Attributes(Theme, key);

    /// <summary>The text color of a text attributes key; <c>null</c> = the color of the text around.</summary>
    public Color? Foreground(ThemeKey key) => Attributes(key).Foreground is { } c ? ToColor(c) : null;

    /// <summary>The background of a text attributes key; <c>null</c> = none.</summary>
    public Color? Background(ThemeKey key) => Attributes(key).Background is { } c ? ToColor(c) : null;

    /// <summary>A theme color as an Avalonia color.</summary>
    public static Color ToColor(ThemeColor color) => Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
}

/// <summary>
/// The theme in use, applied to the application: the variant of the controls, the Fluent palette and the
/// <c>Signet*Brush</c> resources of the interface keys, which the views use through <c>DynamicResource</c> (a change of
/// the theme shows at once). Code that draws by itself reads <see cref="Current"/> and follows <see cref="Changed"/>.
/// </summary>
public static class AppTheme
{
    /// <summary>The application resource of the folder icon of the file lists (a <see cref="DrawingImage"/>).</summary>
    public const string FolderIconKey = "SignetFolderIcon";

    /// <summary>The application resource of the file icon of the file lists (a <see cref="DrawingImage"/>).</summary>
    public const string FileIconKey = "SignetFileIcon";

    // The interface colors the views take as brushes. The warning and the error notification colors still come from
    // their settings (UiDensityManager) until the settings move into the themes.
    private static readonly (ThemeKey Key, string Resource)[] ColorBrushes =
    {
        (ThemeKeys.UiDangerButton, "SignetDangerButtonBrush"),
        (ThemeKeys.UiDangerButtonHover, "SignetDangerButtonHoverBrush"),
        (ThemeKeys.UiDangerButtonPressed, "SignetDangerButtonPressedBrush"),
        (ThemeKeys.UiDangerButtonText, "SignetDangerButtonTextBrush"),
        (ThemeKeys.UiFrame, "SignetFrameBrush"),
        (ThemeKeys.UiMutedText, "SignetMutedTextBrush"),
        (ThemeKeys.UiErrorText, "SignetErrorTextBrush"),
        (ThemeKeys.UiRiskMarker, "SignetRiskMarkerBrush"),
        (ThemeKeys.UiFilterActive, "SignetFilterActiveBrush"),
    };

    /// <summary>The theme in use (the defaults until one is applied).</summary>
    public static ResolvedTheme Current { get; private set; } = ResolvedTheme.Defaults;

    /// <summary>Raised after a theme was applied.</summary>
    public static event EventHandler? Changed;

    /// <summary>Applies a theme to the application and makes it <see cref="Current"/>.</summary>
    public static void Apply(Application app, ResolvedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(theme);
        Current = theme;
        app.RequestedThemeVariant = theme.Variant;
        ApplyPalette(app, theme);

        foreach ((ThemeKey key, string resource) in ColorBrushes)
        {
            SetBrush(app, resource, theme.Color(key));
        }

        SetBrush(app, "SignetTextSelectionBrush", theme.Background(ThemeKeys.UiTextSelection));
        SetBrush(app, "SignetTextSelectionForegroundBrush", theme.Foreground(ThemeKeys.UiTextSelection));
        app.Resources[FolderIconKey] = FolderIcon(theme);
        app.Resources[FileIconKey] = FileIcon(theme);
        ActionIcons.SetStateColors(
            theme.Color(ThemeKeys.UiIconInactive) ?? ActionIcons.DefaultInactiveColor,
            theme.Color(ThemeKeys.UiIconAttention) ?? ActionIcons.DefaultAttentionColor);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    // The accent and the colors of the Fluent palette the theme sets, for its variant; the rest stays Fluent's.
    private static void ApplyPalette(Application app, ResolvedTheme theme)
    {
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is not { } fluent)
        {
            return;
        }

        ColorPaletteResources palette = new();
        if (theme.Color(ThemeKeys.UiAccent) is { } accent)
        {
            palette.Accent = accent;
        }

        foreach ((string name, ThemeKey key) in ThemeKeys.FluentPalette)
        {
            if (theme.Color(key) is { } color)
            {
                typeof(ColorPaletteResources).GetProperty(name)!.SetValue(palette, color);
            }
        }

        fluent.Palettes[theme.Variant] = palette;
    }

    private static void SetBrush(Application app, string resource, Color? color)
    {
        if (color is { } c)
        {
            app.Resources[resource] = new SolidColorBrush(c);
        }
        else
        {
            app.Resources.Remove(resource);
        }
    }

    // The icons of the file lists: drawn in one style on every system, in the colors of the theme.
    private static DrawingImage FolderIcon(ResolvedTheme theme) => Icon(new List<GeometryDrawing>
    {
        new() { Brush = Brush(theme.Color(ThemeKeys.UiIconFolder)), Geometry = Geometry.Parse("M 1 3 H 6 L 7.5 4.5 H 15 V 13.5 H 1 Z") },
        new() { Brush = Brush(theme.Color(ThemeKeys.UiIconFolderInner)), Geometry = Geometry.Parse("M 1.75 6 H 14.25 V 12.75 H 1.75 Z") },
    });

    private static DrawingImage FileIcon(ResolvedTheme theme)
    {
        Pen outline = new(Brush(theme.Color(ThemeKeys.UiIconFileOutline)), 1) { LineJoin = PenLineJoin.Miter };
        return Icon(new List<GeometryDrawing>
        {
            new() { Brush = Brush(theme.Color(ThemeKeys.UiIconFile)), Pen = outline, Geometry = Geometry.Parse("M 3 1.5 H 9.5 L 13 5 V 14.5 H 3 Z") },
            new() { Brush = Brush(theme.Color(ThemeKeys.UiIconFileFold)), Pen = outline, Geometry = Geometry.Parse("M 9.5 1.5 V 5 H 13 Z") },
        });
    }

    // A 16×16 icon (a transparent square keeps the size).
    private static DrawingImage Icon(IEnumerable<GeometryDrawing> parts)
    {
        DrawingGroup group = new();
        group.Children.Add(new GeometryDrawing { Brush = Brushes.Transparent, Geometry = Geometry.Parse("M 0 0 H 16 V 16 H 0 Z") });
        foreach (GeometryDrawing part in parts)
        {
            group.Children.Add(part);
        }

        return new DrawingImage(group);
    }

    private static SolidColorBrush? Brush(Color? color) => color is { } c ? new SolidColorBrush(c) : null;
}
