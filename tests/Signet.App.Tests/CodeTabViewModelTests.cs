using System.Linq;
using AwesomeAssertions;
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
/// Tests for <see cref="CodeTabViewModel"/> without the AvaloniaEdit control: just a
/// <c>TextDocument</c> plus a view-less <see cref="CodeViewModel"/>.
/// </summary>
public sealed class CodeTabViewModelTests
{
    private static Book Load(TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();

    private static CodeTabViewModel NewCodeTab(Book book, out HtmlResource html)
    {
        html = book.GetAllResources().OfType<HtmlResource>().First();
        var model = new TabManagerModel();
        OpenTab tab = model.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        return new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);
    }

    [Theory]
    [InlineData("<a href=\"ch2.xhtml#n\">x</a>", "ch2.xhtml#n", null, null)]
    [InlineData("<a href=\"https://example.com\">x</a>", null, "https://example.com", null)]
    [InlineData("<p class=\"note\">x</p>", null, null, "note")]
    public void Ctrl_click_on_a_link_or_class_raises_the_matching_request(string element, string? link, string? external, string? className)
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.Document.Text = "<html><body>" + element + "</body></html>";
        string? gotLink = null, gotExternal = null, gotClass = null;
        sut.LinkJumpRequested += r => gotLink = r;
        sut.ExternalLinkRequested += r => gotExternal = r;
        sut.CssClassJumpRequested += c => gotClass = c;

        bool handled = sut.RequestLinkOrClassJumpAt("<html><body>".Length + element.IndexOf('"', System.StringComparison.Ordinal) + 2);

