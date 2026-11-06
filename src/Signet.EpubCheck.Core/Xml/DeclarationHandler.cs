using System;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util.Mime;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>
/// Checks the document type declaration of an XML resource and its entity declarations (upstream's
/// <c>DeclarationHandler</c>): which DOCTYPEs a package document, an XHTML document, an SVG, a MathML or an NCX document
/// of a version may have (<c>HTM-004</c>, <c>HTM-009</c>, <c>OPF-073</c>), and that EPUB 3 declares no external entity
/// (<c>HTM-003</c>). The reports of entities used but not declared (<c>HTM-011</c>) need the entity events of the reader
/// and come with the content documents.
/// </summary>
internal sealed class DeclarationHandler(ValidationContext context) : IDeclarationHandler
{
    private const string Xhtml11PublicId = "-//W3C//DTD XHTML 1.1//EN";
    private const string Xhtml11SystemId = "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd";
    private const string Xhtml11Doctype = "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\" \n\"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">";
    private const string Oeb12PublicId = "+//ISBN 0-9673008-1-9//DTD OEB 1.2 Package//EN";
    private const string Oeb12SystemId = "http://openebook.org/dtds/oeb-1.2/oebpkg12.dtd";
    private const string NcxPublicId = "-//NISO//DTD ncx 2005-1//EN";
    private const string NcxSystemId = "http://www.daisy.org/z3986/2005/ncx-2005-1.dtd";
    private const string NcxDoctype = "<!DOCTYPE ncx PUBLIC \"-//NISO//DTD ncx 2005-1//EN\" \n \"http://www.daisy.org/z3986/2005/ncx-2005-1.dtd\">";
    private const string SvgPublicId = "-//W3C//DTD SVG 1.1//EN";
    private const string SvgSystemId = "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd";
    private const string MathMlPublicId = "-//W3C//DTD MathML 3.0//EN";
    private const string MathMlSystemId = "http://www.w3.org/Math/DTD/mathml3/mathml3.dtd";
    private const string LegacyCompat = "about:legacy-compat";
    private const string Html5Doctype = "<!DOCTYPE html>";
    private const string OpfMimeType = "opf";

    private ILocator? _locator;
    private bool _first = true;

    public void SetDocumentLocator(ILocator locator) => _locator = locator;

    public void StartDtd(string rootName, string? publicId, string? systemId)
    {
        if (!_first)
        {
            return;
        }

        _first = false;
        if (context.Version == EpubVersion.Version2)
        {
            CheckEpub2(rootName, publicId, systemId);
        }
        else if (context.Version == EpubVersion.Version3)
        {
            CheckEpub3(rootName, publicId, systemId);
        }
    }

    public void InternalEntityDecl(string name, string value)
    {
    }

    public void ExternalEntityDecl(string name, string? publicId, string? systemId)
    {
        if (context.Version == EpubVersion.Version3)
        {
            context.Report.Message(MessageId.HTM_003, Location(), name);
        }
    }

    private void CheckEpub2(string root, string? publicId, string? systemId)
    {
        if (MediaTypes.Is(MediaTypes.Xhtml, context.MimeType) && root == "html"
            && MatchDoctypeId(Xhtml11PublicId, publicId, Xhtml11Doctype))
        {
            MatchDoctypeId(Xhtml11SystemId, systemId, Xhtml11Doctype);
        }

        if (context.MimeType == OpfMimeType && (publicId is not null || systemId is not null))
        {
            bool legacyPackage = root == "package" && (publicId is null || publicId == Oeb12PublicId) && (systemId is null || systemId == Oeb12SystemId);
            if (!legacyPackage)
            {
                context.Report.Message(MessageId.HTM_009, Location());
            }
        }

        if (context.MimeType == "application/x-dtbncx+xml" && MatchDoctypeId(NcxPublicId, publicId, NcxDoctype))
        {
            MatchDoctypeId(NcxSystemId, systemId, NcxDoctype);
        }
    }

    private void CheckEpub3(string root, string? publicId, string? systemId)
    {
        if (MediaTypes.Is(MediaTypes.Xhtml, context.MimeType) && string.Equals(root, "html", StringComparison.OrdinalIgnoreCase))
        {
            if (!(publicId is null && (systemId is null || systemId == LegacyCompat)))
            {
                context.Report.Message(MessageId.HTM_004, Location(), publicId, Html5Doctype);
            }
        }
        else if (publicId is not null || systemId is not null)
        {
            bool allowed = context.MimeType switch
            {
                MediaTypes.Svg => publicId == SvgPublicId && systemId == SvgSystemId,
                "application/mathml+xml" or "application/mathml-content+xml" or "application/mathml-presentation+xml" =>
                    publicId == MathMlPublicId && systemId == MathMlSystemId,
                "application/x-dtbncx+xml" => publicId == NcxPublicId && systemId == NcxSystemId,
                _ => false,
            };
            if (!allowed)
            {
                context.Report.Message(MessageId.OPF_073, Location());
            }
        }
    }

    private bool MatchDoctypeId(string expected, string? given, string complete)
    {
        if (expected != given)
        {
            context.Report.Message(MessageId.HTM_004, Location(), given ?? string.Empty, complete);
            return false;
        }

        return true;
    }

    private EpubLocation Location() =>
        _locator is null ? context.Location() : context.Location().At(_locator.LineNumber, _locator.ColumnNumber);
}
