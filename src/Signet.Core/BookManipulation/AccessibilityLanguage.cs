using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The language operations of Tools → Accessibility: the <c>dc:language</c> values of the book and the language of its (X)HTML files (the
/// main language is the first <c>dc:language</c>). Screen readers use the language of a file to choose the voice.
/// </summary>
public static class AccessibilityLanguage
{
    /// <summary>The languages of the book (its non-empty <c>dc:language</c> values), main language first.</summary>
    public static IReadOnlyList<string> GetLanguages(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.GetMetadataValues("dc:language").Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
    }

    /// <summary>
    /// Makes <paramref name="languages"/> the <c>dc:language</c> values of the book (trimmed, without empty ones and repeats); the first is the
    /// main language. At least one is needed: a book without a language is invalid.
    /// </summary>
    public static void SetLanguages(Book book, IEnumerable<string> languages)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(languages);
        List<string> clean = languages.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (clean.Count == 0)
        {
            throw new ArgumentException("A book needs a language.", nameof(languages));
        }

        book.GetOpf().SetLanguages(clean);
        book.Modified = true;
    }

    /// <summary>How many (X)HTML files of the book name no language (<c>lang</c> / <c>xml:lang</c> on <c>&lt;html&gt;</c>).</summary>
    public static int CountFilesWithoutLanguage(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.GetHtmlResources().Count(h => !NamesLanguage(h.GetText()));
    }

    private static bool NamesLanguage(string text)
    {
        TagLister.TagInfo? root = TagLister.For(text).Tags
            .FirstOrDefault(t => t.Kind == TagKind.Begin && t.TagName.Equals("html", StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return true;
        }

        string tag = text.Substring(root.Pos, root.Len);
        return TagLister.ParseAttribute(tag, "lang").Pos >= 0 || TagLister.ParseAttribute(tag, "xml:lang").Pos >= 0;
    }

    /// <summary>
    /// Gives the (X)HTML files the main language of the book: only the ones that name no language (<paramref name="onlyMissing"/>), or all of
    /// them, replacing what they had. EPUB 3 files get <c>lang</c> and <c>xml:lang</c>, EPUB 2 files <c>xml:lang</c>. Returns how many files changed.
    /// </summary>
    public static int SetHtmlLanguages(Book book, bool onlyMissing)
    {
        ArgumentNullException.ThrowIfNull(book);
        IReadOnlyList<string> languages = GetLanguages(book);
        if (languages.Count == 0)
        {
            return 0;
        }

        int changed = 0;
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            if (onlyMissing ? SetHtmlLanguageFix.SetLanguage(html, languages[0], book.IsEpub3) : SetLanguage(html, languages[0], book.IsEpub3))
            {
                changed++;
            }
        }

        return changed;
    }

    // Sets lang and xml:lang on <html>: the values it has are replaced, the ones it lacks are added.
    private static bool SetLanguage(HtmlResource html, string language, bool epub3)
    {
        string text = html.GetText();
        TagLister.TagInfo? root = TagLister.For(text).Tags
            .FirstOrDefault(t => t.Kind == TagKind.Begin && t.TagName.Equals("html", StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return false;
        }

        string tagText = text.Substring(root.Pos, root.Len);
        string value = WebUtility.HtmlEncode(language);
        List<(int Pos, int Length, string Text)> edits = new();
        List<string> missing = new();
        foreach (string attribute in epub3 ? new[] { "lang", "xml:lang" } : new[] { "xml:lang" })
        {
            TagLister.AttInfo info = TagLister.ParseAttribute(tagText, attribute);
            if (info.Pos >= 0 && info.VPos >= 0)
            {
                if (!string.Equals(info.AValue, value, StringComparison.Ordinal))
                {
                    edits.Add((root.Pos + info.VPos, info.AValue.Length, value));
                }
            }
            else
            {
                missing.Add($" {attribute}=\"{value}\"");
            }
        }

        // EPUB 2: an existing lang attribute is not valid there but is not ours to remove; its value follows the book too.
        if (!epub3)
        {
            TagLister.AttInfo lang = TagLister.ParseAttribute(tagText, "lang");
            if (lang.Pos >= 0 && lang.VPos >= 0 && !string.Equals(lang.AValue, value, StringComparison.Ordinal))
            {
                edits.Add((root.Pos + lang.VPos, lang.AValue.Length, value));
            }
        }

        if (missing.Count > 0)
        {
            edits.Add((root.Pos + 1 + root.TagName.Length, 0, string.Concat(missing)));
        }

        if (edits.Count == 0)
        {
            return false;
        }

        StringBuilder sb = new(text);
        foreach ((int pos, int length, string replacement) in edits.OrderByDescending(e => e.Pos))
        {
            sb.Remove(pos, length).Insert(pos, replacement);
        }

        return ReferenceFix.SetIfChanged(html, text, sb.ToString());
    }
}
