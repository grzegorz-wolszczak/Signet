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
