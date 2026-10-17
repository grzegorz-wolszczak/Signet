using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// A "Fonts" page of the Preferences window (<see cref="FontPageViewModel"/>): a row per font setting, each with the preview of
/// its font under it.
/// </summary>
public partial class FontsPageView : UserControl
{
    /// <summary>
    /// Whether the settings can be changed (default: yes). Unlike <see cref="InputElement.IsEnabled"/> on the page it keeps
    /// the scroll bar and the preview alive, so a page of switched-off settings can still be scrolled and read.
    /// </summary>
    public static readonly StyledProperty<bool> IsContentEnabledProperty =
        AvaloniaProperty.Register<FontsPageView, bool>(nameof(IsContentEnabled), defaultValue: true);

    /// <summary>Gets or sets <see cref="IsContentEnabledProperty"/>.</summary>
    public bool IsContentEnabled
    {
        get => GetValue(IsContentEnabledProperty);
        set => SetValue(IsContentEnabledProperty, value);
    }

    /// <summary>Raised when the user has dragged the splitter: <see cref="PreviewWidth"/> is the new width.</summary>
    public event EventHandler? PreviewWidthChanged;

    /// <summary>
    /// The width in pixels of the space to the right of the settings (setting it fixes that width; the settings take the
    /// rest). By default it is as wide as the settings, so they take half of the page.
    /// </summary>
    public double PreviewWidth
    {
        get => PreviewColumn.ActualWidth > 0 ? PreviewColumn.ActualWidth : PreviewColumn.Width.Value;
        set => PreviewColumn.Width = new GridLength(Math.Max(PreviewColumn.MinWidth, value));
    }

    private ColumnDefinition PreviewColumn => PreviewGrid.ColumnDefinitions[2];

    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e) => PreviewWidthChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Initializes the view.</summary>
    public FontsPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildRows();
    }

    // The rows are created here rather than by an ItemsControl: the controls of a page exist before the page is shown
    // (an ItemsControl realizes its items only then), so the search of the Preferences window finds their labels.
    private void BuildRows()
    {
        Rows.Children.Clear();
        if (DataContext is not FontPageViewModel page)
        {
            return;
        }

        string? group = null;
        foreach (FontSettingViewModel font in page.Fonts)
        {
            if (font.Group is { } heading && heading != group)
            {
                Rows.Children.Add(new TextBlock
                {
                    Text = heading,
                    FontWeight = FontWeight.Bold,
                    Margin = new Avalonia.Thickness(0, Rows.Children.Count == 0 ? 0 : 6, 0, -6),
                });
            }

            group = font.Group;
            FontSettingView row = new() { DataContext = font, Name = "Font" + font.Key };
            if (_previewHeights.TryGetValue(font.Key, out int height))
            {
                row.SetUserPreviewHeight(height);
            }

            row.PreviewHeightChanged += (_, _) => _previewHeights[font.Key] = (int)Math.Round(row.UserPreviewHeight ?? 0);
            Rows.Children.Add(row);
        }
    }

    /// <summary>The row of a font setting of this page; <c>null</c> when it is not on it.</summary>
    public Control? RowOf(FontSettingViewModel font) =>
        Rows.Children.OfType<FontSettingView>().FirstOrDefault(r => ReferenceEquals(r.DataContext, font));

    // The heights the user gave the previews (setting key -> pixels), to remember: the ones loaded and the ones changed.
    private readonly Dictionary<string, int> _previewHeights = new(StringComparer.Ordinal);

    /// <summary>The heights the user gave the previews of the rows (setting key -&gt; pixels); the other rows have the default.</summary>
    public IReadOnlyDictionary<string, int> PreviewHeights => _previewHeights;

    /// <summary>Gives the preview of a row the height the user chose (also before the row exists: it is applied when it does).</summary>
    public void SetPreviewHeight(string key, int height)
    {
        _previewHeights[key] = height;
        foreach (FontSettingView row in Rows.Children.OfType<FontSettingView>())
        {
            if (row.DataContext is FontSettingViewModel font && font.Key == key)
            {
                row.SetUserPreviewHeight(height);
            }
        }
    }
}
