using System.Collections.Generic;

namespace Signet.Core.Toc;

/// <summary>
/// An editable table of contents entry used by the "Edit Table Of Contents" dialog —
/// a hierarchical representation shared by nav (EPUB 3)
/// and NCX (EPUB 2). <see cref="Target"/> is stored as a URL-encoded bookpath
/// (absolute with respect to the OCF directory) with an optional <c>#fragment</c> — so targets from nav and NCX
/// have the same form.
/// </summary>
public sealed class TocEntry
{
    /// <summary>The entry text (decoded).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The URL-encoded bookpath of the target (+ an optional <c>#fragment</c>); empty for an entry without a link.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Whether this is the artificial root of the tree (it corresponds to no entry).</summary>
    public bool IsRoot { get; set; }

    /// <summary>The child entries.</summary>
    public IList<TocEntry> Children { get; } = new List<TocEntry>();
}
