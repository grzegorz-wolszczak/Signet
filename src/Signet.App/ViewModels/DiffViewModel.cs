using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Diff;

namespace Signet.App.ViewModels;

/// <summary>What the right part of the diff window shows for the selected file.</summary>
public enum DiffContentMode
{
    /// <summary>No file selected or no differences — only a message.</summary>
    Message,

    /// <summary>Text diff.</summary>
    Text,

    /// <summary>Side-by-side image previews.</summary>
    Image,
}

/// <summary>
/// View model of the "Compare" window: the differences between the checkpoint state
/// (left side) and the current one (right side), side-by-side or single-column view, number of
/// context lines, navigation between changes, searching in the left/right pane and a double click
/// that opens the location in the editor.
/// </summary>
/// <remarks>
/// The files are listed on the left, and the diff concerns the selected file.
/// </remarks>
public sealed partial class DiffViewModel : ViewModelBase
{
    /// <summary>Context line count variants; <c>null</c> — the whole text.</summary>
    private static readonly int?[] ContextValues = { 3, 5, 10, 50, null };

    private TextDiff? _textDiff;
    private DiffFileItem? _selectedFile;
    private int _contextIndex;
    private bool _isUnified;
    private DiffContentMode _contentMode = DiffContentMode.Message;
    private string _message = string.Empty;
    private int _selectedRowIndex = -1;
    private string _searchText = string.Empty;
    private bool _searchInRight = true;
    private string _status = string.Empty;

    /// <summary>Creates the model for the list of differences between states.</summary>
    /// <param name="files">Changed files (from <see cref="BookComparer.Compare"/>).</param>
    /// <param name="leftTitle">Name of the left state (the checkpoint).</param>
    /// <param name="rightTitle">Name of the right (current) state.</param>
    public DiffViewModel(IReadOnlyList<BookFileDiff> files, string leftTitle, string rightTitle)
    {
        ArgumentNullException.ThrowIfNull(files);
        LeftTitle = leftTitle ?? throw new ArgumentNullException(nameof(leftTitle));
        RightTitle = rightTitle ?? throw new ArgumentNullException(nameof(rightTitle));
        Files = new ObservableCollection<DiffFileItem>(files.Select(f => new DiffFileItem(f)));
        ContextOptions = ContextValues
            .Select(v => v is { } n ? Strings.Format("Diff_ContextLines", n) : Strings.Get("Diff_ContextAll"))
            .ToList();
        ViewOptions = new[] { Strings.Get("Diff_ViewSideBySide"), Strings.Get("Diff_ViewUnified") };

        _message = Files.Count == 0 ? Strings.Get("Diff_NoChanges") : string.Empty;
        SelectedFile = Files.FirstOrDefault();
    }

    /// <summary>
    /// Request to open a file of the current book in the editor at a line (book path, 1-based line) —
    /// a double click in the right pane.
    /// </summary>
    public event EventHandler<(string BookPath, int Line)>? OpenInEditorRequested;

    /// <summary>Name of the left state.</summary>
    public string LeftTitle { get; }

    /// <summary>Name of the right state.</summary>
    public string RightTitle { get; }

    /// <summary>Window title.</summary>
    public string WindowTitle => Strings.Format("Diff_WindowTitle", LeftTitle, RightTitle);

    /// <summary>Changed files.</summary>
    public ObservableCollection<DiffFileItem> Files { get; }

    /// <summary>Labels of the context variants (for the drop-down list).</summary>
    public IReadOnlyList<string> ContextOptions { get; }

    /// <summary>View labels: side by side / single column.</summary>
    public IReadOnlyList<string> ViewOptions { get; }

    /// <summary>Side-by-side view rows for the selected file.</summary>
    public ObservableCollection<SideBySideRowItem> SideBySideRows { get; } = new();

    /// <summary>Single-column view rows for the selected file.</summary>
    public ObservableCollection<UnifiedRowItem> UnifiedRows { get; } = new();

