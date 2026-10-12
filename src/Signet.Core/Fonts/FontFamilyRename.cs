using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Signet.Core.Fonts;

/// <summary>
/// Renames a font family everywhere in the book: the <c>font-family</c> of its <c>@font-face</c> rules and every
/// <c>font-family</c> / <c>font</c> list that names it (stylesheets, <c>&lt;style&gt;</c> blocks, <c>style</c>
/// attributes). Only the names change; the rest of each file stays as it was.
/// </summary>
public static class FontFamilyRename
{
    /// <summary>
    /// The new texts of the files that name <paramref name="oldFamily"/> (by book path), with the family renamed to
    /// <paramref name="newFamily"/>. Empty when nothing names the family or the names are the same.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Rename(BookFontIndex index, string oldFamily, string newFamily)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(oldFamily);
        ArgumentNullException.ThrowIfNull(newFamily);
        string oldKey = FontFaceDescriptors.FamilyKey(oldFamily);
        string newName = FontFaceDescriptors.NormalizeFamily(newFamily);
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        if (newName.Length == 0 || string.Equals(FontFaceDescriptors.NormalizeFamily(oldFamily), newName, StringComparison.Ordinal))
        {
            return result;
        }

        Dictionary<string, List<(int Start, int End, string Replacement)>> edits = new(StringComparer.Ordinal);
        foreach (FontFaceRule rule in index.Rules.Where(r => r.FamilyDeclaration is not null && string.Equals(r.FamilyKey, oldKey, StringComparison.Ordinal)))
        {
            string text = index.TextOf(rule.BookPath);
            FontFamilyEntry? entry = FontFamilyList.Parse(text, rule.FamilyDeclaration!.ValueStart, rule.FamilyDeclaration.ValueEnd).FirstOrDefault();
            if (entry is not null)
            {
                Add(edits, rule.BookPath, entry.Start, entry.End, FontFamilyList.Quote(newName, QuoteOf(text, entry, '\0')));
            }
        }

        foreach (FontFamilyUsage usage in index.Usages)
        {
            string text = index.TextOf(usage.BookPath);
            foreach (FontFamilyEntry entry in usage.Families.Where(f => !f.IsGeneric && string.Equals(FontFaceDescriptors.FamilyKey(f.Name), oldKey, StringComparison.Ordinal)))
            {
                Add(edits, usage.BookPath, entry.Start, entry.End, FontFamilyList.Quote(newName, QuoteOf(text, entry, usage.AttributeQuote)));
            }
        }

        foreach ((string bookPath, List<(int Start, int End, string Replacement)> fileEdits) in edits)
        {
            StringBuilder sb = new(index.TextOf(bookPath));
            foreach ((int start, int end, string replacement) in fileEdits.OrderByDescending(e => e.Start))
            {
                sb.Remove(start, end - start).Insert(start, replacement);
            }

            result[bookPath] = sb.ToString();
        }

        return result;
    }

    // The quote to write the new name with: the one the entry used, otherwise '"' — but never the quote delimiting
    // the style attribute the entry is in (an entity-encoded quote is read as that quote).
    private static char QuoteOf(string text, FontFamilyEntry entry, char attributeQuote)
    {
        char quote = entry.IsQuoted && text[entry.Start] is '"' or '\'' ? text[entry.Start] : '"';
        if (attributeQuote != '\0' && (quote == attributeQuote || !entry.IsQuoted || text[entry.Start] == '&'))
        {
            quote = attributeQuote == '"' ? '\'' : '"';
        }

        return quote;
    }

    private static void Add(
        Dictionary<string, List<(int Start, int End, string Replacement)>> edits, string bookPath, int start, int end, string replacement)
    {
        if (!edits.TryGetValue(bookPath, out List<(int, int, string)>? list))
        {
            list = new List<(int, int, string)>();
            edits[bookPath] = list;
        }

        list.Add((start, end, replacement));
    }
}
