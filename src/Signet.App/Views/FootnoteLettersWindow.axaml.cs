using System;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// The modal "Unify Letters of Shared Footnotes" dialog (<see cref="FootnoteLettersViewModel"/>): the style, and the
/// shared notes whose letters change as a tree (files, notes, their references and the links to them) with check boxes.
/// "Unify" rewrites the checked notes and closes the dialog; a double click opens the place in the main window.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the tree is rebuilt and when the window closes, like in the other TreeDataGrid views.")]
public partial class FootnoteLettersWindow : Window
{
    private FootnoteLettersViewModel? _bound;
    private LocalizedColumns<FootnoteLetterNode>? _columns;
    private HierarchicalTreeDataGridSource<FootnoteLetterNode>? _source;

    /// <summary>Initializes the window.</summary>
    public FootnoteLettersWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        DataContextChanged += (_, _) => Bind();
        Closed += (_, _) =>
        {
            Unbind();
            Tree.Source = null;
            _source?.Dispose();
            _source = null;
        };
    }

    /// <summary>Shows the dialog over <paramref name="owner"/>.</summary>
    public static Task ShowAsync(Window owner, FootnoteLettersViewModel viewModel) =>
        new FootnoteLettersWindow { DataContext = viewModel }.ShowDialog(owner);

    private void Bind()
    {
        Unbind();
        _bound = DataContext as FootnoteLettersViewModel;
        if (_bound is not null)
        {
            _bound.Roots.CollectionChanged += OnRootsChanged;
            _bound.Done += OnDone;
        }

        BuildTree();
    }

    private void Unbind()
    {
        if (_bound is not null)
        {
            _bound.Roots.CollectionChanged -= OnRootsChanged;
            _bound.Done -= OnDone;
            _bound = null;
        }
    }

    private void OnDone(object? sender, EventArgs e) => Close();

    // A new style builds the tree again (the rows expand as they are created).
    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            BuildTree();
        }
    }

    // The source of the tree (TreeDataGrid sources belong to the views).
    private void BuildTree()
    {
        HierarchicalTreeDataGridSource<FootnoteLetterNode>? previous = _source;
        _source = null;
        if (_bound is { } vm)
        {
            _columns ??= new LocalizedColumns<FootnoteLetterNode>();
            _source = new HierarchicalTreeDataGridSource<FootnoteLetterNode>(vm.Roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("FootnoteLettersWindow_ColumnFootnote", "FootnoteLettersTitleCellTemplate", new GridLength(320)),
                        n => n.Children,
                        n => n.HasChildren,
                        n => n.IsExpanded),
                    _columns.Template("FootnoteLettersWindow_ColumnPlace", "FootnoteLettersPlaceCellTemplate", new GridLength(1, GridUnitType.Star)),
                },
            };
        }

        Tree.Source = _source;
        previous?.Dispose();
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FootnoteLetterNode node })
        {
            node.Open();
            e.Handled = true;
        }
    }
}
