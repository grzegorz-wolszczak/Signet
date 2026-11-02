using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Hyphenation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Hyphenation;

/// <summary>
/// Tests of <see cref="HyphenationPatternFiles"/>: which installed file of patterns a language of a book is hyphenated
/// with.
/// </summary>
public sealed class HyphenationPatternFilesTests : IDisposable
{
    private readonly TempDir _dir = new("hyph-files-");

    public void Dispose() => _dir.Dispose();

    private HyphenationPatternFiles Installed(params string[] names)
    {
        foreach (string name in names)
        {
            File.WriteAllText(_dir.Combine(name), "UTF-8\n1a\n");
        }

        return new HyphenationPatternFiles(_dir.Path);
    }

    [Fact]
    public void Only_the_files_of_hyphenation_patterns_are_listed_with_their_language()
    {
        // Arrange
        HyphenationPatternFiles sut = Installed("hyph_pl_PL.dic", "pl_PL.dic", "pl_PL.aff", "th_pl_PL_v2.dat", "hyph_de_CH.dic");

        // Act
        var languages = sut.Installed.Select(f => f.Language);

        // Assert
        languages.Should().Equal("de-CH", "pl-PL");
    }

    [Fact]
    public void A_language_with_its_region_takes_the_file_of_that_region()
    {
        // Arrange
        HyphenationPatternFiles sut = Installed("hyph_en_GB.dic", "hyph_en_US.dic");

        // Act
        HyphenationMatch match = sut.Match("en-US");

        // Assert
        match.File!.FileName.Should().Be("hyph_en_US.dic");
        match.IsFallback.Should().BeFalse();
    }

    [Fact]
    public void Case_and_underscores_of_the_tag_do_not_matter()
    {
        // Arrange
        HyphenationPatternFiles sut = Installed("hyph_pl_PL.dic");

        // Act
        HyphenationMatch match = sut.Match("PL_pl");

        // Assert
        match.File!.FileName.Should().Be("hyph_pl_PL.dic");
        match.IsFallback.Should().BeFalse();
    }

    [Fact]
    public void A_language_without_a_region_takes_a_file_of_the_language_without_a_warning()
    {
        // Arrange
        HyphenationPatternFiles sut = Installed("hyph_de_AT.dic", "hyph_de_DE.dic");

        // Act
        HyphenationMatch match = sut.Match("de");

        // Assert
        match.File!.FileName.Should().Be("hyph_de_DE.dic", "the region that is the language itself goes first");
        match.IsFallback.Should().BeFalse("the book named no region");
    }

    [Fact]
    public void A_region_without_a_file_takes_another_region_of_the_language_with_a_warning()
    {
        // Arrange
        HyphenationPatternFiles sut = Installed("hyph_en_US.dic");

        // Act
        HyphenationMatch match = sut.Match("en-GB");

        // Assert
        match.CanHyphenate.Should().BeTrue();
        match.File!.FileName.Should().Be("hyph_en_US.dic");
        match.IsFallback.Should().BeTrue();
        match.ExpectedFileName.Should().Be("hyph_en_GB.dic");
    }

    [Fact]
    public void A_language_without_a_file_names_the_file_it_needs()
    {
        // Arrange
        HyphenationPatternFiles sut = Installed("hyph_pl_PL.dic");

        // Act
        HyphenationMatch withRegion = sut.Match("de-DE");
        HyphenationMatch withoutRegion = sut.Match("de");

        // Assert
        withRegion.CanHyphenate.Should().BeFalse();
        withRegion.ExpectedFileName.Should().Be("hyph_de_DE.dic");
        withoutRegion.ExpectedFileName.Should().Be("hyph_de_*.dic");
    }

    [Fact]
    public void A_folder_that_does_not_exist_has_no_files()
    {
        // Act
        HyphenationPatternFiles sut = new(_dir.Combine("missing"));

        // Assert
        sut.Installed.Should().BeEmpty();
        sut.Match("pl").CanHyphenate.Should().BeFalse();
    }
}
