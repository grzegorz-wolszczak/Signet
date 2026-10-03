using System;
using System.Collections.Generic;
using System.Linq;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Signet.Core.Diff;

/// <summary>Kind of a line in a diff.</summary>
public enum DiffLineKind
{
    /// <summary>Unchanged line.</summary>
    Unchanged,

    /// <summary>Deleted line (present only on the left).</summary>
    Deleted,

    /// <summary>Inserted line (present only on the right).</summary>
    Inserted,

    /// <summary>Modified line — intra-line differences are in <see cref="DiffCell.Spans"/>.</summary>
    Modified,

    /// <summary>Empty padding cell (no counterpart on this side).</summary>
    Empty,
}

/// <summary>A fragment of a line's text; <see cref="Changed"/> — the fragment differs from the other side.</summary>
/// <param name="Text">Fragment text.</param>
/// <param name="Changed">Whether the fragment is changed (intra-line difference).</param>
public sealed record DiffSpan(string Text, bool Changed);

/// <summary>One side of a side-by-side diff row.</summary>
/// <param name="LineNumber">Line number in the file (1-based), or <c>null</c> for an empty cell.</param>
/// <param name="Kind">Line kind.</param>
/// <param name="Spans">Text fragments (with word differences for <see cref="DiffLineKind.Modified"/>).</param>
public sealed record DiffCell(int? LineNumber, DiffLineKind Kind, IReadOnlyList<DiffSpan> Spans)
{
    /// <summary>Full line text.</summary>
    public string Text => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>A row of the side-by-side view, or a collapsed block of unchanged lines.</summary>
/// <param name="Left">Left side (checkpoint state).</param>
/// <param name="Right">Right side (current state).</param>
/// <param name="CollapsedLines">Number of hidden unchanged lines when the row is a collapsed block; otherwise <c>null</c>.</param>
public sealed record SideBySideDiffRow(DiffCell Left, DiffCell Right, int? CollapsedLines)
{
    /// <summary>Whether the row is a change (either side differs from "unchanged").</summary>
    public bool IsChange => CollapsedLines is null && (Left.Kind != DiffLineKind.Unchanged || Right.Kind != DiffLineKind.Unchanged);
}

/// <summary>A row of the single-column (unified) view, or a collapsed block of unchanged lines.</summary>
/// <param name="LeftLineNumber">Line number on the left, or <c>null</c>.</param>
/// <param name="RightLineNumber">Line number on the right, or <c>null</c>.</param>
/// <param name="Kind"><see cref="DiffLineKind.Unchanged"/>, <see cref="DiffLineKind.Deleted"/> or <see cref="DiffLineKind.Inserted"/>.</param>
/// <param name="Spans">Text fragments with intra-line differences marked.</param>
/// <param name="CollapsedLines">Number of hidden unchanged lines for a collapsed block; otherwise <c>null</c>.</param>
public sealed record UnifiedDiffRow(
    int? LeftLineNumber, int? RightLineNumber, DiffLineKind Kind, IReadOnlyList<DiffSpan> Spans, int? CollapsedLines)
{
    /// <summary>Full line text.</summary>
    public string Text => string.Concat(Spans.Select(s => s.Text));

    /// <summary>Whether the row is a change.</summary>
    public bool IsChange => CollapsedLines is null && Kind != DiffLineKind.Unchanged;
}

/// <summary>
/// Text diff of two versions of a file (engine: DiffPlex) with side-by-side and single-column views,
/// context around changes and intra-line differences.
/// </summary>
public sealed class TextDiff
{
    private readonly IReadOnlyList<SideBySideDiffRow> _rows;

    private TextDiff(IReadOnlyList<SideBySideDiffRow> rows)
    {
        _rows = rows;
    }

    /// <summary>Whether the texts differ.</summary>
    public bool HasChanges => _rows.Any(r => r.IsChange);

    /// <summary>Computes a line-by-line diff (whitespace and case are significant).</summary>
    public static TextDiff Compute(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        SideBySideDiffModel model = SideBySideDiffBuilder.Diff(left, right, ignoreWhiteSpace: false, ignoreCase: false);
        List<SideBySideDiffRow> rows = new(model.OldText.Lines.Count);
        for (int i = 0; i < model.OldText.Lines.Count; i++)
        {
            rows.Add(new SideBySideDiffRow(ToCell(model.OldText.Lines[i]), ToCell(model.NewText.Lines[i]), null));
        }

        return new TextDiff(rows);
    }

    /// <summary>
    /// Rows of the side-by-side view. <paramref name="context"/> — number of unchanged lines around each
    /// change (the rest is collapsed into blocks); <c>null</c> — the whole text.
    /// </summary>
    public IReadOnlyList<SideBySideDiffRow> SideBySide(int? context)
    {
        List<SideBySideDiffRow> result = new();
        foreach (Segment segment in Segments(context))
        {
            if (segment.CollapsedCount is { } hidden)
            {
                result.Add(new SideBySideDiffRow(EmptyCell, EmptyCell, hidden));
            }
            else
            {
                result.AddRange(_rows.Skip(segment.Start).Take(segment.Count));
            }
        }

        return result;
    }

