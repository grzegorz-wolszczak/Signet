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

    /// <summary>
    /// A non-blocking structural warning found in an otherwise well-formed document (currently only a
    /// missing DOCTYPE), or <c>null</c> when there is none. Set only when <see cref="IsWellFormed"/>
    /// is <c>true</c>.
    /// </summary>
    public WellFormedWarning? Warning { get; init; }
}

/// <summary>The kind of a non-blocking structural warning.</summary>
public enum WellFormedWarningKind
{
    /// <summary>The XHTML document has no <c>&lt;!DOCTYPE&gt;</c> declaration.</summary>
    MissingDoctype,
}

/// <summary>
/// A non-blocking structural warning of an XHTML document (see <see cref="WellFormedResult.Warning"/>).
/// </summary>
/// <param name="Kind">The kind of the warning.</param>
/// <param name="Offset">Start (0-based character offset) of the text range the warning points at.</param>
/// <param name="Length">Length of that range (at least 1 for a non-empty document).</param>
/// <param name="Message">A translated, user-facing description.</param>
public sealed record WellFormedWarning(WellFormedWarningKind Kind, int Offset, int Length, string Message);

/// <summary>
/// Well-formedness checking of XML/XHTML with the position of the first error: pure XML
/// well-formedness plus additional XHTML structural rules.
/// </summary>
/// <remarks>
/// <para><see cref="Check"/> uses an <see cref="XmlReader"/> without network access — it is the source of
/// truth for the pass/fail verdict and the line and column numbers. The DTD is ignored, except for
/// documents with an XHTML 1.x DOCTYPE: there the DTD request is answered locally with the XHTML entity
/// declarations (<see cref="XhtmlEntities"/>), so named HTML entities (<c>&amp;nbsp;</c>, <c>&amp;mdash;</c>, …)
/// are valid exactly where the DOCTYPE defines them. Elsewhere (EPUB 3, no DOCTYPE) they are undefined
/// and cause an error.</para>
/// <para>When the error is a tag nesting error, <see cref="TagLister"/> replaces the message with a
/// more readable description (e.g. "Tag &lt;p&gt; was not closed"); the position remains the one
/// reported by <see cref="XmlReader"/>.</para>
/// <para><see cref="CheckXhtmlStructure"/> first runs <see cref="Check"/> and then requires the
/// <c>html</c>, <c>head</c> and <c>body</c> tags to occur exactly once each and allows at most one
/// <c>DOCTYPE</c>. A missing <c>DOCTYPE</c> is only a warning (<see cref="WellFormedResult.Warning"/>):
/// files without it (typical calibre output) are accepted by reading systems and are safe for every
/// operation.</para>
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

        bool xhtml1 = XhtmlEntities.HasXhtml1Doctype(text);
        XmlReaderSettings settings = new()
        {
            DtdProcessing = xhtml1 ? DtdProcessing.Parse : DtdProcessing.Ignore,
            XmlResolver = xhtml1 ? new XhtmlEntities.EntityDtdResolver() : null,
            MaxCharactersFromEntities = 10_000_000,
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
    /// <see cref="Check"/> plus XHTML structural rules: at most one <c>DOCTYPE</c> and exactly one
    /// each of the <c>html</c>, <c>head</c> and <c>body</c> tags. A missing <c>DOCTYPE</c> does not
    /// fail the check — it is reported in <see cref="WellFormedResult.Warning"/>.
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
        TagLister.TagInfo? xmlHeader = null;
        TagLister.TagInfo? htmlTag = null;

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
            else if (ti.Kind == TagKind.XmlHeader)
            {
                xmlHeader ??= ti;
            }
            else if (ti.Kind == TagKind.Begin)
            {
                switch (ti.TagName)
                {
                    case "html":
                        htmlTags++;
                        htmlTag ??= ti;
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

        if (doctypes > 1)
        {
            return new WellFormedResult(false, 1, 1, CoreStrings.Get("WellFormed_DoctypeDuplicate"));
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

        if (doctypes == 0)
        {
            // Point at the XML declaration (the DOCTYPE belongs right after it), or at <html> without one.
            TagLister.TagInfo anchor = xmlHeader ?? htmlTag!;
            return WellFormedResult.Ok with
            {
                Warning = new WellFormedWarning(
                    WellFormedWarningKind.MissingDoctype,
                    anchor.Pos,
                    Math.Max(1, anchor.Len),
                    CoreStrings.Get("WellFormed_DoctypeMissing")),
            };
        }

        return WellFormedResult.Ok;
    }

    /// <summary>
    /// Whether the XHTML document is well-formed and passes <see cref="CheckXhtmlStructure"/> but has
    /// no <c>DOCTYPE</c> (the only non-blocking structural warning).
    /// </summary>
    public static bool IsMissingDoctype(string text, string version = "2.0") =>
        CheckXhtmlStructure(text, version).Warning?.Kind == WellFormedWarningKind.MissingDoctype;

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
