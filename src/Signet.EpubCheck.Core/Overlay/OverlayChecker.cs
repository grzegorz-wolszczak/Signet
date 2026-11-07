using System;
using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Overlay;

/// <summary>Remembers which media overlay refers to each content document (upstream's <c>OverlayTextChecker</c>).</summary>
public sealed class OverlayTextChecker
{
    private readonly Dictionary<Url, string> _documentToOverlay = [];

    /// <summary>Records that the overlay <paramref name="overlayId"/> refers to <paramref name="contentDocUrl"/>; false when another overlay did.</summary>
    public bool RegisterOverlay(Url contentDocUrl, string overlayId)
    {
        ArgumentNullException.ThrowIfNull(contentDocUrl);
        ArgumentNullException.ThrowIfNull(overlayId);
        lock (_documentToOverlay)
        {
            if (_documentToOverlay.TryAdd(contentDocUrl, overlayId))
            {
                return true;
            }

            return overlayId == _documentToOverlay[contentDocUrl];
        }
    }

    /// <summary>Whether an overlay refers to the content document.</summary>
    public bool IsReferencedByOverlay(Url? contentDocUrl)
    {
        lock (_documentToOverlay)
        {
            return contentDocUrl is not null && _documentToOverlay.ContainsKey(contentDocUrl);
        }
    }

    /// <summary>Whether <paramref name="overlayId"/> is the overlay that refers to the content document.</summary>
    public bool IsCorrectOverlay(Url contentDocUrl, string overlayId)
    {
        lock (_documentToOverlay)
        {
            return _documentToOverlay.TryGetValue(contentDocUrl, out string? id) && overlayId == id;
        }
    }
}

/// <summary>
/// Checks a media overlay document (upstream's <c>OverlayChecker</c>): it is validated with <c>media-overlay-30.rnc</c> and
/// <c>.sch</c> and read by <see cref="OverlayHandler"/>.
/// </summary>
public sealed class OverlayChecker : PublicationResourceChecker
{
    private const string SmilMimeType = "application/smil+xml";

    /// <summary>A checker of the media overlay of <paramref name="context"/>.</summary>
    /// <exception cref="InvalidOperationException">The resource is not a media overlay by its media type (upstream's precondition; <c>CHK-008</c>).</exception>
    public OverlayChecker(ValidationContext context)
        : base(context)
    {
        if (context.MimeType != SmilMimeType)
        {
            throw new InvalidOperationException("The resource is not a media overlay document.");
        }
    }

    /// <inheritdoc />
    protected override bool CheckContent()
    {
        XmlParser parser = new(Context);
        parser.AddValidator(XmlValidators.Mo30Rnc.Get());
        parser.AddValidator(XmlValidators.Mo30Sch.Get());
        parser.AddContentHandler(new OverlayHandler(Context));
        parser.Process();
        return true;
    }
}

/// <summary>
/// Reads a media overlay (upstream's <c>OverlayHandler</c>): the content documents its <c>text</c> elements refer to (one overlay per
/// document, <c>MED-011</c>), its audio (the type must be a core one, <c>MED-005</c>; no fragment, <c>MED-014</c>), the order of the clips
/// (<c>MED-008</c>, <c>MED-009</c>), the semantics of <c>epub:type</c> and the <c>remote-resources</c> property.
/// </summary>
public sealed class OverlayHandler : XmlHandler
{
    private static readonly IReadOnlyDictionary<string, IVocab> s_reservedVocabs = new Dictionary<string, IVocab>
    {
        [string.Empty] = AggregateVocab.Of(StructureVocab.Vocab, StructureVocab.UncheckedVocab),
    };

    private static readonly IReadOnlyDictionary<string, IVocab> s_knownVocabUris = new Dictionary<string, IVocab>();
    private static readonly IReadOnlySet<string> s_defaultVocabUris = new HashSet<string>(StringComparer.Ordinal) { StructureVocab.Uri };

    private readonly SortedSet<PackageVocabs.ItemProperty> _requiredProperties = [];
    private IReadOnlyDictionary<string, IVocab> _vocabs = s_reservedVocabs;

    /// <summary>A handler of the media overlay of <paramref name="context"/>.</summary>
    public OverlayHandler(ValidationContext context)
        : base(context)
    {
    }

