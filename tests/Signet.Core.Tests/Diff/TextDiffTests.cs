using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Diff;
using Xunit;

namespace Signet.Core.Tests.Diff;

/// <summary>Tests of <see cref="TextDiff"/> — a line-by-line diff for the "Compare" window.</summary>
public sealed class TextDiffTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private static string[] Numbered(int count) => Enumerable.Range(1, count).Select(i => $"line {i}").ToArray();

    [Fact]
    public void Identical_texts_have_no_changes()
    {
        string text = Lines("a", "b", "c");

        TextDiff diff = TextDiff.Compute(text, text);

        diff.HasChanges.Should().BeFalse();
        diff.SideBySide(null).Should().OnlyContain(r => !r.IsChange);
    }

    [Fact]
    public void Side_by_side_aligns_an_inserted_line_with_an_empty_cell()
    {
        TextDiff diff = TextDiff.Compute(Lines("a", "c"), Lines("a", "b", "c"));

        IReadOnlyList<SideBySideDiffRow> rows = diff.SideBySide(null);

        rows.Should().HaveCount(3);
        rows[1].Left.Kind.Should().Be(DiffLineKind.Empty);
        rows[1].Right.Kind.Should().Be(DiffLineKind.Inserted);
        rows[1].Right.Text.Should().Be("b");
        rows[1].Right.LineNumber.Should().Be(2);
        rows[2].Left.LineNumber.Should().Be(2);
        rows[2].Right.LineNumber.Should().Be(3);
    }

    [Fact]
    public void Modified_line_marks_only_the_changed_words()
    {
        TextDiff diff = TextDiff.Compute(Lines("<p>Ala ma kota</p>"), Lines("<p>Ala ma psa</p>"));

        SideBySideDiffRow row = diff.SideBySide(null).Single();

        row.Left.Kind.Should().Be(DiffLineKind.Modified);
        row.Right.Kind.Should().Be(DiffLineKind.Modified);
        row.Left.Spans.Where(s => s.Changed).Select(s => s.Text).Should().ContainSingle(t => t.Contains("kota"));
        row.Right.Spans.Where(s => s.Changed).Select(s => s.Text).Should().ContainSingle(t => t.Contains("psa"));
        row.Right.Spans.Where(s => !s.Changed).Select(s => s.Text).Should().Contain(t => t.Contains("Ala"));
        row.Right.Text.Should().Be("<p>Ala ma psa</p>");
    }

    [Fact]
    public void Context_collapses_unchanged_lines_far_from_changes()
    {
        string[] left = Numbered(20);
        string[] right = left.ToArray();
        right[9] = "changed";

        IReadOnlyList<SideBySideDiffRow> rows = TextDiff.Compute(Lines(left), Lines(right)).SideBySide(context: 3);

        rows.First().CollapsedLines.Should().Be(6, "lines 1–6 are farther than 3 from the change on line 10");
        rows.Last().CollapsedLines.Should().Be(7, "lines 14–20 are collapsed");
        rows.Where(r => r.CollapsedLines is null).Should().HaveCount(7, "the change + 3 context lines on each side");
    }

    [Fact]
    public void No_context_shows_all_lines()
    {
        string[] left = Numbered(20);
        string[] right = left.ToArray();
        right[9] = "changed";

        TextDiff.Compute(Lines(left), Lines(right)).SideBySide(context: null)
            .Should().HaveCount(20).And.OnlyContain(r => r.CollapsedLines == null);
    }

    [Fact]
    public void Unified_lists_deleted_lines_before_inserted_ones_in_a_change_block()
    {
        TextDiff diff = TextDiff.Compute(Lines("a", "b1", "b2", "c"), Lines("a", "x1", "x2", "c"));

        IReadOnlyList<UnifiedDiffRow> rows = diff.Unified(null);

        rows.Select(r => (r.Kind, r.Text)).Should().Equal(
            (DiffLineKind.Unchanged, "a"),
            (DiffLineKind.Deleted, "b1"),
            (DiffLineKind.Deleted, "b2"),
            (DiffLineKind.Inserted, "x1"),
            (DiffLineKind.Inserted, "x2"),
            (DiffLineKind.Unchanged, "c"));
        rows[1].LeftLineNumber.Should().Be(2);
        rows[3].RightLineNumber.Should().Be(2);
        rows[5].LeftLineNumber.Should().Be(4);
        rows[5].RightLineNumber.Should().Be(4);
    }

    [Fact]
    public void Unified_respects_the_context()
    {
        string[] left = Numbered(20);
        string[] right = left.ToArray();
        right[9] = "changed";

        IReadOnlyList<UnifiedDiffRow> rows = TextDiff.Compute(Lines(left), Lines(right)).Unified(context: 1);

        rows.First().CollapsedLines.Should().Be(8);
        rows.Where(r => r.IsChange).Select(r => r.Kind).Should().Equal(DiffLineKind.Deleted, DiffLineKind.Inserted);
        rows.Last().CollapsedLines.Should().Be(9);
    }
}
