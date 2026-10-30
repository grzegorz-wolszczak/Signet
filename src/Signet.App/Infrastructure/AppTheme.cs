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

/// <summary>
/// The look in use with its values resolved (<see cref="ThemeResolver"/>), in Avalonia's types: the theme of the interface
/// and the color scheme of the editors, each with its own parents. A key is looked up in the document it belongs to
/// (<see cref="ThemeKey.Document"/>), so the code that draws asks for any key and does not care which of the two holds it.
/// </summary>
public sealed class ResolvedTheme
{
    /// <summary>Resolves a theme of the interface and a color scheme of the editors.</summary>
    /// <param name="theme">The theme of the interface.</param>
    /// <param name="themeResolver">Resolves its keys (with its parents).</param>
    /// <param name="scheme">The color scheme of the editors.</param>
    /// <param name="schemeResolver">Resolves its keys (with its parents).</param>
    public ResolvedTheme(Theme theme, ThemeResolver themeResolver, Theme scheme, ThemeResolver schemeResolver)
    {
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        ThemeResolver = themeResolver ?? throw new ArgumentNullException(nameof(themeResolver));
        Scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
        SchemeResolver = schemeResolver ?? throw new ArgumentNullException(nameof(schemeResolver));
    }

    /// <summary>Signet Light, without stores: the defaults of the key registry.</summary>
    public static ResolvedTheme Defaults { get; } = new(
        new Theme(ThemeStore.LightId, "Signet Light"), new ThemeResolver(_ => null),
        new Theme(ThemeStore.LightId, "Signet Light"), new ThemeResolver(_ => null));

    /// <summary>The theme of the interface.</summary>
    public Theme Theme { get; }

    /// <summary>Resolves the keys of <see cref="Theme"/>.</summary>
    public ThemeResolver ThemeResolver { get; }

    /// <summary>The color scheme of the editors.</summary>
    public Theme Scheme { get; }

    /// <summary>Resolves the keys of <see cref="Scheme"/>.</summary>
    public ThemeResolver SchemeResolver { get; }

    /// <summary>
    /// A look that has only one of the two documents of its own (the editor of that document previews it): the other one
    /// is Signet Light.
    /// </summary>
    public static ResolvedTheme Of(ThemeDocumentKind kind, Theme document, ThemeResolver resolver) =>
        kind == ThemeDocumentKind.Theme
            ? new ResolvedTheme(document, resolver, Defaults.Scheme, Defaults.SchemeResolver)
            : new ResolvedTheme(Defaults.Theme, Defaults.ThemeResolver, document, resolver);

    /// <summary>The variant of the controls.</summary>
    public AvaloniaThemeVariant Variant =>
        ThemeResolver.Variant(Theme) == InterfaceVariant.Dark ? AvaloniaThemeVariant.Dark : AvaloniaThemeVariant.Light;

    /// <summary>The color of a color key as the theme or the scheme holds it; <c>null</c> = the platform's.</summary>
    public ThemeColor? ColorOf(ThemeKey key) =>
        key.Document == ThemeDocumentKind.Theme ? ThemeResolver.Color(Theme, key) : SchemeResolver.Color(Scheme, key);

    /// <summary>The color of a color key; <c>null</c> = the platform's.</summary>
    public Color? Color(ThemeKey key) => ColorOf(key) is { } c ? ToColor(c) : null;

    /// <summary>The attributes of a text attributes key.</summary>
    public TextAttributes Attributes(ThemeKey key) =>
        key.Document == ThemeDocumentKind.Theme ? ThemeResolver.Attributes(Theme, key) : SchemeResolver.Attributes(Scheme, key);

    /// <summary>The text color of a text attributes key; <c>null</c> = the color of the text around.</summary>
    public Color? Foreground(ThemeKey key) => Attributes(key).Foreground is { } c ? ToColor(c) : null;

    /// <summary>The background of a text attributes key; <c>null</c> = none.</summary>
    public Color? Background(ThemeKey key) => Attributes(key).Background is { } c ? ToColor(c) : null;

    /// <summary>A theme color as an Avalonia color.</summary>
    public static Color ToColor(ThemeColor color) => Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
}

/// <summary>
/// The theme and the color scheme in use, applied to the application: the variant of the controls, the Fluent palette
/// and the <c>Signet*Brush</c> resources of the interface keys, which the views use through <c>DynamicResource</c> (a
/// change of the theme shows at once). Code that draws by itself reads <see cref="Current"/> and follows
/// <see cref="Changed"/>.
/// </summary>
public static class AppTheme
{
    /// <summary>The application resource of the background of a context menu's title (<see cref="MenuTitle"/>).</summary>
    public const string ContextMenuTitleBackgroundKey = "SignetContextMenuTitleBackgroundBrush";

    /// <summary>The application resource of the folder icon of the file lists (a <see cref="DrawingImage"/>).</summary>
    public const string FolderIconKey = "SignetFolderIcon";

    /// <summary>The application resource of the file icon of the file lists (a <see cref="DrawingImage"/>).</summary>
    public const string FileIconKey = "SignetFileIcon";

