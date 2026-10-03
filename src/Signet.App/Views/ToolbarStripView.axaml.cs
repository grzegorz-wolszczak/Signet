using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Toolbars;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// View of a single toolbar: buttons in a <see cref="ToolbarOverflowPanel"/> + a "»" button with
/// a menu of the items that did not fit, and the menus of action-list buttons
/// (<c>tbHeadings</c>/<c>tbCase</c>).
/// </summary>
public partial class ToolbarStripView : UserControl
{
    private ToolbarOverflowPanel? _panel;

    /// <summary>Initializes the view.</summary>
    public ToolbarStripView()
    {
        InitializeComponent();
        Items.LayoutUpdated += OnItemsLayoutUpdated;
    }

    // ItemsPanelRoot exists only after the template is applied — hook up on the first layout pass.
    private void OnItemsLayoutUpdated(object? sender, EventArgs e)
    {
        if (_panel is not null || Items.ItemsPanelRoot is not ToolbarOverflowPanel panel)
        {
            return;
        }

        _panel = panel;
        panel.PropertyChanged += OnPanelPropertyChanged;
        Chevron.IsVisible = panel.HasOverflow;
        Items.LayoutUpdated -= OnItemsLayoutUpdated;
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_panel is not null)
        {
            _panel.PropertyChanged -= OnPanelPropertyChanged;
            _panel = null;
        }

        Items.LayoutUpdated -= OnItemsLayoutUpdated;
        Items.LayoutUpdated += OnItemsLayoutUpdated;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPanelPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ToolbarOverflowPanel.HasOverflowProperty && _panel is not null)
        {
            Chevron.IsVisible = _panel.HasOverflow;
        }
    }

    private void OnChevronClick(object? sender, RoutedEventArgs e)
    {
        if (_panel is null)
        {
            return;
        }

        MenuFlyout flyout = new();
        foreach (object? item in _panel.OverflowedItems)
        {
            if (item is not ToolbarItemViewModel entry || entry.IsSeparator)
            {
                continue;
            }

            if (entry.IsDropDown)
            {
                MenuItem sub = new() { Header = entry.ToolTip, Icon = IconFor(entry.IconKey) };
                sub.ItemsSource = BuildChoiceItems(entry);
                flyout.Items.Add(sub);
            }
            else if (entry.Action is { } action)
            {
                flyout.Items.Add(new MenuItem
                {
                    Header = MenuText(action),
                    Icon = IconFor(action.IconKey),
                    Command = action,
                    InputGesture = action.Gesture,
                });
            }
        }

        flyout.ShowAt(Chevron);
    }

    // Menu of an action-list button — created once, after the button is loaded.
    private void OnDropDownLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not SplitButton button || button.DataContext is not ToolbarItemViewModel entry || button.Flyout is not null)
        {
            return;
        }

        MenuFlyout flyout = new() { ItemsSource = BuildChoiceItems(entry) };
        button.Flyout = flyout;
        entry.MenuRequested += (_, _) => flyout.ShowAt(button);
    }

    private static List<MenuItem> BuildChoiceItems(ToolbarItemViewModel entry)
    {
        List<MenuItem> items = new();
        foreach (AppAction action in entry.MenuActions)
        {
            items.Add(new MenuItem
            {
                Header = MenuText(action),
                Icon = IconFor(action.IconKey),
                Command = entry.ChooseCommand,
                CommandParameter = action,

                // The menu is built once — bind so that a shortcut changed in Preferences shows up immediately.
                [!MenuItem.InputGestureProperty] = new ReflectionBinding(nameof(AppAction.Gesture))
                {
                    Source = action,
                    Mode = BindingMode.OneWay,
                },
            });
        }

        return items;
    }

    private static string MenuText(AppAction action) => AppAction.ConvertMnemonics(action.Descriptor.Text);

    private static Image? IconFor(string? iconKey) =>
        IconKeyToImageConverter.Instance.Convert(iconKey, typeof(IImage), null, CultureInfo.InvariantCulture) is IImage image
            ? new Image { Source = image, Width = 16, Height = 16 }
            : null;
}
