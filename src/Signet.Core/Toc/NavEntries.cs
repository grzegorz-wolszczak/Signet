using System.Collections.Generic;

namespace Signet.Core.Toc;

/// <summary>
/// A table of contents entry of the navigation document (a single <c>&lt;li&gt;&lt;a&gt;</c> in <c>nav[epub:type=toc]</c>).
/// A flat representation (<see cref="Level"/> from 1) and a hierarchical one
/// (<see cref="Children"/>) — <see cref="NavProcessor.MakeHierarchy"/> / <see cref="NavProcessor.Flatten"/>.
/// </summary>
public sealed class NavTocEntry
{
    /// <summary>The nesting level counted from 1 (used in the flat representation).</summary>
    public int Level { get; set; } = 1;

    /// <summary>The link text (decoded).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The <c>href</c> attribute — kept URL-encoded (may contain a fragment).</summary>
    public string Href { get; set; } = string.Empty;

    /// <summary>The child entries (only in the hierarchical representation).</summary>
    public IList<NavTocEntry> Children { get; } = new List<NavTocEntry>();
}

/// <summary>An entry of the <c>nav[epub:type=landmarks]</c> section (<c>&lt;a epub:type&gt;</c>).</summary>
public sealed class NavLandmarkEntry
{
    /// <summary>The semantic code from <c>epub:type</c> (e.g. <c>bodymatter</c>, <c>toc</c>).</summary>
    public string EpubType { get; set; } = string.Empty;

    /// <summary>The link text (decoded).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The <c>href</c> attribute — URL-encoded (may contain a fragment).</summary>
    public string Href { get; set; } = string.Empty;
}

/// <summary>An entry of the <c>nav[epub:type=page-list]</c> section.</summary>
public sealed class NavPageListEntry
{
    /// <summary>The page name / number (the link text, decoded).</summary>
    public string PageName { get; set; } = string.Empty;

    /// <summary>The <c>href</c> attribute — URL-encoded (may contain a fragment).</summary>
    public string Href { get; set; } = string.Empty;
}

/// <summary>
/// A landmark split into bookpath + fragment + code + title.
/// </summary>
public readonly record struct LandmarkInfo(string BookPath, string Fragment, string Code, string Title);
