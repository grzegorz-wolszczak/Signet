using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Signet.App.CodeEditor;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// A page of the keys of Appearance › Color Scheme (<see cref="ThemeKeysPageViewModel"/>). The tree's source is built
/// here (TreeDataGrid sources belong to the views), and so is the preview of the page: an editor with the demo text of
/// a code page (two for JavaScript and JSON), or the sample controls of Preview and Interface.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the DataContext changes, like in the other TreeDataGrid views.")]
public partial class ThemeKeysView : UserControl
{
    private ThemeKeysPageViewModel? _bound;
    private LocalizedColumns<ThemeKeyNode>? _columns;
    private HierarchicalTreeDataGridSource<ThemeKeyNode>? _source;
    private TreeSelectionSync<ThemeKeyNode>? _selection;

    // The preview: its editors (code pages) or its sample controls (Preview, Interface).
    private readonly List<ThemePreviewEditor> _editors = new();
    private ThemeControlsPreview? _controls;

    // A scroll to the selected key waits for the tree to be shown (a link selects the key before its tab is shown).
    private bool _scrollPending;

    /// <summary>Initializes the view.</summary>
    public ThemeKeysView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildTree();
    }

    /// <summary>The width of the tree of keys (pixels); the left splitter changes it.</summary>
    public double TreeWidth
    {
        get => Split.ColumnDefinitions[0].Width.Value;
        set => Split.ColumnDefinitions[0].Width = new GridLength(Math.Max(Split.ColumnDefinitions[0].MinWidth, value));
    }

    /// <summary>The height of the preview (pixels); the splitter above it changes it.</summary>
    public double PreviewHeight
    {
        get => Split.RowDefinitions[2].Height.Value;
        set => Split.RowDefinitions[2].Height = new GridLength(Math.Max(Split.RowDefinitions[2].MinHeight, value));
    }

    private void BuildTree()
    {
        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _source?.Dispose();
        _source = null;
        _selection = null;
        _bound = DataContext as ThemeKeysPageViewModel;
        if (_bound is not null)
        {
            ThemeKeysPageViewModel vm = _bound;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _columns = new LocalizedColumns<ThemeKeyNode>();
            _source = new HierarchicalTreeDataGridSource<ThemeKeyNode>(vm.Roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("ColorScheme_KeysColumn", "ThemeKeyNameCellTemplate", new GridLength(1, GridUnitType.Star)),
                        n => n.Children,
                        n => n.IsGroup,
                        n => n.IsExpanded),
                },
            };
            _selection = new TreeSelectionSync<ThemeKeyNode>(_source, vm.Roots, n => n.Children, () => vm.Selected, n => vm.Selected = n);
        }

        Tree.Source = _source;
        _selection?.SelectFromViewModel();
        BuildPreview();
    }

    private void BuildPreview()
    {
        _editors.Clear();
        _controls = null;
        PreviewHost.Child = null;
        if (_bound is not { } vm)
        {
            return;
        }

        ResolvedTheme theme = vm.Theme ?? ResolvedTheme.Defaults;
        switch (vm.Page)
        {
            case ThemePage.Preview or ThemePage.Interface:
                _controls = new ThemeControlsPreview();
                _controls.KeyClicked += OnPreviewKeyClicked;
                _controls.Show(vm.Page, theme);
                PreviewHost.Child = _controls;
                return;
            case ThemePage.JavaScriptJson:
                Grid columns = new() { ColumnDefinitions = new ColumnDefinitions("*,*") };
                columns.Children.Add(NewEditor(ThemeSamples.JavaScript, theme));
                Control json = NewEditor(ThemeSamples.Json, theme);
                Grid.SetColumn(json, 1);
                columns.Children.Add(json);
                PreviewHost.Child = columns;
                return;
            default:
                ThemeSample sample = SampleOf(vm.Page);
                PreviewHost.Child = NewEditor(vm.ExtendedHighlighting ? sample : sample.Basic, theme);
                return;
        }
    }

    private static ThemeSample SampleOf(ThemePage page) => page switch
    {
        ThemePage.General => ThemeSamples.General,
        ThemePage.Xhtml => ThemeSamples.Xhtml,
        ThemePage.Css => ThemeSamples.Css,
        ThemePage.Diff => ThemeSamples.Diff,
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
    };

    private ThemePreviewEditor NewEditor(ThemeSample sample, ResolvedTheme theme)
    {
        ThemePreviewEditor editor = new();
        ApplyFont(editor);
        editor.Show(sample, theme);
        editor.KeyClicked += OnPreviewKeyClicked;
        _editors.Add(editor);
        return editor;
    }

    // The font of the code previews (Appearance › Color Scheme › preview font); an empty family keeps the editor's.
    private void ApplyFont(ThemePreviewEditor editor)
    {
        if (_bound?.PreviewFont is not { } font)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(font.FontFamily))
        {
            editor.FontFamily = new FontFamily(font.FontFamily);
        }

        if (font.FontSize > 0)
        {
            EditorZoom.SetBaseSize(editor, font.FontSize); // the zoom of Ctrl + wheel comes on top
        }

        editor.FontWeight = FontWeightOptions.ToAvalonia(font.FontWeight);
    }

    // A click in the preview selects the key under the mouse (a key of another page, e.g. the text of the editor on
    // the XHTML page, is ignored).
    private void OnPreviewKeyClicked(object? sender, ThemeKey key) => _bound?.SelectKey(key);

    // Expands the groups of the selected key in the tree (the view model's IsExpanded of a group does not reach the
    // rows of the source once they exist), so its row exists and can be scrolled to.
    private void ExpandToSelected()
    {
        if (_source is null || _bound?.Selected is not { } node || _selection?.FindPath(node) is not { } path)
        {
            return;
        }

        for (int depth = 1; depth < path.Count; depth++)
        {
            _source.Expand(new IndexPath(path.Take(depth)));
        }
    }

    // Brings the selected key into view — a link to a key (from another page, or the base of an inherited key) shows it
    // even when it is far down the tree. When the tree is not shown yet (the link's page or tab is being opened), it
    // waits for the tree's next layout.
    private void ScrollToSelected()
    {
        if (!Tree.IsEffectivelyVisible || TopLevel.GetTopLevel(Tree) is null || Tree.RowsPresenter is null)
        {
            if (!_scrollPending)
            {
                _scrollPending = true;
                Tree.LayoutUpdated += OnTreeLayoutUpdated;
            }

            return;
        }

        if (_source is not null && _bound?.Selected is { } node && _selection?.FindPath(node) is { } path)
        {
            int row = _source.Rows.ModelIndexToRowIndex(new IndexPath(path));
            if (row >= 0)
            {
                Tree.RowsPresenter.BringIntoView(row);
            }
        }
    }

    private void OnTreeLayoutUpdated(object? sender, EventArgs e)
    {
        if (Tree.IsEffectivelyVisible && TopLevel.GetTopLevel(Tree) is not null && Tree.RowsPresenter is not null)
        {
            Tree.LayoutUpdated -= OnTreeLayoutUpdated;
            _scrollPending = false;
            Dispatcher.UIThread.Post(ScrollToSelected, DispatcherPriority.Loaded);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeKeysPageViewModel.Selected))
        {
            ExpandToSelected();
            _selection?.SelectFromViewModel();
            Dispatcher.UIThread.Post(ScrollToSelected, DispatcherPriority.Loaded);
            ThemeKey? key = _bound?.Key;
            foreach (ThemePreviewEditor editor in _editors)
            {
                editor.Blink(key);
            }

            _controls?.Blink(key);
        }
        else if (e.PropertyName == nameof(ThemeKeysPageViewModel.PreviewFont))
        {
            foreach (ThemePreviewEditor editor in _editors)
            {
                ApplyFont(editor);
            }
        }
        else if (e.PropertyName == nameof(ThemeKeysPageViewModel.Theme) && _bound?.Theme is { } theme)
        {
            foreach (ThemePreviewEditor editor in _editors)
            {
                editor.SetTheme(theme);
            }

            _controls?.SetTheme(theme);
        }
    }
}
