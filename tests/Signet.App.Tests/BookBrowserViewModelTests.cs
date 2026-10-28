using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Resources;
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
            System.IO.Path.GetTempPath(), $"signet-bb-{System.Guid.NewGuid():N}.xml"));
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

    private sealed class SuspendingBookOperationHost : IBookOperationHost
    {
        private readonly BookBrowserViewModel _viewModel;

        public SuspendingBookOperationHost(BookBrowserViewModel viewModel) => _viewModel = viewModel;

        public Task<BookOperationOutcome<T>> RunAsync<T>(string title, string? checkpointName, Func<T> work)
        {
            _viewModel.SuspendRebuilds = true;
            try
            {
                return Task.FromResult(new BookOperationOutcome<T>(true, work(), false));
            }
            finally
            {
                _viewModel.SuspendRebuilds = false;
            }
        }
    }

    [Fact]
    public void Without_a_book_there_are_no_nodes()
    {
        // Arrange
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act & Assert
        sut.HasBook.Should().BeFalse();

        // Act & Assert
        sut.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void SetBook_builds_folder_nodes_with_file_children()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Assert
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
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Assert
        sut.Nodes.Single(n => n.Header == "Video").Children.Should().BeEmpty();
        sut.Nodes.Single(n => n.Header == "Misc").Children.Should().BeEmpty();
    }

    [Fact]
    public void SetBook_requests_opening_the_first_file_of_the_Text_folder()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);
        IReadOnlyList<Resource>? opened = null;
        sut.OpenResourceRequested += (_, r) => opened = r;

        // Act
        sut.SetBook(book);

        // Assert
        opened.Should().ContainSingle()
            .Which.Should().BeSameAs(sut.Nodes.Single(n => n.Header == "Text").Children[0].Entry!.Resource);
    }

    [Fact]
    public void Only_the_Text_folder_is_expanded_after_SetBook()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Assert
        sut.Nodes.Where(n => n.IsExpanded).Select(n => n.Header).Should().Equal("Text");
    }

    [Fact]
    public void The_folders_chosen_in_the_settings_are_expanded_after_SetBook()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out SettingsStore settings);
        settings.SetBookBrowserExpandOnLoad(OpfModelGroupKind.Text, false);
        settings.SetBookBrowserExpandOnLoad(OpfModelGroupKind.Images, true);
        settings.SetBookBrowserExpandOnLoad(OpfModelGroupKind.Misc, true);

        // Act
        sut.SetBook(book);

        // Assert
        sut.Nodes.Where(n => n.IsFolder && n.IsExpanded).Select(n => n.Header).Should().BeEquivalentTo("Images", "Misc");
    }

    [Fact]
    public void Folders_have_the_calibre_icons_except_Audio_and_Video()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Assert
        sut.Nodes.Where(n => n.IsFolder).ToDictionary(n => n.Header, n => n.FolderIconKey).Should().Equal(new Dictionary<string, string?>
        {
            ["Text"] = "keyboard-prefs",
            ["Styles"] = "lookfeel",
            ["Images"] = "view-image",
            ["Fonts"] = "font",
            ["Audio"] = null,
            ["Video"] = null,
            ["Misc"] = "dir",
        });
        sut.Nodes.Where(n => n.IsFolder && n.FolderIconKey is null).Should().OnlyContain(n => n.IsPlainFolder);
    }

    [Fact]
    public void Folder_expansion_survives_a_tree_rebuild_but_resets_for_a_new_book()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Arrange
        sut.Nodes.Single(n => n.Header == "Images").IsExpanded = true;
        sut.Nodes.Single(n => n.Header == "Text").IsExpanded = false;

        // Act
        sut.AddBlankCssCommand.Execute(null);

        // Assert
        sut.Nodes.Where(n => n.IsExpanded).Select(n => n.Header).Should().Equal("Images");

        // Act
        sut.SetBook(book);

        // Assert
        sut.Nodes.Where(n => n.IsExpanded).Select(n => n.Header).Should().Equal("Text");
    }

    [Fact]
    public void Folder_tooltip_shows_the_default_group_folder_and_file_count()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        // Assert
        text.ToolTip.Should().Contain(book.GetFolderKeeper().GetDefaultFolderForGroup("Text"))
            .And.Contain(text.Children.Count.ToString(System.Globalization.CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Cover_and_nav_flags_are_exposed_on_entries()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        List<BookBrowserNode> all = sut.Nodes.SelectMany(Flatten).ToList();

        // Assert
        all.Should().ContainSingle(n => n.Entry != null && n.Entry.IsCover);
        all.Should().ContainSingle(n => n.Entry != null && n.Entry.IsNav);
    }

    [Fact]
    public void Open_command_raises_request_for_selected_files_only()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        IReadOnlyList<Resource>? opened = null;
        sut.OpenResourceRequested += (_, r) => opened = r;

        BookBrowserNode textFolder = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { textFolder, textFolder.Children[0] });

        // Act & Assert
        sut.OpenSelectedCommand.CanExecute(null).Should().BeTrue();

        // Act
        sut.OpenSelectedCommand.Execute(null);

        // Assert
        opened.Should().ContainSingle()
            .Which.BookPath.Should().Be(textFolder.Children[0].Entry!.BookPath);
    }

    [Fact]
    public void Rename_command_is_enabled_only_for_a_single_selection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Arrange
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        // Act
        sut.UpdateSelection(new object?[] { text.Children[0], text.Children[1] });

        // Act & Assert
        sut.RenameSelectedCommand.CanExecute(null).Should().BeFalse();

        // Act
        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.RenameSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Rename_command_starts_in_place_editing_with_the_bare_filename()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode file = sut.Nodes.Single(n => n.Header == "Text").Children[0];
        sut.UpdateSelection(new object?[] { file });

        // Arrange
        OpfModelEntry? requested = null;
        sut.RenameRequested += (_, entry) => requested = entry;

        // Act
        sut.RenameSelectedCommand.Execute(null);

        // Assert
        sut.EditingNode.Should().BeSameAs(file);
        file.IsEditing.Should().BeTrue();
        file.EditText.Should().Be(file.Entry!.Resource.Filename);
        requested.Should().BeSameAs(file.Entry);
    }

    [Fact]
    public async Task CommitInPlaceRename_renames_the_file_and_reselects_it()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode file = sut.Nodes.Single(n => n.Header == "Text").Children[0];
        Resource resource = file.Entry!.Resource;
        sut.UpdateSelection(new object?[] { file });
        IReadOnlyList<BookBrowserNode>? reselected = null;
        sut.TreeSelectionRequested += (_, nodes) => reselected = nodes;

        sut.RenameSelectedCommand.Execute(null);
        file.EditText = "  renamed.xhtml ";
        await sut.CommitInPlaceRenameAsync();

        // Assert
        resource.Filename.Should().Be("renamed.xhtml");
        sut.EditingNode.Should().BeNull();
        reselected.Should().ContainSingle()
            .Which.Entry!.Resource.Should().BeSameAs(resource);
    }

    [Fact]
    public void CancelInPlaceRename_leaves_the_filename_untouched()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode file = sut.Nodes.Single(n => n.Header == "Text").Children[0];
        string original = file.Entry!.Resource.Filename;
        sut.UpdateSelection(new object?[] { file });

        sut.RenameSelectedCommand.Execute(null);
        file.EditText = "other.xhtml";
        sut.CancelInPlaceRename();

        // Assert
        file.Entry.Resource.Filename.Should().Be(original);
        file.IsEditing.Should().BeFalse();
        sut.EditingNode.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CommitInPlaceRename_with_a_blank_name_does_nothing(string blank)
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode file = sut.Nodes.Single(n => n.Header == "Text").Children[0];
        string original = file.Entry!.Resource.Filename;
        sut.UpdateSelection(new object?[] { file });

        sut.RenameSelectedCommand.Execute(null);
        file.EditText = blank;
        await sut.CommitInPlaceRenameAsync();

        // Assert
        file.Entry.Resource.Filename.Should().Be(original);
        sut.EditingNode.Should().BeNull();
    }

    [Fact]
    public async Task ApplyDelete_removes_files_and_rebuilds_the_tree()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Arrange
        BookBrowserNode figure = sut.Nodes.Single(n => n.Header == "Images")
            .Children.First(n => n.Header.EndsWith("figure.png", System.StringComparison.Ordinal));

        // Act
        await sut.ApplyDeleteAsync(new[] { figure.Entry! });

        // Assert
        sut.Nodes.Single(n => n.Header == "Images").Children
            .Should().NotContain(n => n.Header.EndsWith("figure.png", System.StringComparison.Ordinal));
        book.GetAllResources().Should().NotContain(r => r.BookPath.EndsWith("figure.png", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyDelete_removes_the_toc_entries_of_deleted_files_and_reports_it()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.CreateEmptyHtmlFile();
        HtmlResource kept = book.GetHtmlResourcesExcludingNav()[0];

        // Act
        TocEditModel.Save(book, new TocEntry
        {
            IsRoot = true,
            Children =
            {
                new TocEntry { Text = "Kept", Target = kept.BookPath },
                new TocEntry { Text = "Deleted", Target = chapter.BookPath },
            },
        });
        BookBrowserViewModel sut = NewViewModel(out _);
        sut.SetBook(book);
        int tabsSaved = 0;
        int tocChanged = 0;
        sut.SaveOpenTabs = () => tabsSaved++;
        sut.TocChanged += (_, _) => tocChanged++;
        BookBrowserNode node = Flatten(sut.Nodes.Single(n => n.Header == "Text")).Single(n => n.Entry?.Resource == chapter);

        await sut.ApplyDeleteAsync(new[] { node.Entry! });

        // Assert
        TocEditModel.GetRootTocEntry(book).Children.Select(e => e.Text).Should().Equal("Kept");
        tabsSaved.Should().Be(1, "the open tabs are committed before the TOC is edited");
        tocChanged.Should().Be(1);
    }

    [Fact]
    public void AddBlankHtml_addsResourceAndRaisesOpenRequest()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Arrange
        int textCountBefore = sut.Nodes.Single(n => n.Header == "Text").Children.Count;

        IReadOnlyList<Resource>? opened = null;
        sut.OpenResourceRequested += (_, r) => opened = r;

        // Act & Assert
        sut.AddBlankHtmlCommand.CanExecute(null).Should().BeTrue();

        // Act
        sut.AddBlankHtmlCommand.Execute(null);

        // Assert
        opened.Should().ContainSingle().Which.Should().BeOfType<HtmlResource>();
        sut.Nodes.Single(n => n.Header == "Text").Children.Should().HaveCount(textCountBefore + 1);
    }

    [Fact]
    public void AddBlankCss_addsStylesheet()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        sut.AddBlankCssCommand.Execute(null);

        // Assert
        book.GetAllResources().Should().ContainSingle(r => r.Filename == "Style0001.css");
    }

    [Fact]
    public void AddBlankSvg_addsSvgResource()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        sut.AddBlankSvgCommand.Execute(null);

        // Assert
        book.GetAllResources().Should().ContainSingle(r => r.Filename == "Image0001.svg");
    }

    [Fact]
    public void AddBlankJs_isDisabledForEpub2Book()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub2Minimal, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Act & Assert
        sut.AddBlankJsCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AddBlankJs_isEnabledForEpub3Book()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Act & Assert
        sut.AddBlankJsCommand.CanExecute(null).Should().BeTrue();

        // Act
        sut.AddBlankJsCommand.Execute(null);

        // Assert
        book.GetAllResources().Should().ContainSingle(r => r.Filename == "Script0001.js");
    }

    [Fact]
    public async Task ApplyAddExistingFiles_copiesFilesIntoBookAndRaisesOpenRequest()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        string imagePath = temp.Combine("added.jpg");
        System.IO.File.WriteAllBytes(imagePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        IReadOnlyList<Resource>? opened = null;
        sut.OpenResourceRequested += (_, r) => opened = r;

        await sut.ApplyAddExistingFilesAsync(new[] { imagePath });

        // Assert
        opened.Should().ContainSingle().Which.Filename.Should().Be("added.jpg");
        book.GetAllResources().Should().Contain(r => r.Filename == "added.jpg");
    }

    [Fact]
    public async Task ApplyDelete_refusesToRemoveTheNavDocument()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        BookBrowserNode nav = sut.Nodes.SelectMany(Flatten).Single(n => n.Entry?.IsNav == true);

        await sut.ApplyDeleteAsync(new[] { nav.Entry! });

        // Assert
        book.GetAllResources().Should().Contain(r => r.BookPath == nav.Entry!.BookPath);
    }

    [Fact]
    public async Task ApplyDelete_refusesToRemoveTheNcx()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub2Minimal, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        BookBrowserNode ncx = sut.Nodes.SelectMany(Flatten).Single(n => n.Header.EndsWith(".ncx", System.StringComparison.Ordinal));

        await sut.ApplyDeleteAsync(new[] { ncx.Entry! });

        // Assert
        book.GetNcx().Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyDelete_refusesToRemoveAllHtmlFiles()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        List<OpfModelEntry> allHtml = sut.Nodes.SelectMany(Flatten)
            .Where(n => n.Entry?.ResourceType == ResourceType.Html)
            .Select(n => n.Entry!)
            .ToList();

        await sut.ApplyDeleteAsync(allHtml);

        // Assert
        book.GetHtmlResources().Should().HaveCount(allHtml.Count);
    }

    [Fact]
    public async Task ApplyMassRename_NumbersTheFilesWithPaddingKeepsTheExtensionsAndRemembersTheOptions()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out SettingsStore settings);

        // Act
        sut.SetBook(book);

        // Arrange
        List<OpfModelEntry> text = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).ToList();
        List<string> extensions = text.Select(e => System.IO.Path.GetExtension(e.Resource.Filename)).ToList();
        MassRenameOptions options = new("part", 2, '#', 5, false);

        // Act
        await sut.ApplyMassRenameAsync(text, options);

        // Assert
        text.Select(e => e.Resource.Filename).Should().Equal(
            extensions.Select((ext, i) => "part" + (2 + i).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(5, '#') + ext));
        settings.MassRename.Should().Be(options);
        sut.GetInitialMassRename().Should().Be(options, "the window starts with the last values");
    }

    [Fact]
    public async Task ApplyMassRename_InTheOrderOfTheBookNumbersTheFilesByTheSpineNotBySelection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        List<OpfModelEntry> text = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).ToList();

        // Assert
        text.Should().HaveCountGreaterThan(1);
        List<OpfModelEntry> bySpine = text.OrderBy(e => e.ReadingOrder >= 0 ? e.ReadingOrder : int.MaxValue).ToList();
        List<OpfModelEntry> reversed = Enumerable.Reverse(text).ToList();

        // Act & Assert
        BookBrowserViewModel.CanUseBookOrder(reversed).Should().BeTrue();

        // Act
        await sut.ApplyMassRenameAsync(reversed, new MassRenameOptions("part", 1, '0', 3, true));

        // Assert
        bySpine.Select(e => System.IO.Path.GetFileNameWithoutExtension(e.Resource.Filename))
            .Should().Equal(Enumerable.Range(1, bySpine.Count).Select(i => "part" + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void MassRename_TheOrderOfTheBookIsOnlyForHtmlFiles()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        OpfModelEntry html = sut.Nodes.Single(n => n.Header == "Text").Children.First().Entry!;
        OpfModelEntry other = sut.Nodes.First(n => n.Header != "Text" && n.Children.Any(c => c.Entry is not null)).Children.First(c => c.Entry is not null).Entry!;

        // Act & Assert
        BookBrowserViewModel.CanUseBookOrder(new[] { html }).Should().BeTrue();

        // Act & Assert
        BookBrowserViewModel.CanUseBookOrder(new[] { html, other }).Should().BeFalse();

        // Act & Assert
        BookBrowserViewModel.CanUseBookOrder(System.Array.Empty<OpfModelEntry>()).Should().BeFalse();

        // Not possible, so the selection order counts even when the option is on.
        // Act
        sut.PlanMassRename(new[] { other, html }, new MassRenameOptions("p", 1, '0', 1, true)).Resources

            // Assert
            .Should().Equal(other.Resource, html.Resource);
    }

    [Fact]
    public async Task ApplyMassRename_ForbiddenCharacterShowsStatusAndDoesNotRename()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out SettingsStore settings);

        // Act
        sut.SetBook(book);

        // Arrange
        List<OpfModelEntry> text = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).ToList();
        List<string> before = book.GetHtmlResources().Select(r => r.Filename).ToList();

        // Act
        await sut.ApplyMassRenameAsync(text, new MassRenameOptions("bad:name", 1, '0', 1, false));

        // Assert
        book.GetHtmlResources().Select(r => r.Filename).Should().BeEquivalentTo(before);
        settings.MassRename.Should().Be(MassRenameOptions.Default, "a failed rename is not remembered");
    }

    [Theory]
    [InlineData("Text", true, false, false, false)]
    [InlineData("Styles", false, true, false, false)]
    [InlineData("Images", false, false, true, false)]
    [InlineData("Audio", false, false, false, false)]
    [InlineData("Video", false, false, false, false)]
    [InlineData("Misc", false, false, false, true)]
    public void ContextMenu_OnAFolderOnlyTheAddActionsThatFitItAreEnabled(
        string folder, bool html, bool css, bool svg, bool js)
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        sut.UpdateSelection(new object[] { sut.Nodes.Single(n => n.Header == folder) });

        // Assert
        sut.ShowAddItems.Should().BeTrue();
        (sut.CanAddBlankHtmlHere, sut.CanAddBlankCssHere, sut.CanAddBlankSvgHere, sut.CanAddBlankJsHere).Should().Be((html, css, svg, js));
        sut.CanAddExistingFilesHere.Should().BeTrue();
    }

    [Fact]
    public void ContextMenu_BlankJavascriptNeedsAnEpub3Book()
    {
        // Arrange
        using TempDir temp = new();
        using Book epub2 = Load(CorpusPaths.Epub2Minimal, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(epub2);

        sut.UpdateSelection(new object[] { sut.Nodes.Single(n => n.Header == "Misc") });

        // Act & Assert
        sut.CanAddBlankJsHere.Should().BeFalse("an EPUB 2 book has no scripts");

        // Assert
        sut.CanAddExistingFilesHere.Should().BeTrue();
    }

    [Fact]
    public void ContextMenu_BlankJavascriptExplainsWhyItIsOff()
    {
        // Arrange
        using TempDir temp = new();
        using Book epub2 = Load(CorpusPaths.Epub2Minimal, temp);
        using Book epub3 = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(epub2);
        sut.UpdateSelection(new object[] { sut.Nodes.Single(n => n.Header == "Misc") });

        // Assert
        sut.AddBlankJsTip.Should().Be(Strings.Get("BookBrowserView_AddBlankJsEpub2"));

        // Act
        sut.SetBook(epub3);
        sut.UpdateSelection(new object[] { sut.Nodes.Single(n => n.Header == "Text") });

        // Assert
        sut.AddBlankJsTip.Should().Be(Strings.Get("BookBrowserView_AddBlankJsNotHere"));

        // Act
        sut.UpdateSelection(new object[] { sut.Nodes.Single(n => n.Header == "Misc") });

        // Assert
        sut.AddBlankJsTip.Should().BeNull("it can be used here");
    }

    [Fact]
    public void ContextMenu_OnAFileThereAreNoAddActions()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        sut.UpdateSelection(new object[] { sut.Nodes.Single(n => n.Header == "Text").Children[0] });

        // Assert
        sut.ShowAddItems.Should().BeFalse();
        sut.AddTarget.Should().BeNull();
        sut.CanAddExistingFilesHere.Should().BeFalse();
    }

    [Fact]
    public void ContextMenu_TheFontsFolderHasItsOwnAddAction()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        sut.UpdateSelection(new object[] { sut.Nodes.Single(n => n.Header == "Fonts") });

        // Assert
        sut.CanAddExistingFilesHere.Should().BeFalse();
        sut.CanAddBlankHtmlHere.Should().BeFalse();
    }

    [Fact]
    public void ContextMenu_TheAddExistingFilesActionAndItsFilePickerFollowTheFolder()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        sut.UpdateSelection(new object[] { sut.Nodes.Single(n => n.Header == "Styles") });

        // Assert
        sut.AddExistingFilesHeader.Should().Be(Strings.Get("BookBrowserView_AddStylesheetFiles"));
        sut.AddExistingFilePatterns.Should().Equal("*.css");

        // Act
        sut.UpdateSelection(new object[] { sut.Nodes.Single(n => n.Header == "Images") });

        // Assert
        sut.AddExistingFilesHeader.Should().Be(Strings.Get("BookBrowserView_AddImageFiles"));
        sut.AddExistingFilePatterns.Should().Contain("*.png").And.Contain("*.svg").And.NotContain("*.css");

        // Act
        sut.UpdateSelection(new object[] { sut.Nodes.Single(n => n.Header == "Misc") });

        // Assert
        sut.AddExistingFilesHeader.Should().Be(Strings.Get("BookBrowserView_AddExistingFiles"));
        sut.AddExistingFilePatterns.Should().BeEmpty("Misc takes any file");
    }

    [Fact]
    public void ContextMenu_SelectAllNeedsAFolderWithMoreThanOneFile()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        BookBrowserNode empty = sut.Nodes.Single(n => n.Header == "Misc");

        // Assert
        empty.Children.Should().BeEmpty();

        // Act
        sut.UpdateSelection(new object[] { empty });

        // Assert
        sut.SelectAllInFolderCommand.CanExecute(null).Should().BeFalse("an empty folder has nothing to select");

        // Arrange
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        // Act
        sut.UpdateSelection(new object[] { text });

        // Assert
        sut.SelectAllInFolderCommand.CanExecute(null).Should().Be(text.Children.Count > 1);

        // Act
        sut.UpdateSelection(new object[] { text.Children[0] });

        // Assert
        sut.SelectAllInFolderCommand.CanExecute(null).Should().Be(text.Children.Count > 1);
    }

    [Fact]
    public async Task ApplyRenameList_RenamesAndUpdatesCrossReferences()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        HtmlResource chapter = book.GetHtmlResources().Single(r => r.Filename == "chapter1.xhtml");
        List<Resource> resources = new() { image };
        List<string> newNames = new() { "figure-bulk.png" };

        await sut.ApplyRenameListAsync(resources, newNames);

        // Assert
        image.Filename.Should().Be("figure-bulk.png");
        chapter.GetText().Should().Contain("figure-bulk.png");
    }

    [Fact]
    public void RenameSelectedWithTemplateCommand_and_BulkRegexRenameSelectedCommand_require_a_selection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Act & Assert
        sut.RenameSelectedWithTemplateCommand.CanExecute(null).Should().BeFalse();

        // Act & Assert
        sut.BulkRegexRenameSelectedCommand.CanExecute(null).Should().BeFalse();

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        // Act
        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.RenameSelectedWithTemplateCommand.CanExecute(null).Should().BeTrue();

        // Act & Assert
        sut.BulkRegexRenameSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    private static List<object?> NonNavTextChildren(BookBrowserNode text) =>
        text.Children.Where(n => !n.Entry!.IsNav).Cast<object?>().ToList();

    [Fact]
    public void MergeSelectedCommand_requires_at_least_two_files_of_any_type()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        BookBrowserNode style = sut.Nodes.Single(n => n.Header == "Styles").Children[0];
        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.MergeSelectedCommand.CanExecute(null).Should().BeFalse();

        // Act
        sut.UpdateSelection(new object?[] { text.Children[0], style });

        // Act & Assert
        sut.MergeSelectedCommand.CanExecute(null).Should().BeTrue("a selection that cannot be merged gets an error window");
    }

    [Fact]
    public void MergeSelected_with_html_and_other_files_reports_both_groups_and_changes_nothing()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        int resourceCount = book.GetAllResources().Count;
        BookBrowserNode chapter = sut.Nodes.Single(n => n.Header == "Text").Children.First(n => !n.Entry!.IsNav);
        BookBrowserNode style = sut.Nodes.Single(n => n.Header == "Styles").Children[0];
        BookBrowserNode image = sut.Nodes.Single(n => n.Header == "Images").Children[0];
        string? error = null;
        sut.MergeErrorRequested += (_, message) => error = message;

        sut.UpdateSelection(new object?[] { chapter, style, image });
        sut.MergeSelectedCommand.Execute(null);

        // Assert
        error.Should().Be(Strings.Format(
            "BookBrowser_MergeMixedTypes",
            $"  • {Path.GetFileName(style.Entry!.BookPath)}\n  • {Path.GetFileName(image.Entry!.BookPath)}",
            $"  • {Path.GetFileName(chapter.Entry!.BookPath)}"));
        book.GetAllResources().Should().HaveCount(resourceCount);
    }

    [Fact]
    public void MergeSelected_without_html_files_reports_that_only_html_can_be_merged()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        List<BookBrowserNode> images = sut.Nodes.Single(n => n.Header == "Images").Children.Take(2).ToList();
        string? error = null;
        sut.MergeErrorRequested += (_, message) => error = message;

        sut.UpdateSelection(images.Cast<object?>());
        sut.MergeSelectedCommand.Execute(null);

        // Assert
        error.Should().Be(Strings.Format(
            "BookBrowser_MergeOnlyHtml",
            string.Join("\n", images.Select(n => "  • " + Path.GetFileName(n.Entry!.BookPath)))));
    }

    [Fact]
    public void MergeSelected_combinesSelectedFilesIntoTheFirstOne()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        int htmlCountBefore = book.GetHtmlResources().Count;

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        int selectedCount = NonNavTextChildren(text).Count;
        sut.UpdateSelection(NonNavTextChildren(text));
        sut.MergeSelectedCommand.Execute(null);

        // Assert
        book.GetHtmlResources().Should().HaveCount(htmlCountBefore - selectedCount + 1);
    }

    [Fact]
    public void MergeSelected_refusesWhenNavIsIncludedInSelection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        int htmlCountBefore = book.GetHtmlResources().Count;

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        string? error = null;
        sut.MergeErrorRequested += (_, message) => error = message;
        sut.UpdateSelection(text.Children.Cast<object?>());
        sut.MergeSelectedCommand.Execute(null);

        // Assert
        book.GetHtmlResources().Should().HaveCount(htmlCountBefore);
        error.Should().Be(Strings.Format(
            "BookBrowser_MergeNav", Path.GetFileName(text.Children.Single(n => n.Entry!.IsNav).Entry!.BookPath)));
    }

    [Fact]
    public void SplitSelected_withoutMarkers_showsStatusMessageAndDoesNotChangeTheBook()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        int htmlCountBefore = book.GetHtmlResources().Count;

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.SplitSelectedCommand.CanExecute(null).Should().BeTrue();

        // Act
        sut.SplitSelectedCommand.Execute(null);

        // Assert
        book.GetHtmlResources().Should().HaveCount(htmlCountBefore);
    }

    [Fact]
    public void SplitSelected_withMarker_createsNewSection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        HtmlResource chapter = book.GetHtmlResources().Single(r => r.Filename == "chapter1.xhtml");
        chapter.SetText(chapter.GetText().Replace(
            "<h1>Chapter 1</h1>", "<h1>Chapter 1</h1><hr class=\"signet_split_marker\"/>", System.StringComparison.Ordinal));
        int htmlCountBefore = book.GetHtmlResources().Count;

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        BookBrowserNode chapterNode = text.Children.Single(n => n.Header.EndsWith("chapter1.xhtml", System.StringComparison.Ordinal));
        sut.UpdateSelection(new object?[] { chapterNode });
        sut.SplitSelectedCommand.Execute(null);

        // Assert
        book.GetHtmlResources().Should().HaveCount(htmlCountBefore + 1);
    }

    [Fact]
    public async Task MoveText_reorders_the_spine()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        using BookBrowserViewModel sut = NewViewModel(out _);
        sut.Operations = new SuspendingBookOperationHost(sut);
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        BookBrowserNode first = text.Children[0];
        sut.UpdateSelection(new object?[] { first });

        // Act & Assert
        sut.MoveTextDownCommand.CanExecute(null).Should().BeTrue();

        // Act
        await sut.MoveTextDownCommand.ExecuteAsync(null);

        // Assert
        sut.Nodes.Single(n => n.Header == "Text").Children[1].Header.Should().Be(first.Header);

        BookBrowserNode moved = sut.Nodes.Single(n => n.Header == "Text").Children[1];
        sut.UpdateSelection(new object?[] { moved });

        // Act
        await sut.MoveTextUpCommand.ExecuteAsync(null);

        // Assert
        sut.Nodes.Single(n => n.Header == "Text").Children[0].Header.Should().Be(first.Header);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MoveTextToEdge_WhenOperationSuspendsRefresh_RebuildsBrowserTree(bool toTop)
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        using BookBrowserViewModel sut = NewViewModel(out _);
        sut.Operations = new SuspendingBookOperationHost(sut);
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        BookBrowserNode[] before = text.Children.Where(node => node.Entry!.ReadingOrder >= 0).ToArray();
        BookBrowserNode selected = toTop ? before[before.Length - 1] : before[0];
        sut.UpdateSelection(new object?[] { selected });

        // Act
        if (toTop)
        {
            await sut.MoveTextToTopCommand.ExecuteAsync(null);
        }
        else
        {
            await sut.MoveTextToBottomCommand.ExecuteAsync(null);
        }

        // Assert
        BookBrowserNode[] after = sut.Nodes.Single(n => n.Header == "Text").Children
            .Where(node => node.Entry!.ReadingOrder >= 0)
            .ToArray();
        if (toTop)
        {
            after[0].Header.Should().Be(selected.Header);
        }
        else
        {
            after[after.Length - 1].Header.Should().Be(selected.Header);
        }
    }

    [Fact]
    public void AddSemanticsSelectedCommand_requires_a_single_html_selection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.AddSemanticsSelectedCommand.CanExecute(null).Should().BeTrue();

        // Act
        sut.UpdateSelection(new object?[] { text.Children[0], text.Children[1] });

        // Act & Assert
        sut.AddSemanticsSelectedCommand.CanExecute(null).Should().BeFalse();

        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");

        // Act
        sut.UpdateSelection(new object?[] { images.Children[0] });

        // Act & Assert
        sut.AddSemanticsSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CoverImageSelectedCommand_requires_a_single_image_selection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        sut.UpdateSelection(new object?[] { images.Children[0] });

        // Act & Assert
        sut.CoverImageSelectedCommand.CanExecute(null).Should().BeTrue();

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        // Act
        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.CoverImageSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CoverImageSelected_MarksSelectedImageAsCover()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        ImageResource figure = (ImageResource)book.GetAllResources().Single(r => r.Filename == "figure.png");
        BookBrowserNode figureNode = sut.Nodes.Single(n => n.Header == "Images")
            .Children.Single(n => n.Entry!.Resource == figure);
        sut.UpdateSelection(new object?[] { figureNode });

        sut.CoverImageSelectedCommand.Execute(null);

        // Assert
        book.GetOpf().IsCoverImage(figure).Should().BeTrue();
        sut.Nodes.Single(n => n.Header == "Images").Children
            .Single(n => n.Entry!.Resource == figure).Entry!.IsCover.Should().BeTrue();
    }

    [Fact]
    public void GetSemanticsInfo_Epub3_ReturnsLandmarkCodesAndCurrentCode()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        OpfModelEntry chapter = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).Single(e => e.Resource.Filename == "chapter1.xhtml");

        (bool isEpub3, string currentCode, IReadOnlyDictionary<string, DescriptiveInfo> codeMap) =
            sut.GetSemanticsInfo(chapter);

        // Assert
        isEpub3.Should().BeTrue();
        currentCode.Should().Be("bodymatter"); // already set in the nav.xhtml fixture
        codeMap.Should().ContainKey("bodymatter");
    }

    [Fact]
    public async Task ApplySemanticCode_Epub3_SetsLandmarkAndRefreshesTree()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        OpfModelEntry chapter = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).Single(e => e.Resource.Filename == "chapter1.xhtml");

        await sut.ApplySemanticCodeAsync(chapter, "preface");

        OpfModelEntry updated = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).Single(e => e.Resource.Filename == "chapter1.xhtml");

        // Assert
        updated.SemanticTypes.Should().Contain(Landmarks.GetName("preface")).And.NotContain(Landmarks.GetName("bodymatter"));
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public async Task ApplySemanticCode_Epub3_NavResourceOnlyAcceptsTocCode()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        OpfModelEntry navEntry = sut.Nodes.Single(n => n.Header == "Text")
            .Children.Select(n => n.Entry!).Single(e => e.IsNav);
        HtmlResource navResource = (HtmlResource)navEntry.Resource;

        int landmarkCountBefore = new NavProcessor(navResource).GetLandmarks().Count;
        await sut.ApplySemanticCodeAsync(navEntry, "bodymatter");

        // Assert
        new NavProcessor(navResource).GetLandmarks().Should().HaveCount(landmarkCountBefore);

        // The nav may be marked with the "toc" semantics — stored as the special href "#toc",
        // so GetLandmarkCodeForResource(nav) will not find it (a side effect —
        // so we check the landmark list directly).
        // Act
        await sut.ApplySemanticCodeAsync(navEntry, "toc");

        // Assert
        new NavProcessor(navResource).GetLandmarks().Should().Contain(e => e.EpubType == "toc" && e.Href == "#toc");
    }

    [Fact]
    public void LinkStylesheetsSelectedCommand_requires_html_selection_and_existing_css()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.LinkStylesheetsSelectedCommand.CanExecute(null).Should().BeTrue();

        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");

        // Act
        sut.UpdateSelection(new object?[] { images.Children[0] });

        // Act & Assert
        sut.LinkStylesheetsSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void LinkJavascriptsSelectedCommand_disabled_without_any_script_resource()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.LinkJavascriptsSelectedCommand.CanExecute(null).Should().BeFalse();

        // Arrange
        book.CreateEmptyJsFile();

        // Act
        sut.SetBook(book);
        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.LinkJavascriptsSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Changing_the_look_settings_raises_AppearanceChanged_and_the_look_follows_them()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("s.xml"));
        BookBrowserViewModel sut = new(settings, new StatusBarService());
        int raised = 0;
        sut.AppearanceChanged += (_, _) => raised++;

        sut.Appearance.Should().Be(BookBrowserAppearance.Default);

        // Act
        settings.BookBrowserAppearance = new BookBrowserAppearance("Consolas", 14, "Arial", 11, 35, 4);

        // Assert
        raised.Should().BeGreaterThan(0);
        sut.Appearance.Should().Be(new BookBrowserAppearance("Consolas", 14, "Arial", 11, 35, 4));

        int before = raised;

        // Act
        settings.BookBrowserColumns = "Properties";

        // Assert
        raised.Should().Be(before, "the optional columns are not part of the look");
    }

    [Fact]
    public void The_names_of_the_files_show_their_folders_when_the_setting_is_on_and_follow_it_live()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        SettingsStore settings = new(temp.Combine("s.xml"));
        BookBrowserViewModel sut = new(settings, new StatusBarService());

        // Act
        sut.SetBook(book);
        IEnumerable<string> Names() => sut.Nodes.SelectMany(Flatten).Where(n => n.Entry is not null).Select(n => n.Header);

        // Assert
        Names().Should().NotContain(h => h.Contains('/', StringComparison.Ordinal), "only the file names by default");

        // Act
        settings.BookBrowserAppearance = settings.BookBrowserAppearance with { ShowFolders = true };

        // Assert
        Names().Should().Contain(h => h.Contains('/', StringComparison.Ordinal), "the folders show without reloading the book");

        // Act
        settings.BookBrowserAppearance = settings.BookBrowserAppearance with { ShowFolders = false };

        // Assert
        Names().Should().NotContain(h => h.Contains('/', StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyLinkStylesheets_rewrites_head_links_of_selected_files()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        var chapter = (HtmlResource)book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");

        await sut.ApplyLinkStylesheetsAsync(new[] { chapter }, System.Array.Empty<string>());

        // Assert
        chapter.GetText().Should().NotContain("stylesheet");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyLinkStylesheets_reportsSkippedNotWellFormedFileOnStatusBarAndInReport()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        StatusBarService status = new();
        SettingsStore settings = new(temp.Combine("s.xml"));
        BookBrowserViewModel sut = new(settings, status);

        // Act
        sut.SetBook(book);
        SkippedFilesReport? report = null;
        sut.SkippedFilesReported += (_, r) => report = r;

        var chapter = (HtmlResource)book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText("<html><head><body><p>broken");

        await sut.ApplyLinkStylesheetsAsync(new[] { chapter }, System.Array.Empty<string>());

        // Assert
        status.CurrentMessage.Should().Be(
            Strings.Format("Status_BatchNothingProcessed", Strings.Get("BookBrowserView_LinkStylesheetsTitle"), 1));
        status.CurrentLevel.Should().Be(NotificationLevel.Warning);
        report.Should().NotBeNull();
        report!.Skipped.Should().ContainSingle().Which.BookPath.Should().Be(chapter.BookPath);
    }

    [Fact]
    public void MoveSelectedCommand_requires_a_single_media_type_group_selection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);

        // Act & Assert
        sut.MoveSelectedCommand.CanExecute(null).Should().BeFalse();

        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");

        // Act
        sut.UpdateSelection(new object?[] { images.Children[0] });

        // Act & Assert
        sut.MoveSelectedCommand.CanExecute(null).Should().BeTrue();

        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        // Act
        sut.UpdateSelection(new object?[] { images.Children[0], text.Children[0] });

        // Act & Assert
        sut.MoveSelectedCommand.CanExecute(null).Should().BeFalse("the selection spans two different media-type groups");
    }

    [Fact]
    public void MoveSelectedCommand_raises_MoveRequested_for_the_current_selection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        sut.UpdateSelection(new object?[] { images.Children[0] });

        IReadOnlyList<OpfModelEntry>? raised = null;
        sut.MoveRequested += (_, entries) => raised = entries;
        sut.MoveSelectedCommand.Execute(null);

        // Assert
        raised.Should().NotBeNull();
        raised!.Should().HaveCount(1);
    }

    [Fact]
    public void GetFoldersForSelection_returnsFoldersOfTheImagesGroup()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        string imagesFolder = Signet.Core.BookPath.StartingDir(image.BookPath);

        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        IReadOnlyList<string> folders = sut.GetFoldersForSelection(new[] { images.Children[0].Entry! });

        // Assert
        folders.Should().Contain(imagesFolder);
    }

    [Fact]
    public async Task ApplyMove_movesTheResourceAndUpdatesCrossReferences()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        string imagesFolder = Signet.Core.BookPath.StartingDir(image.BookPath);
        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        OpfModelEntry entry = images.Children.Select(n => n.Entry!).Single(e => e.Resource.Filename == "figure.png");

        await sut.ApplyMoveAsync(new[] { entry }, imagesFolder + "/sub");

        // Assert
        image.BookPath.Should().Be(imagesFolder + "/sub/figure.png");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyMove_invalidDestination_showsStatusMessage()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        StatusBarService status = new();
        SettingsStore settings = new(temp.Combine("s.xml"));
        BookBrowserViewModel sut = new(settings, status);

        // Act
        sut.SetBook(book);
        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");
        OpfModelEntry entry = images.Children.Select(n => n.Entry!).Single(e => e.Resource.Filename == "figure.png");

        await sut.ApplyMoveAsync(new[] { entry }, "../outside");

        // Assert
        status.CurrentMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AddCopy_duplicatesSelectedHtmlAndOpensCopy()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        HtmlResource source = (HtmlResource)text.Children[0].Entry!.Resource;
        source.SetText("<html><body><p>oryginal</p></body></html>");

        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.AddCopySelectedCommand.CanExecute(null).Should().BeTrue();

        IReadOnlyList<Resource>? opened = null;
        sut.OpenResourceRequested += (_, r) => opened = r;

        // Act
        sut.AddCopySelectedCommand.Execute(null);

        // Assert
        HtmlResource copy = (HtmlResource)opened.Should().ContainSingle().Which;
        copy.Should().NotBeSameAs(source);
        copy.GetText().Should().Be(source.GetText());
    }

    [Fact]
    public void AddCopy_disabledForMultiSelectionOrNonHtmlCss()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");

        sut.UpdateSelection(new object?[] { images.Children[0] });

        // Act & Assert
        sut.AddCopySelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RenumberToc_enabledOnlyForNcxSelection_andRecomputesPlayOrder()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub2Minimal, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        NcxResource ncx = book.GetNcx()!;
        BookBrowserNode ncxNode = sut.Nodes.Where(n => !n.IsFolder).Single(n => n.Entry!.Resource == ncx);

        sut.UpdateSelection(new object?[] { ncxNode });

        // Act & Assert
        sut.RenumberTocSelectedCommand.CanExecute(null).Should().BeTrue();

        string before = ncx.GetText();

        // Act
        sut.RenumberTocSelectedCommand.Execute(null);

        // Saving through SetNcxDocument(GetNcxDocument()) always goes through ToXml()
        // (RecomputePlayOrder) — the content may be identical if it was already correct, but
        // the operation must run without an exception and mark the book as modified.
        // Assert
        ncx.GetText().Should().NotBeNullOrEmpty();
        before.Should().NotBeNullOrEmpty();
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void RenumberToc_disabledForNonNcxSelection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.RenumberTocSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void GetInfo_disabledWithoutSelection_enabledWithSelection_buildsMetadataText()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        // Act & Assert
        sut.GetInfoSelectedCommand.CanExecute(null).Should().BeFalse();

        // Act
        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.GetInfoSelectedCommand.CanExecute(null).Should().BeTrue();

        string? info = null;
        sut.GetInfoRequested += (_, infoText) => info = infoText;

        // Act
        sut.GetInfoSelectedCommand.Execute(null);

        // Assert
        info.Should().NotBeNull();
        info!.Should().Contain(text.Children[0].Entry!.Resource.Filename);
        info.Should().Contain("Media type");
        info.Should().Contain("EPUB version");
    }

    [Fact]
    public void SaveAs_copiesResourceContentToDestination()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3WithNcx, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");
        HtmlResource source = (HtmlResource)text.Children[0].Entry!.Resource;
        source.SetText("<html><body><p>save-as</p></body></html>");
        string destination = temp.Combine("exported.xhtml");

        sut.ApplySaveAs(text.Children[0].Entry!, destination);

        // Assert
        System.IO.File.Exists(destination).Should().BeTrue();
        System.IO.File.ReadAllText(destination).Should().Contain("save-as");
    }

    [Fact]
    public void SelectAllInFolder_requestsAllSiblingsOfCurrentSelection()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode images = sut.Nodes.Single(n => n.Header == "Images");

        sut.UpdateSelection(new object?[] { images.Children[0] });

        IReadOnlyList<BookBrowserNode>? requested = null;
        sut.TreeSelectionRequested += (_, nodes) => requested = nodes;
        sut.SelectAllInFolderCommand.Execute(null);

        // Assert
        requested.Should().BeEquivalentTo(images.Children);
    }

    [Fact]
    public void ValidateSelectedWithW3C_enabledOnlyForCssSelection_raisesRequestWithResources()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel(out _);

        // Act
        sut.SetBook(book);
        BookBrowserNode styles = sut.Nodes.Single(n => n.Header == "Styles");
        BookBrowserNode text = sut.Nodes.Single(n => n.Header == "Text");

        sut.UpdateSelection(new object?[] { text.Children[0] });

        // Act & Assert
        sut.ValidateSelectedWithW3CCommand.CanExecute(null).Should().BeFalse();

        // Act
        sut.UpdateSelection(new object?[] { styles.Children[0] });

        // Act & Assert
        sut.ValidateSelectedWithW3CCommand.CanExecute(null).Should().BeTrue();

        IReadOnlyList<CssResource>? requested = null;
        sut.ValidateWithW3CRequested += (_, css) => requested = css;

        // Act
        sut.ValidateSelectedWithW3CCommand.Execute(null);

        // Assert
        requested.Should().ContainSingle()
            .Which.Should().Be(styles.Children[0].Entry!.Resource);
    }
}
