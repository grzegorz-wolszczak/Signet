using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for the view-less logic of the preview panel, without the native WebView control.</summary>
public sealed class PreviewViewModelTests
{
    private static Book Load(TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();

    private static PreviewViewModel New()
    {
        // TimeSpan.Zero = synchronous live refresh (no timer), which keeps the tests deterministic.
        PreviewViewModel vm = new(new StatusBarService()) { DebounceInterval = TimeSpan.Zero };
        return vm;
    }

    [Fact]
    public void ShowResource_HtmlResource_RaisesNavigateToFileUrlUnderMirror()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        Uri? navigated = null;
        sut.NavigateRequested += (_, url) => navigated = url;

        // Act
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        // Assert
        sut.HasContent.Should().BeTrue();
        navigated.Should().NotBeNull();
        navigated!.IsFile.Should().BeTrue();
        navigated.LocalPath.Should().StartWith(sut.MirrorRootPath);
        File.Exists(navigated.LocalPath).Should().BeTrue("the mirror should contain the written chapter file");
    }

    [Fact]
    public void ShowResource_NonHtmlResource_IsIgnored()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        int navigations = 0;
        sut.NavigateRequested += (_, _) => navigations++;

        // Act
        sut.SetBook(book);
        sut.ShowResource(book.GetAllResources().First(r => r is CssResource));

