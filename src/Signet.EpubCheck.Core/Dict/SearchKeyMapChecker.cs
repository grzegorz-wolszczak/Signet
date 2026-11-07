using System;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Dict;

/// <summary>
/// Checks the search key map of an EPUB dictionary (upstream's <c>SearchKeyMapChecker</c>): the file must end with <c>.xml</c>
/// (<c>OPF-080</c>), it is validated with <c>search-key-map.rnc</c> and read by <see cref="SearchKeyMapHandler"/>.
/// </summary>
public sealed class SearchKeyMapChecker : PublicationResourceChecker
{
    private const string SearchKeyMapMimeType = "application/vnd.epub.search-key-map+xml";
    private const string XmlExtension = ".xml";

    /// <summary>A checker of the search key map of <paramref name="context"/>.</summary>
    /// <exception cref="InvalidOperationException">The resource is not a search key map by its media type (upstream's precondition; <c>CHK-008</c>).</exception>
    public SearchKeyMapChecker(ValidationContext context)
        : base(context)
    {
        if (context.MimeType != SearchKeyMapMimeType)
        {
            throw new InvalidOperationException("The resource is not a search key map.");
        }
    }

    /// <inheritdoc />
    protected override bool CheckContent()
    {
        // the file name
        if (!Context.Path.EndsWith(XmlExtension, StringComparison.Ordinal))
        {
            Report.Message(MessageId.OPF_080, Context.Location());
        }

        // the content
        XmlParser parser = new(Context);
        parser.AddValidator(XmlValidators.SearchKeyMapRnc.Get());
        parser.AddContentHandler(new SearchKeyMapHandler(Context));
        parser.Process();
        return true;
    }
}

/// <summary>Reads a search key map (upstream's <c>SearchKeyMapHandler</c>): the <c>href</c> of its groups and matches are references to the dictionary.</summary>
public sealed class SearchKeyMapHandler : XmlHandler
{
    /// <summary>A handler of the search key map of <paramref name="context"/>.</summary>
    public SearchKeyMapHandler(ValidationContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    protected override void StartElement()
    {
        XMLElement e = CurrentElement;
        if (e.Namespace == Namespaces.Ops && e.Name is "search-key-group" or "match")
        {
            RegisterReference(CheckUrl(e.GetAttribute("href")), ReferenceType.SearchKey);
        }
    }
}
