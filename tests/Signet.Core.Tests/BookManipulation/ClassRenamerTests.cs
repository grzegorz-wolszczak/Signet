using System;
using System.IO;
using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="ClassRenamer"/> — "Rename Class" from Code View,
/// including the cascade handling: which style sources style which element.
/// </summary>
public sealed class ClassRenamerTests
{
    private const string LinkA = "<link rel=\"stylesheet\" href=\"../styles/a.css\"/>";

    // body > p.c1 (twice) and body > div > p.c1; the document links a.css and has its own <style> block.
    private const string Chapter =
        "<html><head>" + LinkA + "<style>p.c1 { color: red }</style></head>\n"
        + "<body><p class=\"c1\">a</p><div><p class=\"x c1\">b</p></div><p class=\"c1\">c</p></body></html>";

    private static readonly ClassAtCaret BodyP = new("c1", ["html", "body", "p"]);

    private static ClassRenamer Renamer(string html, string css) =>
        new([new ClassRenameSource("text/ch1.xhtml", html)], [new ClassRenameSource("styles/a.css", css)]);

    private static ClassRenameRequest SameNesting => ClassRenameRequest.ForElement(BodyP, ClassRenameScope.SameNesting);

    private static ClassRenameRequest Everywhere => ClassRenameRequest.ForElement(BodyP, ClassRenameScope.Everywhere);

    private static ClassRenameRequest FromSheet(string bookPath) =>
        ClassRenameRequest.ForStyleSource(new StyleClassAtCaret("c1", bookPath, -1));

    // ---- detecting the class under the caret: the class attribute ----

    [Theory]
    [InlineData(16)] // right before "c"
    [InlineData(17)] // between "c" and "1"
    [InlineData(18)] // right after "1"
    public void FindClassAtCaret_detects_the_class_when_the_caret_touches_or_is_inside_the_name(int caret)
    {
        ClassAtCaret? result = ClassRenamer.FindClassAtCaret("<html><body><p class=\"c1\">x</p></body></html>", caret + 6);

        result.Should().BeEquivalentTo(new ClassAtCaret("c1", ["html", "body", "p"]));
    }

    [Fact]
    public void FindClassAtCaret_picks_the_class_under_the_caret_among_several()
    {
        const string html = "<html><body><div class=\"first second\"/></body></html>";

        ClassRenamer.FindClassAtCaret(html, html.IndexOf("second", StringComparison.Ordinal) + 3)!.Name.Should().Be("second");
    }

    [Theory]
    [InlineData("<html><body><p title=\"c1\">x</p></body></html>", 23)] // another attribute
    [InlineData("<html><body><p class=\"c1\">c1 text</p></body></html>", 27)] // element content
    [InlineData("<html><body><p class=\"a  b\">x</p></body></html>", 24)] // a space between classes
    public void FindClassAtCaret_returns_null_outside_class_names(string html, int caret) =>
        ClassRenamer.FindClassAtCaret(html, caret).Should().BeNull();

    // ---- detecting the class under the caret: a CSS selector ----

    [Theory]
    [InlineData(4)] // on the dot (selecting ".c1" from the right)
    [InlineData(5)] // right before the name
    [InlineData(6)] // in the middle
    [InlineData(7)] // right after the name
    public void FindStyleClassAtCaret_detects_the_class_in_a_stylesheet_selector(int caret) =>
        ClassRenamer.FindStyleClassAtCaret("div .c1 > span { color: red }", caret, "styles/a.css", isStyleSheet: true)
            .Should().Be(new StyleClassAtCaret("c1", "styles/a.css", -1));

    [Theory]
    [InlineData(2)] // „div"
    [InlineData(20)] // deklaracja
    public void FindStyleClassAtCaret_returns_null_outside_class_names_of_selectors(int caret) =>
        ClassRenamer.FindStyleClassAtCaret("div .c1 > span { color: red }", caret, "styles/a.css", isStyleSheet: true)
            .Should().BeNull();

