using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Signet.Core.Localization;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// CSS declaration checks in stylesheets and <c>&lt;style&gt;</c> elements: an unknown property name (usually a typo)
/// and a property without a value. Called from <see cref="BookValidator.ValidateCurrentBook"/>.
/// </summary>
/// <remarks>
/// calibre runs stylelint (JavaScript); Signet checks the declarations its own CSS parser (<see cref="CssInfo"/>) finds
/// against the property names of <see cref="CssPropertyNames"/> and the <c>@font-face</c> / <c>@page</c> descriptors.
/// Vendor-prefixed properties (<c>-webkit-</c>, <c>-epub-</c>, …) and custom properties (<c>--name</c>) are not
/// checked. Values are not validated beyond being present: what a reader accepts varies too much.
/// </remarks>
public static class CssPropertyValidator
{
    // The @font-face and @page descriptors: checked in the same declaration blocks, but not properties.
    private static readonly FrozenSet<string> Descriptors = new[]
    {
        "ascent-override", "descent-override", "font-display", "font-named-instance", "line-gap-override",
        "size", "size-adjust", "src", "unicode-range", "bleed",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs the CSS declaration checks for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();
        foreach (CssResource css in book.GetCssResources())
        {
            string text = css.GetText();
            CheckCss(css.BookPath, text, 0, text, results);
        }

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            string text = html.GetText();
            foreach ((int offset, string css) in MarkupEdits.StyleElements(text))
            {
                CheckCss(html.BookPath, css, offset, text, results);
            }
        }

        return results;
    }

    /// <summary>Whether <paramref name="property"/> is a known, prefixed or custom property.</summary>
    public static bool IsKnownProperty(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return property.StartsWith('-')
            || CssPropertyNames.IsStandard(property)
            || CssPropertyNames.IsEpubVendorProperty(property)
            || Descriptors.Contains(property);
    }

    private static void CheckCss(string bookPath, string css, int offset, string fileText, List<ValidationResult> results)
    {
        CssInfo info;
        try
        {
            info = new CssInfo(css, offset);
        }
        catch (FormatException)
        {
            return;
        }

        // CssInfo works on the text with "\r\n" turned into "\n": its positions count from there.
        string normalized = css.Replace("\r\n", "\n", StringComparison.Ordinal);
        int firstLine = LineOf(fileText, offset);
        foreach (CssRule rule in info.Rules)
        {
            string ruleName = rule.SelectorText.Length > 0 ? rule.SelectorText.Trim() : rule.AtRulePrelude ?? string.Empty;
            int line = -1;
            foreach (CssDeclaration declaration in rule.Declarations)
            {
                string property = declaration.Property;
                if (property.Length == 0)
                {
                    continue;
                }

                string? code = !IsKnownProperty(property) ? "Validation_CssUnknownProperty"
                    : StripImportant(declaration.Value).Length == 0 ? "Validation_CssEmptyValue"
                    : null;
                if (code is null)
                {
                    continue;
                }

                line = line > 0 ? line : firstLine + LineOf(normalized, rule.BlockStart - offset) - 1;
                results.Add(new ValidationResult(
                    ValidationSeverity.Warning, bookPath, line, -1, CoreStrings.Format(code, property, ruleName), code));
            }
        }
    }

    private static string StripImportant(string value)
    {
        string trimmed = value.Trim();
        int bang = trimmed.LastIndexOf('!');
        return bang >= 0 && trimmed[(bang + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..bang].Trim()
            : trimmed;
    }

    private static int LineOf(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < Math.Min(offset, text.Length); i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
