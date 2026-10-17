using System;
using System.Collections.Generic;

namespace Signet.Core.Misc;

/// <summary>
/// The weights ("grubości") of a font as the settings store them: the numbers of the OpenType <c>wght</c> scale
/// (100 Thin … 900 Black), 0 meaning "not set" — the place the font is used in keeps its own weight (the interface text
/// Regular, a column header what its theme says).
/// </summary>
public static class FontWeightScale
{
    /// <summary>The value of a weight that is not set.</summary>
    public const int Default = 0;

    /// <summary>The lightest weight that can be stored.</summary>
    public const int Min = 100;

    /// <summary>The heaviest weight that can be stored.</summary>
    public const int Max = 950;

    /// <summary>The weights fonts are named after, lightest first (the numbers of the OpenType scale, plus SemiLight).</summary>
    public static IReadOnlyList<int> Standard { get; } = new[] { 100, 200, 300, 350, 400, 500, 600, 700, 800, 900, 950 };

    /// <summary>A stored weight made valid: 0 stays "not set", anything else is clamped to <see cref="Min"/>–<see cref="Max"/>.</summary>
    public static int Normalize(int weight) => weight <= 0 ? Default : Math.Clamp(weight, Min, Max);

    /// <summary>
    /// The name fonts give a weight (Thin, ExtraLight, Light, SemiLight, Regular, Medium, SemiBold, Bold, ExtraBold, Black,
    /// ExtraBlack); a weight between the standard ones is shown as its number. These are the names of the font files, the same
    /// in every language; <c>null</c> for <see cref="Default"/>.
    /// </summary>
    public static string? NameOf(int weight) => weight switch
    {
        <= 0 => null,
        100 => "Thin",
        200 => "ExtraLight",
        300 => "Light",
        350 => "SemiLight",
        400 => "Regular",
        500 => "Medium",
        600 => "SemiBold",
        700 => "Bold",
        800 => "ExtraBold",
        900 => "Black",
        950 => "ExtraBlack",
        _ => weight.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
