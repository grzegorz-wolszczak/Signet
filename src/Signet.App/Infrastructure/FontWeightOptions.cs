using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Signet.App.Resources;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>A weight in the list of the font picker and of the "Fonts" pages ("Light", "SemiBold"…).</summary>
/// <param name="Value">The weight (<see cref="FontWeightScale"/>); 0 = not set.</param>
/// <param name="Name">The name shown: the one fonts give the weight, or "(default)" for 0.</param>
public sealed record FontWeightOption(int Value, string Name)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>The weights a font offers, as the options of the weight lists, and the conversion to Avalonia's weights.</summary>
public static class FontWeightOptions
{
    /// <summary>The option "not set": the place the font is used in keeps its own weight.</summary>
    public static FontWeightOption Default => new(FontWeightScale.Default, Strings.Get("FontWeight_Default"));

    /// <summary>
    /// The options for a font: "(default)" first, then the weights the font has (Light, Regular, SemiBold…, lightest first;
    /// see <see cref="FontCatalog.WeightsOf"/>), and <paramref name="current"/> too when the font has no such weight (a
    /// stored weight must stay selectable, and visible, to be changed).
    /// </summary>
    /// <param name="family">The typeface (a fallback list "A, B" — the first one counts); empty = the interface font.</param>
    /// <param name="current">The weight stored now.</param>
    public static IReadOnlyList<FontWeightOption> For(string family, int current)
    {
        int stored = FontWeightScale.Normalize(current);
        IEnumerable<int> weights = FontCatalog.WeightsOf(family);
        if (stored > 0 && !weights.Contains(stored))
        {
            weights = weights.Append(stored).OrderBy(w => w);
        }

        return weights.Select(w => new FontWeightOption(w, FontWeightScale.NameOf(w)!)).Prepend(Default).ToArray();
    }

    /// <summary>The Avalonia weight of a stored one; 0 (not set) is Regular.</summary>
    public static FontWeight ToAvalonia(int weight) => weight <= 0 ? FontWeight.Normal : (FontWeight)weight;

    /// <summary>Stored weight → <see cref="FontWeight"/> for a binding (0 = Regular).</summary>
    public static readonly IValueConverter FromNumber =
        new FuncValueConverter<int, FontWeight>(ToAvalonia);
}
