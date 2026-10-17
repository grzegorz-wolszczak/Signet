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

    /// <summary>The Avalonia weight of a stored one; 0 (not set) is Regular.</summary>
    public static FontWeight ToAvalonia(int weight) => weight <= 0 ? FontWeight.Normal : (FontWeight)weight;

    /// <summary>Stored weight → <see cref="FontWeight"/> for a binding (0 = Regular).</summary>
    public static readonly IValueConverter FromNumber =
        new FuncValueConverter<int, FontWeight>(ToAvalonia);
}
