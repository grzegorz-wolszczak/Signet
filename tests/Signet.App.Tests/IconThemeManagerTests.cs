using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for the icon set choice and the toolbar icon size (without starting Avalonia:
/// <see cref="IconThemeManager"/> skips loading resources when there is no <c>Application.Current</c>).
/// </summary>
public sealed class IconThemeManagerTests : IDisposable
{
    private readonly Disposables _disposables = new();

    private readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), "Signet.Tests", $"icons-{Guid.NewGuid():N}.xml");

    [Fact]
    public void Default_icon_theme_is_the_colour_main_set()
    {
        // Act
        IconThemeManager sut = new(new SettingsStore(_settingsPath), NullLogger<IconThemeManager>.Instance);

        // Assert
        sut.Current.Should().Be("main");
        IconThemeManager.SupportedThemes.Should().Contain(("main", "Default"));
    }

    [Theory]
    [InlineData("MAIN", "main")]
    [InlineData("Fluent", "fluent")]
    [InlineData("unknown-theme", "main")]
    public void Set_normalizes_the_theme_code_and_falls_back_to_main(string code, string expected)
    {
        // Arrange
        SettingsStore settings = new(_settingsPath);
        IconThemeManager sut = new(settings, NullLogger<IconThemeManager>.Instance);

        // Act
        sut.Set(code);

        // Assert
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

    [Fact]
    public void A_custom_icon_set_is_read_from_icons_xml()
    {
        // Arrange
        TempDir dir = _disposables.Add(new TempDir());
        string path = Path.Combine(dir.Path, IconThemeManager.CustomIconFileName);
        File.WriteAllText(path, """
            <icons>
              <icon key="icon-open"> M0 0 L10 10 Z </icon>
              <icon key="">M1 1</icon>
              <icon key="icon-save">M2 2 H4</icon>
            </icons>
            """);

        // Act & Assert
        IconThemeManager.ReadCustomIcons(path).Should().BeEquivalentTo(new System.Collections.Generic.Dictionary<string, string>
        {
            ["icon-open"] = "M0 0 L10 10 Z",
            ["icon-save"] = "M2 2 H4",
        });

        // Assert
        IconThemeManager.CustomIconFileName.Should().Be("icons.xml");
    }

    [Fact]
    public void A_json_icon_map_is_not_read()
    {
        // Arrange
        TempDir dir = _disposables.Add(new TempDir());
        string path = Path.Combine(dir.Path, IconThemeManager.CustomIconFileName);
        File.WriteAllText(path, "{ \"icon-open\": \"M0 0 L10 10 Z\" }");

        // Act & Assert
        IconThemeManager.ReadCustomIcons(path).Should().BeEmpty();
    }

    public void Dispose()
    {
        _disposables.Dispose();
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }
    }
}
