using System;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="Book.SetFontObfuscation"/> (the obfuscation toggle in FontTab).</summary>
public sealed class BookFontObfuscationTests
{
    private static (Book Book, FontResource Font) LoadWithFont(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        Book book = new ImportEpub(epub).GetBook();
        FontResource font = book.GetAllResources().OfType<FontResource>().First();
        return (book, font);
    }

    [Fact]
    public void SetFontObfuscation_toggles_algorithm_and_marks_book_modified()
    {
        // Arrange
        using TempDir temp = new();
        (Book book, FontResource font) = LoadWithFont(temp);

        font.ObfuscationAlgorithm.Should().BeEmpty();

        // Act
        book.SetFontObfuscation(font, OcfReader.IdpfFontAlgorithmId);

        // Assert
        font.ObfuscationAlgorithm.Should().Be(OcfReader.IdpfFontAlgorithmId);
        book.Modified.Should().BeTrue();
        book.HasObfuscatedFonts().Should().BeTrue();

        // Act
        book.SetFontObfuscation(font, string.Empty);

        // Assert
        font.ObfuscationAlgorithm.Should().BeEmpty();
        book.HasObfuscatedFonts().Should().BeFalse();

        book.Dispose();
    }

    [Fact]
    public void SetFontObfuscation_rejects_an_unknown_algorithm()
    {
        // Arrange
        using TempDir temp = new();
        (Book book, FontResource font) = LoadWithFont(temp);

        // Act
        Action act = () => book.SetFontObfuscation(font, "http://example.com/made-up");

        // Act & Assert
        act.Should().Throw<ArgumentException>();
        book.Dispose();
    }
}