    [Fact]
    public void FindStyleClassAtCaret_in_xhtml_finds_the_style_block_and_ignores_class_attributes()
    {
        const string html = "<html><head><style>.a { }</style><style>.b { }</style></head><body><p class=\"a\"/></body></html>";

        ClassRenamer.FindStyleClassAtCaret(html, html.IndexOf(".b", StringComparison.Ordinal) + 1, "t.xhtml", isStyleSheet: false)
            .Should().Be(new StyleClassAtCaret("b", "t.xhtml", 1));
        ClassRenamer.FindStyleClassAtCaret(html, html.IndexOf("\"a\"", StringComparison.Ordinal) + 1, "t.xhtml", isStyleSheet: false)
            .Should().BeNull();
    }

    // ---- renaming everywhere ----

    [Fact]
    public void Everywhere_renames_all_attributes_and_all_definitions_in_place_even_unused_ones()
    {
        ClassRenamer sut = new(
            [new ClassRenameSource("text/ch1.xhtml", Chapter)],
            [new ClassRenameSource("styles/a.css", ".c1 { margin: 0 }\n"), new ClassRenameSource("styles/unlinked.css", ".c1 { }\n")]);

        ClassRenameResult result = sut.Rename(Everywhere, "lead");

        result.ChangedTexts["text/ch1.xhtml"].Should().Be(
            "<html><head>" + LinkA + "<style>p.lead { color: red }</style></head>\n"
            + "<body><p class=\"lead\">a</p><div><p class=\"x lead\">b</p></div><p class=\"lead\">c</p></body></html>");
        result.ChangedTexts["styles/a.css"].Should().Be(".lead { margin: 0 }\n");
        result.ChangedTexts["styles/unlinked.css"].Should().Be(".lead { }\n");
        result.Stats.Sources.Select(s => s.Change).Should().OnlyContain(c => c == ClassRenameSourceChange.RenamedInPlace);
        (result.Stats.ChangedOccurrences, result.Stats.TotalOccurrences, result.Stats.ChangedFiles).Should().Be((3, 3, 3));
    }

    [Fact]
    public void Class_names_are_matched_as_whole_tokens_only()
    {
        ClassRenamer sut = Renamer("<html><head>" + LinkA + "</head><body><p class=\"c1 c10 xc1\">a</p></body></html>", ".c10, .xc1 { }\n");

        ClassRenameResult result = sut.Rename(Everywhere, "lead");

        result.ChangedTexts["text/ch1.xhtml"].Should().Contain("class=\"lead c10 xc1\"");
        result.ChangedTexts.Should().NotContainKey("styles/a.css");
    }

    // ---- the same nesting, with the cascade ----

    [Fact]
    public void SameNesting_copies_rules_of_sources_also_used_by_elements_that_keep_the_old_name()
    {
        ClassRenamer sut = Renamer(Chapter, "body {\n  margin: 1em;\n}\n\n.c1 {\n  color: blue;\n}\n");

        ClassRenameResult result = sut.Rename(SameNesting, "lead");

        result.ChangedTexts["text/ch1.xhtml"].Should().Be(
            "<html><head>" + LinkA + "<style>p.c1 { color: red }\np.lead { color: red }</style></head>\n"
            + "<body><p class=\"lead\">a</p><div><p class=\"x c1\">b</p></div><p class=\"lead\">c</p></body></html>");
        result.ChangedTexts["styles/a.css"].Should().Be(
            "body {\n  margin: 1em;\n}\n\n.c1 {\n  color: blue;\n}\n.lead {\n  color: blue;\n}\n");
        result.Stats.Sources.Should().Equal(
            new ClassRenameSourceAction("styles/a.css", -1, ClassRenameSourceChange.Copied, 1),
            new ClassRenameSourceAction("text/ch1.xhtml", 0, ClassRenameSourceChange.Copied, 1));
        result.Stats.SourcesUsedByChangedElements.Should().Be(2);
    }

    [Fact]
    public void SameNesting_leaves_sources_that_no_changed_element_uses()
    {
        const string html = "<html><head>" + LinkA + "</head>\n"
            + "<body><p class=\"c1\">a</p><div><p class=\"c1\">b</p></div></body></html>";
        ClassRenamer sut = new(
            [new ClassRenameSource("text/ch1.xhtml", html)],
            [new ClassRenameSource("styles/a.css", "body > .c1 { color: red }\ndiv > .c1 { color: blue }\n"),
             new ClassRenameSource("styles/unlinked.css", ".c1 { }\n")]);

        ClassRenameResult result = sut.Rename(SameNesting, "lead");

        // both rules are in the same stylesheet, and the stylesheet is also used by div > p → a copy
        result.ChangedTexts["styles/a.css"].Should().Be(
            "body > .c1 { color: red }\nbody > .lead { color: red }\ndiv > .c1 { color: blue }\ndiv > .lead { color: blue }\n");
        result.ChangedTexts.Should().NotContainKey("styles/unlinked.css");
        result.Stats.Sources.Should().Contain(new ClassRenameSourceAction("styles/unlinked.css", -1, ClassRenameSourceChange.Unchanged, 1));
    }

