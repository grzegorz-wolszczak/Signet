using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.SourceUpdates;
using Xunit;

namespace Signet.Core.Tests.SourceUpdates;

/// <summary>Tests for <see cref="PerformCssUpdates"/> — recomputing <c>url(...)</c>/<c>@import</c> in CSS.</summary>
public sealed class PerformCssUpdatesTests
{
    [Fact]
    public void Apply_NoUpdates_ReturnsSourceUnchanged()
    {
        string css = "body { background: url(images/old.jpg); }";

        string result = PerformCssUpdates.Apply(css, new Dictionary<string, string>(), "OEBPS/Styles/s.css", "OEBPS/Styles/s.css");

        result.Should().Be(css);
    }

    [Fact]
    public void Apply_BackgroundUrl_IsRewritten()
    {
        Dictionary<string, string> updates = new() { ["OEBPS/Images/old.jpg"] = "OEBPS/Images/new.jpg" };
        string css = "body { background: url(../Images/old.jpg); }";

        string result = PerformCssUpdates.Apply(css, updates, "OEBPS/Styles/s.css", "OEBPS/Styles/s.css");

        result.Should().Be("body { background: url(../Images/new.jpg); }");
    }

    [Fact]
    public void Apply_QuotedUrl_IsRewritten()
    {
        Dictionary<string, string> updates = new() { ["OEBPS/Fonts/old.otf"] = "OEBPS/Fonts/new.otf" };
        string css = "@font-face { src: url('../Fonts/old.otf') format('opentype'); }";

        string result = PerformCssUpdates.Apply(css, updates, "OEBPS/Styles/s.css", "OEBPS/Styles/s.css");

        result.Should().Be("@font-face { src: url('../Fonts/new.otf') format('opentype'); }");
    }

    [Fact]
    public void Apply_ImportRule_IsRewritten()
    {
        Dictionary<string, string> updates = new() { ["OEBPS/Styles/base.css"] = "OEBPS/Styles/base2.css" };
        string css = "@import \"base.css\";";

        string result = PerformCssUpdates.Apply(css, updates, "OEBPS/Styles/s.css", "OEBPS/Styles/s.css");

        result.Should().Be("@import \"base2.css\";");
    }

    [Fact]
    public void Apply_UnrelatedProperty_IsIgnored()
    {
        Dictionary<string, string> updates = new() { ["OEBPS/Images/old.jpg"] = "OEBPS/Images/new.jpg" };
        string css = "p { color: red; margin: 0; }";

        string result = PerformCssUpdates.Apply(css, updates, "OEBPS/Styles/s.css", "OEBPS/Styles/s.css");

        result.Should().Be(css);
    }

    [Fact]
    public void Apply_MultipleUrlsInSameFile_AreAllRewritten()
    {
        Dictionary<string, string> updates = new()
        {
            ["OEBPS/Images/a.jpg"] = "OEBPS/Images/a2.jpg",
            ["OEBPS/Images/b.jpg"] = "OEBPS/Images/b2.jpg",
        };
        string css = "body { background: url(../Images/a.jpg); }\n.x { background: url(../Images/b.jpg); }";

        string result = PerformCssUpdates.Apply(css, updates, "OEBPS/Styles/s.css", "OEBPS/Styles/s.css");

        result.Should().Be("body { background: url(../Images/a2.jpg); }\n.x { background: url(../Images/b2.jpg); }");
    }
}
