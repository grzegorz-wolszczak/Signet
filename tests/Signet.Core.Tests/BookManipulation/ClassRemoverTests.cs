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

/// <summary>Tests of <see cref="ClassRemover"/> — "Remove Class" from Code Editor.</summary>
public sealed class ClassRemoverTests
{
    private static ClassRemover Remover(string html, string css) =>
        new([new ClassRenameSource("text/ch1.xhtml", html)], [new ClassRenameSource("styles/a.css", css)]);

    [Fact]
    public void Remove_drops_only_the_matching_token_and_keeps_the_rest_of_the_attribute()
    {
        // Arrange
        ClassRemover sut = Remover("<html><body><p class=\"a c1 b\">x</p></body></html>", "");

        // Act
        ClassRemovalResult result = sut.Remove("c1");

        // Assert
        result.ChangedTexts["text/ch1.xhtml"].Should().Be("<html><body><p class=\"a b\">x</p></body></html>");
        result.Stats.AttributeOccurrences.Should().Be(1);
        result.Stats.ChangedFiles.Should().Be(1);
    }

    [Fact]
    public void Remove_drops_the_whole_class_attribute_when_it_was_the_only_class()
    {
        // Arrange
        ClassRemover sut = Remover("<html><body><p class=\"c1\">x</p></body></html>", "");

        // Act
        ClassRemovalResult result = sut.Remove("c1");

        // Assert
        result.ChangedTexts["text/ch1.xhtml"].Should().Be("<html><body><p>x</p></body></html>");
        result.Stats.AttributeOccurrences.Should().Be(1);
    }

    [Fact]
    public void Remove_drops_several_matching_tokens_in_the_same_attribute()
    {
        // Arrange
        ClassRemover sut = Remover("<html><body><p class=\"c1 keep c1\">x</p></body></html>", "");

        // Act
        ClassRemovalResult result = sut.Remove("c1");

        // Assert
        result.ChangedTexts["text/ch1.xhtml"].Should().Be("<html><body><p class=\"keep\">x</p></body></html>");
        result.Stats.AttributeOccurrences.Should().Be(2);
    }

    [Fact]
    public void Remove_deletes_a_whole_css_rule_whose_selector_was_only_the_class()
    {
        // Arrange
        ClassRemover sut = Remover("", ".c1 { color: red }\n.keep { color: blue }");

        // Act
        ClassRemovalResult result = sut.Remove("c1");

        // Assert
        // The newline that separated the two rules is not part of the removed rule and stays, like CssRuleMerger.
        result.ChangedTexts["styles/a.css"].Should().Be("\n.keep { color: blue }");
        result.Stats.RulesRemoved.Should().Be(1);
    }

    [Fact]
    public void Remove_strips_a_compound_selector_but_keeps_the_rule()
    {
        // Arrange
        ClassRemover sut = Remover("", ".c1.keep { color: red }");

        // Act
        ClassRemovalResult result = sut.Remove("c1");

        // Assert
        result.ChangedTexts["styles/a.css"].Should().Be(".keep { color: red }");
        result.Stats.RulesRemoved.Should().Be(0);
    }

    [Fact]
    public void Remove_strips_a_type_selector_combination_but_keeps_the_element_part()
    {
        // Arrange
        ClassRemover sut = Remover("", "p.c1 { color: red }");

        // Act
        ClassRemovalResult result = sut.Remove("c1");

        // Assert
        result.ChangedTexts["styles/a.css"].Should().Be("p { color: red }");
    }

    [Fact]
    public void Remove_drops_only_the_matching_member_of_a_selector_group()
    {
        // Arrange
        ClassRemover sut = Remover("", ".c1, .keep { color: red }");

        // Act
        ClassRemovalResult result = sut.Remove("c1");

        // Assert
        result.ChangedTexts["styles/a.css"].Should().Be(".keep { color: red }");
        result.Stats.RulesRemoved.Should().Be(0);
    }

    [Fact]
    public void Remove_handles_a_style_block_of_an_xhtml_file_too()
    {
        // Arrange
        ClassRemover sut = Remover(
            "<html><head><style>.c1 { color: red }</style></head>"
            + "<body><p class=\"c1\">x</p></body></html>",
            "");

        // Act
        ClassRemovalResult result = sut.Remove("c1");

        // Assert
        string text = result.ChangedTexts["text/ch1.xhtml"];
        text.Should().NotContain("c1");
        result.Stats.RulesRemoved.Should().Be(1);
        result.Stats.AttributeOccurrences.Should().Be(1);
        result.Stats.ChangedFiles.Should().Be(1);
    }

    [Fact]
    public void Remove_leaves_files_that_do_not_use_the_class_untouched()
    {
        // Arrange
        ClassRemover sut = Remover("<html><body><p class=\"other\">x</p></body></html>", ".other { color: red }");

        // Act
        ClassRemovalResult result = sut.Remove("c1");

        // Assert
        result.ChangedTexts.Should().BeEmpty();
        result.Stats.Should().Be(new ClassRemovalStats(0, 0, 0));
    }

    [Fact]
    public void FindUsages_and_SummarizeUsages_report_every_occurrence_by_file()
    {
        // Arrange
        ClassRemover sut = Remover(
            "<html><body><p class=\"c1\">a</p><p class=\"c1\">b</p></body></html>",
            ".c1 { color: red }");

        // Act
        IReadOnlyList<ClassUsage> usages = sut.FindUsages("c1");
        IReadOnlyList<ClassUsageFileSummary> summary = sut.SummarizeUsages("c1");

        // Assert
        usages.Should().HaveCount(3);
        summary.Should().BeEquivalentTo(new[]
        {
            new ClassUsageFileSummary("styles/a.css", 1),
            new ClassUsageFileSummary("text/ch1.xhtml", 2),
        });
    }

    // ---- Book ----

    [Fact]
    public void Book_applies_a_removal_across_the_html_file_and_its_stylesheet()
    {
        // Arrange
        using TempDir temp = new();
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(chapterPath, File.ReadAllText(chapterPath).Replace("<p>", "<p class=\"c1\">", StringComparison.Ordinal));
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.c1 { color: red }\n");
        using Book book = new ImportEpub(EpubBuilder.BuildInto(tree, temp, "book.epub")).GetBook();
        CssResource css = book.GetCssResources().Single();

        // Act
        ClassRemover remover = book.PrepareClassRemoval().Remover!;
        ClassRemovalResult result = remover.Remove("c1");

        // Assert
        book.ApplyClassRemoval(result).Should().BeTrue();
        book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().NotContain("c1");
        css.GetText().Should().NotContain("c1");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void Book_refuses_to_prepare_a_removal_when_an_xhtml_file_is_not_well_formed()
    {
        // Arrange
        using TempDir temp = new();
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(chapterPath, File.ReadAllText(chapterPath).Replace("</p>", string.Empty, StringComparison.Ordinal));
        using Book book = new ImportEpub(EpubBuilder.BuildInto(tree, temp, "book.epub")).GetBook();

        // Act
        ClassRemovalPreparation result = book.PrepareClassRemoval();

        // Assert
        result.Remover.Should().BeNull();
        result.NotWellFormed.Should().BeOfType<HtmlResource>().Which.Filename.Should().Be("chapter1.xhtml");
    }
}
