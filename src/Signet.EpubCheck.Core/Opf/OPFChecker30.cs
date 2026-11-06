using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>
/// Checks an EPUB 3 package document (upstream's <c>OPFChecker30</c>): the schemas of EPUB 3 and of the profile or
/// publication type, the manifest items (<c>RSC-029</c>, <c>PKG-025</c>, <c>OPF-090</c>, <c>OPF-091</c>), the spine items
/// (<c>OPF-043</c>, <c>OPF-044</c>, <c>OPF-077</c>, <c>OPF-100</c>), the linked resources (<c>OPF-067</c>) and the
/// collections (dictionaries, indexes, previews: <c>OPF-071</c>, <c>OPF-075</c>, <c>OPF-076</c>, <c>OPF-081</c>–<c>OPF-084</c>).
/// </summary>
/// <remarks>
/// Not ported yet, because they need what other tasks bring: the checks that use the features the content documents report
/// (pagination, semantics, navigation, dictionaries: <c>NAV-003</c>–<c>NAV-008</c>, <c>OPF-066</c>, <c>OPF-078</c>, <c>OPF-079</c>,
/// <c>HTM-051</c>), the total duration of media overlays (<c>MED-016</c>), and the checks after the resources were validated
/// (<c>RSC-006</c>, <c>OPF-096</c>, <c>OPF-097</c>, <c>MED-010</c>–<c>MED-013</c>).
/// </remarks>
public class OPFChecker30(ValidationContext context) : OPFChecker(context)
{
    private const string SearchKeyMapType = "application/vnd.epub.search-key-map+xml";
    private const string XhtmlType = "application/xhtml+xml";
    private const string SvgType = "image/svg+xml";
    private const string EpubCfiScheme = "epubcfi";
    private const string MetaInfPrefix = "META-INF/";

    /// <inheritdoc />
    protected override OPFHandler CreateHandler() => new OPFHandler30(Context);

    /// <inheritdoc />
    protected override IEnumerable<XmlValidators> Validators()
    {
        yield return XmlValidators.Opf30Rnc;
        yield return XmlValidators.Opf30Sch;
        yield return XmlValidators.Opf30CollectionDoSch;
        yield return XmlValidators.Opf30CollectionDictSch;
        yield return XmlValidators.Opf30CollectionIdxSch;
        yield return XmlValidators.Opf30CollectionManifestSch;
        yield return XmlValidators.Opf30CollectionPreviewSch;
        if (Context.Profile == EpubProfile.Dict || Context.PublicationTypes.Contains(PublicationType.Dictionary))
        {
            yield return XmlValidators.OpfDictSch;
        }

        if (Context.Profile == EpubProfile.Edupub || Context.PublicationTypes.Contains(PublicationType.Edupub))
        {
            yield return XmlValidators.OpfEdupubSch;
        }

        if (Context.Profile == EpubProfile.Preview || Context.PublicationTypes.Contains(PublicationType.Preview))
        {
            yield return XmlValidators.OpfPreviewSch;
        }
    }

    /// <inheritdoc />
    protected override bool CheckContent()
    {
        base.CheckContent();
        OPFHandler30 handler = (OPFHandler30)OpfHandler!;
        CheckLinkedResources(handler);
        CheckCollections(handler);
        return true;
    }

    /// <inheritdoc />
    protected override void CheckItem(OPFItem item, OPFHandler handler)
    {
        if (item.HasDataUrl)
        {
            Report.Message(MessageId.RSC_029, item.Location);
            return;
        }

        if (item.Path.StartsWith(MetaInfPrefix, System.StringComparison.Ordinal))
        {
            Report.Message(MessageId.PKG_025, item.Location);
        }

        string mimeType = item.MimeType;
        if (string.IsNullOrEmpty(mimeType) || !OpfMediaTypes.IsWellFormed(mimeType))
        {
            return;
        }

        if (OpfMediaTypes.GetPreferredMediaType(mimeType, item.Path) is { } preferred)
        {
            Report.Message(MessageId.OPF_090, item.Location, preferred, mimeType);
        }

        if (!item.IsRemote && item.Url.Fragment is not null)
        {
            Report.Message(MessageId.OPF_091, item.Location);
        }
    }

