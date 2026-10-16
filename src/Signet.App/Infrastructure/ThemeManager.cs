using System.IO;
using System.Linq;
using Avalonia;
using Microsoft.Extensions.Logging;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.Infrastructure;

/// <summary>The two stores of the look of the application: the themes of the interface and the color schemes of the editors.</summary>
/// <param name="Themes">The themes of the interface (<see cref="ThemeDocumentKind.Theme"/>).</param>
/// <param name="Schemes">The color schemes of the editors (<see cref="ThemeDocumentKind.Scheme"/>).</param>
public sealed record ThemeStores(ThemeStore Themes, ThemeStore Schemes)
{
    /// <summary>The stores in the folders <c>themes</c> and <c>schemes</c> of a folder.</summary>
    public static ThemeStores In(string folder) => new(
        new ThemeStore(Path.Combine(folder, "themes")),
        new ThemeStore(Path.Combine(folder, "schemes"), ThemeDocumentKind.Scheme));
}

/// <summary>
/// The look of the application: the stores of the themes of the interface and of the color schemes of the editors, and the
/// theme (<see cref="SettingsStore.UiThemeId"/>) and the scheme (<see cref="SettingsStore.EditorSchemeId"/>) in use, chosen
/// independently and applied together through <see cref="AppTheme"/>.
/// </summary>
/// <param name="settings">Application settings.</param>
/// <param name="stores">The themes and the color schemes.</param>
/// <param name="logger">Logger.</param>
public sealed class ThemeManager(SettingsStore settings, ThemeStores stores, ILogger<ThemeManager> logger)
{
    private readonly SettingsStore _settings = settings;
    private readonly ILogger<ThemeManager> _logger = logger;

    /// <summary>The bundled and the user's themes of the interface.</summary>
    public ThemeStore Themes { get; } = stores.Themes;

    /// <summary>The bundled and the user's color schemes of the editors.</summary>
    public ThemeStore Schemes { get; } = stores.Schemes;

    /// <summary>
    /// Applies the saved theme and color scheme (at startup and after Preferences changed them). One that no longer
    /// exists gives way to Signet Light. The files of the folders that could not be read are logged.
    /// </summary>
    public void ApplySaved()
    {
        foreach (string problem in Themes.LoadProblems.Concat(Schemes.LoadProblems))
        {
            _logger.LogWarning("Theme file skipped: {Problem}", problem);
        }

        Theme theme = Resolve(Themes, _settings.UiThemeId, "Theme");
        Theme scheme = Resolve(Schemes, _settings.EditorSchemeId, "Color scheme");
        if (Application.Current is { } app)
        {
            AppTheme.Apply(app, new ResolvedTheme(theme, Themes.Resolver, scheme, Schemes.Resolver));
            _logger.LogInformation(
                "Theme applied: {ThemeName} ({ThemeId}), color scheme {SchemeName} ({SchemeId})", theme.Name, theme.Id, scheme.Name, scheme.Id);
        }
    }

    private Theme Resolve(ThemeStore store, string id, string what)
    {
        Theme found = store.Find(id) ?? store.Get(ThemeStore.LightId);
        if (found.Id != id)
        {
            _logger.LogWarning("{What} {Id} not found, using {Fallback}", what, id, found.Id);
        }

        return found;
    }
}
