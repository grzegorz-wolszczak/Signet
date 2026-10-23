using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Xunit;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// Tests of <see cref="SyntheticBook"/>. A generated book is what the benchmarks measure on, so it has to be a book
/// Signet reads the way it is meant to: the right number of files and footnotes, in the right places, and nothing for
/// the validators to complain about. A generator that quietly wrote a broken book would make every measurement on it
/// meaningless.
/// </summary>
public sealed class SyntheticBookTests
{
    [Theory]
    [InlineData(SyntheticNotes.Foreign, false, false)]
    [InlineData(SyntheticNotes.ForeignInOneFile, true, false)]
    [InlineData(SyntheticNotes.Signet, false, true)]
    [InlineData(SyntheticNotes.SignetInOneFile, true, true)]
    public void A_generated_book_holds_the_footnotes_it_was_asked_for(SyntheticNotes notes, bool oneFile, bool signet)
    {
        // Arrange
        using TempDir temp = new("synthetic-");

        // Act
        string epub = SyntheticBook.BuildEpub(temp, files: 4, notesPerFile: 3, notes);
        using Book book = new ImportEpub(epub).GetBook();

        // Assert
        book.GetHtmlResourcesExcludingNav().Should().HaveCount(oneFile ? 5 : 4, "four chapters, plus the notes file");
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);
        footnotes.Should().HaveCount(12).And.OnlyContain(f => f.IsSignet == signet);
        footnotes.Should().OnlyContain(f => f.Placement == (oneFile ? FootnotePlacement.Global : FootnotePlacement.Local));
        LinkIntegrityValidator.Validate(book).Should().BeEmpty();
        FootnoteValidator.Validate(book).Should().BeEmpty();
    }

    [Fact]
    public void A_generated_book_without_footnotes_has_none()
    {
        // Arrange
        using TempDir temp = new("synthetic-");

        // Act
        string epub = SyntheticBook.BuildEpub(temp, files: 3);
        using Book book = new ImportEpub(epub).GetBook();

        // Assert
        book.GetHtmlResourcesExcludingNav().Should().HaveCount(3);
        FootnoteScanner.Scan(book).Should().BeEmpty();
        LinkIntegrityValidator.Validate(book).Should().BeEmpty();
    }

    /// <summary>
    /// The same arguments give the same book, byte for byte, so a measurement can be repeated and a failure can be
    /// looked at again on the very book that produced it.
    /// </summary>
    [Fact]
    public void The_same_arguments_give_the_same_book()
    {
        // Arrange
        using TempDir temp = new("synthetic-");

        // Act
        string first = SyntheticBook.BuildTree(temp.Combine("a"), files: 3, notesPerFile: 2, SyntheticNotes.Foreign);
        string second = SyntheticBook.BuildTree(temp.Combine("b"), files: 3, notesPerFile: 2, SyntheticNotes.Foreign);

        // Assert
        Dictionary<string, string> left = Contents(first);
        Dictionary<string, string> right = Contents(second);
        left.Keys.Should().BeEquivalentTo(right.Keys);
        foreach ((string path, string text) in left)
        {
            text.Should().Be(right[path], "the file {0} is written the same way", path);
        }
    }

    [Fact]
    public void A_book_of_one_file_is_allowed_and_fewer_is_not()
    {
        // Arrange
        using TempDir temp = new("synthetic-");

        // Act
        string epub = SyntheticBook.BuildEpub(temp, files: 1, notesPerFile: 1, SyntheticNotes.Foreign);
        using Book book = new ImportEpub(epub).GetBook();

        // Assert
        FootnoteScanner.Scan(book).Should().ContainSingle();

        // Act & Assert
        FluentActions.Invoking(() => SyntheticBook.BuildTree(temp.Combine("none"), files: 0))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Dictionary<string, string> Contents(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);
}
