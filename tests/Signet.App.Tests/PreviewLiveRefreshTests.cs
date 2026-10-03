using System;
using System.IO;
using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Live preview: edits in Code View reach the preview mirror from the tabs' working text, without
/// saving the tab to its resource.
/// </summary>
public sealed class PreviewLiveRefreshTests
{
    private sealed class Session : IDisposable
    {
        public Session(TempDir temp)
        {
            string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
            Vm = new MainWindowViewModel();
            Vm.LoadBook(new ImportEpub(epub).GetBook(), epub);
            Vm.Preview.DebounceInterval = TimeSpan.Zero;
            Vm.Preview.ReloadRequested += (_, _) => Reloads++;
        }

        public MainWindowViewModel Vm { get; }

        public int Reloads { get; private set; }

        public Book Book => ((IBookWorkspace)Vm).CurrentBook!;

        public HtmlResource Html => Book.GetHtmlResources().First();

        public CssResource Css => Book.GetAllResources().OfType<CssResource>().First();

        public CodeTabViewModel Open(Resource resource)
        {
            Vm.Tabs.OpenResource(resource);
            return Vm.ActiveCodeTab!;
        }

        public string Mirrored(Resource resource) => File.ReadAllText(Path.Combine(
            Vm.Preview.MirrorRootPath!, resource.BookPath.Replace('/', Path.DirectorySeparatorChar)));

        public void Dispose()
        {
            Vm.Preview.Dispose();
            Vm.CheckpointHistory.Dispose();
        }
    }

    private static void AppendParagraph(CodeTabViewModel tab, string text) =>
        tab.Document.Text = tab.Document.Text.Replace("</body>", $"<p>{text}</p></body>", StringComparison.Ordinal);

    [Fact]
    public void Loading_a_book_shows_the_initially_opened_chapter_in_preview()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel vm = new();
        using PreviewViewModel _ = vm.Preview;

        vm.LoadBook(new ImportEpub(epub).GetBook(), epub);

