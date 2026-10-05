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
}
