using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Util.Mime;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Ops;

/// <summary>
/// Reads a content document of EPUB 3 (upstream's <c>OPSHandler30</c>): the semantics of <c>epub:type</c>, the item properties
/// the document needs (<c>OPF-014</c>, <c>OPF-015</c>, <c>OPF-018</c>), the sources of images, audio, video and objects with
/// their fallbacks, the viewport of fixed-layout documents, links to alternate style sheets, discouraged elements and the
/// accessibility of anchors and MathML.
/// </summary>
public partial class OPSHandler30 : OPSHandler
{
    private const string HasPalpableContent = "IS_PALPABLE";
    private const string Audio = "audio";
    private const string Video = "video";
    private const string Svg = "svg";
    private const string Picture = "picture";
    private const string Source = "source";
    private const string ViewportName = "viewport";
    private const string SvgMimeType = "image/svg+xml";
    private const string CodecsParameter = "codecs";
    private const string AudioMp4 = "audio/mp4";
    private const string AudioOgg = "audio/ogg";
    private const string Src = "src";

    private static readonly IReadOnlyDictionary<string, IVocab> s_reservedVocabs = new Dictionary<string, IVocab>
    {
        [string.Empty] = AggregateVocab.Of(StructureVocab.Vocab, DataNavVocab.Vocab, DictVocab.Vocab, IndexVocab.Vocab, ComicsVocab.Vocab, StructureVocab.UncheckedVocab),
        [MagazineNavigationVocab.Prefix] = MagazineNavigationVocab.Vocab,
        [ForeignVocabs.PrismPrefix] = ForeignVocabs.PrismVocab,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IVocab> s_altCssVocabs = new Dictionary<string, IVocab>
    {
        [string.Empty] = AggregateVocab.Of(AltStylesheetVocab.Vocab, new UncheckedVocab(string.Empty, string.Empty)),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IVocab> s_knownVocabUris = new Dictionary<string, IVocab>
    {
        [MagazineNavigationVocab.Uri] = MagazineNavigationVocab.Vocab,
        [ForeignVocabs.PrismUri] = ForeignVocabs.PrismVocab,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> s_defaultVocabUris = new HashSet<string>(StringComparer.Ordinal) { StructureVocab.Uri };

    // The names of the event attributes that run a script (upstream's scriptEvents and mouseEvents).
    private static readonly FrozenSet<string> s_scriptEvents = FrozenSet.ToFrozenSet(
        [
            "onafterprint", "onbeforeprint", "onbeforeunload", "onerror", "onhaschange", "onload", "onmessage", "onoffline",
            "onpagehide", "onpageshow", "onpopstate", "onredo", "onresize", "onstorage", "onundo", "onunload",
            "onblur", "onchange", "oncontextmenu", "onfocus", "onformchange", "onforminput", "oninput", "oninvalid", "onreset",
            "onselect", "onsubmit",
            "onkeydown", "onkeypress", "onkeyup",
            "onabort", "oncanplay", "oncanplaythrough", "ondurationchange", "onemptied", "onended", "onerror", "onloadeddata",
            "onloadedmetadata", "onloadstart", "onpause", "onplay", "onplaying", "onprogress", "onratechange",
            "onreadystatechange", "onseeked", "onseeking", "onstalled", "onsuspend", "ontimeupdate", "onvolumechange",
            "onwaiting",
            "onclick", "ondblclick", "ondrag", "ondragend", "ondragenter", "ondragleave", "ondragover", "ondragstart", "ondrop",
            "onmousedown", "onmousemove", "onmouseout", "onmouseover", "onmouseup", "onmousewheel", "onscroll",
        ],
        StringComparer.Ordinal);

    private readonly SortedSet<PackageVocabs.ItemProperty> _requiredProperties = [];
    private readonly SortedSet<PackageVocabs.ItemProperty> _allowedProperties = [];
    private readonly bool _isLinear;

    // Upstream keeps a HashMap, whose iteration order does not matter there; the order here is the order of the sources.
    private List<(Url? Url, string? MimeType)> _mediaSources = [];
    private IReadOnlyDictionary<string, IVocab> _vocabs = s_reservedVocabs;

    /// <summary>A handler of the content document of <paramref name="context"/>.</summary>
    public OPSHandler30(ValidationContext context)
        : base(context)
    {
        _isLinear = !context.HasProperty(EpubCheckVocab.Property.NonLinear);
    }

    /// <summary>Inside a <c>picture</c>.</summary>
    protected bool InPicture { get; set; }

    /// <summary>The last <c>a</c> has no text yet.</summary>
    protected bool AnchorNeedsText { get; set; }

    /// <summary>Inside MathML.</summary>
    protected bool InMathML { get; set; }

    /// <summary>Inside SVG.</summary>
    protected bool InSvg { get; set; }

    /// <summary>Inside the <c>body</c>, before its first non-<c>section</c> child (the sections feature).</summary>
    protected bool InBody { get; set; }

    /// <summary>Inside a region-based <c>nav</c> of a data navigation document.</summary>
    protected bool InRegionBasedNav { get; set; }

    /// <summary>The outermost <c>svg</c> of an SVG document was already read.</summary>
    protected bool IsOutermostSvgAlreadyProcessed { get; set; }

    /// <summary>The MathML has an <c>alttext</c> or an <c>annotation-xml</c>.</summary>
    protected bool HasAltOrAnnotation { get; set; }

    /// <summary>The SVG anchor has a label.</summary>
    protected bool HasLabel { get; set; }

    /// <summary>A list item was seen.</summary>
    protected bool HasListItem { get; set; }

    /// <summary>The first viewport <c>meta</c> was read.</summary>
    protected bool HasViewport { get; set; }

    /// <inheritdoc />
    protected override void CheckImage(string? href)
    {
        XMLElement e = CurrentElement;

        // For SVG and MathML elements, only the reference is registered.
        if (e.Namespace != Namespaces.Xhtml)
        {
            base.CheckImage(href);
            return;
        }

        string? src = e.GetAttribute(Src);
        string? srcset = e.GetAttribute("srcset");

        // the image URLs to register: the sorted set of the source and of the URLs of the source set
        SortedSet<string> imageSources = new(StringComparer.Ordinal);
        if (src is not null)
        {
            imageSources.Add(src);
        }

        imageSources.UnionWith(SourceSet.Parse(srcset).ImageUrls);

        foreach (string urlString in imageSources)
        {
            Url? url = CheckResourceUrl(urlString);
            if (url is null || Context.ReferenceRegistry is null)
            {
                continue;
            }

            Resource? imageResource = Context.ResourceRegistry!.GetResource(EpubCheckUrlUtils.DocUrl(url));

            // the fallback rules of picture
            if (InPicture && imageResource is not null)
            {
                string mimeType = imageResource.MimeType;
                Url imageUrl = imageResource.Url;
                switch (e.Name)
                {
                    case "img":
                        // an `img` child of `picture` MUST be a core media type resource
                        if (!OpfMediaTypes.IsBlessedImageType(mimeType, EpubVersion.Version3))
                        {
                            Report.Message(MessageId.MED_003, Location(), Context.Relativize(imageUrl), mimeType);
                        }

                        break;
                    case "source":
                        // a `source` child of `picture` MUST be a core media type resource or have a `type` attribute
                        string type = JavaStrings.Trim(e.GetAttribute("type") ?? string.Empty);
                        if (type.Length == 0 && !OpfMediaTypes.IsBlessedImageType(mimeType, EpubVersion.Version3))
                        {
                            Report.Message(MessageId.MED_007, Location(), Context.Relativize(imageUrl), mimeType);
                        }
                        else
                        {
                            // warn about HTML-declared/EPUB-declared type mismatch
                            CheckMimetypeMatches(url, type);
                        }

                        break;
                }
            }

            // The manifest fallback of the image is only checked when it is not in a `picture`.
            RegisterReference(url, ReferenceType.Image, InPicture);
        }
    }

    /// <summary>Parses the <c>epub:type</c> value <paramref name="type"/>, reports what is wrong with it, and reads the semantics.</summary>
    protected virtual void CheckType(string? type)
    {
        if (type is null)
        {
            return;
        }

        IReadOnlyList<VocabProperty> propList = VocabUtil.ParsePropertyList(type, _vocabs, VocabContext, Location());
        CheckTypes(VocabProperty.Filter<StructureVocab.EpubType>(propList));

        // the properties of the structure vocabulary that are not known
        foreach (VocabProperty property in propList)
        {
            if (property.VocabUri == StructureVocab.Uri && property.Value is null)
            {
                Report.Message(MessageId.OPF_088, Location(), property.Name);
            }
        }

        // the 'region-based' property (data navigation documents)
        if (propList.Contains(DataNavVocab.Vocab.Get(DataNavVocab.EpubType.RegionBased)))
        {
            if (CurrentElement.Name != "nav" || !Context.HasProperty(PackageVocabs.ItemVocab.Get(PackageVocabs.ItemProperty.DataNav)))
            {
                Report.Message(MessageId.HTM_052, Location());
            }
            else
            {
                InRegionBasedNav = true;
            }
        }

        // whether the document has dictionary content
        if (propList.Contains(DictVocab.Vocab.Get(DictVocab.EpubType.Dictionary)))
        {
            Context.FeatureReport.Report(FeatureKind.Dictionary, Location());
        }
    }

    /// <summary>Reads the structural semantics of an element: the features they stand for and the item properties they allow.</summary>
    protected virtual void CheckTypes(IReadOnlySet<StructureVocab.EpubType> types)
    {
        if (types.Contains(StructureVocab.EpubType.Pagebreak))
        {
            Context.FeatureReport.Report(FeatureKind.PageBreak, Location());
        }

        if (types.Contains(StructureVocab.EpubType.Index))
        {
            _allowedProperties.Add(PackageVocabs.ItemProperty.Index);
            Context.FeatureReport.Report(FeatureKind.Index, Location());
        }

        if (types.Contains(StructureVocab.EpubType.Glossary))
        {
            _allowedProperties.Add(PackageVocabs.ItemProperty.Glossary);
        }
    }

    /// <inheritdoc />
    protected override IReadOnlyList<Url> CheckSvgFontFaceUri()
    {
        IReadOnlyList<Url> urls = base.CheckSvgFontFaceUri();
        if (urls.Any(Context.IsRemote))
        {
            _requiredProperties.Add(PackageVocabs.ItemProperty.RemoteResources);
        }

        return urls;
    }

    /// <summary>Checks the <c>ssml:ph</c> attribute (issue 139: a real check of the IPA and x-SAMPA syntax is still to come upstream).</summary>
    protected void CheckSsmlPh(string? ph)
    {
        if (ph is not null && JavaStrings.Trim(ph).Length < 1)
        {
            Report.Message(MessageId.HTM_007, Location());
        }
    }

    /// <inheritdoc />
    public override void Characters(string text)
    {
        base.Characters(text);

        // the palpable state
        if (JavaStrings.Trim(text).Length != 0)
        {
            // Upstream's FIXME: this should only be set for elements that allow flow or phrasing content.
            CurrentElement.SetPrivateData(HasPalpableContent, true);
        }

        if (AnchorNeedsText)
        {
            AnchorNeedsText = false;
        }
    }

    /// <inheritdoc />
    protected override void StartElement()
    {
        base.StartElement();

        XMLElement e = CurrentElement;

        // the initial palpable state of the element
        e.SetPrivateData(HasPalpableContent, false);

        CheckDiscouragedElements();
        ProcessSemantics();
        ProcessSectioning();

        string name = e.Name;
        if (e.Namespace == Namespaces.Xhtml)
        {
            switch (name)
            {
                case "html":
                    _vocabs = VocabUtil.ParsePrefixDeclaration(
                        e.GetAttributeNS(Namespaces.Ops, "prefix"), s_reservedVocabs, s_knownVocabUris, s_defaultVocabUris, Report, Location());
                    break;
                case "meta":
                    ProcessMeta();
                    break;
                case "form":
                    _requiredProperties.Add(PackageVocabs.ItemProperty.Scripted);
                    break;
                case "link":
                    ProcessLink();
                    break;
                case Audio:
                    StartMediaElement();
                    break;
                case Video:
                    ProcessVideo();
                    StartMediaElement();
                    break;
                case "figure":
                    ProcessFigure();
                    break;
                case "table":
                    ProcessTable();
                    break;
                case "track":
                    StartTrack();
                    break;
                case "a":
                    AnchorNeedsText = true;
                    ProcessAnchor(e);
                    break;
                case "input":
                    StartInput();
                    break;
                case Picture:
                    InPicture = true;
                    break;
                case Source:
                    if (e.Parent?.Name == Picture)
                    {
                        CheckImage(null);
                    }
                    else
                    {
                        // an audio or video source
                        StartMediaSource();
                    }

                    break;
                case "embed":
                    StartEmbed();
                    break;
                case "blockquote":
                case "q":
                case "ins":
                case "del":
                    CheckCiteAttribute();
                    break;
            }
        }
        else if (e.Namespace == Namespaces.MathMl)
        {
            if (name == "math")
            {
                _requiredProperties.Add(PackageVocabs.ItemProperty.Mathml);
                InMathML = true;
                HasAltOrAnnotation = e.GetAttribute("alttext") is not null;
                string? altimg = e.GetAttribute("altimg");
                if (altimg is not null)
                {
                    CheckImage(altimg);
                }
            }
            else if (name == "annotation-xml")
            {
                HasAltOrAnnotation = true;
            }
        }
        else if (e.Namespace == Namespaces.Svg)
        {
            switch (name)
            {
                case Svg:
                    ProcessSvg();
                    break;
                case "a":
                    AnchorNeedsText = true;
                    ProcessAnchor(e);
                    break;
                case "title":
                case "text":
                    HasLabel = true;
                    break;
            }
        }
        else if (e.Namespace == Namespaces.Ops && name == "switch")
        {
            _requiredProperties.Add(PackageVocabs.ItemProperty.Switch);
        }

        ProcessInlineScripts();

        CheckType(e.GetAttributeNS(Namespaces.Ops, "type"));

        CheckSsmlPh(e.GetAttributeNS(Namespaces.Ssml, "ph"));
    }

    /// <inheritdoc />
    protected override IReadOnlyList<string?> GetSvgHrefs()
    {
        string? href = CurrentElement.GetAttribute("href");
        string? xhref = CurrentElement.GetAttributeNS(Namespaces.XLink, "href");
        if (xhref is not null && href is null)
        {
            Report.Message(MessageId.HTM_062, Location());
            return [xhref];
        }

        if (xhref is not null && xhref != href)
        {
            Report.Message(MessageId.HTM_063, Location(), xhref, href);
            return [href, xhref];
        }

        return [href];
    }

    private void CheckCiteAttribute() => RegisterReference(CheckUrl(CurrentElement.GetAttribute("cite")), ReferenceType.Cite);

    private void StartTrack() => RegisterReference(CheckResourceUrl(CurrentElement.GetAttribute(Src)), ReferenceType.Track);

    private void StartInput() => RegisterReference(CheckResourceUrl(CurrentElement.GetAttribute(Src)), ReferenceType.Generic);

    private void StartEmbed()
    {
        Url? url = CheckResourceUrl(CurrentElement.GetAttribute(Src));
        CheckMimetypeMatches(url, CurrentElement.GetAttribute("type"));
        RegisterReference(url, ReferenceType.Generic);
    }

    /// <summary>Reports the elements EPUB discourages (<c>HTM-055</c>).</summary>
    protected void CheckDiscouragedElements()
    {
        XMLElement element = CurrentElement;
        if (element.Namespace == Namespaces.Xhtml && element.Name is "base" or "embed" or "rp")
        {
            Report.Message(MessageId.HTM_055, Location(), element.Name);
        }
    }

    /// <summary>An event attribute makes the document scripted.</summary>
    protected void ProcessInlineScripts()
    {
        foreach (XMLAttr attribute in CurrentElement.Attributes)
        {
            if (s_scriptEvents.Contains(attribute.Name.ToLowerInvariant()))
            {
                ProcessJavascript();
                return;
            }
        }
    }

    /// <inheritdoc />
    protected override void CheckScript()
    {
        base.CheckScript();
        RegisterReference(CheckResourceUrl(CurrentElement.GetAttribute(Src)), ReferenceType.Script);
    }

    /// <inheritdoc />
    protected override void ProcessJavascript()
    {
        base.ProcessJavascript();
        _requiredProperties.Add(PackageVocabs.ItemProperty.Scripted);
    }

    /// <summary>Checks the classes of a <c>link</c> to an alternate style sheet (<c>CSS-005</c>).</summary>
    protected void ProcessLink()
    {
        string? classAttribute = CurrentElement.GetAttribute("class");
        if (classAttribute is null)
        {
            return;
        }

        IReadOnlyList<VocabProperty> properties = VocabUtil.ParsePropertyList(classAttribute, s_altCssVocabs, VocabContext, Location());
        IReadOnlySet<AltStylesheetVocab.Property> altClasses = VocabProperty.Filter<AltStylesheetVocab.Property>(properties);

        if (properties.Count == 1)
        {
            return;
        }

        bool vertical = altClasses.Contains(AltStylesheetVocab.Property.Vertical);
        bool horizontal = altClasses.Contains(AltStylesheetVocab.Property.Horizontal);
        bool day = altClasses.Contains(AltStylesheetVocab.Property.Day);
        bool night = altClasses.Contains(AltStylesheetVocab.Property.Night);

        if ((vertical && horizontal) || (day && night))
        {
            Report.Message(MessageId.CSS_005, Location(), classAttribute);
        }
    }

    /// <summary>Starts an anchor (HTML or SVG): whether it needs a label.</summary>
    protected void ProcessAnchor(XMLElement e)
    {
        if (e.GetAttribute("href") is null)
        {
            AnchorNeedsText = false;
        }

        if (InSvg || Context.MimeType == SvgMimeType)
        {
            string? title = e.GetAttributeNS(Namespaces.XLink, "title");
            string? ariaLabel = e.GetAttribute("aria-label");
            HasLabel = !string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(ariaLabel);
        }
    }

    /// <summary>Starts an <c>audio</c> or <c>video</c> element.</summary>
    protected void StartMediaElement()
    {
        _mediaSources = [];

        // The schema ensures that if `src` is set, the element has no `source` children.
        Url? url = CheckResourceUrl(CurrentElement.GetAttribute(Src));

        // registers the reference (nothing when the URL is null)
        RegisterMediaResource(url, Context.GetMimeType(url), hasFallback: false);
    }

    /// <summary>Ends an <c>audio</c> or <c>video</c> element: registers its sources.</summary>
    protected void EndMediaElement()
    {
        // The element has an intrinsic fallback if any of its source children is a core media type resource.
        bool hasFallback = _mediaSources.Any(source => OpfMediaTypes.IsCoreMediaType(source.MimeType));

        // registers the sources with the fallback flag
        foreach ((Url? url, string? mimeType) in _mediaSources)
        {
            RegisterMediaResource(url, mimeType, hasFallback);
        }
    }

    /// <summary>Starts a <c>source</c> of an <c>audio</c> or <c>video</c> element.</summary>
    protected void StartMediaSource()
    {
        XMLElement element = CurrentElement;
        if (element.Parent?.Name is not (Audio or Video))
        {
            return; // a schema error was reported
        }

        Url? url = CheckResourceUrl(element.GetAttribute(Src));

        // the rules of a full publication
        if (Context.Container is not null)
        {
            // the media type of the resource
            string? mimeType = CheckMimetypeMatches(url, element.GetAttribute("type"));

            // The type is kept for the fallback check and for registering the resource when the `audio` or `video` closes.
            int index = _mediaSources.FindIndex(source => Equals(source.Url, url));
            if (index >= 0)
            {
                _mediaSources[index] = (url, mimeType);
            }
            else
            {
                _mediaSources.Add((url, mimeType));
            }
        }
    }

    /// <summary>Registers the reference to a media resource.</summary>
    protected void RegisterMediaResource(Url? url, string? mimeType, bool hasFallback)
    {
        if (url is null)
        {
            return;
        }

        if (OpfMediaTypes.IsAudioType(mimeType))
        {
            Context.FeatureReport.Report(FeatureKind.Audio, Location());
            RegisterReference(url, ReferenceType.Audio, hasFallback);
        }
        else
        {
            Context.FeatureReport.Report(FeatureKind.Video, Location());
            RegisterReference(url, ReferenceType.Video);
        }
    }

    /// <summary>
    /// Compares the media type <paramref name="mimeTypeString"/> an element declares with the media type of the resource
    /// <paramref name="resource"/> (<c>OPF-013</c>); returns the declared type, normalized, or the type of the resource when
    /// one of them is not known.
    /// </summary>
    protected string? CheckMimetypeMatches(Url? resource, string? mimeTypeString)
    {
        string? resourceMimeTypeString = Context.GetMimeType(resource);
        if (mimeTypeString is null || resourceMimeTypeString is null)
        {
            return resourceMimeTypeString;
        }

        MimeType? resourceMimeType = MimeType.Parse(resourceMimeTypeString);
        MimeType? mimeType = MimeType.Parse(mimeTypeString);
        if (mimeType is null)
        {
            // Upstream fails here on a type that does not parse.
            Report.Message(MessageId.OPF_013, Location(), Context.Relativize(resource!), mimeTypeString, resourceMimeTypeString);
            return resourceMimeTypeString;
        }

        // The codecs parameter is kept for audio/ogg and audio/mp4, all the parameters are removed otherwise.
        mimeType = mimeType.Essence is AudioMp4 or AudioOgg ? mimeType.FilterParameters(CodecsParameter) : mimeType.FilterParameters();

        // any mismatch is a warning
        if (!mimeType.Equals(resourceMimeType))
        {
            Report.Message(MessageId.OPF_013, Location(), Context.Relativize(resource!), mimeTypeString, resourceMimeTypeString);
        }

        // the declared type, normalized
        return mimeType.ToString();
    }

    /// <summary>Registers the poster of a <c>video</c>.</summary>
    protected void ProcessVideo() => RegisterReference(CheckResourceUrl(CurrentElement.GetAttribute("poster")), ReferenceType.Image);

    /// <inheritdoc />
    protected override void ProcessHyperlink(Url href)
    {
        base.ProcessHyperlink(href);
        if (href.Scheme == "data")
        {
            Report.Message(MessageId.RSC_029, Location());
            return;
        }

        if (InRegionBasedNav)
        {
            RegisterReference(href, ReferenceType.RegionBasedNav);
        }
    }

    /// <summary>Checks the URL of a resource a document uses: a remote one needs the property <c>remote-resources</c>, an SVG one in HTML allows <c>svg</c>.</summary>
    protected Url? CheckResourceUrl(string? src)
    {
        if (src is null || JavaStrings.Trim(src).Length == 0)
        {
            return null;
        }

        // parses and checks the URL
        Url? url = CheckUrl(src);

        // Upstream asks whether a URL that did not parse is remote, which fails.
        if (url is null)
        {
            return null;
        }

        // the `remote-resources` property MUST be set if a remote resource is referenced
        if (Context.IsRemote(url))
        {
            _requiredProperties.Add(PackageVocabs.ItemProperty.RemoteResources);
        }

        // the `svg` property MAY be set if an SVG resource is referenced in HTML
        if (Context.GetMimeType(url) is { } mimeType && MediaTypes.Is(MediaTypes.Svg, mimeType) && !MediaTypes.Is(MediaTypes.Svg, Context.MimeType))
        {
            _allowedProperties.Add(PackageVocabs.ItemProperty.Svg);
        }

        return url;
    }

    /// <inheritdoc />
    protected override void CheckObject()
    {
        // checked in the closing tag
    }

    /// <summary>Ends an <c>object</c>: checks its resource and registers it.</summary>
    protected void EndObject()
    {
        XMLElement element = CurrentElement;

        // the object resource
        Url? url = CheckResourceUrl(element.GetAttribute("data"));

        // the media type declared for the object
        CheckMimetypeMatches(url, element.GetAttribute("type"));

        // The object has an intrinsic fallback if it has palpable content.
        bool hasFallback = (bool)(element.GetPrivateData(HasPalpableContent) ?? false);

        RegisterReference(url, ReferenceType.Generic, hasFallback);
    }

    /// <inheritdoc />
    protected override void CheckIFrame()
    {
        base.CheckIFrame();
        RegisterReference(CheckResourceUrl(CurrentElement.GetAttribute(Src)), ReferenceType.Generic);
    }

    /// <summary>Starts an <c>svg</c> element.</summary>
    protected void ProcessSvg()
    {
        InSvg = true;
        if (Context.MimeType != SvgMimeType)
        {
            _requiredProperties.Add(PackageVocabs.ItemProperty.Svg);
        }
        else if (!IsOutermostSvgAlreadyProcessed)
        {
            IsOutermostSvgAlreadyProcessed = true;
            if (Context.OpfItem is { IsFixedLayout: true } && CurrentElement.GetAttribute("viewBox") is null)
            {
                Report.Message(MessageId.HTM_048, Location());
            }
        }
    }

    /// <summary>Reads a <c>meta</c> element: the viewport of fixed-layout documents.</summary>
    protected void ProcessMeta()
    {
        XMLElement e = CurrentElement;
        if (e.Namespace != Namespaces.Xhtml || JavaStrings.Trim(e.GetAttribute("name") ?? string.Empty) != ViewportName)
        {
            return;
        }

        string? content = e.GetAttribute("content");
        bool fixedLayout = Context.OpfItem is { IsFixedLayout: true };

        // For fixed-layout documents, the first viewport meta element is checked.
        if (!HasViewport && fixedLayout)
        {
            HasViewport = true;
            List<ViewportMetaParseError> syntaxErrors = [];
            ViewportMeta viewport = ViewportMeta.Parse(content, (error, _) => syntaxErrors.Add(error));
            if (syntaxErrors.Count != 0)
            {
                Report.Message(MessageId.HTM_047, Location(), content);
                return;
            }

            foreach (string property in new[] { "width", "height" })
            {
                // the viewport must have a valid width and height
                if (!viewport.HasProperty(property))
                {
                    Report.Message(MessageId.HTM_056, Location(), property);
                    continue;
                }

                IReadOnlyList<string> values = viewport.GetValues(property);
                if (values.Count > 1)
                {
                    Report.Message(MessageId.HTM_059, Location(), property, string.Join(", ", values.Select(value => '"' + value + '"')));
                }

                if (!ViewportMeta.IsValidProperty(property, values[0]))
                {
                    Report.Message(MessageId.HTM_057, Location(), property);
                }
            }
        }
        else if (fixedLayout)
        {
            // a secondary viewport meta in a fixed-layout document is ignored
            Report.Message(MessageId.HTM_060a, Location(), content);
        }
        else
        {
            // a viewport meta in a reflowable document is ignored
            Report.Message(MessageId.HTM_060b, Location(), content);
        }
    }

    /// <summary>A <c>table</c> is a feature.</summary>
    protected virtual void ProcessTable() => Context.FeatureReport.Report(FeatureKind.Table, Location());

    /// <summary>A <c>figure</c> is a feature.</summary>
    protected virtual void ProcessFigure() => Context.FeatureReport.Report(FeatureKind.Figure, Location());

    private void ProcessSemantics()
    {
        XMLElement e = CurrentElement;
        if (e.GetAttribute("itemscope") is not null && !Context.FeatureReport.HasFeature(FeatureKind.HasMicrodata))
        {
            Context.FeatureReport.Report(FeatureKind.HasMicrodata, Location());
        }

        if (e.GetAttribute("property") is not null && !Context.FeatureReport.HasFeature(FeatureKind.HasRdfa))
        {
            Context.FeatureReport.Report(FeatureKind.HasRdfa, Location());
        }
    }

    private void ProcessSectioning()
    {
        XMLElement e = CurrentElement;
        if (_isLinear && Context.Profile == EpubProfile.Edupub && e.Namespace == Namespaces.Xhtml)
        {
            if (e.Name == "body")
            {
                InBody = true;
            }
            else if (InBody && e.Name != "section")
            {
                Context.FeatureReport.Report(FeatureKind.Sections, Location());
                InBody = false;
            }
            else if (e.Name == "section")
            {
                InBody = false;
                Context.FeatureReport.Report(FeatureKind.Sections, Location());
            }
        }
    }

    /// <inheritdoc />
    protected override void EndElement()
    {
        base.EndElement();
        XMLElement e = CurrentElement;
        string name = e.Name;

        if (OpenElements == 0 && name is "html" or Svg)
        {
            CheckOverlaysStyles();
            CheckProperties();
        }
        else if (name == "object")
        {
            EndObject();
        }
        else if (name is Video or Audio)
        {
            EndMediaElement();
        }
        else if (name == "a")
        {
            if (AnchorNeedsText)
            {
                Report.Message(MessageId.ACC_004, Location().WithContext("a"));
                AnchorNeedsText = false;
            }

            if ((InSvg || Context.MimeType == SvgMimeType) && !HasLabel)
            {
                Report.Message(MessageId.ACC_011, Location().WithContext(e.Name));
            }
        }
        else if (name == "math")
        {
            InMathML = false;
            if (!HasAltOrAnnotation)
            {
                Report.Message(MessageId.ACC_009, Location().WithContext("math"));
            }
        }
        else if (name == "nav" && InRegionBasedNav)
        {
            InRegionBasedNav = false;
        }
        else if (name == Picture)
        {
            InPicture = false;
        }
        else if (name == Svg)
        {
            InSvg = false;
        }
        else if (e.Namespace == Namespaces.Xhtml && name == "head")
        {
            CheckHead();
        }

        UpdatePalpableState();
    }

    /// <summary>Whether the current element, which ends, is palpable content.</summary>
    protected bool IsPalpable()
    {
        XMLElement element = CurrentElement;
        string name = element.Name;

        if (element.GetAttribute("hidden") is not null)
        {
            return false;
        }

        switch (element.Namespace)
        {
            case Namespaces.Xhtml:
                switch (name)
                {
                    // embedded content
                    case Audio:
                    case "canvas":
                    case "embed":
                    case "iframe":
                    case "img":
                    case "object":
                    case Picture:
                    case Video:
                        return true;
                    case "html":
                    case "head":
                    case "script":
                    case "link":
                    case "meta":
                    case "title":
                    case "style":
                        return false;
                    default:
                        // Upstream's FIXME: some elements (templates, scripts, …) should be excluded.
                        return (bool)(element.GetPrivateData(HasPalpableContent) ?? false);
                }

            case Namespaces.Svg:
                return name == Svg;
            case Namespaces.MathMl:
                return name == "math";
            default:
                return false;
        }
    }

    private void UpdatePalpableState()
    {
        XMLElement element = CurrentElement;
        if (element.Parent is { } parent)
        {
            bool known = (bool)(parent.GetPrivateData(HasPalpableContent) ?? false);
            parent.SetPrivateData(HasPalpableContent, known || IsPalpable());
        }
    }

    /// <summary>The styles of the media overlay active classes need CSS (<c>CSS-030</c>).</summary>
    protected void CheckOverlaysStyles()
    {
        if (Context.OpfItem is { MediaOverlay: not null }
            && (Context.FeatureReport.HasFeature(FeatureKind.MediaOverlaysActiveClass) || Context.FeatureReport.HasFeature(FeatureKind.MediaOverlaysPlaybackActiveClass))
            && !HasCss)
        {
            Report.Message(MessageId.CSS_030, Location());
        }
    }

    /// <summary>Checks the properties of the manifest item against what the document needs (<c>OPF-014</c>, <c>OPF-015</c>, <c>OPF-018</c>).</summary>
    protected void CheckProperties()
    {
        // single file validation
        if (Context.Container is null)
        {
            return;
        }

        SortedSet<PackageVocabs.ItemProperty> itemProps = [.. VocabProperty.Filter<PackageVocabs.ItemProperty>(Context.Properties)];

        foreach (PackageVocabs.ItemProperty requiredProperty in _requiredProperties.Where(property => !itemProps.Contains(property)))
        {
            Report.Message(MessageId.OPF_014, Context.Location(), PackageVocabs.ItemVocab.GetName(requiredProperty));
        }

        SortedSet<PackageVocabs.ItemProperty> uncheckedProperties = [.. itemProps.Where(property => !_requiredProperties.Contains(property))];
        uncheckedProperties.Remove(PackageVocabs.ItemProperty.Nav);
        uncheckedProperties.Remove(PackageVocabs.ItemProperty.DataNav);
        uncheckedProperties.Remove(PackageVocabs.ItemProperty.CoverImage);
        uncheckedProperties.ExceptWith(_allowedProperties);
        if (uncheckedProperties.Remove(PackageVocabs.ItemProperty.RemoteResources))
        {
            Report.Message(_requiredProperties.Contains(PackageVocabs.ItemProperty.Scripted) ? MessageId.OPF_018b : MessageId.OPF_018, Location());
        }

        if (uncheckedProperties.Count != 0)
        {
            Report.Message(MessageId.OPF_015, Context.Location(), string.Join(", ", PackageVocabs.ItemVocab.GetNames(uncheckedProperties)));
        }
    }

    /// <summary>Checks the <c>head</c> of a fixed-layout document: it needs a viewport (<c>HTM-046</c>).</summary>
    protected void CheckHead()
    {
        if (Context.OpfItem is { IsFixedLayout: true } && !HasViewport)
        {
            Report.Message(MessageId.HTM_046, Location());
        }
    }

    /// <inheritdoc />
    protected override void CheckLink()
    {
        base.CheckLink();
        XMLElement e = CurrentElement;
        string? rel = e.GetAttribute("rel");
        if (rel is not null)
        {
            string? title = e.GetAttribute("title");
            string[] linkTypes = Whitespace().Split(rel).Where(token => token.Length != 0).ToArray();
            if (linkTypes.Contains("alternate", StringComparer.Ordinal) && linkTypes.Contains("stylesheet", StringComparer.Ordinal) && string.IsNullOrEmpty(title))
            {
                Report.Message(MessageId.CSS_015, Location());
            }
        }
    }

    /// <inheritdoc />
    public override void StartPrefixMapping(string prefix, string uri)
    {
        base.StartPrefixMapping(prefix, uri);
        if (prefix == "epub" && uri != Namespaces.Ops)
        {
            Report.Message(MessageId.HTM_010, Location(), uri);
        }
    }

    private VocabContext VocabContext => new(Report, Context.MimeType, Context.Culture);

    // Splitter.onPattern("\\s+"): ASCII white space only.
    [GeneratedRegex(@"[ \t\n\x0B\f\r]+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
