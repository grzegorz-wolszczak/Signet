using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

/// <summary>Well-formedness of BCP 47 language tags, with the error texts of Java's locale builder.</summary>
public sealed class LanguageTagTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("fr-CA")]
    [InlineData("eng")]
    [InlineData("zh-Hant-TW")]
    [InlineData("zh-cmn-Hans-CN")]
    [InlineData("sl-rozaj-biske")]
    [InlineData("de-CH-1901")]
    [InlineData("en-a-bbb-x-a-ccc")]
    [InlineData("x-private")]
    [InlineData("en-x-twain")]
    [InlineData("es-419")]
    [InlineData("i-klingon")]
    [InlineData("art-lojban")]
    [InlineData("EN-us")]
    public void Validate_WellFormedTag_IsAccepted(string tag)
    {
        // Act & Assert
        LanguageTag.Validate(tag).Should().BeNull();
    }

    [Theory]
    [InlineData("", "Empty subtag [at index 0]")]
    [InlineData("e", "Invalid subtag: e [at index 0]")]
    [InlineData("123", "Invalid subtag: 123 [at index 0]")]
    [InlineData("en-", "Empty subtag [at index 3]")]
    [InlineData("en--US", "Empty subtag [at index 3]")]
    [InlineData(" en", "Invalid subtag:  en [at index 0]")]
    [InlineData("en ", "Invalid subtag: en  [at index 0]")]
    [InlineData("en_US", "Invalid subtag: en_US [at index 0]")]
    [InlineData("en-US-toolongvariant", "Invalid subtag: toolongvariant [at index 6]")]
    [InlineData("en-a", "Incomplete extension 'a' [at index 3]")]
    [InlineData("en-x", "Incomplete privateuse [at index 3]")]
    [InlineData("en-a-x-b", "Incomplete extension 'a' [at index 3]")]
    public void Validate_IllFormedTag_ExplainsWhereAndWhy(string tag, string message)
    {
        // Act & Assert
        LanguageTag.Validate(tag).Should().Be(message);
    }
}
