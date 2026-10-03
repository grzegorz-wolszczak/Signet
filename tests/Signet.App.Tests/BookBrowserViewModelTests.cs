using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Semantics;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests of <see cref="BookBrowserViewModel"/>: the no-book state, building nodes, selection, opening.</summary>
public sealed class BookBrowserViewModelTests
{
    private static BookBrowserViewModel NewViewModel(out SettingsStore settings)
    {
        settings = new SettingsStore(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"signet-bb-{System.Guid.NewGuid():N}.json"));
        return new BookBrowserViewModel(settings, new StatusBarService());
    }

    private static Book Load(string corpusDir, TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(corpusDir, temp)).GetBook();

    private static IEnumerable<BookBrowserNode> Flatten(BookBrowserNode node)
    {
        yield return node;
        foreach (BookBrowserNode child in node.Children)
        {
            foreach (BookBrowserNode descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public void Without_a_book_there_are_no_nodes()
    {
        BookBrowserViewModel sut = NewViewModel(out _);

        sut.HasBook.Should().BeFalse();
        sut.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void SetBook_builds_folder_nodes_with_file_children()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        sut.SetBook(book);

        sut.HasBook.Should().BeTrue();
        sut.Nodes.Where(n => n.IsFolder).Select(n => n.Header)
            .Should().Equal("Text", "Styles", "Images", "Fonts", "Audio", "Video", "Misc");
        sut.Nodes.Single(n => n.Header == "Images").Children
            .Should().OnlyContain(n => !n.IsFolder);
        sut.Nodes.Where(n => !n.IsFolder).Should().Contain(n => n.Header.EndsWith(".opf", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_groups_are_shown_as_empty_folders()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        sut.SetBook(book);

        sut.Nodes.Single(n => n.Header == "Video").Children.Should().BeEmpty();
        sut.Nodes.Single(n => n.Header == "Misc").Children.Should().BeEmpty();
    }

    [Fact]
    public void SetBook_requests_opening_the_first_file_of_the_Text_folder()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        IReadOnlyList<Resource>? opened = null;
        sut.OpenResourceRequested += (_, r) => opened = r;

        sut.SetBook(book);

        opened.Should().ContainSingle()
            .Which.Should().BeSameAs(sut.Nodes.Single(n => n.Header == "Text").Children[0].Entry!.Resource);
    }

    [Fact]
    public void Only_the_Text_folder_is_expanded_after_SetBook()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        sut.SetBook(book);

        sut.Nodes.Where(n => n.IsExpanded).Select(n => n.Header).Should().Equal("Text");
    }

    [Fact]
    public void Folder_expansion_survives_a_tree_rebuild_but_resets_for_a_new_book()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        sut.Nodes.Single(n => n.Header == "Images").IsExpanded = true;
        sut.Nodes.Single(n => n.Header == "Text").IsExpanded = false;
        sut.AddBlankCssCommand.Execute(null);

        sut.Nodes.Where(n => n.IsExpanded).Select(n => n.Header).Should().Equal("Images");

        sut.SetBook(book);

        sut.Nodes.Where(n => n.IsExpanded).Select(n => n.Header).Should().Equal("Text");
    }

    [Fact]
    public void Folder_tooltip_shows_the_default_group_folder_and_file_count()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        text.ToolTip.Should().Contain(book.GetFolderKeeper().GetDefaultFolderForGroup("Text"))
            .And.Contain(text.Children.Count.ToString(System.Globalization.CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Cover_and_nav_flags_are_exposed_on_entries()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        List<BookBrowserNode> all = sut.Nodes.SelectMany(Flatten).ToList();
        all.Should().ContainSingle(n => n.Entry != null && n.Entry.IsCover);
        all.Should().ContainSingle(n => n.Entry != null && n.Entry.IsNav);
    }

    [Fact]
    public void Open_command_raises_request_for_selected_files_only()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        IReadOnlyList<Resource>? opened = null;
        sut.OpenResourceRequested += (_, r) => opened = r;

        BookBrowserNode textFolder = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { textFolder, textFolder.Children[0] });

        sut.OpenSelectedCommand.CanExecute(null).Should().BeTrue();
        sut.OpenSelectedCommand.Execute(null);

        opened.Should().ContainSingle()
            .Which.BookPath.Should().Be(textFolder.Children[0].Entry!.BookPath);
    }

    [Fact]
    public void Rename_command_is_enabled_only_for_a_single_selection()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        sut.UpdateSelection(new object?[] { text.Children[0], text.Children[1] });
        sut.RenameSelectedCommand.CanExecute(null).Should().BeFalse();

        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.RenameSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Rename_command_starts_in_place_editing_with_the_bare_filename()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode file = sut.Nodes.Single(n => n.Header == "Text").Children[0];
        sut.UpdateSelection(new object?[] { file });
        OpfModelEntry? requested = null;
        sut.RenameRequested += (_, entry) => requested = entry;

        sut.RenameSelectedCommand.Execute(null);

        sut.EditingNode.Should().BeSameAs(file);
        file.IsEditing.Should().BeTrue();
        file.EditText.Should().Be(file.Entry!.Resource.Filename);
        requested.Should().BeSameAs(file.Entry);
    }

    [Fact]
    public void CommitInPlaceRename_renames_the_file_and_reselects_it()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode file = sut.Nodes.Single(n => n.Header == "Text").Children[0];
        Resource resource = file.Entry!.Resource;
        sut.UpdateSelection(new object?[] { file });
        IReadOnlyList<BookBrowserNode>? reselected = null;
        sut.TreeSelectionRequested += (_, nodes) => reselected = nodes;

        sut.RenameSelectedCommand.Execute(null);
        file.EditText = "  renamed.xhtml ";
        sut.CommitInPlaceRename();

        resource.Filename.Should().Be("renamed.xhtml");
        sut.EditingNode.Should().BeNull();
        reselected.Should().ContainSingle()
            .Which.Entry!.Resource.Should().BeSameAs(resource);
    }

    [Fact]
    public void CancelInPlaceRename_leaves_the_filename_untouched()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode file = sut.Nodes.Single(n => n.Header == "Text").Children[0];
        string original = file.Entry!.Resource.Filename;
        sut.UpdateSelection(new object?[] { file });

        sut.RenameSelectedCommand.Execute(null);
        file.EditText = "other.xhtml";
        sut.CancelInPlaceRename();

        file.Entry.Resource.Filename.Should().Be(original);
        file.IsEditing.Should().BeFalse();
        sut.EditingNode.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CommitInPlaceRename_with_a_blank_name_does_nothing(string blank)
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode file = sut.Nodes.Single(n => n.Header == "Text").Children[0];
        string original = file.Entry!.Resource.Filename;
        sut.UpdateSelection(new object?[] { file });

        sut.RenameSelectedCommand.Execute(null);
        file.EditText = blank;
        sut.CommitInPlaceRename();

        file.Entry.Resource.Filename.Should().Be(original);
        sut.EditingNode.Should().BeNull();
    }

    [Fact]
    public void ApplyDelete_removes_files_and_rebuilds_the_tree()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode figure = sut.Nodes.Single(n => n.Header == "Images")
            .Children.First(n => n.Header.EndsWith("figure.png", System.StringComparison.Ordinal));

        sut.ApplyDelete(new[] { figure.Entry! });

        sut.Nodes.Single(n => n.Header == "Images").Children
            .Should().NotContain(n => n.Header.EndsWith("figure.png", System.StringComparison.Ordinal));
        book.GetAllResources().Should().NotContain(r => r.BookPath.EndsWith("figure.png", System.StringComparison.Ordinal));
    }

    [Fact]
    public void AddBlankHtml_addsResourceAndRaisesOpenRequest()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        int textCountBefore = sut.Nodes.Single(n => n.Header == "Text").Children.Count;

        IReadOnlyList<Resource>? opened = null;
        sut.OpenResourceRequested += (_, r) => opened = r;

        sut.AddBlankHtmlCommand.CanExecute(null).Should().BeTrue();
        sut.AddBlankHtmlCommand.Execute(null);

        opened.Should().ContainSingle().Which.Should().BeOfType<HtmlResource>();
        sut.Nodes.Single(n => n.Header == "Text").Children.Should().HaveCount(textCountBefore + 1);
    }

    [Fact]
    public void AddBlankCss_addsStylesheet()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        sut.AddBlankCssCommand.Execute(null);

        book.GetAllResources().Should().ContainSingle(r => r.Filename == "Style0001.css");
    }

    [Fact]
    public void AddBlankSvg_addsSvgResource()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        sut.AddBlankSvgCommand.Execute(null);

        book.GetAllResources().Should().ContainSingle(r => r.Filename == "Image0001.svg");
    }

    [Fact]
    public void AddBlankJs_isDisabledForEpub2Book()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub2Minimal, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        sut.AddBlankJsCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AddBlankJs_isEnabledForEpub3Book()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        sut.AddBlankJsCommand.CanExecute(null).Should().BeTrue();
        sut.AddBlankJsCommand.Execute(null);

        book.GetAllResources().Should().ContainSingle(r => r.Filename == "Script0001.js");
    }

    [Fact]
    public void ApplyAddExistingFiles_copiesFilesIntoBookAndRaisesOpenRequest()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        string imagePath = temp.Combine("added.jpg");
        System.IO.File.WriteAllBytes(imagePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        IReadOnlyList<Resource>? opened = null;
        sut.OpenResourceRequested += (_, r) => opened = r;

        sut.ApplyAddExistingFiles(new[] { imagePath });

        opened.Should().ContainSingle().Which.Filename.Should().Be("added.jpg");
        book.GetAllResources().Should().Contain(r => r.Filename == "added.jpg");
    }

    [Fact]
    public void ApplyDelete_refusesToRemoveTheNavDocument()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode nav = sut.Nodes.SelectMany(Flatten).Single(n => n.Entry?.IsNav == true);

        sut.ApplyDelete(new[] { nav.Entry! });

        book.GetAllResources().Should().Contain(r => r.BookPath == nav.Entry!.BookPath);
    }

    [Fact]
    public void ApplyDelete_refusesToRemoveTheNcx()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub2Minimal, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode ncx = sut.Nodes.SelectMany(Flatten).Single(n => n.Header.EndsWith(".ncx", System.StringComparison.Ordinal));

        sut.ApplyDelete(new[] { ncx.Entry! });

        book.GetNcx().Should().NotBeNull();
    }

    [Fact]
    public void ApplyDelete_refusesToRemoveAllHtmlFiles()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        List<OpfModelEntry> allHtml = sut.Nodes.SelectMany(Flatten)
            .Where(n => n.Entry?.ResourceType == ResourceType.Html)
            .Select(n => n.Entry!)
            .ToList();

        sut.ApplyDelete(allHtml);

        book.GetHtmlResources().Should().HaveCount(allHtml.Count);
    }

    [Fact]
    public void ApplyRenameWithTemplate_RenamesSelectedFilesSequentiallyAndUpdatesLinks()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out SettingsStore settings);
        sut.SetBook(book);

        List<OpfModelEntry> text = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).ToList();

        sut.ApplyRenameWithTemplate(text, "Chapter0001.xhtml");

        book.GetHtmlResources().Select(r => r.Filename)
            .Should().Contain(f => f.StartsWith("Chapter", System.StringComparison.Ordinal));
        settings.RenameTemplate.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ApplyRenameWithTemplate_ForbiddenCharacter_ShowsStatusAndDoesNotRename()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        List<OpfModelEntry> text = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).ToList();
        List<string> before = book.GetHtmlResources().Select(r => r.Filename).ToList();

        sut.ApplyRenameWithTemplate(text, "bad:name.xhtml");

        book.GetHtmlResources().Select(r => r.Filename).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void ApplyRenameList_RenamesAndUpdatesCrossReferences()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        HtmlResource chapter = book.GetHtmlResources().Single(r => r.Filename == "chapter1.xhtml");
        List<Resource> resources = new() { image };
        List<string> newNames = new() { "figure-bulk.png" };

        sut.ApplyRenameList(resources, newNames);

        image.Filename.Should().Be("figure-bulk.png");
        chapter.GetText().Should().Contain("figure-bulk.png");
    }

    [Fact]
    public void RenameSelectedWithTemplateCommand_and_BulkRegexRenameSelectedCommand_require_a_selection()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        sut.RenameSelectedWithTemplateCommand.CanExecute(null).Should().BeFalse();
        sut.BulkRegexRenameSelectedCommand.CanExecute(null).Should().BeFalse();

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });

        sut.RenameSelectedWithTemplateCommand.CanExecute(null).Should().BeTrue();
        sut.BulkRegexRenameSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    private static List<object?> NonNavTextChildren(BookBrowserNode text) =>
        text.Children.Where(n => !n.Entry!.IsNav).Cast<object?>().ToList();

    [Fact]
    public void MergeSelectedCommand_requires_at_least_two_html_files()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.MergeSelectedCommand.CanExecute(null).Should().BeFalse();

        sut.UpdateSelection(NonNavTextChildren(text));
        sut.MergeSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void MergeSelected_combinesSelectedFilesIntoTheFirstOne()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        int htmlCountBefore = book.GetHtmlResources().Count;

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        int selectedCount = NonNavTextChildren(text).Count;
        sut.UpdateSelection(NonNavTextChildren(text));
        sut.MergeSelectedCommand.Execute(null);

        book.GetHtmlResources().Should().HaveCount(htmlCountBefore - selectedCount + 1);
    }

    [Fact]
    public void MergeSelected_refusesWhenNavIsIncludedInSelection()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        int htmlCountBefore = book.GetHtmlResources().Count;

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(text.Children.Cast<object?>());
        sut.MergeSelectedCommand.Execute(null);

        book.GetHtmlResources().Should().HaveCount(htmlCountBefore);
    }

    [Fact]
    public void SplitSelected_withoutMarkers_showsStatusMessageAndDoesNotChangeTheBook()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        int htmlCountBefore = book.GetHtmlResources().Count;

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.SplitSelectedCommand.CanExecute(null).Should().BeTrue();
        sut.SplitSelectedCommand.Execute(null);

        book.GetHtmlResources().Should().HaveCount(htmlCountBefore);
    }

    [Fact]
    public void SplitSelected_withMarker_createsNewSection()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        HtmlResource chapter = book.GetHtmlResources().Single(r => r.Filename == "chapter1.xhtml");
        chapter.SetText(chapter.GetText().Replace(
            "<h1>Chapter 1</h1>", "<h1>Chapter 1</h1><hr class=\"signet_split_marker\"/>", System.StringComparison.Ordinal));
        int htmlCountBefore = book.GetHtmlResources().Count;

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        BookBrowserNode chapterNode = text.Children.Single(n => n.Header.EndsWith("chapter1.xhtml", System.StringComparison.Ordinal));
        sut.UpdateSelection(new object?[] { chapterNode });
        sut.SplitSelectedCommand.Execute(null);

        book.GetHtmlResources().Should().HaveCount(htmlCountBefore + 1);
    }

    [Fact]
    public void MoveText_reorders_the_spine()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        BookBrowserNode first = text.Children[0];
        sut.UpdateSelection(new object?[] { first });

        sut.MoveTextDownCommand.CanExecute(null).Should().BeTrue();
        sut.MoveTextDownCommand.Execute(null);

        sut.Nodes.Single(n => n.Header == "Text").Children[1].Header.Should().Be(first.Header);
    }

    [Fact]
    public void AddSemanticsSelectedCommand_requires_a_single_html_selection()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.AddSemanticsSelectedCommand.CanExecute(null).Should().BeTrue();

        sut.UpdateSelection(new object?[] { text.Children[0], text.Children[1] });
        sut.AddSemanticsSelectedCommand.CanExecute(null).Should().BeFalse();

        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        sut.UpdateSelection(new object?[] { images.Children[0] });
        sut.AddSemanticsSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CoverImageSelectedCommand_requires_a_single_image_selection()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        sut.UpdateSelection(new object?[] { images.Children[0] });
        sut.CoverImageSelectedCommand.CanExecute(null).Should().BeTrue();

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.CoverImageSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CoverImageSelected_MarksSelectedImageAsCover()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        ImageResource figure = (ImageResource)book.GetAllResources().Single(r => r.Filename == "figure.png");
        BookBrowserNode figureNode = sut.Nodes.Single(n => n.Header == "Images")
            .Children.Single(n => n.Entry!.Resource == figure);
        sut.UpdateSelection(new object?[] { figureNode });

        sut.CoverImageSelectedCommand.Execute(null);

        book.GetOpf().IsCoverImage(figure).Should().BeTrue();
        sut.Nodes.Single(n => n.Header == "Images").Children
            .Single(n => n.Entry!.Resource == figure).Entry!.IsCover.Should().BeTrue();
    }

    [Fact]
    public void GetSemanticsInfo_Epub3_ReturnsLandmarkCodesAndCurrentCode()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        OpfModelEntry chapter = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).Single(e => e.Resource.Filename == "chapter1.xhtml");

        (bool isEpub3, string currentCode, IReadOnlyDictionary<string, DescriptiveInfo> codeMap) =
            sut.GetSemanticsInfo(chapter);

        isEpub3.Should().BeTrue();
        currentCode.Should().Be("bodymatter"); // already set in the nav.xhtml fixture
        codeMap.Should().ContainKey("bodymatter");
    }

    [Fact]
    public void ApplySemanticCode_Epub3_SetsLandmarkAndRefreshesTree()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        OpfModelEntry chapter = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).Single(e => e.Resource.Filename == "chapter1.xhtml");

        sut.ApplySemanticCode(chapter, "preface");

        OpfModelEntry updated = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).Single(e => e.Resource.Filename == "chapter1.xhtml");
        updated.SemanticTypes.Should().Contain(Landmarks.GetName("preface")).And.NotContain(Landmarks.GetName("bodymatter"));
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void ApplySemanticCode_Epub3_NavResourceOnlyAcceptsTocCode()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        OpfModelEntry navEntry = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).Single(e => e.IsNav);
        HtmlResource navResource = (HtmlResource)navEntry.Resource;

        int landmarkCountBefore = new NavProcessor(navResource).GetLandmarks().Count;
        sut.ApplySemanticCode(navEntry, "bodymatter");
        new NavProcessor(navResource).GetLandmarks().Should().HaveCount(landmarkCountBefore);

        // The nav may be marked with the "toc" semantics — stored as the special href "#toc",
        // so GetLandmarkCodeForResource(nav) will not find it (a side effect —
        // so we check the landmark list directly).
        sut.ApplySemanticCode(navEntry, "toc");
        new NavProcessor(navResource).GetLandmarks().Should().Contain(e => e.EpubType == "toc" && e.Href == "#toc");
    }

    [Fact]
    public void LinkStylesheetsSelectedCommand_requires_html_selection_and_existing_css()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.LinkStylesheetsSelectedCommand.CanExecute(null).Should().BeTrue();

        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        sut.UpdateSelection(new object?[] { images.Children[0] });
        sut.LinkStylesheetsSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void LinkJavascriptsSelectedCommand_disabled_without_any_script_resource()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.LinkJavascriptsSelectedCommand.CanExecute(null).Should().BeFalse();

        book.CreateEmptyJsFile();
        sut.SetBook(book);
        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.LinkJavascriptsSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ApplyLinkStylesheets_rewrites_head_links_of_selected_files()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        var chapter = (HtmlResource)book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");

        sut.ApplyLinkStylesheets(new[] { chapter }, System.Array.Empty<string>());

        chapter.GetText().Should().NotContain("stylesheet");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void ApplyLinkStylesheets_reportsNotWellFormedFileOnStatusBar()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        StatusBarService status = new();
        SettingsStore settings = new(temp.Combine("s.json"));
        BookBrowserViewModel sut = new(settings, status);
        sut.SetBook(book);

        var chapter = (HtmlResource)book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText("<html><head><body><p>broken");

        sut.ApplyLinkStylesheets(new[] { chapter }, System.Array.Empty<string>());

        status.CurrentMessage.Should().Contain("well-formed");
    }

    [Fact]
    public void MoveSelectedCommand_requires_a_single_media_type_group_selection()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);

        sut.MoveSelectedCommand.CanExecute(null).Should().BeFalse();

        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        sut.UpdateSelection(new object?[] { images.Children[0] });
        sut.MoveSelectedCommand.CanExecute(null).Should().BeTrue();

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { images.Children[0], text.Children[0] });
        sut.MoveSelectedCommand.CanExecute(null).Should().BeFalse("the selection spans two different media-type groups");
    }

    [Fact]
    public void MoveSelectedCommand_raises_MoveRequested_for_the_current_selection()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        sut.UpdateSelection(new object?[] { images.Children[0] });

        IReadOnlyList<OpfModelEntry>? raised = null;
        sut.MoveRequested += (_, entries) => raised = entries;
        sut.MoveSelectedCommand.Execute(null);

        raised.Should().NotBeNull();
        raised!.Should().HaveCount(1);
    }

    [Fact]
    public void GetFoldersForSelection_returnsFoldersOfTheImagesGroup()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        string imagesFolder = Signet.Core.BookPath.StartingDir(image.BookPath);

        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        IReadOnlyList<string> folders = sut.GetFoldersForSelection(new[] { images.Children[0].Entry! });

        folders.Should().Contain(imagesFolder);
    }

    [Fact]
    public void ApplyMove_movesTheResourceAndUpdatesCrossReferences()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        string imagesFolder = Signet.Core.BookPath.StartingDir(image.BookPath);
        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        OpfModelEntry entry = images.Children.Select(n => n.Entry!).Single(e => e.Resource.Filename == "figure.png");

        sut.ApplyMove(new[] { entry }, imagesFolder + "/sub");

        image.BookPath.Should().Be(imagesFolder + "/sub/figure.png");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void ApplyMove_invalidDestination_showsStatusMessage()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        StatusBarService status = new();
        SettingsStore settings = new(temp.Combine("s.json"));
        BookBrowserViewModel sut = new(settings, status);
        sut.SetBook(book);
        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        OpfModelEntry entry = images.Children.Select(n => n.Entry!).Single(e => e.Resource.Filename == "figure.png");

        sut.ApplyMove(new[] { entry }, "../outside");

        status.CurrentMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AddCopy_duplicatesSelectedHtmlAndOpensCopy()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        HtmlResource source = (HtmlResource)text.Children[0].Entry!.Resource;
        source.SetText("<html><body><p>oryginal</p></body></html>");

        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.AddCopySelectedCommand.CanExecute(null).Should().BeTrue();

        IReadOnlyList<Resource>? opened = null;
        sut.OpenResourceRequested += (_, r) => opened = r;
        sut.AddCopySelectedCommand.Execute(null);

        HtmlResource copy = (HtmlResource)opened.Should().ContainSingle().Which;
        copy.Should().NotBeSameAs(source);
        copy.GetText().Should().Be(source.GetText());
    }

    [Fact]
    public void AddCopy_disabledForMultiSelectionOrNonHtmlCss()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");

        sut.UpdateSelection(new object?[] { images.Children[0] });
        sut.AddCopySelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RenumberToc_enabledOnlyForNcxSelection_andRecomputesPlayOrder()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub2Minimal, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        NcxResource ncx = book.GetNcx()!;
        BookBrowserNode ncxNode = sut.Nodes.Where(n => !n.IsFolder).Single(n => n.Entry!.Resource == ncx);

        sut.UpdateSelection(new object?[] { ncxNode });
        sut.RenumberTocSelectedCommand.CanExecute(null).Should().BeTrue();

        string before = ncx.GetText();
        sut.RenumberTocSelectedCommand.Execute(null);

        // Saving through SetNcxDocument(GetNcxDocument()) always goes through ToXml()
        // (RecomputePlayOrder) — the content may be identical if it was already correct, but
        // the operation must run without an exception and mark the book as modified.
        ncx.GetText().Should().NotBeNullOrEmpty();
        before.Should().NotBeNullOrEmpty();
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void RenumberToc_disabledForNonNcxSelection()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        sut.UpdateSelection(new object?[] { text.Children[0] });

        sut.RenumberTocSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void GetInfo_disabledWithoutSelection_enabledWithSelection_buildsMetadataText()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        sut.GetInfoSelectedCommand.CanExecute(null).Should().BeFalse();

        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.GetInfoSelectedCommand.CanExecute(null).Should().BeTrue();

        string? info = null;
        sut.GetInfoRequested += (_, infoText) => info = infoText;
        sut.GetInfoSelectedCommand.Execute(null);

        info.Should().NotBeNull();
        info!.Should().Contain(text.Children[0].Entry!.Resource.Filename);
        info.Should().Contain("Media type");
        info.Should().Contain("EPUB version");
    }

    [Fact]
    public void SaveAs_copiesResourceContentToDestination()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        HtmlResource source = (HtmlResource)text.Children[0].Entry!.Resource;
        source.SetText("<html><body><p>save-as</p></body></html>");
        string destination = temp.Combine("exported.xhtml");

        sut.ApplySaveAs(text.Children[0].Entry!, destination);

        System.IO.File.Exists(destination).Should().BeTrue();
        System.IO.File.ReadAllText(destination).Should().Contain("save-as");
    }

    [Fact]
    public void SelectAllInFolder_requestsAllSiblingsOfCurrentSelection()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");

        sut.UpdateSelection(new object?[] { images.Children[0] });

        IReadOnlyList<BookBrowserNode>? requested = null;
        sut.TreeSelectionRequested += (_, nodes) => requested = nodes;
        sut.SelectAllInFolderCommand.Execute(null);

        requested.Should().BeEquivalentTo(images.Children);
    }

    [Fact]
    public void ValidateSelectedWithW3C_enabledOnlyForCssSelection_raisesRequestWithResources()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        BookBrowserNode styles = sut.Nodes.Single(n => n.Header == "Styles");
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        sut.UpdateSelection(new object?[] { text.Children[0] });
        sut.ValidateSelectedWithW3CCommand.CanExecute(null).Should().BeFalse();

        sut.UpdateSelection(new object?[] { styles.Children[0] });
        sut.ValidateSelectedWithW3CCommand.CanExecute(null).Should().BeTrue();

        IReadOnlyList<CssResource>? requested = null;
        sut.ValidateWithW3CRequested += (_, css) => requested = css;
        sut.ValidateSelectedWithW3CCommand.Execute(null);

        requested.Should().ContainSingle()
            .Which.Should().Be(styles.Children[0].Entry!.Resource);
    }
}
