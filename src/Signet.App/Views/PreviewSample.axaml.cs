using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// The Preview panel for the preview of the Preferences window (see <c>PreviewSample.axaml</c>): a page drawn in the fonts of the
/// settings, with one block marked as the real preview marks the block of the caret (a background or an outline, in the color of
/// the theme, fading after a delay when that is set). A click on a block moves the mark there. The colors (the highlight, and
/// the background and text forced on the book) are those of the theme being edited, so a change shows before Save.
/// </summary>
public partial class PreviewSample : UserControl
{
    private readonly Border[] _blocks;
    private readonly DispatcherTimer _hideTimer = new();
    private PreferencesViewModel? _viewModel;
    private int _marked = 1;

    /// <summary>Initializes the sample.</summary>
    public PreviewSample()
    {
        InitializeComponent();
        _blocks = new[] { Block0, Block1, Block2 };
        foreach (Border block in _blocks)
        {
            block.PointerPressed += OnBlockPressed;
        }

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Mark(-1);
        };
        DataContextChanged += (_, _) => Track(DataContext as PreferencesViewModel);
        AttachedToVisualTree += (_, _) => AppTheme.Changed += OnThemeChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            AppTheme.Changed -= OnThemeChanged;
            _hideTimer.Stop();
        };
    }

    /// <summary>The block that is marked now (0-2), or -1 when the mark has faded.</summary>
    public int MarkedBlock { get; private set; } = 1;

    private void OnThemeChanged(object? sender, EventArgs e) => Mark(_marked);

    private void Track(PreferencesViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_viewModel is not null)
        {
            _viewModel.Theme.ThemeEdited -= OnThemeEdited;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Theme.ThemeEdited += OnThemeEdited;
        }

        Show(1);
    }

    // A color of the theme being edited changed: the page and the mark are drawn again in it.
    private void OnThemeEdited(object? sender, EventArgs e) => Mark(_marked);

    // The theme being edited (with the changes not saved yet), or the one in use without a view model.
    private ResolvedTheme EditedTheme => _viewModel?.Theme.Resolved ?? AppTheme.Current;

    // A change of the settings of the highlight shows the mark again (and starts its fading again).
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName?.StartsWith("PreviewHighlight", StringComparison.Ordinal) == true)
        {
            Show(_marked < 0 ? 1 : _marked);
        }
    }

    private void OnBlockPressed(object? sender, PointerPressedEventArgs e)
    {
        int index = Array.IndexOf(_blocks, sender);
        if (index >= 0)
        {
            Show(index);
        }
    }

    // Marks a block and, when the highlight fades, starts the timer of its fading.
    private void Show(int index)
    {
        Mark(index);
        _hideTimer.Stop();
        if (_viewModel is { PreviewHighlightAutoHide: true } vm)
        {
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, vm.PreviewHighlightAutoHideDelay));
            _hideTimer.Start();
        }
    }

    private void Mark(int index)
    {
        _marked = index;
        MarkedBlock = index;
        PreviewHighlightStyle style = _viewModel?.PreviewHighlightStyle ?? PreviewHighlightStyle.Background;
        int opacity = Math.Clamp(_viewModel?.PreviewHighlightOpacity ?? 45, 5, 100);
        int width = Math.Clamp(_viewModel?.PreviewHighlightOutlineWidth ?? 3, 1, 20);
        ResolvedTheme theme = EditedTheme;
        Color color = theme.Color(ThemeKeys.PreviewHighlight) ?? Color.FromRgb(0xFF, 0xEB, 0x3B);
        Page.Background = new SolidColorBrush(theme.Color(ThemeKeys.PreviewBackground) ?? Colors.White);
        TextElement.SetForeground(PageContent, new SolidColorBrush(theme.Color(ThemeKeys.PreviewText) ?? Colors.Black));
        for (int i = 0; i < _blocks.Length; i++)
        {
            Border block = _blocks[i];
            bool marked = i == index;
            // The width of the outline is kept on every block, so marking one does not move the page.
            block.BorderThickness = new Thickness(style == PreviewHighlightStyle.Outline ? width : 0);
            block.Background = marked && style == PreviewHighlightStyle.Background
                ? new SolidColorBrush(color, opacity / 100.0)
                : Brushes.Transparent;
            block.BorderBrush = marked && style == PreviewHighlightStyle.Outline ? new SolidColorBrush(color) : Brushes.Transparent;
        }
    }
}
