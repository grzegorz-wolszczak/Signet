using System;
using System.Collections.Generic;
using Signet.Core.Resources;

namespace Signet.Core.MainUI;

/// <summary>
/// A file group in the <see cref="OpfModel"/> tree — the "Text", "Styles",
/// "Images", "Fonts", "Audio", "Video" and "Misc" folders.
/// </summary>
public enum OpfModelGroupKind
{
    /// <summary>(X)HTML documents — sorted by spine order.</summary>
    Text,

    /// <summary>CSS stylesheets.</summary>
    Styles,

    /// <summary>Raster images and SVG.</summary>
    Images,

    /// <summary>Fonts.</summary>
    Fonts,

    /// <summary>Audio files.</summary>
    Audio,

    /// <summary>Video files.</summary>
    Video,

    /// <summary>The remaining resources (JS, JSON, XML, PDF, unknown).</summary>
    Misc,
}

/// <summary>
/// A single entry (file) in the <see cref="OpfModel"/> tree. Carries the resource and the presentation flags
/// computed in advance (cover, navigation document, XML validity, semantics,
/// manifest properties), with no dependency on a view class.
/// </summary>
public sealed class OpfModelEntry
{
    internal OpfModelEntry(Resource resource, string displayName)
    {
        Resource = resource;
        DisplayName = displayName;
        SortKey = displayName;
    }

    /// <summary>The represented resource.</summary>
    public Resource Resource { get; }

    /// <summary>The resource identifier in <see cref="BookManipulation.FolderKeeper"/>.</summary>
    public string Identifier => Resource.Identifier;

    /// <summary>The resource's bookpath.</summary>
    public string BookPath => Resource.BookPath;

    /// <summary>The text shown in the tree (the short name or the full path — depending on settings).</summary>
    public string DisplayName { get; }

    /// <summary>The resource kind.</summary>
    public ResourceType ResourceType => Resource.Type;

    /// <summary>The position in the spine (0-based) or <c>-1</c> when the file is not in the spine. Applies to (X)HTML only.</summary>
    public int ReadingOrder { get; internal set; } = -1;

    /// <summary>Whether this is the cover image (meta <c>cover</c> / guide <c>cover</c>).</summary>
    public bool IsCover { get; internal set; }

    /// <summary>Whether this is the EPUB 3 navigation document (<c>properties="nav"</c>).</summary>
    public bool IsNav { get; internal set; }

    /// <summary>
    /// The XML validity of an (X)HTML file: <c>true</c>/<c>false</c>, or <c>null</c> when it does not apply
    /// (the resource is not (X)HTML) or the check failed.
    /// </summary>
    public bool? IsWellFormed { get; internal set; }

    /// <summary>The semantic names assigned to the file (EPUB 3 landmarks / EPUB 2 guide).</summary>
    public IReadOnlyList<string> SemanticTypes { get; internal set; } = Array.Empty<string>();

    /// <summary>The value of the manifest entry's <c>properties</c> attribute (EPUB 3) or an empty string.</summary>
    public string ManifestProperties { get; internal set; } = string.Empty;

    /// <summary>The ready tooltip text — path + font description + semantics + properties.</summary>
    public string ToolTip { get; internal set; } = string.Empty;

    /// <summary>The alphanumeric sort key (the name without the extension) — for the "Sort" actions.</summary>
    internal string SortKey { get; set; }
}

/// <summary>A file group (folder) in the <see cref="OpfModel"/> tree.</summary>
public sealed class OpfModelFolder
{
    internal OpfModelFolder(OpfModelGroupKind kind, string name, string defaultFolder, IReadOnlyList<OpfModelEntry> entries)
    {
        Kind = kind;
        Name = name;
        DefaultFolder = defaultFolder;
        Entries = entries;
    }

    /// <summary>The group kind.</summary>
    public OpfModelGroupKind Kind { get; }

    /// <summary>The folder name displayed in the tree (<c>Text</c>, <c>Styles</c>, …).</summary>
    public string Name { get; }

    /// <summary>
    /// The group's default folder in the book (e.g. <c>OEBPS/Text</c>) — used as a folder tooltip.
    /// </summary>
    public string DefaultFolder { get; }

    /// <summary>The files in the group in display order (may be empty).</summary>
    public IReadOnlyList<OpfModelEntry> Entries { get; }
}
