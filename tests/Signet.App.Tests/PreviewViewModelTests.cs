using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
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
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        Uri? navigated = null;
        sut.NavigateRequested += (_, url) => navigated = url;

        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        sut.HasContent.Should().BeTrue();
        navigated.Should().NotBeNull();
        navigated!.IsFile.Should().BeTrue();
        navigated.LocalPath.Should().StartWith(sut.MirrorRootPath);
        File.Exists(navigated.LocalPath).Should().BeTrue("the mirror should contain the written chapter file");
    }

    [Fact]
    public void ShowResource_NonHtmlResource_IsIgnored()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        int navigations = 0;
        sut.NavigateRequested += (_, _) => navigations++;

        sut.SetBook(book);
        sut.ShowResource(book.GetAllResources().First(r => r is CssResource));

        sut.HasContent.Should().BeFalse();
        navigations.Should().Be(0);
    }

    [Fact]
    public void Refresh_ReSyncsMirrorAndRaisesReload()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);

        bool reloaded = false;
        sut.ReloadRequested += (_, _) => reloaded = true;

        const string marker = "<!-- refreshed -->";
        html.SetText(html.GetText() + marker);
        sut.Refresh();

        reloaded.Should().BeTrue();
        string mirrored = Path.Combine(sut.MirrorRootPath!, html.BookPath.Replace('/', Path.DirectorySeparatorChar));
        File.ReadAllText(mirrored).Should().Contain(marker);
    }

    [Fact]
    public void AllowNavigation_BlocksExternalAllowsInternal()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        sut.SetBook(book);
        HtmlResource html = book.GetHtmlResources().First();
        sut.ShowResource(html);

        Uri internalTarget = new(Path.Combine(sut.MirrorRootPath!, html.BookPath.Replace('/', Path.DirectorySeparatorChar)));
        sut.AllowNavigation(internalTarget).Should().BeTrue();
        sut.AllowNavigation(new Uri("https://example.com")).Should().BeFalse();
        sut.AllowNavigation(new Uri("about:blank")).Should().BeTrue();
    }

    [Fact]
    public void Zoom_CommandsClampAndReportPercent()
    {
        using PreviewViewModel sut = New();

        double lastZoom = 0;
        sut.ZoomChanged += (_, z) => lastZoom = z;

        sut.ZoomInCommand.Execute(null);
        sut.ZoomFactor.Should().BeApproximately(1.1, 1e-9);
        lastZoom.Should().BeApproximately(1.1, 1e-9);
        sut.ZoomPercentText.Should().Be("110%");

        sut.ZoomResetCommand.Execute(null);
        sut.ZoomFactor.Should().Be(1.0);

        for (int i = 0; i < 100; i++)
        {
            sut.ZoomOutCommand.Execute(null);
        }

        sut.ZoomFactor.Should().Be(PreviewViewModel.MinZoom);
    }

    [Theory]
    [InlineData("signet-zoom:in", 1.1)]
    [InlineData("signet-zoom:out", 1 / 1.1)]
    public void Zoom_MessagesFromThePage_ChangeThePreviewsOwnZoom(string message, double expected)
    {
        using PreviewViewModel sut = New();
        double lastZoom = 0;
        sut.ZoomChanged += (_, z) => lastZoom = z;

        sut.HandlePreviewMessage(message);

        sut.ZoomFactor.Should().BeApproximately(expected, 1e-9);
        lastZoom.Should().BeApproximately(expected, 1e-9, "the view applies the new zoom to the engine");
    }

    [Fact]
    public void Zoom_ResetMessage_RestoresHundredPercent()
    {
        using PreviewViewModel sut = New();
        sut.SetZoom(2.0);

        sut.HandlePreviewMessage("signet-zoom:reset");

        sut.ZoomFactor.Should().Be(1.0);
    }

    [Theory]
    [InlineData(1.5, 1.5)]
    [InlineData(100.0, PreviewViewModel.MaxZoom)]
    [InlineData(0.01, PreviewViewModel.MinZoom)]
    [InlineData(0.0, 1.0)]
    public void SetZoom_ClampsTheRememberedZoom(double remembered, double expected)
    {
        using PreviewViewModel sut = New();

        sut.SetZoom(remembered);

        sut.ZoomFactor.Should().Be(expected);
    }

    [Fact]
    public void SetBook_Null_ClearsContent()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());
        sut.HasContent.Should().BeTrue();

        sut.SetBook(null);

        sut.HasContent.Should().BeFalse();
        sut.CurrentUrlText.Should().BeEmpty();
    }

    [Fact]
    public void Clear_DropsShownPageAndRaisesClearRequested()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());
        bool cleared = false;
        sut.ClearRequested += (_, _) => cleared = true;

        sut.Clear();

        cleared.Should().BeTrue();
        sut.HasContent.Should().BeFalse();
        sut.CurrentBookPath.Should().BeNull();
        sut.CurrentUrlText.Should().BeEmpty();
        sut.CurrentUrlOrNull().Should().BeNull("a view attached later must not restore the dropped page");
    }

    [Fact]
    public void Clear_ThenShowResource_ShowsThePageAgain()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);
        sut.Clear();
        int navigations = 0;
        sut.NavigateRequested += (_, _) => navigations++;

        sut.ShowResource(html);

        navigations.Should().Be(1);
        sut.CurrentBookPath.Should().Be(html.BookPath);
    }

    [Fact]
    public void NotifyContentChanged_ZeroDebounce_RefreshesAndReflectsEdit()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);

        int reloads = 0;
        sut.ReloadRequested += (_, _) => reloads++;

        const string marker = "<!-- live-edit -->";
        html.SetText(html.GetText() + marker);
        sut.NotifyContentChanged();

        reloads.Should().Be(1);
        string mirrored = Path.Combine(sut.MirrorRootPath!, html.BookPath.Replace('/', Path.DirectorySeparatorChar));
        File.ReadAllText(mirrored).Should().Contain(marker);
    }

    [Fact]
    public void SyncCaretToPreview_RaisesScrollToLocForInstrumentedResource()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);

        int? scrolledLoc = null;
        sut.ScrollToLocRequested += (_, loc) => scrolledLoc = loc;

        // An offset at the end of the file should hit some element (the last loc).
        sut.SyncCaretToPreview(html.GetText().Length);

        scrolledLoc.Should().NotBeNull();
        scrolledLoc!.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void NotifyPageLoaded_AfterRefresh_ScrollsBackToLastCaretLocation()
    {
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
        sut.Refresh();
        scrolled.Should().BeEmpty("scrolling happens only after the page has loaded");

        sut.NotifyPageLoaded();
        sut.NotifyPageLoaded();

        scrolled.Should().ContainSingle("one restore per reload")
            .Which.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void NotifyPageLoaded_WithoutReload_DoesNotScroll()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        HtmlResource html = book.GetHtmlResources().First();
        sut.SetBook(book);
        sut.ShowResource(html);
        sut.SyncCaretToPreview(html.GetText().Length);

        bool scrolled = false;
        sut.ScrollToLocRequested += (_, _) => scrolled = true;
        sut.NotifyPageLoaded();

        scrolled.Should().BeFalse("plain navigation (e.g. switching files) does not restore the caret");
    }

    [Fact]
    public void NotifyPageLoaded_AfterSwitchingFile_ForgetsCaretOfPreviousFile()
    {
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
        sut.Refresh();
        sut.NotifyPageLoaded();

        scrolled.Should().BeFalse();
    }

    [Fact]
    public void SyncCaretToPreview_OffsetBeforeAnyElement_DoesNotRaise()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();

        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        bool raised = false;
        sut.ScrollToLocRequested += (_, _) => raised = true;

        sut.SyncCaretToPreview(0);

        raised.Should().BeFalse();
    }

    [Fact]
    public void SyncCaretToPreview_NotWellFormedSource_ForcesScrollToTopInsteadOfOffset()
    {
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
        sut.SyncCaretToPreview(html.GetText().Length - 1);

        scrolledLoc.Should().NotBeNull();
        scrolledLoc!.Value.Should().Be(0, "with a source that is not well-formed, scrolling is forced to the earliest element instead of computing the nearest loc");
    }

    [Theory]
    [InlineData("signet-loc:42", 42)]
    [InlineData("signet-loc:0", 0)]
    public void HandlePreviewMessage_SignetLoc_RaisesCodeCaretJump(string message, int expected)
    {
        using PreviewViewModel sut = New();

        int? jumped = null;
        sut.CodeCaretJumpRequested += (_, offset) => jumped = offset;

        sut.HandlePreviewMessage(message);

        jumped.Should().Be(expected);
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
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.CodeCaretJumpRequested += (_, _) => raised = true;

        sut.HandlePreviewMessage(message);

        raised.Should().BeFalse();
    }

    [Fact]
    public void Dispose_RemovesMirrorDirectory()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        PreviewViewModel sut = New();

        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());
        string root = sut.MirrorRootPath!;

        sut.Dispose();

        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void RequestPrint_WithoutContent_DoesNothing()
    {
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.PrintRequested += (_, _) => raised = true;

        sut.RequestPrint();

        raised.Should().BeFalse();
    }

    [Fact]
    public void RequestPrint_WithContent_RaisesPrintRequested()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        bool raised = false;
        sut.PrintRequested += (_, _) => raised = true;

        sut.RequestPrint();

        raised.Should().BeTrue();
    }

    [Fact]
    public void RequestPrintPreview_WithoutContent_DoesNothing()
    {
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.PrintToPdfRequested += (_, _) => raised = true;

        sut.RequestPrintPreview();

        raised.Should().BeFalse();
    }

    [Fact]
    public void RequestPrintPreview_WithContent_RaisesPrintToPdfRequestedWithTempPdfPath()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        string? path = null;
        sut.PrintToPdfRequested += (_, p) => path = p;

        sut.RequestPrintPreview();

        path.Should().NotBeNullOrEmpty();
        Path.GetExtension(path).Should().Be(".pdf");
    }

    [Fact]
    public void NotifyPrintPreviewReady_InvokesTheExternalFileOpener()
    {
        using PreviewViewModel sut = New();
        string? opened = null;
        sut.AttachExternalFileOpener(p =>
        {
            opened = p;
            return true;
        });

        sut.NotifyPrintPreviewReady("/tmp/preview.pdf");

        opened.Should().Be("/tmp/preview.pdf");
    }

    [Fact]
    public void InspectCommand_WithoutContent_DoesNothing()
    {
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.InspectorRequested += (_, _) => raised = true;

        sut.InspectCommand.Execute(null);

        raised.Should().BeFalse();
    }

    [Fact]
    public void InspectCommand_WithContent_RaisesInspectorRequested()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        bool raised = false;
        sut.InspectorRequested += (_, _) => raised = true;

        sut.InspectCommand.Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void SelectAllContentCommand_WithoutContent_DoesNothing()
    {
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.SelectAllRequested += (_, _) => raised = true;

        sut.SelectAllContentCommand.Execute(null);

        raised.Should().BeFalse();
    }

    [Fact]
    public void SelectAllContentCommand_WithContent_RaisesSelectAllRequested()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        bool raised = false;
        sut.SelectAllRequested += (_, _) => raised = true;

        sut.SelectAllContentCommand.Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void CopySelectionCommand_WithoutContent_DoesNothing()
    {
        using PreviewViewModel sut = New();

        bool raised = false;
        sut.CopyRequested += (_, _) => raised = true;

        sut.CopySelectionCommand.Execute(null);

        raised.Should().BeFalse();
    }

    [Fact]
    public void CopySelectionCommand_WithContent_RaisesCopyRequested()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(book.GetHtmlResources().First());

        bool raised = false;
        sut.CopyRequested += (_, _) => raised = true;

        sut.CopySelectionCommand.Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void HasCustomCss_FalseWhenNoCustomCssFilesInPrefsFolder()
    {
        using TempDir temp = new();
        using PreviewViewModel sut = new(new StatusBarService(), temp.Path);

        sut.HasCustomCss.Should().BeFalse();
        sut.CycleCustomCssCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CycleCustomCss_OneFile_TogglesBetweenNoneAndThatFile()
    {
        using TempDir temp = new();
        File.WriteAllText(Path.Combine(temp.Path, "custom_preview_style.css"), "body{color:red}");
        using PreviewViewModel sut = new(new StatusBarService(), temp.Path);

        sut.HasCustomCss.Should().BeTrue();

        string? applied = "unset";
        sut.CustomCssRequested += (_, path) => applied = path;

        sut.CycleCustomCssCommand.Execute(null);
        applied.Should().Be(Path.Combine(temp.Path, "custom_preview_style.css"));

        sut.CycleCustomCssCommand.Execute(null);
        applied.Should().BeNull();
    }

    [Fact]
    public void CycleCustomCss_TwoFiles_CyclesThroughBothThenNone()
    {
        using TempDir temp = new();
        string main = Path.Combine(temp.Path, "custom_preview_style.css");
        string alt = Path.Combine(temp.Path, "custom_preview_style_alt.css");
        File.WriteAllText(main, "body{color:red}");
        File.WriteAllText(alt, "body{color:blue}");
        using PreviewViewModel sut = new(new StatusBarService(), temp.Path);

        string? applied = "unset";
        sut.CustomCssRequested += (_, path) => applied = path;

        sut.CycleCustomCssCommand.Execute(null);
        applied.Should().Be(main);

        sut.CycleCustomCssCommand.Execute(null);
        applied.Should().Be(alt);

        sut.CycleCustomCssCommand.Execute(null);
        applied.Should().BeNull();
    }
}
