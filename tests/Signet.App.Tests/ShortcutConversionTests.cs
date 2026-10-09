using AwesomeAssertions;
using Avalonia.Input;
using Signet.App.Input;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests of <see cref="ShortcutConversion"/> and the shortcut texts — the settings syntax of the keymap.</summary>
public sealed class ShortcutConversionTests
{
    [Theory]
    [InlineData("Ctrl+S")]
    [InlineData("Ctrl+Shift+F9")]
    [InlineData("Ctrl+K, Ctrl+C")]
    [InlineData("Ctrl+,")]
    [InlineData("Ctrl+,, Ctrl+K")]
    [InlineData("Mouse:Ctrl+Left")]
    [InlineData("Mouse:Alt+Middlex2")]
    [InlineData("Mouse:Back")]
    [InlineData("Mouse:Ctrl+Shift+WheelDown")]
    public void A_shortcut_survives_a_round_trip(string text)
    {
        ShortcutConversion.TryParse(text, out Shortcut? shortcut).Should().BeTrue();

        shortcut!.PortableText.Should().Be(text);
    }

    [Fact]
    public void Two_strokes_and_the_comma_key_are_told_apart()
    {
        ShortcutConversion.TryParse("Ctrl+,", out Shortcut? comma).Should().BeTrue();
        ShortcutConversion.TryParse("Ctrl+K, Ctrl+C", out Shortcut? chord).Should().BeTrue();

        comma.Should().BeOfType<KeyStrokeShortcut>().Which.Second.Should().BeNull();
        comma.Should().BeOfType<KeyStrokeShortcut>().Which.First.Key.Should().Be(Key.OemComma);
        chord.Should().BeOfType<KeyStrokeShortcut>().Which.Second.Should().Be(new KeyGesture(Key.C, KeyModifiers.Control));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Mouse:Ctrl+Nose")]
    [InlineData("Mouse:Hyper+Left")]
    public void Unrecognized_text_is_not_a_shortcut(string text)
    {
        ShortcutConversion.TryParse(text, out Shortcut? shortcut).Should().BeFalse();
        shortcut.Should().BeNull();
    }

    [Fact]
    public void A_list_keeps_the_order_skips_unknown_items_and_duplicates()
    {
        var list = ShortcutConversion.ParseList("Ctrl+S; Mouse:Back; nonsense+?; Ctrl+S");

        ShortcutConversion.ToListString(list).Should().Be("Ctrl+S; Mouse:Back");
        ShortcutConversion.ParseList("F9").Should().ContainSingle("a single key sequence is the older settings format");
    }

    [Fact]
    public void Mouse_shortcuts_are_shown_like_in_intellij()
    {
        using UiCultureScope culture = new("en");

        new MouseShortcut(MouseShortcutButton.Left, KeyModifiers.Control).DisplayText.Should().Be("Ctrl+" + Strings.Get("Shortcut_MouseClick"));
        new MouseShortcut(MouseShortcutButton.Left, KeyModifiers.Alt, 2).DisplayText.Should().Be("Alt+Double Click");
        new MouseShortcut(MouseShortcutButton.WheelUp, KeyModifiers.Control | KeyModifiers.Shift).DisplayText.Should().Be("Ctrl+Shift+Wheel Up");
        new MouseShortcut(MouseShortcutButton.Back, KeyModifiers.None, 2).ClickCount.Should().Be(1, "side buttons have no double click");
    }

    [Fact]
    public void A_plain_left_or_right_click_is_reserved()
    {
        new MouseShortcut(MouseShortcutButton.Left, KeyModifiers.None).IsReserved.Should().BeTrue();
        new MouseShortcut(MouseShortcutButton.Right, KeyModifiers.None).IsReserved.Should().BeTrue();
        new MouseShortcut(MouseShortcutButton.Left, KeyModifiers.None, 2).IsReserved.Should().BeFalse();
        new MouseShortcut(MouseShortcutButton.Middle, KeyModifiers.None).IsReserved.Should().BeFalse();
        new MouseShortcut(MouseShortcutButton.Left, KeyModifiers.Control).IsReserved.Should().BeFalse();
    }
}
