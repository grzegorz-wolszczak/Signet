using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="ClassUsageFinder"/> — "Find Usages" of a CSS class in the whole book.</summary>
public sealed class ClassUsageFinderTests
{
    private const string Chapter =
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n"
        + "<head><style>.note { color: red }\np.other { margin: 0 }</style></head>\n"
        + "<body>\n"
        + "<p class=\"note\">a</p><p class=\"lead  note\">b</p>\n"
        + "<p class=\"notes Note\">c</p>\n"
        + "<p data-x=\"note\">d</p>\n"
        + "</body>\n</html>\n";

    private const string Styles =
        "/* .note in a comment */\n"
        + "div.note > p, .x { color: blue }\n"
        + "@media print {\n  .note:hover { color: black }\n}\n"
        + "p:not(.note) { margin: 1em }\n"
        + "a[title=\".note\"] { }\n";

    private static IReadOnlyList<ClassUsage> Find(string className) =>
        ClassUsageFinder.Find(className, new[]
        {
            new ClassUsageSource("OEBPS/Text/ch1.xhtml", Chapter, IsStyleSheet: false),
            new ClassUsageSource("OEBPS/Styles/main.css", Styles, IsStyleSheet: true),
        });

    private static (int Line, int Column) Locate(string text, string marker, int occurrence = 0)
    {
        int offset = -1;
        for (int i = 0; i <= occurrence; i++)
        {
            offset = text.IndexOf(marker, offset + 1, StringComparison.Ordinal);
        }

        int lineStart = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
        return (text[..offset].Count(c => c == '\n') + 1, offset - lineStart + 1);
    }

    [Fact]
    public void Finds_exact_class_attribute_tokens_and_selectors_in_files_and_style_blocks()
    {
        IReadOnlyList<ClassUsage> usages = Find("note");

        usages.Select(u => (u.BookPath, u.Kind)).Should().Equal(
            ("OEBPS/Text/ch1.xhtml", ClassUsageKind.Selector),
            ("OEBPS/Text/ch1.xhtml", ClassUsageKind.ClassAttribute),
            ("OEBPS/Text/ch1.xhtml", ClassUsageKind.ClassAttribute),
            ("OEBPS/Styles/main.css", ClassUsageKind.Selector),
            ("OEBPS/Styles/main.css", ClassUsageKind.Selector),
            ("OEBPS/Styles/main.css", ClassUsageKind.Selector));
    }

    [Fact]
    public void Line_and_column_point_at_the_first_character_of_the_name()
    {
        IReadOnlyList<ClassUsage> usages = Find("note");

        (usages[1].Line, usages[1].Column).Should().Be(Locate(Chapter, "note\">a"));
        (usages[2].Line, usages[2].Column).Should().Be(Locate(Chapter, "note\">b"));
        usages[1].Line.Should().Be(usages[2].Line, "two usages on the same line are two results");
        (usages[0].Line, usages[0].Column).Should().Be(Locate(Chapter, "note {"));
        (usages[4].Line, usages[4].Column).Should().Be(Locate(Styles, "note:hover"));
        Chapter.Substring(usages[1].Offset, "note".Length).Should().Be("note");
        Styles.Substring(usages[5].Offset, "note".Length).Should().Be("note");
    }

    [Fact]
    public void Matching_is_case_sensitive_and_whole_token_only()
    {
        Find("Note").Should().ContainSingle().Which.Kind.Should().Be(ClassUsageKind.ClassAttribute);
        Find("notes").Should().ContainSingle();
        Find("not").Should().BeEmpty();
        Find("missing").Should().BeEmpty();
    }

    [Fact]
    public void Usages_of_many_files_keep_the_order_of_the_sources()
    {
        ClassUsageSource[] sources = Enumerable.Range(0, 200)
            .Select(i => i % 3 == 0
                ? new ClassUsageSource($"OEBPS/Text/ch{i:D3}.xhtml", "<p>no usage here</p>", IsStyleSheet: false)
                : new ClassUsageSource($"OEBPS/Text/ch{i:D3}.xhtml", Chapter, IsStyleSheet: false))
            .ToArray();

        IReadOnlyList<ClassUsage> usages = ClassUsageFinder.Find("note", sources);

        usages.Select(u => u.BookPath).Distinct().Should().Equal(
            sources.Where((_, i) => i % 3 != 0).Select(s => s.BookPath));
        usages.GroupBy(u => u.BookPath).Should().OnlyContain(file => file.Select(u => u.Offset).SequenceEqual(file.Select(u => u.Offset).Order()))
            .And.OnlyContain(file => file.Count() == 3);
    }

