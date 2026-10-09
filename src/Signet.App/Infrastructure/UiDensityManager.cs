using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.Logging;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// Compact ("Windows-like") look of the interface. The default Avalonia Fluent theme
/// is noticeably looser than native Windows applications: Inter 14 px font
/// instead of Segoe UI 9 pt (12 px) and large menu paddings. In compact mode:
/// <list type="bullet">
/// <item><see cref="FluentTheme.DensityStyle"/> = <see cref="DensityStyle.Compact"/>,</item>
/// <item>the UI font is the system one (<see cref="FontFamily.Default"/>, Segoe UI on Windows) at 12 px,</item>
/// <item>tighter main and context menus (items, separators, icon margin).</item>
/// </list>
/// The code editor has its own font from the Code View settings and is not affected.
/// <para>The manager also applies the warning appearance (Preferences → Appearance → Warnings,
/// <see cref="ApplyWarningAppearance()"/>): its default font size follows the UI font, so both live here — and, next to it,
/// the color of the error notifications (<see cref="ApplyErrorAppearance()"/>).</para>
/// </summary>
/// <param name="settings">Application settings.</param>
/// <param name="iconTheme">Recomputes the toolbar icon size after the font changes.</param>
/// <param name="logger">Logger.</param>
/// <param name="systemFontName">System font name (by default from <see cref="FontManager"/>; tests without a platform supply their own).</param>
/// <param name="isFontInstalled">Whether a font with the given name is in the list of system fonts (by default <see cref="FontCatalog"/>).</param>
public sealed class UiDensityManager(
    SettingsStore settings,
    IconThemeManager iconTheme,
    ILogger<UiDensityManager> logger,
    System.Func<string>? systemFontName = null,
    System.Func<string, bool>? isFontInstalled = null)
{
    /// <summary>UI font size in compact mode (Segoe UI 9 pt at 96 DPI).</summary>
    public const double CompactFontSize = 12;

    /// <summary>Default font of the Fluent theme in standard mode (<c>WithInterFont</c>).</summary>
    public const string StandardFontName = "Inter";

    private readonly SettingsStore _settings = settings;
    private readonly IconThemeManager _iconTheme = iconTheme;
    private readonly ILogger<UiDensityManager> _logger = logger;

    // Dictionary of the font selected in Preferences — replaced on every change (live).
    private ResourceDictionary? _fontResources;

    /// <summary>Whether compact mode is saved.</summary>
    public bool IsCompact => _settings.UiCompactDensity;

    /// <summary>UI font size in standard mode (the Fluent theme default).</summary>
    public const double StandardFontSize = 14;

    /// <summary>Default font size for the saved appearance mode.</summary>
    public double DefaultFontSize => DefaultFontSizeFor(IsCompact);

    /// <summary>Name of the default font for the saved appearance mode (see <see cref="DefaultFontNameFor"/>).</summary>
    public string DefaultFontName => DefaultFontNameFor(IsCompact);

    /// <summary>Default font size for the given appearance mode.</summary>
    public static double DefaultFontSizeFor(bool compact) => compact ? CompactFontSize : StandardFontSize;

    /// <summary>
    /// Name of the font used when none is selected in Preferences: in compact mode the system
    /// font (Segoe UI on Windows), in standard mode the Fluent theme's Inter. Always a name visible
    /// in the font picker list — Inter is built into the application, so when it is not
    /// installed in the system, the system font is returned.
    /// </summary>
    public string DefaultFontNameFor(bool compact)
    {
        string system = (systemFontName ?? (() => FontManager.Current.DefaultFontFamily.Name))();
        if (compact)
        {
            return system;
        }

        System.Func<string, bool> installed = isFontInstalled ?? (name =>
            FontCatalog.SystemFonts.Any(f => string.Equals(f.Name, name, System.StringComparison.OrdinalIgnoreCase)));
        return installed(StandardFontName) ? StandardFontName : system;
    }

    /// <summary>
    /// Applies the saved mode and UI font (Preferences → Appearance) to the application resources
    /// (called once at startup).
    /// </summary>
    public void ApplySaved()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        if (IsCompact)
        {
            Apply(app);
            _logger.LogInformation("Compact UI appearance applied");
        }

        ApplyFont(app, _settings.UiFont, _settings.UiFontSize);
        ApplyWarning(app, _settings.WarningAppearance, EffectiveFontSize);
        ApplyError(app, _settings.ErrorAppearance);
    }

    /// <summary>The UI font size in use: the one chosen in Preferences, otherwise the mode default.</summary>
    public double EffectiveFontSize => _settings.UiFontSize > 0 ? _settings.UiFontSize : DefaultFontSize;

    /// <summary>
    /// Applies the saved warning appearance live (Preferences → Appearance → Warnings): the font size of the warning
    /// texts (<c>SignetWarningFontSize</c>) and the warning color of both themes (<c>SignetWarningBrush</c>).
    /// </summary>
    public void ApplyWarningAppearance()
    {
        if (Application.Current is { } app)
        {
            ApplyWarning(app, _settings.WarningAppearance, EffectiveFontSize);
        }
    }

    /// <summary>
    /// Sets the warning resources of the application: the font size (<paramref name="uiFontSize"/> when
    /// <see cref="WarningAppearance.FontSize"/> is 0) and the <c>SignetWarningBrush</c> of the light and dark theme
    /// dictionaries — controls pick them up via <c>DynamicResource</c>.
    /// </summary>
    public static void ApplyWarning(Application app, WarningAppearance appearance, double uiFontSize)
    {
        System.ArgumentNullException.ThrowIfNull(app);
        app.Resources["SignetWarningFontSize"] = appearance.FontSize > 0 ? appearance.FontSize : uiFontSize;
        SetThemeBrush(app, ThemeVariant.Light, "SignetWarningBrush", appearance.LightColor);
        SetThemeBrush(app, ThemeVariant.Dark, "SignetWarningBrush", appearance.DarkColor);
    }

    /// <summary>
    /// Applies the saved color of the error notifications live (Preferences → Appearance → Errors):
    /// <c>SignetErrorBrush</c> of both themes.
    /// </summary>
    public void ApplyErrorAppearance()
    {
        if (Application.Current is { } app)
        {
            ApplyError(app, _settings.ErrorAppearance);
        }
    }

    /// <summary>
    /// Sets the <c>SignetErrorBrush</c> of the light and dark theme dictionaries — controls pick it up via
    /// <c>DynamicResource</c>.
    /// </summary>
    public static void ApplyError(Application app, ErrorAppearance appearance)
    {
        System.ArgumentNullException.ThrowIfNull(app);
        SetThemeBrush(app, ThemeVariant.Light, "SignetErrorBrush", appearance.LightColor);
        SetThemeBrush(app, ThemeVariant.Dark, "SignetErrorBrush", appearance.DarkColor);
    }

    private static void SetThemeBrush(Application app, ThemeVariant variant, string key, string color)
    {
        if (app.Resources.ThemeDictionaries.TryGetValue(variant, out IThemeVariantProvider? provider)
            && provider is IResourceDictionary dictionary
            && Color.TryParse(color, out Color parsed))
        {
            dictionary[key] = new SolidColorBrush(parsed);
        }
    }

    /// <summary>
    /// Applies the UI font live (Preferences → "Choose…" / "Default"): replaces the
    /// font dictionary in the application resources — controls pick it up via <c>DynamicResource</c> —
    /// and recomputes the toolbar icon size, which depends on the font height.
    /// </summary>
    public void ApplyUiFont(string family, int size)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        ApplyFont(app, family, size);
        ApplyWarning(app, _settings.WarningAppearance, EffectiveFontSize);
        _iconTheme.RefreshToolbarIconSize();
    }

    private void ApplyFont(Application app, string family, int size)
    {
        if (_fontResources is not null)
        {
            app.Resources.MergedDictionaries.Remove(_fontResources);
            _fontResources = null;
        }

        if (CreateFontResources(family, size) is { } font)
        {
            // Added after the compact-mode dictionary — the selected font takes precedence.
            app.Resources.MergedDictionaries.Add(font);
            _fontResources = font;
            _logger.LogInformation("UI font: {Family} {Size}", family, size);
        }
    }

    /// <summary>
    /// Resources of the UI font selected in Preferences: family (empty = mode default) and size
    /// in px (0 = mode default). <see langword="null"/> when nothing is overridden.
    /// </summary>
    public static ResourceDictionary? CreateFontResources(string family, int size)
    {
        ResourceDictionary resources = new();
        if (!string.IsNullOrWhiteSpace(family))
        {
            resources["ContentControlThemeFontFamily"] = FontFamily.Parse(family);
        }

        if (size > 0)
        {
            resources["ControlContentThemeFontSize"] = (double)size;
        }

        return resources.Count > 0 ? resources : null;
    }

    /// <summary>
    /// Overrides of the Fluent theme resources for compact mode. Fluent defaults (for
    /// comparison): Inter 14 px font, menu item 11,9,11,10 (context menu 11,4,11,7),
    /// separator 12,4,12,4, icon margin 0,0,12,0, menu bar 32 px.
    /// </summary>
    public static ResourceDictionary CreateCompactResources() => new()
    {
        ["ContentControlThemeFontFamily"] = FontFamily.Default,
        ["ControlContentThemeFontSize"] = CompactFontSize,
        ["MenuFlyoutItemThemePadding"] = new Thickness(8, 3, 8, 4),
        ["MenuFlyoutItemThemePaddingNarrow"] = new Thickness(4, 3, 8, 4),
        ["MenuFlyoutSeparatorThemePadding"] = new Thickness(4, 3, 4, 3),
        ["MenuIconPresenterMargin"] = new Thickness(0, 0, 8, 0),
        ["MenuFlyoutScrollerMargin"] = new Thickness(0, 2, 0, 2),
        ["MenuBarHeight"] = 24d,
    };

    /// <summary>Applies compact mode to the given application (also in headless tests).</summary>
    public static void Apply(Application app)
    {
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent)
        {
            fluent.DensityStyle = DensityStyle.Compact;
        }

        // Application.Resources take precedence over style resources (the Fluent theme).
        app.Resources.MergedDictionaries.Add(CreateCompactResources());
    }
}