        // Assert
        sut.HasContent.Should().BeFalse();
        navigations.Should().Be(0);
    }

    [Fact]
    public void Refresh_ReSyncsMirrorAndRaisesReload()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();

        // Act
        sut.SetBook(book);
        sut.ShowResource(html);

        bool reloaded = false;
        sut.ReloadRequested += (_, _) => reloaded = true;

        const string marker = "<!-- refreshed -->";
        html.SetText(html.GetText() + marker);
        sut.Refresh();

        // Assert
        reloaded.Should().BeTrue();
        string mirrored = Path.Combine(sut.MirrorRootPath!, html.BookPath.Replace('/', Path.DirectorySeparatorChar));
        File.ReadAllText(mirrored).Should().Contain(marker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void After_a_rename_of_the_shown_file_a_refresh_navigates_to_its_new_address(bool liveEdit)
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);
        List<Uri> navigated = new();
        bool reloaded = false;
        sut.NavigateRequested += (_, url) => navigated.Add(url);
        sut.ReloadRequested += (_, _) => reloaded = true;

        // Act
        new OpfModel(book).RenameResource(html, "renamed.xhtml", out string? error).Should().BeTrue(error);
        if (liveEdit)
        {
            sut.NotifyContentChanged();
        }
        else
        {
            sut.Refresh(); // e.g. after Save
        }

        // Assert
        reloaded.Should().BeFalse("reloading would load the old address, no longer in the mirror");
        sut.CurrentBookPath.Should().Be(html.BookPath).And.EndWith("renamed.xhtml");
        Uri url = navigated.Should().ContainSingle().Subject;
        url.LocalPath.Should().EndWith("renamed.xhtml");
        File.Exists(url.LocalPath).Should().BeTrue("the mirror holds the renamed file");

        // Further refreshes reload the page as usual.
        // Act
        sut.Refresh();

        // Assert
        reloaded.Should().BeTrue();
    }

    [Fact]
    public void AllowNavigation_BlocksExternalAllowsInternal()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        sut.SetBook(book);
        HtmlResource html = book.GetHtmlResources().First();
        sut.ShowResource(html);

        Uri internalTarget = new(Path.Combine(sut.MirrorRootPath!, html.BookPath.Replace('/', Path.DirectorySeparatorChar)));

        // Act & Assert
        sut.AllowNavigation(internalTarget).Should().BeTrue();
        sut.AllowNavigation(new Uri("https://example.com")).Should().BeFalse();
        sut.AllowNavigation(new Uri("about:blank")).Should().BeTrue();
    }

    [Fact]
    public void Zoom_CommandsClampAndReportPercent()
    {
        // Arrange
        using PreviewViewModel sut = New();

        double lastZoom = 0;
        sut.ZoomChanged += (_, z) => lastZoom = z;

        // Act
        sut.ZoomInCommand.Execute(null);

        // Assert
        sut.ZoomFactor.Should().BeApproximately(1.1, 1e-9);
        lastZoom.Should().BeApproximately(1.1, 1e-9);
        sut.ZoomPercentText.Should().Be("110%");

        // Act
        sut.ZoomResetCommand.Execute(null);

        // Assert
        sut.ZoomFactor.Should().Be(1.0);

        // Act
        for (int i = 0; i < 100; i++)
        {
            sut.ZoomOutCommand.Execute(null);
        }

        // Assert
        sut.ZoomFactor.Should().Be(PreviewViewModel.MinZoom);
    }

    [Theory]
    [InlineData("signet-zoom:in", 1.1)]
    [InlineData("signet-zoom:out", 1 / 1.1)]
    public void Zoom_MessagesFromThePage_ChangeThePreviewsOwnZoom(string message, double expected)
    {
        // Arrange
        using PreviewViewModel sut = New();
        double lastZoom = 0;
        sut.ZoomChanged += (_, z) => lastZoom = z;

        // Act
        sut.HandlePreviewMessage(message);

        // Assert
        sut.ZoomFactor.Should().BeApproximately(expected, 1e-9);
        lastZoom.Should().BeApproximately(expected, 1e-9, "the view applies the new zoom to the engine");
    }

    [Fact]
    public void Zoom_ResetMessage_RestoresHundredPercent()
    {
        // Arrange
        using PreviewViewModel sut = New();
        sut.SetZoom(2.0);

        // Act
        sut.HandlePreviewMessage("signet-zoom:reset");

        // Assert
        sut.ZoomFactor.Should().Be(1.0);
    }

    [Theory]
    [InlineData(1.5, 1.5)]
    [InlineData(100.0, PreviewViewModel.MaxZoom)]
    [InlineData(0.01, PreviewViewModel.MinZoom)]
    [InlineData(0.0, 1.0)]
    public void SetZoom_ClampsTheRememberedZoom(double remembered, double expected)
    {
        // Arrange
        using PreviewViewModel sut = New();

        // Act
        sut.SetZoom(remembered);

        // Assert
        sut.ZoomFactor.Should().Be(expected);
    }

    [Fact]
    public void SetBook_Null_ClearsContent()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        sut.HasContent.Should().BeTrue();

        // Act
        sut.SetBook(null);

        // Assert
        sut.HasContent.Should().BeFalse();
        sut.CurrentUrlText.Should().BeEmpty();
    }

    [Fact]
    public void Clear_DropsShownPageAndRaisesClearRequested()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());
        bool cleared = false;
        sut.ClearRequested += (_, _) => cleared = true;

        // Act
        sut.Clear();

        // Assert
        cleared.Should().BeTrue();
        sut.HasContent.Should().BeFalse();
        sut.CurrentBookPath.Should().BeNull();
        sut.CurrentUrlText.Should().BeEmpty();
        sut.CurrentUrlOrNull().Should().BeNull("a view attached later must not restore the dropped page");
    }

    [Fact]
    public void Clear_ThenShowResource_ShowsThePageAgain()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);
        sut.Clear();
        int navigations = 0;
        sut.NavigateRequested += (_, _) => navigations++;

        // Act
        sut.ShowResource(html);

        // Assert
        navigations.Should().Be(1);
        sut.CurrentBookPath.Should().Be(html.BookPath);
    }

    [Fact]
    public void NotifyContentChanged_ZeroDebounce_RefreshesAndReflectsEdit()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);

        int reloads = 0;
        sut.ReloadRequested += (_, _) => reloads++;

        const string marker = "<!-- live-edit -->";

        // Act
        html.SetText(html.GetText() + marker);
        sut.NotifyContentChanged();

        // Assert
        reloads.Should().Be(1);
        string mirrored = Path.Combine(sut.MirrorRootPath!, html.BookPath.Replace('/', Path.DirectorySeparatorChar));
        File.ReadAllText(mirrored).Should().Contain(marker);
    }

    [Fact]
    public void SyncCaretToPreview_RaisesScrollToLocForInstrumentedResource()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);

        int? scrolledLoc = null;
        sut.ScrollToLocRequested += (_, loc) => scrolledLoc = loc;

        // An offset at the end of the file should hit some element (the last loc).
        // Act
        sut.SyncCaretToPreview(html.GetText().Length);

        // Assert
        scrolledLoc.Should().NotBeNull();
        scrolledLoc!.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void NotifyPageLoaded_AfterRefresh_ScrollsBackToLastCaretLocation()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);
        int caret = html.GetText().Length;
        sut.SyncCaretToPreview(caret);

        List<int> scrolled = new();
        sut.ScrollToLocRequested += (_, loc) => scrolled.Add(loc);

        // Act
        sut.Refresh();

        // Assert
        scrolled.Should().BeEmpty("scrolling happens only after the page has loaded");

        // Act
        sut.NotifyPageLoaded();
        sut.NotifyPageLoaded();

        // Assert
        scrolled.Should().ContainSingle("one restore per reload")
            .Which.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void NotifyPageLoaded_WithoutReload_DoesNotScroll()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);
        sut.SyncCaretToPreview(html.GetText().Length);

        bool scrolled = false;
        sut.ScrollToLocRequested += (_, _) => scrolled = true;

        // Act
        sut.NotifyPageLoaded();

        // Assert
        scrolled.Should().BeFalse("plain navigation (e.g. switching files) does not restore the caret");
    }

    [Fact]
    public void NotifyPageLoaded_AfterSwitchingFile_ForgetsCaretOfPreviousFile()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource[] htmls = book.GetHtmlResources().ToArray();
        sut.SetBook(book);
        sut.ShowResource(htmls[0]);
        sut.SyncCaretToPreview(htmls[0].GetText().Length);
        sut.ShowResource(htmls[1]);

        bool scrolled = false;
        sut.ScrollToLocRequested += (_, _) => scrolled = true;

        // Act
        sut.Refresh();
        sut.NotifyPageLoaded();

        // Assert
        scrolled.Should().BeFalse();
    }

    [Fact]
    public void The_caret_sync_caused_by_a_click_in_the_preview_highlights_without_scrolling()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);
        int offset = html.GetText().Length;
        List<int> scrolled = new();
        List<int> highlighted = new();
        sut.ScrollToLocRequested += (_, loc) => scrolled.Add(loc);
        sut.HighlightLocRequested += (_, loc) => highlighted.Add(loc);
        sut.CodeCaretJumpRequested += (_, jump) => sut.SyncCaretToPreview(jump); // the host moves the caret

        // Act
        sut.HandlePreviewMessage("signet-loc:" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Assert
        scrolled.Should().BeEmpty("the clicked element is already in view — the preview must not jump it to the middle");
        highlighted.Should().ContainSingle().Which.Should().BeGreaterThanOrEqualTo(0);

        // A later caret move to the same place (in Code Editor) scrolls as usual.
        // Act
        sut.SyncCaretToPreview(offset);

        // Assert
        scrolled.Should().ContainSingle();
    }

    [Fact]
    public void A_click_in_the_preview_of_a_not_well_formed_file_does_not_scroll_to_the_top()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        HtmlResource html = book.GetHtmlResources().First();
        html.SetText("<html><body><p>Tom & Jerry</p><p id=\"target\">druga linia</p></body></html>");
        sut.SetBook(book);
        sut.ShowResource(html);
        bool scrolled = false;
        sut.ScrollToLocRequested += (_, _) => scrolled = true;
        sut.CodeCaretJumpRequested += (_, jump) => sut.SyncCaretToPreview(jump);

        // Act
        sut.HandlePreviewMessage("signet-loc:30");

        // Assert
        scrolled.Should().BeFalse();
    }

    [Fact]
    public void SyncCaretToPreview_OffsetBeforeAnyElement_DoesNotRaise()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        bool raised = false;
        sut.ScrollToLocRequested += (_, _) => raised = true;

        // Act
        sut.SyncCaretToPreview(0);

        // Assert
        raised.Should().BeFalse();
    }

    [Fact]
    public void SyncCaretToPreview_NotWellFormedSource_ForcesScrollToTopInsteadOfOffset()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        // An unescaped ampersand -> not well-formed according to WellFormedChecker, but the lenient
        // HTML5 parser (AngleSharp) still instruments the document (Locs is not empty).
        html.SetText("<html><body><p>Tom & Jerry</p><p id=\"target\">druga linia</p></body></html>");
        sut.SetBook(book);
        sut.ShowResource(html);

        int? scrolledLoc = null;
        sut.ScrollToLocRequested += (_, loc) => scrolledLoc = loc;

        // An offset near the end of the file would normally hit the last element; here it must be
        // ignored in favor of a forced scroll to the top.
        // Act
        sut.SyncCaretToPreview(html.GetText().Length - 1);

        // Assert
        scrolledLoc.Should().NotBeNull();
        scrolledLoc!.Value.Should().Be(0, "with a source that is not well-formed, scrolling is forced to the earliest element instead of computing the nearest loc");
    }

    [Theory]
    [InlineData("signet-loc:42", 42)]
    [InlineData("signet-loc:0", 0)]
    public void HandlePreviewMessage_SignetLoc_RaisesCodeCaretJump(string message, int expected)
    {
        // Arrange
        using PreviewViewModel sut = New();

        int? jumped = null;
        sut.CodeCaretJumpRequested += (_, offset) => jumped = offset;

        // Act
        sut.HandlePreviewMessage(message);

        // Assert
        jumped.Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HandlePreviewMessage_FootnoteReference_navigates_to_the_note_and_optionally_moves_code(
        bool followFootnotesInCodeView)
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        HtmlResource note = book.CreateEmptyHtmlFile();
        chapter.SetText("<html><body><p>Text<a epub:type=\"noteref\" href=\"" + note.Filename
            + "#note\" id=\"ref\">[1]</a></p></body></html>");
        note.SetText("<html><body>\n<aside epub:type=\"footnote\" id=\"note\"><p><a href=\"" + chapter.Filename
            + "#ref\">1.</a> Global note.</p></aside>\n</body></html>");
        using PreviewViewModel sut = New();
        sut.FollowFootnotesInCodeView = followFootnotesInCodeView;
        List<Uri> navigated = new();
        int? jumped = null;
        sut.NavigateRequested += (_, url) => navigated.Add(url);
        sut.CodeCaretJumpRequested += (_, offset) => jumped = offset;
        sut.SetBook(book);
        sut.ShowResource(chapter);
        navigated.Clear();

        Uri noteUrl = new UriBuilder(
            new Uri(Path.Combine(sut.MirrorRootPath!, note.BookPath.Replace('/', Path.DirectorySeparatorChar))))
        {
            Fragment = "note",
        }.Uri;
        int expectedOffset = XhtmlDoc.OffsetFromNode(XhtmlDoc.Parse(note.GetText()).GetElementById("note")!);

        // Act
        sut.HandlePreviewMessage("signet-link:-1 " + noteUrl.AbsoluteUri);

        // Assert
        sut.CurrentBookPath.Should().Be(note.BookPath);
        navigated.Should().ContainSingle().Which.Should().Be(noteUrl);
        if (followFootnotesInCodeView)
        {
            jumped.Should().Be(expectedOffset);
        }
        else
        {
            jumped.Should().BeNull();
        }
    }

    [Fact]
    public void HandlePreviewMessage_OrdinaryLink_is_navigated_to_and_moves_code_to_the_clicked_element()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        HtmlResource other = book.CreateEmptyHtmlFile();
        chapter.SetText("<html><body><p><a href=\"" + other.Filename + "#part\">See part</a></p></body></html>");
        other.SetText("<html><body>\n<h2 id=\"part\">Part</h2>\n</body></html>");
        using PreviewViewModel sut = New();
        List<Uri> navigated = new();
        List<int> jumped = new();
        sut.NavigateRequested += (_, url) => navigated.Add(url);
        sut.CodeCaretJumpRequested += (_, offset) => jumped.Add(offset);
        sut.SetBook(book);
        sut.ShowResource(chapter);
        navigated.Clear();
        Uri partUrl = new UriBuilder(
            new Uri(Path.Combine(sut.MirrorRootPath!, other.BookPath.Replace('/', Path.DirectorySeparatorChar))))
        {
            Fragment = "part",
        }.Uri;
        const int clickedOffset = 23;

        // Act
        sut.HandlePreviewMessage("signet-link:" + clickedOffset + " " + partUrl.AbsoluteUri);

        // Assert
        navigated.Should().ContainSingle().Which.Should().Be(partUrl);
        jumped.Should().Equal(clickedOffset);
        sut.CurrentBookPath.Should().Be(chapter.BookPath, "an ordinary link is left to the page, as before");
    }

    [Fact]
    public void After_following_a_footnote_the_note_is_not_shown_again_but_a_later_show_drops_the_fragment()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        HtmlResource note = book.CreateEmptyHtmlFile();
        chapter.SetText("<html><body><p>Text<a epub:type=\"noteref\" href=\"" + note.Filename
            + "#note\" id=\"ref\">[1]</a></p></body></html>");
        note.SetText("<html><body><aside epub:type=\"footnote\" id=\"note\"><p><a href=\"" + chapter.Filename
            + "#ref\">1.</a> Note.</p></aside></body></html>");
        using PreviewViewModel sut = New();
        List<Uri> navigated = new();
        sut.NavigateRequested += (_, url) => navigated.Add(url);
        sut.SetBook(book);
        sut.ShowResource(chapter);
        Uri noteUrl = new UriBuilder(
            new Uri(Path.Combine(sut.MirrorRootPath!, note.BookPath.Replace('/', Path.DirectorySeparatorChar))))
        {
            Fragment = "note",
        }.Uri;
        sut.HandlePreviewMessage("signet-link:-1 " + noteUrl.AbsoluteUri);
        navigated.Clear();

        // Act — the note's tab becomes active because Code View followed the footnote
        sut.ShowResource(note);

        // Assert
        navigated.Should().BeEmpty("the page is already at the note");

        // Act — the same tab shown again later
        sut.ShowResource(note);

        // Assert
        navigated.Should().ContainSingle().Which.Fragment.Should().BeEmpty("the fragment was used up");
    }

    [Fact]
    public void HandlePreviewMessage_FootnoteBacklink_navigates_to_the_reference_and_moves_code()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        HtmlResource note = book.CreateEmptyHtmlFile();
        const string referenceId = "note-ref";
        chapter.SetText(
            "<html><body><h1><a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\""
            + note.Filename + "#note\" id=\"" + referenceId + "\">[1]</a></h1></body></html>");
        note.SetText(
            "<html><body><aside class=\"signet-fn\" epub:type=\"endnote\" id=\"note\">"
            + "<p><a href=\"" + chapter.Filename + "#" + referenceId + "\">1.</a> Note.</p></aside></body></html>");
        using PreviewViewModel sut = New();
        List<Uri> navigated = new();
        int? jumped = null;
        sut.NavigateRequested += (_, url) => navigated.Add(url);
        sut.CodeCaretJumpRequested += (_, offset) => jumped = offset;
        sut.SetBook(book);
        sut.ShowResource(note);
        navigated.Clear();

        Uri referenceUrl = new UriBuilder(
            new Uri(Path.Combine(sut.MirrorRootPath!, chapter.BookPath.Replace('/', Path.DirectorySeparatorChar))))
        {
            Fragment = referenceId,
        }.Uri;
        int expectedOffset = XhtmlDoc.OffsetFromNode(
            XhtmlDoc.Parse(chapter.GetText()).GetElementById(referenceId)!);

        // Act
        sut.HandlePreviewMessage("signet-link:-1 " + referenceUrl.AbsoluteUri);

        // Assert
        sut.CurrentBookPath.Should().Be(chapter.BookPath);
        navigated.Should().ContainSingle().Which.Should().Be(referenceUrl);
        jumped.Should().Be(expectedOffset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("signet-loc:")]
    [InlineData("signet-loc:-3")]
    [InlineData("signet-loc:abc")]
    public void HandlePreviewMessage_Unrecognized_IsIgnored(string? message)
    {
        // Arrange
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.CodeCaretJumpRequested += (_, _) => raised = true;

        // Act
        sut.HandlePreviewMessage(message);

        // Assert
        raised.Should().BeFalse();
    }

    [Fact]
    public void Dispose_RemovesMirrorDirectory()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        PreviewViewModel sut = New();

        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());
        string root = sut.MirrorRootPath!;

        // Act
        sut.Dispose();

        // Assert
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void RequestPrint_WithoutContent_DoesNothing()
    {
        // Arrange
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.PrintRequested += (_, _) => raised = true;

        // Act
        sut.RequestPrint();

        // Assert
        raised.Should().BeFalse();
    }

    [Fact]
    public void RequestPrint_WithContent_RaisesPrintRequested()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        bool raised = false;
        sut.PrintRequested += (_, _) => raised = true;

        // Act
        sut.RequestPrint();

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void RequestPrintPreview_WithoutContent_DoesNothing()
    {
        // Arrange
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.PrintToPdfRequested += (_, _) => raised = true;

        // Act
        sut.RequestPrintPreview();

        // Assert
        raised.Should().BeFalse();
    }

    [Fact]
    public void RequestPrintPreview_WithContent_RaisesPrintToPdfRequestedWithTempPdfPath()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        string? path = null;
        sut.PrintToPdfRequested += (_, p) => path = p;

        // Act
        sut.RequestPrintPreview();

        // Assert
        path.Should().NotBeNullOrEmpty();
        Path.GetExtension(path).Should().Be(".pdf");
    }

    [Fact]
    public void NotifyPrintPreviewReady_InvokesTheExternalFileOpener()
    {
        // Arrange
        using PreviewViewModel sut = New();
        string? opened = null;
        sut.AttachExternalFileOpener(p =>
        {
            opened = p;
            return true;
        });

        // Act
        sut.NotifyPrintPreviewReady("/tmp/preview.pdf");

        // Assert
        opened.Should().Be("/tmp/preview.pdf");
    }

    [Fact]
    public void InspectCommand_WithoutContent_DoesNothing()
    {
        // Arrange
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.InspectorRequested += (_, _) => raised = true;

        // Act
        sut.InspectCommand.Execute(null);

        // Assert
        raised.Should().BeFalse();
    }

    [Fact]
    public void InspectCommand_WithContent_RaisesInspectorRequested()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        bool raised = false;
        sut.InspectorRequested += (_, _) => raised = true;

        // Act
        sut.InspectCommand.Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void SelectAllContentCommand_WithoutContent_DoesNothing()
    {
        // Arrange
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.SelectAllRequested += (_, _) => raised = true;

        // Act
        sut.SelectAllContentCommand.Execute(null);

        // Assert
        raised.Should().BeFalse();
    }

    [Fact]
    public void SelectAllContentCommand_WithContent_RaisesSelectAllRequested()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        bool raised = false;
        sut.SelectAllRequested += (_, _) => raised = true;

        // Act
        sut.SelectAllContentCommand.Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void CopySelectionCommand_WithoutContent_DoesNothing()
    {
        // Arrange
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.CopyRequested += (_, _) => raised = true;

        // Act
        sut.CopySelectionCommand.Execute(null);

        // Assert
        raised.Should().BeFalse();
    }

    [Fact]
    public void CopySelectionCommand_WithContent_RaisesCopyRequested()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        bool raised = false;
        sut.CopyRequested += (_, _) => raised = true;

        // Act
        sut.CopySelectionCommand.Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void HasCustomCss_FalseWhenNoCustomCssFilesInPrefsFolder()
    {
        // Arrange
        using TempDir temp = new();
        using PreviewViewModel sut = new(new StatusBarService(), temp.Path);

        // Act & Assert
        sut.HasCustomCss.Should().BeFalse();
        sut.CycleCustomCssCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CycleCustomCss_OneFile_TogglesBetweenNoneAndThatFile()
    {
        // Arrange
        using TempDir temp = new();
        File.WriteAllText(Path.Combine(temp.Path, "custom_preview_style.css"), "body{color:red}");
        using PreviewViewModel sut = new(new StatusBarService(), temp.Path);

        // Act & Assert
        sut.HasCustomCss.Should().BeTrue();

        string? applied = "unset";
        sut.CustomCssRequested += (_, path) => applied = path;

        // Act
        sut.CycleCustomCssCommand.Execute(null);

        // Assert
        applied.Should().Be(Path.Combine(temp.Path, "custom_preview_style.css"));

        // Act
        sut.CycleCustomCssCommand.Execute(null);

        // Assert
        applied.Should().BeNull();
    }

    [Fact]
    public void CycleCustomCss_TwoFiles_CyclesThroughBothThenNone()
    {
        // Arrange
        using TempDir temp = new();
        string main = Path.Combine(temp.Path, "custom_preview_style.css");
        string alt = Path.Combine(temp.Path, "custom_preview_style_alt.css");
        File.WriteAllText(main, "body{color:red}");
        File.WriteAllText(alt, "body{color:blue}");
        using PreviewViewModel sut = new(new StatusBarService(), temp.Path);

        string? applied = "unset";
        sut.CustomCssRequested += (_, path) => applied = path;

        // Act
        sut.CycleCustomCssCommand.Execute(null);

        // Assert
        applied.Should().Be(main);

        // Act
        sut.CycleCustomCssCommand.Execute(null);

        // Assert
        applied.Should().Be(alt);

        // Act
        sut.CycleCustomCssCommand.Execute(null);

        // Assert
        applied.Should().BeNull();
    }
}
