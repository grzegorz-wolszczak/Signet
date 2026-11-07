using System;
using System.Collections.Generic;
using System.Globalization;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Util.Mime;

namespace Signet.EpubCheck.Core.References;

/// <summary>
/// Checks the references the resources of a publication make once all of them were read (upstream's
/// <c>ResourceReferencesChecker</c>): that a reference points to a resource that exists and is declared, that it is allowed
/// to be remote, that the resource has the type the reference needs and a fallback if it needs one, that the fragment
/// identifier points to an id of the right kind, and that the links of the table of contents, of the page list and of the
/// media overlays are in reading order.
/// </summary>
public sealed class ResourceReferencesChecker
{
    private const string EpubCfiScheme = "epubcfi";
    private const string ContentFallbackKind = "content-fallback";
    private const string TocText = "toc";
    private const string PageListText = "page-list";
    private const string SpineOrder = "spine";
    private const string DocumentOrder = "document";

    private readonly IReport _report;
    private readonly OCFContainer _container;
    private readonly EpubVersion _version;
    private readonly ReferenceRegistry _referenceRegistry;
    private readonly ResourceRegistry _resourceRegistry;
    private readonly CultureInfo _culture;
    private readonly HashSet<Url> _undeclared = [];

    /// <summary>A checker of the references of the publication of <paramref name="context"/>, which must have a container.</summary>
    public ResourceReferencesChecker(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Container is null || context.ResourceRegistry is null || context.ReferenceRegistry is null)
        {
            throw new ArgumentException("The references can only be checked in a container.", nameof(context));
        }

