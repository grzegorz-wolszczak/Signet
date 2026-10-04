using System;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace Signet.Core.Parsers;

/// <summary>
/// Matching CSS selectors against a static document. State pseudo-classes (<c>:hover</c>…) and pseudo-elements
/// (<c>::before</c>…) cannot be evaluated without a live page, so they are stripped before matching: <c>a:hover</c>
/// is used wherever an <c>a</c> exists.
/// </summary>
public static class CssSelectorMatching
{
    private static readonly Regex DynamicPseudoRegex = new(
        @"::?(?:hover|focus|focus-within|focus-visible|active|visited|link|any-link|target|before|after|first-line|first-letter|marker|selection|placeholder|backdrop)\b(?:\([^)]*\))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary><paramref name="selector"/> without state pseudo-classes and pseudo-elements (may become empty).</summary>
    public static string StripDynamicPseudo(string selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return DynamicPseudoRegex.Replace(selector, string.Empty).Trim();
    }

    /// <summary>
    /// Whether <paramref name="element"/> matches <paramref name="selector"/> (after
    /// <see cref="StripDynamicPseudo"/>; a selector that is only a pseudo-element/state matches everything).
    /// <c>null</c> when the selector cannot be evaluated (AngleSharp does not understand it).
    /// </summary>
    public static bool? TryMatch(IElement element, string selector)
    {
        ArgumentNullException.ThrowIfNull(element);
        string stripped = StripDynamicPseudo(selector);
        if (stripped.Length == 0)
        {
            return true;
        }

        try
        {
            return element.Matches(stripped);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether any element of <paramref name="document"/> matches <paramref name="selector"/> (see
    /// <see cref="TryMatch"/>); <c>null</c> when the selector cannot be evaluated.
    /// </summary>
    public static bool? TryMatchAny(IParentNode document, string selector)
    {
        ArgumentNullException.ThrowIfNull(document);
        string stripped = StripDynamicPseudo(selector);
        if (stripped.Length == 0)
        {
            return true;
        }

        try
        {
            return document.QuerySelector(stripped) is not null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
