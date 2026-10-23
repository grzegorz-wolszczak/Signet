using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Footnotes;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>A file the notes that become global can go to (the "Notes file" box of the dialog).</summary>
/// <param name="File">The file; <c>null</c> = a new <see cref="FootnoteEditor.GlobalFileName"/>.</param>
/// <param name="Display">What the box shows.</param>
public sealed record GlobalFileChoice(HtmlResource? File, string Display);

/// <summary>
/// A row of the list of the "Change Footnote Placement" dialog: a footnote of Signet with its marker, whether it is local or
/// global now, the start of its text and its file; the check box marks it to move to the other placement. A footnote that
/// cannot move (<see cref="PlacementItem.Blocker"/>) is shown grayed out, with the reason.
/// </summary>
public sealed partial class FootnotePlacementRow : ObservableObject
{
    // How much of the note the row shows.
    private const int TextLength = 120;

    private readonly FootnotePlacementViewModel _owner;
    private string? _text;

    internal FootnotePlacementRow(FootnotePlacementViewModel owner, PlacementItem item)
    {
        _owner = owner;
        Item = item;
        Kind = Strings.Get(item.Placement == FootnotePlacement.Local ? "FootnotePlacement_Local" : "FootnotePlacement_Global");
        List<HtmlResource> files = item.References.Select(r => r.ReferenceFile).Distinct().ToList();
        File = files.Count > 1 ? Strings.Format("FootnotePlacementWindow_FileMore", files[0].Filename, files.Count - 1) : files[0].Filename;
        Remark = item.Blocker == FootnoteError.None ? string.Empty : Strings.Get("FootnotePlacementWindow_BlockedSeveralFiles");
    }

    /// <summary>The footnote.</summary>
    public PlacementItem Item { get; }

    /// <summary>The marker of the reference, <c>[4]</c> (<c>[4a], [4b]</c> for a shared note).</summary>
    public string Marker => Item.Marker;

    /// <summary>"local" or "global": what it is now.</summary>
    public string Kind { get; }

    /// <summary>The file with the reference (the first of them, and how many more).</summary>
    public string File { get; }

    /// <summary>Why it cannot move; empty when it can.</summary>
    public string Remark { get; }

    /// <summary>Whether it can move (its check box can be checked).</summary>
    public bool IsEnabled => Item.CanMove;

    /// <summary>How visible the row is: dimmed when it cannot move.</summary>
    public double Fade => IsEnabled ? 1 : 0.5;

    /// <summary>The start of the text of the note (read when the row is first shown: the files are parsed for it).</summary>
    public string Text => _text ??= ReadText();

    /// <summary>Whether the footnote moves to the other placement.</summary>
    [ObservableProperty]
    private bool _isChecked;

    /// <summary>A double click: opens the reference in the code.</summary>
    internal void Open() => _owner.Navigate(Item.Footnote);

    partial void OnIsCheckedChanged(bool value)
    {
        if (value && !IsEnabled)
        {
            IsChecked = false;
            return;
        }

        _owner.OnRowToggled();
    }

    private string ReadText()
    {
        string text = FootnoteEditor.GetContent(Item.Footnote)?.Text.Replace("\n\n", " ", StringComparison.Ordinal) ?? string.Empty;
        return text.Length > TextLength ? text[..TextLength] + "…" : text;
    }
}

/// <summary>
/// The "Change Footnote Placement" dialog: all the footnotes of Signet of the book in a list with check boxes, local or global;
/// the checked ones move to the other placement. A filter hides the local or the global ones (a hidden footnote is never
/// checked), and the file the notes that become global go to can be chosen.
/// </summary>
public sealed partial class FootnotePlacementViewModel : ObservableObject
{
    private readonly List<FootnotePlacementRow> _all;
    private readonly Action<string, int> _navigate;
    private readonly Func<IReadOnlyList<PlacementItem>, HtmlResource?, Task<PlacementOutcome?>> _apply;
    private bool _updating;

    /// <summary>Creates the model.</summary>
    /// <param name="items">The footnotes of Signet of the book (<see cref="FootnoteEditor.PlanPlacement"/>).</param>
    /// <param name="files">The files a note can be put in (the XHTML files of the book, without the navigation document), in reading order.</param>
    /// <param name="notesFile">The file with the global notes of Signet now; <c>null</c> when the book has none.</param>
    /// <param name="navigate">Opens a file (book path) at an offset in the main window.</param>
    /// <param name="apply">Moves the footnotes to the other placement (the file for the ones that become global); returns what was done, or <c>null</c> when it was not done.</param>
    public FootnotePlacementViewModel(
        IReadOnlyList<PlacementItem> items,
        IReadOnlyList<HtmlResource> files,
        HtmlResource? notesFile,
        Action<string, int> navigate,
        Func<IReadOnlyList<PlacementItem>, HtmlResource?, Task<PlacementOutcome?>> apply)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(files);
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _all = items.Select(i => new FootnotePlacementRow(this, i)).ToList();

