using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Signet.Core.Misc;

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

    // The weights found per family: reading them asks the font manager for every weight of the scale.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<int>> WeightsByFamily =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The weights a font has (the numbers of <see cref="FontWeightScale"/>), lightest first: Light, Regular, SemiBold… as
    /// the files of the family provide them. Found by asking the font manager for each weight of the scale and keeping the
    /// distinct weights of the faces it returns (it answers with the nearest face), so a weight the family lacks is not
    /// offered. A family it cannot read offers Regular and Bold.
    /// </summary>
    /// <param name="family">The typeface (a fallback list "A, B" — the first one counts); empty = the interface font.</param>
    public static IReadOnlyList<int> WeightsOf(string? family)
    {
        string name = (family ?? string.Empty).Split(',')[0].Trim().Trim('"', '\'');
        try
        {
            if (name.Length == 0)
            {
                name = FontManager.Current.DefaultFontFamily.Name;
            }

            return WeightsByFamily.GetOrAdd(name, ReadWeights);
        }
        catch (InvalidOperationException)
        {
            // No font manager (the application is not started, e.g. in tests of the view models): Regular and Bold.
            return DefaultWeights;
        }
    }

    private static readonly int[] DefaultWeights = { (int)FontWeight.Normal, (int)FontWeight.Bold };

    private static IReadOnlyList<int> ReadWeights(string name)
    {
        FontManager manager = FontManager.Current;
        SortedSet<int> found = new();
        foreach (int weight in FontWeightScale.Standard)
        {
            try
            {
                if (manager.TryGetGlyphTypeface(new Typeface(name, FontStyle.Normal, (FontWeight)weight), out GlyphTypeface? glyphs))
                {
                    found.Add((int)glyphs.Weight);
                }
            }
#pragma warning disable CA1031 // A corrupt font must not break the list — it just offers no weight.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        return found.Count > 0 ? found.ToArray() : DefaultWeights;
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