    [Fact]
    public void SameNesting_renames_in_place_when_only_changed_elements_use_the_source()
    {
        ClassRenamer sut = Renamer("<html><head>" + LinkA + "</head><body><p class=\"c1\">a</p><p class=\"c1\">b</p></body></html>", ".c1 { color: red }\n");

        ClassRenameResult result = sut.Rename(SameNesting, "lead");

        result.ChangedTexts["styles/a.css"].Should().Be(".lead { color: red }\n");
        result.Stats.CopiesCssRules.Should().BeFalse();
    }

    [Fact]
    public void Copy_keeps_only_group_members_with_the_class_and_stays_inside_media()
    {
        ClassRenamer sut = Renamer(Chapter, "@media screen {\n  .c1 span, h1, p.c1 { color: red }\n}\n");

        ClassRenameResult result = sut.Rename(SameNesting, "lead");

        result.ChangedTexts["styles/a.css"].Should().Be(
            "@media screen {\n  .c1 span, h1, p.c1 { color: red }\n  .lead span, p.lead { color: red }\n}\n");
    }

    [Fact]
    public void Copy_uses_the_crlf_line_ending_of_the_file()
    {
        ClassRenamer sut = Renamer(Chapter, ".c1 { color: red }\r\n");

        ClassRenameResult result = sut.Rename(SameNesting, "lead");

        result.ChangedTexts["styles/a.css"].Should().Be(".c1 { color: red }\r\n.lead { color: red }\r\n");
    }

    // ---- a rename started in a stylesheet / a <style> block ----

    [Fact]
    public void From_a_stylesheet_only_elements_of_documents_using_it_are_renamed()
    {
        ClassRenamer sut = new(
            [new ClassRenameSource("text/ch1.xhtml", "<html><head>" + LinkA + "</head><body><p class=\"c1\">a</p></body></html>"),
             new ClassRenameSource("text/ch2.xhtml", "<html><head></head><body><p class=\"c1\">b</p></body></html>")],
            [new ClassRenameSource("styles/a.css", ".c1 { color: red }\n")]);

        ClassRenameResult result = sut.Rename(FromSheet("styles/a.css"), "lead");

        result.ChangedTexts["text/ch1.xhtml"].Should().Contain("<p class=\"lead\">a</p>");
        result.ChangedTexts.Should().NotContainKey("text/ch2.xhtml");
        result.ChangedTexts["styles/a.css"].Should().Be(".lead { color: red }\n");
        (result.Stats.ChangedOccurrences, result.Stats.TotalOccurrences).Should().Be((1, 2));
    }

    [Fact]
    public void From_a_stylesheet_elements_not_matching_its_selectors_keep_the_old_name()
    {
        const string html = "<html><head>" + LinkA + "</head><body><p class=\"c1\">a</p><div><p class=\"c1\">b</p></div></body></html>";
        ClassRenamer sut = Renamer(html, "div > .c1 { color: red }\n");

        ClassRenameResult result = sut.Rename(FromSheet("styles/a.css"), "lead");

        result.ChangedTexts["text/ch1.xhtml"].Should().Contain("<p class=\"c1\">a</p>").And.Contain("<div><p class=\"lead\">b</p>");
        result.ChangedTexts["styles/a.css"].Should().Be("div > .lead { color: red }\n");
    }

    [Fact]
    public void From_a_stylesheet_state_pseudo_classes_and_pseudo_elements_count_as_matching()
    {
        ClassRenamer sut = Renamer("<html><head>" + LinkA + "</head><body><a class=\"c1\">a</a></body></html>", "a.c1:hover::after { content: '' }\n");

        ClassRenameResult result = sut.Rename(FromSheet("styles/a.css"), "lead");

        result.ChangedTexts["text/ch1.xhtml"].Should().Contain("<a class=\"lead\">");
    }

