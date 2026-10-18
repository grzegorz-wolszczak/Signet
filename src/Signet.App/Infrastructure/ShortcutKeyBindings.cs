using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Signet.App.Actions;
using Signet.App.Input;
using Signet.App.Resources;
using Signet.App.Services;

namespace Signet.App.Infrastructure;

/// <summary>
/// The keyboard shortcuts of actions on a control (the main window, or a panel or a code editor for the actions scoped
/// to it — those fire only while the focus is inside it): every one-stroke shortcut of an action becomes a <see cref="KeyBinding"/>, and
/// two-stroke shortcuts (IntelliJ's "second stroke") wait after their first stroke for the second one. The bindings
/// follow shortcut changes made in Preferences without a restart.
/// </summary>
/// <remarks>
/// When a stroke is both a one-stroke shortcut and the first stroke of a two-stroke one, the one-stroke shortcut wins
/// (the keymap editor reports such shortcuts as conflicts).
/// </remarks>
public sealed class ShortcutKeyBindings
{
    private readonly Control _host;
    private IReadOnlyList<AppAction> _actions = Array.Empty<AppAction>();
    private IReadOnlyList<(AppAction Action, KeyStrokeShortcut Shortcut)> _pendingChords = Array.Empty<(AppAction, KeyStrokeShortcut)>();
    private bool _suspended;

    // Shortcuts that could clash with AltGr (Ctrl+Alt+<character key>) while "Enable AltGr" is on: not KeyBindings (those
    // are tried before any key handler and would swallow the character), run from OnKeyDownTunnel instead.
    private IReadOnlyList<(KeyGesture Gesture, ICommand Command)> _altGrCandidates = Array.Empty<(KeyGesture, ICommand)>();

    /// <summary>Creates the bindings for <paramref name="host"/> (nothing is bound until <see cref="Attach"/>).</summary>
    public ShortcutKeyBindings(Control host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _host.AddHandler(InputElement.KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    /// <summary>Whether a two-stroke shortcut waits for its second stroke.</summary>
    public bool IsWaitingForSecondStroke => _pendingChords.Count > 0;

    /// <summary>Binds the shortcuts of <paramref name="actions"/> on the host (replaces the previous actions).</summary>
    public void Attach(IReadOnlyList<AppAction> actions)
    {
        Detach();
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        AltGrKeys.EnabledChanged += OnAltGrEnabledChanged;
        foreach (AppAction action in _actions)
        {
            action.PropertyChanged += OnActionPropertyChanged;
        }

        Rebuild();
    }

    /// <summary>Removes the bindings and stops following the actions.</summary>
    public void Detach()
    {
        foreach (AppAction action in _actions)
        {
            action.PropertyChanged -= OnActionPropertyChanged;
        }

        AltGrKeys.EnabledChanged -= OnAltGrEnabledChanged;
        _actions = Array.Empty<AppAction>();
        _pendingChords = Array.Empty<(AppAction, KeyStrokeShortcut)>();
        _altGrCandidates = Array.Empty<(KeyGesture, ICommand)>();
        _host.KeyBindings.Clear();
    }

    /// <summary>
    /// Switches the bindings off (e.g. while a name is typed in an editor, so that a shortcut does not act on the tree)
    /// or back on.
    /// </summary>
    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        Rebuild();
    }

    private void OnAltGrEnabledChanged(object? sender, EventArgs e) => Rebuild();

    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppAction.Shortcuts))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _host.KeyBindings.Clear();
        _pendingChords = Array.Empty<(AppAction, KeyStrokeShortcut)>();
        _altGrCandidates = Array.Empty<(KeyGesture, ICommand)>();
        if (_suspended)
        {
            return;
        }

        List<(AppAction Action, KeyStrokeShortcut Shortcut)> keyboard = _actions
            .SelectMany(a => a.Shortcuts.OfType<KeyStrokeShortcut>().Select(s => (a, s)))
            .ToList();

        List<(KeyGesture, ICommand)> altGr = new();

        void Bind(KeyGesture gesture, ICommand command)
        {
            if (AltGrKeys.Enabled && AltGrKeys.CanBeTypedCharacter(gesture))
            {
                altGr.Add((gesture, command));
                return;
            }

            _host.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
        }

        // One-stroke shortcuts first: KeyBindings fire the first match.
        foreach ((AppAction action, KeyStrokeShortcut shortcut) in keyboard.Where(k => k.Shortcut.Second is null))
        {
            Bind(shortcut.First, action);
        }

        foreach (IGrouping<string, (AppAction Action, KeyStrokeShortcut Shortcut)> chords in keyboard
                     .Where(k => k.Shortcut.Second is not null)
                     .GroupBy(k => KeyGestureConversion.ToPortableString(k.Shortcut.First), StringComparer.Ordinal))
        {
            List<(AppAction, KeyStrokeShortcut)> candidates = chords.ToList();
            KeyGesture first = candidates[0].Item2.First;
            Bind(first, new StartChordCommand(this, first, candidates));
        }

        _altGrCandidates = altGr;
    }

    // The first stroke of two-stroke shortcuts: wait for the second one (shown on the status bar, as in IntelliJ).
    private void StartChord(KeyGesture first, IReadOnlyList<(AppAction, KeyStrokeShortcut)> candidates)
    {
        _pendingChords = candidates;
        App.Services?.GetService<IStatusBarService>()?.ShowMessage(
            Strings.Format("Status_WaitingForSecondStroke", KeyGestureConversion.ToPortableString(first)), TimeSpan.FromSeconds(5));
    }

    // The stroke after a first stroke: runs the matching two-stroke shortcut; any other key cancels (and is consumed).
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (_pendingChords.Count == 0)
        {
            RunAltGrCandidate(e);
            return;
        }

        if (IsModifierKey(e.Key))
        {
            return;
        }

        IReadOnlyList<(AppAction Action, KeyStrokeShortcut Shortcut)> pending = _pendingChords;
        _pendingChords = Array.Empty<(AppAction, KeyStrokeShortcut)>();
        e.Handled = true;
        foreach ((AppAction action, KeyStrokeShortcut shortcut) in pending)
        {
            if (shortcut.Second is { } second && second.Key == e.Key && second.KeyModifiers == e.KeyModifiers)
            {
                if (action.CanExecute(null))
                {
                    action.Execute(null);
                }

                return;
            }
        }
    }

    // A Ctrl+Alt shortcut while "Enable AltGr" is on: what a KeyBinding would do, except when the stroke types a character.
    private void RunAltGrCandidate(KeyEventArgs e)
    {
        if (e.Handled || _altGrCandidates.Count == 0 || AltGrKeys.IsTypedCharacter(e))
        {
            return;
        }

        foreach ((KeyGesture gesture, ICommand command) in _altGrCandidates)
        {
            if (gesture.Matches(e) && command.CanExecute(null))
            {
                command.Execute(null);
                e.Handled = true;
                return;
            }
        }
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    private sealed class StartChordCommand(
        ShortcutKeyBindings owner, KeyGesture first, IReadOnlyList<(AppAction, KeyStrokeShortcut)> candidates) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => owner.StartChord(first, candidates);
    }
}
