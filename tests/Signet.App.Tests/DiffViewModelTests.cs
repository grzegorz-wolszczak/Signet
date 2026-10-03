using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Diff;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>View model of the Compare window.</summary>
public sealed class DiffViewModelTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private static string[] Numbered(int count) => Enumerable.Range(1, count).Select(i => $"line {i}").ToArray();

    private static BookFileDiff TextFile(TempDir temp, string left, string right, string bookPath = "EPUB/text/ch.xhtml")
    {
        string leftPath = temp.Combine("left.xhtml");
        string rightPath = temp.Combine("right.xhtml");
        File.WriteAllText(leftPath, left);
        File.WriteAllText(rightPath, right);
        return new BookFileDiff(BookFileChange.Modified, bookPath, bookPath, leftPath, rightPath, BookFileContentKind.Text);
    }

    private static DiffViewModel New(params BookFileDiff[] files) => new(files, "Start", "Current");

    private static string[] TwoChanges()
    {
        string[] right = Numbered(40);
        right[4] = "first change";
        right[29] = "second change";
        return right;
    }

    [Fact]
    public void Without_differences_shows_the_no_changes_message()
    {
        DiffViewModel vm = New();

        vm.IsMessageVisible.Should().BeTrue();
        vm.Message.Should().Be(Strings.Get("Diff_NoChanges"));
    }

    [Fact]
    public void Selects_the_first_file_and_jumps_to_its_first_change()
    {
        using TempDir temp = new();
        DiffViewModel vm = New(TextFile(temp, Lines(Numbered(40)), Lines(TwoChanges())));

        vm.SelectedFile.Should().Be(vm.Files[0]);
        vm.Files[0].Label.Should().Contain("EPUB/text/ch.xhtml");
        vm.IsSideBySideTextVisible.Should().BeTrue();
        vm.SideBySideRows[vm.SelectedRowIndex].Row.IsChange.Should().BeTrue();
        vm.SideBySideRows[vm.SelectedRowIndex].Row.Right.Text.Should().Be("first change");
    }

    [Fact]
    public void Next_and_previous_change_move_between_change_blocks()
    {
        using TempDir temp = new();
        DiffViewModel vm = New(TextFile(temp, Lines(Numbered(40)), Lines(TwoChanges())));

        vm.NextChangeCommand.Execute(null);
        vm.SideBySideRows[vm.SelectedRowIndex].Row.Right.Text.Should().Be("second change");

        vm.NextChangeCommand.Execute(null);
        vm.Status.Should().Be(Strings.Get("Diff_NoNextChange"));

        vm.PreviousChangeCommand.Execute(null);
        vm.SideBySideRows[vm.SelectedRowIndex].Row.Right.Text.Should().Be("first change");
    }

    [Fact]
    public void Default_context_is_three_lines_and_all_text_shows_every_line()
    {
        using TempDir temp = new();
        DiffViewModel vm = New(TextFile(temp, Lines(Numbered(40)), Lines(TwoChanges())));

        vm.ContextOptions[vm.ContextIndex].Should().Be(Strings.Format("Diff_ContextLines", 3));
        vm.SideBySideRows.Should().Contain(r => r.IsCollapsed);

        vm.ContextIndex = vm.ContextOptions.Count - 1;

        vm.SideBySideRows.Should().HaveCount(40).And.OnlyContain(r => r.IsLine);
    }

    [Fact]
    public void Unified_view_shows_deleted_and_inserted_lines()
    {
        using TempDir temp = new();
        DiffViewModel vm = New(TextFile(temp, Lines("a", "old", "c"), Lines("a", "new", "c")));

        vm.ViewIndex = 1;

        vm.IsUnified.Should().BeTrue();
        vm.IsUnifiedTextVisible.Should().BeTrue();
        vm.IsSideBySideTextVisible.Should().BeFalse();
        vm.UnifiedRows.Where(r => r.Row.IsChange).Select(r => r.Marker + r.Row.Text).Should().Equal("-old", "+new");
    }

    [Fact]
    public void Search_finds_text_in_the_chosen_panel()
    {
        using TempDir temp = new();
        DiffViewModel vm = New(TextFile(temp, Lines("alpha", "beta", "gamma"), Lines("alpha", "BETA2", "gamma")));
        vm.ContextIndex = vm.ContextOptions.Count - 1;

        vm.SearchText = "beta2";
        vm.FindNextCommand.Execute(null);
        vm.SelectedRowIndex.Should().Be(1);

        vm.SearchInLeft = true;
        vm.FindNextCommand.Execute(null);
        vm.Status.Should().Be(Strings.Get("Diff_NotFound"), "the left panel has no \"beta2\"");

        vm.SearchText = "gamma";
        vm.FindNextCommand.Execute(null);
        vm.SelectedRowIndex.Should().Be(2);
    }

    [Fact]
    public void Activating_a_row_requests_the_editor_at_the_right_side_line()
    {
        using TempDir temp = new();
        DiffViewModel vm = New(TextFile(temp, Lines("a", "b"), Lines("a", "x", "b")));
        (string BookPath, int Line)? requested = null;
        vm.OpenInEditorRequested += (_, target) => requested = target;

        vm.ActivateRow(vm.SelectedRowIndex);

        requested.Should().Be(("EPUB/text/ch.xhtml", 2));
    }

    [Fact]
    public void Removed_file_does_not_open_the_editor()
    {
        using TempDir temp = new();
        string leftPath = temp.Combine("left.xhtml");
        File.WriteAllText(leftPath, "gone");
        DiffViewModel vm = New(new BookFileDiff(
            BookFileChange.Removed, "EPUB/x.xhtml", null, leftPath, null, BookFileContentKind.Text));
        bool requested = false;
        vm.OpenInEditorRequested += (_, _) => requested = true;

        vm.ActivateRow(0);

        requested.Should().BeFalse();
        vm.SideBySideRows.Should().ContainSingle().Which.Row.Left.Kind.Should().Be(DiffLineKind.Deleted);
    }

    [Fact]
    public void Renamed_binary_and_image_files_have_their_own_presentation()
    {
        using TempDir temp = new();
        string a = temp.Combine("a.png");
        string b = temp.Combine("b.png");
        File.WriteAllBytes(a, new byte[] { 1 });
        File.WriteAllBytes(b, new byte[] { 2 });
        BookFileDiff renamed = new(BookFileChange.Renamed, "EPUB/old.css", "EPUB/new.css", a, b, BookFileContentKind.Text);
        BookFileDiff binary = new(BookFileChange.Added, null, "EPUB/font.ttf", null, b, BookFileContentKind.Binary);
        BookFileDiff image = new(BookFileChange.Modified, "EPUB/i.png", "EPUB/i.png", a, b, BookFileContentKind.Image);
        DiffViewModel vm = New(renamed, binary, image);

        vm.Message.Should().Be(Strings.Format("Diff_RenamedMessage", "EPUB/old.css", "EPUB/new.css"));

        vm.SelectedFile = vm.Files[1];
        vm.IsMessageVisible.Should().BeTrue();
        vm.Message.Should().Be(Strings.Format("Diff_BinaryAdded", "EPUB/font.ttf"));

        vm.SelectedFile = vm.Files[2];
        vm.IsImageVisible.Should().BeTrue();
        vm.LeftImagePath.Should().Be(a);
        vm.RightImagePath.Should().Be(b);
    }
}
