using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Adds the accessibility metadata of the EPUB Accessibility specification that the book lacks and that can be told from its
/// contents (the fix of <c>Validation_Accessibility_NoMetadata</c>). It never invents a summary and makes only claims it can check:
/// </summary>
/// <list type="bullet">
/// <item><c>schema:accessMode</c>: <c>textual</c>, and <c>visual</c> when the book has images;</item>
/// <item><c>schema:accessModeSufficient</c>: <c>textual</c> when every image has a text alternative (or there are none);</item>
/// <item><c>schema:accessibilityFeature</c>: <c>alternativeText</c> when there are images and all of them have one,
/// <c>tableOfContents</c> when the nav has a table of contents with links;</item>
/// <item><c>schema:accessibilityHazard</c>: <c>unknown</c> (it cannot be told automatically).</item>
/// </list>
public sealed class AddAccessibilityMetadataFix : ValidationFix
{
    private static readonly Regex TocType = new(@"epub:type\s*=\s*[""'][^""']*\btoc\b", RegexOptions.Compiled);

    /// <inheritdoc/>
    public override bool Apply(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (!book.IsEpub3)
        {
            return false;
        }

        OpfResource opf = book.GetOpf();
        (int images, int undescribed) = AccessibilityValidator.CountImages(book);
        bool described = undescribed == 0;
        bool changed = false;

        List<string> modes = new() { "textual" };
        if (images > 0)
        {
            modes.Add("visual");
        }

        List<string> features = new();
        if (images > 0 && described)
        {
            features.Add("alternativeText");
        }

        if (HasTableOfContents(book))
        {
            features.Add("tableOfContents");
        }

        changed |= AddIfMissing(opf, "schema:accessMode", modes);
        changed |= AddIfMissing(opf, "schema:accessModeSufficient", described ? ["textual"] : []);
        changed |= AddIfMissing(opf, "schema:accessibilityFeature", features);
        changed |= AddIfMissing(opf, "schema:accessibilityHazard", ["unknown"]);
        return changed;
    }

    private static bool AddIfMissing(OpfResource opf, string property, List<string> values)
    {
        if (opf.GetMetaPropertyValues(property).Count > 0)
        {
            return false;
        }

        foreach (string value in values)
        {
            opf.AddMetaProperty(property, value);
        }

        return values.Count > 0;
    }

    private static bool HasTableOfContents(Book book)
    {
        string text = book.GetNavResource()?.GetText() ?? string.Empty;
        return TocType.IsMatch(text) && text.Contains("<a ", StringComparison.OrdinalIgnoreCase);
    }
}
