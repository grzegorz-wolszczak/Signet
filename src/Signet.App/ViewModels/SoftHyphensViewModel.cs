using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.BookManipulation;
using Signet.Core.Hyphenation;

namespace Signet.App.ViewModels;

/// <summary>Which of the two operations the "Hyphenation" dialog shows.</summary>
public enum SoftHyphensMode
{
    /// <summary>Adding soft hyphens.</summary>
    Add,

    /// <summary>Removing soft hyphens.</summary>
    Remove,

    /// <summary>Converting soft hyphens to another form.</summary>
    Convert,
}

/// <summary>
/// The "Hyphenation" dialog, with a switch between adding, removing and converting soft hyphens; each has its own tree
/// and checks.
/// Adding: the languages of the book, each with the patterns it is hyphenated with (or why it cannot be), and the files
/// its text is in, all checked that can be; the shortest hyphenated word. Removing: the languages whose text has soft
/// hyphens, with their files, and "Outside the text" — every soft hyphen in an attribute, the head, an element that is
/// not prose or a comment, grouped by where it stands, one by one. Converting: the same rows, of the soft hyphens not
/// written in the chosen form yet. The form (the character U+00AD or <c>&amp;#173;</c>) is shared by adding and
/// converting and remembered. "Add" / "Remove" / "Convert" applies the plan of the checked rows and the dialog stays
/// open on the book surveyed anew; "Close" closes it.
/// </summary>
public sealed partial class SoftHyphensViewModel : ObservableObject
{
    // The fewest and the most letters of the shortest hyphenated word the dialog accepts.
    private const int MinWordLengthLow = 2;
    private const int MinWordLengthHigh = 30;

    // The attributes a soft hyphen in which gets a group of its own; the others share "other attributes".
    private static readonly string[] NamedAttributes = ["id", "href", "class", "alt", "title"];

    private readonly Action<string, int> _navigate;
    private readonly Func<HyphenationPatternFiles> _readPatternFiles;
    private readonly Func<SoftHyphenAddPlan, Task<SoftHyphenSurvey?>> _add;
    private readonly Func<SoftHyphenRemovePlan, Task<SoftHyphenSurvey?>> _remove;
    private readonly Func<SoftHyphenConvertPlan, Task<SoftHyphenSurvey?>> _convert;
    private readonly Action<SoftHyphenForm> _rememberForm;
    private readonly Dictionary<string, HyphenationPatterns> _patterns = new(StringComparer.Ordinal);
    private SoftHyphenSurvey _survey;
    private HyphenationPatternFiles _patternFiles;

    /// <summary>Creates the dialog.</summary>
    /// <param name="survey">What the book holds (<see cref="Signet.Core.BookManipulation.Book.SurveySoftHyphens"/>).</param>
    /// <param name="readPatternFiles">Reads the installed patterns (again, after the dictionary manager changed them).</param>
    /// <param name="navigate">Opens a file at an offset (a double click on a file or a place).</param>
    /// <param name="add">Applies a plan of adding; answers the book surveyed anew, or <c>null</c> when it did not run.</param>
    /// <param name="remove">Applies a plan of removing; answers the book surveyed anew, or <c>null</c> when it did not run.</param>
    /// <param name="convert">Applies a plan of converting; answers the book surveyed anew, or <c>null</c> when it did not run.</param>
    /// <param name="form">The form chosen last.</param>
    /// <param name="rememberForm">Remembers the form chosen (for the next time).</param>
    public SoftHyphensViewModel(
        SoftHyphenSurvey survey,
        Func<HyphenationPatternFiles> readPatternFiles,
        Action<string, int> navigate,
        Func<SoftHyphenAddPlan, Task<SoftHyphenSurvey?>> add,
        Func<SoftHyphenRemovePlan, Task<SoftHyphenSurvey?>> remove,
        Func<SoftHyphenConvertPlan, Task<SoftHyphenSurvey?>> convert,
        SoftHyphenForm form,
        Action<SoftHyphenForm> rememberForm)
    {
        _survey = survey ?? throw new ArgumentNullException(nameof(survey));
        _readPatternFiles = readPatternFiles ?? throw new ArgumentNullException(nameof(readPatternFiles));
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        _add = add ?? throw new ArgumentNullException(nameof(add));
        _remove = remove ?? throw new ArgumentNullException(nameof(remove));
        _convert = convert ?? throw new ArgumentNullException(nameof(convert));
        _rememberForm = rememberForm ?? throw new ArgumentNullException(nameof(rememberForm));
        _form = form;
        _patternFiles = readPatternFiles();
        Build();
    }