    /// <summary>Selected file.</summary>
    public DiffFileItem? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
            {
                LoadSelectedFile();
            }
        }
    }

    /// <summary>Index of the selected context variant (3 lines by default).</summary>
    public int ContextIndex
    {
        get => _contextIndex;
        set
        {
            int clamped = Math.Clamp(value, 0, ContextValues.Length - 1);
            if (SetProperty(ref _contextIndex, clamped))
            {
                RebuildRows();
            }
        }
    }

    /// <summary>View index: 0 — side by side, 1 — single column.</summary>
    public int ViewIndex
    {
        get => _isUnified ? 1 : 0;
        set => IsUnified = value == 1;
    }

    /// <summary>Whether the view is single-column (unified).</summary>
    public bool IsUnified
    {
        get => _isUnified;
        set
        {
            if (SetProperty(ref _isUnified, value))
            {
                OnPropertyChanged(nameof(ViewIndex));
                OnPropertyChanged(nameof(IsSideBySideTextVisible));
                OnPropertyChanged(nameof(IsUnifiedTextVisible));
                RebuildRows();
            }
        }
    }

    /// <summary>Mode of the right part of the window.</summary>
    public DiffContentMode ContentMode
    {
        get => _contentMode;
        private set
        {
            if (SetProperty(ref _contentMode, value))
            {
                OnPropertyChanged(nameof(IsMessageVisible));
                OnPropertyChanged(nameof(IsImageVisible));
                OnPropertyChanged(nameof(IsSideBySideTextVisible));
                OnPropertyChanged(nameof(IsUnifiedTextVisible));
            }
        }
    }

    /// <summary>Whether a message is shown instead of a diff.</summary>
    public bool IsMessageVisible => ContentMode == DiffContentMode.Message;

    /// <summary>Whether images are shown.</summary>
    public bool IsImageVisible => ContentMode == DiffContentMode.Image;

    /// <summary>Whether the side-by-side text diff is shown.</summary>
    public bool IsSideBySideTextVisible => ContentMode == DiffContentMode.Text && !IsUnified;

    /// <summary>Whether the single-column text diff is shown.</summary>
    public bool IsUnifiedTextVisible => ContentMode == DiffContentMode.Text && IsUnified;

    /// <summary>Message (no differences, binary file, rename…).</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>Full path of the left image, or <c>null</c>.</summary>
    public string? LeftImagePath => ContentMode == DiffContentMode.Image ? SelectedFile?.Diff.LeftFullPath : null;

    /// <summary>Full path of the right image, or <c>null</c>.</summary>
    public string? RightImagePath => ContentMode == DiffContentMode.Image ? SelectedFile?.Diff.RightFullPath : null;

    /// <summary>Index of the selected row in the current view (-1 — none); the view scrolls to it.</summary>
    public int SelectedRowIndex
    {
        get => _selectedRowIndex;
        set => SetProperty(ref _selectedRowIndex, value);
    }

    /// <summary>Search text (case-insensitive).</summary>
    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value ?? string.Empty);
    }

    /// <summary>Whether to search in the right pane (otherwise in the left one).</summary>
    public bool SearchInRight
    {
        get => _searchInRight;
        set
        {
            if (SetProperty(ref _searchInRight, value))
            {
                OnPropertyChanged(nameof(SearchInLeft));
            }
        }
    }

    /// <summary>Whether to search in the left pane.</summary>
    public bool SearchInLeft
    {
        get => !_searchInRight;
        set => SearchInRight = !value;
    }

    /// <summary>Status bar message of the window (e.g. "Not found").</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>
    /// Double click on a row: when the row has a line on the right side (the current state), raises
    /// <see cref="OpenInEditorRequested"/>.
    /// </summary>
    public void ActivateRow(int index)
    {
        if (SelectedFile?.Diff is not { RightPath: { } bookPath } || index < 0)
        {
            return;
        }

        int? line = IsUnified
            ? (index < UnifiedRows.Count ? UnifiedRows[index].Row.RightLineNumber : null)
            : (index < SideBySideRows.Count ? SideBySideRows[index].Row.Right.LineNumber : null);
        if (line is { } l)
        {
            OpenInEditorRequested?.Invoke(this, (bookPath, l));
        }
    }

    [RelayCommand]
    private void NextChange() => GoToChange(forward: true);

    [RelayCommand]
    private void PreviousChange() => GoToChange(forward: false);

    [RelayCommand]
    private void FindNext() => Find(forward: true);

    [RelayCommand]
    private void FindPrevious() => Find(forward: false);

    // Starts of change blocks: a changed row that is not preceded by another change.
    private List<int> ChangeStarts()
    {
        List<bool> isChange = IsUnified
            ? UnifiedRows.Select(r => r.Row.IsChange).ToList()
            : SideBySideRows.Select(r => r.Row.IsChange).ToList();
        List<int> starts = new();
        for (int i = 0; i < isChange.Count; i++)
        {
            if (isChange[i] && (i == 0 || !isChange[i - 1]))
            {
                starts.Add(i);
            }
        }

        return starts;
    }

    private void GoToChange(bool forward)
    {
        List<int> starts = ChangeStarts();
        int? target = forward
            ? starts.Where(i => i > SelectedRowIndex).Cast<int?>().FirstOrDefault()
            : starts.Where(i => i < SelectedRowIndex).Cast<int?>().LastOrDefault();
        if (target is { } t)
        {
            SelectedRowIndex = t;
            Status = string.Empty;
        }
        else
        {
            Status = Strings.Get(forward ? "Diff_NoNextChange" : "Diff_NoPreviousChange");
        }
    }

    private void Find(bool forward)
    {
        if (SearchText.Length == 0)
        {
            return;
        }

        List<string?> texts = IsUnified
            ? UnifiedRows.Select(r => (SearchInRight ? r.Row.RightLineNumber : r.Row.LeftLineNumber) is null ? null : r.Row.Text).ToList()
            : SideBySideRows.Select(r => (SearchInRight ? r.Row.Right : r.Row.Left).Text).Cast<string?>().ToList();

        int count = texts.Count;
        for (int step = 1; step <= count; step++)
        {
            int index = forward
                ? (SelectedRowIndex + step + count) % count
                : (SelectedRowIndex - step + (2 * count)) % count;
            if (texts[index] is { } text && text.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase))
            {
                SelectedRowIndex = index;
                Status = string.Empty;
                return;
            }
        }

        Status = Strings.Get("Diff_NotFound");
    }

    private void LoadSelectedFile()
    {
        _textDiff = null;
        SideBySideRows.Clear();
        UnifiedRows.Clear();
        SelectedRowIndex = -1;
        Status = string.Empty;

        if (SelectedFile?.Diff is not { } diff)
        {
            Message = Files.Count == 0 ? Strings.Get("Diff_NoChanges") : string.Empty;
            ContentMode = DiffContentMode.Message;
            NotifyImages();
            return;
        }

        if (diff.Change == BookFileChange.Renamed)
        {
            Message = Strings.Format("Diff_RenamedMessage", diff.LeftPath, diff.RightPath);
            ContentMode = DiffContentMode.Message;
        }
        else if (diff.ContentKind == BookFileContentKind.Image)
        {
            ContentMode = DiffContentMode.Image;
        }
        else if (diff.ContentKind == BookFileContentKind.Binary)
        {
            Message = Strings.Format(
                diff.Change switch
                {
                    BookFileChange.Added => "Diff_BinaryAdded",
                    BookFileChange.Removed => "Diff_BinaryRemoved",
                    _ => "Diff_BinaryChanged",
                },
                diff.DisplayPath);
            ContentMode = DiffContentMode.Message;
        }
        else
        {
            try
            {
                _textDiff = TextDiff.Compute(diff.ReadLeftText(), diff.ReadRightText());
                ContentMode = DiffContentMode.Text;
                RebuildRows();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Message = ex.Message;
                ContentMode = DiffContentMode.Message;
            }
        }

        NotifyImages();
    }

    private void NotifyImages()
    {
        OnPropertyChanged(nameof(LeftImagePath));
        OnPropertyChanged(nameof(RightImagePath));
    }

    private void RebuildRows()
    {
        SideBySideRows.Clear();
        UnifiedRows.Clear();
        SelectedRowIndex = -1;
        if (_textDiff is null)
        {
            return;
        }

        int? context = ContextValues[_contextIndex];
        if (IsUnified)
        {
            foreach (UnifiedDiffRow row in _textDiff.Unified(context))
            {
                UnifiedRows.Add(new UnifiedRowItem(row));
            }
        }
        else
        {
            foreach (SideBySideDiffRow row in _textDiff.SideBySide(context))
            {
                SideBySideRows.Add(new SideBySideRowItem(row));
            }
        }

        GoToFirstChange();
    }

    private void GoToFirstChange()
    {
        List<int> starts = ChangeStarts();
        if (starts.Count > 0)
        {
            SelectedRowIndex = starts[0];
        }
    }
}

