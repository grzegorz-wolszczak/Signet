using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="IdUsageFinder"/> — "Find Usages (Id)" of an id in the whole book.</summary>
public sealed class IdUsageFinderTests
{
    private const string Chapter =
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n"
        + "<head><style>#note { color: red }\np#other { margin: 0 }</style></head>\n"
        + "<body>\n"
        + "<p id=\"note\">a</p><span id=\"Note\">b</span>\n"
        + "<p data-x=\"note\">d</p>\n"
        + "</body>\n</html>\n";

    private const string Styles =
        "/* #note in a comment */\n"
        + "div#note > p, #x { color: blue }\n"
        + "@media print {\n  #note:hover { color: black }\n}\n"
        + "p:not(#note) { margin: 1em }\n"
        + "a[title=\"#note\"] { }\n";

    private static IReadOnlyList<ClassUsage> Find(string id) =>
        IdUsageFinder.Find(id, new[]
        {
            new ClassUsageSource("OEBPS/Text/ch1.xhtml", Chapter, IsStyleSheet: false),
            new ClassUsageSource("OEBPS/Styles/main.css", Styles, IsStyleSheet: true),
        });

    [Fact]
    public void Finds_the_exact_id_attribute_and_every_hash_selector_in_files_and_style_blocks()
    {
        // Act
        IReadOnlyList<ClassUsage> usages = Find("note");

        // Assert
        usages.Select(u => (u.BookPath, u.Kind)).Should().Equal(
            ("OEBPS/Text/ch1.xhtml", ClassUsageKind.Selector),
            ("OEBPS/Text/ch1.xhtml", ClassUsageKind.IdAttribute),
            ("OEBPS/Styles/main.css", ClassUsageKind.Selector),
            ("OEBPS/Styles/main.css", ClassUsageKind.Selector),
            ("OEBPS/Styles/main.css", ClassUsageKind.Selector));
    }

    [Fact]
    public void Matching_is_case_sensitive_and_a_whole_value_only()
    {
        // Act & Assert
        Find("Note").Should().ContainSingle().Which.Kind.Should().Be(ClassUsageKind.IdAttribute);

        // Act & Assert
        Find("not").Should().BeEmpty();

        // Act & Assert
        Find("missing").Should().BeEmpty();
    }

    [Fact]
    public void A_name_inside_tags_without_an_id_attribute_or_in_text_is_not_a_usage()
    {
        // Arrange
        const string text = "<html><body><p title=\"note\">note</p><p data-note=\"x\" id=\"x\">y</p></body></html>";

        // Act & Assert
        IdUsageFinder.Find("note", new[] { new ClassUsageSource("OEBPS/Text/a.xhtml", text, IsStyleSheet: false) })
            .Should().BeEmpty();
    }

    [Fact]
    public void Every_usage_carries_the_text_of_its_line_without_the_indentation()
    {
        // Act
        IReadOnlyList<ClassUsage> usages = Find("note");

        // Assert
        usages[1].Context.Should().Be("<p id=\"note\">a</p><span id=\"Note\">b</span>");
        usages[3].Context.Should().Be("#note:hover { color: black }", "the indentation is left out");
    }

    [Fact]
    public void The_context_tells_where_the_id_is_in_it()
    {
        // Arrange
        const string text = "<p>\n    <span id=\"intro\">x</span>\n</p>";

        // Act
        ClassUsage usage = IdUsageFinder.Find("intro", new[] { new ClassUsageSource("a.xhtml", text, IsStyleSheet: false) }).Single();

        // Assert
        usage.Context.Substring(usage.ContextMatchStart, usage.ContextMatchLength).Should().Be("intro");
    }

    [Fact]
    public void The_context_of_a_selector_marks_the_name_after_the_hash()
    {
        // Arrange
        const string css = "  p#note, #note > b { color: red }";

        // Act
        ClassUsage first = IdUsageFinder.Find("note", new[] { new ClassUsageSource("s.css", css, IsStyleSheet: true) }).First();

        // Assert
        first.Context.Should().StartWith("p#note");
        first.Context.Substring(first.ContextMatchStart, first.ContextMatchLength).Should().Be("note");
    }

    [Fact]
    public void FindIdAtCaret_finds_the_value_when_the_caret_is_inside_it()
    {
        // Arrange
        const string text = "<html><body><p id=\"note\">x</p></body></html>";

        // Act & Assert
        IdUsageFinder.FindIdAtCaret(text, text.IndexOf("not", StringComparison.Ordinal) + 1).Should().Be("note");
    }

    [Fact]
    public void FindIdAtCaret_is_null_outside_the_attribute_value_and_for_other_attributes()
    {
        // Arrange
        const string withId = "<html><body><p id=\"note\">x</p></body></html>";
        const string withClass = "<html><body><p class=\"note\">x</p></body></html>";

        // Act & Assert
        IdUsageFinder.FindIdAtCaret(withId, withId.IndexOf('x')).Should().BeNull("the caret is in the element content, not the attribute");

        // Act & Assert
        IdUsageFinder.FindIdAtCaret(withClass, withClass.IndexOf("not", StringComparison.Ordinal) + 1)
            .Should().BeNull("the attribute is class, not id");
    }

    [Fact]
    public void FindIdAtCaretInSelector_finds_the_name_at_or_right_after_the_hash()
    {
        // Arrange
        const string css = "#note, p.other { color: red }";

        // Act & Assert
        IdUsageFinder.FindIdAtCaretInSelector(css, css.IndexOf("note", StringComparison.Ordinal), isStyleSheet: true).Should().Be("note");

        // Act & Assert
        IdUsageFinder.FindIdAtCaretInSelector(css, css.IndexOf('#'), isStyleSheet: true).Should().Be("note");

        // Act & Assert
        IdUsageFinder.FindIdAtCaretInSelector(css, css.IndexOf("other", StringComparison.Ordinal), isStyleSheet: true).Should().BeNull();
    }

    [Fact]
    public void FindIdAtCaretInSelector_reads_style_blocks_of_an_xhtml_file_too()
    {
        // Arrange
        const string html = "<html><head><style>#note { color: red }</style></head><body/></html>";

        // Act & Assert
        IdUsageFinder.FindIdAtCaretInSelector(html, html.IndexOf("note", StringComparison.Ordinal), isStyleSheet: false).Should().Be("note");
    }

    // ---- Book ----

    [Fact]
    public void Book_finds_usages_across_the_html_file_and_its_stylesheet()
    {
        // Arrange
        using TempDir temp = new();
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(chapterPath, File.ReadAllText(chapterPath).Replace("<p>", "<p id=\"intro\">", StringComparison.Ordinal));
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n#intro { color: red }\n");
        using Book book = new ImportEpub(EpubBuilder.BuildInto(tree, temp, "book.epub")).GetBook();

        // Act
        IReadOnlyList<ClassUsage> usages = book.FindIdUsages("intro");

        // Assert
        usages.Select(u => u.Kind).Should().Equal(ClassUsageKind.IdAttribute, ClassUsageKind.Selector);
    }
}
