using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>
/// The modal "Preferences" window, modelled on Rider's Settings (see <see cref="PreferencesViewModel"/>):
/// <list type="bullet">
/// <item>the sections as a tree (top-level ones in bold) with a search box — the search keeps the sections whose name
/// or page text contains it and frames the matching controls of the shown page;</item>
/// <item>the path of the section above its page;</item>
/// <item>Save writes the changes and keeps the window open (enabled only when something changed); Cancel closes it
/// without them; Escape and the close button ask what to do with unsaved changes.</item>
/// </list>
/// The pages are the tabs of a TabControl whose tab strip is hidden; the tree selects them.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the window closes, like in the other TreeDataGrid views.")]
public partial class PreferencesWindow : Window
{
    /// <summary>The height of a resizable multi-line field until the user drags its grip (about six lines).</summary>
    public const double DefaultFieldHeight = 124;

    private readonly PreferencesSectionsViewModel _sections;
    private readonly HierarchicalTreeDataGridSource<PreferencesSectionNode> _source;
    private readonly TreeSelectionSync<PreferencesSectionNode> _selection;
    private readonly List<Control> _framed = new();
    private bool _closeConfirmed;
    private bool _askingAboutChanges;

    /// <summary>Initializes the window.</summary>
    public PreferencesWindow()
    {
        InitializeComponent();

        // Every page is a top-level section for now; subsections are nodes with a parent.
        _sections = new PreferencesSectionsViewModel(
            Pages.Items.OfType<TabItem>().Select((tab, index) => new PreferencesSectionNode(tab.Header as string ?? string.Empty, index)),
            PageTexts);
        LocalizedColumns<PreferencesSectionNode> columns = new();
        VisibleItemsView<PreferencesSectionNode> roots = Visible(_sections.Roots);
        _source = new HierarchicalTreeDataGridSource<PreferencesSectionNode>(roots)
        {
            Columns =
            {
                columns.Expander(
                    columns.Template("PreferencesWindow_Title", "PreferencesSectionCellTemplate", new GridLength(1, GridUnitType.Star)),
                    n => Visible(n.Children),
                    n => n.HasChildren,
                    n => n.IsExpanded),
            },
        };
        _selection = new TreeSelectionSync<PreferencesSectionNode>(
            _source, roots, n => Visible(n.Children), () => _sections.Selected, n => _sections.Selected = n);
        SectionTree.Source = _source;

        _sections.PropertyChanged += OnSectionsPropertyChanged;
        Pages.SelectionChanged += (_, _) =>
        {
            if (_sections.FindByPage(Pages.SelectedIndex) is { } node)
            {
                _sections.Selected = node;
            }
        };
        SectionSearch.TextChanged += (_, _) =>
        {
            _sections.Query = SectionSearch.Text ?? string.Empty;
            QueueFrameRefresh();
        };
        Closed += (_, _) =>
        {
            _sections.PropertyChanged -= OnSectionsPropertyChanged;
            _source.Dispose();
        };

        ShowSelectedSection();
    }

    /// <summary>The section tree and its search (for tests and the main window).</summary>
    public PreferencesSectionsViewModel Sections => _sections;

    /// <summary>The index of the selected page (the main window remembers it between openings).</summary>
    public int SelectedPageIndex
    {
        get => Pages.SelectedIndex;
        set => _sections.Selected = _sections.FindByPage(value >= 0 && value < Pages.ItemCount ? value : 0) ?? _sections.Selected;
    }

    /// <summary>
    /// The heights the user gave the resizable fields (field name -&gt; height); the main window remembers them between
    /// openings, like the window size.
    /// </summary>
    public IReadOnlyDictionary<string, int> FieldHeights
    {
        get => ResizeGrips()
            .Where(g => g.Target is { Name.Length: > 0 } target && !double.IsNaN(target.Height))
            .ToDictionary(g => g.Target!.Name!, g => (int)Math.Round(g.Target!.Height), StringComparer.Ordinal);

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (HeightResizeGrip grip in ResizeGrips())
            {
                if (grip.Target is { Name: { } name } target && value.TryGetValue(name, out int height) && height > 0)
                {
                    target.Height = Math.Max(grip.MinTargetHeight, height);
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PreferencesViewModel vm)
        {
            // The "Choose…" buttons next to the fonts open a modal picker window over this window.
            vm.FontPicker = request => FontPickerWindow.AskAsync(this, request);
            vm.ColorPicker = (title, initial) => ColorPickerWindow.AskAsync(this, title, initial);
            vm.TextPrompt = (title, prompt, initial) => TextPromptWindow.AskAsync(this, title, prompt, initial);
            vm.ErrorMessage = (title, message) => MessageDialog.ShowAsync(this, title, message);
        }
    }

    /// <summary>Escape closes the window (asking about unsaved changes) unless a control used it.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>
    /// With unsaved changes, closing by the user (the close button, Escape) asks first: save and close, discard and
    /// close, or go back. Closing with the owner or the application does not ask.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed
            || DataContext is not PreferencesViewModel { HasChanges: true }
            || e.CloseReason is not (WindowCloseReason.WindowClosing or WindowCloseReason.Undefined))
        {
            return;
        }