    private VocabContext VocabContext => new(Report, Context.MimeType, Context.Culture);

    /// <inheritdoc />
    protected override void StartElement()
    {
        XMLElement e = CurrentElement;
        ProcessGlobalAttributes();
        switch (e.Name)
        {
            case "smil":
                _vocabs = VocabUtil.ParsePrefixDeclaration(
                    e.GetAttributeNS(Namespaces.Ops, "prefix"), s_reservedVocabs, s_knownVocabUris, s_defaultVocabUris, Report, Location());
                break;
            case "body" or "seq":
                ProcessContentDocumentLink(CheckUrl(e.GetAttributeNS(Namespaces.Ops, "textref")));
                break;
            case "text":
                ProcessContentDocumentLink(CheckUrl(e.GetAttribute("src")));
                break;
            case "audio":
                ProcessAudioSrc();
                CheckTime(e.GetAttribute("clipBegin"), e.GetAttribute("clipEnd"));
                break;
        }
    }

    /// <inheritdoc />
    protected override void EndElement()
    {
        if (CurrentElement.Name == "smil")
        {
            CheckProperties();
        }
    }

    private void CheckTime(string? clipBegin, string? clipEnd)
    {
        if (clipEnd is null)
        {
            return;
        }

        SmilClock start;
        SmilClock end;
        try
        {
            start = new SmilClock(clipBegin ?? "0");
            end = new SmilClock(clipEnd);
        }
        catch (FormatException)
        {
            return; // reported by the schema
        }

        if (start.CompareTo(end) == 1)
        {
            Report.Message(MessageId.MED_008, Location());
        }
        else if (start.Equals(end))
        {
            Report.Message(MessageId.MED_009, Location());
        }
    }

    private void ProcessGlobalAttributes()
    {
        IReadOnlyList<VocabProperty> properties = VocabUtil.ParsePropertyList(
            CurrentElement.GetAttributeNS(Namespaces.Ops, "type"), _vocabs, VocabContext, Location());
        foreach (VocabProperty property in properties)
        {
            if (property.VocabUri == StructureVocab.Uri && property.Value is null)
            {
                Report.Message(MessageId.OPF_088, Location(), property.Name);
            }
        }
    }

    private void ProcessAudioSrc()
    {
        Url? url = CheckUrl(CurrentElement.GetAttribute("src"));

        // Upstream asks for the fragment of a URL that may not have parsed.
        if (url?.Fragment is { } fragment)
        {
            Report.Message(MessageId.MED_014, Location(), fragment);
            url = EpubCheckUrlUtils.DocUrl(url);
        }

        if (url is not null && Context.Container is not null)
        {
            string? mimeType = Context.ResourceRegistry!.GetMimeType(url);
            if (mimeType is not null && !OpfMediaTypes.IsBlessedAudioType(mimeType))
            {
                Report.Message(MessageId.MED_005, Location(), Context.Relativize(url), mimeType);
            }

            RegisterReference(url, ReferenceType.Audio, hasIntrinsicFallback: true);
            if (Context.IsRemote(url))
            {
                _requiredProperties.Add(PackageVocabs.ItemProperty.RemoteResources);
            }
        }
    }

    private void ProcessContentDocumentLink(Url? url)
    {
        if (url is null || Context.Container is null || Context.OverlayTextChecker is not { } checker || Context.OpfItem is not { } item)
        {
            return;
        }

        Url documentUrl = EpubCheckUrlUtils.DocUrl(url);
        if (!checker.RegisterOverlay(documentUrl, item.Id))
        {
            Report.Message(MessageId.MED_011, Location(), Context.Relativize(url));
        }

        RegisterReference(url, ReferenceType.OverlayTextLink);
    }

    private void CheckProperties()
    {
        // single file validation
        if (Context.Container is null)
        {
            return;
        }

        SortedSet<PackageVocabs.ItemProperty> itemProps = [.. VocabProperty.Filter<PackageVocabs.ItemProperty>(Context.Properties)];
        foreach (PackageVocabs.ItemProperty required in _requiredProperties.Where(property => !itemProps.Contains(property)))
        {
            Report.Message(MessageId.OPF_014, Context.Location(), PackageVocabs.ItemVocab.GetName(required));
        }
    }
}
