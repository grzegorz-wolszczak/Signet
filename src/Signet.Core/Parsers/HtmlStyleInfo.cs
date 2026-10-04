using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Signet.Core.Parsers;

/// <summary>
/// Analysis of the inline <c>&lt;style&gt;…&lt;/style&gt;</c> blocks in an XHTML document. Each block is parsed
/// by a separate <see cref="CssInfo"/> with an offset equal to the position of the block content in the source.
/// </summary>
/// <remarks>
/// After <see cref="GetReformattedCssText"/> / <see cref="RemoveMatchingSelectors"/>
/// the object is stale — create a new <see cref="HtmlStyleInfo"/> over the returned text.
/// A <c>&lt;style&gt;</c> block starting exactly at source position 0
/// is ignored (the condition <c>styleStart &gt; 0</c>).
/// </remarks>
public sealed class HtmlStyleInfo
{
    private const int TabSpacesWidth = 4;

    private static readonly Regex StyleOpen =
        new(@"<\s*style\s*[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StyleClose =
        new(@"<\s*/\s*style\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly List<CssInfo> _styles = new();
    private readonly List<string> _styleTexts = new();
    private readonly List<int> _starts = new();
    private readonly List<int> _lengths = new();
    private string _source;

    /// <summary>Finds the <c>&lt;style&gt;</c> blocks in <paramref name="htmlText"/> and parses their content.</summary>
    public HtmlStyleInfo(string htmlText)
    {
        ArgumentNullException.ThrowIfNull(htmlText);
        _source = htmlText;

        int searchOffset = 0;
        while (FindInlineStyleBlock(htmlText, searchOffset, out int styleStart, out int styleEnd))
        {
            _styles.Add(new CssInfo(htmlText[styleStart..styleEnd], styleStart));
            _styleTexts.Add(htmlText[styleStart..styleEnd]);
            _starts.Add(styleStart);
            _lengths.Add(styleEnd - styleStart);
            searchOffset = styleEnd;
        }
    }

    /// <summary>Whether the document has at least one <c>&lt;style&gt;</c> block with content.</summary>
    public bool HasStyles => _styles.Count > 0;

    /// <summary>The parsers of the individual <c>&lt;style&gt;</c> blocks (source order).</summary>
    public IReadOnlyList<CssInfo> Styles => _styles;

    /// <summary>The raw CSS text of the individual <c>&lt;style&gt;</c> blocks (source order), as found at construction.</summary>
    public IReadOnlyList<string> StyleBlockTexts => _styleTexts;

    /// <summary>The selectors from all <c>&lt;style&gt;</c> blocks.</summary>
    public IReadOnlyList<CssSelector> GetAllSelectors()
    {
        List<CssSelector> selectors = new();
        foreach (CssInfo cp in _styles)
        {
            selectors.AddRange(cp.GetAllSelectors());
        }

        return selectors;
    }

    /// <summary>The first matching selector from any block.</summary>
    public CssSelector? GetCssSelectorForElementClass(string elementName, string className)
    {
        foreach (CssInfo cp in _styles)
        {
            CssSelector? found = cp.GetCssSelectorForElementClass(elementName, className);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>All selectors matching an element/class, from all blocks.</summary>
    public IReadOnlyList<CssSelector> GetAllCssSelectorsForElementClass(string elementName, string className)
    {
        List<CssSelector> matches = new();
        foreach (CssInfo cp in _styles)
        {
            matches.AddRange(cp.GetAllCssSelectorsForElementClass(elementName, className));
        }

        return matches;
    }

    /// <summary>The values of a given property from all blocks.</summary>
    public IReadOnlyList<string> GetAllPropertyValues(string property)
    {
        List<string> values = new();
        foreach (CssInfo cp in _styles)
        {
            values.AddRange(cp.GetAllPropertyValues(property));
        }

        return values;
    }

    /// <summary>
    /// Returns the document text with each <c>&lt;style&gt;</c> content
    /// replaced by its reformatted form (surrounded by <c>\n</c>). Replacements go from the end, so as to
    /// keep earlier offsets valid.
    /// </summary>
    public string GetReformattedCssText(bool multipleLineFormat)
    {
        List<string> styleTexts = new();
        foreach (CssInfo cp in _styles)
        {
            styleTexts.Add(cp.GetReformattedCssText(multipleLineFormat));
        }

        for (int i = styleTexts.Count - 1; i >= 0; i--)
        {
            string replacement = "\n" + styleTexts[i] + "\n";
            string top = _source[.._starts[i]];
            string bottom = _source[(_starts[i] + _lengths[i])..];
            _source = top + replacement + bottom;
        }

        return _source;
    }

    /// <summary>
    /// Removes the given selectors from the <c>&lt;style&gt;</c> blocks.
    /// </summary>
    /// <remarks>
    /// When a block contains none of the given selectors,
    /// its content stays unchanged (it is never replaced with an empty
    /// <c>\n\n</c>, which would lose data).
    /// </remarks>
    public string RemoveMatchingSelectors(IEnumerable<CssSelector> cssSelectors)
    {
        ArgumentNullException.ThrowIfNull(cssSelectors);
        List<CssSelector> selectors = new(cssSelectors);

        List<string?> styleTexts = new();
        foreach (CssInfo cp in _styles)
        {
            styleTexts.Add(cp.RemoveMatchingSelectors(selectors));
        }

        for (int i = styleTexts.Count - 1; i >= 0; i--)
        {
            string original = _source.Substring(_starts[i], _lengths[i]);
            string newText = styleTexts[i] is { } t ? "\n" + t + "\n" : original;
            string top = _source[.._starts[i]];
            string bottom = _source[(_starts[i] + _lengths[i])..];
            _source = top + newText + bottom;
        }

        return _source;
    }

    // =====================================================================
    //  Static helpers (used by the code editor)
    // =====================================================================

    /// <summary>
    /// Splits a text fragment (CSS declarations from inside a rule)
    /// into property/value pairs. Malformed entries come out as <see cref="CssProperty.Name"/>
    /// with <see cref="CssProperty.Value"/> = <c>null</c>.
    /// </summary>
    public static IReadOnlyList<CssProperty> GetCssProperties(string text, int styleTextStartPos, int styleTextEndPos)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<CssProperty> result = new();
        if (styleTextEndPos - 1 <= styleTextStartPos)
        {
            return result;
        }

        string styleText = text[styleTextStartPos..styleTextEndPos];
        foreach (string propertyText in styleText.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (propertyText.Trim().Length == 0)
            {
                continue;
            }

            string[] nameValues = propertyText.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (nameValues.Length != 2)
            {
                result.Add(new CssProperty(propertyText.Trim(), null));
            }
            else
            {
                result.Add(new CssProperty(nameValues[0].Trim(), nameValues[1].Trim()));
            }
        }

        return result;
    }

    /// <summary>
    /// Assembles a property list into declaration text (an indent of
    /// <see cref="TabSpacesWidth"/> spaces + <paramref name="selectorIndent"/>), multi-line or single-line.
    /// </summary>
    public static string FormatCssProperties(IReadOnlyList<CssProperty> newProperties, bool multipleLineFormat, int selectorIndent = 0)
    {
        ArgumentNullException.ThrowIfNull(newProperties);
        string tabSpaces = new(' ', TabSpacesWidth + selectorIndent);

        if (newProperties.Count == 0)
        {
            return multipleLineFormat ? "\n" + new string(' ', selectorIndent) : string.Empty;
        }

        List<string> propertyValues = new();
        foreach (CssProperty property in newProperties)
        {
            propertyValues.Add(property.Value is null ? property.Name : $"{property.Name}: {property.Value}");
        }

        if (multipleLineFormat)
        {
            return "\n" + tabSpaces + string.Join(";\n" + tabSpaces, propertyValues) + ";\n" + new string(' ', selectorIndent);
        }

        return " " + string.Join("; ", propertyValues) + "; ";
    }

    // =====================================================================
    //  Finding an inline style block
    // =====================================================================

    private static bool FindInlineStyleBlock(string text, int offset, out int styleStart, out int styleEnd)
    {
        styleStart = -1;
        styleEnd = -1;

        Match match = StyleOpen.Match(text, offset);
        if (!match.Success)
        {
            return false;
        }

        styleStart = match.Index;
        int styleLen = match.Length;

        // Literally "styleStart > 0" (a block at position 0 is skipped).
        if (styleStart <= 0)
        {
            return false;
        }

        styleStart += styleLen;
        Match closeMatch = StyleClose.Match(text, styleStart);
        styleEnd = closeMatch.Success ? closeMatch.Index : -1;

        return styleEnd >= styleStart;
    }
}

/// <summary>A property/value pair from <see cref="HtmlStyleInfo.GetCssProperties"/>.</summary>
/// <param name="Name">The property name (or the whole fragment when malformed).</param>
/// <param name="Value">The value or <c>null</c> for a malformed entry.</param>
public readonly record struct CssProperty(string Name, string? Value);
