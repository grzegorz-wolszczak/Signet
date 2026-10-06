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
}