/// <summary>Item of the changed files list.</summary>
/// <param name="Diff">The file's difference.</param>
public sealed record DiffFileItem(BookFileDiff Diff)
{
    /// <summary>Label: kind of change and path.</summary>
    public string Label => Strings.Format(
        "Diff_FileLabel",
        Strings.Get(Diff.Change switch
        {
            BookFileChange.Added => "Diff_Added",
            BookFileChange.Removed => "Diff_Removed",
            BookFileChange.Renamed => "Diff_Renamed",
            _ => "Diff_Modified",
        }),
        Diff.DisplayPath);
}

/// <summary>Side-by-side view row with the texts of line numbers and of the collapsed block.</summary>
/// <param name="Row">Diff row.</param>
public sealed record SideBySideRowItem(SideBySideDiffRow Row)
{
    /// <summary>Line number on the left (empty for a padding cell).</summary>
    public string LeftNumber => Row.Left.LineNumber?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>Line number on the right.</summary>
    public string RightNumber => Row.Right.LineNumber?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>Whether the row is a collapsed block of unchanged lines.</summary>
    public bool IsCollapsed => Row.CollapsedLines is not null;

    /// <summary>Whether the row is a regular line (not a collapsed block).</summary>
    public bool IsLine => Row.CollapsedLines is null;

    /// <summary>Text of the collapsed block.</summary>
    public string CollapsedText => Row.CollapsedLines is { } n ? Strings.Format("Diff_Collapsed", n) : string.Empty;
}

/// <summary>Single-column view row.</summary>
/// <param name="Row">Diff row.</param>
public sealed record UnifiedRowItem(UnifiedDiffRow Row)
{
    /// <summary>Line number on the left.</summary>
    public string LeftNumber => Row.LeftLineNumber?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>Line number on the right.</summary>
    public string RightNumber => Row.RightLineNumber?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The "-" / "+" / space marker.</summary>
    public string Marker => Row.Kind switch
    {
        DiffLineKind.Deleted => "-",
        DiffLineKind.Inserted => "+",
        _ => " ",
    };

    /// <summary>Whether the row is a collapsed block of unchanged lines.</summary>
    public bool IsCollapsed => Row.CollapsedLines is not null;

    /// <summary>Whether the row is a regular line.</summary>
    public bool IsLine => Row.CollapsedLines is null;

    /// <summary>Text of the collapsed block.</summary>
    public string CollapsedText => Row.CollapsedLines is { } n ? Strings.Format("Diff_Collapsed", n) : string.Empty;
}
