using System;
using System.Linq;
using System.Net;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Gives an (X)HTML file that names no language the language of the book: <c>lang</c> and <c>xml:lang</c> on <c>&lt;html&gt;</c>
/// for EPUB 3, <c>xml:lang</c> alone for EPUB 2 (XHTML 1.1 has no <c>lang</c>). The fix of
/// <c>Validation_Accessibility_NoLanguage</c>.
/// </summary>
public sealed class SetHtmlLanguageFix : ValidationFix
{
    private readonly string _bookPath;
    private readonly string _language;

    /// <summary>Creates the fix for the file <paramref name="bookPath"/>.</summary>
    /// <param name="bookPath">The file.</param>
    /// <param name="language">The language code to set.</param>
    public SetHtmlLanguageFix(string bookPath, string language)
    {
        _bookPath = bookPath;
        _language = language;
    }

    /// <inheritdoc/>
    public override bool Apply(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(_bookPath) is not HtmlResource html)
        {
            return false;
        }

        return SetLanguage(html, _language, book.IsEpub3);
    }

    /// <summary>Sets the language on the file when it names none; <c>false</c> when it already does or has no <c>html</c> element.</summary>
    internal static bool SetLanguage(HtmlResource html, string language, bool epub3)
    {
        string text = html.GetText();
        return ReferenceFix.SetIfChanged(html, text, AddLanguage(text, language, epub3));
    }

    /// <summary>
    /// <paramref name="text"/> with the language on its <c>&lt;html&gt;</c> element when that names none (<c>lang</c> and <c>xml:lang</c>
    /// in EPUB 3, <c>xml:lang</c> in EPUB 2); the same text when it names one, has no <c>html</c> element, or <paramref name="language"/> is empty.
    /// </summary>
    internal static string AddLanguage(string text, string language, bool epub3)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return text;
        }

        TagLister.TagInfo? root = TagLister.For(text).Tags
            .FirstOrDefault(t => t.Kind == TagKind.Begin && t.TagName.Equals("html", StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return text;
        }

        string tagText = text.Substring(root.Pos, root.Len);
        if (TagLister.ParseAttribute(tagText, "lang").Pos >= 0 || TagLister.ParseAttribute(tagText, "xml:lang").Pos >= 0)
        {
            return text;
        }

        string value = WebUtility.HtmlEncode(language.Trim());
        string attributes = epub3 ? $" lang=\"{value}\" xml:lang=\"{value}\"" : $" xml:lang=\"{value}\"";
        int insertAt = root.Pos + 1 + root.TagName.Length;
        return text.Insert(insertAt, attributes);
    }
}
