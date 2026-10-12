using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Footnotes;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// What converting footnotes of other origin did (<see cref="FootnoteConversionViewModel"/>'s callback): the plan of
/// the book afterwards (the footnotes still left to convert) and what the check after the conversion found wrong.
/// </summary>
/// <param name="Plan">The plan of the book after the conversion (or after the book was brought back).</param>
/// <param name="Problems">What the check found wrong; empty when the conversion succeeded.</param>
/// <param name="Restored">Whether the book was brought back to the state before the conversion (after problems).</param>
/// <param name="SignetFileName">The file name of Signet's notes file now, or <c>null</c> when the book has none.</param>
public sealed record FootnoteConversionOutcome(
    ConversionPlan Plan, IReadOnlyList<ConversionProblem> Problems, bool Restored, string? SignetFileName);

/// <summary>
/// View model of the "Convert Footnotes of Other Origin" dialog: the footnotes of other origin of the book (each with
/// its reference, the start of its note, the number it gets and what needs a look), the options and the choice of the
/// notes file for the global ones. Items with warnings are unchecked until the user checks them; items that cannot be
/// converted cannot be checked. "Convert" converts the checked items and the dialog stays open with the plan of the
/// changed book (the footnotes left to convert), like the Cleanup dialog.
/// </summary>
public sealed partial class FootnoteConversionViewModel : ViewModelBase
{
    private readonly Action<string, int> _navigate;
    private readonly Func<ConversionRequest, Task<FootnoteConversionOutcome?>> _convert;
    private string _signetFileName;
    private bool _updating;

    [ObservableProperty]
    private bool _keepClasses;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private FootnoteConversionFileViewModel? _selectedFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private bool _isConverting;