        e.Cancel = true;
        if (!_askingAboutChanges)
        {
            _ = AskAboutUnsavedChangesAsync();
        }
    }

    private async Task AskAboutUnsavedChangesAsync()
    {
        _askingAboutChanges = true;
        try
        {
            int answer = await ChoiceWindow.AskAsync(
                this,
                Strings.Get("PreferencesWindow_UnsavedTitle"),
                Strings.Get("PreferencesWindow_UnsavedMessage"),
                Strings.Get("PreferencesWindow_UnsavedSave"),
                Strings.Get("PreferencesWindow_UnsavedDiscard"),
                Strings.Get("PreferencesWindow_UnsavedBack"));
            if (answer is 0 or 1)
            {
                if (answer == 0 && DataContext is PreferencesViewModel vm && vm.ApplyCommand.CanExecute(null))
                {
                    vm.ApplyCommand.Execute(null);
                }

                _closeConfirmed = true;
                Close();
            }
        }
        finally
        {
            _askingAboutChanges = false;
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        _closeConfirmed = true;
        Close();
    }

    private void OnClearSectionSearchClicked(object? sender, RoutedEventArgs e)
    {
        SectionSearch.Text = string.Empty;
        SectionSearch.Focus();
    }

    private void OnSectionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreferencesSectionsViewModel.Selected))
        {
            ShowSelectedSection();
        }
    }

    // The selected section: its page, its path above the page, its row in the tree and the search frames on the page.
    private void ShowSelectedSection()
    {
        if (_sections.Selected is { } node && Pages.SelectedIndex != node.PageIndex)
        {
            Pages.SelectedIndex = node.PageIndex;
        }

        Breadcrumb.Text = _sections.Breadcrumb;
        _selection.SelectFromViewModel();
        QueueFrameRefresh();
    }

    // After the layout pass: the page's controls exist and can be framed.
    private void QueueFrameRefresh() => Dispatcher.UIThread.Post(RefreshFrames, DispatcherPriority.Loaded);

    // Frames the controls of the shown page whose own text contains the search text (Rider's search highlight), and
    // brings the first one into view. A control inside a framed one is not framed again.
    private void RefreshFrames()
    {
        foreach (Control control in _framed)
        {
            AdornerLayer.SetAdorner(control, null);
        }

        _framed.Clear();
        if (!_sections.IsSearching || Pages.SelectedItem is not TabItem { Content: ILogical page })
        {
            return;
        }

        foreach (ILogical element in SelfAndLogicalDescendants(page))
        {
            if (element is Control { IsEffectivelyVisible: true } control
                && _sections.Matches(OwnText(element))
                && !_framed.Any(framed => control.GetLogicalAncestors().Contains(framed)))
            {
                AdornerLayer.SetAdorner(control, SearchHitFrame());
                _framed.Add(control);
            }
        }

        _framed.FirstOrDefault()?.BringIntoView();
    }

    // The frame around a control matching the search: in the accent color, a little outside the control. The adorner
    // layer clips an adorner to the control's bounds by default, which cut the whole frame away: clipping is off.
    private static Border SearchHitFrame()
    {
        Border frame = new()
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(-4, -2),
            IsHitTestVisible = false,
        };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("SystemControlHighlightAccentBrush"));
        AdornerLayer.SetIsClipEnabled(frame, false);
        return frame;
    }

    // The texts of a page the search looks in: labels, check boxes, radio and other buttons, headers of inner tabs.
    private IEnumerable<string> PageTexts(int index) =>
        index >= 0 && index < Pages.ItemCount && Pages.Items[index] is TabItem { Content: ILogical page }
            ? SelfAndLogicalDescendants(page).Select(OwnText).OfType<string>()
            : Enumerable.Empty<string>();

    private static IEnumerable<ILogical> SelfAndLogicalDescendants(ILogical root) => new[] { root }.Concat(root.GetLogicalDescendants());

    // The grips of the resizable fields of every page (the pages not shown are in the logical tree too).
    private IEnumerable<HeightResizeGrip> ResizeGrips() =>
        Pages.Items.OfType<TabItem>()
            .Select(t => t.Content)
            .OfType<ILogical>()
            .SelectMany(SelfAndLogicalDescendants)
            .OfType<HeightResizeGrip>();

    private static string? OwnText(ILogical element) => element switch
    {
        TextBlock text => text.Text,
        HeaderedContentControl { Header: string header } => header,
        ContentControl { Content: string content } => content,
        _ => null,
    };

    private static VisibleItemsView<PreferencesSectionNode> Visible(System.Collections.ObjectModel.ObservableCollection<PreferencesSectionNode> nodes) =>
        VisibleItems.For(nodes, n => n.IsVisible, nameof(PreferencesSectionNode.IsVisible));

    /// <summary>"Open settings folder".</summary>
    private async void OnOpenPreferencesLocationClicked(object? sender, RoutedEventArgs e)
    {
        await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(AppDirectories.EnsurePrefsDirectory()));
    }

    private void OnUserWordSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm && sender is ListBox list)
        {
            vm.SetSelectedUserWords(list.SelectedItems?.OfType<string>() ?? Enumerable.Empty<string>());
        }
    }

    private void OnUserWordDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm && vm.EditUserWordCommand.CanExecute(null))
        {
            vm.EditUserWordCommand.Execute(null);
        }
    }
}
