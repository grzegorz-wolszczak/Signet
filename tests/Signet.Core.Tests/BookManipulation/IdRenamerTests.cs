using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="IdRenamer"/> — "Rename #id" from Code Editor.</summary>
public sealed class IdRenamerTests
{
    private static IdRenamer Renamer(string html1, string css = "", string html2 = "") =>
        new(
            html2.Length == 0
                ? [new ClassRenameSource("text/ch1.xhtml", html1)]
                : [new ClassRenameSource("text/ch1.xhtml", html1), new ClassRenameSource("text/ch2.xhtml", html2)],
            [new ClassRenameSource("styles/a.css", css)]);

    [Fact]
    public void Rename_renames_the_attribute_and_the_matching_css_selector()
    {
        // Arrange
        IdRenamer sut = Renamer("<html><body><p id=\"note1\">a</p></body></html>", "#note1 { color: red }");

        // Act
        IdRenameResult result = sut.Rename("note1", "note2");

        // Assert
        result.ChangedTexts["text/ch1.xhtml"].Should().Be("<html><body><p id=\"note2\">a</p></body></html>");
        result.ChangedTexts["styles/a.css"].Should().Be("#note2 { color: red }");
        result.Stats.Should().Be(new IdRenameStats(AttributeOccurrences: 1, SelectorOccurrences: 1, ReferencesUpdated: 0, ChangedFiles: 2));
    }

    [Fact]
    public void Rename_renames_every_duplicate_occurrence_of_a_pre_existing_duplicate_id()
    {
        // Arrange
        IdRenamer sut = Renamer("<html><body><p id=\"note1\">a</p><span id=\"note1\">b</span></body></html>");

        // Act
        IdRenameResult result = sut.Rename("note1", "note2");

        // Assert
        result.ChangedTexts["text/ch1.xhtml"].Should().Be("<html><body><p id=\"note2\">a</p><span id=\"note2\">b</span></body></html>");
        result.Stats.AttributeOccurrences.Should().Be(2);
    }

    [Fact]
    public void Rename_updates_a_same_file_href_fragment_that_points_at_the_renamed_id()
    {
        // Arrange
        IdRenamer sut = Renamer(
            "<html><body><p id=\"note1\">a</p><a href=\"#note1\">back</a></body></html>");

        // Act
        IdRenameResult result = sut.Rename("note1", "note2");

        // Assert
        result.ChangedTexts["text/ch1.xhtml"].Should().Be(
            "<html><body><p id=\"note2\">a</p><a href=\"#note2\">back</a></body></html>");
        result.Stats.ReferencesUpdated.Should().Be(1);
    }

    [Fact]
    public void Rename_updates_a_cross_file_href_fragment_that_points_at_the_renamed_id()
    {
        // Arrange
        IdRenamer sut = Renamer(
            "<html><body><p id=\"note1\">a</p></body></html>",
            html2: "<html><body><a href=\"ch1.xhtml#note1\">back</a></body></html>");

        // Act
        IdRenameResult result = sut.Rename("note1", "note2");

        // Assert
        result.ChangedTexts["text/ch2.xhtml"].Should().Be("<html><body><a href=\"ch1.xhtml#note2\">back</a></body></html>");
        result.Stats.ReferencesUpdated.Should().Be(1);
    }

    [Fact]
    public void Rename_leaves_a_href_fragment_alone_when_its_target_file_does_not_carry_the_id()
    {
        // Arrange
        IdRenamer sut = Renamer(
            "<html><body><p id=\"note1\">a</p></body></html>",
            html2: "<html><body><a href=\"other.xhtml#note1\">back</a></body></html>");

        // Act
        IdRenameResult result = sut.Rename("note1", "note2");

        // Assert
        result.ChangedTexts.Should().NotContainKey("text/ch2.xhtml");
        result.Stats.ReferencesUpdated.Should().Be(0);
    }

    [Fact]
    public void Rename_reads_style_blocks_of_an_xhtml_file_too()
    {
        // Arrange
        IdRenamer sut = Renamer(
            "<html><head><style>#note1 { color: red }</style></head><body><p id=\"note1\">a</p></body></html>");

        // Act
        IdRenameResult result = sut.Rename("note1", "note2");

        // Assert
        string text = result.ChangedTexts["text/ch1.xhtml"];
        text.Should().NotContain("note1").And.Contain("#note2").And.Contain("id=\"note2\"");
    }

    [Fact]
    public void Rename_updates_the_ids_in_aria_lists_and_other_id_lists_of_the_file()
    {
        // Arrange
        IdRenamer sut = Renamer(
            "<html><body><h2 id=\"note1\">t</h2>"
            + "<p aria-labelledby=\"x  note1\tz\" aria-describedby=\"note1\" aria-errormessage=\"note1\">a</p>"
            + "<label for=\"note1\">l</label><table><tr><td headers=\"a note1\">c</td></tr></table></body></html>");

        // Act
        IdRenameResult result = sut.Rename("note1", "note2");

        // Assert
        string text = result.ChangedTexts["text/ch1.xhtml"];
        text.Should().Contain("aria-labelledby=\"x  note2\tz\"").And.Contain("aria-describedby=\"note2\"")
            .And.Contain("aria-errormessage=\"note2\"").And.Contain("<label for=\"note2\">")
            .And.Contain("headers=\"a note2\"").And.NotContain("note1");
        result.Stats.ReferencesUpdated.Should().Be(5, "every reference counts, with the links");
    }