    /// <summary>Creates the view model.</summary>
    /// <param name="plan">The conversion plan of the book (<see cref="FootnoteEditor.PlanConversion"/>).</param>
    /// <param name="signetFileName">
    /// The file name of Signet's notes file when the book has one, otherwise <c>null</c> (it would be created).
    /// </param>
    /// <param name="navigate">Opens a file at an offset (a double click on a reference or a note).</param>
    /// <param name="convert">
    /// Converts the request in the book; returns what happened, or <c>null</c> when it could not be done (no book, or
    /// the user cancelled it) — the dialog then stays as it is.
    /// </param>
    public FootnoteConversionViewModel(
        ConversionPlan plan,
        string? signetFileName,
        Action<string, int> navigate,
        Func<ConversionRequest, Task<FootnoteConversionOutcome?>> convert)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(navigate);
        ArgumentNullException.ThrowIfNull(convert);
        _navigate = navigate;
        _convert = convert;
        _signetFileName = signetFileName ?? string.Empty;
        ConvertCommand = new AsyncRelayCommand(ConvertAsync, () => CanConvert);
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        Load(plan);
    }

    /// <summary>The footnotes of other origin, in reading order.</summary>
    public ObservableCollection<FootnoteConversionItemViewModel> Items { get; } = new();

    /// <summary>
    /// The files the global notes can go to: the candidates, then Signet's notes file (existing or new).
    /// </summary>
    public ObservableCollection<FootnoteConversionFileViewModel> Files { get; } = new();

    /// <summary>What went wrong in the last conversion (one line each); empty when nothing did.</summary>
    public ObservableCollection<string> Problems { get; } = new();

    /// <summary>The heading over <see cref="Problems"/>.</summary>
    public string ProblemsTitle { get; private set; } = string.Empty;

    /// <summary>Whether the last conversion found problems.</summary>
    public bool HasProblems => Problems.Count > 0;

    /// <summary>Whether the book has footnotes of other origin.</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>Whether the choice of the notes file is needed: a checked footnote is global.</summary>
    public bool NeedsGlobalFile => Items.Any(i => i.IsChecked && i.IsGlobal);

    /// <summary>"3 of 10 checked".</summary>
    public string Summary =>
        Strings.Format("FootnoteConversionWindow_Summary", Items.Count(i => i.IsChecked), Items.Count);

    /// <summary>Whether "Convert" can run: something is checked and, for global footnotes, a file is chosen.</summary>
    public bool CanConvert => !IsConverting && Items.Any(i => i.IsChecked) && (!NeedsGlobalFile || SelectedFile is not null);

    /// <summary>Converts the checked footnotes.</summary>
    public IAsyncRelayCommand ConvertCommand { get; }

    /// <summary>Checks every item that can be converted.</summary>
    public IRelayCommand SelectAllCommand { get; }

    /// <summary>Unchecks every item.</summary>
    public IRelayCommand SelectNoneCommand { get; }

    /// <summary>The request for the checked items and the options.</summary>
    public ConversionRequest Request => new(
        Items.Where(i => i.IsChecked).Select(i => i.Item).ToList(),
        NeedsGlobalFile ? SelectedFile?.File : null,
        KeepClasses);

    /// <summary>Opens the file of the reference or of the note of <paramref name="item"/> at its place.</summary>
    public void Navigate(FootnoteConversionItemViewModel item, FootnoteSide side)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (FootnoteEditor.Locate(item.Item.Footnote, side) is { } place)
        {
            _navigate(place.File.BookPath, place.Offset);
        }
    }

    internal void OnItemToggled()
    {
        if (_updating)
        {
            return;
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(NeedsGlobalFile));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    private void SetAll(bool isChecked)
    {
        _updating = true;
        try
        {
            foreach (FootnoteConversionItemViewModel item in Items.Where(i => i.CanConvert))
            {
                item.IsChecked = isChecked;
            }
        }
        finally
        {
            _updating = false;
        }

        OnItemToggled();
    }

    private async Task ConvertAsync()
    {
        IsConverting = true;
        try
        {
            if (await _convert(Request).ConfigureAwait(true) is not { } outcome)
            {
                return;
            }

            _signetFileName = outcome.SignetFileName ?? string.Empty;
            Load(outcome.Plan);
            ShowProblems(outcome);
        }
        finally
        {
            IsConverting = false;
        }
    }

    // Shows a plan: the items with their default check state and the files with the automatic choice.
    private void Load(ConversionPlan plan)
    {
        _updating = true;
        try
        {
            Items.Clear();
            foreach (ConversionItem item in plan.Items)
            {
                Items.Add(new FootnoteConversionItemViewModel(this, item));
            }

            Files.Clear();
            foreach (GlobalFileCandidate candidate in plan.Candidates)
            {
                Files.Add(new FootnoteConversionFileViewModel(
                    candidate.File, CandidateText(candidate, ReferenceEquals(candidate, plan.AutomaticFile))));
            }

            Files.Add(new FootnoteConversionFileViewModel(
                null,
                _signetFileName.Length > 0
                    ? Strings.Format("FootnoteConversionWindow_SignetFile", _signetFileName)
                    : Strings.Format("FootnoteConversionWindow_NewSignetFile", FootnoteEditor.GlobalFileName)));
            SelectedFile = plan.AutomaticFile is { } automatic
                ? Files.First(f => ReferenceEquals(f.File, automatic.File))
                : null;
        }
        finally
        {
            _updating = false;
        }

        OnPropertyChanged(nameof(HasItems));
        OnItemToggled();
    }

    private void ShowProblems(FootnoteConversionOutcome outcome)
    {
        Problems.Clear();
        foreach (ConversionProblem problem in outcome.Problems)
        {
            Problems.Add(ProblemText(problem));
        }

        ProblemsTitle = Strings.Get(outcome.Restored ? "FootnoteConversionWindow_ProblemsRestored" : "FootnoteConversionWindow_ProblemsKept");
        OnPropertyChanged(nameof(ProblemsTitle));
        OnPropertyChanged(nameof(HasProblems));
    }

    /// <summary>One line about a problem found by the check after the conversion.</summary>
    public static string ProblemText(ConversionProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        string what = Strings.Get("FootnoteConversionProblem_" + problem.Kind.ToString());
        string where = string.Join(", ", new[] { problem.BookPath, problem.Detail }.Where(s => s.Length > 0));
        return where.Length == 0 ? what : what + " — " + where;
    }

    // "Notes.xhtml — 12 notes; only notes, all global notes; epub:type of notes (chosen automatically)".
    private static string CandidateText(GlobalFileCandidate candidate, bool automatic)
    {
        List<string> details = new()
        {
            Strings.Get(candidate.IsDedicated ? "FootnoteConversionWindow_CandidateDedicated" : "FootnoteConversionWindow_CandidateNotDedicated"),
        };
        if (candidate.HoldsAll)
        {
            details.Add(Strings.Get("FootnoteConversionWindow_CandidateHoldsAll"));
        }

        details.AddRange(candidate.Hints.Select(h => Strings.Get("FootnoteConversionWindow_Hint_" + h.ToString())));
        string text = Strings.Format(
            "FootnoteConversionWindow_Candidate", candidate.File.BookPath,
            candidate.NoteCount.ToString(CultureInfo.CurrentCulture), string.Join(", ", details));
        return automatic ? text + " " + Strings.Get("FootnoteConversionWindow_Automatic") : text;
    }
}

