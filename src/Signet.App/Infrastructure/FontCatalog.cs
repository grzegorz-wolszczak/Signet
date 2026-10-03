using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace Signet.App.Infrastructure;

/// <summary>A single font installed in the system — an item of the list in the font picker dialog.</summary>
/// <param name="Name">Font family name.</param>
/// <param name="IsMonospace">Whether the font has fixed-width characters.</param>
public sealed record FontEntry(string Name, bool IsMonospace)
{
    /// <summary>Avalonia family used to render the name in that font.</summary>
    public FontFamily Family { get; } = new(Name);
}

/// <summary>
/// List of system fonts for the font picker dialog (<c>FontPickerWindow</c>). Computed once
/// (on first use) and kept in memory — reading the metrics of several hundred fonts takes noticeable time.
/// </summary>
public static class FontCatalog
{
    private static readonly Lazy<IReadOnlyList<FontEntry>> Cached = new(Load);

    /// <summary>System fonts sorted by name, with fixed-width information.</summary>
    public static IReadOnlyList<FontEntry> SystemFonts => Cached.Value;

    private static List<FontEntry> Load()
    {
        FontManager manager = FontManager.Current;
        return manager.SystemFonts
            .Select(f => f.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .Select(n => new FontEntry(n, IsFixedPitch(manager, n)))
            .ToList();
    }

    private static bool IsFixedPitch(FontManager manager, string name)
    {
        try
        {
            return manager.TryGetGlyphTypeface(new Typeface(name), out GlyphTypeface? glyphs)
                && glyphs.Metrics.IsFixedPitch;
        }
#pragma warning disable CA1031 // A corrupt font must not break the list — treat it as proportional.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
