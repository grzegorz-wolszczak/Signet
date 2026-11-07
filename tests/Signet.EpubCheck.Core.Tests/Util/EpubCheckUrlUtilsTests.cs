using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

public sealed class EpubCheckUrlUtilsTests
{
    // Upstream's URLUtilsTest.
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("hello", "hello")]
    [InlineData("a space", "a%20space")]
    [InlineData("a#hash", "a%23hash")]
    [InlineData("a%20space", "a%2520space")]
    [InlineData("a/path", "a/path")]
    [InlineData("a?path", "a%3Fpath")]
    public void EncodePath_EncodesWhatIsNotAUrlCodePoint(string? value, string? expected)
    {
        // Act
        string? encoded = EpubCheckUrlUtils.EncodePath(value);

        // Assert
        encoded.Should().Be(expected);
    }

    [Theory]
    [InlineData("file:///C:/path")]
    [InlineData("file:///C|/path")]
    [InlineData("file:///C|a/path")]
    [InlineData("file:///path/C|/path")]
    public void Parse_FileUrlsWithDriveLetters_Succeed(string value)
    {
        // Act & Assert (upstream's testToHumanLocation, which only parses)
        FluentActions.Invoking(() => Url.Parse(value)).Should().NotThrow();
    }

    [Theory]
    [InlineData("http://example.org/a", "http://example.org/b", true)]
    [InlineData("http://example.org/a", "https://example.org/a", false)]
    [InlineData("http://example.org/a", "http://example.org:8080/a", false)]
    [InlineData("urn:a", "urn:b", false)]
    [InlineData("file:///a/b.xhtml", "file:///a/c.xhtml", true)]
    public void IsSameOrigin_ComparesSchemeHostAndPort(string a, string b, bool expected)
    {
        // Act
        bool same = EpubCheckUrlUtils.IsSameOrigin(Url.Parse(a), Url.Parse(b));

        // Assert
        same.Should().Be(expected);
    }

    [Fact]
    public void IsRemote_DataUrl_IsNotRemote()
    {
        // Arrange
        Url local = Url.Parse("file:///book/a.xhtml");

        // Act
        bool data = EpubCheckUrlUtils.IsRemote(Url.Parse("data:image/png;base64,AAAA"), local);
        bool http = EpubCheckUrlUtils.IsRemote(Url.Parse("http://example.org/"), local);

        // Assert
        data.Should().BeFalse();
        http.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://example.org/", true)]
    [InlineData("chapter.xhtml", false)]
    [InlineData("http://exa mple.org/", true)] // fails for another reason than the scheme
    public void IsAbsoluteUrlString_MissingSchemeOnly_IsRelative(string value, bool expected)
    {
        // Act
        bool absolute = EpubCheckUrlUtils.IsAbsoluteUrlString(value);

        // Assert
        absolute.Should().Be(expected);
    }

    [Fact]
    public void Normalize_DecodesAndComposesThePath()
    {
        // Arrange
        Url url = Url.Parse("http://example.org/caf%65%CC%81/%7Euser?%41#%5F");

        // Act
        Url normalized = EpubCheckUrlUtils.Normalize(url)!;

        // Assert
        normalized.ToString().Should().Be("http://example.org/caf%C3%A9/~user?A#_");
    }

    [Theory]
    [InlineData("http://example.org/a/b.xhtml?q=1#f", "http://example.org/a/b.xhtml?q=1#f")] // nothing to normalize: the same URL
    [InlineData("http://example.org/a/../b/./c.xhtml", "http://example.org/b/c.xhtml")]
    [InlineData("http://example.org/a%20b/%7Ec%2Fd", "http://example.org/a%20b/~c/d")] // the whole path is decoded, so %2F becomes a slash
    [InlineData("http://example.org/%C3%A4", "http://example.org/%C3%A4")]
    [InlineData("http://example.org/", "http://example.org/")]
    [InlineData("mailto:someone@example.org", "mailto:someone@example.org")]
    public void Normalize_KeepsWhatIsNormalAndFixesWhatIsNot(string value, string expected)
    {
        // Arrange
        Url url = Url.Parse(value);

        // Act
        string normalized = EpubCheckUrlUtils.Normalize(url)!.ToString();

        // Assert
        normalized.Should().Be(expected);
        EpubCheckUrlUtils.Normalize(Url.Parse(normalized))!.ToString().Should().Be(expected, "normalizing twice changes nothing");
    }

    [Theory]
    [InlineData("data:image/png;base64,AAAA", "image/png")]
    [InlineData("data:text/plain,hello", "text/plain")]
    [InlineData("http://example.org/", null)]
    public void GetDataUrlType_IsTheMediaTypeOfADataUrl(string value, string? expected)
    {
        // Act
        string? type = EpubCheckUrlUtils.GetDataUrlType(Url.Parse(value));

        // Assert
        type.Should().Be(expected);
    }

    [Fact]
    public void DocUrl_OfAnOpaqueUrl_StaysOpaque()
    {
        // Arrange (galimatias' withFragment(null) made it hierarchical: "urn://")
        Url url = Url.Parse("urn:isbn:123#part");

        // Act
        Url document = EpubCheckUrlUtils.DocUrl(url);

        // Assert
        document.ToString().Should().Be("urn:isbn:123");
    }

    [Fact]
    public void Relativize_PathWithRegexCharacters_IsRelative()
    {
        // Arrange (galimatias used String.replaceFirst, a regular expression)
        Url container = Url.Parse("file:///book(1)/");
        Url resource = Url.Parse("file:///book(1)/EPUB/a.xhtml#x");

        // Act
        string relative = container.Relativize(resource);

        // Assert
        relative.Should().Be("EPUB/a.xhtml#x");
    }

    [Fact]
    public void Equals_UrlsWithDifferentPaths_AreDifferent()
    {
        // Arrange (galimatias compared the path of one with the host of the other)
        Url a = Url.Parse("http://example.org/a");
        Url b = Url.Parse("http://example.org/b");

        // Act
        bool equal = a.Equals(b);

        // Assert
        equal.Should().BeFalse();
    }

    [Fact]
    public void ToHumanString_DecodesThePathAndTheDomain()
    {
        // Arrange
        Url url = Url.Parse("http://xn--caf-dma.example/r%C3%A9sum%C3%A9");

        // Act
        string human = url.ToHumanString();

        // Assert
        human.Should().Be("http://caf\u00E9.example/r\u00E9sum\u00E9");
    }

    [Fact]
    public void FileUrlOfAPath_GoesBackToThePath()
    {
        // Arrange
        string path = System.IO.Path.GetFullPath("book dir/a.xhtml");

        // Act
        string back = EpubCheckUrlUtils.ToFilePath(EpubCheckUrlUtils.ToUrl(path));

        // Assert
        back.Should().Be(path);
    }
}
