using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>
/// Tests for <see cref="MultiFileFindEngine"/> — finding across all files in scope and moving to the
/// next file that contains a match.
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
        string signature = "sig")
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
            Signature = signature,
        };
    }

    [Fact]
    public void FindNext_StartsInCurrentFileFromCaret()
    {
        var engine = new MultiFileFindEngine();
        var files = new (string, string)[]
        {
            ("a.xhtml", "cat cat"),
            ("b.xhtml", "cat"),
        };

        MultiFileFindResult result = engine.FindNext(Request(files, "a.xhtml", caret: 0, Normal("cat")));

        result.Found.Should().BeTrue();
        result.BookPath.Should().Be("a.xhtml");
        (result.Start, result.End).Should().Be((0, 3));
    }

    [Fact]
    public void FindNext_CrossesIntoNextFileWhenCurrentExhausted()
    {
        var engine = new MultiFileFindEngine();
        var files = new (string, string)[]
        {
            ("a.xhtml", "cat"),
            ("b.xhtml", "the cat sat"),
            ("c.xhtml", "no match here"),
        };

        // Caret after the only match in a.xhtml — the next match is in b.xhtml.
        MultiFileFindResult result = engine.FindNext(Request(files, "a.xhtml", caret: 3, Normal("cat")));

        result.Found.Should().BeTrue();
        result.BookPath.Should().Be("b.xhtml");
        (result.Start, result.End).Should().Be((4, 7));
    }

    [Fact]
    public void FindNext_WrapsBackToEarlierFileThroughEndOfScope()
    {
        var engine = new MultiFileFindEngine();
        var files = new (string, string)[]
        {
            ("a.xhtml", "cat"),
            ("b.xhtml", "dog"),
            ("c.xhtml", "bird"),
        };

        // Start in b.xhtml (no matches in b/c); the search wraps around to a.xhtml.
        MultiFileFindResult result = engine.FindNext(Request(files, "b.xhtml", caret: 0, Normal("cat")));

        result.Found.Should().BeTrue();
        result.BookPath.Should().Be("a.xhtml");
    }

    [Fact]
    public void FindNext_ReturnsNotFoundWhenNoFileContainsPattern()
    {
        var engine = new MultiFileFindEngine();
        var files = new (string, string)[]
        {
            ("a.xhtml", "one"),
            ("b.xhtml", "two"),
        };

        engine.FindNext(Request(files, "a.xhtml", caret: 0, Normal("zzz"))).Found.Should().BeFalse();
    }

    [Fact]
    public void FindNext_Up_WalksToPreviousFile()
    {
        var engine = new MultiFileFindEngine();
        var files = new (string, string)[]
        {
            ("a.xhtml", "cat here"),
            ("b.xhtml", "nothing"),
            ("c.xhtml", "start"),
        };

        MultiFileFindResult result = engine.FindNext(
            Request(files, "c.xhtml", caret: 0, Normal("cat"), SearchDirection.Up));

        result.Found.Should().BeTrue();
        result.BookPath.Should().Be("a.xhtml");
    }

    [Fact]
    public void FindNext_RepeatedCalls_AdvanceThroughAllMatchesOnce()
    {
        var engine = new MultiFileFindEngine();
        var files = new List<MultiFileSearchFile>
        {
            new("a.xhtml", "x x"),
            new("b.xhtml", "x"),
        };

        // Emulates repeated Find Next: the selection/caret "lands" on the previous match.
        MultiFileSearchRequest Next(string current, int selStart, int selEnd) => new()
        {
            Files = files,
            CurrentBookPath = current,
            CurrentSelectionStart = selStart,
            CurrentSelectionEnd = selEnd,
            CurrentCaret = selEnd,
            Pattern = Normal("x"),
            Direction = SearchDirection.Down,
            Signature = "sig",
        };

        MultiFileFindResult r1 = engine.FindNext(Next("a.xhtml", 0, 0));
        (r1.BookPath, r1.Start, r1.End).Should().Be(("a.xhtml", 0, 1));

        MultiFileFindResult r2 = engine.FindNext(Next("a.xhtml", r1.Start, r1.End));
        (r2.BookPath, r2.Start, r2.End).Should().Be(("a.xhtml", 2, 3));

        MultiFileFindResult r3 = engine.FindNext(Next("a.xhtml", r2.Start, r2.End));
        r3.BookPath.Should().Be("b.xhtml");
        (r3.Start, r3.End).Should().Be((0, 1));

        // After the last match, positioned in b.xhtml past it — the search ends.
        MultiFileFindResult r4 = engine.FindNext(Next("b.xhtml", 0, 1));
        r4.Found.Should().BeFalse();
    }

    [Fact]
    public void FindNext_NewSignatureResetsStartingResource()
    {
        var engine = new MultiFileFindEngine();
        var files = new (string, string)[]
        {
            ("a.xhtml", "cat"),
            ("b.xhtml", "cat"),
        };

        engine.FindNext(Request(files, "b.xhtml", caret: 3, Normal("cat"), signature: "s1"))
            .Found.Should().BeTrue();

        // New signature — the starting file is recomputed from the current one (b.xhtml, caret 0).
        MultiFileFindResult again = engine.FindNext(
            Request(files, "b.xhtml", caret: 0, Normal("cat"), signature: "s2"));
        again.BookPath.Should().Be("b.xhtml");
        (again.Start, again.End).Should().Be((0, 3));
    }

    [Fact]
    public void FindNext_InvalidRegex_ReturnsNotFound()
    {
        var engine = new MultiFileFindEngine();
        var files = new (string, string)[] { ("a.xhtml", "abc") };

        engine.FindNext(Request(files, "a.xhtml", 0, "(unclosed")).Found.Should().BeFalse();
    }

    [Fact]
    public void FindNext_CurrentFileOutsideScope_ScansScopeFromStart()
    {
        var engine = new MultiFileFindEngine();
        var files = new (string, string)[]
        {
            ("style.css", "p { color: red }"),
            ("other.css", "a { color: blue }"),
        };

        // The current file (some HTML) is not in the CSS scope — the scan starts at the first file in scope.
        MultiFileFindResult result = engine.FindNext(Request(files, "chapter.xhtml", caret: 0, Normal("color")));

        result.Found.Should().BeTrue();
        result.BookPath.Should().Be("style.css");
    }
}