        handled.Should().BeTrue();
        (gotLink, gotExternal, gotClass).Should().Be((link, external, className));
    }

    [Fact]
    public void Ctrl_click_on_plain_text_raises_nothing()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.Document.Text = "<html><body><p>text</p></body></html>";

        sut.RequestLinkOrClassJumpAt("<html><body><p>te".Length).Should().BeFalse();
    }

    [Fact]
    public void Link_targets_are_checked_against_the_book_relative_to_the_file_folder()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out HtmlResource html);
        string folder = Signet.Core.BookPath.StartingDir(html.BookPath);
        sut.BookPathExists = p => p == Signet.Core.BookPath.BuildBookPath("ch2.xhtml", folder);

        sut.LinkTargetExists("ch2.xhtml#x").Should().BeTrue();
        sut.LinkTargetExists("ch3.xhtml").Should().BeFalse();
        sut.LinkTargetExists("#only-fragment").Should().BeTrue();
        sut.LinkTargetExists("http://example.com/a.png").Should().BeTrue();
    }

    [Fact]
    public void Loads_resource_content_into_the_document_unmodified()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out HtmlResource html);

        html.InitialLoad();
        sut.Document.Text.Should().Be(html.GetText());
        sut.IsModified.Should().BeFalse();
        sut.Syntax.Should().Be(CodeViewSyntax.Html);
    }

    [Fact]
    public void Editing_the_document_marks_modified_and_save_pushes_to_the_resource()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out HtmlResource html);

        sut.Document.Text = sut.Document.Text.Replace("</body>", "<p>added</p></body>");
        sut.IsModified.Should().BeTrue();

        sut.Save();

        sut.IsModified.Should().BeFalse();
        html.GetText().Should().Contain("<p>added</p>");
    }

    [Fact]
    public void Undo_and_redo_go_through_the_document_undo_stack()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        string original = sut.Document.Text;

        sut.Document.Insert(0, "X");
        sut.CanUndo.Should().BeTrue();

        sut.Undo();
        sut.Document.Text.Should().Be(original);

        sut.Redo();
        sut.Document.Text.Should().Be("X" + original);
    }

    [Fact]
    public void Well_formed_state_tracks_edits()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);

        sut.IsWellFormed.Should().BeTrue();
        sut.WellFormedError.Should().BeNull();

        sut.Document.Text = sut.Document.Text.Replace("</body>", string.Empty);

        sut.IsWellFormed.Should().BeFalse();
        sut.WellFormedError.Should().NotBeNull();
        sut.WellFormedError!.Line.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Missing_doctype_is_a_warning_not_an_error()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.WellFormedWarning.Should().BeNull();

        sut.Document.Text = System.Text.RegularExpressions.Regex.Replace(sut.Document.Text, "<!DOCTYPE[^>]*>", string.Empty);

        sut.IsWellFormed.Should().BeTrue();
        sut.WellFormedError.Should().BeNull();
        sut.WellFormedWarning.Should().NotBeNull();
        sut.WellFormedWarning!.Kind.Should().Be(WellFormedWarningKind.MissingDoctype);
        sut.RunWellFormedCheck().Should().BeTrue();
    }

    [Fact]
    public void RunWellFormedCheck_returns_the_verdict()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);

        sut.RunWellFormedCheck().Should().BeTrue();
    }

    [Fact]
    public void InsertSectionMarker_adds_the_split_marker_through_the_document()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);

        sut.InsertSectionMarker(0);

        sut.Document.Text.Should().StartWith(CodeViewModel.SectionMarker);
        sut.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void GoToLine_requests_a_scroll_clamped_to_the_document()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        int? requested = null;
        sut.ScrollToLineRequested += line => requested = line;

        sut.GoToLine(99999);

        requested.Should().Be(sut.Document.LineCount);
    }

    [Fact]
    public void Tag_highlight_updates_when_the_caret_moves_into_a_tag()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.Document.Text = "<body><p>hi</p></body>";

        sut.UpdateCaret(1, 8, "<body><".Length, 'p');
        sut.TagHighlight.Should().NotBeNull();

        sut.UpdateCaret(1, 11, "<body><p>h".Length, 'i');
        sut.TagHighlight.Should().BeNull();
    }

    [Fact]
    public void Reload_discards_unsaved_edits()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out HtmlResource html);
        html.InitialLoad();
        string original = html.GetText();

        sut.Document.Text = "junk";
        sut.Reload();

        sut.Document.Text.Should().Be(original);
        sut.IsModified.Should().BeFalse();
    }

    [Fact]
    public void Modified_tab_title_gets_a_marker_prefix()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        string caption = sut.Title!;

        sut.Document.Insert(0, "z");

        sut.Title.Should().Be("• " + caption);
    }

    [Fact]
    public void Constructing_a_code_tab_for_a_binary_resource_throws()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        ImageResource image = book.GetAllResources().OfType<ImageResource>().First();
        var model = new TabManagerModel();
        OpenTab tab = model.OpenResource(image);
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();

        var act = () => new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);

        act.Should().Throw<System.ArgumentException>();
    }

    // ---- Format menu ----

    private const string FormatDoc =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head><title>t</title></head>\n<body>\n" +
        "<p>Hello world</p>\n</body>\n</html>\n";

    private static (int Start, int End) Span(string text, string needle)
    {
        int i = text.IndexOf(needle, System.StringComparison.Ordinal);
        return (i, i + needle.Length);
    }

    [Fact]
    public void Bold_wraps_the_selection_and_reports_the_new_selection()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.Document.Text = FormatDoc;
        (int start, int end) = Span(FormatDoc, "world");
        sut.UpdateSelection(start, end);
        int? selStart = null;
        sut.SelectionRequested += (s, _) => selStart = s;

        sut.Bold();

        sut.Document.Text.Should().Contain("<p>Hello <b>world</b></p>");
        selStart.Should().Be(start + "<b>".Length);
    }

    [Fact]
    public void Heading_style_replaces_the_paragraph_and_updates_caret_block_element()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.Document.Text = FormatDoc;
        (int start, int end) = Span(sut.Document.Text, "Hello");
        sut.UpdateSelection(start, end);

        sut.HeadingStyle("h1", preserveAttributes: false);

        sut.Document.Text.Should().Contain("<h1>Hello world</h1>");
        int caret = sut.Document.Text.IndexOf("Hello", System.StringComparison.Ordinal) + 1;
        sut.UpdateCaret(1, 1, caret, 'e');
        sut.CaretBlockElement.Should().Be("h1");
    }

    [Fact]
    public void RemoveTagPairEnabled_tracks_caret_position_and_selection()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.Document.Text = FormatDoc.Replace("<p>Hello world</p>", "<p><em>x</em></p>");

        int insideEm = sut.Document.Text.IndexOf("<em>", System.StringComparison.Ordinal) + 2;
        sut.UpdateSelection(insideEm, insideEm);
        sut.UpdateCaret(1, 1, insideEm, 'e');
        sut.RemoveTagPairEnabled.Should().BeTrue();

        sut.RemoveTagPair();
        sut.Document.Text.Should().Contain("<p>x</p>");
    }

    private static CodeTabViewModel TagTab(Book book, string text, string caretBefore)
    {
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.Document.Text = text;
        int caret = caretBefore.Length;
        sut.UpdateSelection(caret, caret);
        sut.UpdateCaret(1, 1, caret, 'x');
        return sut;
    }

    [Fact]
    public void Tag_structure_actions_select_rename_and_split_the_enclosing_element()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        const string text = "<html><body><p class=\"a\">one two</p></body></html>";
        CodeTabViewModel sut = TagTab(book, text, "<html><body><p class=\"a\">one");
        (int Start, int End)? selected = null;
        sut.SearchResultRequested += (start, end, _) => selected = (start, end);

        sut.SupportsTagStructure.Should().BeTrue();
        sut.EnclosingTagName.Should().Be("p");
        sut.SelectTagContents();
        selected.Should().Be((text.IndexOf("one", System.StringComparison.Ordinal), text.IndexOf("</p>", System.StringComparison.Ordinal)));

        sut.SplitTag();
        sut.Document.Text.Should().Contain("<p class=\"a\">one</p><p class=\"a\"> two</p>");

        sut.UpdateCaret(1, 1, "<html><body><p class=\"a\">o".Length, 'n');
        sut.RenameTag("div");
        sut.Document.Text.Should().Contain("<div class=\"a\">one</div><p class=\"a\"> two</p>");
    }

    [Fact]
    public void Link_completions_offer_book_files_and_anchors_filtered_by_the_query()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out HtmlResource html);
        string folder = Signet.Core.BookPath.StartingDir(html.BookPath);
        sut.BookFiles = () => new[]
        {
            new LinkCompletionFile(html.BookPath, "application/xhtml+xml"),
            new LinkCompletionFile(Signet.Core.BookPath.BuildBookPath("ch2.xhtml", folder), "application/xhtml+xml"),
            new LinkCompletionFile(Signet.Core.BookPath.BuildBookPath("cover.jpg", folder), "image/jpeg"),
        };
        sut.DocumentTextOf = path => path.EndsWith("ch2.xhtml", System.StringComparison.Ordinal)
            ? "<body><h1 id=\"start\">Start</h1></body>" : null;

        sut.Document.Text = "<body><p id=\"here\">x</p><a href=\"c";
        LinkCompletionResult? files = sut.GetLinkCompletions(sut.Document.TextLength);
        files!.Items.Select(i => i.Text).Should().Equal("ch2.xhtml");
        files.ReplaceStart.Should().Be(sut.Document.TextLength - 1);

        sut.Document.Text = "<body><p id=\"here\">x</p><a href=\"ch2.xhtml#";
        sut.GetLinkCompletions(sut.Document.TextLength)!.Items.Select(i => i.Text).Should().Equal("start");

        sut.Document.Text = "<body><p id=\"here\">x</p><a href=\"#h";
        sut.GetLinkCompletions(sut.Document.TextLength)!.Items.Select(i => i.Text).Should().Equal("here");

        sut.Document.Text = "<body><p class=\"c";
        sut.GetLinkCompletions(sut.Document.TextLength).Should().BeNull();
    }

    [Theory]
    [InlineData(true, "<html><body><p>abc</p>\n</body></html>")]
    [InlineData(false, "<html><body><p>abc</\n</body></html>")]
    public void Typing_slash_after_lt_auto_closes_the_element_when_enabled(bool enabled, string expected)
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        settings.CodeViewAutoCloseTags = enabled;
        var sut = new CodeTabViewModel(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);
        sut.Document.Text = "<html><body><p>abc</\n</body></html>";
        int caret = "<html><body><p>abc</".Length;

        int? newCaret = sut.TryAutoCloseTag(caret);

        sut.Document.Text.Should().Be(expected);
        newCaret.Should().Be(enabled ? caret + "p>".Length : null);
    }

    // "cafe" + U+0301 (combining acute) and "nai" + U+0308 (combining diaeresis), built from code points so that no
    // editor can normalize the test data itself.
    private static readonly string DecomposedSample = "cafe" + (char)0x0301 + " nai" + (char)0x0308 + "ve";

    private static readonly string ComposedSample = "caf" + (char)0x00E9 + " na" + (char)0x00EF + "ve";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pasted_text_is_normalized_to_nfc_when_enabled(bool enabled)
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        settings.CodeViewPasteNormalizeNfc = enabled;
        var sut = new CodeTabViewModel(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);

        sut.NormalizesPastedText.Should().Be(enabled);
        sut.PrepareClipboardText(DecomposedSample).Should().Be(enabled ? ComposedSample : DecomposedSample);
    }

    [Fact]
    public void Normalizing_pasted_text_is_on_by_default()
    {
        (SettingsStore settings, _) = SpellCheckTestFactory.New();

        settings.CodeViewPasteNormalizeNfc.Should().BeTrue();
    }

    [Fact]
    public void Tag_structure_actions_are_not_supported_for_css()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        var sut = new CodeTabViewModel(new TabManagerModel().OpenResource(css), new StatusBarService(), settings, spellChecker);

        sut.SupportsTagStructure.Should().BeFalse();
    }

    [Fact]
    public void InsertRawText_inserts_the_value_at_the_selection()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.Document.Text = FormatDoc;
        (int start, int end) = Span(FormatDoc, "world");
        sut.UpdateSelection(start, end);

        sut.InsertRawText("—");

        sut.Document.Text.Should().Contain("<p>Hello —</p>");
    }

    [Fact]
    public void InsertHyperlink_wraps_the_selection_in_an_anchor()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.Document.Text = FormatDoc;
        (int start, int end) = Span(sut.Document.Text, "world");
        sut.UpdateSelection(start, end);

        sut.InsertHyperlink("chapter2.xhtml#top");

        sut.Document.Text.Should().Contain("<a href=\"chapter2.xhtml#top\">world</a>");
    }

    [Fact]
    public void InsertIdEnabled_tracks_whether_the_caret_sits_in_an_anchor_or_plain_body_text()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        CodeTabViewModel sut = NewCodeTab(book, out _);
        sut.Document.Text = FormatDoc.Replace("<p>Hello world</p>", "<p><a href=\"x\">z</a></p>");

        int inAnchor = sut.Document.Text.IndexOf("<a href=\"x\">z", System.StringComparison.Ordinal) + "<a href=\"x\">".Length;
        sut.UpdateSelection(inAnchor, inAnchor);
        sut.UpdateCaret(1, 1, inAnchor, 'z');
        sut.InsertIdEnabled.Should().BeTrue();

        int inTitle = sut.Document.Text.IndexOf("<title>", System.StringComparison.Ordinal) + "<title>".Length;
        sut.UpdateSelection(inTitle, inTitle);
        sut.UpdateCaret(1, 1, inTitle, 't');
        sut.InsertIdEnabled.Should().BeFalse();
    }

    [Fact]
    public void Format_state_is_inactive_for_a_css_resource()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        Signet.Core.Resources.CssResource css =
            book.GetAllResources().OfType<Signet.Core.Resources.CssResource>().First();
        var model = new TabManagerModel();
        OpenTab tab = model.OpenResource(css);
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        var sut = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);

        sut.SupportsFormatting.Should().BeFalse();
        sut.CaretBlockElement.Should().BeEmpty();
        sut.RemoveFormattingEnabled.Should().BeFalse();
        sut.InsertFileEnabled.Should().BeFalse();
        sut.InsertHyperlinkEnabled.Should().BeFalse();
    }
}