    /// <summary>
    /// Rows of the single-column view: in each change block, deleted (left) lines come first, then
    /// inserted (right) lines. <paramref name="context"/> as in <see cref="SideBySide"/>.
    /// </summary>
    public IReadOnlyList<UnifiedDiffRow> Unified(int? context)
    {
        List<UnifiedDiffRow> result = new();
        foreach (Segment segment in Segments(context))
        {
            if (segment.CollapsedCount is { } hidden)
            {
                result.Add(new UnifiedDiffRow(null, null, DiffLineKind.Unchanged, Array.Empty<DiffSpan>(), hidden));
                continue;
            }

            List<UnifiedDiffRow> inserted = new();
            foreach (SideBySideDiffRow row in _rows.Skip(segment.Start).Take(segment.Count))
            {
                if (!row.IsChange)
                {
                    FlushInserted(result, inserted);
                    result.Add(new UnifiedDiffRow(
                        row.Left.LineNumber, row.Right.LineNumber, DiffLineKind.Unchanged, row.Left.Spans, null));
                    continue;
                }

                if (row.Left.Kind is DiffLineKind.Deleted or DiffLineKind.Modified)
                {
                    result.Add(new UnifiedDiffRow(row.Left.LineNumber, null, DiffLineKind.Deleted, row.Left.Spans, null));
                }

                if (row.Right.Kind is DiffLineKind.Inserted or DiffLineKind.Modified)
                {
                    inserted.Add(new UnifiedDiffRow(null, row.Right.LineNumber, DiffLineKind.Inserted, row.Right.Spans, null));
                }
            }

            FlushInserted(result, inserted);
        }

        return result;
    }

    private static readonly DiffCell EmptyCell = new(null, DiffLineKind.Empty, Array.Empty<DiffSpan>());

    private static void FlushInserted(List<UnifiedDiffRow> result, List<UnifiedDiffRow> inserted)
    {
        result.AddRange(inserted);
        inserted.Clear();
    }

    // Splits rows into visible ranges and collapsed blocks of unchanged lines.
    private IEnumerable<Segment> Segments(int? context)
    {
        if (context is null)
        {
            yield return new Segment(0, _rows.Count, null);
            yield break;
        }

        int around = Math.Max(0, context.Value);
        bool[] visible = new bool[_rows.Count];
        for (int i = 0; i < _rows.Count; i++)
        {
            if (!_rows[i].IsChange)
            {
                continue;
            }

            for (int j = Math.Max(0, i - around); j <= Math.Min(_rows.Count - 1, i + around); j++)
            {
                visible[j] = true;
            }
        }

        int start = 0;
        while (start < _rows.Count)
        {
            bool isVisible = visible[start];
            int end = start;
            while (end < _rows.Count && visible[end] == isVisible)
            {
                end++;
            }

            yield return isVisible
                ? new Segment(start, end - start, null)
                : new Segment(start, end - start, end - start);
            start = end;
        }
    }

    private static DiffCell ToCell(DiffPiece piece)
    {
        DiffLineKind kind = piece.Type switch
        {
            ChangeType.Deleted => DiffLineKind.Deleted,
            ChangeType.Inserted => DiffLineKind.Inserted,
            ChangeType.Modified => DiffLineKind.Modified,
            ChangeType.Imaginary => DiffLineKind.Empty,
            _ => DiffLineKind.Unchanged,
        };

        if (kind == DiffLineKind.Empty)
        {
            return EmptyCell;
        }

        IReadOnlyList<DiffSpan> spans = kind == DiffLineKind.Modified && piece.SubPieces.Count > 0
            ? MergeSpans(piece.SubPieces
                .Where(p => p.Type != ChangeType.Imaginary && !string.IsNullOrEmpty(p.Text))
                .Select(p => new DiffSpan(p.Text!, p.Type != ChangeType.Unchanged)))
            : new[] { new DiffSpan(piece.Text ?? string.Empty, false) };

        return new DiffCell(piece.Position, kind, spans);
    }

    // Adjacent fragments of the same kind are merged into one (fewer elements to draw).
    private static List<DiffSpan> MergeSpans(IEnumerable<DiffSpan> spans)
    {
        List<DiffSpan> merged = new();
        foreach (DiffSpan span in spans)
        {
            if (merged.Count > 0 && merged[^1].Changed == span.Changed)
            {
                merged[^1] = new DiffSpan(merged[^1].Text + span.Text, span.Changed);
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }

    private readonly record struct Segment(int Start, int Count, int? CollapsedCount);
}
