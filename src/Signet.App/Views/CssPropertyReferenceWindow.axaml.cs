using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "TreeDataGrid sources are disposed when replaced and when the window closes.")]
public partial class CssPropertyReferenceWindow : Window
{
    private CssPropertyReferenceViewModel? _bound;
    private LocalizedColumns<CssPropertyReferenceRow>? _inheritedColumns;
    private LocalizedColumns<CssPropertyReferenceRow>? _nonInheritedColumns;
    private FlatTreeDataGridSource<CssPropertyReferenceRow>? _inheritedSource;
    private FlatTreeDataGridSource<CssPropertyReferenceRow>? _nonInheritedSource;

    /// <summary>Initializes the non-modal, read-only CSS reference window.</summary>
    public CssPropertyReferenceWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) =>
        {
            Unbind();
            InheritedList.Source = null;
            NonInheritedList.Source = null;
            DisposeSources();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => Bind();

    private void Bind()
    {
        Unbind();
        _bound = DataContext as CssPropertyReferenceViewModel;
        if (_bound is { } viewModel)
        {
            viewModel.PropertyChanged += OnPropertyChanged;
        }

        BuildInheritedList();
        BuildNonInheritedList();
    }

    private void Unbind()
    {
        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnPropertyChanged;
            _bound = null;
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CssPropertyReferenceViewModel.InheritedProperties))
        {
            BuildInheritedList();
        }
        else if (e.PropertyName == nameof(CssPropertyReferenceViewModel.NonInheritedProperties))
        {
            BuildNonInheritedList();
        }
    }

    private void BuildInheritedList()
    {
        FlatTreeDataGridSource<CssPropertyReferenceRow>? previous = _inheritedSource;
        _inheritedSource = null;
        _inheritedColumns = null;
        if (_bound is { } viewModel)
        {
            _inheritedColumns = new LocalizedColumns<CssPropertyReferenceRow>();
            _inheritedSource = CreateSource(viewModel.InheritedProperties, _inheritedColumns);
        }

        InheritedList.Source = _inheritedSource;
        previous?.Dispose();
    }

    private void BuildNonInheritedList()
    {
        FlatTreeDataGridSource<CssPropertyReferenceRow>? previous = _nonInheritedSource;
        _nonInheritedSource = null;
        _nonInheritedColumns = null;
        if (_bound is { } viewModel)
        {
            _nonInheritedColumns = new LocalizedColumns<CssPropertyReferenceRow>();
            _nonInheritedSource = CreateSource(viewModel.NonInheritedProperties, _nonInheritedColumns);
        }

        NonInheritedList.Source = _nonInheritedSource;
        previous?.Dispose();
    }

    private static FlatTreeDataGridSource<CssPropertyReferenceRow> CreateSource(
        IReadOnlyList<CssPropertyReferenceRow> rows,
        LocalizedColumns<CssPropertyReferenceRow> columns) =>
        new(rows)
        {
            Columns =
            {
                columns.Text("CssPropertyReferenceWindow_Display", row => row.Display, new GridLength(125)),
                columns.Template(
                    "CssPropertyReferenceWindow_Name",
                    "PropertyNameCellTemplate",
                    new GridLength(1, GridUnitType.Star)),
            },
        };

    private void DisposeSources()
    {
        _inheritedSource?.Dispose();
        _inheritedSource = null;
        _inheritedColumns = null;
        _nonInheritedSource?.Dispose();
        _nonInheritedSource = null;
        _nonInheritedColumns = null;
    }
}
