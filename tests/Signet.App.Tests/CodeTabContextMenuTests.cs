using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Code View context menu logic in <see cref="CodeTabViewModel"/>, tested without the control
/// and with the host replaced by a mock.
/// </summary>
public sealed class CodeTabContextMenuTests
{
    private static Book Load(TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();

    private static CodeTabViewModel NewTab(Resource resource, ICodeTabHost? host = null)
    {
        OpenTab tab = new TabManagerModel().OpenResource(resource);
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        return new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = host };
    }

    [Fact]
    public void Html_tab_offers_reformat_html_and_css_tab_offers_reformat_css()
    {
        using TempDir temp = new();
        using Book book = Load(temp);

        CodeTabViewModel html = NewTab(book.GetAllResources().OfType<HtmlResource>().First());
        CodeTabViewModel css = NewTab(book.GetAllResources().OfType<CssResource>().First());

        (html.IsHtmlFlow, html.IsCss).Should().Be((true, false));
        (css.IsHtmlFlow, css.IsCss).Should().Be((false, true));
    }

    /// <summary>"Rename Class…" is offered only in XHTML, when the caret is on a class name.</summary>
    [Fact]
    public void Class_at_caret_for_rename_is_offered_only_on_a_class_name_in_xhtml()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel html = NewTab(book.GetAllResources().OfType<HtmlResource>().First());
        html.Document.Text = "<html><body><p class=\"c1\">c1</p></body></html>";
        const string beforeName = "<html><body><p class=\"c";

        html.UpdateCaret(1, beforeName.Length + 1, beforeName.Length, -1);
        html.ClassAtCaretForRename().Should().BeEquivalentTo(new Signet.Core.BookManipulation.ClassAtCaret("c1", ["html", "body", "p"]));

        html.UpdateCaret(1, 30, 29, -1); // element content
        html.ClassAtCaretForRename().Should().BeNull();

        CodeTabViewModel css = NewTab(book.GetAllResources().OfType<CssResource>().First());
        css.Document.Text = "<p class=\"c1\"/>";
        css.UpdateCaret(1, 11, 10, -1);
        css.ClassAtCaretForRename().Should().BeNull();
    }

    /// <summary>
    /// The "Rename Class" action (keymap shortcut) asks the view for the dialog only when the caret is on a class name.
    /// </summary>
    [Fact]
    public void Rename_class_action_requests_the_dialog_only_on_a_class_name()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel html = NewTab(book.GetAllResources().OfType<HtmlResource>().First());
        html.Document.Text = "<html><body><p class=\"c1\">c1</p></body></html>";
        const string beforeName = "<html><body><p class=\"c";
        int requests = 0;
        html.RenameClassRequested += () => requests++;

        html.UpdateCaret(1, 30, 29, -1); // element content
        html.RequestRenameClassAtCaret().Should().BeFalse();
        requests.Should().Be(0);

        html.UpdateCaret(1, beforeName.Length + 1, beforeName.Length, -1);
        html.RequestRenameClassAtCaret().Should().BeTrue();
        requests.Should().Be(1);
    }

    /// <summary>"Rename Class…" from a selector: in a CSS stylesheet and in a &lt;style&gt; block of an XHTML file.</summary>
    [Fact]
    public void Style_class_at_caret_for_rename_is_offered_in_stylesheets_and_style_blocks()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel css = NewTab(book.GetAllResources().OfType<CssResource>().First());
        css.Document.Text = "p.note { color: red }";
        css.UpdateCaret(1, 5, 4, -1);

        css.StyleClassAtCaretForRename().Should().Be(new Signet.Core.BookManipulation.StyleClassAtCaret("note", css.ResourceBookPath, -1));

        css.UpdateCaret(1, 13, 12, -1); // declaration
        css.StyleClassAtCaretForRename().Should().BeNull();

        CodeTabViewModel html = NewTab(book.GetAllResources().OfType<HtmlResource>().First());
        html.Document.Text = "<html><head><style>.a { }</style></head><body/></html>";
        html.UpdateCaret(1, 22, 21, -1);
        html.StyleClassAtCaretForRename().Should().Be(new Signet.Core.BookManipulation.StyleClassAtCaret("a", html.ResourceBookPath, 0));
    }

    [Fact]
    public void Toggle_line_wrap_mode_changes_only_this_tab()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewTab(book.GetAllResources().OfType<HtmlResource>().First());
        bool before = sut.WordWrap;

        sut.ToggleLineWrapMode();

        sut.WordWrap.Should().Be(!before);
    }

    [Fact]
    public void Selected_text_and_unmark_offer_follow_the_selection()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewTab(book.GetAllResources().OfType<HtmlResource>().First());
        sut.Document.Text = "<p>hello world</p>";

        sut.UpdateSelection(3, 8);
        (sut.HasSelection, sut.SelectedText).Should().Be((true, "hello"));
        sut.ToggleMarkSelection();

        sut.UpdateSelection(4, 4);
        sut.HasSelection.Should().BeFalse();
        sut.OffersUnmark.Should().BeTrue();
    }

    [Fact]
    public void Image_under_the_caret_resolves_to_a_book_path_and_opens_its_tab()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        var host = new Mock<ICodeTabHost>();
        CodeTabViewModel sut = NewTab(html, host.Object);
        const string before = "<html><body><img s";
        sut.Document.Text = "<html><body><img src=\"../images/cover.png\"/></body></html>";
        sut.UpdateCaret(1, before.Length + 1, before.Length, -1);
        string? opened = null;
        sut.LinkJumpRequested += r => opened = r;

        sut.ImageSourceAtCaret().Should().Be("../images/cover.png");
        sut.ViewImageAtCaret();
        sut.OpenImageTabAtCaret();

        string expectedPath = LinkReference.ResolveBookPath("../images/cover.png", html.Folder)!;
        host.Verify(h => h.ViewImage(expectedPath), Times.Once);
        opened.Should().Be("../images/cover.png");
    }

    [Fact]
    public void Reformat_css_switches_between_single_and_multiple_line_format()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewTab(book.GetAllResources().OfType<CssResource>().First());
        sut.Document.Text = "p { color: red; margin: 0 }";

        sut.ReformatCss(multipleLineFormat: true);
        string multi = sut.Document.Text;
        sut.ReformatCss(multipleLineFormat: false);

        multi.Split('\n').Length.Should().BeGreaterThan(2);
        sut.Document.Text.Trim().Split('\n').Should().ContainSingle();
    }

    [Fact]
    public async Task Reformat_html_replaces_the_text_with_the_host_result_as_one_undo_step()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        var host = new Mock<ICodeTabHost>();
        host.Setup(h => h.ReformatHtmlTextAsync(It.IsAny<Resource>(), "<p>a</p>", true)).ReturnsAsync("<p>mended</p>");
        CodeTabViewModel sut = NewTab(book.GetAllResources().OfType<HtmlResource>().First(), host.Object);
        sut.Document.Text = "<p>a</p>";
        sut.Document.UndoStack.ClearAll();

        await sut.ReformatHtmlAsync(toValid: true);

        sut.Document.Text.Should().Be("<p>mended</p>");
        sut.Undo();
        sut.Document.Text.Should().Be("<p>a</p>");
    }
}
