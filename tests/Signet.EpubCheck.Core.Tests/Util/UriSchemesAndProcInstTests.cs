using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

public sealed class UriSchemesAndProcInstTests
{
    [Theory]
    [InlineData("http", true)]
    [InlineData("HTTPS", true)]
    [InlineData("mailto", true)]
    [InlineData("iris.beep", true)]
    [InlineData("z39.50r", true)]
    [InlineData("data", true)]
    [InlineData("zzz", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Contains_IsTheListOfUpstream(string? scheme, bool expected)
    {
        // Act
        bool known = UriSchemes.Contains(scheme);

        // Assert
        known.Should().Be(expected);
    }

    [Theory]
    [InlineData("type=\"text/css\" href=\"a.css\"", "href", "a.css")]
    [InlineData("type='text/css' href='a.css'", "type", "text/css")]
    [InlineData("type = \"text/css\"   href\t=\n'a.css'", "href", "a.css")]
    [InlineData("href=\"a&amp;b.css\"", "href", "a&b.css")]
    [InlineData("href=\"&#x41;&#66;&lt;&quot;\"", "href", "AB<\"")]
    [InlineData("href=\"a.css\"", "type", null)]
    [InlineData("xhref=\"a.css\" href=\"b.css\"", "href", "b.css")]
    [InlineData("href=a.css", "href", null)]
    [InlineData("href=\"a.css", "href", null)]
    [InlineData("href", "href", null)]
    public void GetPseudoAttribute_ReadsTheNamedValue(string content, string name, string? expected)
    {
        // Act
        string? value = ProcInstParser.GetPseudoAttribute(content, name);

        // Assert
        value.Should().Be(expected);
    }
}
