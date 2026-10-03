using AwesomeAssertions;
using Avalonia.Input;
using Signet.App.Input;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for converting Qt-style shortcut strings ↔ <see cref="KeyGesture"/>.</summary>
public sealed class KeyGestureConversionTests
{
    [Theory]
    [InlineData("Ctrl+S", Key.S, KeyModifiers.Control)]
    [InlineData("Ctrl+Shift+S", Key.S, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Ctrl+Alt+A", Key.A, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData("F8", Key.F8, KeyModifiers.None)]
    [InlineData("Ctrl+1", Key.D1, KeyModifiers.Control)]
    [InlineData("Ctrl+Return", Key.Return, KeyModifiers.Control)]
    [InlineData("Ctrl+PgDown", Key.PageDown, KeyModifiers.Control)]
    [InlineData("Alt+PgUp", Key.PageUp, KeyModifiers.Alt)]
    [InlineData("Ctrl+=", Key.OemPlus, KeyModifiers.Control)]
    [InlineData("Ctrl+-", Key.OemMinus, KeyModifiers.Control)]
    [InlineData("Ctrl+.", Key.OemPeriod, KeyModifiers.Control)]
    [InlineData("Ctrl+/", Key.OemQuestion, KeyModifiers.Control)]
    [InlineData("Ctrl+\\", Key.OemBackslash, KeyModifiers.Control)]
    [InlineData("Ctrl+]", Key.OemCloseBrackets, KeyModifiers.Control)]
    [InlineData("Ctrl+Space", Key.Space, KeyModifiers.Control)]
    [InlineData("Alt+F1", Key.F1, KeyModifiers.Alt)]
    public void Parses_qt_shortcuts(string input, Key expectedKey, KeyModifiers expectedModifiers)
    {
        KeyGestureConversion.TryParse(input, out KeyGesture? gesture).Should().BeTrue();
        gesture!.Key.Should().Be(expectedKey);
        gesture.KeyModifiers.Should().Be(expectedModifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_input_returns_false(string? input)
    {
        KeyGestureConversion.TryParse(input, out KeyGesture? gesture).Should().BeFalse();
        gesture.Should().BeNull();
    }

    [Fact]
    public void Round_trips_through_qt_string()
    {
        _ = KeyGestureConversion.TryParse("Ctrl+Shift+G", out KeyGesture? gesture);
        KeyGestureConversion.ToPortableString(gesture).Should().Be("Ctrl+Shift+G");
    }
}
