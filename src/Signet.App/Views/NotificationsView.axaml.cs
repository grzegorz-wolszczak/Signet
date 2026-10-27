using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Signet.Core.Misc;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// "Notifications" panel view — the list of the status bar messages, newest first. While the panel
/// is actually visible on screen (expanded, not just a collapsed tab), the warnings count as read.
/// </summary>
public partial class NotificationsView : UserControl
{
    private NotificationsViewModel? _boundViewModel;
    private bool _isShown;
    private Style[] _appearanceStyles = Array.Empty<Style>();

    /// <summary>Initializes the view.</summary>
    public NotificationsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        DetachedFromVisualTree += (_, _) => _isShown = false;
        AttachedToVisualTree += (_, _) => ApplyAppearance();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.WarningArrived -= OnWarningArrived;
            _boundViewModel.AppearanceChanged -= OnAppearanceChanged;
        }

        _boundViewModel = DataContext as NotificationsViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.WarningArrived += OnWarningArrived;
            _boundViewModel.AppearanceChanged += OnAppearanceChanged;
        }

        ApplyAppearance();
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => ApplyAppearance();

    // The font of a row's text (the time and the message) and the vertical space between rows — Preferences ›
    // Appearance › General › Fonts › "Notifications entries" and the spacing setting of the panel's General page.
    private void ApplyAppearance()
    {
        foreach (Style old in _appearanceStyles)
        {
            List.Styles.Remove(old);
        }

        if (_boundViewModel is null)
        {
            _appearanceStyles = Array.Empty<Style>();
            return;
        }

        NotificationsAppearance look = _boundViewModel.Appearance;

        Style entries = new(x => x.Is<TextBlock>().Class("entryFont"));
        // Always set (Regular when not set): a style taken away does not give the text its weight back, a style added
        // later wins over it.
        entries.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeightOptions.ToAvalonia(look.FontWeight)));
        if (look.FontFamily.Length > 0)
        {
            entries.Setters.Add(new Setter(TextElement.FontFamilyProperty, new FontFamily(look.FontFamily)));
        }

        if (look.FontSize > 0)
        {
            entries.Setters.Add(new Setter(TextElement.FontSizeProperty, (double)look.FontSize));
        }

        Style rows = new(x => x.OfType<ListBoxItem>());
        rows.Setters.Add(new Setter(Layoutable.MarginProperty, new Thickness(0, 0, 0, look.EntrySpacing)));

        _appearanceStyles = new[] { entries, rows };
        foreach (Style style in _appearanceStyles)
        {
            List.Styles.Add(style);
        }
    }

    // The panel became visible (e.g. a collapsed tab was slid out) — the user can see the list.
    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        _isShown = e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Height > 0;
        if (_isShown)
        {
            _boundViewModel?.MarkAsRead();
        }
    }

    // A warning that arrives while the list is on screen is seen right away. Deferred, so the
    // handlers of WarningArrived in the main window (showing the panel) run with the count raised.
    private void OnWarningArrived(object? sender, EventArgs e)
    {
        if (_isShown)
        {
            Dispatcher.UIThread.Post(() => _boundViewModel?.MarkAsRead());
        }
    }
}
