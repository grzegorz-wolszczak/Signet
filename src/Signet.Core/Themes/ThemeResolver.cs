using System;
using System.Collections.Generic;

namespace Signet.Core.Themes;

/// <summary>
/// The values a theme gives its keys, with both kinds of inheritance resolved:
/// <list type="number">
/// <item>the theme's own entry — an "inherit" entry takes the value of the key's base, resolved again from the theme
/// asked about (so a base the user changed counts);</item>
/// <item>otherwise the parent's entry, and so on up to the bundled theme;</item>
/// <item>otherwise the default of the key (<see cref="ThemeKeys"/>).</item>
/// </list>
/// The base of a key counts only through an "inherit" entry: a key no theme sets keeps its own default, so Signet
/// Light (which sets nothing) shows the colors of the registry.
/// </summary>
public sealed class ThemeResolver
{
    private readonly Func<string, Theme?> _themes;

    /// <summary>Creates a resolver.</summary>
    /// <param name="themes">Finds a theme by its identifier (for the parents); <c>null</c> = there is no such theme.</param>
    public ThemeResolver(Func<string, Theme?> themes) =>
        _themes = themes ?? throw new ArgumentNullException(nameof(themes));

    /// <summary>The color of a color key in the theme; <c>null</c> = the platform's (e.g. the system accent).</summary>
    /// <exception cref="ArgumentException">The key is not a color key.</exception>
    public ThemeColor? Color(Theme theme, ThemeKey key)
    {
        RequireKind(key, ThemeKeyKind.Color);
        return Resolve(theme, key).Color;
    }

    /// <summary>The attributes of a text attributes key in the theme.</summary>
    /// <exception cref="ArgumentException">The key is not a text attributes key.</exception>
    public TextAttributes Attributes(Theme theme, ThemeKey key)
    {
        RequireKind(key, ThemeKeyKind.TextAttributes);
        return Resolve(theme, key).Attributes ?? TextAttributes.Empty;
    }

    /// <summary>The variant of the interface of the theme (its own, a parent's, or light).</summary>
    public InterfaceVariant Variant(Theme theme)
    {
        foreach (Theme t in Lineage(theme))
        {
            if (t.Variant is { } variant)
            {
                return variant;
            }
        }

        return InterfaceVariant.Light;
    }

    /// <summary>The icon set of the theme (its own or a parent's); <c>null</c> = the one chosen in Preferences.</summary>
    public string? IconSet(Theme theme)
    {
        foreach (Theme t in Lineage(theme))
        {
            if (t.IconSet is { } iconSet)
            {
                return iconSet;
            }
        }

        return null;
    }

    /// <summary>The font the theme (or the nearest parent) sets for a place (<see cref="ThemeFontKeys"/>); <c>null</c> = none.</summary>
    public ThemeFont? Font(Theme theme, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        foreach (Theme t in Lineage(theme))
        {
            if (t.Fonts.TryGetValue(key, out ThemeFont font))
            {
                return font;
            }
        }

        return null;
    }

    /// <summary>
    /// The theme the value of the key comes from: the theme asked about or one of its parents — for the editor (whether
    /// the user changed the key in this theme).
    /// </summary>
    /// <returns>The theme with the entry, or <c>null</c> when none in the lineage has one.</returns>
    public Theme? Owner(Theme theme, ThemeKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        foreach (Theme t in Lineage(theme))
        {
            if (t.Entries.ContainsKey(key.Id))
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>The theme followed by its parents (a missing parent or a cycle ends the line).</summary>
    public IEnumerable<Theme> Lineage(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (Theme? t = theme; t is not null && seen.Add(t.Id); t = t.ParentId is { } parent ? _themes(parent) : null)
        {
            yield return t;
        }
    }

    private ThemeEntry Resolve(Theme theme, ThemeKey key)
    {
        foreach (Theme t in Lineage(theme))
        {
            if (t.Entries.TryGetValue(key.Id, out ThemeEntry? entry))
            {
                return entry.Inherit ? Base(theme, key) : Complete(entry, key);
            }
        }

        return Default(key);
    }

    // The value of the base of the key in the theme; the default of the key when it has no base. The registry has no
    // cycles of bases (checked when it is built).
    private ThemeEntry Base(Theme theme, ThemeKey key) =>
        key.BaseId is { } baseId ? Resolve(theme, ThemeKeys.Get(baseId)) : Default(key);

    private static ThemeEntry Default(ThemeKey key) =>
        key.Kind == ThemeKeyKind.Color ? ThemeEntry.Of(key.DefaultColor) : ThemeEntry.Of(key.DefaultAttributes!);

    // An entry of the other kind than the key (e.g. a color written for an attributes key) counts as the default.
    private static ThemeEntry Complete(ThemeEntry entry, ThemeKey key) =>
        key.Kind == ThemeKeyKind.TextAttributes && entry.Attributes is null ? Default(key) : entry;

    private static void RequireKind(ThemeKey key, ThemeKeyKind kind)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Kind != kind)
        {
            throw new ArgumentException($"The theme key '{key.Id}' holds {key.Kind}, not {kind}.", nameof(key));
        }
    }
}
