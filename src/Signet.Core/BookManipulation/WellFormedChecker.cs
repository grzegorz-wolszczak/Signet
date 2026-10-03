using System;
using System.IO;
using System.Xml;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Result of a well-formedness check of an XML/XHTML document.
/// </summary>
/// <param name="IsWellFormed">Whether the document is well-formed.</param>
/// <param name="Line">Line number of the first error (1-based), or <c>-1</c> when there is no error.</param>
/// <param name="Column">Column number of the first error (1-based), or <c>-1</c> when there is no error.</param>
/// <param name="Message">Message — <c>"well-formed"</c> when there is no error.</param>
public sealed record WellFormedResult(bool IsWellFormed, int Line, int Column, string Message)
{
    /// <summary>The "valid" result — no error position.</summary>
    public static WellFormedResult Ok { get; } = new(true, -1, -1, "well-formed");
}

/// <summary>
/// Well-formedness checking of XML/XHTML with the position of the first error: pure XML
/// well-formedness plus additional XHTML structural rules.
/// </summary>
/// <remarks>
/// <para><see cref="Check"/> uses an <see cref="XmlReader"/> with DTD resolution disabled
/// (<see cref="DtdProcessing.Ignore"/>, <c>XmlResolver = null</c>) — it is the source of truth for
/// the pass/fail verdict and the line and column numbers. Consequence: named HTML entities
/// (<c>&amp;nbsp;</c> etc.) are treated as undefined and cause an error — consistent with the EPUB
/// corpus rule (we use numeric entities).</para>
/// <para>When the error is a tag nesting error, <see cref="TagLister"/> replaces the message with a
/// more readable description (e.g. "Tag &lt;p&gt; was not closed"); the position remains the one
/// reported by <see cref="XmlReader"/>.</para>
/// <para><see cref="CheckXhtmlStructure"/> first runs <see cref="Check"/> and then requires the
/// <c>DOCTYPE</c> and the <c>html</c>, <c>head</c> and <c>body</c> tags to occur exactly
/// once each.</para>
/// </remarks>
public static class WellFormedChecker
{
    /// <summary>
    /// Checks pure well-formedness of an XML/XHTML document.
    /// </summary>
    /// <param name="text">Document content.</param>
    /// <param name="mediaType">
    /// MIME type of the document (e.g. <c>application/xhtml+xml</c>) — currently only affects the
    /// message context; reserved for future distinctions.
    /// </param>
    public static WellFormedResult Check(string text, string mediaType = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        _ = mediaType;

        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            CheckCharacters = true,
        };

        try
        {
            using XmlReader reader = XmlReader.Create(new StringReader(text), settings);
            while (reader.Read())
            {
            }

            return WellFormedResult.Ok;
        }
        catch (XmlException ex)
        {
            string message = ex.Message;
            NestingError? nesting = TryFindNestingError(text);
            if (nesting is { } clear)
            {
                message = clear.Message;
            }

            return new WellFormedResult(false, ex.LineNumber, ex.LinePosition, message);
        }
    }

    /// <summary>
    /// <see cref="Check"/> plus XHTML structural rules: exactly one <c>DOCTYPE</c> and exactly one
    /// each of the <c>html</c>, <c>head</c> and <c>body</c> tags.
    /// </summary>
    /// <param name="text">XHTML document content.</param>
    /// <param name="version">
    /// EPUB version (<c>"2.0"</c> / <c>"3.0"</c>) — reserved; currently does not change the
    /// rules.
    /// </param>
    public static WellFormedResult CheckXhtmlStructure(string text, string version = "2.0")
    {
        ArgumentNullException.ThrowIfNull(text);
        _ = version;

        WellFormedResult basic = Check(text, "application/xhtml+xml");
        if (!basic.IsWellFormed)
        {
            return basic;
        }

        int doctypes = 0;
        int htmlTags = 0;
        int headTags = 0;
        int bodyTags = 0;

        TagLister lister = new(text);
        foreach (TagLister.TagInfo ti in lister.Tags)
        {
            if (ti.Len == -1)
            {
                break;
            }

            if (ti.Kind == TagKind.Doctype)
            {
                doctypes++;
            }
            else if (ti.Kind == TagKind.Begin)
            {
                switch (ti.TagName)
                {
                    case "html":
                        htmlTags++;
                        break;
                    case "head":
                        headTags++;
                        break;
                    case "body":
                        bodyTags++;
                        break;
                }
            }
        }

        if (doctypes != 1)
        {
            return new WellFormedResult(false, 1, 1, CoreStrings.Get("WellFormed_DoctypeMissing"));
        }

        if (htmlTags != 1)
        {
            return new WellFormedResult(false, 1, 1, CoreStrings.Get("WellFormed_HtmlMissing"));
        }

        if (headTags != 1)
        {
            return new WellFormedResult(false, 1, 1, CoreStrings.Get("WellFormed_HeadMissing"));
        }

        if (bodyTags != 1)
        {
            return new WellFormedResult(false, 1, 1, CoreStrings.Get("WellFormed_BodyMissing"));
        }

        return WellFormedResult.Ok;
    }

    /// <summary>Shortcut: whether the document is well-formed (without the error position).</summary>
    public static bool IsWellFormed(string text, string mediaType = "") => Check(text, mediaType).IsWellFormed;

    private static NestingError? TryFindNestingError(string text)
    {
        try
        {
            return new TagLister(text).FindFirstNestingError();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
