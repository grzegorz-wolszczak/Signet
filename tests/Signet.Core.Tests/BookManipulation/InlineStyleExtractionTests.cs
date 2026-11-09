using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="InlineStyleExtraction"/> — "Extract Inline Styles" (Tools → CSS Tools).</summary>
public sealed class InlineStyleExtractionTests : IDisposable
{
    private const string LinkLine = "<link rel=\"stylesheet\" type=\"text/css\" href=\"../styles/style.css\"/>";

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // The minimal book with the given CSS and one chapter per body (chapter1.xhtml, chapter2.xhtml…); the last one does not
    // link the stylesheet when unlinkedLast is set.
    private Book Load(string css, string[] bodies, bool unlinkedLast = false)
    {
        string tree = _temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n" + css + "\n");
        string chapter1 = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        string original = File.ReadAllText(chapter1);
        string opf = Path.Combine(tree, "EPUB", "package.opf");
        string opfText = File.ReadAllText(opf);
        for (int i = 0; i < bodies.Length; i++)
        {
            string page = original.Replace("<p>Hello, world.</p>", bodies[i], StringComparison.Ordinal);
            if (unlinkedLast && i == bodies.Length - 1)
            {
                page = page.Replace(LinkLine, string.Empty, StringComparison.Ordinal);
            }

            File.WriteAllText(Path.Combine(tree, "EPUB", "text", $"chapter{i + 1}.xhtml"), page);
            if (i > 0)
            {
                opfText = opfText
                    .Replace("  </manifest>", $"    <item id=\"c{i + 1}\" href=\"text/chapter{i + 1}.xhtml\" media-type=\"application/xhtml+xml\"/>\n  </manifest>", StringComparison.Ordinal)
                    .Replace("  </spine>", $"    <itemref idref=\"c{i + 1}\"/>\n  </spine>", StringComparison.Ordinal);
            }
        }

        File.WriteAllText(opf, opfText);
        return new ImportEpub(EpubBuilder.BuildInto(tree, _temp, "book.epub")).GetBook();
    }

    private static HtmlResource Chapter(Book book, int number) =>
        book.GetHtmlResources().Single(h => h.Filename == $"chapter{number}.xhtml");

    private static CssResource Stylesheet(Book book) => book.GetCssResources().Single();

    private static InlineStyleExtractionPlan PlanOf(InlineStyleSurvey survey, int minOccurrences = 2)
    {
        Dictionary<string, string> names = new();
        foreach (InlineStyleGroup group in survey.Groups.Where(g => g.Occurrences.Count >= minOccurrences))
        {
            names[group.Key] = InlineStyleExtraction.ClassName(survey.FirstFreeNumber + names.Count);
        }

        return new InlineStyleExtractionPlan(survey.StylesheetBookPath!, names);
    }

    [Fact]
    public void Survey_groups_the_same_value_across_files_and_orders_by_use()
    {
        // Arrange
        using Book book = Load(
            string.Empty,
            [
                "<p style=\"text-indent:1em;\">a</p><p style=\"text-align:center;\">b</p><p style=\"text-indent:1em;\">c</p>",
                "<p style=\"text-indent:1em;\">d</p><span style=\"text-align:center;\">e</span>",
            ]);

        // Act
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);