    [Fact]
    public void Rename_leaves_aria_references_alone_in_files_that_do_not_carry_the_id_and_whole_tokens_only()
    {
        // Arrange
        IdRenamer sut = Renamer(
            "<html><body><p id=\"note1\">a</p><p aria-labelledby=\"note10 xnote1 note1x\">b</p></body></html>",
            html2: "<html><body><p id=\"o\" aria-labelledby=\"note1\">c</p></body></html>");

        // Act
        IdRenameResult result = sut.Rename("note1", "note2");

        // Assert
        result.ChangedTexts.Should().NotContainKey("text/ch2.xhtml", "an aria reference points into its own file only");
        result.ChangedTexts["text/ch1.xhtml"].Should().Contain("aria-labelledby=\"note10 xnote1 note1x\"");
        result.Stats.ReferencesUpdated.Should().Be(0);
    }

    [Theory]
    [InlineData("[id=\"note1\"] { color: red }", "[id=\"note2\"] { color: red }")]
    [InlineData("p[id=note1] { color: red }", "p[id=note2] { color: red }")]
    [InlineData("[ ID = 'note1' ] { color: red }", "[ ID = 'note2' ] { color: red }")]
    [InlineData("#note1.big, p[id=\"note1\"] { color: red }", "#note2.big, p[id=\"note2\"] { color: red }")]
    [InlineData("[class=\"note1\"] { color: red }", "[class=\"note1\"] { color: red }")]
    [InlineData("[id^=\"note1\"] { color: red }", "[id^=\"note1\"] { color: red }")]
    [InlineData("[data-id=\"note1\"] { color: red }", "[data-id=\"note1\"] { color: red }")]
    [InlineData("/* #note1 [id=\"note1\"] */ p::after { content: \"#note1\"; }", "/* #note1 [id=\"note1\"] */ p::after { content: \"#note1\"; }")]
    public void Rename_changes_the_id_in_selectors_of_both_kinds_and_leaves_comments_strings_and_other_attributes(string css, string expected)
    {
        // Arrange
        IdRenamer sut = Renamer("<html><body><p id=\"note1\">a</p></body></html>", css);

        // Act
        IdRenameResult result = sut.Rename("note1", "note2");

        // Assert
        (result.ChangedTexts.TryGetValue("styles/a.css", out string? text) ? text : css).Should().Be(expected);
    }

    [Fact]
    public void Preview_reports_the_scale_without_computing_the_texts()
    {
        // Arrange
        IdRenamer sut = Renamer("<html><body><p id=\"note1\">a</p></body></html>", "#note1 { color: red }");

        // Act
        IdRenameStats stats = sut.Preview("note1");

        // Assert
        stats.Should().Be(new IdRenameStats(1, 1, 0, 2));
    }

    [Theory]
    [InlineData("", IdNameProblem.Empty)]
    [InlineData("1a", IdNameProblem.Invalid)]
    [InlineData("a b", IdNameProblem.Invalid)]
    [InlineData("a:b", IdNameProblem.Invalid)]
    [InlineData("note1", IdNameProblem.SameAsOld)]
    [InlineData("other", IdNameProblem.AlreadyExists)]
    public void Validate_rejects_an_invalid_or_taken_name(string newId, IdNameProblem expected)
    {
        // Arrange
        IdRenamer sut = Renamer("<html><body><p id=\"note1\">a</p><p id=\"other\">b</p></body></html>");

        // Act & Assert
        sut.Validate("note1", newId).Problems.Should().Equal(expected);
    }

    [Fact]
    public void Validate_rejects_a_name_that_already_exists_only_as_a_css_selector()
    {
        // Arrange
        IdRenamer sut = Renamer("<html><body><p id=\"note1\">a</p></body></html>", "#takenBySelector { }");

        // Act & Assert
        sut.Validate("note1", "takenBySelector").Problems.Should().Equal(IdNameProblem.AlreadyExists);
    }

    [Fact]
    public void Validate_accepts_a_fresh_valid_name()
    {
        // Arrange
        IdRenamer sut = Renamer("<html><body><p id=\"note1\">a</p></body></html>");

        // Act & Assert
        sut.Validate("note1", "note2").IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rename_throws_for_an_invalid_name()
    {
        // Arrange
        IdRenamer sut = Renamer("<html><body><p id=\"note1\">a</p></body></html>");

        // Act & Assert
        sut.Invoking(s => s.Rename("note1", "1bad")).Should().Throw<ArgumentException>();
    }

    // ---- Book ----

    [Fact]
    public void Book_applies_a_rename_across_the_html_file_and_its_stylesheet()
    {
        // Arrange
        using TempDir temp = new();
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(chapterPath, File.ReadAllText(chapterPath).Replace("<p>", "<p id=\"intro\">", StringComparison.Ordinal));
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n#intro { color: red }\n");
        using Book book = new ImportEpub(EpubBuilder.BuildInto(tree, temp, "book.epub")).GetBook();
        CssResource css = book.GetCssResources().Single();

        // Act
        IdRenamer renamer = book.PrepareIdRename().Renamer!;
        IdRenameResult result = renamer.Rename("intro", "foreword");

        // Assert
        book.ApplyIdRename(result).Should().BeTrue();
        book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().Contain("id=\"foreword\"");
        css.GetText().Should().Contain("#foreword");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void Book_refuses_to_prepare_a_rename_when_an_xhtml_file_is_not_well_formed()
    {
        // Arrange
        using TempDir temp = new();
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(chapterPath, File.ReadAllText(chapterPath).Replace("</p>", string.Empty, StringComparison.Ordinal));
        using Book book = new ImportEpub(EpubBuilder.BuildInto(tree, temp, "book.epub")).GetBook();

        // Act
        IdRenamePreparation result = book.PrepareIdRename();

        // Assert
        result.Renamer.Should().BeNull();
        result.NotWellFormed.Should().BeOfType<HtmlResource>().Which.Filename.Should().Be("chapter1.xhtml");
    }
}