    /// <inheritdoc />
    protected override void CheckSpineItem(OPFItem item, OPFHandler handler)
    {
        if (item.HasDataUrl)
        {
            return;
        }

        CheckSpineItemProperties(item, (OPFHandler30)handler);
        if (!OpfMediaTypes.IsBlessedItemType(item.MimeType, Version))
        {
            if (!item.HasFallback)
            {
                Report.Message(MessageId.OPF_043, item.Location, item.MimeType);
            }
            else if (!item.HasContentDocumentFallback)
            {
                Report.Message(MessageId.OPF_044, item.Location, item.MimeType);
            }
        }
    }

    private void CheckSpineItemProperties(OPFItem item, OPFHandler30 handler)
    {
        if (!item.IsFixedLayout || handler.Metadata.ContainsPrimary(RenditionVocabs.MetaVocab.Get(RenditionVocabs.MetaProperty.Layout), "roll"))
        {
            foreach (VocabProperty property in item.Properties)
            {
                if (RenditionVocabs.SpreadProperties.Contains(property))
                {
                    Report.Message(MessageId.OPF_100, item.Location, property.PrefixedName);
                }
            }
        }

        if (item.Properties.Contains(PackageVocabs.ItemVocab.Get(PackageVocabs.ItemProperty.DataNav)))
        {
            Report.Message(MessageId.OPF_077, item.Location);
        }
    }

    private void CheckLinkedResources(OPFHandler30 handler)
    {
        foreach (LinkedResource link in handler.LinkedResources.AsList)
        {
            if (handler.GetItemByUrl(link.DocumentUrl) is { IsInSpine: false } item)
            {
                Report.Message(MessageId.OPF_067, Context.Location(), item.Path);
            }
        }
    }

    private void CheckCollections(OPFHandler30 handler)
    {
        foreach (CollectionNode collection in handler.Collections.AsList)
        {
            if (collection.HasRole(CollectionRole.Dictionary))
            {
                CheckDictionaryCollection(collection, handler);
            }

            if (collection.HasRole(CollectionRole.Index))
            {
                CheckIndexCollection(collection, handler);
            }

            if (collection.HasRole(CollectionRole.Preview))
            {
                CheckPreviewCollection(collection, handler);
            }
        }
    }

    private void CheckDictionaryCollection(CollectionNode collection, OPFHandler handler)
    {
        bool searchKeyMapFound = false;
        foreach (LinkedResource resource in collection.Resources.AsList)
        {
            OPFItem? item = handler.GetItemByUrl(resource.DocumentUrl);
            if (item is null)
            {
                Report.Message(MessageId.OPF_081, Context.Location(), Context.Relativize(resource.DocumentUrl));
            }
            else if (item.MimeType == SearchKeyMapType)
            {
                if (searchKeyMapFound)
                {
                    Report.Message(MessageId.OPF_082, Context.Location());
                }

                searchKeyMapFound = true;
            }
            else if (item.MimeType != XhtmlType)
            {
                Report.Message(MessageId.OPF_084, Context.Location(), Context.Relativize(resource.DocumentUrl));
            }
        }

        if (!searchKeyMapFound)
        {
            Report.Message(MessageId.OPF_083, Context.Location());
        }
    }

    private void CheckIndexCollection(CollectionNode collection, OPFHandler handler)
    {
        if (!collection.HasRole(CollectionRole.Index) && !collection.HasRole(CollectionRole.IndexGroup))
        {
            return;
        }

        foreach (LinkedResource resource in collection.Resources.AsList)
        {
            if (handler.GetItemByUrl(resource.DocumentUrl) is not { MimeType: XhtmlType })
            {
                Report.Message(MessageId.OPF_071, Context.Location());
            }
        }

        foreach (CollectionNode child in collection.Collections.AsList)
        {
            CheckIndexCollection(child, handler);
        }
    }

    private void CheckPreviewCollection(CollectionNode collection, OPFHandler handler)
    {
        foreach (LinkedResource resource in collection.Resources.AsList)
        {
            if (handler.GetItemByUrl(resource.DocumentUrl) is not { MimeType: XhtmlType or SvgType })
            {
                Report.Message(MessageId.OPF_075, Context.Location());
            }
            else
            {
                EpubUrlFragment fragment = EpubUrlFragment.Parse(resource.Url);
                if (fragment.Exists && fragment.Scheme == EpubCfiScheme)
                {
                    Report.Message(MessageId.OPF_076, Context.Location());
                }
            }
        }
    }
}
