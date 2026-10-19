using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>
/// Tests for <see cref="MultiFileFindEngine"/> — finding across all files in scope: from the caret in the current file
/// through the following files, wrapping (or not) past the end of the scope, and "Restart" from its beginning.
/// </summary>
public sealed class MultiFileFindEngineTests
{
    private static string Normal(string text) =>
        SearchRegexBuilder.BuildSearchRegex(text, SearchMode.Normal, SearchOptions.None, false);

    private static MultiFileSearchRequest Request(
        IReadOnlyList<(string Path, string Text)> files,
        string current,
        int caret,
        string pattern,
        SearchDirection direction = SearchDirection.Down,
        bool wrap = true,
        bool fromStart = false)
    {
        var list = new List<MultiFileSearchFile>();
        foreach ((string path, string text) in files)
        {
            list.Add(new MultiFileSearchFile(path, text));
        }

        return new MultiFileSearchRequest
        {
            Files = list,
            CurrentBookPath = current,
            CurrentCaret = caret,
            CurrentSelectionStart = caret,
            CurrentSelectionEnd = caret,
            Pattern = pattern,
            Direction = direction,
            Wrap = wrap,
            FromStart = fromStart,
        };
    }

    [Fact]
    public void FindNext_StartsInCurrentFileFromCaret()
    {
        // Arrange
        var files = new (string, string)[] { ("a.xhtml", "cat cat"), ("b.xhtml", "cat") };

        // Act
        MultiFileFindResult result = MultiFileFindEngine.FindNext(Request(files, "a.xhtml", caret: 1, Normal("cat")));

        // Assert
        (result.Found, result.BookPath, result.Start, result.End, result.Wrapped).Should().Be((true, "a.xhtml", 4, 7, false));
    }

    [Fact]
    public void FindNext_CrossesIntoNextFileWhenCurrentExhausted()
    {
        // Arrange
        var files = new (string, string)[] { ("a.xhtml", "cat"), ("b.xhtml", "the cat sat"), ("c.xhtml", "no match here") };

        // Act
        MultiFileFindResult result = MultiFileFindEngine.FindNext(Request(files, "a.xhtml", caret: 3, Normal("cat")));

        // Assert
        (result.BookPath, result.Start, result.End, result.Wrapped).Should().Be(("b.xhtml", 4, 7, false));
    }

    [Fact]
    public void FindNext_WithWrap_GoesOnFromTheFirstFile()
    {
        // Arrange
        var files = new (string, string)[] { ("a.xhtml", "cat"), ("b.xhtml", "dog"), ("c.xhtml", "bird") };

        // Act
        MultiFileFindResult result = MultiFileFindEngine.FindNext(Request(files, "b.xhtml", caret: 0, Normal("cat")));

        // Assert
        (result.Found, result.BookPath, result.Wrapped).Should().Be((true, "a.xhtml", true));
    }

    [Fact]
    public void FindNext_WithoutWrap_StopsAtTheEndOfTheScope()
    {
        // Arrange
        var files = new (string, string)[] { ("a.xhtml", "cat"), ("b.xhtml", "dog") };

        // Act & Assert
        MultiFileFindEngine.FindNext(Request(files, "b.xhtml", caret: 0, Normal("cat"), wrap: false)).Found.Should().BeFalse();
    }

    [Fact]
    public void FindNext_WithWrap_FindsTheOnlyMatchAgainAfterTheCaretMovedPastIt()
    {
        // Arrange
        // The reported case: one match in the current file, the caret clicked below it, other files without matches.
        var files = new (string, string)[] { ("a.xhtml", "one\nterm\nthree"), ("b.xhtml", "nothing") };
        int below = "one\nterm\nth".Length;

        // Act
        MultiFileFindResult result = MultiFileFindEngine.FindNext(Request(files, "a.xhtml", below, Normal("term")));

        // Assert
        (result.Found, result.BookPath, result.Start, result.Wrapped).Should().Be((true, "a.xhtml", 4, true));
    }

