using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.Core.Misc;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for the icon set choice and the toolbar icon size (without starting Avalonia:
/// <see cref="IconThemeManager"/> skips loading resources when there is no <c>Application.Current</c>).
/// </summary>
public sealed class IconThemeManagerTests : IDisposable
{
    private readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), "Signet.Tests", $"icons-{Guid.NewGuid():N}.json");

    [Fact]
    public void Default_icon_theme_is_the_colour_main_set()
    {
        IconThemeManager sut = new(new SettingsStore(_settingsPath), NullLogger<IconThemeManager>.Instance);

        sut.Current.Should().Be("main");
        IconThemeManager.SupportedThemes.Should().Contain(("main", "Default"));
    }

    [Theory]
    [InlineData("MAIN", "main")]
    [InlineData("Fluent", "fluent")]
    [InlineData("unknown-theme", "main")]
    public void Set_normalizes_the_theme_code_and_falls_back_to_main(string code, string expected)
    {
        SettingsStore settings = new(_settingsPath);
        IconThemeManager sut = new(settings, NullLogger<IconThemeManager>.Instance);

        sut.Set(code);

        sut.Current.Should().Be(expected);
        settings.UiIconTheme.Should().Be(expected);
    }

    [Theory]
    [InlineData(16.0, 1.8, 28)]
    [InlineData(5.0, 1.8, 12)]
    [InlineData(40.0, 1.8, 48)]
    public void Toolbar_icon_size_is_line_spacing_times_scale_clamped_to_12_48(
        double lineSpacing, double scale, int expected) =>
        IconThemeManager.ComputeToolbarIconSize(lineSpacing, scale).Should().Be(expected);

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }
    }
}