        // The default is the notes file of the book, else a file called like it, else a new one.
        HtmlResource? preferred = notesFile ?? files.FirstOrDefault(f => string.Equals(f.Filename, FootnoteEditor.GlobalFileName, StringComparison.OrdinalIgnoreCase));
        List<GlobalFileChoice> choices = new()
        {
            preferred is null
                ? new GlobalFileChoice(null, Strings.Format("FootnotePlacementWindow_NewFile", FootnoteEditor.GlobalFileName))
                : new GlobalFileChoice(preferred, ReferenceEquals(preferred, notesFile)
                    ? Strings.Format("FootnotePlacementWindow_CurrentFile", preferred.BookPath)
                    : preferred.BookPath),
        };
        choices.AddRange(files.Where(f => !ReferenceEquals(f, preferred)).Select(f => new GlobalFileChoice(f, f.BookPath)));
        GlobalFiles = choices;
        _selectedGlobalFile = choices[0];

        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        SelectAllCommand = new RelayCommand(() => SetChecked(true));
        SelectNoneCommand = new RelayCommand(() => SetChecked(false));
        _rows = _all;
    }

    /// <summary>The files the notes that become global can go to; the first is the default.</summary>
    public IReadOnlyList<GlobalFileChoice> GlobalFiles { get; }

    /// <summary>The file the notes that become global go to.</summary>
    [ObservableProperty]
    private GlobalFileChoice _selectedGlobalFile;

    /// <summary>Whether the local footnotes are in the list.</summary>
    [ObservableProperty]
    private bool _showLocal = true;

    /// <summary>Whether the global footnotes are in the list.</summary>
    [ObservableProperty]
    private bool _showGlobal = true;

    /// <summary>The footnotes shown (as the filter says), in reading order.</summary>
    [ObservableProperty]
    private IReadOnlyList<FootnotePlacementRow> _rows;

    /// <summary>"3 of 5 checked (to global: 2, to local: 1)".</summary>
    public string Summary => Strings.Format(
        "FootnotePlacementWindow_Summary",
        Checked().Count().ToString(CultureInfo.CurrentCulture), Rows.Count.ToString(CultureInfo.CurrentCulture),
        ToGlobal.ToString(CultureInfo.CurrentCulture), ToLocal.ToString(CultureInfo.CurrentCulture));

    /// <summary>How many checked footnotes become global.</summary>
    public int ToGlobal => Checked().Count(r => r.Item.Placement == FootnotePlacement.Local);

    /// <summary>How many checked footnotes become local.</summary>
    public int ToLocal => Checked().Count(r => r.Item.Placement == FootnotePlacement.Global);

    /// <summary>Whether the file of the global notes matters: a checked footnote becomes global.</summary>
    public bool IsGlobalFileEnabled => ToGlobal > 0;

    /// <summary>Whether the book has any footnote of Signet.</summary>
    public bool HasItems => _all.Count > 0;

    /// <summary>Whether something is checked.</summary>
    public bool CanApply => Checked().Any() && !IsBookOperationBusy;

    private bool _isBookOperationBusy;

    /// <summary>
    /// Whether another operation on the book is running elsewhere (set by the host window from
    /// <c>MainWindowViewModel.IsBookOperationBusy</c>) — disables <see cref="ApplyCommand"/> until it finishes.
    /// </summary>
    public bool IsBookOperationBusy
    {
        get => _isBookOperationBusy;
        set
        {
            if (SetProperty(ref _isBookOperationBusy, value))
            {
                OnPropertyChanged(nameof(CanApply));
                ApplyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>"Change" — moves the checked footnotes and closes the dialog (<see cref="Done"/>).</summary>
    public IAsyncRelayCommand ApplyCommand { get; }

    /// <summary>Checks every footnote of the list that can move.</summary>
    public IRelayCommand SelectAllCommand { get; }

    /// <summary>Unchecks every footnote of the list.</summary>
    public IRelayCommand SelectNoneCommand { get; }

    /// <summary>Raised when the footnotes were moved (the dialog closes).</summary>
    public event EventHandler? Done;

    /// <summary>Opens the reference of <paramref name="footnote"/> in the code.</summary>
    internal void Navigate(Footnote footnote)
    {
        if (FootnoteEditor.Locate(footnote, FootnoteSide.Reference) is { } place)
        {
            _navigate(place.File.BookPath, place.Offset);
        }
    }

    /// <summary>Updates the summary after a footnote was (un)checked.</summary>
    internal void OnRowToggled()
    {
        if (_updating)
        {
            return;
        }

        RefreshSummary();
    }

    partial void OnShowLocalChanged(bool value) => Filter();

    partial void OnShowGlobalChanged(bool value) => Filter();

    // Only the footnotes shown can be checked: the hidden ones are unchecked, so nothing unseen is moved.
    private void Filter()
    {
        _updating = true;
        try
        {
            foreach (FootnotePlacementRow row in _all.Where(r => !IsShown(r)))
            {
                row.IsChecked = false;
            }
        }
        finally
        {
            _updating = false;
        }

        Rows = _all.Where(IsShown).ToList();
        RefreshSummary();
    }

    private bool IsShown(FootnotePlacementRow row) => row.Item.Placement == FootnotePlacement.Local ? ShowLocal : ShowGlobal;

    private IEnumerable<FootnotePlacementRow> Checked() => _all.Where(r => r.IsChecked);

    private void SetChecked(bool isChecked)
    {
        _updating = true;
        try
        {
            foreach (FootnotePlacementRow row in Rows)
            {
                row.IsChecked = isChecked && row.IsEnabled;
            }
        }
        finally
        {
            _updating = false;
        }

        RefreshSummary();
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ToGlobal));
        OnPropertyChanged(nameof(ToLocal));
        OnPropertyChanged(nameof(IsGlobalFileEnabled));
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyAsync()
    {
        List<PlacementItem> items = Checked().Select(r => r.Item).ToList();
        if (await _apply(items, ToGlobal > 0 ? SelectedGlobalFile.File : null).ConfigureAwait(true) is not null)
        {
            Done?.Invoke(this, EventArgs.Empty);
        }
    }
}