    [Fact]
    public void A_name_inside_tags_without_a_class_attribute_or_in_text_is_not_a_usage()
    {
        const string text = "<html><body><p title=\"note\">note</p><p data-note=\"x\" class=\"x\">y</p></body></html>";

        ClassUsageFinder.Find("note", new[] { new ClassUsageSource("OEBPS/Text/a.xhtml", text, IsStyleSheet: false) })
            .Should().BeEmpty();
    }

    [Fact]
    public void Every_usage_carries_the_text_of_its_line_without_the_indentation()
    {
        IReadOnlyList<ClassUsage> usages = Find("note");

        usages[1].Context.Should().Be("<p class=\"note\">a</p><p class=\"lead  note\">b</p>");
        usages[2].Context.Should().Be(usages[1].Context, "both usages are on the same line");
        usages[4].Context.Should().Be(".note:hover { color: black }", "the indentation is left out");
    }

    [Fact]
    public void A_long_line_is_cut_around_the_usage()
    {
        string text = "<p>" + new string('x', 500) + "</p><p class=\"note\">" + new string('y', 500) + "</p>\r\n";

        ClassUsage usage = ClassUsageFinder.Find("note", new[] { new ClassUsageSource("a.xhtml", text, IsStyleSheet: false) }).Single();

        usage.Context.Should().StartWith("…").And.EndWith("…").And.Contain("class=\"note\"");
        usage.Context.Length.Should().BeLessThanOrEqualTo(ClassUsageFinder.MaxContextLength + 2);
    }

    [Fact]
    public void FromTextMatches_gives_line_column_context_and_length_of_each_match()
    {
        const string text = "first line\n   second cat here\nthird cat";

        IReadOnlyList<ClassUsage> usages = ClassUsageFinder.FromTextMatches(
            "Text/a.xhtml", text, new[] { (Start: 21, End: 24), (Start: 36, End: 39) });

        usages.Should().Equal(
            new ClassUsage("Text/a.xhtml", 21, 2, 11, ClassUsageKind.Text, "second cat here", 3, 7, 3),
            new ClassUsage("Text/a.xhtml", 36, 3, 7, ClassUsageKind.Text, "third cat", 3, 6, 3));
    }

    [Fact]
    public void The_context_tells_where_the_class_name_is_in_it()
    {
        const string text = "<p>\n    <span class=\"calibre4 bold\">x</span>\n</p>";

        ClassUsage usage = ClassUsageFinder.Find("calibre4", new[] { new ClassUsageSource("a.xhtml", text, IsStyleSheet: false) }).Single();

        usage.Context.Substring(usage.ContextMatchStart, usage.ContextMatchLength).Should().Be("calibre4");
    }

    [Fact]
    public void The_context_of_a_selector_marks_the_name_after_the_dot()
    {
        const string css = "  p.note, .note > b { color: red }";

        ClassUsage first = ClassUsageFinder.Find("note", new[] { new ClassUsageSource("s.css", css, IsStyleSheet: true) }).First();

        first.Context.Should().StartWith("p.note");
        first.Context.Substring(first.ContextMatchStart, first.ContextMatchLength).Should().Be("note");
    }

    [Fact]
    public void The_mark_stays_on_the_usage_when_a_long_line_is_cut()
    {
        string text = "<p>" + new string('x', 500) + "</p><p class=\"note\">" + new string('y', 500) + "</p>\r\n";

        ClassUsage usage = ClassUsageFinder.Find("note", new[] { new ClassUsageSource("a.xhtml", text, IsStyleSheet: false) }).Single();

        usage.Context.Substring(usage.ContextMatchStart, usage.ContextMatchLength).Should().Be("note");
    }

    [Fact]
    public void The_mark_of_a_text_match_over_several_lines_ends_with_the_line()
    {
        const string text = "alpha beta\ngamma";

        ClassUsage usage = ClassUsageFinder.FromTextMatches("a.xhtml", text, new[] { (Start: 6, End: 16) }).Single();

        usage.Context.Should().Be("alpha beta");
        usage.Context.Substring(usage.ContextMatchStart, usage.ContextMatchLength).Should().Be("beta");
        usage.Length.Should().Be(10);
    }

    [Fact]
    public void FromTextMatches_without_matches_is_empty()
    {
        ClassUsageFinder.FromTextMatches("a", "text", Array.Empty<(int Start, int End)>()).Should().BeEmpty();
    }
}
