using System.Collections.Generic;

namespace Signet.Core.Toc;

/// <summary>
/// A <c>&lt;meta&gt;</c> entry from the <c>&lt;head&gt;</c> section of an NCX (e.g. <c>dtb:uid</c>, <c>dtb:depth</c>).
/// </summary>
public sealed record NcxMeta
{
    /// <summary>Creates a meta entry.</summary>
    public NcxMeta(string name, string content, string scheme = "")
    {
        Name = name;
        Content = content;
        Scheme = scheme;
    }

    /// <summary>The <c>name</c> attribute.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The <c>content</c> attribute (decoded from XML entities).</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The <c>scheme</c> attribute (empty if absent).</summary>
    public string Scheme { get; set; } = string.Empty;
}

/// <summary>
/// A node of the <c>&lt;navMap&gt;</c> tree (a <c>&lt;navPoint&gt;</c> element). <see cref="PlayOrder"/>
/// is recalculated on serialization (<see cref="NcxDocument.ToXml"/>), so a manually set
/// value only serves for reading the state after parsing.
/// </summary>
public sealed class NcxNavPoint
{
    /// <summary>The <c>id</c> attribute (empty =&gt; generated as <c>navPoint-{playOrder}</c> on write).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The <c>class</c> attribute (empty if absent).</summary>
    public string Class { get; set; } = string.Empty;

    /// <summary>The play order (recalculated from the tree order on write).</summary>
    public int PlayOrder { get; set; }

    /// <summary>The text from <c>&lt;navLabel&gt;&lt;text&gt;</c> (decoded, with whitespace collapsed).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The <c>src</c> attribute from <c>&lt;content&gt;</c> (decoded from XML entities).</summary>
    public string ContentSrc { get; set; } = string.Empty;

    /// <summary>Nested <c>&lt;navPoint&gt;</c> elements.</summary>
    public IList<NcxNavPoint> Children { get; } = new List<NcxNavPoint>();
}

/// <summary>A <c>&lt;pageTarget&gt;</c> entry from <c>&lt;pageList&gt;</c>.</summary>
public sealed class NcxPageTarget
{
    /// <summary>The <c>id</c> attribute.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The <c>type</c> attribute (<c>normal</c> / <c>front</c> / <c>special</c>).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The <c>value</c> attribute (the page number).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The <c>class</c> attribute (empty if absent).</summary>
    public string Class { get; set; } = string.Empty;

    /// <summary>The play order (recalculated on write).</summary>
    public int PlayOrder { get; set; }

    /// <summary>The text from <c>&lt;navLabel&gt;&lt;text&gt;</c>.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The <c>src</c> attribute from <c>&lt;content&gt;</c>.</summary>
    public string ContentSrc { get; set; } = string.Empty;
}

/// <summary>A <c>&lt;navList&gt;</c> entry (e.g. a list of illustrations) with its own <c>&lt;navTarget&gt;</c> elements.</summary>
public sealed class NcxNavList
{
    /// <summary>The <c>id</c> attribute.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The <c>class</c> attribute (empty if absent).</summary>
    public string Class { get; set; } = string.Empty;

    /// <summary>The text from the <c>&lt;navLabel&gt;&lt;text&gt;</c> of the <c>&lt;navList&gt;</c> itself.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The <c>&lt;navTarget&gt;</c> entries.</summary>
    public IList<NcxNavTarget> Targets { get; } = new List<NcxNavTarget>();
}

/// <summary>A <c>&lt;navTarget&gt;</c> entry from <c>&lt;navList&gt;</c>.</summary>
public sealed class NcxNavTarget
{
    /// <summary>The <c>id</c> attribute.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The <c>class</c> attribute (empty if absent).</summary>
    public string Class { get; set; } = string.Empty;

    /// <summary>The <c>value</c> attribute (empty if absent).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The play order (recalculated on write).</summary>
    public int PlayOrder { get; set; }

    /// <summary>The text from <c>&lt;navLabel&gt;&lt;text&gt;</c>.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The <c>src</c> attribute from <c>&lt;content&gt;</c>.</summary>
    public string ContentSrc { get; set; } = string.Empty;
}
