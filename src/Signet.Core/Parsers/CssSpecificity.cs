using System;
using System.Text.RegularExpressions;

namespace Signet.Core.Parsers;

/// <summary>
/// The specificity of a CSS selector per the standard algorithm (id / class+attribute+pseudo-class /
/// element+pseudo-element), extended with <see cref="Origin"/> to represent an inline style
/// (<c>style="..."</c>), which per the CSS specification beats EVERY selector regardless of
/// its specificity — the logic behind the "Live CSS Panel".
/// </summary>
/// <remarks>
/// <b>A deliberate simplification:</b> <see cref="ForSelector"/> is a regex heuristic, not a full
/// CSS selector parser: the arguments of <c>:not(...)</c>/<c>:is(...)</c> are not parsed
/// recursively (counted as a single pseudo-class), and selectors
/// with nested parentheses in pseudo-class arguments (rare in the CSS2/3 used in EPUB) may
/// be counted imprecisely. That is enough to order the cascade correctly in typical
/// EPUB stylesheets.
/// </remarks>
public readonly record struct CssSpecificity(int Origin, int Ids, int Classes, int Types)
    : IComparable<CssSpecificity>
{
    private static readonly Regex AttributeSelectorRegex = new(@"\[[^\]]*\]", RegexOptions.Compiled);
    private static readonly Regex PseudoElementRegex = new(
        @"::[A-Za-z-]+|:(before|after|first-line|first-letter)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IdRegex = new(@"#[A-Za-z0-9_-]+", RegexOptions.Compiled);
    private static readonly Regex ClassRegex = new(@"\.[A-Za-z0-9_-]+", RegexOptions.Compiled);
    private static readonly Regex PseudoClassRegex = new(@":[A-Za-z-]+(\([^()]*\))?", RegexOptions.Compiled);
    private static readonly Regex TypeSelectorRegex = new(@"[A-Za-z][A-Za-z0-9-]*", RegexOptions.Compiled);

    /// <summary>The specificity of an inline style (<c>style="..."</c>) — beats every selector.</summary>
    public static CssSpecificity Inline { get; } = new(1, 0, 0, 0);

    /// <summary>
    /// Computes the specificity of a single selector (without commas — see
    /// <see cref="CssInfo.SplitGroupSelector"/> for splitting a group).
    /// </summary>
    public static CssSpecificity ForSelector(string selectorText)
    {
        ArgumentNullException.ThrowIfNull(selectorText);

        string s = selectorText;

        // The order matters: attributes ([data-x="#a"]) are removed first, so that "#"/"." inside
        // their values do not skew the later counters; pseudo-elements (::x) before pseudo-classes
        // (:x), because both start with a colon.
        int classes = CountMatches(ref s, AttributeSelectorRegex);
        int types = CountMatches(ref s, PseudoElementRegex);
        int ids = CountMatches(ref s, IdRegex);
        classes += CountMatches(ref s, ClassRegex);
        classes += CountMatches(ref s, PseudoClassRegex);

        // "*" (the universal selector) does not start with a letter, so TypeSelectorRegex does not catch it —
        // per the specification it adds nothing to the specificity.
        types += TypeSelectorRegex.Count(s);

        return new CssSpecificity(0, ids, classes, types);
    }

    private static int CountMatches(ref string s, Regex pattern)
    {
        int count = 0;
        s = pattern.Replace(s, m =>
        {
            count++;
            return new string(' ', m.Length);
        });

        return count;
    }

    /// <inheritdoc/>
    public int CompareTo(CssSpecificity other)
    {
        int byOrigin = Origin.CompareTo(other.Origin);
        if (byOrigin != 0)
        {
            return byOrigin;
        }

        int byIds = Ids.CompareTo(other.Ids);
        if (byIds != 0)
        {
            return byIds;
        }

        int byClasses = Classes.CompareTo(other.Classes);
        return byClasses != 0 ? byClasses : Types.CompareTo(other.Types);
    }

    /// <summary>Whether <paramref name="left"/> has a lower cascade priority than <paramref name="right"/>.</summary>
    public static bool operator <(CssSpecificity left, CssSpecificity right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> has a lower or equal cascade priority than <paramref name="right"/>.</summary>
    public static bool operator <=(CssSpecificity left, CssSpecificity right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> has a higher cascade priority than <paramref name="right"/>.</summary>
    public static bool operator >(CssSpecificity left, CssSpecificity right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> has a higher or equal cascade priority than <paramref name="right"/>.</summary>
    public static bool operator >=(CssSpecificity left, CssSpecificity right) => left.CompareTo(right) >= 0;
}
