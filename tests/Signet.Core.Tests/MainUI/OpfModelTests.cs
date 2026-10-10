using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>
/// Tests of <see cref="OpfModel"/> — building the tree from <see cref="Book"/>, spine sorting,
/// flags (cover / nav / well-formed), reordering Text and reacting to resource registry events.
/// </summary>
public sealed class OpfModelTests
{
    private static Book Load(string corpusDir, TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(corpusDir, temp)).GetBook();

    [Fact]
    public void All_seven_groups_are_always_built_in_canonical_order_even_when_empty()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);

        model.Folders.Select(f => f.Kind).Should().Equal(
            OpfModelGroupKind.Text,
            OpfModelGroupKind.Styles,
            OpfModelGroupKind.Images,
            OpfModelGroupKind.Fonts,
            OpfModelGroupKind.Audio,
            OpfModelGroupKind.Video,
            OpfModelGroupKind.Misc);
        model.GetFolder(OpfModelGroupKind.Video)!.Entries.Should().BeEmpty();
        model.GetFolder(OpfModelGroupKind.Text)!.DefaultFolder
            .Should().Be(book.GetFolderKeeper().GetDefaultFolderForGroup("Text"));
    }

    [Fact]
    public void Opf_and_ncx_are_top_level_not_inside_a_group()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub2Minimal, temp);
        using OpfModel model = new(book);

        model.TopLevelFiles.Select(e => e.ResourceType)
            .Should().Contain(ResourceType.Opf).And.Contain(ResourceType.Ncx);
        model.Folders.SelectMany(f => f.Entries)
            .Should().NotContain(e =>
                e.ResourceType == ResourceType.Opf || e.ResourceType == ResourceType.Ncx);
    }

    [Fact]
    public void Text_group_is_ordered_by_spine()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        using OpfModel model = new(book);

        var text = model.GetFolder(OpfModelGroupKind.Text)!;
        var spined = text.Entries.Where(e => e.ReadingOrder >= 0).ToList();

        spined.Select(e => e.ReadingOrder).Should().BeInAscendingOrder();
        spined.Select(e => e.BookPath).Should().ContainInOrder(
            book.GetOpf().GetSpineOrderBookPaths());
    }

    [Fact]
    public void Non_text_groups_are_sorted_alphabetically()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);

        var images = model.GetFolder(OpfModelGroupKind.Images)!;
        images.Entries.Select(e => e.DisplayName)
            .Should().BeInAscendingOrder(StringComparer.Ordinal)
            .And.HaveCountGreaterThan(1);
    }

    [Fact]
    public void Cover_image_and_nav_document_are_flagged()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);

        model.AllEntries().Should().ContainSingle(e => e.IsCover)
            .Which.BookPath.Should().EndWith("cover.png");
        model.AllEntries().Should().ContainSingle(e => e.IsNav)
            .Which.BookPath.Should().EndWith("nav.xhtml");
    }

    [Fact]
    public void Nav_entry_carries_manifest_properties()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);

        model.AllEntries().Single(e => e.IsNav).ManifestProperties.Should().Contain("nav");
    }

    [Fact]
    public void Html_entries_in_a_clean_book_are_well_formed()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        using OpfModel model = new(book);

        model.AllEntries()
            .Where(e => e.ResourceType == ResourceType.Html)
            .Should().OnlyContain(e => e.IsWellFormed == true);
    }

    [Fact]
    public void ReorderText_rewrites_the_spine_and_raises_changed()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        using OpfModel model = new(book);

        int changed = 0;
        model.Changed += (_, _) => changed++;

        var text = model.GetFolder(OpfModelGroupKind.Text)!;
        var reversed = text.Entries.Reverse().ToList();
        model.ReorderText(reversed).Should().BeTrue();

        changed.Should().BeGreaterThan(0);
        var spine = book.GetOpf().GetSpineOrderBookPaths();
        spine.Should().HaveCount(2);
        spine[0].Should().EndWith("chapter2.xhtml");
        spine[1].Should().EndWith("chapter1.xhtml");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void ReorderText_rejects_a_list_that_is_not_a_permutation()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        using OpfModel model = new(book);

        var text = model.GetFolder(OpfModelGroupKind.Text)!;
        var incomplete = text.Entries.Take(1).ToList();

        Action act = () => model.ReorderText(incomplete);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MoveText_shifts_a_single_document()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        using OpfModel model = new(book);

        var first = model.GetFolder(OpfModelGroupKind.Text)!.Entries[0];
        model.MoveText(first, 1).Should().BeTrue();

        model.GetFolder(OpfModelGroupKind.Text)!.Entries[1].BookPath.Should().Be(first.BookPath);
    }

    private static Book BookWithChapters(int count)
    {
        Book book = BookCreator.CreateNewBook("2.0");
        while (book.GetHtmlResources().Count < count)
        {
            book.CreateEmptyHtmlFile();
        }

        return book;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MoveTextToEdge_moves_the_selected_documents_keeping_their_order(bool toTop)
    {
        using Book book = BookWithChapters(4);
        using OpfModel model = new(book);
        List<string> before = model.GetFolder(OpfModelGroupKind.Text)!.Entries.Select(e => e.BookPath).ToList();
        before.Should().HaveCountGreaterThanOrEqualTo(3);
        var entries = model.GetFolder(OpfModelGroupKind.Text)!.Entries;
        var selected = new[] { entries[^1], entries[1] };
        string[] moved = { before[1], before[^1] };

        model.MoveTextToEdge(selected, toTop).Should().BeTrue();

        List<string> after = model.GetFolder(OpfModelGroupKind.Text)!.Entries.Select(e => e.BookPath).ToList();
        List<string> rest = before.Except(moved).ToList();
        after.Should().Equal(toTop ? moved.Concat(rest) : rest.Concat(moved));
        book.GetOpf().GetSpineOrderBookPaths().Where(after.Contains).Should().Equal(after, "the spine follows the new order");
    }

    [Fact]
    public void MoveTextToEdge_reports_no_change_when_the_documents_are_already_there()
    {
        using Book book = BookWithChapters(3);
        using OpfModel model = new(book);
        var entries = model.GetFolder(OpfModelGroupKind.Text)!.Entries;

        model.MoveTextToEdge(new[] { entries[0] }, toTop: true).Should().BeFalse();
        model.MoveTextToEdge(new[] { entries[^1] }, toTop: false).Should().BeFalse();
    }

    [Fact]
    public void Model_refreshes_when_a_resource_is_removed()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);

        int before = model.GetFolder(OpfModelGroupKind.Images)!.Entries.Count;
        int changed = 0;
        model.Changed += (_, _) => changed++;

        Resource figure = model.GetFolder(OpfModelGroupKind.Images)!
            .Entries.First(e => e.BookPath.EndsWith("figure.png", StringComparison.Ordinal)).Resource;
        book.GetFolderKeeper().BulkRemoveResources(new[] { figure });

        changed.Should().BeGreaterThan(0);
        model.GetFolder(OpfModelGroupKind.Images)!.Entries.Count.Should().Be(before - 1);
    }

    [Fact]
    public void ShowFullPath_switches_display_names_to_book_paths()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book, showFullPath: true);

        model.AllEntries().Should().OnlyContain(e => e.DisplayName == e.BookPath);
    }
}
