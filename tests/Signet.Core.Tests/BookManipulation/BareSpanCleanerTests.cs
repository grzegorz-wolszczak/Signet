using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="BareSpanCleaner"/> — finding and unwrapping spans without attributes.</summary>
public sealed class BareSpanCleanerTests
{
    private static string Page(string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n"
        + "<head><title>t</title></head>\n<body>\n" + body + "\n</body>\n</html>\n";

    [Fact]
    public void Only_spans_without_any_attribute_are_found()
    {
        string text = Page("<p><span>a</span> <span >b</span> <span class=\"x\">c</span> <span lang=\"en\">d</span></p>");

        BareSpanCleaner.Find(text).Select(s => s.Content).Should().Equal("a", "b");
    }

    [Fact]
    public void Removing_keeps_the_content_including_nested_bare_spans()
    {
        string text = Page("<p>x <span>a <em>b</em> <span>c</span></span> y</p>");

        string result = BareSpanCleaner.Remove(text, BareSpanCleaner.Find(text).Select(s => s.OpenPos));

        result.Should().Be(Page("<p>x a <em>b</em> c y</p>"));
    }
}
