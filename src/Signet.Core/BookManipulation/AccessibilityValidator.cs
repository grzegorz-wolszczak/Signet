using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Localization;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The accessibility checks (the EPUB Accessibility specification, as in Calibre's "Check book"): images without a text
/// alternative. Called from <see cref="BookValidator.ValidateCurrentBook"/>; EPUBCheck does not report these (its ACC messages
/// are off by default and cover other situations).
/// </summary>
/// <remarks>
/// An <c>img</c> needs an <c>alt</c> attribute (an empty one marks a decorative image and is legal). An inline <c>svg</c> needs a
/// <c>title</c> element, <c>aria-label</c> or <c>aria-labelledby</c>. Both are left alone when the element is hidden
/// (<c>aria-hidden="true"</c>) or marked as decoration (<c>role="presentation"</c> / <c>"none"</c>).
/// </remarks>
public static class AccessibilityValidator
{
    /// <summary>Runs the accessibility checks for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            CheckLanguage(book, html, results);
            CheckImages(html, results);

            // The role attribute is not valid in the XHTML 1.1 of EPUB 2.
            if (book.IsEpub3)
            {
                CheckAriaRoles(html, results);
            }
        }

        CheckHeadingLevels(book, results);

        return results;
    }

    // The headings in reading order (the spine, file after file) go down by at most one level at a time: an h4 straight after
    // an h2 — also when the h2 ends the previous file — is reported. Going up by any number of levels is fine.
    private static void CheckHeadingLevels(Book book, List<ValidationResult> results)
    {
        FolderKeeper keeper = book.GetFolderKeeper();
        HtmlResource? nav = book.GetNavResource();
        int previous = 0;
        foreach (string bookPath in book.GetOpf().GetSpineOrderBookPaths())
        {
            if (keeper.GetResourceByBookPathNoThrow(bookPath) is not HtmlResource html || ReferenceEquals(html, nav))
            {
                continue;
            }

            string text = html.GetText();
            foreach (TagLister.TagInfo tag in TagLister.For(text).Tags)
            {
                if (tag.Kind != TagKind.Begin || tag.TagName.Length != 2 || char.ToLowerInvariant(tag.TagName[0]) != 'h'
                    || tag.TagName[1] is < '1' or > '6')
                {
                    continue;
                }

                int level = tag.TagName[1] - '0';
                if (previous > 0 && level > previous + 1)
                {
                    int line = 1 + text.Take(tag.Pos).Count(c => c == '\n');
                    results.Add(new ValidationResult(
                        ValidationSeverity.Warning, html.BookPath, line, -1,
                        CoreStrings.Format("Validation_Accessibility_HeadingSkip", level, previous),
                        "Validation_Accessibility_HeadingSkip"));
                }

                previous = level;
            }
        }
    }

    // An element with an epub:type that has a DPUB-ARIA role but no role: screen readers use the role. One result per element; the fix
    // adds the roles of the whole file.
    private static void CheckAriaRoles(HtmlResource html, List<ValidationResult> results)
    {
        IReadOnlyList<MissingAriaRole> missing = AriaRoleAdder.Find(html.GetText());
        if (missing.Count == 0)
        {
            return;
        }

        AddAriaRolesFix fix = new(html.BookPath);
        foreach (MissingAriaRole role in missing)
        {
            results.Add(new ValidationResult(
                ValidationSeverity.Info, html.BookPath, role.Line, -1,
                CoreStrings.Format("Validation_Accessibility_MissingRole", role.TagName, role.Role),
                "Validation_Accessibility_MissingRole", fix));
        }
    }

    // The <html> element names a language (lang, xml:lang); the fix gives it the language of the book.
    private static void CheckLanguage(Book book, HtmlResource html, List<ValidationResult> results)
    {
        string text = html.GetText();
        IReadOnlyList<TagLister.TagInfo> tags = TagLister.For(text).Tags;
        TagLister.TagInfo? root = tags.FirstOrDefault(t => t.Kind == TagKind.Begin && t.TagName.Equals("html", StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return;
        }

        string tagText = text.Substring(root.Pos, root.Len);
        if (TagLister.ParseAttribute(tagText, "lang").Pos >= 0 || TagLister.ParseAttribute(tagText, "xml:lang").Pos >= 0)
        {
            return;
        }

        string language = BookLanguage(book);
        int line = 1 + text.Take(root.Pos).Count(c => c == '\n');
        results.Add(new ValidationResult(
            ValidationSeverity.Warning, html.BookPath, line, -1, CoreStrings.Get("Validation_Accessibility_NoLanguage"),
            "Validation_Accessibility_NoLanguage", language.Length > 0 ? new SetHtmlLanguageFix(html.BookPath, language) : null));
    }

    /// <summary>The main language of the book (the first <c>dc:language</c>), or an empty string when it has none.</summary>
    internal static string BookLanguage(Book book) =>
        book.GetMetadataValues("dc:language").Select(v => v.Trim()).FirstOrDefault(v => v.Length > 0) ?? string.Empty;

    private static void CheckImages(HtmlResource html, List<ValidationResult> results)
    {
        string text = html.GetText();
        TagLister lister = TagLister.For(text);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        int line = 1;
        int linePosition = 0;
        int svgEnd = -1;
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Kind is not (TagKind.Begin or TagKind.SelfClosing) || tag.Pos < svgEnd)
            {
                continue;
            }

            bool isImg = tag.TagName.Equals("img", StringComparison.OrdinalIgnoreCase);
            bool isSvg = tag.TagName.Equals("svg", StringComparison.OrdinalIgnoreCase);
            if (!isImg && !isSvg)
            {
                continue;
            }

            string tagText = text.Substring(tag.Pos, tag.Len);
            if (IsHiddenOrDecorative(tagText))
            {
                continue;
            }

            string code;
            if (isImg)
            {
                if (TagLister.ParseAttribute(tagText, "alt").Pos >= 0 || HasLabel(tagText))
                {
                    continue;
                }

                code = "Validation_Accessibility_ImageNoAlt";
            }
            else
            {
                int close = lister.FindCloseTagForOpen(i);
                svgEnd = close < 0 ? text.Length : tags[close].Pos;
                if (HasLabel(tagText) || HasTitle(tags, i, close))
                {
                    continue;
                }

                code = "Validation_Accessibility_SvgNoAlt";
            }

            for (; linePosition < tag.Pos; linePosition++)
            {
                if (text[linePosition] == '\n')
                {
                    line++;
                }
            }

            results.Add(new ValidationResult(ValidationSeverity.Warning, html.BookPath, line, -1, CoreStrings.Get(code), code));
        }
    }

    private static bool HasLabel(string tagText) =>
        TagLister.ParseAttribute(tagText, "aria-label").AValue.Trim().Length > 0
        || TagLister.ParseAttribute(tagText, "aria-labelledby").AValue.Trim().Length > 0;

    private static bool IsHiddenOrDecorative(string tagText)
    {
        if (string.Equals(TagLister.ParseAttribute(tagText, "aria-hidden").AValue.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string role = TagLister.ParseAttribute(tagText, "role").AValue.Trim();
        return role.Equals("presentation", StringComparison.OrdinalIgnoreCase) || role.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    // A title element directly inside the svg (not one of an element nested in it).
    private static bool HasTitle(IReadOnlyList<TagLister.TagInfo> tags, int open, int close)
    {
        int end = close < 0 ? tags.Count : close;
        for (int i = open + 1; i < end; i++)
        {
            if (tags[i].Kind == TagKind.Begin && tags[i].TagName.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
