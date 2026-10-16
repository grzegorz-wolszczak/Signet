using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.MainUI;

namespace Signet.App.Views;

/// <summary>
/// The "Mass Rename" window (as calibre's): a prefix, the number to start from, the character and the minimum length the
/// number is padded to, and whether to number the files in the order of the book. A live preview shows the new names; the
/// window only edits the options, the view model plans and carries out the rename (<see cref="BookBrowserViewModel.PlanMassRename"/>).
/// </summary>
public partial class MassRenameWindow : Window
{
    private BookBrowserViewModel? _viewModel;
    private IReadOnlyList<OpfModelEntry> _entries = Array.Empty<OpfModelEntry>();
    private bool _valid;

    /// <summary>Initializes the window.</summary>
    public MassRenameWindow()
    {
        InitializeComponent();
        PrefixBox.TextChanged += (_, _) => UpdatePreview();
        PaddingCharBox.TextChanged += (_, _) => UpdatePreview();
        StartNumberBox.ValueChanged += (_, _) => UpdatePreview();
        MinLengthBox.ValueChanged += (_, _) => UpdatePreview();
        BookOrderBox.IsCheckedChanged += (_, _) => UpdatePreview();
        OkButton.Click += (_, _) => Close(_valid ? (MassRenameOptions?)Options : null);
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>The options as the fields show them.</summary>
    public MassRenameOptions Options => new(
        PrefixBox.Text ?? string.Empty,
        (int)Math.Clamp(StartNumberBox.Value ?? 0, 0, MassRenameOptions.StartNumberMax),
        PaddingCharBox.Text is { Length: > 0 } padding ? padding[0] : '\0',
        (int)Math.Clamp(MinLengthBox.Value ?? 1, 1, MassRenameOptions.MinLengthMax),
        BookOrderBox.IsChecked == true);

    /// <summary>Shows the window for the entries; returns the options once accepted (the preview was valid), otherwise <c>null</c>.</summary>
    public static async Task<MassRenameOptions?> AskAsync(Window owner, BookBrowserViewModel viewModel, IReadOnlyList<OpfModelEntry> entries) =>
        await Create(viewModel, entries).ShowDialog<MassRenameOptions?>(owner);

    /// <summary>The window for the entries, filled with the values used last time and showing the preview (not shown yet).</summary>
    public static MassRenameWindow Create(BookBrowserViewModel viewModel, IReadOnlyList<OpfModelEntry> entries)
    {
        MassRenameWindow window = new() { _viewModel = viewModel, _entries = entries };
        MassRenameOptions initial = viewModel.GetInitialMassRename();
        window.DescriptionText.Text = Strings.Format("MassRenameWindow_Description", entries.Count);
        window.PrefixBox.Text = initial.Prefix;
        window.StartNumberBox.Value = initial.StartNumber;
        window.PaddingCharBox.Text = initial.PaddingChar == '\0' ? string.Empty : initial.PaddingChar.ToString();
        window.MinLengthBox.Value = initial.MinLength;
        bool canUseBookOrder = BookBrowserViewModel.CanUseBookOrder(entries);
        window.BookOrderBox.IsEnabled = canUseBookOrder;
        window.BookOrderBox.IsChecked = canUseBookOrder && initial.BookOrder;
        window.UpdatePreview();
        return window;
    }

    private void UpdatePreview()
    {
        if (_viewModel is null)
        {
            return;
        }

        MassRenamePlan plan = _viewModel.PlanMassRename(_entries, Options);
        _valid = plan.NewFilenames is not null;
        OkButton.IsEnabled = _valid;
        ErrorText.Text = plan.Error;
        ErrorText.IsVisible = plan.Error is { Length: > 0 };
        if (plan.NewFilenames is null)
        {
            PreviewList.ItemsSource = null;
            ExampleText.Text = string.Empty;
            return;
        }

        ExampleText.Text = Strings.Format("MassRenameWindow_Example", plan.NewFilenames.FirstOrDefault() ?? string.Empty);
        PreviewList.ItemsSource = plan.Resources.Select((r, i) => $"{r.Filename}  →  {plan.NewFilenames[i]}").ToList();
    }
}
