namespace Signet.Core.Search;

/// <summary>
/// "Marked text" — a persistent text region, independent of the current selection, to which
/// search/replace can be restricted (set by the "Mark Selected Text" action).
/// </summary>
public sealed class MarkedTextRegion
{
    /// <summary>Start of the region (code units), or <c>-1</c> when none.</summary>
    public int Start { get; internal set; } = -1;

    /// <summary>End of the region (code units), or <c>-1</c> when none.</summary>
    public int End { get; internal set; } = -1;

    /// <summary>Whether the region is set (start &gt;= 0 and end &gt; 0).</summary>
    public bool IsMarked => Start >= 0 && End > 0;

    internal void Set(int start, int end)
    {
        Start = start;
        End = end;
    }

    internal void Clear()
    {
        Start = -1;
        End = -1;
    }

    // Keeps the end of the region within the text bounds.
    internal void ClampTo(int textLength)
    {
        if (IsMarked && End > textLength)
        {
            End = textLength;
        }
    }
}
