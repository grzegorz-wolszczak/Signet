using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Dtbook;

/// <summary>Reads a DTBook document (upstream's <c>DTBookHandler</c>): registers its ids and the references of its <c>a</c>, <c>link</c> and <c>img</c> elements.</summary>
public class DTBookHandler : XmlHandler
{
    private const string DtBookNamespace = "http://www.daisy.org/z3986/2005/dtbook/";

    /// <summary>A handler of the DTBook document of <paramref name="context"/>.</summary>
    public DTBookHandler(ValidationContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    protected override void StartElement()
    {
        XMLElement e = CurrentElement;
        if (e.Namespace != DtBookNamespace)
        {
            return;
        }

        // registers the ids
        Context.ResourceRegistry?.RegisterId(e.GetAttribute("id"), ReferenceType.Generic, Context.Url);

        // Checks the cross-references (link@href, a@href, img@src). They are only allowed to leave the publication when the
        // `external` attribute of the `a` is true.
        Url? url = null;
        ReferenceType type = ReferenceType.Generic;
        switch (e.Name)
        {
            case "a":
                url = CheckUrl(e.GetAttribute("href"));
                if (url is not null && e.GetAttribute("external") == "true")
                {
                    // Upstream's FIXME: it should check that the attribute is set for remote URLs.
                    if (Context.IsRemote(url))
                    {
                        Report.Info(Path, FeatureKind.Reference, url.ToString());
                        if (!UriSchemes.Contains(url.Scheme))
                        {
                            Report.Message(MessageId.OPF_021, Location(), url.ToHumanString());
                        }

                        url = null;
                    }
                }

                break;
            case "link":
                url = CheckUrl(e.GetAttribute("href"));
                break;
            case "img":
                url = CheckUrl(e.GetAttribute("src"));
                type = ReferenceType.Image;
                break;
        }

        if (url is not null)
        {
            RegisterReference(url, type);
            if (Context.IsRemote(url))
            {
                Report.Info(Path, FeatureKind.Reference, url.ToString());
            }
        }
    }
}
