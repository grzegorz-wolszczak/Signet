using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Toc;

/// <summary>
/// Tests of <see cref="Headings"/> and <see cref="HeadingSelectorModel"/>.
/// </summary>
public sealed class HeadingsTests
{
    private static Book Load(TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp)).GetBook();

    private static HtmlResource Chapter(Book book) =>
        book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");

    private static void SetBody(Book book, string body)
    {
        HtmlResource chapter = Chapter(book);
        string text = chapter.GetText();
        int start = text.IndexOf("<body", System.StringComparison.Ordinal);
        int end = text.IndexOf("</body>", System.StringComparison.Ordinal) + "</body>".Length;
        chapter.SetText(text[..start] + body + text[end..]);
    }

    [Fact]
    public void Builds_a_hierarchy_from_a_flat_list()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        SetBody(book, "<body>\n<h1>A</h1>\n<h3>A sub</h3>\n<h1>B</h1>\n</body>");

        // Act
        var tree = Headings.MakeHeadingHierarchy(Headings.GetHeadingList(book.GetHtmlResourcesExcludingNav()));

        // Assert
        tree.Select(h => h.Text).Should().Equal("A", "B");
        tree[0].Children.Should().ContainSingle().Which.Text.Should().Be("A sub");
    }

    [Fact]
    public void Only_the_first_heading_near_the_body_is_at_file_start()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        SetBody(book, "<body>\n<h1>First</h1>\n<h1>Second</h1>\n</body>");

        // Act
        var list = Headings.GetHeadingList(book.GetHtmlResourcesExcludingNav());

        // Assert
        list[0].AtFileStart.Should().BeTrue();
        list[1].AtFileStart.Should().BeFalse();
    }

    [Fact]
    public void Excluded_headings_are_filtered_unless_requested()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        SetBody(book, "<body>\n<h1>Keep</h1>\n<h1 class=\"signet_not_in_toc\">Drop</h1>\n</body>");

        // Act & Assert
        Headings.GetHeadingList(book.GetHtmlResourcesExcludingNav())
            .Select(h => h.Text).Should().Equal("Keep");

        // Act & Assert
        Headings.GetHeadingList(book.GetHtmlResourcesExcludingNav(), includeUnwantedHeadings: true)
            .Select(h => h.Text).Should().Equal("Keep", "Drop");
    }

    [Fact]
    public void Heading_text_prefers_the_title_attribute()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        SetBody(book, "<body>\n<h1 title=\"Nice Name\">Ugly  markup</h1>\n</body>");

        // Act & Assert
        Headings.GetHeadingList(book.GetHtmlResourcesExcludingNav())[0].Text.Should().Be("Nice Name");
    }

    [Fact]
    public void Selector_model_up_to_level_toggles_inclusion()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        SetBody(book, "<body>\n<h1>A</h1>\n<h2>B</h2>\n<h3>C</h3>\n</body>");
        HeadingSelectorModel model = new(book);

        // Act
        model.SetAllHeadingInclusion(2);

        var flat = Headings.GetFlattenedHeadings(model.RootHeadings);

        // Assert
        flat.Single(h => h.Text == "A").IncludeInToc.Should().BeTrue();
        flat.Single(h => h.Text == "B").IncludeInToc.Should().BeTrue();
        flat.Single(h => h.Text == "C").IncludeInToc.Should().BeFalse();
    }

    [Fact]
    public void Selector_model_change_level_rewrites_the_tag_on_apply()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        SetBody(book, "<body>\n<h1>A</h1>\n<h2>B</h2>\n</body>");
        HeadingSelectorModel model = new(book);

        Heading b = Headings.GetFlattenedHeadings(model.RootHeadings).Single(h => h.Text == "B");

        // Act
        model.ChangeHeadingLevel(b, -1).Should().BeTrue();
        model.Apply().Should().BeTrue();

        // Assert
        Chapter(book).GetText().Should().Contain(">B</h1>").And.NotContain("<h2");
    }

    private static (HeadingSelectorModel Model, Heading A, Heading B, Heading C) Nested(Book book, int topLevel)
    {
        SetBody(book, $"<body>\n<h{topLevel}>A</h{topLevel}>\n<h{topLevel + 1}>B</h{topLevel + 1}>\n"
            + $"<h{topLevel + 2}>C</h{topLevel + 2}>\n</body>");
        HeadingSelectorModel model = new(book);
        Heading[] flat = Headings.GetFlattenedHeadings(model.RootHeadings).ToArray();
        return (model, flat.Single(h => h.Text == "A"), flat.Single(h => h.Text == "B"), flat.Single(h => h.Text == "C"));
    }

    [Theory]
    [InlineData(false, 2, 2, 3)]
    [InlineData(true, 2, 3, 4)]
    public void Selector_model_change_level_moves_the_descendants_only_when_asked(
        bool updateChildHeadings, int expectedA, int expectedB, int expectedC)
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        (HeadingSelectorModel model, Heading a, Heading b, Heading c) = Nested(book, topLevel: 1);

        // Act
        bool changed = model.ChangeHeadingLevel(a, 1, updateChildHeadings);

        // Assert
        changed.Should().BeTrue();
        (a.Level, b.Level, c.Level).Should().Be((expectedA, expectedB, expectedC));
    }

    [Fact]
    public void Selector_model_change_level_refuses_to_push_a_descendant_past_h6()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        (HeadingSelectorModel model, Heading a, Heading b, Heading c) = Nested(book, topLevel: 4);

        // Act
        bool changed = model.ChangeHeadingLevel(a, 1, updateChildHeadings: true);

        // Assert
        changed.Should().BeFalse();
        (a.Level, b.Level, c.Level).Should().Be((4, 5, 6));
        model.HasHeadingLevelChanges.Should().BeFalse();
    }

    [Fact]
    public void Selector_model_change_level_leaves_the_listed_descendants_where_they_are()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        (HeadingSelectorModel model, Heading a, Heading b, Heading c) = Nested(book, topLevel: 4);

        // Act
        bool changed = model.ChangeHeadingLevel(a, 1, updateChildHeadings: true, new HashSet<Heading> { c });

        // Assert
        changed.Should().BeTrue();
        (a.Level, b.Level, c.Level).Should().Be((5, 6, 6));
    }

    [Fact]
    public void Selector_model_reports_level_changes_until_they_are_undone()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        (HeadingSelectorModel model, Heading a, _, _) = Nested(book, topLevel: 1);

        // Act & Assert
        model.HasHeadingLevelChanges.Should().BeFalse();

        // Act
        model.ChangeHeadingLevel(a, 1, updateChildHeadings: true);

        // Assert
        model.HasHeadingLevelChanges.Should().BeTrue();

        // Act
        model.ChangeHeadingLevel(a, -1, updateChildHeadings: true);

        // Assert
        model.HasHeadingLevelChanges.Should().BeFalse();
    }

    [Fact]
    public void Heading_source_offset_points_at_its_opening_tag()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        SetBody(book, "<body>\n<h1>A</h1>\n<p>text</p>\n<h2 id=\"b\">B</h2>\n</body>");
        string text = Chapter(book).GetText();

        // Act
        var list = Headings.GetHeadingList(book.GetHtmlResourcesExcludingNav());

        // Assert
        list.Select(h => text[h.SourceOffset..].Split('>')[0] + ">").Should().Equal("<h1>", "<h2 id=\"b\">");
    }

    [Fact]
    public void Selector_model_excluding_a_heading_writes_the_not_in_toc_class()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        SetBody(book, "<body>\n<h1>A</h1>\n<h2>B</h2>\n</body>");
        HeadingSelectorModel model = new(book);

        Heading b = Headings.GetFlattenedHeadings(model.RootHeadings).Single(h => h.Text == "B");

        // Act
        model.SetInclusion(b, false);
        model.Apply().Should().BeTrue();

        // Assert
        Chapter(book).GetText().Should().Contain("signet_not_in_toc");
    }

    [Fact]
    public void Selector_model_apply_without_changes_returns_false()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp);
        SetBody(book, "<body>\n<h1>Only</h1>\n</body>");
        HeadingSelectorModel model = new(book);

        // Act & Assert
        model.Apply().Should().BeFalse();
    }
}
