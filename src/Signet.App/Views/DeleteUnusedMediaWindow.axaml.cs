using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.Core.Resources;

namespace Signet.App.Views;

/// <summary>
/// The modal "Delete Unused Media Files" dialog: a list of unused media resources with
/// a checkbox (all checked by default), a "select/unselect all" toggle and a double
/// click that opens the file.
/// </summary>
public partial class DeleteUnusedMediaWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public DeleteUnusedMediaWindow()
    {
        InitializeComponent();
        DataContext = this;

        ToggleSelectAllCheckBox.Click += (_, _) => SelectUnselectAll(ToggleSelectAllCheckBox.IsChecked == true);
        RowsList.DoubleTapped += OnRowsListDoubleTapped;
        OkButton.Click += (_, _) => Close(CheckedResources());
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>List rows (resource book path + check state).</summary>
    public ObservableCollection<DeleteUnusedMediaRow> Rows { get; } = new();

    /// <summary>
    /// Shows the dialog; returns the checked resources to delete, or <c>null</c> after cancelling.
    /// <paramref name="openFile"/> is called after a double click on a row.
    /// </summary>
    public static async Task<IReadOnlyList<Resource>?> AskAsync(
        Window owner, IReadOnlyList<Resource> candidates, Action<string> openFile)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(openFile);

        DeleteUnusedMediaWindow window = new();
        foreach (Resource resource in candidates)
        {
            window.Rows.Add(new DeleteUnusedMediaRow(resource));
        }

        window._openFile = openFile;
        return await window.ShowDialog<IReadOnlyList<Resource>?>(owner);
    }

    private Action<string>? _openFile;

    private void OnRowsListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RowsList.SelectedItem is DeleteUnusedMediaRow row)
        {
            _openFile?.Invoke(row.BookPath);
        }
    }

    private void SelectUnselectAll(bool value)
    {
        foreach (DeleteUnusedMediaRow row in Rows)
        {
            row.IsChecked = value;
        }
    }

    private List<Resource> CheckedResources() =>
        Rows.Where(row => row.IsChecked).Select(row => row.Resource).ToList();
}

/// <summary>A row of the "Delete Unused Media Files" window.</summary>
public partial class DeleteUnusedMediaRow : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked = true;

    /// <summary>Creates a row for the given resource.</summary>
    public DeleteUnusedMediaRow(Resource resource) => Resource = resource;

    /// <summary>The media resource the row refers to.</summary>
    public Resource Resource { get; }

    /// <summary>Book path of the resource (shown in the list).</summary>
    public string BookPath => Resource.BookPath;
}
