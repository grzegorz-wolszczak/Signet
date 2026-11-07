using System;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Dtbook;

/// <summary>Checks a DTBook content document of EPUB 2 (upstream's <c>DTBookChecker</c>): it is validated with <c>dtbook-2005-2.rng</c> and read by <see cref="DTBookHandler"/>.</summary>
public class DTBookChecker : PublicationResourceChecker
{
    private const string DtBookMimeType = "application/x-dtbook+xml";

    /// <summary>A checker of the DTBook document of <paramref name="context"/>.</summary>
    /// <exception cref="InvalidOperationException">The resource is not a DTBook document by its media type (upstream's precondition; <c>CHK-008</c>).</exception>
    public DTBookChecker(ValidationContext context)
        : base(context)
    {
        if (context.MimeType != DtBookMimeType)
        {
            throw new InvalidOperationException("The resource is not a DTBook document.");
        }
    }

    /// <inheritdoc />
    protected override bool CheckContent()
    {
        XmlParser parser = new(Context);
        parser.AddValidator(XmlValidators.DtbookRng.Get());
        parser.AddContentHandler(new DTBookHandler(Context));
        parser.Process();
        return true;
    }
}
