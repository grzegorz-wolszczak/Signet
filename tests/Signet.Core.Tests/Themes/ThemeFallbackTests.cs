using AwesomeAssertions;
using Signet.Core.Themes;
using Xunit;

namespace Signet.Core.Tests.Themes;

/// <summary>
/// Tests for <see cref="ThemeResolver.ColorSource"/> — the chain a color key falls back to while nothing sets it
/// (<see cref="ThemeKey.FallbackId"/>). It is what the application does anyway when it finds no color, written on the
/// keys so the Preferences editor can show the color and name where it comes from.
/// </summary>
public sealed class ThemeFallbackTests
{
    [Fact]
    public void A_key_with_a_color_of_its_own_is_its_own_source()
    {
        // Arrange
        Theme theme = new Theme("test", "Test")
            .With(ThemeKeys.UiNotificationsTimestampBackground.Id, ThemeEntry.Of(ThemeColor.FromRgb(1, 2, 3)));
        ThemeResolver sut = new(_ => null);

        // Act
        ThemeKey? source = sut.ColorSource(theme, ThemeKeys.UiNotificationsTimestampBackground);

        // Assert
        source.Should().BeSameAs(ThemeKeys.UiNotificationsTimestampBackground);
        sut.Color(theme, source!).Should().Be(ThemeColor.FromRgb(1, 2, 3));
    }

    [Fact]
    public void A_key_with_no_color_takes_the_one_of_the_key_it_falls_back_to()
    {
        // Arrange
        Theme theme = new Theme("test", "Test")
            .With(ThemeKeys.UiNotificationsBackground.Id, ThemeEntry.Of(ThemeColor.FromRgb(9, 8, 7)));
        ThemeResolver sut = new(_ => null);

        // Act
        ThemeKey? source = sut.ColorSource(theme, ThemeKeys.UiNotificationsTimestampBackground);

        // Assert
        source.Should().BeSameAs(ThemeKeys.UiNotificationsBackground);
        sut.Color(theme, source!).Should().Be(ThemeColor.FromRgb(9, 8, 7));
    }

    /// <summary>
    /// The chain of a panel color ends at a color of the Fluent palette, which Signet writes down — so every link of
    /// it resolves and the editor always has a value to show. The timestamp column falls back to the list, the list
    /// to the background of the window.
    /// </summary>
    [Fact]
    public void A_panel_color_falls_back_through_its_panel_to_the_palette()
    {
        // Arrange
        Theme theme = new("test", "Test");
        ThemeResolver sut = new(_ => null);

        // Act
        ThemeKey? source = sut.ColorSource(theme, ThemeKeys.UiNotificationsTimestampBackground);

        // Assert
        source.Should().NotBeNull();
        source!.Id.Should().Be("fluent.regionColor", "nothing on the way sets a color, so the chain reaches the palette");
        sut.Color(theme, source).Should().NotBeNull();
    }

    /// <summary>A key that names no fallback and has no default of its own still has no source — the controls decide.</summary>
    [Fact]
    public void A_key_with_no_fallback_and_no_default_has_no_source()
    {
        // Arrange
        Theme theme = new("test", "Test");
        ThemeResolver sut = new(_ => null);

        // Act & Assert
        sut.ColorSource(theme, ThemeKeys.UiAccent).Should().BeNull("the accent is the system's");
    }

    /// <summary>
    /// The warning message falls back to the error notification color, which has a default of its own — so this chain
    /// always ends in a color, even in a theme that sets nothing.
    /// </summary>
    [Fact]
    public void A_fallback_to_a_key_with_a_default_color_resolves_without_any_theme_entry()
    {
        // Arrange
        Theme theme = new("test", "Test");
        ThemeResolver sut = new(_ => null);

        // Act
        ThemeKey? source = sut.ColorSource(theme, ThemeKeys.UiNotificationsMessageWarningForeground);

        // Assert
        source.Should().BeSameAs(ThemeKeys.UiErrorNotification);
        sut.Color(theme, source!).Should().Be(ThemeKeys.UiErrorNotification.DefaultColor);
    }
}
