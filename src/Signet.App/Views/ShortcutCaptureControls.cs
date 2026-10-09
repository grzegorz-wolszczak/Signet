using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Signet.App.Infrastructure;
using Signet.App.Input;

namespace Signet.App.Views;

/// <summary>
/// A field that records the key combination pressed in it (one stroke) — IntelliJ's ShortcutTextField. Modifier
/// keys alone are ignored until a real key comes; every key (Tab, Enter, Esc too) becomes the stroke.
/// </summary>
public sealed class ShortcutCaptureBox : TextBox
{
    /// <summary>Creates the field.</summary>
    public ShortcutCaptureBox()
    {
        IsReadOnly = true;
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    /// <summary>Raised after <see cref="Gesture"/> changed.</summary>
    public event EventHandler? GestureChanged;

    /// <summary>The recorded stroke, or <c>null</c>.</summary>
    public KeyGesture? Gesture { get; private set; }

    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <summary>Sets (or clears) the stroke without pressing it.</summary>
    public void SetGesture(KeyGesture? gesture)
    {
        Gesture = gesture;
        Text = KeyGestureConversion.ToPortableString(gesture);
        GestureChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
        {
            return;
        }

        SetGesture(new KeyGesture(e.Key, e.KeyModifiers));
    }
}

/// <summary>
/// An area that records a mouse shortcut made in it — a click (single or double, any button, with the modifier keys
/// held) or a wheel turn — IntelliJ's MouseShortcutPanel.
/// </summary>
public sealed class MouseShortcutPad : Border
{
    private readonly TextBlock _text;

    /// <summary>Creates the area.</summary>
    public MouseShortcutPad()
    {
        Background = Brushes.Transparent;
        MinHeight = 48;
        Padding = new Avalonia.Thickness(8);
        _text = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        };
        Child = _text;
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    /// <summary>Raised after <see cref="Shortcut"/> changed.</summary>
    public event EventHandler? ShortcutChanged;

    /// <summary>The recorded mouse shortcut, or <c>null</c>.</summary>
    public MouseShortcut? Shortcut { get; private set; }

    /// <summary>The text shown while nothing is recorded.</summary>
    public string Hint
    {
        get => _hint;
        set
        {
            _hint = value;
            Refresh();
        }
    }

    private string _hint = string.Empty;

    /// <summary>Sets (or clears) the shortcut without making it.</summary>
    public void SetShortcut(MouseShortcut? shortcut)
    {
        Shortcut = shortcut;
        Refresh();
        ShortcutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh() => _text.Text = Shortcut?.DisplayText ?? _hint;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (MouseShortcutRouter.FromPress(e.GetCurrentPoint(this).Properties.PointerUpdateKind, e.KeyModifiers, e.ClickCount) is { } shortcut)
        {
            e.Handled = true;
            SetShortcut(shortcut);
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (MouseShortcutRouter.FromWheel(e.Delta.Y, e.KeyModifiers) is { } shortcut)
        {
            e.Handled = true;
            SetShortcut(shortcut);
        }
    }
}
