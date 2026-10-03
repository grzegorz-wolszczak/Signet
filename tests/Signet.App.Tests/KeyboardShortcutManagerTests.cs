using AwesomeAssertions;
using Avalonia.Input;
using Signet.App.Actions;
using Signet.App.Input;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="KeyboardShortcutManager"/>: registration, overrides, conflicts, persistence.</summary>
public sealed class KeyboardShortcutManagerTests
{
    [Fact]
    public void Register_applies_default_and_is_idempotent()
    {
        using TestHost host = new();
        KeyboardShortcut first = host.Shortcuts.RegisterAction("A", "Ctrl+K", "desc");
        KeyboardShortcut again = host.Shortcuts.RegisterAction("A", "Ctrl+Z", "other");

        again.Should().BeSameAs(first);
        first.KeyGesture!.ToString().Should().Be(new KeyGesture(Key.K, KeyModifiers.Control).ToString());
    }

    [Fact]
    public void Setting_gesture_rejects_a_used_one_but_allows_clearing()
    {
        using TestHost host = new();
        host.Shortcuts.RegisterAction("A", "Ctrl+K", "a");
        host.Shortcuts.RegisterAction("B", "Ctrl+L", "b");

        host.Shortcuts.SetKeyGesture("B", new KeyGesture(Key.K, KeyModifiers.Control)).Should().BeFalse();
        host.Shortcuts.SetKeyGesture("B", null).Should().BeTrue();
        host.Shortcuts.Get("B")!.KeyGesture.Should().BeNull();
    }

    [Fact]
    public void Override_persists_and_survives_reopen_then_resets()
    {
        using TestHost host = new();
        host.Shortcuts.RegisterAction(AppActionIds.Save, "Ctrl+S", "save");
        host.Shortcuts.SetKeyGesture(AppActionIds.Save, new KeyGesture(Key.F9));

        host.ReopenSettings().GetStringMap(KeyboardShortcutManager.SettingsGroup)
            .Should().ContainKey(AppActionIds.Save).WhoseValue.Should().Be("F9");

        KeyboardShortcutManager reopened = new(host.ReopenSettings());
        reopened.RegisterAction(AppActionIds.Save, "Ctrl+S", "save")
            .KeyGesture!.ToString().Should().Be(new KeyGesture(Key.F9).ToString());

        reopened.ResetToDefault(AppActionIds.Save);
        host.ReopenSettings().GetStringMap(KeyboardShortcutManager.SettingsGroup).Should().NotContainKey(AppActionIds.Save);
    }

    [Fact]
    public void Full_catalog_default_conflicts_are_the_known_context_dependent_pairs()
    {
        using TestHost host = new();

        // Ctrl+Shift+M is intentionally mapped to two context-dependent actions
        // (Decrease Indent in Code View, Mark Selected Text elsewhere).
        // The second conflict is a known, deliberately kept one: Clip 60 defaults to "Ctrl+0",
        // the same as Zoom Reset, even though Clip 1..10 use Ctrl+Alt+N.
        host.Shortcuts.FindConflicts().Should().BeEquivalentTo(new[]
        {
            // The shortcut in Qt-style syntax, as shown in the shortcut editor fields, not "Ctrl+D0"
            // from Avalonia's KeyGesture.ToString().
            (AppActionIds.Clip(60), AppActionIds.ZoomReset, "Ctrl+0"),
            (AppActionIds.DecreaseIndent, AppActionIds.MarkSelection, "Ctrl+Shift+M"),
        });
    }
}
