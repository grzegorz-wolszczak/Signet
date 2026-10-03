using Avalonia;
using Avalonia.Styling;
using Microsoft.Extensions.Logging;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// Applies the theme preference from <see cref="SettingsStore"/> to the Avalonia application
/// and persists changes, using the Fluent theme variants (light/dark).
/// </summary>
public sealed class ThemeManager(SettingsStore settings, ILogger<ThemeManager> logger)
{
    private readonly SettingsStore _settings = settings;
    private readonly ILogger<ThemeManager> _logger = logger;

    /// <summary>The current theme preference.</summary>
    public ThemePreference Current => _settings.ThemePreference;

    /// <summary>Applies the saved theme preference to <see cref="Application.Current"/>.</summary>
    public void ApplySaved() => ApplyVariant(_settings.ThemePreference);

    /// <summary>Sets and persists the new theme preference, then applies it.</summary>
    public void Set(ThemePreference preference)
    {
        _settings.ThemePreference = preference;
        _settings.Save();
        ApplyVariant(preference);
        _logger.LogInformation("Theme changed to {Theme}", preference);
    }

    /// <summary>Cycles System → Light → Dark → System.</summary>
    public ThemePreference Cycle()
    {
        ThemePreference next = _settings.ThemePreference switch
        {
            ThemePreference.System => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.Dark,
            _ => ThemePreference.System,
        };
        Set(next);
        return next;
    }

    private static void ApplyVariant(ThemePreference preference)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
