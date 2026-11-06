using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

// What java.net.URI(String) of the JDK accepts and rejects.
public sealed class JavaUriTests
{
    [Theory]
    [InlineData("http://example.org/foo#")]
    [InlineData("http://example.org:8080/a/b?c=d#e")]
    [InlineData("urn:isbn:9780000000000")]
    [InlineData("mailto:someone@example.org")]
    [InlineData("relative/path")]
    [InlineData("")]
    [InlineData("http://[::1]/")]
    [InlineData("http://user@host/")]
    [InlineData("http://ex%41mple.org/")]
    [InlineData("http://example.org/café")] // unescaped non-ASCII is allowed
    [InlineData("file:///tmp/x")]
    [InlineData("http://example.org:80a/")] // not a server: a registry-based authority
    public void IsValid_JavaAcceptsIt_IsTrue(string value)
    {
        // Act
        bool valid = JavaUri.IsValid(value);

        // Assert
        valid.Should().BeTrue();
    }

    [Theory]
    [InlineData("[bad]")] // '[' outside a host
    [InlineData(":foo")] // no scheme name
    [InlineData("1abc:foo")] // a scheme starts with a letter
    [InlineData("http:")] // no scheme-specific part
    [InlineData("http://exa mple.org/")]
    [InlineData("http://example.org/%4")] // malformed escape
    [InlineData("http://example.org/a#b#c")] // '#' in the fragment
    [InlineData("http://example.org/{x}")]
    [InlineData("http://[::zz]/")] // not an IPv6 address
    [InlineData("http://[::1/")] // no closing bracket
    [InlineData("http://example.org:80a/x[")]
    [InlineData("http://example.org/\u0080")] // U+0080 is not let through (Java: c > 128)
    public void IsValid_JavaRejectsIt_IsFalse(string value)
    {
        // Act
        bool valid = JavaUri.IsValid(value);

        // Assert
        valid.Should().BeFalse();
    }
}
