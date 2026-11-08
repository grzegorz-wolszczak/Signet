using System;
using System.Collections.Generic;
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
            CheckImages(html, results);
        }

        return results;
    }

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
