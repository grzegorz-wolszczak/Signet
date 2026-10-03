using System.Globalization;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.Core.Misc;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The script that highlights the caret location in the preview (<see cref="PreviewHighlightScript"/>).</summary>
public sealed class PreviewHighlightScriptTests
{
    [Theory]
    [AutoData]
    public void Default_highlights_block_background_with_translucent_yellow_and_stays(int loc)
    {
        string js = PreviewHighlightScript.Build(loc, PreviewHighlight.Default, dark: false);

        js.Should().Contain($"[data-signet-loc=\"{loc.ToString(CultureInfo.InvariantCulture)}\"]")
            .And.Contain(".closest('" + PreviewHighlightScript.AncestorSelector + "')")
            .And.Contain("el.style.backgroundColor='rgba(255,235,59,0.45)'")
            .And.Contain("scrollIntoView")
            .And.NotContain("setTimeout(")
            .And.NotContain("px solid");
    }

    [Fact]
    public void Outline_style_draws_frame_of_configured_width_in_theme_color()
    {
        PreviewHighlight h = PreviewHighlight.Default with
        {
            Style = PreviewHighlightStyle.Outline, LightColor = "#112233", DarkColor = "#abcdef", OutlineWidth = 5,
        };

        string light = PreviewHighlightScript.Build(1, h, dark: false);
        string dark = PreviewHighlightScript.Build(1, h, dark: true);

        light.Should().Contain("el.style.outline='5px solid #112233'").And.NotContain("rgba(");
        dark.Should().Contain("el.style.outline='5px solid #abcdef'");
    }

    [Fact]
    public void Background_uses_dark_color_and_opacity_with_invariant_decimal_point()
    {
        PreviewHighlight h = PreviewHighlight.Default with { DarkColor = "#0080ff", OpacityPercent = 50 };
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
        try
        {
            PreviewHighlightScript.Build(1, h, dark: true).Should().Contain("rgba(0,128,255,0.5)");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(PreviewHighlightStyle.Background, "el.style.backgroundColor='transparent'")]
    [InlineData(PreviewHighlightStyle.Outline, "el.style.outlineColor='transparent'")]
    public void Auto_hide_fades_out_after_configured_delay(PreviewHighlightStyle style, string fade)
    {
        PreviewHighlight h = PreviewHighlight.Default with { Style = style, AutoHide = true, AutoHideDelayMs = 2500 };

        string js = PreviewHighlightScript.Build(1, h, dark: false);

        js.Should().Contain("w.__signetPreviewHighlightTimer=setTimeout(function(){")
            .And.Contain(fade)
            .And.EndWith("},2500);})();");
    }

    [Fact]
    public void Out_of_range_settings_are_normalized()
    {
        PreviewHighlight h = PreviewHighlight.Default with { Style = PreviewHighlightStyle.Outline, OutlineWidth = 500 };

        PreviewHighlightScript.Build(1, h, dark: false)
            .Should().Contain($"'{PreviewHighlight.OutlineWidthMax}px solid ");
    }
}
