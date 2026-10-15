using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Avalonia.Input;
using Signet.App.Actions;
using Signet.App.Input;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="KeyboardShortcutManager"/>: registration, several shortcuts per action, conflicts (IntelliJ's
/// rules, including two-stroke shortcuts and panel scopes), persistence and the older settings format.
/// </summary>
public sealed class KeyboardShortcutManagerTests
{
    private static KeyStrokeShortcut Keys(Key key, KeyModifiers modifiers = KeyModifiers.None) => new(new KeyGesture(key, modifiers));

    private static KeyStrokeShortcut Chord(Key first, Key second) =>
        new(new KeyGesture(first, KeyModifiers.Control), new KeyGesture(second, KeyModifiers.Control));

    [Fact]
    public void Register_applies_default_and_is_idempotent()
    {
        using TestHost host = new();
        KeyboardShortcut first = host.Shortcuts.RegisterAction("A", "Ctrl+K", "desc");
        KeyboardShortcut again = host.Shortcuts.RegisterAction("A", "Ctrl+Z", "other");

        again.Should().BeSameAs(first);
        first.Shortcuts.Should().Equal(Keys(Key.K, KeyModifiers.Control));
        first.KeyGesture.Should().Be(new KeyGesture(Key.K, KeyModifiers.Control));
    }

    [Fact]
    public void An_action_can_have_several_keyboard_and_mouse_shortcuts()
    {
        using TestHost host = new();
        host.Shortcuts.RegisterAction("A", "Ctrl+K", "a");
        MouseShortcut mouse = new(MouseShortcutButton.Back, KeyModifiers.None);

        host.Shortcuts.AddShortcut("A", Chord(Key.K, Key.C));
        host.Shortcuts.AddShortcut("A", mouse);
        host.Shortcuts.AddShortcut("A", mouse);

        host.Shortcuts.Get("A")!.Shortcuts.Should().Equal(Keys(Key.K, KeyModifiers.Control), Chord(Key.K, Key.C), mouse);

        host.Shortcuts.RemoveShortcut("A", Keys(Key.K, KeyModifiers.Control));
        host.Shortcuts.Get("A")!.KeyGesture.Should().BeNull("the remaining keyboard shortcut has two strokes");

        host.Shortcuts.RemoveAllShortcuts("A");
        host.Shortcuts.Get("A")!.Shortcuts.Should().BeEmpty();
    }

    [Fact]
    public void Conflicts_follow_intellij_rules_for_strokes_and_mouse()
    {
        using TestHost host = new();
        host.Shortcuts.RegisterAction("CtrlK", "Ctrl+K", "a");
        host.Shortcuts.RegisterAction("ChordKC", string.Empty, "b");
        host.Shortcuts.AddShortcut("ChordKC", Chord(Key.K, Key.C));
        host.Shortcuts.RegisterAction("Mouse", string.Empty, "c");
        host.Shortcuts.AddShortcut("Mouse", new MouseShortcut(MouseShortcutButton.Left, KeyModifiers.Control));
        host.Shortcuts.RegisterAction("New", string.Empty, "d");

        // The first stroke of a two-stroke shortcut collides with the same one-stroke shortcut, and vice versa.
        host.Shortcuts.FindConflicts("New", Chord(Key.K, Key.X)).Should().Equal("CtrlK");
        host.Shortcuts.FindConflicts("New", Keys(Key.K, KeyModifiers.Control)).Should().BeEquivalentTo("ChordKC", "CtrlK");
        host.Shortcuts.FindConflicts("New", Chord(Key.K, Key.C)).Should().BeEquivalentTo("ChordKC", "CtrlK");
        host.Shortcuts.FindConflicts("New", new MouseShortcut(MouseShortcutButton.Left, KeyModifiers.Control)).Should().Equal("Mouse");
        host.Shortcuts.FindConflicts("New", new MouseShortcut(MouseShortcutButton.Left, KeyModifiers.Control, 2)).Should().BeEmpty();
    }

    [Fact]
    public void Shortcuts_collide_only_where_both_work_and_a_panel_only_shadows_a_window_wide_one()
    {
        using TestHost host = new();
        host.Shortcuts.RegisterAction("PanelA", "F2", "a", scope: "A");
        host.Shortcuts.RegisterAction("PanelA2", "Ctrl+F2", "a2", scope: "A");
        host.Shortcuts.RegisterAction("PanelB", "F3", "b", scope: "B");
        host.Shortcuts.RegisterAction("Code", "Ctrl+F3", "c", scope: KeyboardShortcutManager.CodeEditorScope);
        host.Shortcuts.RegisterAction("Window", "Ctrl+K", "w");

        // (TestHost registers the whole catalog too, so only the test actions are checked.)
        host.Shortcuts.FindConflicts("PanelB", Keys(Key.F2)).Should().NotContain("PanelA", "another panel may reuse the shortcut");
        host.Shortcuts.FindConflicts("PanelA", Keys(Key.F2, KeyModifiers.Control)).Should().Contain("PanelA2", "the same panel");
        host.Shortcuts.FindConflicts("Code", Keys(Key.F2)).Should().NotContain("PanelA", "the focus is in the editor or in the panel");
        host.Shortcuts.FindConflicts("PanelA", Keys(Key.F3, KeyModifiers.Control)).Should().NotContain("Code");
        host.Shortcuts.FindConflicts("Code", Keys(Key.K, KeyModifiers.Control)).Should().Contain("Window");
        host.Shortcuts.FindConflicts("Window", Keys(Key.F3, KeyModifiers.Control)).Should().Contain("Code");

        host.Shortcuts.FindConflicts("Window", Keys(Key.F2)).Should().NotContain("PanelA");
        host.Shortcuts.FindShadowed("Window", Keys(Key.F2)).Should().Contain("PanelA", "the panel takes F2 over while it has focus");
        host.Shortcuts.FindShadowed("PanelB", Keys(Key.K, KeyModifiers.Control)).Should().Contain("Window");
        host.Shortcuts.FindShadowed("Code", Keys(Key.F2)).Should().NotContain("PanelA");
    }

    [Fact]
    public void F2_for_Rename_Class_collides_with_nothing_although_two_panels_use_it()
    {
        using TestHost host = new();
        KeyStrokeShortcut f2 = Keys(Key.F2);
        host.Shortcuts.Get(AppActionIds.BookBrowserRename)!.Shortcuts.Should().Contain(f2);
        host.Shortcuts.Get(AppActionIds.ClipEditorRename)!.Shortcuts.Should().Contain(f2);

        host.Shortcuts.Get(AppActionIds.RenameClass)!.Scope.Should().Be(KeyboardShortcutManager.CodeEditorScope);
        host.Shortcuts.FindConflicts(AppActionIds.RenameClass, f2).Should().BeEmpty();
        host.Shortcuts.FindShadowed(AppActionIds.RenameClass, f2).Should().BeEmpty();
    }

    [Fact]
    public void RemoveConflicts_takes_the_shortcut_away_from_the_other_actions()
    {
        using TestHost host = new();
        host.Shortcuts.RegisterAction("A", "Ctrl+K", "a");
        host.Shortcuts.RegisterAction("B", "F5", "b");
        host.Shortcuts.AddShortcut("B", Keys(Key.F6));

        host.Shortcuts.RemoveConflicts("A", Keys(Key.F5));
        host.Shortcuts.AddShortcut("A", Keys(Key.F5));

        host.Shortcuts.Get("B")!.Shortcuts.Should().Equal(Keys(Key.F6));
        host.Shortcuts.Get("A")!.Shortcuts.Should().Equal(Keys(Key.K, KeyModifiers.Control), Keys(Key.F5));
    }

    [Fact]
    public void FindActions_matches_the_first_stroke_and_the_second_when_given()
    {
        using TestHost host = new();
        host.Shortcuts.RegisterAction("CtrlK", "Ctrl+K", "a");
        host.Shortcuts.RegisterAction("ChordKC", string.Empty, "b");
        host.Shortcuts.AddShortcut("ChordKC", Chord(Key.K, Key.C));
        host.Shortcuts.RegisterAction("Back", string.Empty, "c");
        host.Shortcuts.AddShortcut("Back", new MouseShortcut(MouseShortcutButton.Back, KeyModifiers.None));

        host.Shortcuts.FindActions(Keys(Key.K, KeyModifiers.Control)).Should().BeEquivalentTo("CtrlK", "ChordKC");
        host.Shortcuts.FindActions(Chord(Key.K, Key.C)).Should().Equal("ChordKC");
        host.Shortcuts.FindActions(new MouseShortcut(MouseShortcutButton.Back, KeyModifiers.None)).Should().Equal("Back");
    }

    [Fact]
    public void Overrides_persist_as_a_list_survive_reopen_then_reset()
    {
        using TestHost host = new();
        host.Shortcuts.RegisterAction(AppActionIds.Save, "Ctrl+S", "save");
        host.Shortcuts.AddShortcut(AppActionIds.Save, Chord(Key.K, Key.S));
        host.Shortcuts.AddShortcut(AppActionIds.Save, new MouseShortcut(MouseShortcutButton.Middle, KeyModifiers.Alt, 2));

        host.ReopenSettings().GetStringMap(KeyboardShortcutManager.SettingsGroup)
            .Should().ContainKey(AppActionIds.Save).WhoseValue.Should().Be("Ctrl+S; Ctrl+K, Ctrl+S; Mouse:Alt+Middlex2");

        KeyboardShortcutManager reopened = new(host.ReopenSettings());
        reopened.RegisterAction(AppActionIds.Save, "Ctrl+S", "save").Shortcuts.Should().HaveCount(3);

        reopened.ResetToDefault(AppActionIds.Save);
        host.ReopenSettings().GetStringMap(KeyboardShortcutManager.SettingsGroup).Should().NotContainKey(AppActionIds.Save);
    }

    [Fact]
    public void Older_single_shortcut_settings_are_read()
    {
        using TestHost host = new();
        host.Settings.SetStringMap(KeyboardShortcutManager.SettingsGroup, new Dictionary<string, string> { ["A"] = "F9", ["B"] = string.Empty });
        KeyboardShortcutManager manager = new(host.Settings);

        manager.RegisterAction("A", "Ctrl+S", "a").Shortcuts.Should().Equal(Keys(Key.F9));
        manager.RegisterAction("B", "Ctrl+B", "b").Shortcuts.Should().BeEmpty("an empty value removed the shortcut");
    }

    [Fact]
    public void Full_catalog_default_conflicts_are_the_known_context_dependent_pairs()
    {
        using TestHost host = new();

        // Ctrl+Shift+M is intentionally mapped to two context-dependent actions (Decrease Indent in Code Editor, Mark
        // Selected Text elsewhere). The second conflict is a known, deliberately kept one: Clip 60 defaults to
        // "Ctrl+0", the same as Zoom Reset, even though Clip 1..10 use Ctrl+Alt+N.
        List<(string, string)> pairs = host.Shortcuts.AllShortcuts
            .SelectMany(s => s.Shortcuts.SelectMany(shortcut => host.Shortcuts.FindConflicts(s.Id, shortcut).Select(other => (s.Id, other))))
            .Where(p => string.CompareOrdinal(p.Id, p.other) < 0)
            .Distinct()
            .ToList();

        pairs.Should().BeEquivalentTo(new[]
        {
            (AppActionIds.Clip(60), AppActionIds.ZoomReset),
            (AppActionIds.DecreaseIndent, AppActionIds.MarkSelection),
        });
    }
}
