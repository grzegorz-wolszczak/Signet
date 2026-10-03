using System;
using System.Globalization;
using System.Resources;

namespace Signet.Core.Localization;

/// <summary>
/// User-visible texts from the Core layer (messages, warnings, reference
/// catalogs). <c>CoreStrings.resx</c> is the neutral language
/// (Polish, like <c>Strings.resx</c> in App), <c>CoreStrings.en.resx</c> — the English satellite.
/// The language is chosen by <see cref="CultureInfo.CurrentUICulture"/>, which the application sets
/// (<c>LocalizationManager</c>), so Core speaks the same language as the interface.
/// </summary>
public static class CoreStrings
{
    private static readonly ResourceManager Manager =
        new("Signet.Core.Localization.CoreStrings", typeof(CoreStrings).Assembly);

    /// <summary>
    /// The translated text for <paramref name="key"/>. Throws when the key is missing even from the
    /// neutral resource (a programmer error).
    /// </summary>
    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Manager.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"Missing Core localization key: \"{key}\".");
    }

    /// <summary>Like <see cref="Get"/>, but <see langword="null"/> for a nonexistent key.</summary>
    public static string? TryGet(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Manager.GetString(key, CultureInfo.CurrentUICulture);
    }

    /// <summary>
    /// <see cref="Get"/> with argument substitution; the template per <see cref="CultureInfo.CurrentUICulture"/>,
    /// numbers per <see cref="CultureInfo.CurrentCulture"/> (like <c>Strings.Format</c> in App).
    /// </summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>The raw <see cref="ResourceManager"/> — for key consistency tests.</summary>
    public static ResourceManager RawManager => Manager;
}
