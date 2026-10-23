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
/// <para>The view shows them as a tree (<see cref="Roots"/>): the files, their footnotes and under a footnote its other
/// links (the other references to its note first of all); "Only those that need a look" leaves the rows with ⚠, and
/// "Check All" / "Uncheck All" then work on those only. The panel under the tree shows the <see cref="Selected"/> row.</para>
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

    /// <summary>
    /// Whether another operation on the book is running elsewhere (set by the host window from
    /// <c>MainWindowViewModel.IsBookOperationBusy</c>) — disables <see cref="ConvertCommand"/> until it finishes.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private bool _isBookOperationBusy;

    /// <summary>Whether the tree shows only the footnotes that need a look (⚠, also those that cannot be converted).</summary>
    [ObservableProperty]
    private bool _onlyWarnings;

    /// <summary>The row whose details the panel under the tree shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private FootnoteConversionNode? _selected;

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

    /// <summary>The rows of the tree: the files with their footnotes (after the filter), in reading order.</summary>
    public IReadOnlyList<FootnoteConversionFileNode> Roots { get; private set; } = Array.Empty<FootnoteConversionFileNode>();

    /// <summary>Whether a row is selected (the panel under the tree shows it).</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>"Only those that need a look (⚠ 570)".</summary>
    public string OnlyWarningsText =>
        Strings.Format("FootnoteConversionWindow_OnlyWarnings", Items.Count(i => i.HasWarnings).ToString(CultureInfo.CurrentCulture));

    /// <summary>
    /// The files the global notes can go to: the candidates, then Signet's notes file (existing or new).
    /// </summary>
    public ObservableCollection<FootnoteConversionFileViewModel> Files { get; } = new();

    /// <summary>What went wrong in the last conversion (a few lines each); empty when nothing did.</summary>
    public ObservableCollection<FootnoteConversionProblemRow> Problems { get; } = new();

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
    public bool CanConvert => !IsConverting && !IsBookOperationBusy && Items.Any(i => i.IsChecked) && (!NeedsGlobalFile || SelectedFile is not null);

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
        Navigate(item.Item.Footnote, side);
    }

    /// <summary>Opens the file of the reference (one of a shared note's) or of the note of <paramref name="footnote"/>.</summary>
    internal void Navigate(Footnote footnote, FootnoteSide side)
    {
        if (FootnoteEditor.Locate(footnote, side) is { } place)
        {
            _navigate(place.File.BookPath, place.Offset);
        }
    }

    /// <summary>Opens <paramref name="file"/> at <paramref name="offset"/> (a negative offset = only the file).</summary>
    internal void NavigateTo(Resource file, int offset) => _navigate(file.BookPath, offset);

    /// <summary>Checks or unchecks the <paramref name="items"/> that can be converted.</summary>
    internal void SetChecked(IEnumerable<FootnoteConversionItemViewModel> items, bool isChecked)
    {
        _updating = true;
        try
        {
            foreach (FootnoteConversionItemViewModel item in items.Where(i => i.CanConvert))
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

    partial void OnOnlyWarningsChanged(bool value) => BuildRoots();

    // The tree: the footnotes left by the filter under their files (of the reference), in reading order.
    private void BuildRoots()
    {
        Selected = null;
        Roots = Items.Where(i => !OnlyWarnings || i.HasWarnings)
            .GroupBy(i => i.Item.Footnote.ReferenceFile)
            .Select(g =>
            {
                FootnoteConversionItemViewModel[] items = g.ToArray();
                FootnoteConversionFileNode file = new(this, g.Key, items);
                foreach (FootnoteConversionItemViewModel item in items)
                {
                    item.Parent = file;
                }

                return file;
            })
            .ToArray();
        OnPropertyChanged(nameof(Roots));
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

    // "Check All" / "Uncheck All": the footnotes the tree shows.
    private void SetAll(bool isChecked) => SetChecked(Roots.SelectMany(f => f.Children).Cast<FootnoteConversionItemViewModel>(), isChecked);

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

        BuildRoots();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(OnlyWarningsText));
        OnItemToggled();
    }

    private void ShowProblems(FootnoteConversionOutcome outcome)
    {
        Problems.Clear();
        foreach (ConversionProblem problem in outcome.Problems)
        {
            // The book was brought back, so the note is where it was: a click goes to it.
            FootnoteConversionItemViewModel? item = outcome.Restored && problem.SourceId.Length > 0
                ? Items.FirstOrDefault(i => i.Item.Footnote.NoteId == problem.SourceId && i.Item.Footnote.NoteFile.BookPath == problem.SourcePath)
                : null;
            Problems.Add(new FootnoteConversionProblemRow(
                ProblemText(problem), item is null ? null : new RelayCommand(() => Navigate(item, FootnoteSide.Note))));
        }

        ProblemsTitle = Strings.Get(outcome.Restored ? "FootnoteConversionWindow_ProblemsRestored" : "FootnoteConversionWindow_ProblemsKept");
        OnPropertyChanged(nameof(ProblemsTitle));
        OnPropertyChanged(nameof(HasProblems));
    }

    // How much of a text the report of a problem shows.
    private const int ReportedTextLength = 300;

    private const char LineBreak = '\n';

    /// <summary>
    /// A problem found by the check after the conversion: what is wrong and where the footnote was before the conversion
    /// (its marker, id and file); for a text that changed also the text before and after, and what went missing or appeared.
    /// </summary>
    public static string ProblemText(ConversionProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        string what = Strings.Get("FootnoteConversionProblem_" + problem.Kind.ToString());
        string where = problem.SourceId.Length > 0
            ? Strings.Format("FootnoteConversionProblem_Source", problem.Marker, problem.SourcePath, problem.SourceId)
            : string.Join(", ", new[] { problem.BookPath, problem.Detail }.Where(s => s.Length > 0));
        System.Text.StringBuilder text = new(where.Length == 0 ? what : what + " — " + where);
        if (problem.Kind != ConversionProblemKind.TextChanged || problem.Expected is not { } expected)
        {
            return text.ToString();
        }

        text.Append(LineBreak).Append(Strings.Format("FootnoteConversionProblem_Before", Shorten(expected)));
        if (problem.Actual is not { } actual)
        {
            return text.Append(LineBreak).Append(Strings.Get("FootnoteConversionProblem_NoteLost")).ToString();
        }

        text.Append(LineBreak).Append(Strings.Format("FootnoteConversionProblem_After", Shorten(actual)));
        (string gone, string appeared) = Difference(expected, actual);
        if (gone.Length > 0)
        {
            text.Append(LineBreak).Append(Strings.Format("FootnoteConversionProblem_Gone", Shorten(gone)));
        }

        if (appeared.Length > 0)
        {
            text.Append(LineBreak).Append(Strings.Format("FootnoteConversionProblem_Appeared", Shorten(appeared)));
        }

        return text.ToString();
    }

    /// <summary>What <paramref name="before"/> has that <paramref name="after"/> has not, and the other way round (what lies between the common start and end).</summary>
    public static (string Gone, string Appeared) Difference(string before, string after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        int start = 0;
        while (start < before.Length && start < after.Length && before[start] == after[start])
        {
            start++;
        }

        int end = 0;
        while (end < before.Length - start && end < after.Length - start && before[^(end + 1)] == after[^(end + 1)])
        {
            end++;
        }

        return (before[start..^end].Trim(), after[start..^end].Trim());
    }

    private static string Shorten(string text) =>
        text.Length > ReportedTextLength ? text[..ReportedTextLength] + "…" : text;

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

/// <summary>A footnote of other origin in the conversion dialog (a row of the tree, under its file).</summary>
public sealed partial class FootnoteConversionItemViewModel : FootnoteConversionNode
{
    // How much of the note the row shows (the panel under the tree shows all of it).
    private const int NoteLength = 200;

    private readonly FootnoteConversionViewModel _owner;
    private (string Before, string After)? _context;

    [ObservableProperty]
    private bool _isChecked;

    internal FootnoteConversionItemViewModel(FootnoteConversionViewModel owner, ConversionItem item)
    {
        _owner = owner;
        Item = item;
        Footnote footnote = item.Footnote;
        IsGlobal = footnote.Placement == FootnotePlacement.Global;
        Kind = Strings.Get(IsGlobal ? "FootnoteConversionWindow_Global" : "FootnoteConversionWindow_Local");
        Title = Strings.Format("FootnoteConversionWindow_ItemTitle", item.Marker, item.NewNumber);
        Header = Strings.Format("FootnoteConversionWindow_ItemHeader", item.Marker, item.NewNumber, Kind);
        Note = item.NoteText.Length > NoteLength ? item.NoteText[..NoteLength] + "…" : item.NoteText;
        Warnings = item.CanConvert
            ? item.Warnings.Select(w => WarningText(w, item)).ToList()
            : new[] { BlockerText(item) };
        Notices = item.Notices.Select(n => NoticeText(n, item)).ToList();
        Children = item.Links.Select(l => new FootnoteConversionLinkNode(owner, l, footnote.ReferenceFile)).ToArray();
        _isChecked = item.CanConvert && item.Warnings.Count == 0;
    }

    /// <summary>The item of the plan.</summary>
    public ConversionItem Item { get; }

    /// <summary>The file row it is under.</summary>
    internal FootnoteConversionFileNode? Parent { get; set; }

    /// <summary>Whether the footnote is global (its note is in another file).</summary>
    public bool IsGlobal { get; }

    /// <summary>Whether it can be converted (only then can it be checked).</summary>
    public bool CanConvert => Item.CanConvert;

    /// <inheritdoc />
    public override bool CanCheck => CanConvert;

    /// <inheritdoc />
    public override bool HasCheckBox => true;

    /// <inheritdoc />
    public override bool? Check
    {
        get => IsChecked;
        set
        {
            if (CanConvert)
            {
                IsChecked = value == true;
            }
        }
    }

    /// <summary>Whether it needs a look (marked ⚠).</summary>
    public override bool HasWarnings => Warnings.Count > 0;

    /// <summary>"“*” → 1".</summary>
    public override string Title { get; }

    /// <summary>"local" / "global".</summary>
    public override string Kind { get; }

    /// <summary>"“*” → 1 (global)".</summary>
    public string Header { get; }

    /// <summary>
    /// The text around the reference: "…text before [*] text after…" (shortened before it for the cell); computed when
    /// the row is first shown — a book can have thousands of footnotes.
    /// </summary>
    public override string Reference => ReferenceAround(cell: true);

    /// <summary>The start of the text of the note.</summary>
    public override string Note { get; }

    /// <summary>The other links to it: the other references to its note, the links the conversion rewrites.</summary>
    public override IReadOnlyList<FootnoteConversionNode> Children { get; }

    /// <summary>What needs a look, or why it cannot be converted.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <inheritdoc />
    public override string DetailTitle => Header;

    /// <inheritdoc />
    public override IReadOnlyList<string> DetailWarnings => Warnings;

    /// <summary>What the conversion does besides the usual (for information: the item stays checked).</summary>
    public IReadOnlyList<string> Notices { get; }

    /// <inheritdoc />
    public override IReadOnlyList<string> DetailNotices => Notices;

    /// <inheritdoc />
    public override void GoToReference() => _owner.Navigate(this, FootnoteSide.Reference);

    /// <inheritdoc />
    public override void GoToNote() => _owner.Navigate(this, FootnoteSide.Note);

    /// <summary>Its reference, its note and its other links.</summary>
    protected override IReadOnlyList<FootnoteConversionPlace> CreatePlaces()
    {
        Footnote footnote = Item.Footnote;
        return new[]
        {
            new FootnoteConversionPlace(
                Strings.Get("FootnoteConversionWindow_PlaceReference"),
                FootnoteEditor.Locate(footnote, FootnoteSide.Reference) is { } reference ? Location(reference.File, reference.Offset) : footnote.ReferenceFile.BookPath,
                ReferenceAround(cell: false),
                new RelayCommand(GoToReference)),
            new FootnoteConversionPlace(
                Strings.Get("FootnoteConversionWindow_PlaceNote"),
                FootnoteEditor.Locate(footnote, FootnoteSide.Note) is { } note ? Location(note.File, note.Offset) : footnote.NoteFile.BookPath,
                Item.NoteText,
                new RelayCommand(GoToNote)),
        }
        .Concat(Item.References.Skip(1).Select((reference, i) => new FootnoteConversionPlace(
            Strings.Format("FootnoteConversionWindow_PlaceOtherReference", (i + 2).ToString(CultureInfo.CurrentCulture)),
            FootnoteEditor.Locate(reference, FootnoteSide.Reference) is { } at ? Location(at.File, at.Offset) : reference.ReferenceFile.BookPath,
            FootnoteEditor.LocateReferenceRange(reference) is { } range
                ? ReferenceText(FootnoteViewModel.Context(range.File.GetText(), range.Start, range.End), cell: false)
                : string.Empty,
            new RelayCommand(() => _owner.Navigate(reference, FootnoteSide.Reference)))))
        .Concat(Children.SelectMany(c => c.Places))
        .ToArray();
    }

    partial void OnIsCheckedChanged(bool value)
    {
        OnPropertyChanged(nameof(Check));
        Parent?.RefreshCheck();
        _owner.OnItemToggled();
    }

    /// <summary>Why <paramref name="item"/> cannot be converted.</summary>
    public static string BlockerText(ConversionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Blocker switch
        {
            ConversionBlocker.None => Strings.Get("FootnoteConversionWindow_CannotConvert"),
            _ => Strings.Get("FootnoteConversionBlocker_" + item.Blocker),
        };
    }

    /// <summary>The description of a notice of <paramref name="item"/>.</summary>
    public static string NoticeText(ConversionNotice notice, ConversionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return notice == ConversionNotice.SharedNote
            ? Strings.Format(
                "FootnoteConversionNotice_SharedNote", item.References.Count.ToString(CultureInfo.CurrentCulture),
                item.NewNumber.ToString(CultureInfo.CurrentCulture))
            : Strings.Get("FootnoteConversionNotice_" + notice.ToString());
    }

    /// <summary>The description of a warning of <paramref name="item"/>.</summary>
    public static string WarningText(ConversionWarning warning, ConversionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return warning switch
        {
            ConversionWarning.NonNumericMarker => Strings.Format("FootnoteConversionWarning_NonNumericMarker", item.Marker, item.NewNumber),
            ConversionWarning.NumberChanges => Strings.Format("FootnoteConversionWarning_NumberChanges", item.Marker, item.NewNumber),
            ConversionWarning.LinkedElsewhere => Strings.Format("FootnoteConversionWarning_LinkedElsewhere", string.Join(", ", item.OtherLinks)),
            ConversionWarning.ContainsNote or ConversionWarning.InsideNote =>
                Strings.Format("FootnoteConversionWarning_" + warning, item.NestedWith),
            _ => Strings.Get("FootnoteConversionWarning_" + warning.ToString()),
        };
    }

    private string ReferenceAround(bool cell) => ReferenceText(_context ??= ReferenceContext(Item), cell);

    private string ReferenceText((string Before, string After) context, bool cell) =>
        ReferenceText(context.Before, Item.Marker, context.After, cell);

    // The text before and after the reference (where the plan found it: a book can have thousands of footnotes, so
    // the files are not parsed again).
    private static (string Before, string After) ReferenceContext(ConversionItem item) =>
        item.ReferenceRange is { } range
            ? FootnoteViewModel.Context(item.Footnote.ReferenceFile.GetText(), range.Start, range.End)
            : (string.Empty, string.Empty);
}

/// <summary>A problem the check after the conversion found, as the dialog lists it.</summary>
/// <param name="Text">What is wrong (a few lines).</param>
/// <param name="GoTo">Opens the note in the code; <c>null</c> when it cannot (the book was not brought back).</param>
public sealed record FootnoteConversionProblemRow(string Text, System.Windows.Input.ICommand? GoTo)
{
    /// <summary>Whether a click goes to the note.</summary>
    public bool IsLink => GoTo is not null;
}

/// <summary>A file the global notes can go to in the conversion dialog.</summary>
/// <param name="File">The candidate, or <c>null</c> for Signet's notes file.</param>
/// <param name="Text">What the list shows.</param>
public sealed record FootnoteConversionFileViewModel(HtmlResource? File, string Text);