    /// <summary>Raised when the dialog is to close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The fewest letters of the shortest hyphenated word the dialog accepts (the field's bound).</summary>
    public static decimal MinWordLengthMin => MinWordLengthLow;

    /// <summary>The most letters of the shortest hyphenated word the dialog accepts (the field's bound).</summary>
    public static decimal MinWordLengthMax => MinWordLengthHigh;

    /// <summary>Adding or removing (the switch at the top).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddMode), nameof(IsRemoveMode), nameof(IsConvertMode), nameof(ShowsForm), nameof(Roots),
        nameof(Intro), nameof(ApplyText), nameof(CountHeaderKey), nameof(CanApply), nameof(HasLanguagesWithoutPatterns),
        nameof(IsEmpty), nameof(EmptyText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private SoftHyphensMode _mode;

    /// <summary>Whether the dialog shows adding (the "Add" side of the switch).</summary>
    public bool IsAddMode
    {
        get => Mode == SoftHyphensMode.Add;
        set
        {
            if (value)
            {
                Mode = SoftHyphensMode.Add;
            }
        }
    }

    /// <summary>Whether the dialog shows removing (the "Remove" side of the switch).</summary>
    public bool IsRemoveMode
    {
        get => Mode == SoftHyphensMode.Remove;
        set
        {
            if (value)
            {
                Mode = SoftHyphensMode.Remove;
            }
        }
    }

    /// <summary>Whether the dialog shows converting (the "Convert" side of the switch).</summary>
    public bool IsConvertMode
    {
        get => Mode == SoftHyphensMode.Convert;
        set
        {
            if (value)
            {
                Mode = SoftHyphensMode.Convert;
            }
        }
    }

    /// <summary>Whether the shown side writes soft hyphens (adding, converting), so the form can be chosen.</summary>
    public bool ShowsForm => Mode != SoftHyphensMode.Remove;

    /// <summary>What the shown side does, above the tree.</summary>
    public string Intro => Strings.Get("SoftHyphens_" + Mode + "Intro");

    /// <summary>The text of the button that applies the plan of the shown side.</summary>
    public string ApplyText => Strings.Get("SoftHyphens_" + Mode);

    /// <summary>The header of the count column of the shown side.</summary>
    public string CountHeaderKey => Mode switch
    {
        SoftHyphensMode.Add => "SoftHyphens_ColumnWords",
        SoftHyphensMode.Convert => "SoftHyphens_ColumnToConvert",
        _ => "SoftHyphens_ColumnSoftHyphens",
    };

    /// <summary>How the soft hyphens added or converted to are written (shared by both sides, remembered).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCharacterForm), nameof(IsReferenceForm))]
    private SoftHyphenForm _form;

    /// <summary>Whether the soft hyphens are written as the character U+00AD.</summary>
    public bool IsCharacterForm
    {
        get => Form == SoftHyphenForm.Character;
        set
        {
            if (value)
            {
                Form = SoftHyphenForm.Character;
            }
        }
    }

    /// <summary>Whether the soft hyphens are written as <c>&amp;#173;</c>.</summary>
    public bool IsReferenceForm
    {
        get => Form == SoftHyphenForm.Reference;
        set
        {
            if (value)
            {
                Form = SoftHyphenForm.Reference;
            }
        }
    }

    /// <summary>The rows of adding.</summary>
    public ObservableCollection<SoftHyphensNode> AddRoots { get; } = new();

    /// <summary>The rows of removing.</summary>
    public ObservableCollection<SoftHyphensNode> RemoveRoots { get; } = new();

    /// <summary>The rows of converting.</summary>
    public ObservableCollection<SoftHyphensNode> ConvertRoots { get; } = new();

    /// <summary>The rows of the shown side.</summary>
    public ObservableCollection<SoftHyphensNode> Roots => Mode switch
    {
        SoftHyphensMode.Add => AddRoots,
        SoftHyphensMode.Convert => ConvertRoots,
        _ => RemoveRoots,
    };

    /// <summary>
    /// Opens the dictionary manager over the dialog — attached by the view; it answers whether a dictionary was
    /// installed or deleted. <c>null</c> (tests without a UI) = the button does nothing.
    /// </summary>
    public Func<Task<bool>>? DictionaryManager { get; set; }

    /// <summary>Whether some language of the book has no patterns (the dictionary manager is offered).</summary>
    public bool HasLanguagesWithoutPatterns =>
        IsAddMode && AddRoots.OfType<SoftHyphensLanguageNode>().Any(l => !l.Language.IsUnknown && !l.HasCheckBox);

    /// <summary>Whether the book has nothing for the shown side (no text to hyphenate, no soft hyphen to remove).</summary>
    public bool IsEmpty => Roots.Count == 0;

    /// <summary>What the shown side says when the book has nothing for it.</summary>
    public string EmptyText => Strings.Get("SoftHyphens_NothingTo" + Mode);

    /// <summary>The fewest letters of a word that is hyphenated (adding only).</summary>
    [ObservableProperty]
    private int _minWordLength = SoftHyphenInserter.DefaultMinWordLength;

    /// <summary>Whether another operation on the book is running (the main window keeps it current); "Add" / "Remove" wait.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBookOperationBusy;

    /// <summary>Whether the shown side has anything checked and nothing else is running.</summary>
    public bool CanApply => !IsBookOperationBusy && Roots.SelectMany(r => r.Leaves()).Any(l => l.HasCheckBox && l.Check == true);

    // Another form: it is remembered, and converting lists the soft hyphens not written in it.
    partial void OnFormChanged(SoftHyphenForm value)
    {
        _rememberForm(value);
        BuildConverting();
        OnPropertyChanged(nameof(IsEmpty));
        OnChecksChanged();
    }

    partial void OnMinWordLengthChanged(int value)
    {
        int clamped = Math.Clamp(value, MinWordLengthLow, MinWordLengthHigh);
        if (clamped != value)
        {
            MinWordLength = clamped;
            return;
        }

        foreach (SoftHyphensNode root in AddRoots)
        {
            root.RefreshCount();
        }
    }

    /// <summary>The plan of the checked rows of adding.</summary>
    public SoftHyphenAddPlan AddPlan()
    {
        HashSet<(string BookPath, string Language)> chosen = CheckedFiles(AddRoots);
        Dictionary<string, HyphenationPatterns> patterns = chosen
            .Select(c => c.Language)
            .Distinct(StringComparer.Ordinal)
            .Where(_patterns.ContainsKey)
            .ToDictionary(key => key, key => _patterns[key], StringComparer.Ordinal);
        return new SoftHyphenAddPlan(chosen, patterns, MinWordLength, Form);
    }

    /// <summary>The plan of the checked rows of removing.</summary>
    public SoftHyphenRemovePlan RemovePlan() => new(CheckedFiles(RemoveRoots), CheckedPlaces(RemoveRoots));

    /// <summary>The plan of the checked rows of converting.</summary>
    public SoftHyphenConvertPlan ConvertPlan() => new(CheckedFiles(ConvertRoots), CheckedPlaces(ConvertRoots), Form);

    /// <summary>
    /// Where the book says a language: "lang on &lt;html&gt; — files: 2, dc:language (OPF) — files: 1"; without the
    /// counts for a single file.
    /// </summary>
    public static string SourcesText(IReadOnlyList<(LanguageSource Source, int Files)> sources, bool withFiles = true)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return string.Join(", ", sources.Select(s =>
        {
            string name = s.Source.Kind switch
            {
                LanguageSourceKind.Element => Strings.Format("SoftHyphens_SourceElement", s.Source.ElementName),
                LanguageSourceKind.Opf => Strings.Get("SoftHyphens_SourceOpf"),
                _ => Strings.Get("SoftHyphens_SourceNone"),
            };
            return withFiles ? Strings.Format("SoftHyphens_SourceFiles", name, s.Files) : name;
        }));
    }