    // The interface colors the views take as brushes.
    private static readonly (ThemeKey Key, string Resource)[] ColorBrushes =
    {
        (ThemeKeys.UiWarningText, "SignetWarningBrush"),
        (ThemeKeys.UiErrorNotification, "SignetErrorBrush"),
        (ThemeKeys.UiDangerButton, "SignetDangerButtonBrush"),
        (ThemeKeys.UiDangerButtonHover, "SignetDangerButtonHoverBrush"),
        (ThemeKeys.UiDangerButtonPressed, "SignetDangerButtonPressedBrush"),
        (ThemeKeys.UiDangerButtonText, "SignetDangerButtonTextBrush"),
        (ThemeKeys.UiCautionButton, "SignetCautionButtonBrush"),
        (ThemeKeys.UiCautionButtonHover, "SignetCautionButtonHoverBrush"),
        (ThemeKeys.UiCautionButtonPressed, "SignetCautionButtonPressedBrush"),
        (ThemeKeys.UiCautionButtonText, "SignetCautionButtonTextBrush"),
        (ThemeKeys.UiFrame, "SignetFrameBrush"),
        (ThemeKeys.UiBookBrowserBackground, "SignetBookBrowserBackgroundBrush"),
        (ThemeKeys.UiSearchEditorBackground, "SignetSearchEditorBackgroundBrush"),
        (ThemeKeys.UiNotificationsBackground, "SignetNotificationsBackgroundBrush"),
        (ThemeKeys.UiMutedText, "SignetMutedTextBrush"),
        (ThemeKeys.UiErrorText, "SignetErrorTextBrush"),
        (ThemeKeys.UiRenameErrorText, "SignetRenameErrorTextBrush"),
        (ThemeKeys.UiRiskMarker, "SignetRiskMarkerBrush"),
        (ThemeKeys.UiFilterActive, "SignetFilterActiveBrush"),
    };

    /// <summary>The theme and the color scheme in use (the defaults until they are applied).</summary>
    public static ResolvedTheme Current { get; private set; } = ResolvedTheme.Defaults;

    /// <summary>Raised after a theme or a color scheme was applied.</summary>
    public static event EventHandler? Changed;

    /// <summary>Applies a theme and a color scheme to the application and makes them <see cref="Current"/>.</summary>
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

        // Code in a formatted tooltip; unset: the accent color (a lighter shade on a dark theme, to be readable).
        string codeAccent = app.ActualThemeVariant == ThemeVariant.Dark ? FluentResourceKeys.SystemAccentColorLight2 : FluentResourceKeys.SystemAccentColor;
        SetBrush(app, Views.RichTextView.CodeBrushKey, theme.Color(ThemeKeys.UiTooltipCode)
            ?? (app.TryGetResource(codeAccent, app.ActualThemeVariant, out object? accentColor) && accentColor is Color code ? code : null));

        // Unset: a light shade of the accent color (lighter on a light theme, darker on a dark one).
        string accentShade = app.ActualThemeVariant == ThemeVariant.Dark ? FluentResourceKeys.SystemAccentColorDark2 : FluentResourceKeys.SystemAccentColorLight2;
        SetBrush(app, ContextMenuTitleBackgroundKey, theme.Color(ThemeKeys.UiContextMenuTitleBackground)
            ?? (app.TryGetResource(accentShade, app.ActualThemeVariant, out object? accent) && accent is Color shade ? shade : null));

        SetBrush(app, "SignetDocumentTabForegroundBrush", theme.Foreground(ThemeKeys.UiDocumentTab));
        SetBrush(app, "SignetDocumentTabBackgroundBrush", theme.Background(ThemeKeys.UiDocumentTab));
        SetBrush(app, "SignetDocumentTabSelectedForegroundBrush", theme.Foreground(ThemeKeys.UiDocumentTabSelected));
        SetBrush(app, "SignetDocumentTabSelectedBackgroundBrush", theme.Background(ThemeKeys.UiDocumentTabSelected));
        SetBrush(app, "SignetTextSelectionBrush", theme.Background(ThemeKeys.UiTextSelection));
        SetBrush(app, "SignetTextSelectionForegroundBrush", theme.Foreground(ThemeKeys.UiTextSelection));

        // The two columns of the Notifications panel: each background unset = the list background
        // (ui.notifications.background); the warning message foreground unset = the error notification color; the rest
        // unset = the normal text color.
        Color? notificationsBackground = theme.Color(ThemeKeys.UiNotificationsBackground);
        SetBrush(app, "SignetNotificationsTimestampBackgroundBrush",
            theme.Color(ThemeKeys.UiNotificationsTimestampBackground) ?? notificationsBackground);
        SetBrush(app, "SignetNotificationsTimestampForegroundBrush", theme.Color(ThemeKeys.UiNotificationsTimestampForeground));
        SetBrush(app, "SignetNotificationsMessageInfoBackgroundBrush",
            theme.Color(ThemeKeys.UiNotificationsMessageInfoBackground) ?? notificationsBackground);
        SetBrush(app, "SignetNotificationsMessageInfoForegroundBrush", theme.Color(ThemeKeys.UiNotificationsMessageInfoForeground));
        SetBrush(app, "SignetNotificationsMessageWarningBackgroundBrush",
            theme.Color(ThemeKeys.UiNotificationsMessageWarningBackground) ?? notificationsBackground);
        SetBrush(app, "SignetNotificationsMessageWarningForegroundBrush",
            theme.Color(ThemeKeys.UiNotificationsMessageWarningForeground) ?? theme.Color(ThemeKeys.UiErrorNotification));
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
