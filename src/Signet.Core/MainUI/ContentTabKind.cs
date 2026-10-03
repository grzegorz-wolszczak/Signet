using System;
using Signet.Core.Resources;

namespace Signet.Core.MainUI;

/// <summary>
/// The kind of tab a given resource opens in; each resource type gets its own view class.
/// </summary>
public enum ContentTabKind
{
    /// <summary>An (X)HTML document (Code View + Preview).</summary>
    Flow,

    /// <summary>A CSS stylesheet.</summary>
    Css,

    /// <summary>An editable text resource (JS, JSON).</summary>
    MiscText,

    /// <summary>An SVG image (editable as text + a preview).</summary>
    Svg,

    /// <summary>An OPF document.</summary>
    Opf,

    /// <summary>An NCX table of contents.</summary>
    Ncx,

    /// <summary>Any other XML.</summary>
    Xml,

    /// <summary>A plain text resource without highlighting.</summary>
    Text,

    /// <summary>A raster image (preview, no editing).</summary>
    Image,

    /// <summary>A font (a typeface preview, no editing).</summary>
    Font,

    /// <summary>Audio or video.</summary>
    AudioVideo,

    /// <summary>A PDF document.</summary>
    Pdf,

    /// <summary>A resource that cannot be opened in any tab.</summary>
    Unsupported,
}

/// <summary>
/// Maps <see cref="ResourceType"/> to <see cref="ContentTabKind"/> — the choice of tab
/// class for a resource.
/// </summary>
public static class ContentTabKindMap
{
    /// <summary>The tab kind for the given resource.</summary>
    public static ContentTabKind ForResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return ForType(resource.Type);
    }

    /// <summary>The tab kind for the given resource type.</summary>
    public static ContentTabKind ForType(ResourceType type) => type switch
    {
        ResourceType.Html => ContentTabKind.Flow,
        ResourceType.Css => ContentTabKind.Css,
        ResourceType.Image => ContentTabKind.Image,
        ResourceType.MiscText => ContentTabKind.MiscText,
        ResourceType.Svg => ContentTabKind.Svg,
        ResourceType.Opf => ContentTabKind.Opf,
        ResourceType.Ncx => ContentTabKind.Ncx,
        ResourceType.Xml => ContentTabKind.Xml,
        ResourceType.Text => ContentTabKind.Text,
        ResourceType.Audio => ContentTabKind.AudioVideo,
        ResourceType.Video => ContentTabKind.AudioVideo,
        ResourceType.Pdf => ContentTabKind.Pdf,
        ResourceType.Font => ContentTabKind.Font,
        _ => ContentTabKind.Unsupported,
    };

    /// <summary>
    /// Whether a tab of the given kind edits the resource's text content (so on close /
    /// switch its content must be flushed to the resource).
    /// </summary>
    public static bool IsEditable(ContentTabKind kind) => kind switch
    {
        ContentTabKind.Flow => true,
        ContentTabKind.Css => true,
        ContentTabKind.MiscText => true,
        ContentTabKind.Svg => true,
        ContentTabKind.Opf => true,
        ContentTabKind.Ncx => true,
        ContentTabKind.Xml => true,
        ContentTabKind.Text => true,
        _ => false,
    };
}