    [Fact]
    public void From_a_stylesheet_sources_used_only_by_the_changed_elements_are_renamed_in_place()
    {
        ClassRenamer sut = new(
            [new ClassRenameSource("text/ch1.xhtml", Chapter),
             new ClassRenameSource("text/ch2.xhtml", "<html><head><link rel=\"stylesheet\" href=\"../styles/b.css\"/></head><body><p class=\"c1\">z</p></body></html>")],
            [new ClassRenameSource("styles/a.css", "p.c1 { margin: 0 }\n"), new ClassRenameSource("styles/b.css", ".c1 { color: blue }\n")]);

        ClassRenameResult result = sut.Rename(FromSheet("styles/a.css"), "lead");

        result.ChangedTexts["styles/a.css"].Should().Be("p.lead { margin: 0 }\n");
        // the ch1 <style> block is used only by the elements being changed → in place; b.css is not visible to ch1 → unchanged
        result.ChangedTexts["text/ch1.xhtml"].Should().Contain("<style>p.lead { color: red }</style>");
        result.ChangedTexts.Should().NotContainKey("styles/b.css");
        result.ChangedTexts.Should().NotContainKey("text/ch2.xhtml");
        result.Stats.SourcesUsedByChangedElements.Should().Be(2);
        result.Stats.Sources.Should().Contain(new ClassRenameSourceAction("styles/b.css", -1, ClassRenameSourceChange.Unchanged, 1));
    }

    [Fact]
    public void From_a_stylesheet_a_second_source_shared_with_other_elements_is_copied()
    {
        ClassRenamer sut = new(
            [new ClassRenameSource("text/ch1.xhtml", "<html><head>" + LinkA + "<link rel=\"stylesheet\" href=\"../styles/b.css\"/></head><body><p class=\"c1\">a</p></body></html>"),
             new ClassRenameSource("text/ch2.xhtml", "<html><head><link rel=\"stylesheet\" href=\"../styles/b.css\"/></head><body><p class=\"c1\">z</p></body></html>")],
            [new ClassRenameSource("styles/a.css", ".c1 { margin: 0 }\n"), new ClassRenameSource("styles/b.css", ".c1 { color: blue }\n")]);

        ClassRenameResult result = sut.Rename(FromSheet("styles/a.css"), "lead");

        result.ChangedTexts["styles/b.css"].Should().Be(".c1 { color: blue }\n.lead { color: blue }\n");
        result.ChangedTexts.Should().NotContainKey("text/ch2.xhtml");
        result.Stats.CopiesCssRules.Should().BeTrue();
    }

    [Fact]
    public void Stylesheets_imported_by_a_linked_stylesheet_are_used_by_the_document()
    {
        ClassRenamer sut = new(
            [new ClassRenameSource("text/ch1.xhtml", "<html><head><link rel=\"stylesheet\" href=\"../styles/main.css\"/></head><body><p class=\"c1\">a</p></body></html>")],
            [new ClassRenameSource("styles/main.css", "/* @import \"ignored.css\"; */\n@import url(\"parts/a.css\");\n"),
             new ClassRenameSource("styles/parts/a.css", ".c1 { color: red }\n")]);

        ClassRenameResult result = sut.Rename(FromSheet("styles/parts/a.css"), "lead");

        result.ChangedTexts["text/ch1.xhtml"].Should().Contain("<p class=\"lead\">a</p>");
    }

    [Fact]
    public void From_a_style_block_only_its_document_is_affected()
    {
        const string html = "<html><head><style>.c1 { color: red }</style></head><body><p class=\"c1\">a</p></body></html>";
        ClassRenamer sut = new(
            [new ClassRenameSource("text/ch1.xhtml", html),
             new ClassRenameSource("text/ch2.xhtml", "<html><head><style>.c1 { }</style></head><body><p class=\"c1\">b</p></body></html>")],
            []);

        ClassRenameResult result = sut.Rename(ClassRenameRequest.ForStyleSource(new StyleClassAtCaret("c1", "text/ch1.xhtml", 0)), "lead");

        result.ChangedTexts["text/ch1.xhtml"].Should().Be("<html><head><style>.lead { color: red }</style></head><body><p class=\"lead\">a</p></body></html>");
        result.ChangedTexts.Should().NotContainKey("text/ch2.xhtml");
    }

