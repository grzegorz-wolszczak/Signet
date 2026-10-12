using Avalonia;
using Microsoft.Extensions.Logging;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.Infrastructure;

/// <summary>
/// The themes of the application: the store of the bundled and the user's themes and the one in use
/// (<see cref="SettingsStore.UiThemeId"/>), applied through <see cref="AppTheme"/>.
/// </summary>
/// <param name="settings">Application settings.</param>
/// <param name="store">The themes.</param>
/// <param name="logger">Logger.</param>
public sealed class ThemeManager(SettingsStore settings, ThemeStore store, ILogger<ThemeManager> logger)
{
    private readonly SettingsStore _settings = settings;
    private readonly ILogger<ThemeManager> _logger = logger;

    /// <summary>The bundled and the user's themes.</summary>
    public ThemeStore Store { get; } = store;

    /// <summary>
    /// Applies the saved theme (at startup and after Preferences changed it). A theme that no longer exists gives way
    /// to Signet Light. The files of the theme folder that could not be read are logged. The colors the settings held
    /// before the themes move into the theme "My colors" first (<see cref="LegacyColorMigration"/>).
    /// </summary>
    public void ApplySaved()
    {
        if (LegacyColorMigration.Run(_settings, Store) is { } migrated)
        {
            _logger.LogInformation("The colors of the settings moved into the theme {ThemeName} ({ThemeId})", migrated.Name, migrated.Id);
        }

        foreach (string problem in Store.LoadProblems)
        {
            _logger.LogWarning("Theme file skipped: {Problem}", problem);
        }

        string id = _settings.UiThemeId;
        Theme theme = Store.Find(id) ?? Store.Get(ThemeStore.LightId);
        if (theme.Id != id)
        {
            _logger.LogWarning("Theme {ThemeId} not found, using {Fallback}", id, theme.Id);
        }

        if (Application.Current is { } app)
        {
            AppTheme.Apply(app, new ResolvedTheme(theme, Store.Resolver));
            _logger.LogInformation("Theme applied: {ThemeName} ({ThemeId})", theme.Name, theme.Id);
        }
    }
}
