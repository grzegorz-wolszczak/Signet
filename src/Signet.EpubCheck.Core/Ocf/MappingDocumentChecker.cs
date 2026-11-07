using System;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Xml;
using static Signet.EpubCheck.Core.Xml.ValidationContextPredicates;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// Checks the mapping document of the renditions of a publication (upstream's <c>MappingDocumentChecker</c>): an XHTML document
/// validated with <c>mapping.rnc</c> and <c>mapping.sch</c> when the check has the <c>epubcheck:rendition-mapping</c> property.
/// </summary>
internal sealed class MappingDocumentChecker : IChecker
{
    private static readonly ValidatorMap s_validatorMap = ValidatorMap.CreateBuilder()
        .PutAll(HasProp(EpubCheckVocab.Property.RenditionMapping), XmlValidators.RenditionMappingRnc, XmlValidators.RenditionMappingSch)
        .Build();

    private readonly ValidationContext _context;

    public MappingDocumentChecker(ValidationContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public void Check()
    {
        XmlParser parser = new(_context);
        foreach (XmlValidator validator in s_validatorMap.GetValidators(_context))
        {
            parser.AddValidator(validator);
        }

        parser.Process();
    }
}
