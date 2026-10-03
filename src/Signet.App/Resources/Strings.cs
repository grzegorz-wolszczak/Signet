using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;

namespace Signet.App.Resources;

/// <summary>
/// Access to the UI text resources — a thin wrapper over <see cref="ResourceManager"/>
/// on <c>Strings.resx</c> (neutral language — Polish) and the satellite <c>Strings.en.resx</c>
/// (English). There is no Visual Studio–generated <c>Strings.Designer.cs</c> class — this project is
/// built exclusively with <c>dotnet build</c> from the command line, where the <c>ResXFileCodeGenerator</c>
/// custom tool does not run; instead of a property per key there is a single dynamic <see cref="Get"/>.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("Signet.App.Resources.Strings", typeof(Strings).Assembly);

    // Language change listeners — weak references so that registration does not extend the lifetime of view models.
    private static readonly List<WeakReference<ILanguageAware>> Listeners = new();

    /// <summary>
    /// Registers an object that built its texts once (menus, actions, toolbars, panels) and must
    /// recompute them when the language is switched while the application is running. Texts in XAML
    /// (<see cref="LocExtension"/>) refresh themselves through <see cref="Localizer"/>.
    /// </summary>
    public static void RegisterLanguageAware(ILanguageAware listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (Listeners)
        {
            Listeners.Add(new WeakReference<ILanguageAware>(listener));
        }
    }

    /// <summary>
    /// Announces a change of <see cref="CultureInfo.CurrentUICulture"/>: refreshes XAML bindings
    /// and calls <see cref="ILanguageAware.OnLanguageChanged"/> on live listeners.
    /// </summary>
    public static void NotifyLanguageChanged()
    {
        Localizer.Instance.Refresh();

        List<ILanguageAware> alive = new();
        lock (Listeners)
        {
            Listeners.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (WeakReference<ILanguageAware> weak in Listeners)
            {
                if (weak.TryGetTarget(out ILanguageAware? listener))
                {
                    alive.Add(listener);
                }
            }
        }

        foreach (ILanguageAware listener in alive)
        {
            listener.OnLanguageChanged();
        }
    }

    /// <summary>
    /// Returns the translated text for <paramref name="key"/> according to <see cref="CultureInfo.CurrentUICulture"/>
    /// (a satellite without the key falls back to the neutral language — Polish). Throws when the key is missing
    /// even from the neutral resource (a programmer error, not a user error).
    /// </summary>
    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Manager.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"Missing localization resource key: \"{key}\".");
    }

    /// <summary>
    /// Like <see cref="Get"/>, but for a key that may be missing (texts built from data, e.g.
    /// menu actions) — returns <see langword="null"/> instead of throwing.
    /// </summary>
    public static string? TryGet(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Manager.GetString(key, CultureInfo.CurrentUICulture);
    }

    /// <summary>
    /// Equivalent of <see cref="Get"/> with argument substitution. The template is chosen by
    /// <see cref="CultureInfo.CurrentUICulture"/> (like <see cref="Get"/>), but the arguments themselves
    /// (numbers/dates) are formatted by <see cref="CultureInfo.CurrentCulture"/> — the choice of UI language
    /// must not affect number formatting (CA1305).
    /// </summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>Access to the raw <see cref="ResourceManager"/> (scanner in <c>LocalizationTests</c>).</summary>
    public static ResourceManager RawManager => Manager;
}
