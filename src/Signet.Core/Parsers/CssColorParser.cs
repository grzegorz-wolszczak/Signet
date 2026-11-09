using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Signet.Core.Parsers;

/// <summary>An 8-bit sRGB color with alpha.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
/// <param name="A">Alpha (255 = opaque).</param>
public readonly record struct CssColor(byte R, byte G, byte B, byte A)
{
    /// <summary><c>#rrggbb</c>, or <c>#rrggbbaa</c> when the color is not opaque.</summary>
    public string ToHex() => A == 255 ? $"#{R:x2}{G:x2}{B:x2}" : $"#{R:x2}{G:x2}{B:x2}{A:x2}";

    /// <summary>The short form (<c>#f00</c>, <c>#f008</c>) when the color has one, else <see cref="ToHex"/>.</summary>
    public string ToShortHex()
    {
        bool shortable = R % 17 == 0 && G % 17 == 0 && B % 17 == 0 && A % 17 == 0;
        if (!shortable)
        {
            return ToHex();
        }

        return A == 255 ? $"#{R / 17:x}{G / 17:x}{B / 17:x}" : $"#{R / 17:x}{G / 17:x}{B / 17:x}{A / 17:x}";
    }

    /// <summary>The hex text.</summary>
    public override string ToString() => ToHex();
}

/// <summary>
/// Reads a CSS color (CSS Color Module Level 4) as 8-bit sRGB: hex notations, <c>rgb()</c>/<c>rgba()</c>, <c>hsl()</c>/<c>hsla()</c>,
/// <c>hwb()</c>, <c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c>, <c>color()</c> in the spaces <c>srgb</c>, <c>srgb-linear</c> and
/// <c>display-p3</c>, the named colors and <c>transparent</c>. The values are Calibre's tests of the same notations (the 8-bit values Chromium
/// renders); out-of-gamut colors are clipped. A color the text cannot be read as (a variable, <c>calc()</c>, <c>currentColor</c>, a system
/// color) is refused.
/// </summary>
public static class CssColorParser
{
    private const double D50X = 0.3457 / 0.3585;
    private const double D50Z = (1.0 - 0.3457 - 0.3585) / 0.3585;

    private static readonly Dictionary<string, CssColor> Named = BuildNamed();

    private static readonly HashSet<string> FunctionNames = new(StringComparer.Ordinal)
    {
        "rgb", "rgba", "hsl", "hsla", "hwb", "lab", "lch", "oklab", "oklch", "color",
    };

    /// <summary>Whether <paramref name="name"/> is the name of a color (<c>rebeccapurple</c>, any letter case) or <c>transparent</c>.</summary>
    public static bool IsColorName(string name) => Named.ContainsKey(name.ToLowerInvariant());

