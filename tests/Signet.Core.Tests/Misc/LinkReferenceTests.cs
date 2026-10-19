using AwesomeAssertions;
using Signet.Core.Misc;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>Tests of resolving <c>href</c>/<c>src</c>/<c>url()</c> references.</summary>
public sealed class LinkReferenceTests
{
    [Theory]
    [InlineData("http://example.com/a", true)]
    [InlineData("mailto:a@b.c", true)]
    [InlineData("//cdn.example.com/x.css", true)]
    [InlineData("../Text/a.xhtml", false)]
    [InlineData("a.xhtml#b:c", false)]
    [InlineData("#note1", false)]
    public void External_references_are_recognised(string reference, bool external)
    {
        // Act & Assert
        LinkReference.IsExternal(reference).Should().Be(external);
    }

    [Fact]
    public void Split_removes_query_and_decodes_path_and_fragment()
    {
        // Act & Assert
        LinkReference.Split("a%20b.xhtml?x=1#r%C3%B3%C5%BCa")
            .Should().Be(("a b.xhtml", "róża"));
    }

    [Theory]
    [InlineData("../Images/a%20b.png", "OEBPS/Text", "OEBPS/Images/a b.png")]
    [InlineData("ch2.xhtml#x", "OEBPS/Text", "OEBPS/Text/ch2.xhtml")]
    [InlineData("Text/a.xhtml", "", "Text/a.xhtml")]
    [InlineData("#x", "OEBPS/Text", null)]
    [InlineData("https://example.com", "OEBPS/Text", null)]
    public void ResolveBookPath_resolves_relative_to_the_owner_folder(string reference, string folder, string? expected)
    {
        // Act & Assert
        LinkReference.ResolveBookPath(reference, folder).Should().Be(expected);
    }

    [Theory]
    [InlineData("n1", "<h2")]
    [InlineData("old", "<a ")]
    [InlineData("missing", null)]
    [InlineData("", null)]
    public void FindAnchorOffset_points_at_the_element_with_the_id_or_name(string fragment, string? element)
    {
        // Arrange
        const string text = "<body><p>x</p><h2 id=\"n1\">T</h2><a name='old'/></body>";

        int expected = element is null ? -1 : text.IndexOf(element, System.StringComparison.Ordinal);

        // Act & Assert
        LinkReference.FindAnchorOffset(text, fragment).Should().Be(expected);
    }

    [Fact]
    public void TargetExists_asks_only_for_book_files()
    {
        // Act & Assert
        LinkReference.TargetExists("#x", "OEBPS", _ => false).Should().BeTrue();

        // Act & Assert
        LinkReference.TargetExists("http://a.b", "OEBPS", _ => false).Should().BeTrue();

        // Act & Assert
        LinkReference.TargetExists("a.css", "OEBPS/Styles", p => p == "OEBPS/Styles/a.css").Should().BeTrue();

        // Act & Assert
        LinkReference.TargetExists("b.css", "OEBPS/Styles", p => p == "OEBPS/Styles/a.css").Should().BeFalse();
    }
}
