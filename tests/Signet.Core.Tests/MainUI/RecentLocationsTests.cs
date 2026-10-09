using System;
using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="RecentLocations"/> — the content of the Recent Locations popup.</summary>
public sealed class RecentLocationsTests
{
    private static NavigationPlace At(string bookPath, int line) => new(bookPath, line * 10, line, DateTimeOffset.UnixEpoch);

    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => $"line {i}"));

    [Theory]
    [AutoData]
    public void Pick_returns_the_newest_places_once_each_without_missing_files_and_caretless_tabs(
        string a, string b, string image, string deleted)
    {
        NavigationPlace[] places =
        {
            At(a, 10), At(b, 1), new(image, -1, 0, DateTimeOffset.UnixEpoch), At(deleted, 1), At(a, 11), At(a, 50),
        };

        RecentLocations.Pick(places, 10, p => p != deleted)
            .Should().Equal(At(a, 50), At(a, 11), At(b, 1));
    }

    [Theory]
    [AutoData]
    public void Pick_stops_at_the_limit(string file)
    {
        NavigationPlace[] places = Enumerable.Range(0, 10).Select(i => At(file, i * 10)).ToArray();

        RecentLocations.Pick(places, 3, _ => true).Select(p => p.Line).Should().Equal(90, 80, 70);
    }

    [Fact]
    public void Snippet_shows_two_lines_on_each_side()
    {
        string text = Lines(20);
        int offset = text.IndexOf("line 10", StringComparison.Ordinal);

        LocationSnippet snippet = RecentLocations.Snippet(text, offset);

        snippet.FirstLine.Should().Be(8);
        snippet.Text.Should().Be("line 8\nline 9\nline 10\nline 11\nline 12");
    }

    [Fact]
    public void Snippet_near_the_start_shows_more_lines_after()
    {
        LocationSnippet snippet = RecentLocations.Snippet(Lines(20), 0);

        snippet.FirstLine.Should().Be(1);
        snippet.Text.Split('\n').Should().HaveCount(5).And.EndWith("line 5");
    }

    [Fact]
    public void Snippet_drops_blank_lines_at_its_edges()
    {
        string text = "a\n\n\nplace\n\n\nz";

        LocationSnippet snippet = RecentLocations.Snippet(text, text.IndexOf("place", StringComparison.Ordinal));

        snippet.FirstLine.Should().Be(4);
        snippet.Text.Should().Be("place");
    }

    [Fact]
    public void Markup_breadcrumb_lists_the_open_elements_from_body_with_ids_and_classes()
    {
        const string html = "<html><head><title>t</title></head><body><section id=\"ch1\"><p>first</p>" +
                            "<p class=\"indent big\">here</p></section></body></html>";

        RecentLocations.Breadcrumb(html, html.IndexOf("here", StringComparison.Ordinal), BreadcrumbKind.Markup)
            .Should().Be("body > section#ch1 > p.indent.big");
    }

    [Fact]
    public void Css_breadcrumb_is_the_rule_selector_with_its_at_rule()
    {
        const string css = "p { margin: 0; }\n@media screen { h1.title { color: red; } }";

        RecentLocations.Breadcrumb(css, css.IndexOf("color", StringComparison.Ordinal), BreadcrumbKind.Css)
            .Should().Be("@media screen > h1.title");
        RecentLocations.Breadcrumb(css, css.IndexOf("margin", StringComparison.Ordinal), BreadcrumbKind.Css)
            .Should().Be("p");
    }

    [Theory]
    [AutoData]
    public void No_breadcrumb_kind_gives_an_empty_breadcrumb(string text)
    {
        RecentLocations.Breadcrumb(text, 0, BreadcrumbKind.None).Should().BeEmpty();
    }
}
