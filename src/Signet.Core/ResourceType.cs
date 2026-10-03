using System;

namespace Signet.Core;

/// <summary>
/// The kind of a resource in an EPUB book. The values are bit flags,
/// which makes it easy to filter sets of resources
/// (e.g. "all textual" = <see cref="Html"/> | <see cref="Text"/>).
/// </summary>
[Flags]
public enum ResourceType
{
    /// <summary>No match / a miscellaneous resource.</summary>
    Generic = 1 << 0,

    /// <summary>A text resource, but <em>not</em> (X)HTML.</summary>
    Text = 1 << 1,

    /// <summary>Any XML resource that is not (X)HTML.</summary>
    Xml = 1 << 2,

    /// <summary>Plain (X)HTML.</summary>
    Html = 1 << 3,

    /// <summary>A CSS stylesheet.</summary>
    Css = 1 << 4,

    /// <summary>A raster image of any type.</summary>
    Image = 1 << 5,

    /// <summary>An SVG image.</summary>
    Svg = 1 << 6,

    /// <summary>A font (TTF/OTF/WOFF/...).</summary>
    Font = 1 << 7,

    /// <summary>An OPF document.</summary>
    Opf = 1 << 8,

    /// <summary>An NCX table of contents.</summary>
    Ncx = 1 << 9,

    /// <summary>An editable text resource: JS, JSON, etc.</summary>
    MiscText = 1 << 10,

    /// <summary>An audio resource.</summary>
    Audio = 1 << 11,

    /// <summary>A video resource.</summary>
    Video = 1 << 12,

    /// <summary>A PDF document.</summary>
    Pdf = 1 << 13,
}
