using System;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Ncx;

/// <summary>
/// Checks the NCX of an EPUB 2 publication (upstream's <c>NCXChecker</c>): the document is validated with <c>ncx.rng</c> and
/// <c>ncx.sch</c>, an NCX in an EPUB 3 publication is obsolete (<c>OBS-001</c>), the <c>dtb:uid</c> must not have white space
/// around it (<c>NCX-004</c>) and must be the unique identifier of the package document (<c>NCX-001</c>).
/// </summary>
public class NCXChecker : PublicationResourceChecker
{
    private const string NcxMediaType = "application/x-dtbncx+xml";

    private string? _ncxId;

    /// <summary>A checker of the NCX of <paramref name="context"/>.</summary>
    /// <exception cref="InvalidOperationException">The resource is not an NCX by its media type (upstream's precondition; <c>CHK-008</c>).</exception>
    public NCXChecker(ValidationContext context)
        : base(context)
    {
        if (context.MimeType != NcxMediaType)
        {
            throw new InvalidOperationException("The resource is not an NCX document.");
        }
    }

    /// <inheritdoc />
    protected override bool CheckContent()
    {
        if (Context.Version == EpubVersion.Version3)
        {
            Report.Message(MessageId.OBS_001, Context.Location(), "ncx");
        }

        XmlParser parser = new(Context);
        parser.AddValidator(XmlValidators.NcxRng.Get());
        parser.AddValidator(XmlValidators.NcxSch.Get());
        NCXHandler handler = new(Context);
        parser.AddContentHandler(handler);
        parser.Process();

        _ncxId = handler.Uid;
        if (_ncxId is not null && _ncxId != JavaStrings.Trim(_ncxId))
        {
            Report.Message(MessageId.NCX_004, Context.Location());
        }

        return true;
    }

    /// <inheritdoc />
    protected override bool CheckPublicationAfterContent()
    {
        if (Context.FeatureReport.GetFeature(FeatureKind.UniqueIdent) is [{ Value: { } uid }, ..] && _ncxId is not null && uid != JavaStrings.Trim(_ncxId))
        {
            Report.Message(MessageId.NCX_001, Context.Location(), _ncxId, uid);
        }

        return true;
    }
}

/// <summary>Reads the NCX (upstream's <c>NCXHandler</c>): the references of its <c>content</c> elements, the <c>dtb:uid</c>, empty labels.</summary>
internal sealed class NCXHandler(ValidationContext context) : XmlHandler(context)
{
    private const string NcxNamespace = "http://www.daisy.org/z3986/2005/ncx/";
    private const string TextKey = "text";

    public string? Uid { get; private set; }

    public override void Characters(string text)
    {
        XMLElement? element = CurrentElement;
        if (element is { Namespace: NcxNamespace, Name: "text" })
        {
            element.SetPrivateData(TextKey, (string?)element.GetPrivateData(TextKey) + text);
        }
    }

    protected override void StartElement()
    {
        XMLElement element = CurrentElement;
        if (element.Namespace != NcxNamespace)
        {
            return;
        }

        if (element.Name == "content")
        {
            if (CheckUrl(element.GetAttribute("src")) is { } url)
            {
                if (Context.IsRemote(url))
                {
                    Report.Info(Path, FeatureKind.Reference, url.ToString());
                }

                RegisterReference(url, ReferenceType.Hyperlink);
            }
        }
        else if (element.Name == "meta" && element.GetAttribute("name") == "dtb:uid")
        {
            Uid = element.GetAttribute("content");
        }
    }

    protected override void EndElement()
    {
        XMLElement element = CurrentElement;
        if (element is { Namespace: NcxNamespace, Name: "text" } && JavaStrings.Trim((string?)element.GetPrivateData(TextKey) ?? string.Empty).Length == 0)
        {
            Report.Message(MessageId.NCX_006, Location());
        }
    }
}
