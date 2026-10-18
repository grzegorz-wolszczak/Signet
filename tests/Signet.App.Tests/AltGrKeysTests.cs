using System;
using System.IO;
using Avalonia.Input;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.Input;
using Signet.App.ViewModels;
using Signet.Core.Spellcheck;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// "Enable AltGr on international keyboards": which key strokes count as a character typed with AltGr, and the
/// preference. <see cref="AltGrKeys"/> is static, so these tests run one after another and put it back off.
/// </summary>
[Collection("AltGrKeys")]
public sealed class AltGrKeysTests : IDisposable
{
    private readonly Func<KeyEventArgs, bool?> _detector = AltGrKeys.DetectAltGr;

    public AltGrKeysTests()
    {
        AltGrKeys.Enabled = false;
        AltGrKeys.DetectAltGr = _ => null;
    }

    public void Dispose()
    {
        AltGrKeys.Enabled = false;
        AltGrKeys.DetectAltGr = _detector;
    }

    private static KeyEventArgs Stroke(KeyModifiers modifiers, string? symbol) =>
        new() { Key = Key.L, KeyModifiers = modifiers, KeySymbol = symbol };

    [Fact]
    public void Off_by_default_nothing_counts_as_AltGr()
    {
        AltGrKeys.IsTypedCharacter(Stroke(KeyModifiers.Control | KeyModifiers.Alt, "ł")).Should().BeFalse();
    }

    [Fact]
    public void A_character_with_Ctrl_and_Alt_counts_as_AltGr_when_enabled()
    {
        AltGrKeys.Enabled = true;

        AltGrKeys.IsTypedCharacter(Stroke(KeyModifiers.Control | KeyModifiers.Alt, "ł")).Should().BeTrue();
        AltGrKeys.IsTypedCharacter(Stroke(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, "Ł")).Should().BeTrue();
    }

    [Fact]
    public void A_stroke_that_types_nothing_or_lacks_Ctrl_or_Alt_is_a_shortcut()
    {
        AltGrKeys.Enabled = true;

        AltGrKeys.IsTypedCharacter(Stroke(KeyModifiers.Control | KeyModifiers.Alt, null)).Should().BeFalse("no character was typed");
        AltGrKeys.IsTypedCharacter(Stroke(KeyModifiers.Control | KeyModifiers.Alt, string.Empty)).Should().BeFalse();
        AltGrKeys.IsTypedCharacter(Stroke(KeyModifiers.Control | KeyModifiers.Alt, "\u000c")).Should().BeFalse("a control character is not text");
        AltGrKeys.IsTypedCharacter(Stroke(KeyModifiers.Alt, "ł")).Should().BeFalse();
        AltGrKeys.IsTypedCharacter(Stroke(KeyModifiers.Control, "l")).Should().BeFalse();
        AltGrKeys.IsTypedCharacter(Stroke(KeyModifiers.None, "l")).Should().BeFalse();
    }

    [Fact]
    public void Where_the_right_Alt_is_known_it_decides_and_not_the_character()
    {
        AltGrKeys.Enabled = true;
        const KeyModifiers ctrlAlt = KeyModifiers.Control | KeyModifiers.Alt;

        // Ctrl+Alt+S with the left Alt types ś too on a Polish keyboard, yet it is the shortcut.
        AltGrKeys.DetectAltGr = _ => false;
        AltGrKeys.IsTypedCharacter(Stroke(ctrlAlt, "ś")).Should().BeFalse();

        // AltGr+S: the right Alt is down; the character does not matter (the symbol may not be known at this point).
        AltGrKeys.DetectAltGr = _ => true;
        AltGrKeys.IsTypedCharacter(Stroke(ctrlAlt, "ś")).Should().BeTrue();
        AltGrKeys.IsTypedCharacter(Stroke(ctrlAlt, null)).Should().BeTrue();
        AltGrKeys.IsTypedCharacter(Stroke(KeyModifiers.Alt, "ś")).Should().BeFalse("without Ctrl the stroke is not AltGr");
    }

    [Fact]
    public void The_preference_is_off_by_default_and_is_applied_with_the_other_settings()
    {
        using TestHost host = new();
        PreferencesViewModel vm = NewPreferences(host);
        vm.EnableAltGr.Should().BeFalse();

        vm.EnableAltGr = true;

        AltGrKeys.Enabled.Should().BeFalse("a change stays in the window until it is applied");
        host.Settings.EnableAltGr.Should().BeFalse();

        vm.ApplyCommand.Execute(null);

        AltGrKeys.Enabled.Should().BeTrue();
        host.ReopenSettings().EnableAltGr.Should().BeTrue();
    }

    private static PreferencesViewModel NewPreferences(TestHost host)
    {
        LocalizationManager localization = new(host.Settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(host.Settings, NullLogger<IconThemeManager>.Instance);
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "prefs-" + Guid.NewGuid().ToString("N"));
        SpellChecker spellChecker = new(host.Settings, Path.Combine(root, "hunspell"), Path.Combine(root, "user"));
        UiDensityManager uiDensity = new(
            host.Settings, iconTheme, NullLogger<UiDensityManager>.Instance, () => "Segoe UI", _ => false);
        ThemeManager themes = new(host.Settings, ThemeStores.In(root), NullLogger<ThemeManager>.Instance);
        return new PreferencesViewModel(host.Settings, spellChecker, localization, iconTheme, host.Shortcuts, host.Registry, uiDensity, themes);
    }
}
