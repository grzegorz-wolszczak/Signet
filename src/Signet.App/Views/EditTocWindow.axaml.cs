using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The modal "Edit Table Of Contents" dialog.
/// An editable table of contents tree with multi-selection, moving of contiguous ranges,
/// a context menu and the "Select Target" dialog.
/// </summary>
public partial class EditTocWindow : Window
{
    private EditTocViewModel? _bound;

    /// <summary>Initializes the window.</summary>
    public EditTocWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Tree.SelectionChanged += OnSelectionChanged;
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Shows the dialog modally; returns <c>true</c> when the user accepted and saving succeeded.</summary>
    public static async Task<bool> RunAsync(Window owner, EditTocViewModel viewModel)
    {
        var window = new EditTocWindow { DataContext = viewModel };
        await window.ShowDialog(owner);
        return viewModel.Accepted;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.CloseRequested -= OnCloseRequested;
            _bound.SelectTargetRequested -= OnSelectTargetRequested;
            _bound.RenameRequested -= OnRenameRequested;
            _bound.SelectionChangeRequested -= OnSelectionChangeRequested;
        }

        _bound = DataContext as EditTocViewModel;

        if (_bound is not null)
        {
            _bound.CloseRequested += OnCloseRequested;
            _bound.SelectTargetRequested += OnSelectTargetRequested;
            _bound.RenameRequested += OnRenameRequested;
            _bound.SelectionChangeRequested += OnSelectionChangeRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        IEnumerable<EditTocNodeViewModel> selected =
            Tree.SelectedItems?.OfType<EditTocNodeViewModel>() ?? Enumerable.Empty<EditTocNodeViewModel>();
        _bound?.SetSelectedNodes(selected);
    }

    private void OnSelectionChangeRequested(object? sender, IReadOnlyList<EditTocNodeViewModel> nodes)
    {
        if (Tree.SelectedItems is null)
        {
            return;
        }

        Tree.SelectedItems.Clear();
        foreach (EditTocNodeViewModel node in nodes)
        {
            Tree.SelectedItems.Add(node);
        }
    }

    private async void OnSelectTargetRequested(object? sender, EventArgs e)
    {
        if (_bound is null || _bound.SelectedNode is null)
        {
            return;
        }

        string? href = await SelectHyperlinkWindow.AskAsync(
            this, _bound.GetTargetItems(), _bound.CurrentTargetHref());

        if (href is not null)
        {
            _bound.ApplyTarget(href);
        }
    }

    private void OnRenameRequested(object? sender, EventArgs e)
    {
        if (Tree.SelectedItem is not EditTocNodeViewModel node)
        {
            return;
        }

        TreeViewItem? container = Tree.GetVisualDescendants()
            .OfType<TreeViewItem>()
            .FirstOrDefault(item => ReferenceEquals(item.DataContext, node));

        TextBox? editor = container?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (editor is not null)
        {
            editor.Focus();
            editor.SelectAll();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_bound is null)
        {
            return;
        }

        bool editingText = e.Source is TextBox;

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Up)
        {
            Execute(_bound.MoveUpCommand);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Down)
        {
            Execute(_bound.MoveDownCommand);
            e.Handled = true;
        }
        else if (!editingText && e.Key == Key.Left)
        {
            Execute(_bound.MoveLeftCommand);
            e.Handled = true;
        }
        else if (!editingText && e.Key == Key.Right)
        {
            Execute(_bound.MoveRightCommand);
            e.Handled = true;
        }
    }

    private static void Execute(CommunityToolkit.Mvvm.Input.RelayCommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