        _report = context.Report;
        _container = context.Container;
        _version = context.Version;
        _referenceRegistry = context.ReferenceRegistry;
        _resourceRegistry = context.ResourceRegistry;
        _culture = (context.Report as ReportBase)?.Culture ?? CultureInfo.GetCultureInfo("en");
    }

    /// <summary>Checks all the references registered so far.</summary>
    public void Check()
    {
        _undeclared.Clear();
        Queue<Reference> tocLinks = new();
        Queue<Reference> pageListLinks = new();
        Queue<Reference> overlayLinks = new();
        foreach (Reference reference in _referenceRegistry.AsList())
        {
            switch (reference.Type)
            {
                case ReferenceType.RegionBasedNav:
                    CheckRegionBasedNav(reference);
                    break;
                case ReferenceType.NavTocLink:
                    tocLinks.Enqueue(reference);
                    break;
                case ReferenceType.NavPagelistLink:
                    pageListLinks.Enqueue(reference);
                    break;
                case ReferenceType.OverlayTextLink:
                    overlayLinks.Enqueue(reference);
                    CheckReference(reference);
                    break;
                default:
                    CheckReference(reference);
                    break;
            }
        }

        // Upstream collects the page list links and never checks their order.
        CheckReadingOrder(tocLinks);
        CheckReadingOrder(overlayLinks);
    }

    private void CheckReference(Reference reference)
    {
        _report.Info(reference.Location.Path, FeatureKind.Resource, _container.Relativize(reference.Url));

        Resource? targetResource = _resourceRegistry.GetResource(reference.TargetResource);
        if (_container.IsRemote(reference.Url) && !CheckRemoteReference(reference, targetResource))
        {
            return;
        }

        if (targetResource is null)
        {
            CheckUndeclaredReference(reference);
            return;
        }

        CheckFallbacks(reference, targetResource);
        EpubUrlFragment fragment = EpubUrlFragment.Parse(reference.Url, targetResource.MimeType);
        if (CheckReferenceType(reference, targetResource, fragment))
        {
            CheckFragment(reference, targetResource, fragment);
        }
    }

    private void CheckFragment(Reference reference, Resource targetResource, EpubUrlFragment fragment)
    {
        string targetMimeType = targetResource.MimeType;
        if (!fragment.Exists || fragment.IsEmpty)
        {
            return;
        }

        // media overlays requirements
        if (reference.Type == ReferenceType.OverlayTextLink)
        {
            // a reference to XHTML indicates an element by id
            if (MediaTypes.Is(MediaTypes.Xhtml, targetMimeType) && fragment.Id.Length == 0)
            {
                _report.Message(MessageId.MED_017, reference.Location, fragment.ToString());
            }

            // a reference to SVG uses an SVG fragment identifier
            else if (MediaTypes.Is(MediaTypes.Svg, targetMimeType) && !fragment.IsValid)
            {
                _report.Message(MessageId.MED_018, reference.Location, fragment.ToString());
            }
        }

        // The fragments based on an id are checked; the other kinds (EPUB CFI) are not.
        if (fragment.Id.Length == 0 || _container.IsRemote(reference.TargetResource))
        {
            return;
        }

        ReferenceType? targetIdType = _resourceRegistry.GetIdType(fragment.Id, targetResource);

        // the target id exists (if the target is XHTML or SVG)
        if (targetIdType is null && (MediaTypes.Is(MediaTypes.Svg, targetMimeType) || MediaTypes.Is(MediaTypes.Xhtml, targetMimeType)))
        {
            _report.Message(MessageId.RSC_012, reference.Location.WithContext(reference.Url.ToString()));
            return;
        }

        switch (reference.Type)
        {
            case ReferenceType.SvgPaint:
            case ReferenceType.SvgClipPath:
                if (targetIdType != reference.Type)
                {
                    _report.Message(MessageId.RSC_014, reference.Location.WithContext(reference.Url.ToString()));
                }

                break;
            case ReferenceType.SvgSymbol:
            case ReferenceType.Cite:
            case ReferenceType.Hyperlink:
            case ReferenceType.OverlayTextLink:
                if (targetIdType != reference.Type && targetIdType != ReferenceType.Generic)
                {
                    _report.Message(MessageId.RSC_014, reference.Location.WithContext(reference.Url.ToString()));
                }

                break;
        }
    }

    // Returns false when an error was reported and the checks of the reference stop.
    private bool CheckReferenceType(Reference reference, Resource targetResource, EpubUrlFragment fragment)
    {
        string targetMimeType = targetResource.MimeType;
        EpubLocation here = reference.Location.WithContext(_container.Relativize(reference.Url));
        switch (reference.Type)
        {
            case ReferenceType.Hyperlink:
                if (fragment.Scheme == EpubCfiScheme)
                {
                    break; // EPUB CFI is not supported
                }

                // if the media type is missing, an error was reported already
                if (!OpfMediaTypes.IsBlessedItemType(targetMimeType, _version)
                    && !OpfMediaTypes.IsDeprecatedBlessedItemType(targetMimeType)
                    && !targetResource.HasContentDocumentFallback)
                {
                    _report.Message(MessageId.RSC_010, here);
                    return false;
                }

                if (!targetResource.IsInSpine)
                {
                    _report.Message(MessageId.RSC_011, here);
                    return false;
                }

                break;
            case ReferenceType.Image:
                if (fragment.Scheme == EpubCfiScheme)
                {
                    break; // EPUB CFI is not supported
                }

                if (fragment.Exists && !MediaTypes.Is(MediaTypes.Svg, targetMimeType))
                {
                    _report.Message(MessageId.RSC_009, here);
                    return false;
                }

                break;
            case ReferenceType.OverlayTextLink:
                if (!OpfMediaTypes.IsBlessedItemType(targetMimeType, _version))
                {
                    _report.Message(MessageId.RSC_010, here);
                    return false;
                }

                break;
            case ReferenceType.Script:
                if (!OpfMediaTypes.IsScriptType(targetMimeType))
                {
                    _report.Message(MessageId.RSC_034, here);
                    return false;
                }

                if (!OpfMediaTypes.IsBlessedScriptType(targetMimeType))
                {
                    _report.Message(MessageId.RSC_035, here, targetMimeType);
                }

                break;
            case ReferenceType.SearchKey:
                if ((!fragment.Exists || fragment.Scheme != EpubCfiScheme) && !targetResource.IsInSpine)
                {
                    _report.Message(MessageId.RSC_021, reference.Location, _container.Relativize(reference.TargetResource));
                    return false;
                }

                break;
            case ReferenceType.Stylesheet:
                if (fragment.Exists)
                {
                    _report.Message(MessageId.RSC_013, here);
                    return false;
                }

                break;
            case ReferenceType.SvgClipPath:
            case ReferenceType.SvgPaint:
            case ReferenceType.SvgSymbol:
                if (!fragment.Exists)
                {
                    _report.Message(MessageId.RSC_015, reference.Location.WithContext(reference.Url));
                    return false;
                }

                break;
        }

        return true;
    }

    private void CheckFallbacks(Reference reference, Resource targetResource)
    {
        // the manifest fallback of a content (outdated)
        if (reference.Type.IsPublicationResourceReference() && targetResource.HasManifestFallback)
        {
            _report.Message(MessageId.OBS_001, reference.Location, ContentFallbackKind, _container.Relativize(reference.TargetResource));
        }

        // the resource has a core media type fallback
        string targetMimeType = targetResource.MimeType;
        if (reference.Type is ReferenceType.Image or ReferenceType.Audio or ReferenceType.Generic
            && !reference.HasIntrinsicFallback
            && !OpfMediaTypes.IsCoreMediaType(targetMimeType)
            && !targetResource.HasCoreMediaTypeFallback)
        {
            _report.Message(MessageId.RSC_032, reference.Location, _container.Relativize(reference.TargetResource), targetMimeType);
        }
    }

    private void CheckUndeclaredReference(Reference reference)
    {
        // a reference to a local resource that is not in the container
        if (!_container.Contains(reference.Url) && !_container.IsRemote(reference.Url))
        {
            // only a warning for a 'link' in EPUB 3, an error otherwise
            _report.Message(
                _version == EpubVersion.Version3 && reference.Type == ReferenceType.Link ? MessageId.RSC_007w : MessageId.RSC_007,
                reference.Location,
                _container.Relativize(reference.TargetResource));
        }

        // a publication resource that is not declared (once); links and remote hyperlinks and cites are not publication resources
        else if (!_undeclared.Contains(reference.TargetResource)
            && !(reference.Type == ReferenceType.Link
                || (_container.IsRemote(reference.TargetResource) && reference.Type is ReferenceType.Hyperlink or ReferenceType.Cite)))
        {
            _undeclared.Add(reference.TargetResource);
            _report.Message(MessageId.RSC_008, reference.Location, _container.Relativize(reference.TargetResource));
        }
    }

    // Returns false when an error was reported and the checks of the reference stop.
    private bool CheckRemoteReference(Reference reference, Resource? targetResource)
    {
        bool linkLike = reference.Type is ReferenceType.Cite or ReferenceType.Link or ReferenceType.Hyperlink;
        bool spineItemOfEpub3 = _version == EpubVersion.Version3 && targetResource is { IsInSpine: true };
        bool mediaOrFont = _version == EpubVersion.Version3
            && ((targetResource is not null
                    && (OpfMediaTypes.IsAudioType(targetResource.MimeType) || OpfMediaTypes.IsVideoType(targetResource.MimeType) || OpfMediaTypes.IsFontType(targetResource.MimeType)))
                || reference.Type is ReferenceType.Font or ReferenceType.Audio or ReferenceType.Video);

        // Remote links and hyperlinks are not publication resources, spine items are checked with the package document, and
        // audio, video and fonts can be remote in EPUB 3.
        if (!linkLike && !spineItemOfEpub3 && !mediaOrFont)
        {
            _report.Message(MessageId.RSC_006, reference.Location, reference.Url);
            return false;
        }

        // the remote resource uses HTTPS (file URLs are disallowed and reported elsewhere)
        if (_version == EpubVersion.Version3
            && reference.Type is not (ReferenceType.Link or ReferenceType.Hyperlink)
            && reference.Url.Scheme != "https"
            && reference.Url.Scheme != "file")
        {
            _report.Message(MessageId.RSC_031, reference.Location, reference.Url);
        }

        return true;
    }

    private void CheckRegionBasedNav(Reference reference)
    {
        // the link target must be a fixed-layout item
        Resource? resource = _resourceRegistry.GetResource(reference.TargetResource);
        if (resource is not { Item.IsFixedLayout: true })
        {
            _report.Message(MessageId.NAV_009, reference.Location);
        }
    }

    private void CheckReadingOrder(Queue<Reference> references)
    {
        int lastSpinePosition = -1;
        int lastAnchorPosition = -1;
        while (references.Count > 0)
        {
            Reference reference = references.Dequeue();

            // The link target must be a spine item (checked elsewhere).
            if (_resourceRegistry.GetResource(reference.TargetResource) is not { Item.IsInSpine: true } resource)
            {
                continue;
            }

            int targetSpinePosition = resource.Item.SpinePosition;
            if (targetSpinePosition < lastSpinePosition)
            {
                ReportOrder(reference, SpineOrder);
                lastSpinePosition = targetSpinePosition;
                lastAnchorPosition = -1;
                continue;
            }

            // a new spine item resets the last positions
            if (targetSpinePosition > lastSpinePosition)
            {
                lastSpinePosition = targetSpinePosition;
                lastAnchorPosition = -1;
            }

            // the fragment is in document order
            EpubUrlFragment fragment = EpubUrlFragment.Parse(reference.Url, resource.MimeType);
            int targetAnchorPosition = _resourceRegistry.GetIdPosition(fragment.Id, resource);
            if (targetAnchorPosition > -1)
            {
                if (targetAnchorPosition < lastAnchorPosition)
                {
                    ReportOrder(reference, DocumentOrder);
                }

                lastAnchorPosition = targetAnchorPosition;
            }
        }
    }

    private void ReportOrder(Reference reference, string kind)
    {
        string orderContext = LocalizedMessages.GetInstance(_culture).GetSuggestion(MessageId.NAV_011, kind);
        if (reference.Type == ReferenceType.OverlayTextLink)
        {
            _report.Message(MessageId.MED_015, reference.Location, _container.Relativize(reference.Url), orderContext);
        }
        else
        {
            _report.Message(
                MessageId.NAV_011,
                reference.Location,
                reference.Type == ReferenceType.NavTocLink ? TocText : PageListText,
                _container.Relativize(reference.Url),
                orderContext);
        }
    }
}
