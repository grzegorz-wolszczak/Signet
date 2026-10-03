using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Signet.App.Resources;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// Applies the UI language preference from <see cref="SettingsStore"/>. Two languages are
/// supported: Polish and English.
/// </summary>
/// <remarks>
/// <see cref="Set"/> only persists the choice; <see cref="ApplySaved"/> sets
/// <see cref="CultureInfo.CurrentUICulture"/>/<see cref="CultureInfo.DefaultThreadCurrentUICulture"/>
/// once at application startup, before anything from the UI layer is built, and again whenever the
/// language changes (e.g. after closing Preferences). It deliberately does not touch
/// <see cref="CultureInfo.CurrentCulture"/> (number/date formatting) — that stays controlled by the
/// operating system, so as not to break number parsing where the code assumes the invariant culture.
/// </remarks>
public sealed class LocalizationManager(SettingsStore settings, ILogger<LocalizationManager> logger)
{
    private readonly SettingsStore _settings = settings;
    private readonly ILogger<LocalizationManager> _logger = logger;

    /// <summary>Supported UI languages (culture code + native name shown in Preferences).</summary>
    public static readonly IReadOnlyList<(string Code, string DisplayName)> SupportedLanguages =
        new[] { ("pl", "Polski"), ("en", "English") };

    /// <summary>The current (normalized) language preference.</summary>
    public string Current => Normalize(_settings.UiLanguage);

    /// <summary>
    /// Applies the saved language preference to <see cref="CultureInfo.CurrentUICulture"/>. When the
    /// language changes (e.g. after closing Preferences), the UI switches live —
    /// <see cref="Strings.NotifyLanguageChanged"/>.
    /// </summary>
    public void ApplySaved()
    {
        string previous = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Apply(Current);
        if (!string.Equals(previous, Current, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("UI language switched to {Language}", Current);
            Strings.NotifyLanguageChanged();
        }
    }

    /// <summary>Persists the new language preference; <see cref="ApplySaved"/> applies it.</summary>
    public void Set(string languageCode)
    {
        string normalized = Normalize(languageCode);
        _settings.UiLanguage = normalized;
        _settings.Save();
        _logger.LogInformation("UI language saved as {Language}", normalized);
    }

    private static void Apply(string code)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>
    /// Reduces any culture code to one of <see cref="SupportedLanguages"/>: an exact
    /// match (<c>"pl"</c>/<c>"en"</c>/<c>"pl-PL"</c>…) wins; otherwise codes starting
    /// with <c>"pl"</c> (e.g. a system with a Polish locale) get Polish, everything else gets
    /// English as the more universal default for an open-source tool.
    /// </summary>
    private static string Normalize(string code)
    {
        if (SupportedLanguages.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            return code.ToLowerInvariant();
        }

        return code.StartsWith("pl", StringComparison.OrdinalIgnoreCase) ? "pl" : "en";
    }
}
