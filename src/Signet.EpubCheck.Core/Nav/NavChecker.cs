using System;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Xml;
using static Signet.EpubCheck.Core.Xml.ValidationContextPredicates;

namespace Signet.EpubCheck.Core.Nav;

/// <summary>
/// Checks the navigation document of an EPUB 3 publication (upstream's <c>NavChecker</c>): the document is validated with the
/// schemas of navigation documents and of the profile or publication type, and read by <see cref="NavHandler"/>. A navigation
/// document in an EPUB 2 publication is reported (<c>NAV-001</c>).
/// </summary>
public class NavChecker : PublicationResourceChecker
{
    private const string XhtmlMimeType = "application/xhtml+xml";

    private static readonly ValidatorMap s_validatorMap = ValidatorMap.CreateBuilder()
        .PutAll(XmlValidators.Nav30Rnc, XmlValidators.Xhtml30Sch, XmlValidators.Nav30Sch)
        .PutAll(
            And(Or(Profile(EpubProfile.Edupub), HasPubType(PublicationType.Edupub)),
                Not(HasProp(EpubCheckVocab.Property.FixedLayout)),
                Not(HasProp(EpubCheckVocab.Property.NonLinear))),
            XmlValidators.XhtmlEdupubStructureSch, XmlValidators.XhtmlEdupubSemanticsSch, XmlValidators.XhtmlIdxSch)
        .PutAll(
            And(Or(Profile(EpubProfile.Dict), HasPubType(PublicationType.Dictionary)), MimeType(XhtmlMimeType), Version(EpubVersion.Version3)),
            XmlValidators.XhtmlDictSch)
        .PutAll(
            And(Or(HasProp(PackageVocabs.ItemVocab.Get(PackageVocabs.ItemProperty.Index)), HasProp(EpubCheckVocab.Property.InIndexCollection)),
                MimeType(XhtmlMimeType), Version(EpubVersion.Version3)),
            XmlValidators.XhtmlIdxSch, XmlValidators.XhtmlIdxIndexSch)
        .Build();

    /// <summary>A checker of the navigation document of <paramref name="context"/>.</summary>
    /// <exception cref="InvalidOperationException">The resource is not XHTML by its media type (upstream's precondition; <c>CHK-008</c>).</exception>
    public NavChecker(ValidationContext context)
        : base(context)
    {
        if (context.MimeType != XhtmlMimeType)
        {
            throw new InvalidOperationException("The navigation document is not an XHTML document.");
        }

        if (context.Version == EpubVersion.Version2)
        {
            context.Report.Message(MessageId.NAV_001, context.Location());
        }
    }

    /// <inheritdoc />
    protected override bool CheckContent()
    {
        XmlParser parser = new(Context);
        parser.AddContentHandler(new NavHandler(Context));
        foreach (XmlValidator validator in s_validatorMap.GetValidators(Context))
        {
            parser.AddValidator(validator);
        }

        parser.Process();
        return true;
    }
}
