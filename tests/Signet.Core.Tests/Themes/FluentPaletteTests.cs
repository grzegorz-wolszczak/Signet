using System.Linq;
using AwesomeAssertions;
using Signet.Core.Themes;
using Xunit;

namespace Signet.Core.Tests.Themes;

/// <summary>
/// The colors of the Fluent palette are Signet's own, written down: every one of them has a value in both bundled
/// themes, so a color the user is shown always has a number they can read and change. They were taken from Avalonia's
/// Fluent theme the day they were written, and from then on these keys — not Avalonia — are the source.
/// </summary>
public sealed class FluentPaletteTests
{
    [Fact]
    public void Every_color_of_the_palette_has_a_value_in_the_registry()
    {
        // Act
        ThemeKey[] palette = ThemeKeys.FluentPalette.Select(p => p.Key).ToArray();

        // Assert
        palette.Should().NotBeEmpty();
        palette.Should().OnlyContain(k => k.Kind == ThemeKeyKind.Color);
        palette.Should().OnlyContain(
            k => k.DefaultColor != null,
            "the light variant of the palette is the default of the registry (Signet Light sets nothing of its own)");
    }

    /// <summary>
    /// Signet Dark has to set every color of the palette: the defaults of the registry are the light ones, and a dark
    /// theme that left one of them out would show a light color among dark ones.
    /// </summary>
    [Fact]
    public void Signet_Dark_sets_the_dark_value_of_every_color_of_the_palette()
    {
        // Arrange
        using TestSupport.TempDir directory = new("palette-");
        ThemeStore store = new(directory.Path);
        Theme dark = store.Get(ThemeStore.DarkId);

        // Act
        string[] missing = ThemeKeys.FluentPalette
            .Select(p => p.Key.Id)
            .Where(id => !dark.Entries.ContainsKey(id))
            .ToArray();

        // Assert
        missing.Should().BeEmpty();
    }

    /// <summary>
    /// Both variants resolve to a color for every key of the palette — which is what makes the editor able to show a
    /// value instead of "from the controls" on any of them.
    /// </summary>
    [Fact]
    public void Both_bundled_themes_resolve_every_color_of_the_palette()
    {
        // Arrange
        using TestSupport.TempDir directory = new("palette-");
        ThemeStore store = new(directory.Path);

        // Act & Assert
        foreach (string id in new[] { ThemeStore.LightId, ThemeStore.DarkId })
        {
            Theme theme = store.Get(id);
            foreach ((string name, ThemeKey key) in ThemeKeys.FluentPalette)
            {
                store.Resolver.Color(theme, key).Should().NotBeNull("{0} has a value in {1}", name, id);
            }
        }
    }

    /// <summary>The accent is deliberately not written down: it is the accent of the system.</summary>
    [Fact]
    public void The_accent_is_left_to_the_system()
    {
        // Act & Assert
        ThemeKeys.UiAccent.DefaultColor.Should().BeNull();
    }
}
