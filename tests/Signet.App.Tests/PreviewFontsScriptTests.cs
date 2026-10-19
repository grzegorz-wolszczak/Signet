using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The default fonts of the book in the preview (<see cref="PreviewFontsScript"/>, <see cref="PreviewViewModel.Fonts"/>).</summary>
public sealed class PreviewFontsScriptTests
{
    [Fact]
    public void The_fonts_go_to_the_script_as_javascript_strings_and_the_size_in_pixels()
    {
        // Act
        string js = PreviewFontsScript.Build(new PreviewAppearance("Georgia", "Times New Roman", "Verdana", 18));

        // Assert
        js.Should().Contain("std:\"Georgia\"").And.Contain("serif:\"Times New Roman\"").And.Contain("sans:\"Verdana\"")
            .And.Contain("size:18");
        js.Should().Contain(PreviewFontsScript.StyleId);
        js.Should().Contain("insertBefore(s,h.firstChild)", "the defaults come before the stylesheets of the book");
    }

    [Fact]
    public void A_family_cannot_break_out_of_the_script()
    {
        // Act
        string js = PreviewFontsScript.Build(new PreviewAppearance("x\"</script><b>", string.Empty, string.Empty, 0));

        // Assert
        js.Should().NotContain("</script>");
        js.Should().Contain("serif:\"\"").And.Contain("size:0");
    }

    [Fact]
    public void The_view_model_reports_a_change_of_the_fonts_only()
    {
        // Arrange
        using StatusBarService statusBar = new();
        using PreviewViewModel sut = new(statusBar);
        int changes = 0;
        sut.FontsChanged += (_, _) => changes++;
        PreviewAppearance fonts = new("Georgia", "Times New Roman", "Verdana", 18);

        // Act & Assert
        sut.Fonts.Should().Be(PreviewAppearance.Default);

        // Act
        sut.Fonts = fonts;
        sut.Fonts = fonts;

        // Assert
        sut.Fonts.Should().Be(fonts);
        changes.Should().Be(1);
    }
}