    [Fact]
    public void From_an_unused_stylesheet_only_the_stylesheet_is_renamed()
    {
        ClassRenamer sut = Renamer("<html><head></head><body><p class=\"c1\">a</p></body></html>", ".c1 { }\n");

        ClassRenameResult result = sut.Rename(FromSheet("styles/a.css"), "lead");

        result.ChangedTexts.Keys.Should().Equal("styles/a.css");
        result.Stats.ChangedOccurrences.Should().Be(0);
    }

    [Fact]
    public void Preview_matches_the_stats_of_the_rename()
    {
        ClassRenamer sut = Renamer(Chapter, ".c1 { }\n");

        ClassRenameStats preview = sut.Preview(SameNesting);

        preview.Should().BeEquivalentTo(sut.Rename(SameNesting, "lead").Stats);
        preview.ChangedOccurrences.Should().Be(2);
    }

    // ---- walidacja ----

    [Theory]
    [InlineData("", ClassNameProblem.Empty)]
    [InlineData("a b", ClassNameProblem.ContainsWhitespace)]
    [InlineData("a.b", ClassNameProblem.ContainsInvalidCharacters)]
    [InlineData("1st", ClassNameProblem.StartsWithDigit)]
    [InlineData("-1st", ClassNameProblem.StartsWithHyphenAndDigit)]
    [InlineData("-", ClassNameProblem.SingleHyphen)]
    [InlineData("c1", ClassNameProblem.SameAsOld)]
    [InlineData("x", ClassNameProblem.AlreadyExists)] // z atrybutu class
    [InlineData("other", ClassNameProblem.AlreadyExists)] // only in the CSS selector
    public void Validate_rejects_bad_names_with_a_reason(string newName, ClassNameProblem expected)
    {
        ClassNameValidation result = Renamer(Chapter, ".other { }\n").Validate("c1", newName);

        result.IsValid.Should().BeFalse();
        result.Problems.Should().Contain(expected);
    }

    [Theory]
    [InlineData("lead")]
    [InlineData("_a-1")]
    [InlineData("--custom")]
    [InlineData("żółw")]
    public void Validate_accepts_css_identifiers_not_used_in_the_book(string newName) =>
        Renamer(Chapter, ".other { }\n").Validate("c1", newName).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_lists_every_invalid_character_once() =>
        Renamer(Chapter, string.Empty).Validate("c1", "a.b#c.d").InvalidCharacters.Should().Equal('.', '#');

    [Fact]
    public void Rename_refuses_an_invalid_name()
    {
        Action act = () => Renamer(Chapter, string.Empty).Rename(Everywhere, "x");

        act.Should().Throw<ArgumentException>();
    }

    // ---- Book ----

    [Theory]
    [AutoData]
    public void Book_applies_a_rename_started_in_the_stylesheet(string suffix)
    {
        using TempDir temp = new();
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(chapterPath, File.ReadAllText(chapterPath).Replace("<p>", "<p class=\"c1\">", StringComparison.Ordinal));
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.c1 { color: red }\n");
        using Book book = new ImportEpub(EpubBuilder.BuildInto(tree, temp, "book.epub")).GetBook();
        CssResource css = book.GetCssResources().Single();
        string newName = "n" + suffix.Replace("-", string.Empty, StringComparison.Ordinal);

        ClassRenamer renamer = book.PrepareClassRename().Renamer!;
        ClassRenameResult result = renamer.Rename(FromSheet(css.BookPath), newName);

        book.ApplyClassRename(result).Should().BeTrue();
        book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().Contain($"<p class=\"{newName}\">");
        css.GetText().Should().Contain($".{newName} {{ color: red }}");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void Book_refuses_to_prepare_when_an_xhtml_file_is_not_well_formed()
    {
        using TempDir temp = new();
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(chapterPath, File.ReadAllText(chapterPath).Replace("</p>", string.Empty, StringComparison.Ordinal));
        using Book book = new ImportEpub(EpubBuilder.BuildInto(tree, temp, "book.epub")).GetBook();

        ClassRenamePreparation result = book.PrepareClassRename();

        result.Renamer.Should().BeNull();
        result.NotWellFormed.Should().BeOfType<HtmlResource>().Which.Filename.Should().Be("chapter1.xhtml");
    }
}
