using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Signet.App.Actions;
using Signet.App.Input;
using Signet.App.Views.Tabs;

namespace Signet.App.Infrastructure;

/// <summary>
/// Runs the actions bound to mouse shortcuts (<see cref="MouseShortcut"/>): a button press with modifiers (single or
/// double click, the side buttons) or a wheel turn with modifiers, inside the part of the host chosen by a predicate.
/// The router listens in the tunnelling phase, so a mouse shortcut takes precedence over what the control under the
/// pointer would do with the same input (e.g. Ctrl+click on a link in Code View).
/// </summary>
/// <remarks>
/// Mouse shortcuts work only in Code View for now (<see cref="IsInCodeView"/>); a wider area is a different predicate
/// (e.g. <c>_ =&gt; true</c> for the whole window).
/// </remarks>
public sealed class MouseShortcutRouter
{
    private readonly Func<IEnumerable<AppAction>> _actions;
    private readonly Func<object?, bool> _appliesTo;

    /// <summary>Starts routing mouse input of <paramref name="host"/>.</summary>
    /// <param name="host">The control whose pointer events are watched (e.g. the main window).</param>
    /// <param name="actions">The actions whose mouse shortcuts are active.</param>
    /// <param name="appliesTo">Whether the event source (the control under the pointer) is in the area of mouse shortcuts.</param>
    public MouseShortcutRouter(Control host, Func<IEnumerable<AppAction>> actions, Func<object?, bool> appliesTo)
    {
        ArgumentNullException.ThrowIfNull(host);
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _appliesTo = appliesTo ?? throw new ArgumentNullException(nameof(appliesTo));
        host.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    /// <summary>The area of mouse shortcuts today: the text of a Code View tab.</summary>
    public static bool IsInCodeView(object? source) =>
        source is Visual visual && (visual as CodeTabView ?? visual.FindAncestorOfType<CodeTabView>()) is not null;

    /// <summary>The mouse shortcut of a button press (<c>null</c> for a press that cannot be one).</summary>
    public static MouseShortcut? FromPress(PointerUpdateKind kind, KeyModifiers modifiers, int clickCount)
    {
        MouseShortcutButton? button = kind switch
        {
            PointerUpdateKind.LeftButtonPressed => MouseShortcutButton.Left,
            PointerUpdateKind.MiddleButtonPressed => MouseShortcutButton.Middle,
            PointerUpdateKind.RightButtonPressed => MouseShortcutButton.Right,
            PointerUpdateKind.XButton1Pressed => MouseShortcutButton.Back,
            PointerUpdateKind.XButton2Pressed => MouseShortcutButton.Forward,
            _ => null,
        };
        return button is { } b ? new MouseShortcut(b, modifiers, clickCount) : null;
    }

    /// <summary>The mouse shortcut of a wheel turn (<c>null</c> for a horizontal turn).</summary>
    public static MouseShortcut? FromWheel(double deltaY, KeyModifiers modifiers) => deltaY switch
    {
        > 0 => new MouseShortcut(MouseShortcutButton.WheelUp, modifiers),
        < 0 => new MouseShortcut(MouseShortcutButton.WheelDown, modifiers),
        _ => null,
    };

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Visual visual && _appliesTo(e.Source)
            && FromPress(e.GetCurrentPoint(visual).Properties.PointerUpdateKind, e.KeyModifiers, e.ClickCount) is { } shortcut
            && Run(shortcut))
        {
            e.Handled = true;
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_appliesTo(e.Source) && FromWheel(e.Delta.Y, e.KeyModifiers) is { } shortcut && Run(shortcut))
        {
            e.Handled = true;
        }
    }

    // Runs the first enabled action with the shortcut; returns whether there was one.
    private bool Run(MouseShortcut shortcut)
    {
        AppAction? action = _actions().FirstOrDefault(a => a.IsEnabled && a.Shortcuts.Contains(shortcut));
        if (action is null)
        {
            return false;
        }

        action.Execute(null);
        return true;
    }
}