    /// <summary>The name of a color that has exactly this value (<c>red</c>), or <c>null</c>.</summary>
    public static string? NameOf(CssColor color) =>
        color.A == 255 ? Named.Where(p => p.Value == color && p.Key is not "transparent").Select(p => p.Key).OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).FirstOrDefault() : null;

    /// <summary>
    /// Whether <paramref name="text"/> is a function of the color notations (<c>rgb(</c>, <c>oklch(</c>, <c>color(</c>…) with its parenthesis closed —
    /// whether or not its arguments make a color this parser can convert.
    /// </summary>
    public static bool IsColorFunction(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string clean = StripComments(text).Trim();
        int open = clean.IndexOf('(', StringComparison.Ordinal);
        return open > 0 && clean.EndsWith(')') && FunctionNames.Contains(clean[..open].ToLowerInvariant());
    }

    /// <summary>Reads <paramref name="text"/> as a color; <c>false</c> when it is not one.</summary>
    public static bool TryParse(string text, out CssColor color)
    {
        ArgumentNullException.ThrowIfNull(text);
        color = default;
        string clean = StripComments(text).Trim();
        if (clean.Length == 0)
        {
            return false;
        }

        if (clean[0] == '#')
        {
            return TryParseHex(clean[1..], out color);
        }

        int open = clean.IndexOf('(', StringComparison.Ordinal);
        if (open < 0)
        {
            return Named.TryGetValue(clean.ToLowerInvariant(), out color);
        }

        string name = clean[..open].ToLowerInvariant();
        if (!FunctionNames.Contains(name))
        {
            return false;
        }

        string rest = clean[(open + 1)..];
        int close = rest.IndexOf(')', StringComparison.Ordinal);
        if (close >= 0)
        {
            if (rest[(close + 1)..].Trim().Length > 0)
            {
                return false;
            }

            rest = rest[..close];
        }

        return rest.Contains('(', StringComparison.Ordinal) ? false : TryParseFunction(name, rest, out color);
    }

    // --- hex ---

    private static bool TryParseHex(string digits, out CssColor color)
    {
        color = default;
        if (digits.Length is not (3 or 4 or 6 or 8) || !digits.All(Uri.IsHexDigit))
        {
            return false;
        }

        int Hex(int i, int length) => int.Parse(digits.AsSpan(i, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (digits.Length <= 4)
        {
            byte Short(int i) => (byte)(Hex(i, 1) * 17);
            color = new CssColor(Short(0), Short(1), Short(2), digits.Length == 4 ? Short(3) : (byte)255);
        }
        else
        {
            color = new CssColor((byte)Hex(0, 2), (byte)Hex(2, 2), (byte)Hex(4, 2), digits.Length == 8 ? (byte)Hex(6, 2) : (byte)255);
        }

        return true;
    }

    // --- functions ---

    // One argument: a number, with or without a unit (% included), or none.
    private readonly record struct Argument(double Value, string Unit, bool IsNone)
    {
        public bool IsPercentage => Unit == "%";

        public bool IsPlainNumber => !IsNone && Unit.Length == 0;
    }

    private static bool TryParseFunction(string name, string arguments, out CssColor color)
    {
        color = default;
        if (!Split(arguments, out List<string> tokens, out bool commas, out bool slash))
        {
            return false;
        }

        string space = string.Empty;
        if (name == "color")
        {
            if (commas || tokens.Count == 0)
            {
                return false;
            }

            space = tokens[0].ToLowerInvariant();
            tokens.RemoveAt(0);
        }

        bool alphaGiven = slash;
        int channels = 3;
        int expected = channels + (alphaGiven ? 1 : 0);
        bool legacyAlpha = commas && tokens.Count == 4;
        if (tokens.Count != expected && !legacyAlpha)
        {
            return false;
        }

        List<Argument> values = new();
        foreach (string token in tokens)
        {
            if (!TryArgument(token, out Argument argument))
            {
                return false;
            }

            values.Add(argument);
        }

        if (!TryAlpha(values.Count == 4 ? values[3] : null, out double alpha, allowNone: !commas))
        {
            return false;
        }

        Argument a = values[0];
        Argument b = values[1];
        Argument c = values[2];
        double red;
        double green;
        double blue;
        switch (name)
        {
            case "rgb" or "rgba":
                if (commas && (a.IsNone || b.IsNone || c.IsNone || a.IsPercentage != b.IsPercentage || b.IsPercentage != c.IsPercentage))
                {
                    return false;
                }

                if (!TryRgb(a, out red) || !TryRgb(b, out green) || !TryRgb(c, out blue))
                {
                    return false;
                }

                break;
            case "hsl" or "hsla":
                if (!TryHue(a, out double hue) || !TryPercentOrNumber(b, commas, out double saturation) || !TryPercentOrNumber(c, commas, out double lightness))
                {
                    return false;
                }

                (red, green, blue) = HslToSrgb(hue, Math.Max(0, saturation) / 100.0, lightness / 100.0);
                break;
            case "hwb":
                if (commas || !TryHue(a, out double hwbHue) || !TryPercentOrNumber(b, false, out double white) || !TryPercentOrNumber(c, false, out double black))
                {
                    return false;
                }

                (red, green, blue) = HwbToSrgb(hwbHue, white / 100.0, black / 100.0);
                break;
            case "lab" or "lch" or "oklab" or "oklch":
                if (commas || !TryPerceptual(name, a, b, c, out red, out green, out blue))
                {
                    return false;
                }

                break;
            case "color":
                if (commas || !TryColorSpace(space, a, b, c, out red, out green, out blue))
                {
                    return false;
                }

                break;
            default:
                return false;
        }

        color = new CssColor(To8Bit(red), To8Bit(green), To8Bit(blue), To8Bit(alpha));
        return true;
    }

    // The arguments split at white space, commas and the slash before the alpha. A comma list is the legacy syntax and cannot have a slash.
    private static bool Split(string arguments, out List<string> tokens, out bool commas, out bool slash)
    {
        tokens = new List<string>();
        commas = arguments.Contains(',', StringComparison.Ordinal);
        slash = arguments.Contains('/', StringComparison.Ordinal);
        if (commas && slash)
        {
            return false;
        }

        if (slash)
        {
            string[] parts = arguments.Split('/');
            if (parts.Length != 2)
            {
                return false;
            }

            string[] before = parts[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string[] after = parts[1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (after.Length != 1)
            {
                return false;
            }

            tokens.AddRange(before);
            tokens.Add(after[0]);
            return true;
        }

        string[] items = commas ? arguments.Split(',') : arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (string item in items)
        {
            string trimmed = item.Trim();
            if (trimmed.Length == 0 || (commas && trimmed.Contains(' ', StringComparison.Ordinal)))
            {
                return false;
            }

            tokens.Add(trimmed);
        }

        return true;
    }

    private static bool TryArgument(string token, out Argument argument)
    {
        argument = default;
        if (token.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            argument = new Argument(0, string.Empty, true);
            return true;
        }

        int i = 0;
        if (i < token.Length && token[i] is '+' or '-')
        {
            i++;
        }

        int digitsStart = i;
        while (i < token.Length && char.IsAsciiDigit(token[i]))
        {
            i++;
        }

        if (i < token.Length && token[i] == '.')
        {
            i++;
            while (i < token.Length && char.IsAsciiDigit(token[i]))
            {
                i++;
            }
        }

        if (i == digitsStart || (i == digitsStart + 1 && token[digitsStart] == '.'))
        {
            return false;
        }

        if (i < token.Length && token[i] is 'e' or 'E' && i + 1 < token.Length && (char.IsAsciiDigit(token[i + 1]) || (token[i + 1] is '+' or '-' && i + 2 < token.Length && char.IsAsciiDigit(token[i + 2]))))
        {
            i += 2;
            while (i < token.Length && char.IsAsciiDigit(token[i]))
            {
                i++;
            }
        }

        if (!double.TryParse(token.AsSpan(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return false;
        }

        string unit = token[i..].ToLowerInvariant();
        if (unit.Length > 0 && unit is not ("%" or "deg" or "rad" or "grad" or "turn" or "px"))
        {
            return false;
        }

        argument = new Argument(value, unit, false);
        return true;
    }

    private static bool TryAlpha(Argument? argument, out double alpha, bool allowNone)
    {
        alpha = 1;
        if (argument is not { } a)
        {
            return true;
        }

        if (a.IsNone)
        {
            alpha = 0;
            return allowNone;
        }

        if (a.Unit is not ("" or "%"))
        {
            return false;
        }

        alpha = Math.Clamp(a.IsPercentage ? a.Value / 100.0 : a.Value, 0, 1);
        return true;
    }

    private static bool TryRgb(Argument a, out double channel)
    {
        channel = 0;
        if (a.IsNone)
        {
            channel = 0;
            return true;
        }

        if (a.Unit is not ("" or "%"))
        {
            return false;
        }

        channel = Math.Clamp(a.IsPercentage ? a.Value / 100.0 : a.Value / 255.0, 0, 1);
        return true;
    }

    private static bool TryHue(Argument a, out double degrees)
    {
        degrees = 0;
        if (a.IsNone)
        {
            return true;
        }

        switch (a.Unit)
        {
            case "" or "deg":
                degrees = a.Value;
                return true;
            case "rad":
                degrees = a.Value * 180.0 / Math.PI;
                return true;
            case "grad":
                degrees = a.Value * 0.9;
                return true;
            case "turn":
                degrees = a.Value * 360.0;
                return true;
            default:
                return false;
        }
    }

    // A percentage; a plain number counts as one in the modern syntax, not in the legacy (comma) one.
    private static bool TryPercentOrNumber(Argument a, bool legacy, out double percent)
    {
        percent = 0;
        if (a.IsNone)
        {
            return !legacy;
        }

        if (a.IsPercentage || (a.IsPlainNumber && !legacy))
        {
            percent = a.Value;
            return true;
        }

        return false;
    }

    private static bool TryPerceptual(string name, Argument a, Argument b, Argument c, out double red, out double green, out double blue)
    {
        bool ok = true;
        double Component(Argument x, double percentScale, bool angle = false)
        {
            if (x.IsNone)
            {
                return 0;
            }

            if (angle)
            {
                ok &= TryHue(x, out double degrees);
                return degrees;
            }

            if (x.Unit is not ("" or "%"))
            {
                ok = false;
            }

            return x.IsPercentage ? x.Value / 100.0 * percentScale : x.Value;
        }

        double r;
        double g;
        double bl;
        switch (name)
        {
            case "lab":
            {
                double l = Component(a, 100);
                (r, g, bl) = LabToSrgb(l, Component(b, 125), Component(c, 125));
                break;
            }

            case "lch":
            {
                double l = Component(a, 100);
                double chroma = Math.Max(0, Component(b, 150));
                double hue = Component(c, 0, angle: true) * Math.PI / 180.0;
                (r, g, bl) = LabToSrgb(l, chroma * Math.Cos(hue), chroma * Math.Sin(hue));
                break;
            }

            case "oklab":
            {
                double l = Component(a, 1);
                (r, g, bl) = OklabToSrgb(l, Component(b, 0.4), Component(c, 0.4));
                break;
            }

            default:
            {
                double l = Component(a, 1);
                double chroma = Math.Max(0, Component(b, 0.4));
                double hue = Component(c, 0, angle: true) * Math.PI / 180.0;
                (r, g, bl) = OklabToSrgb(l, chroma * Math.Cos(hue), chroma * Math.Sin(hue));
                break;
            }
        }

        (red, green, blue) = (r, g, bl);
        return ok;
    }

    private static bool TryColorSpace(string space, Argument a, Argument b, Argument c, out double red, out double green, out double blue)
    {
        red = green = blue = 0;
        double[] values = new double[3];
        Argument[] arguments = [a, b, c];
        for (int i = 0; i < 3; i++)
        {
            Argument x = arguments[i];
            if (!x.IsNone && x.Unit is not ("" or "%"))
            {
                return false;
            }

            values[i] = x.IsNone ? 0 : x.IsPercentage ? x.Value / 100.0 : x.Value;
        }

        switch (space)
        {
            case "srgb":
                (red, green, blue) = (Math.Clamp(values[0], 0, 1), Math.Clamp(values[1], 0, 1), Math.Clamp(values[2], 0, 1));
                return true;
            case "srgb-linear":
                (red, green, blue) = (Encode(values[0]), Encode(values[1]), Encode(values[2]));
                return true;
            case "display-p3":
            {
                double lr = Decode(values[0]);
                double lg = Decode(values[1]);
                double lb = Decode(values[2]);
                double x = (0.4865709486482162 * lr) + (0.26566769316909306 * lg) + (0.1982172852343625 * lb);
                double y = (0.2289745640697488 * lr) + (0.6917385218365064 * lg) + (0.079286914093745 * lb);
                double z = (0.04511338185890264 * lg) + (1.043944368900976 * lb);
                (red, green, blue) = XyzD65ToSrgb(x, y, z);
                return true;
            }

            default:
                return false;
        }
    }

    // --- color spaces ---

    private static (double, double, double) HslToSrgb(double hueDegrees, double saturation, double lightness)
    {
        double hue = ((hueDegrees % 360) + 360) % 360;
        double Channel(double n)
        {
            double k = (n + (hue / 30)) % 12;
            double a = saturation * Math.Min(lightness, 1 - lightness);
            return Math.Clamp(lightness - (a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1))), 0, 1);
        }

        return (Channel(0), Channel(8), Channel(4));
    }

    private static (double, double, double) HwbToSrgb(double hueDegrees, double white, double black)
    {
        white = Math.Max(0, white);
        black = Math.Max(0, black);
        if (white + black >= 1)
        {
            double gray = white / (white + black);
            return (gray, gray, gray);
        }

        (double r, double g, double b) = HslToSrgb(hueDegrees, 1, 0.5);
        double scale = 1 - white - black;
        return ((r * scale) + white, (g * scale) + white, (b * scale) + white);
    }

    private static (double, double, double) LabToSrgb(double lightness, double a, double b)
    {
        const double epsilon = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        double fy = (lightness + 16) / 116;
        double fx = (a / 500) + fy;
        double fz = fy - (b / 200);
        double x = (fx * fx * fx > epsilon ? fx * fx * fx : ((116 * fx) - 16) / kappa) * D50X;
        double y = lightness > kappa * epsilon ? Math.Pow((lightness + 16) / 116, 3) : lightness / kappa;
        double z = (fz * fz * fz > epsilon ? fz * fz * fz : ((116 * fz) - 16) / kappa) * D50Z;

        // The Bradford adaptation of D50 to D65.
        double x65 = (0.9554734527042182 * x) + (-0.023098536874261423 * y) + (0.0632593086610217 * z);
        double y65 = (-0.028369706963208136 * x) + (1.0099954580058226 * y) + (0.021041398966943008 * z);
        double z65 = (0.012314001688319899 * x) + (-0.020507696433477912 * y) + (1.3303659366080753 * z);
        return XyzD65ToSrgb(x65, y65, z65);
    }

    private static (double, double, double) OklabToSrgb(double lightness, double a, double b)
    {
        double l = Math.Pow(lightness + (0.3963377774 * a) + (0.2158037573 * b), 3);
        double m = Math.Pow(lightness - (0.1055613458 * a) - (0.0638541728 * b), 3);
        double s = Math.Pow(lightness - (0.0894841775 * a) - (1.2914855480 * b), 3);
        return (
            Encode((4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s)),
            Encode((-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s)),
            Encode((-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s)));
    }

    private static (double, double, double) XyzD65ToSrgb(double x, double y, double z) => (
        Encode((3.2409699419045226 * x) + (-1.537383177570094 * y) + (-0.4986107602930034 * z)),
        Encode((-0.9692436362808796 * x) + (1.8759675015077202 * y) + (0.04155505740717559 * z)),
        Encode((0.05563007969699366 * x) + (-0.20397695888897652 * y) + (1.0569715142428786 * z)));

    // Linear light to the sRGB transfer curve, clipped to the gamut.
    private static double Encode(double linear)
    {
        double v = Math.Abs(linear);
        double encoded = v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;
        return Math.Clamp(Math.Sign(linear) * encoded, 0, 1);
    }

    private static double Decode(double encoded)
    {
        double v = Math.Abs(encoded);
        double linear = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return Math.Sign(encoded) * linear;
    }

    private static byte To8Bit(double unit) => (byte)Math.Clamp(Math.Round(unit * 255, MidpointRounding.AwayFromZero), 0, 255);

    // --- comments ---

    private static string StripComments(string text)
    {
        if (!text.Contains("/*", StringComparison.Ordinal))
        {
            return text;
        }

        StringBuilder sb = new();
        int i = 0;
        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                sb.Append(' ');
                if (end < 0)
                {
                    break;
                }

                i = end + 2;
                continue;
            }

            sb.Append(text[i++]);
        }

        return sb.ToString();
    }

    // --- the names ---

    private static Dictionary<string, CssColor> BuildNamed()
    {
        const string Table =
            "aliceblue f0f8ff antiquewhite faebd7 aqua 00ffff aquamarine 7fffd4 azure f0ffff beige f5f5dc bisque ffe4c4 black 000000 "
            + "blanchedalmond ffebcd blue 0000ff blueviolet 8a2be2 brown a52a2a burlywood deb887 cadetblue 5f9ea0 chartreuse 7fff00 "
            + "chocolate d2691e coral ff7f50 cornflowerblue 6495ed cornsilk fff8dc crimson dc143c cyan 00ffff darkblue 00008b darkcyan 008b8b "
            + "darkgoldenrod b8860b darkgray a9a9a9 darkgreen 006400 darkgrey a9a9a9 darkkhaki bdb76b darkmagenta 8b008b darkolivegreen 556b2f "
            + "darkorange ff8c00 darkorchid 9932cc darkred 8b0000 darksalmon e9967a darkseagreen 8fbc8f darkslateblue 483d8b "
            + "darkslategray 2f4f4f darkslategrey 2f4f4f darkturquoise 00ced1 darkviolet 9400d3 deeppink ff1493 deepskyblue 00bfff "
            + "dimgray 696969 dimgrey 696969 dodgerblue 1e90ff firebrick b22222 floralwhite fffaf0 forestgreen 228b22 fuchsia ff00ff "
            + "gainsboro dcdcdc ghostwhite f8f8ff gold ffd700 goldenrod daa520 gray 808080 green 008000 greenyellow adff2f grey 808080 "
            + "honeydew f0fff0 hotpink ff69b4 indianred cd5c5c indigo 4b0082 ivory fffff0 khaki f0e68c lavender e6e6fa lavenderblush fff0f5 "
            + "lawngreen 7cfc00 lemonchiffon fffacd lightblue add8e6 lightcoral f08080 lightcyan e0ffff lightgoldenrodyellow fafad2 "
            + "lightgray d3d3d3 lightgreen 90ee90 lightgrey d3d3d3 lightpink ffb6c1 lightsalmon ffa07a lightseagreen 20b2aa lightskyblue 87cefa "
            + "lightslategray 778899 lightslategrey 778899 lightsteelblue b0c4de lightyellow ffffe0 lime 00ff00 limegreen 32cd32 linen faf0e6 "
            + "magenta ff00ff maroon 800000 mediumaquamarine 66cdaa mediumblue 0000cd mediumorchid ba55d3 mediumpurple 9370db "
            + "mediumseagreen 3cb371 mediumslateblue 7b68ee mediumspringgreen 00fa9a mediumturquoise 48d1cc mediumvioletred c71585 "
            + "midnightblue 191970 mintcream f5fffa mistyrose ffe4e1 moccasin ffe4b5 navajowhite ffdead navy 000080 oldlace fdf5e6 olive 808000 "
            + "olivedrab 6b8e23 orange ffa500 orangered ff4500 orchid da70d6 palegoldenrod eee8aa palegreen 98fb98 paleturquoise afeeee "
            + "palevioletred db7093 papayawhip ffefd5 peachpuff ffdab9 peru cd853f pink ffc0cb plum dda0dd powderblue b0e0e6 purple 800080 "
            + "rebeccapurple 663399 red ff0000 rosybrown bc8f8f royalblue 4169e1 saddlebrown 8b4513 salmon fa8072 sandybrown f4a460 "
            + "seagreen 2e8b57 seashell fff5ee sienna a0522d silver c0c0c0 skyblue 87ceeb slateblue 6a5acd slategray 708090 slategrey 708090 "
            + "snow fffafa springgreen 00ff7f steelblue 4682b4 tan d2b48c teal 008080 thistle d8bfd8 tomato ff6347 turquoise 40e0d0 "
            + "violet ee82ee wheat f5deb3 white ffffff whitesmoke f5f5f5 yellow ffff00 yellowgreen 9acd32";
        Dictionary<string, CssColor> map = new(StringComparer.Ordinal);
        string[] words = Table.Split(' ');
        for (int i = 0; i < words.Length; i += 2)
        {
            if (TryParseHex(words[i + 1], out CssColor color))
            {
                map[words[i]] = color;
            }
        }

        map["transparent"] = new CssColor(0, 0, 0, 0);
        return map;
    }
}