        vm.ActiveTab.Should().NotBeNull("BookBrowser.SetBook opens the first chapter");
        vm.Preview.CurrentBookPath.Should().Be(vm.ActiveTab!.Resource!.BookPath);
        vm.CheckpointHistory.Dispose();
    }

    [Fact]
    public void Live_refresh_delay_comes_from_settings_default_1000_ms()
    {
        MainWindowViewModel vm = new();
        using PreviewViewModel _ = vm.Preview;

        vm.Preview.DebounceInterval.Should().Be(TimeSpan.FromMilliseconds(1000));
    }

    [Fact]
    public void Preview_highlight_comes_from_settings()
    {
        MainWindowViewModel vm = new();
        using PreviewViewModel _ = vm.Preview;

        vm.Preview.Highlight.Should().Be(PreviewHighlight.Default);
    }

    [Theory]
    [AutoData]
    public void Typing_in_html_tab_updates_mirror_without_saving_the_tab(string marker)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.Open(s.Html);
        string resourceBefore = s.Html.GetText();
        int reloadsBefore = s.Reloads;

        AppendParagraph(tab, marker);

        s.Reloads.Should().BeGreaterThan(reloadsBefore);
        s.Mirrored(s.Html).Should().Contain(marker);
        tab.IsModified.Should().BeTrue("the preview does not save the tab");
        tab.CanUndo.Should().BeTrue();
        s.Html.GetText().Should().Be(resourceBefore, "the resource changes only when the tab is saved");
    }

    [Theory]
    [AutoData]
    public void Editing_css_tab_refreshes_previously_shown_html_with_new_stylesheet(string marker)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        s.Open(s.Html);
        CodeTabViewModel css = s.Open(s.Css);
        string cssBefore = s.Css.GetText();
        int reloadsBefore = s.Reloads;

        css.Document.Text += $"\n/* {marker} */\n";

        s.Reloads.Should().BeGreaterThan(reloadsBefore);
        s.Vm.Preview.CurrentBookPath.Should().Be(s.Html.BookPath, "a CSS tab does not change the HTML being shown");
        s.Mirrored(s.Css).Should().Contain(marker);
        s.Css.GetText().Should().Be(cssBefore);
    }

    [Fact]
    public void Caret_moves_in_css_tab_do_not_scroll_the_html_preview()
    {
        using TempDir temp = new();
        using Session s = new(temp);
        s.Open(s.Html);
        CodeTabViewModel css = s.Open(s.Css);
        bool scrolled = false;
        s.Vm.Preview.ScrollToLocRequested += (_, _) => scrolled = true;

        css.UpdateCaret(1, 1, css.DocumentText.Length, 0);

        scrolled.Should().BeFalse("an offset in a CSS stylesheet does not correspond to any location in the HTML page");
    }

    [Fact]
    public void Caret_moves_in_shown_html_tab_scroll_the_preview()
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel html = s.Open(s.Html);
        bool scrolled = false;
        s.Vm.Preview.ScrollToLocRequested += (_, _) => scrolled = true;

        html.UpdateCaret(1, 1, html.DocumentText.Length, 0);

        scrolled.Should().BeTrue();
    }

    [Theory]
    [AutoData]
    public void Unsaved_edit_in_inactive_tab_is_visible_in_preview(string cssMarker, string htmlMarker)
    {
        // Switching tabs saves the tab being left, so unsaved text in an inactive tab comes from
        // an edit "on the side" (e.g. Find & Replace across multiple tabs).
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel css = s.Open(s.Css);
        CodeTabViewModel html = s.Open(s.Html);
        css.Document.Text += $"\n/* {cssMarker} */\n";

        AppendParagraph(html, htmlMarker);

        css.IsModified.Should().BeTrue();
        s.Css.GetText().Should().NotContain(cssMarker);
        s.Mirrored(s.Css).Should().Contain(cssMarker);
        s.Mirrored(s.Html).Should().Contain(htmlMarker);
    }

    [Theory]
    [AutoData]
    public void Saving_open_tabs_keeps_the_edit_in_the_mirror(string marker)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.Open(s.Html);
        AppendParagraph(tab, marker);

        ((IBookWorkspace)s.Vm).SaveOpenTabs();

        tab.IsModified.Should().BeFalse();
        s.Html.GetText().Should().Contain(marker);
        s.Mirrored(s.Html).Should().Contain(marker);
    }

    [Theory]
    [AutoData]
    public void Not_well_formed_edit_keeps_last_good_preview_until_source_is_fixed(string good, string fixedText)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.Open(s.Html);
        AppendParagraph(tab, good);
        string lastGood = s.Mirrored(s.Html);
        int reloadsBefore = s.Reloads;

        // While typing: an unclosed tag, so not well-formed.
        tab.Document.Text = tab.Document.Text.Replace("</body>", "<p</body>", StringComparison.Ordinal);

        tab.IsWellFormed.Should().BeFalse();
        s.Reloads.Should().Be(reloadsBefore, "the preview stays at the last valid version");
        s.Mirrored(s.Html).Should().Be(lastGood);

        tab.Document.Text = tab.Document.Text.Replace("<p</body>", $"<p>{fixedText}</p></body>", StringComparison.Ordinal);

        s.Reloads.Should().BeGreaterThan(reloadsBefore);
        s.Mirrored(s.Html).Should().Contain(fixedText);
    }

    [Theory]
    [AutoData]
    public void Explicit_refresh_shows_not_well_formed_source_anyway(string marker)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.Open(s.Html);
        tab.Document.Text = tab.Document.Text.Replace("</body>", $"<p>{marker}</body>", StringComparison.Ordinal);
        int reloadsBefore = s.Reloads;

        s.Vm.Preview.Refresh();

        s.Reloads.Should().Be(reloadsBefore + 1);
        s.Mirrored(s.Html).Should().Contain(marker);
    }
}
