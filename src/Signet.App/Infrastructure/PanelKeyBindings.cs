using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Actions;

namespace Signet.App.Infrastructure;

/// <summary>
/// Key bindings of panel-scoped actions (<see cref="AppActionIds.IsPanelCategory"/>) on a control: they fire only
/// while the focus is inside it, and follow a shortcut change made in Preferences without a restart.
/// </summary>
public sealed class PanelKeyBindings
{
    private readonly Control _host;
    private IReadOnlyList<AppAction> _actions = Array.Empty<AppAction>();
    private bool _suspended;

    /// <summary>Creates the bindings for <paramref name="host"/> (nothing is bound until <see cref="Attach"/>).</summary>
    public PanelKeyBindings(Control host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Binds the gestures of <paramref name="actions"/> on the host (replaces the previous actions).</summary>
    public void Attach(IReadOnlyList<AppAction> actions)
    {
        Detach();
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
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

        _actions = Array.Empty<AppAction>();
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

    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppAction.Gesture))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _host.KeyBindings.Clear();
        if (_suspended)
        {
            return;
        }

        foreach (AppAction action in _actions)
        {
            if (action.Gesture is { } gesture)
            {
                _host.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = action });
            }
        }
    }
}
