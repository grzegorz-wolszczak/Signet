using System.Collections.Generic;
using System.IO;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Xml;
using static Signet.EpubCheck.Core.Xml.ValidationContextPredicates;

namespace Signet.EpubCheck.Core.Ops;

/// <summary>
/// Checks an XHTML or SVG content document (upstream's <c>OPSChecker</c>): the document is validated with the schemas its
/// media type, EPUB version, profile and properties call for, and read by <see cref="OPSHandler"/> (EPUB 2) or
/// <see cref="OPSHandler30"/> (EPUB 3).
/// </summary>
public class OPSChecker : PublicationResourceChecker
{
    private const string XhtmlMimeType = "application/xhtml+xml";
    private const string SvgMimeType = "image/svg+xml";

    private static readonly ValidatorMap s_validatorMap = ValidatorMap.CreateBuilder()
        .PutAll(And(MimeType(XhtmlMimeType), Version(EpubVersion.Version2)),
            XmlValidators.Xhtml20Nvdl, XmlValidators.Xhtml20Sch, XmlValidators.Idunique20Sch)
        .PutAll(And(MimeType(XhtmlMimeType), Version(EpubVersion.Version3)),
            XmlValidators.Xhtml30Nvdl, XmlValidators.Svg30InformativeNvdl)
        .PutAll(And(MimeType(SvgMimeType), Version(EpubVersion.Version2)),
            XmlValidators.Svg20Nvdl, XmlValidators.Idunique20Sch)
        .PutAll(And(MimeType(SvgMimeType), Version(EpubVersion.Version3)),
            XmlValidators.Svg30Nvdl, XmlValidators.Svg30InformativeNvdl)
        .PutAll(
            And(Or(Profile(EpubProfile.Dict), HasPubType(PublicationType.Dictionary)), MimeType(XhtmlMimeType), Version(EpubVersion.Version3)),
            XmlValidators.XhtmlDictSch)
        .PutAll(
            And(Or(Profile(EpubProfile.Edupub), HasPubType(PublicationType.Edupub)),
                Not(HasProp(EpubCheckVocab.Property.FixedLayout)),
                Not(HasProp(EpubCheckVocab.Property.NonLinear)),
                MimeType(XhtmlMimeType), Version(EpubVersion.Version3)),
            XmlValidators.XhtmlEdupubStructureSch, XmlValidators.XhtmlEdupubSemanticsSch, XmlValidators.XhtmlIdxSch)
        .PutAll(
            And(Or(Profile(EpubProfile.Idx), HasPubType(PublicationType.Index),
                    HasProp(PackageVocabs.ItemVocab.Get(PackageVocabs.ItemProperty.Index)),
                    HasProp(EpubCheckVocab.Property.InIndexCollection)),
                MimeType(XhtmlMimeType), Version(EpubVersion.Version3)),
            XmlValidators.XhtmlIdxSch, XmlValidators.XhtmlIdxIndexSch)
        .Put(HasProp(PackageVocabs.ItemVocab.Get(PackageVocabs.ItemProperty.DataNav)), XmlValidators.XhtmlDatanavSch)
        .Build();

    /// <summary>A checker of the content document of <paramref name="context"/>.</summary>
    public OPSChecker(ValidationContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    protected override bool CheckContent()
    {
        IReadOnlyList<XmlValidator> validators = s_validatorMap.GetValidators(Context);
        try
        {
            Validate(validators);
        }
        catch (IOException)
        {
            Report.Message(MessageId.PKG_008, Context.Location(), Context.Path);
        }

        return true;
    }

    private void Validate(IReadOnlyList<XmlValidator> validators)
    {
        XmlParser parser = new(Context);
        parser.AddContentHandler(Context.Version == EpubVersion.Version2 ? new OPSHandler(Context) : new OPSHandler30(Context));
        foreach (XmlValidator validator in validators)
        {
            parser.AddValidator(validator);
        }

        parser.Process();
    }
}
