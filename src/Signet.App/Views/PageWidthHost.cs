using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Signet.App.Views;

/// <summary>
/// The settings of a Preferences page and a visible splitter after them: what the settings do not take stays free, and the
/// user drags the splitter to give the controls the width they like. The width is remembered per page (see
/// <see cref="UserWidth"/>); double-clicking the splitter goes back to the default one.
/// </summary>
/// <remarks>
/// The default width fits the content (what the controls and the texts take when nothing wraps), but not more than
/// <see cref="DefaultMaxWidth"/> — a long hint measured on one line would otherwise make the default as wide as the page.
/// The splitter can go as far as the right edge of the page.
/// </remarks>
[TemplatePart(PartGrid, typeof(Grid))]
[TemplatePart(PartSplitter, typeof(GridSplitter))]
public sealed class PageWidthHost : ContentControl
{
    private const string PartGrid = "PART_Grid";
    private const string PartSplitter = "PART_Splitter";

    /// <summary>The widest default width, in pixels.</summary>
    public const double DefaultMaxWidth = 720;

    /// <summary>The narrowest width of the settings.</summary>
    public const double MinSettingsWidth = 480;

    /// <summary>The width the user gave the settings; <see cref="double.NaN"/> = the default one (fitted to the content).</summary>
    public static readonly StyledProperty<double> UserWidthProperty =
        AvaloniaProperty.Register<PageWidthHost, double>(nameof(UserWidth), double.NaN);

    private Grid? _grid;
    private GridSplitter? _splitter;

    /// <summary>Creates the host.</summary>
    public PageWidthHost()
    {
        Focusable = false;
        IsTabStop = false;
    }

    /// <summary>The width of the settings in pixels the user chose; <see cref="double.NaN"/> = not chosen.</summary>
    public double UserWidth
    {
        get => GetValue(UserWidthProperty);
        set => SetValue(UserWidthProperty, value);
    }

    /// <summary>Raised when the user has moved the splitter or reset it (the window remembers <see cref="UserWidth"/>).</summary>
    public event EventHandler? UserWidthChanged;

    /// <summary>The width the settings have now (what the first column of the host takes).</summary>
    public double SettingsWidth => _grid?.ColumnDefinitions[0].Width.Value ?? double.NaN;

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_splitter is not null)
        {
            _splitter.DragCompleted -= OnDragCompleted;
            _splitter.DoubleTapped -= OnSplitterDoubleTapped;
        }

        _grid = e.NameScope.Find<Grid>(PartGrid);
        _splitter = e.NameScope.Find<GridSplitter>(PartSplitter);
        if (_splitter is not null)
        {
            _splitter.DragCompleted += OnDragCompleted;
            _splitter.DoubleTapped += OnSplitterDoubleTapped;
        }

        ApplyWidth();
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // The available width is known only here (and changes with the window), so the first column follows it.
        if (_grid is not null && Math.Abs(finalSize.Width - _lastWidth) > 0.5)
        {
            _lastWidth = finalSize.Width;
            ApplyWidth(finalSize.Width);
        }

        return base.ArrangeOverride(finalSize);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UserWidthProperty)
        {
            ApplyWidth();
        }
    }

    private double _lastWidth;

    private void ApplyWidth() => ApplyWidth(Bounds.Width > 0 ? Bounds.Width : _lastWidth);

    private void ApplyWidth(double available)
    {
        if (_grid is null || available <= 0)
        {
            return;
        }

        double room = Math.Max(0, available - _grid.ColumnDefinitions[1].Width.Value);
        double width = double.IsNaN(UserWidth) ? DefaultWidth(room) : Math.Min(UserWidth, room);
        _grid.ColumnDefinitions[0].Width = new GridLength(Math.Max(Math.Min(MinSettingsWidth, room), width));
    }

    // What the content takes when nothing wraps, within [MinSettingsWidth, DefaultMaxWidth] and the room there is.
    private double DefaultWidth(double room)
    {
        double wanted = MinSettingsWidth;
        if (Content is Control content)
        {
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            wanted = content.DesiredSize.Width;
        }

        return Math.Min(room, Math.Clamp(wanted, MinSettingsWidth, DefaultMaxWidth));
    }

    private void OnDragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        if (_grid is null)
        {
            return;
        }

        UserWidth = Math.Round(_grid.ColumnDefinitions[0].Width.Value);
        UserWidthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        UserWidth = double.NaN;
        UserWidthChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
