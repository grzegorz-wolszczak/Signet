using System;
using Signet.EpubCheck.Core.Nav;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Ops;
using Signet.EpubCheck.Core.Overlay;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Util.Mime;

namespace Signet.EpubCheck.Core;

/// <summary>The kinds of document that can be checked alone (the <c>--mode</c> values of the tool).</summary>
public enum SingleDocumentKind
{
    /// <summary>A package document (<c>opf</c>).</summary>
    PackageDocument,

    /// <summary>An XHTML content document (<c>xhtml</c>).</summary>
    XhtmlContentDocument,

    /// <summary>An SVG content document (<c>svg</c>).</summary>
    SvgContentDocument,

    /// <summary>A media overlays document (<c>mo</c>, EPUB 3 only).</summary>
    MediaOverlaysDocument,

    /// <summary>A navigation document (<c>nav</c>, EPUB 3 only).</summary>
    NavigationDocument,
}

/// <summary>
/// Checks one document outside a publication, as upstream's tool does for <c>--mode opf|xhtml|svg|mo|nav</c>: the checker of
/// the kind gets a context made from the file's URL and the media type the mode gives.
/// </summary>
public static class SingleDocumentChecker
{
    /// <summary>The mode names of the tool and the kinds they stand for.</summary>
    public static bool TryParseMode(string? mode, out SingleDocumentKind kind)
    {
        switch (mode)
        {
            case "opf": kind = SingleDocumentKind.PackageDocument; return true;
            case "xhtml": kind = SingleDocumentKind.XhtmlContentDocument; return true;
            case "svg": kind = SingleDocumentKind.SvgContentDocument; return true;
            case "mo": kind = SingleDocumentKind.MediaOverlaysDocument; return true;
            case "nav": kind = SingleDocumentKind.NavigationDocument; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>Whether upstream's tool has a checker of this kind for the version (the media overlays and the navigation document are EPUB 3 only).</summary>
    public static bool IsSupported(SingleDocumentKind kind, EpubVersion version) =>
        kind is not (SingleDocumentKind.MediaOverlaysDocument or SingleDocumentKind.NavigationDocument) || version == EpubVersion.Version3;

    /// <summary>Checks the document at <paramref name="path"/>, reporting to <paramref name="report"/>.</summary>
    /// <exception cref="NotSupportedException">The kind has no checker for the version (<see cref="IsSupported"/>).</exception>
    public static void Check(string path, SingleDocumentKind kind, EpubVersion version, EpubProfile profile, IReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);
        if (!IsSupported(kind, version))
        {
            throw new NotSupportedException($"The {kind} mode needs EPUB 3.");
        }

        ValidationContext Single(string mimeType) => new()
        {
            Url = EpubCheckUrlUtils.ToUrl(path),
            Report = report,
            Culture = (report as ReportBase)?.Culture,
            Version = version,
            Profile = profile,
            MimeType = mimeType,
        };

        switch (kind)
        {
            case SingleDocumentKind.PackageDocument:
                (version == EpubVersion.Version2 ? new OPFChecker(Single(MediaTypes.PackageDocument)) : new OPFChecker30(Single(MediaTypes.PackageDocument))).Check();
                break;
            case SingleDocumentKind.XhtmlContentDocument:
                new OPSChecker(Single(MediaTypes.Xhtml)).Check();
                break;
            case SingleDocumentKind.SvgContentDocument:
                new OPSChecker(Single(MediaTypes.Svg)).Check();
                break;
            case SingleDocumentKind.MediaOverlaysDocument:
                new OverlayChecker(Single(MediaTypes.Smil)).Check();
                break;
            case SingleDocumentKind.NavigationDocument:
                new NavChecker(Single(MediaTypes.Xhtml)).Check();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