/// <summary>A footnote of other origin in the conversion dialog.</summary>
public sealed partial class FootnoteConversionItemViewModel : ObservableObject
{
    // How much of the note the row shows.
    private const int NoteLength = 120;

    private readonly FootnoteConversionViewModel _owner;

    [ObservableProperty]
    private bool _isChecked;

    internal FootnoteConversionItemViewModel(FootnoteConversionViewModel owner, ConversionItem item)
    {
        _owner = owner;
        Item = item;
        Footnote footnote = item.Footnote;
        IsGlobal = footnote.Placement == FootnotePlacement.Global;
        Header = Strings.Format(
            "FootnoteConversionWindow_ItemHeader", item.Marker, item.NewNumber,
            Strings.Get(IsGlobal ? "FootnoteConversionWindow_Global" : "FootnoteConversionWindow_Local"));
        ReferenceText = Strings.Format("FootnoteConversionWindow_Reference", footnote.ReferenceFile.BookPath, ReferenceContext(footnote, item.Marker));
        string note = item.NoteText.Length > NoteLength ? item.NoteText[..NoteLength] + "…" : item.NoteText;
        NoteText = Strings.Format("FootnoteConversionWindow_Note", footnote.NoteFile.BookPath, note);
        Warnings = item.CanConvert
            ? item.Warnings.Select(w => WarningText(w, item)).ToList()
            : new[] { Strings.Get("FootnoteConversionWindow_CannotConvert") };
        _isChecked = item.CanConvert && item.Warnings.Count == 0;
    }

    /// <summary>The item of the plan.</summary>
    public ConversionItem Item { get; }

    /// <summary>Whether the footnote is global (its note is in another file).</summary>
    public bool IsGlobal { get; }

    /// <summary>Whether it can be converted (only then can it be checked).</summary>
    public bool CanConvert => Item.CanConvert;

    /// <summary>Whether it needs a look (marked ⚠).</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>"“*” → 1 (global)".</summary>
    public string Header { get; }

    /// <summary>The file of the reference and the text around it.</summary>
    public string ReferenceText { get; }

    /// <summary>The file of the note and the start of its text.</summary>
    public string NoteText { get; }

    /// <summary>What needs a look, or why it cannot be converted.</summary>
    public IReadOnlyList<string> Warnings { get; }

    partial void OnIsCheckedChanged(bool value) => _owner.OnItemToggled();

    /// <summary>The description of a warning of <paramref name="item"/>.</summary>
    public static string WarningText(ConversionWarning warning, ConversionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return warning switch
        {
            ConversionWarning.NonNumericMarker => Strings.Format("FootnoteConversionWarning_NonNumericMarker", item.Marker, item.NewNumber),
            ConversionWarning.NumberChanges => Strings.Format("FootnoteConversionWarning_NumberChanges", item.Marker, item.NewNumber),
            ConversionWarning.LinkedElsewhere => Strings.Format("FootnoteConversionWarning_LinkedElsewhere", string.Join(", ", item.OtherLinks)),
            _ => Strings.Get("FootnoteConversionWarning_" + warning.ToString()),
        };
    }

    // "…text before [*] text after…".
    private static string ReferenceContext(Footnote footnote, string marker)
    {
        if (FootnoteEditor.LocateReferenceRange(footnote) is not { } range)
        {
            return string.Empty;
        }

        string text = range.File.GetText();
        (string before, string after) = FootnoteViewModel.Context(text, range.Start, range.End);
        return before + "[" + marker + "]" + after;
    }
}

/// <summary>A file the global notes can go to in the conversion dialog.</summary>
/// <param name="File">The candidate, or <c>null</c> for Signet's notes file.</param>
/// <param name="Text">What the list shows.</param>
public sealed record FootnoteConversionFileViewModel(HtmlResource? File, string Text);