    [Fact]
    public void FindNext_FromStart_SearchesTheScopeFromItsBeginningWhateverTheCaret()
    {
        // Arrange
        var files = new (string, string)[] { ("a.xhtml", "cat"), ("b.xhtml", "cat dog cat") };

        // Act
        MultiFileFindResult result = MultiFileFindEngine.FindNext(
            Request(files, "b.xhtml", caret: 11, Normal("cat"), wrap: false, fromStart: true));

        // Assert
        (result.Found, result.BookPath, result.Start, result.Wrapped).Should().Be((true, "a.xhtml", 0, false));
    }

    [Fact]
    public void FindNext_Up_WalksToPreviousFile_AndFromStartBeginsAtTheEndOfTheLastFile()
    {
        // Arrange
        var files = new (string, string)[] { ("a.xhtml", "cat here"), ("b.xhtml", "nothing"), ("c.xhtml", "start cat") };

        // Act & Assert
        MultiFileFindEngine.FindNext(Request(files, "c.xhtml", caret: 0, Normal("cat"), SearchDirection.Up))
            .BookPath.Should().Be("a.xhtml");

        // Act
        MultiFileFindResult fromEnd = MultiFileFindEngine.FindNext(
            Request(files, "a.xhtml", caret: 0, Normal("cat"), SearchDirection.Up, fromStart: true));

        // Assert
        (fromEnd.BookPath, fromEnd.Start).Should().Be(("c.xhtml", 6));
    }

    [Fact]
    public void FindNext_ReturnsNotFoundWhenNoFileContainsPattern()
    {
        // Arrange
        var files = new (string, string)[] { ("a.xhtml", "one"), ("b.xhtml", "two") };

        // Act & Assert
        MultiFileFindEngine.FindNext(Request(files, "a.xhtml", caret: 0, Normal("zzz"))).Found.Should().BeFalse();
    }

    [Fact]
    public void FindNext_RepeatedCalls_AdvanceThroughAllMatchesAndWrap()
    {
        // Arrange
        var files = new List<MultiFileSearchFile> { new("a.xhtml", "x x"), new("b.xhtml", "x") };

        // Emulates repeated Find Next: the selection "lands" on the previous match.
        MultiFileSearchRequest Next(string current, int selStart, int selEnd) => new()
        {
            Files = files,
            CurrentBookPath = current,
            CurrentSelectionStart = selStart,
            CurrentSelectionEnd = selEnd,
            CurrentCaret = selEnd,
            Pattern = Normal("x"),
            Direction = SearchDirection.Down,
        };

        // Act
        MultiFileFindResult r1 = MultiFileFindEngine.FindNext(Next("a.xhtml", 0, 0));

        // Assert
        (r1.BookPath, r1.Start).Should().Be(("a.xhtml", 0));

        // Act
        MultiFileFindResult r2 = MultiFileFindEngine.FindNext(Next("a.xhtml", r1.Start, r1.End));

        // Assert
        (r2.BookPath, r2.Start).Should().Be(("a.xhtml", 2));

        // Act
        MultiFileFindResult r3 = MultiFileFindEngine.FindNext(Next("a.xhtml", r2.Start, r2.End));

        // Assert
        (r3.BookPath, r3.Start).Should().Be(("b.xhtml", 0));

        // Act
        MultiFileFindResult r4 = MultiFileFindEngine.FindNext(Next("b.xhtml", r3.Start, r3.End));

        // Assert
        (r4.BookPath, r4.Start, r4.Wrapped).Should().Be(("a.xhtml", 0, true));
    }

    [Fact]
    public void FindNext_InvalidRegex_ReturnsNotFound()
    {
        // Arrange
        var files = new (string, string)[] { ("a.xhtml", "abc") };

        // Act & Assert
        MultiFileFindEngine.FindNext(Request(files, "a.xhtml", 0, "(unclosed")).Found.Should().BeFalse();
    }

    [Fact]
    public void FindNext_CurrentFileOutsideScope_ScansScopeFromStart()
    {
        // Arrange
        var files = new (string, string)[] { ("style.css", "p { color: red }"), ("other.css", "a { color: blue }") };

        // Act
        MultiFileFindResult result = MultiFileFindEngine.FindNext(Request(files, "chapter.xhtml", caret: 0, Normal("color")));

        // Assert
        (result.Found, result.BookPath, result.Wrapped).Should().Be((true, "style.css", false));
    }
}
