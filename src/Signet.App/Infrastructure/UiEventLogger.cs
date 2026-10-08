using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Signet.App.Actions;

namespace Signet.App.Infrastructure;

/// <summary>
/// Writes what the user does in any window to the debug log (<see cref="DebugLog"/>), through class handlers
/// registered once for the whole application: button / check box / radio button clicks, menu items, combo box and
/// tab choices, windows opened and closed, and where a window was moved or resized to (once it stops).
/// </summary>
/// <remarks>
/// Every control is named so that the same label in two windows can be told apart:
/// <c>Button "Common_Ok" [MetadataEditorWindow]</c> — the resource key of its text (independent of the UI language),
/// otherwise its name or action id, and the window plus the panel (<see cref="UserControl"/>) it is in. Commands of
/// <see cref="AppAction"/>s are logged by the action registry instead (also when run from a shortcut or a toolbar),
/// so they are not logged twice here. Text typed into controls is never read.
/// </remarks>
public static class UiEventLogger
{
    private const int MaxLabelLength = 60;
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(800);
    private static bool _registered;

    /// <summary>Registers the class handlers (once).</summary>
    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        Button.ClickEvent.AddClassHandler<Button>(OnButtonClick, RoutingStrategies.Bubble, handledEventsToo: true);
        MenuItem.ClickEvent.AddClassHandler<MenuItem>(OnMenuItemClick, RoutingStrategies.Bubble, handledEventsToo: true);
        SelectingItemsControl.SelectionChangedEvent.AddClassHandler<SelectingItemsControl>(
            OnSelectionChanged, RoutingStrategies.Bubble, handledEventsToo: true);
        Window.WindowOpenedEvent.AddClassHandler<Window>(OnWindowOpened, RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
        Window.WindowClosedEvent.AddClassHandler<Window>(OnWindowClosed, RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>A control as it appears in the log: kind, label and place.</summary>
    public static string Describe(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        string kind = control switch
        {
            RadioButton => "RadioButton",
            CheckBox => "CheckBox",
            ToggleButton => "ToggleButton",
            MenuItem => "MenuItem",
            ComboBox => "ComboBox",
            TabControl => "Tab",
            _ => "Button",
        };
        return $"{kind} \"{Label(control)}\" [{Place(control)}]";
    }

    /// <summary>Where a control is: the window type, plus the innermost panel (<see cref="UserControl"/>) in it.</summary>
    public static string Place(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        string window = TopLevel.GetTopLevel(visual) is { } top ? WindowName(top) : "(no window)";
        UserControl? panel = visual as UserControl ?? visual.FindAncestorOfType<UserControl>();
        return panel is null ? window : $"{window} > {panel.GetType().Name}";
    }

    private static void OnButtonClick(Button button, RoutedEventArgs e)
    {
        if (!DebugLog.IsEnabled || button.Command is AppAction)
        {
            return;
        }

        string state = button is ToggleButton toggle ? " → " + (toggle.IsChecked switch
        {
            true => "checked",
            false => "unchecked",
            null => "indeterminate",
        }) : string.Empty;
        DebugLog.Write("UI", "click " + Describe(button) + state);
    }

    private static void OnMenuItemClick(MenuItem item, RoutedEventArgs e)
    {
        // A submenu header opens its submenu; an action is logged by the action registry.
        if (!DebugLog.IsEnabled || item.Command is AppAction || item.HasSubMenu || !ReferenceEquals(e.Source, item))
        {
            return;
        }

        DebugLog.Write("UI", "menu " + Describe(item));
    }

    private static void OnSelectionChanged(SelectingItemsControl control, SelectionChangedEventArgs e)
    {
        // Only a choice the user makes: an open drop-down, or a control with the keyboard focus / under the pointer.
        if (!DebugLog.IsEnabled || !ReferenceEquals(e.Source, control) || e.AddedItems.Count == 0
            || control is not (ComboBox or TabControl)
            || !(control is ComboBox { IsDropDownOpen: true } || control.IsKeyboardFocusWithin || control.IsPointerOver))
        {
            return;
        }

        string chosen = ItemLabel(e.AddedItems[0]);
        DebugLog.Write("UI", $"choose {Describe(control)} → \"{chosen}\"");
    }

    private static void OnWindowOpened(Window window, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, window))
        {
            return;
        }

        if (DebugLog.IsEnabled)
        {
            DebugLog.Write("Window", $"opened {WindowName(window)} at {Bounds(window)}");
        }

        // Where the user moves or resizes the window to — logged once it stops changing.
        DispatcherTimer settle = new() { Interval = SettleDelay };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            if (DebugLog.IsEnabled && window.IsVisible)
            {
                DebugLog.Write("Window", $"moved/resized {WindowName(window)} to {Bounds(window)} ({window.WindowState})");
            }
        };
        void Restart()
        {
            settle.Stop();
            settle.Start();
        }

        window.PositionChanged += (_, _) => Restart();
        window.Resized += (_, _) => Restart();
        window.Closed += (_, _) => settle.Stop();
    }

    private static void OnWindowClosed(Window window, RoutedEventArgs e)
    {
        if (DebugLog.IsEnabled && ReferenceEquals(e.Source, window))
        {
            DebugLog.Write("Window", $"closed {WindowName(window)}");
        }
    }

    private static string WindowName(TopLevel top) => top switch
    {
        Dock.Avalonia.Controls.HostWindow host => $"FloatingPanelWindow \"{host.Title}\"",
        _ => top.GetType().Name,
    };

    private static string Bounds(Window window)
    {
        Size size = window.FrameSize ?? window.ClientSize;
        return FormattableString.Invariant($"{window.Position.X},{window.Position.Y} {size.Width:0}×{size.Height:0}");
    }

    // The resource key of the control's text, otherwise its name, action id or text.
    private static string Label(Control control)
    {
        object? content = control switch
        {
            MenuItem item => item.Header,
            HeaderedContentControl headered => headered.Header,
            ContentControl contentControl => contentControl.Content,
            _ => null,
        };
        if (control is TabControl tabs)
        {
            content = tabs.SelectedItem is TabItem tab ? tab.Header : tabs.SelectedItem;
        }

        string? text = TextOf(content) ?? TextOf(ToolTip.GetTip(control));
        string? label = DebugLog.ResourceKeyOf(text)
            ?? (control.Name is { Length: > 0 } name ? "#" + name : null)
            ?? (control is MenuItem { Command: AppAction action } ? action.Id : null)
            ?? text;
        return Shorten(label ?? "(unnamed)");
    }

    private static string ItemLabel(object? item)
    {
        string? text = item switch
        {
            TabItem tab => TextOf(tab.Header),
            ComboBoxItem comboItem => TextOf(comboItem.Content),
            _ => TextOf(item),
        };
        return Shorten(DebugLog.ResourceKeyOf(text) ?? text ?? "(none)");
    }

    private static string? TextOf(object? content) => content switch
    {
        null => null,
        string s => s,
        TextBlock block => block.Text,
        ContentControl inner => TextOf(inner.Content),
        Panel panel => panel.Children.Select(TextOf).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
        Control => null,
        _ => content.ToString(),
    };

    private static string Shorten(string text)
    {
        string single = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return single.Length <= MaxLabelLength ? single : single[..MaxLabelLength] + "…";
    }
}
