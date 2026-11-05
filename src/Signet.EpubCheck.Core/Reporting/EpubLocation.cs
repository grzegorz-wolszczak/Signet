using System;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>
/// Where a message points: a path (relative to the container for a file inside the publication), a line and a
/// column (<c>-1</c> when unknown) and an optional piece of context (the offending text).
/// </summary>
public sealed record EpubLocation : IComparable<EpubLocation>
{
    /// <summary>Creates a location; a negative line or column is stored as <c>-1</c> (unknown).</summary>
    public EpubLocation(string path, int line = -1, int column = -1, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
        Line = line < 0 ? -1 : line;
        Column = column < 0 ? -1 : column;
        Context = context;
    }

    /// <summary>The path of the file the message is about.</summary>
    public string Path { get; }

    /// <summary>The line (1-based), or <c>-1</c> when unknown.</summary>
    public int Line { get; }

    /// <summary>The column (1-based), or <c>-1</c> when unknown.</summary>
    public int Column { get; }

    /// <summary>The context of the problem (typically the text around it), or <c>null</c>.</summary>
    public string? Context { get; }

    /// <summary>The same location at the given line and column.</summary>
    public EpubLocation At(int line, int column) => new(Path, line, column, Context);

    /// <summary>The same location with the given context (<c>null</c> clears it).</summary>
    public EpubLocation WithContext(object? context) => new(Path, Line, Column, context?.ToString());

    /// <summary>Orders by path, line, column and context (a missing context first), like upstream.</summary>
    public int CompareTo(EpubLocation? other)
    {
        if (other is null)
        {
            return 1;
        }

        int comparison = string.CompareOrdinal(Path, other.Path);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Line.CompareTo(other.Line);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Column.CompareTo(other.Column);
        return comparison != 0 ? comparison : string.CompareOrdinal(Context, other.Context);
    }

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/> (see <see cref="CompareTo"/>).</summary>
    public static bool operator <(EpubLocation? left, EpubLocation? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> sorts before or equal to <paramref name="right"/>.</summary>
    public static bool operator <=(EpubLocation? left, EpubLocation? right) => left is null || left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>.</summary>
    public static bool operator >(EpubLocation? left, EpubLocation? right) => left is not null && left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> sorts after or equal to <paramref name="right"/>.</summary>
    public static bool operator >=(EpubLocation? left, EpubLocation? right) => left is null ? right is null : left.CompareTo(right) >= 0;

    /// <summary>The form upstream prints: <c>path[line,column]</c>.</summary>
    public override string ToString() => $"{Path}[{Line},{Column}]";
}
