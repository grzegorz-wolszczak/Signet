using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="BulkRegexRenaming"/> — the "Bulk rename regex" preview.</summary>
public sealed class BulkRegexRenamingTests
{
    [Fact]
    public void TryPreviewNewFilenames_AppliesPatternToEachFilename()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        HtmlResource b = book.CreateEmptyHtmlFile();
        List<Resource> resources = new() { a, b };

        bool ok = BulkRegexRenaming.TryPreviewNewFilenames(
            resources, "^Section", "Chapter", out IReadOnlyList<string>? newFilenames, out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        newFilenames.Should().Equal(
            "Chapter" + a.Filename["Section".Length..],
            "Chapter" + b.Filename["Section".Length..]);
    }

    [Fact]
    public void TryPreviewNewFilenames_NoMatch_LeavesFilenameUnchanged()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        List<Resource> resources = new() { a };

        bool ok = BulkRegexRenaming.TryPreviewNewFilenames(
            resources, "NoSuchPattern", "X", out IReadOnlyList<string>? newFilenames, out _);

        ok.Should().BeTrue();
        newFilenames.Should().Equal(a.Filename);
    }

    [Fact]
    public void TryPreviewNewFilenames_UsesCaptureGroupBackreferences()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        List<Resource> resources = new() { a };

        bool ok = BulkRegexRenaming.TryPreviewNewFilenames(
            resources, @"Section(\d+)", "Ch$1", out IReadOnlyList<string>? newFilenames, out _);

        ok.Should().BeTrue();
        newFilenames.Should().Equal("Ch" + a.Filename["Section".Length..]);
    }

    [Fact]
    public void TryPreviewNewFilenames_StripsUriDelimiterCharacters()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        List<Resource> resources = new() { a };

        bool ok = BulkRegexRenaming.TryPreviewNewFilenames(
            resources, "^Section", "a/b:c?d", out IReadOnlyList<string>? newFilenames, out _);

        ok.Should().BeTrue();
        newFilenames.Should().Equal("abcd" + a.Filename["Section".Length..]);
    }

    [Fact]
    public void TryPreviewNewFilenames_InvalidPattern_ReturnsFalseWithError()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        List<Resource> resources = new() { a };

        bool ok = BulkRegexRenaming.TryPreviewNewFilenames(
            resources, "(unterminated", "x", out IReadOnlyList<string>? newFilenames, out string? error);

        ok.Should().BeFalse();
        newFilenames.Should().BeNull();
        error.Should().NotBeNull();
    }
}