        // Assert
        survey.StylesheetBookPath.Should().Be(Stylesheet(book).BookPath);
        survey.Groups.Select(g => g.Occurrences.Count).Should().Equal(3, 2);
        survey.Groups[0].Summary.Should().Be("text-indent: 1em");
        survey.Groups[0].Occurrences.Select(o => o.TagName).Should().OnlyContain(t => t == "p");
        survey.Groups[1].Occurrences.Select(o => o.TagName).Should().Equal("p", "span");
    }

    [Fact]
    public void Survey_treats_values_written_differently_as_the_same()
    {
        // Arrange
        using Book book = Load(
            string.Empty,
            ["<p style=\"Text-Indent : 1em ; color:red\">a</p><p style=\"text-indent:1em;color:red;\">b</p><p style=\"text-indent:1em;color:&#x72;ed\">c</p>"]);

        // Act
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);

        // Assert
        survey.Groups.Should().ContainSingle().Which.Occurrences.Should().HaveCount(3);
    }

    [Fact]
    public void Survey_keeps_the_order_of_declarations()
    {
        // Arrange
        using Book book = Load(
            string.Empty,
            ["<p style=\"color:red;margin:0\">a</p><p style=\"margin:0;color:red\">b</p>"]);

        // Act
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);

        // Assert
        survey.Groups.Should().HaveCount(2);
    }

    [Fact]
    public void Survey_reports_the_place_of_each_element()
    {
        // Arrange
        using Book book = Load(string.Empty, ["<p style=\"color:red\">first</p>\n<p style=\"color:red\">second</p>"]);
        string text = Chapter(book, 1).GetText();

        // Act
        IReadOnlyList<InlineStyleOccurrence> occurrences = InlineStyleExtraction.Survey(book).Groups.Single().Occurrences;

        // Assert
        occurrences.Select(o => o.Offset).Should().Equal(text.IndexOf("<p style", StringComparison.Ordinal), text.LastIndexOf("<p style", StringComparison.Ordinal));
        occurrences.Select(o => o.Line).Should().Equal(text.Split('\n').ToList().FindIndex(l => l.Contains("first", StringComparison.Ordinal)) + 1, text.Split('\n').ToList().FindIndex(l => l.Contains("second", StringComparison.Ordinal)) + 1);
        occurrences[0].Snippet.Should().StartWith("<p style=\"color:red\">first");
        occurrences[0].BookPath.Should().Be(Chapter(book, 1).BookPath);
    }

    [Fact]
    public void Survey_ignores_empty_and_unusable_values()
    {
        // Arrange
        using Book book = Load(string.Empty, ["<p style=\"\">a</p><p style=\"  \">b</p><p style=\"nonsense\">c</p><p>d</p>"]);

        // Act
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);

        // Assert
        survey.Groups.Should().BeEmpty();
    }

    [Fact]
    public void Survey_leaves_files_that_do_not_link_the_stylesheet()
    {
        // Arrange
        using Book book = Load(string.Empty, ["<p style=\"color:red\">a</p>", "<p style=\"color:red\">b</p>", "<p style=\"color:red\">c</p>"], unlinkedLast: true);

        // Act
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);

        // Assert
        survey.Groups.Single().Occurrences.Should().HaveCount(2);
        survey.ElementsOutsideStylesheet.Should().Be(1);
    }

    [Fact]
    public void Survey_numbers_after_the_classes_the_book_already_has()
    {
        // Arrange
        using Book book = Load(".signet-cls-7 { color: blue; }", ["<p class=\"signet-cls-7\" style=\"color:red\">a</p>"]);

        // Act
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);

        // Assert
        survey.FirstFreeNumber.Should().Be(8);
    }

    [Fact]
    public void Survey_warns_when_a_more_specific_rule_sets_the_same_property()
    {
        // Arrange
        using Book book = Load(
            "p.note { text-indent: 0; } p { color: green; }",
            ["<p class=\"note\" style=\"text-indent:1em;color:red\">a</p><p class=\"note\" style=\"text-indent:1em;color:red\">b</p>"]);

        // Act
        InlineStyleGroup group = InlineStyleExtraction.Survey(book).Groups.Single();

        // Assert
        group.Consequences.Should().ContainSingle();
        group.Consequences[0].Text.Should().Contain("text-indent").And.Contain("p.note");
        group.Consequences[0].BookPath.Should().Be(Chapter(book, 1).BookPath);
    }

    [Fact]
    public void Survey_does_not_warn_about_rules_a_class_beats()
    {
        // Arrange
        using Book book = Load(
            "p { text-indent: 0; } .plain { text-indent: 2em; } p.note { color: green !important; }",
            ["<p class=\"plain\" style=\"text-indent:1em\">a</p><p class=\"note\" style=\"color:red\">b</p>"]);

        // Act
        IReadOnlyList<InlineStyleGroup> groups = InlineStyleExtraction.Survey(book).Groups;

        // Assert
        groups.SelectMany(g => g.Consequences).Should().BeEmpty();
    }

    [Fact]
    public void Survey_does_not_warn_when_the_rule_says_the_same()
    {
        // Arrange
        using Book book = Load("p.note { text-indent: 1em; }", ["<p class=\"note\" style=\"text-indent:1em\">a</p>"]);

        // Act
        InlineStyleGroup group = InlineStyleExtraction.Survey(book).Groups.Single();

        // Assert
        group.Consequences.Should().BeEmpty();
    }

    [Fact]
    public void ExtractInlineStyles_replaces_the_attribute_with_a_class_and_adds_the_rule()
    {
        // Arrange
        using Book book = Load(
            string.Empty,
            ["<p style=\"text-indent:1em;color:red\">a</p><p style=\"text-indent:1em;color:red\">b</p><p style=\"margin:0\">c</p>"]);
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);

        // Act
        FileBatchResult result = book.ExtractInlineStyles(PlanOf(survey), survey);

        // Assert
        result.Modified.Should().BeTrue();
        Chapter(book, 1).GetText().Should()
            .Contain("<p class=\"signet-cls-1\">a</p><p class=\"signet-cls-1\">b</p>")
            .And.Contain("<p style=\"margin:0\">c</p>");
        Stylesheet(book).GetText().Should().EndWith(".signet-cls-1 {\n  text-indent: 1em;\n  color: red;\n}\n");
    }

    [Fact]
    public void ExtractInlineStyles_adds_the_class_to_an_existing_class_attribute()
    {
        // Arrange
        using Book book = Load(
            string.Empty,
            ["<p class=\"a\" style=\"color:red\">x</p><p style=\"color:red\" class=\"b c\">y</p><p style='color:red' id=\"i\">z</p>"]);
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);

        // Act
        book.ExtractInlineStyles(PlanOf(survey), survey);

        // Assert
        Chapter(book, 1).GetText().Should()
            .Contain("<p class=\"a signet-cls-1\">x</p>")
            .And.Contain("<p class=\"b c signet-cls-1\">y</p>")
            .And.Contain("<p class=\"signet-cls-1\" id=\"i\">z</p>");
    }

    [Fact]
    public void ExtractInlineStyles_changes_only_the_chosen_values_and_linked_files()
    {
        // Arrange
        using Book book = Load(
            string.Empty,
            ["<p style=\"color:red\">a</p><p style=\"color:red\">b</p><p style=\"margin:0\">c</p>", "<p style=\"color:red\">d</p>"],
            unlinkedLast: true);
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);
        string unlinked = Chapter(book, 2).GetText();

        // Act
        book.ExtractInlineStyles(PlanOf(survey), survey);

        // Assert
        Chapter(book, 2).GetText().Should().Be(unlinked);
        Chapter(book, 1).GetText().Should().Contain("<p style=\"margin:0\">c</p>").And.NotContain("style=\"color:red\"");
        Stylesheet(book).GetText().Should().NotContain("margin: 0");
    }

    [Fact]
    public void ExtractInlineStyles_leaves_the_stylesheet_alone_when_nothing_is_chosen()
    {
        // Arrange
        using Book book = Load(string.Empty, ["<p style=\"color:red\">a</p>"]);
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);
        string css = Stylesheet(book).GetText();
        string html = Chapter(book, 1).GetText();

        // Act
        FileBatchResult result = book.ExtractInlineStyles(PlanOf(survey, minOccurrences: 2), survey);

        // Assert
        result.Modified.Should().BeFalse();
        Stylesheet(book).GetText().Should().Be(css);
        Chapter(book, 1).GetText().Should().Be(html);
    }

    [Fact]
    public void ExtractInlineStyles_keeps_important_and_decoded_values()
    {
        // Arrange
        using Book book = Load(
            string.Empty,
            ["<p style=\"font-family:&quot;Noto Serif&quot;, serif;color:red !important\">a</p><p style=\"font-family:&quot;Noto Serif&quot;, serif;color:red !important\">b</p>"]);
        InlineStyleSurvey survey = InlineStyleExtraction.Survey(book);

        // Act
        book.ExtractInlineStyles(PlanOf(survey), survey);

        // Assert
        Stylesheet(book).GetText().Should().Contain("  font-family: \"Noto Serif\", serif;\n  color: red !important;\n");
    }

    [Fact]
    public void ExtractInlineStyles_run_again_finds_nothing_left_to_extract()
    {
        // Arrange
        using Book book = Load(string.Empty, ["<p style=\"color:red\">a</p><p style=\"color:red\">b</p>"]);
        InlineStyleSurvey first = InlineStyleExtraction.Survey(book);
        book.ExtractInlineStyles(PlanOf(first), first);

        // Act
        InlineStyleSurvey second = InlineStyleExtraction.Survey(book);

        // Assert
        second.Groups.Should().BeEmpty();
        second.FirstFreeNumber.Should().Be(2);
    }
}
