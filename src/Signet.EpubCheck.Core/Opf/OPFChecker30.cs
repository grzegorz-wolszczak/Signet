using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
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
/// After the items were checked, the features the content documents reported are checked: pagination (<c>NAV-003</c>,
/// <c>OPF-066</c>), semantics (<c>HTM-051</c>), navigation (<c>NAV-004</c>–<c>NAV-008</c>) and dictionaries (<c>OPF-078</c>,
/// <c>OPF-079</c>). Not ported yet, because they need what other tasks bring: the total duration of media overlays
/// (<c>MED-016</c>), and the checks after the resources were validated (<c>RSC-006</c>, <c>OPF-096</c>, <c>OPF-097</c>,
/// <c>MED-010</c>–<c>MED-013</c>).
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
    protected override void CheckItemAfterResourceValidation(OPFItem item)
    {
        // remote resources
        string mimeType = item.MimeType;
        if (item.IsRemote && !(OpfMediaTypes.IsAudioType(mimeType) || OpfMediaTypes.IsVideoType(mimeType)
            || mimeType == "application/x-shockwave-flash" || OpfMediaTypes.IsFontType(mimeType)))
        {
            // Spine items cannot be remote resources (except, theoretically, audio, video and fonts).
            if (item.IsInSpine)
            {
                Report.Message(MessageId.RSC_006, item.Location, item.Path);
            }

            // no direct reference to the resource was found
            else if (!Context.ReferenceRegistry!.HasReferencesTo(item.Url))
            {
                // it may be allowed when the resource is retrieved from a script
                Report.Message(Context.FeatureReport.HasFeature(FeatureKind.HasScripts) ? MessageId.RSC_006b : MessageId.RSC_006, item.Location, item.Path);
            }
        }

        // the resources of the manifest are referenced (usage)
        if (!(item.IsInSpine || item.IsNav || item.IsNcx)
            && Context.ReferenceRegistry is { } references
            && !references.AsList().Any(reference => reference.TargetResource.Equals(item.Url) && reference.Type.IsPublicationResourceReference()))
        {
            Report.Message(MessageId.OPF_097, item.Location, item.Path);
        }

        // The media overlay attribute of the item is checked with the media overlays (task 3.9).

        // the non-linear content documents are reachable
        if (item.IsInSpine && !item.IsLinear && Context.ReferenceRegistry is { } hyperlinks
            && !hyperlinks.AsList().Any(reference => reference.Type == ReferenceType.Hyperlink && reference.TargetResource.Equals(item.Url)))
        {
            // If the content is scripted, references can be added by scripting, so only a usage is reported.
            Report.Message(Context.FeatureReport.HasFeature(FeatureKind.HasScripts) ? MessageId.OPF_096b : MessageId.OPF_096, item.Location, item.Path);
        }
    }

    /// <inheritdoc />
    protected override bool CheckPackage()
    {
        base.CheckPackage();
        CheckCollectionsContent();
        CheckPagination();
        CheckSemantics();
        CheckNav();
        CheckSpecifics();
        return false;
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

        if (Context.ReferenceRegistry is { } references && !string.IsNullOrEmpty(item.MediaOverlay)
            && handler.GetItemById(item.MediaOverlay) is { } overlay)
        {
            references.RegisterReference(overlay.Url, ReferenceType.MediaOverlay, item.Location);
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

    private bool IsEdupub => Context.Profile == EpubProfile.Edupub || Context.PublicationTypes.Contains(PublicationType.Edupub);

    // The package document may not have been read (it is missing from the container): upstream's handler is then empty.
    private MetadataSet PackageMetadata => (OpfHandler as OPFHandler30)?.Metadata ?? MetadataSet.CreateBuilder().Build();

    // A dictionary collection needs a content document that was reported as dictionary content.
    private void CheckCollectionsContent()
    {
        if (OpfHandler is not OPFHandler30 handler)
        {
            return;
        }

        foreach (CollectionNode collection in handler.Collections.AsList)
        {
            if (collection.HasRole(CollectionRole.Dictionary))
            {
                CheckDictionaryCollectionContent(collection, handler);
            }
        }
    }

    private void CheckDictionaryCollectionContent(CollectionNode collection, OPFHandler handler)
    {
        bool dictionaryFound = false;
        foreach (LinkedResource resource in collection.Resources.AsList)
        {
            OPFItem? item = handler.GetItemByUrl(resource.DocumentUrl);
            if (!dictionaryFound && item is { MimeType: XhtmlType })
            {
                // Search if this resource was reported as dictionary content.
                dictionaryFound = Context.FeatureReport.GetFeature(FeatureKind.Dictionary).Any(dictionary => item.Path == dictionary.Location.Path);
            }
        }

        if (!dictionaryFound)
        {
            Report.Message(MessageId.OPF_078, Context.Location());
        }
    }

    // An EDUPUB publication with page breaks needs a page list and a source of the pagination.
    private void CheckPagination()
    {
        if (!IsEdupub || !Context.FeatureReport.HasFeature(FeatureKind.PageBreak))
        {
            return;
        }

        // there is a page list
        if (!Context.FeatureReport.HasFeature(FeatureKind.PageList))
        {
            Report.Message(MessageId.NAV_003, Context.Location());
        }

        // a "dc:source" metadata expression, refined by a "source-of: pagination" expression
        IReadOnlyList<MetadataSet.Metadata> sources = PackageMetadata.GetPrimary(DCMESVocab.Vocab.Get(DCMESVocab.Property.Source));
        if (sources.Count == 0
            || MetadataSet.TryFindInRefines(sources, PackageVocabs.MetaVocab.Get(PackageVocabs.MetaProperty.SourceOf), "pagination") is null)
        {
            Report.Message(MessageId.OPF_066, Context.Location());
        }
    }

    // An EDUPUB publication with microdata needs RDFa as well.
    private void CheckSemantics()
    {
        if (IsEdupub && Context.FeatureReport.HasFeature(FeatureKind.HasMicrodata) && !Context.FeatureReport.HasFeature(FeatureKind.HasRdfa))
        {
            Report.Message(MessageId.HTM_051, Context.FeatureReport.GetFeature(FeatureKind.HasMicrodata)[0].Location);
        }
    }

    // An EDUPUB publication needs a navigation to its sections, and lists of the audio, figures, tables and videos it has.
    private void CheckNav()
    {
        if (!IsEdupub)
        {
            return;
        }

        FeatureReport features = Context.FeatureReport;
        IReadOnlyList<Feature> sections = features.GetFeature(FeatureKind.Sections);
        IReadOnlyList<Feature> tocLinks = features.GetFeature(FeatureKind.TocLinks);
        EpubLocation location = tocLinks.Count == 0 ? Context.Location() : tocLinks[0].Location;
        if (sections.Count != tocLinks.Count)
        {
            Report.Message(MessageId.NAV_004, location);
        }

        if (features.HasFeature(FeatureKind.Audio) && !features.HasFeature(FeatureKind.Loa))
        {
            Report.Message(MessageId.NAV_005, location);
        }

        if (features.HasFeature(FeatureKind.Figure) && !features.HasFeature(FeatureKind.Loi))
        {
            Report.Message(MessageId.NAV_006, location);
        }

        if (features.HasFeature(FeatureKind.Table) && !features.HasFeature(FeatureKind.Lot))
        {
            Report.Message(MessageId.NAV_007, location);
        }

        if (features.HasFeature(FeatureKind.Video) && !features.HasFeature(FeatureKind.Lov))
        {
            Report.Message(MessageId.NAV_008, location);
        }
    }

    // Dictionary content belongs in a dictionary, and a dictionary needs dictionary content.
    private void CheckSpecifics()
    {
        if (Context.FeatureReport.HasFeature(FeatureKind.Dictionary) && !Context.PublicationTypes.Contains(PublicationType.Dictionary))
        {
            Report.Message(MessageId.OPF_079, Context.FeatureReport.GetFeature(FeatureKind.Dictionary)[0].Location);
        }

        if ((Context.Profile == EpubProfile.Dict || Context.PublicationTypes.Contains(PublicationType.Dictionary))
            && !Context.FeatureReport.HasFeature(FeatureKind.Dictionary))
        {
            Report.Message(MessageId.OPF_078, Context.Location());
        }
    }
}