    /// <summary>Opens a file at an offset in the main window.</summary>
    internal void NavigateTo(string bookPath, int offset) => _navigate(bookPath, offset);

    /// <summary>Called when a check box changed.</summary>
    internal void OnChecksChanged()
    {
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Checks every row of the shown side that can be checked.</summary>
    [RelayCommand]
    private void SelectAll() => SetAll(true);

    /// <summary>Unchecks every row of the shown side.</summary>
    [RelayCommand]
    private void DeselectAll() => SetAll(false);

    /// <summary>
    /// Applies the plan of the checked rows of the shown side; the dialog stays open and shows the book surveyed anew
    /// (both sides, checked afresh).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task Apply()
    {
        SoftHyphenSurvey? surveyed = Mode switch
        {
            SoftHyphensMode.Add => await _add(AddPlan()).ConfigureAwait(true),
            SoftHyphensMode.Convert => await _convert(ConvertPlan()).ConfigureAwait(true),
            _ => await _remove(RemovePlan()).ConfigureAwait(true),
        };
        if (surveyed is not null)
        {
            _survey = surveyed;
            Build();
        }
    }

    /// <summary>Closes the dialog.</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Opens the dictionary manager; when it changed the dictionaries, the patterns are read again.</summary>
    [RelayCommand]
    private async Task OpenDictionaryManager()
    {
        if (DictionaryManager is { } manager && await manager().ConfigureAwait(true))
        {
            _patternFiles = _readPatternFiles();
            Build();
        }
    }

    private void SetAll(bool value)
    {
        foreach (SoftHyphensNode root in Roots.Where(r => r.HasCheckBox))
        {
            root.Check = value;
        }

        OnChecksChanged();
    }

    private static List<SoftHyphenPlace> CheckedPlaces(IEnumerable<SoftHyphensNode> roots) =>
        roots.SelectMany(r => r.Leaves()).OfType<SoftHyphensPlaceNode>().Where(p => p.Check == true).Select(p => p.Place).ToList();

    private static HashSet<(string BookPath, string Language)> CheckedFiles(IEnumerable<SoftHyphensNode> roots) =>
        roots.SelectMany(r => r.Leaves()).OfType<SoftHyphensFileNode>()
            .Where(f => f.HasCheckBox && f.Check == true)
            .Select(f => (f.File.BookPath, f.Language.Key))
            .ToHashSet();

    // (Re)builds the trees from the survey and the installed patterns.
    private void Build()
    {
        BuildAdding();
        BuildRemoving();
        BuildConverting();
        OnPropertyChanged(nameof(HasLanguagesWithoutPatterns));
        OnPropertyChanged(nameof(IsEmpty));
        OnChecksChanged();
    }

    private void BuildAdding()
    {
        AddRoots.Clear();
        _patterns.Clear();
        foreach (SoftHyphenLanguage language in _survey.Languages)
        {
            HyphenationMatch? match = null;
            string? unreadable = null;
            bool canCheck = false;
            if (!language.IsUnknown)
            {
                match = _patternFiles.Match(language.Language);
                if (match.File is { } file)
                {
                    try
                    {
                        _patterns[language.Key] = HyphenationPatternFiles.Patterns(file);
                        canCheck = true;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        unreadable = e.Message;
                    }
                }
            }

            SoftHyphensLanguageNode node = new(this, SoftHyphensMode.Add, language, match, unreadable, canCheck);
            foreach (SoftHyphenLanguageInFile file in language.Files)
            {
                node.Add(new SoftHyphensFileNode(this, SoftHyphensMode.Add, language, file, canCheck));
            }

            AddRoots.Add(node);
        }
    }

    private void BuildRemoving() => BuildSoftHyphenTree(RemoveRoots, SoftHyphensMode.Remove, (_, _) => true, _ => true);

    // Converting: only what is not written in the chosen form yet.
    private void BuildConverting() => BuildSoftHyphenTree(
        ConvertRoots, SoftHyphensMode.Convert, (language, file) => file.SoftHyphensNotIn(Form) > 0, place => !place.IsIn(Form));

    // The tree of the soft hyphens there are (removing, converting): languages → files, and the places outside the text.
    private void BuildSoftHyphenTree(
        ObservableCollection<SoftHyphensNode> roots,
        SoftHyphensMode tree,
        Func<SoftHyphenLanguage, SoftHyphenLanguageInFile, bool> takesFile,
        Func<SoftHyphenPlace, bool> takesPlace)
    {
        roots.Clear();
        foreach (SoftHyphenLanguage language in _survey.Languages)
        {
            List<SoftHyphenLanguageInFile> files = language.Files.Where(f => f.SoftHyphens > 0 && takesFile(language, f)).ToList();
            if (files.Count == 0)
            {
                continue;
            }

            SoftHyphensLanguageNode node = new(this, tree, language, null, null, hasCheckBox: true);
            foreach (SoftHyphenLanguageInFile file in files)
            {
                node.Add(new SoftHyphensFileNode(this, tree, language, file, hasCheckBox: true));
            }

            roots.Add(node);
        }

        List<SoftHyphenPlace> places = _survey.OutsideText.Where(takesPlace).ToList();
        if (places.Count > 0)
        {
            roots.Add(OutsideTextNode(tree, places));
        }
    }

    // "Outside the text": a group per kind of place (the named attributes one each, the rest of the attributes
    // together, the head, each element that is not prose, comments), each place under it.
    private SoftHyphensGroupNode OutsideTextNode(SoftHyphensMode tree, IReadOnlyList<SoftHyphenPlace> places)
    {
        SoftHyphensGroupNode outside = new(this, tree, Strings.Get("SoftHyphens_OutsideText"), Strings.Get("SoftHyphens_OutsideTextTip"))
        {
            IsExpanded = true,
        };
        foreach (IGrouping<string, SoftHyphenPlace> kind in places.GroupBy(KindTitle).OrderBy(g => KindOrder(g.First())))
        {
            SoftHyphensGroupNode group = new(this, tree, Strings.Format("SoftHyphens_KindCount", kind.Key, kind.Count()));
            foreach (SoftHyphenPlace place in kind)
            {
                string title = Strings.Format("SoftHyphens_PlaceTitle", place.BookPath, place.Line);
                if (place.Kind == SoftHyphenPlaceKind.Attribute && !NamedAttributes.Contains(place.Name, StringComparer.Ordinal))
                {
                    title += " (" + place.Name + ")";
                }

                group.Add(new SoftHyphensPlaceNode(this, tree, place, title));
            }

            outside.Add(group);
        }

        return outside;
    }

    private static string KindTitle(SoftHyphenPlace place) => place.Kind switch
    {
        SoftHyphenPlaceKind.Attribute when NamedAttributes.Contains(place.Name, StringComparer.Ordinal) =>
            Strings.Format("SoftHyphens_PlaceAttribute", place.Name),
        SoftHyphenPlaceKind.Attribute => Strings.Get("SoftHyphens_PlaceOtherAttributes"),
        SoftHyphenPlaceKind.Head => Strings.Get("SoftHyphens_PlaceHead"),
        SoftHyphenPlaceKind.Element => Strings.Format("SoftHyphens_PlaceElement", place.Name),
        _ => Strings.Get("SoftHyphens_PlaceComment"),
    };

    // The named attributes first (the faults among them on top), the other attributes, then the rest.
    private static int KindOrder(SoftHyphenPlace place) => place.Kind switch
    {
        SoftHyphenPlaceKind.Attribute => Array.IndexOf(NamedAttributes, place.Name) is var i and >= 0 ? i : NamedAttributes.Length,
        SoftHyphenPlaceKind.Head => 10,
        SoftHyphenPlaceKind.Element => 11,
        _ => 12,
    };
}
