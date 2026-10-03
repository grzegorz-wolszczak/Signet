using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>
/// Model of the "Rename Class" window opened from the Code View context menu: the new name with
/// live validation, the scope choice (only when started from an XHTML element), the change counter
/// and a description of what happens to the class definitions in each style source.
/// </summary>
public sealed partial class RenameClassViewModel : ObservableObject
{
    private readonly ClassRenamer _renamer;
    private readonly ClassAtCaret? _element;
    private readonly StyleClassAtCaret? _source;
    private readonly ClassRenameStats _everywhereStats;
    private readonly ClassRenameStats? _sameNestingStats;

    /// <summary>Rename started on a class in a <c>class</c> attribute — with a scope choice.</summary>
    public RenameClassViewModel(ClassRenamer renamer, ClassAtCaret target)
        : this(renamer, (target ?? throw new ArgumentNullException(nameof(target))).Name)
    {
        _element = target;
        _everywhereStats = renamer.Preview(ClassRenameRequest.ForElement(target, ClassRenameScope.Everywhere));
        _sameNestingStats = renamer.Preview(ClassRenameRequest.ForElement(target, ClassRenameScope.SameNesting));
    }

    /// <summary>Rename started in a stylesheet selector or a <c>&lt;style&gt;</c> block — the scope follows from the cascade.</summary>
    public RenameClassViewModel(ClassRenamer renamer, StyleClassAtCaret target)
        : this(renamer, (target ?? throw new ArgumentNullException(nameof(target))).Name)
    {
        _source = target;
        _everywhereStats = renamer.Preview(ClassRenameRequest.ForStyleSource(target));
    }

    private RenameClassViewModel(ClassRenamer renamer, string oldName)
    {
        _renamer = renamer ?? throw new ArgumentNullException(nameof(renamer));
        OldName = oldName;
        _newName = oldName;
        _everywhereStats = null!;
        UpdateValidation();
    }

    /// <summary>Current class name.</summary>
    public string OldName { get; }

    /// <summary>Class label at the top of the window (when started from a style source — including its name).</summary>
    public string ClassLabel => _source is { } source
        ? Strings.Format("RenameClassWindow_ClassInSource", OldName, SourceName(source.BookPath, source.StyleBlockIndex))
        : Strings.Format("RenameClassWindow_Class", OldName);

    /// <summary>Whether the window shows the scope choice (only when started from an XHTML element).</summary>
    public bool ShowsScopeChoice => _element is not null;

    /// <summary>Label of the "only with the same nesting" option, including the element chain.</summary>
    public string SameNestingLabel => _element is null
        ? string.Empty
        : Strings.Format("RenameClassWindow_SameNesting", string.Join(" > ", _element.ElementPath));

    /// <summary>The entered new name.</summary>
    [ObservableProperty]
    private string _newName;

    /// <summary><c>true</c> — only elements with the same nesting; <c>false</c> — the whole book.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEverywhere))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(SourceNotes))]
    private bool _isSameNesting;

    /// <summary>Inverse of <see cref="IsSameNesting"/> (for the "rename everywhere" radio button).</summary>
    public bool IsEverywhere
    {
        get => !IsSameNesting;
        set => IsSameNesting = !value;
    }

    /// <summary>Validation messages for the new name (one per line) or an empty string.</summary>
    [ObservableProperty]
    private string _validationError = string.Empty;

    /// <summary>Whether the dialog can be accepted — the new name is valid.</summary>
    [ObservableProperty]
    private bool _canAccept;

    /// <summary>The change for the selected scope.</summary>
    public ClassRenameRequest Request => _source is { } source
        ? ClassRenameRequest.ForStyleSource(source)
        : ClassRenameRequest.ForElement(_element!, IsSameNesting ? ClassRenameScope.SameNesting : ClassRenameScope.Everywhere);

    /// <summary>Change counter for the selected scope.</summary>
    public string Summary =>
        Strings.Format("RenameClassWindow_Summary", Stats.ChangedOccurrences, Stats.TotalOccurrences, Stats.ChangedFiles);

    /// <summary>
    /// What happens to the class definitions in each style source (renamed in place / copied under
    /// the new name / unchanged), preceded by a note when the changed elements use several sources.
    /// </summary>
    public IReadOnlyList<string> SourceNotes
    {
        get
        {
            ClassRenameStats stats = Stats;
            List<string> notes = new();
            if (stats.Sources.Count == 0)
            {
                notes.Add(Strings.Get("RenameClassWindow_NoDefinitions"));
                return notes;
            }

            if (stats.SourcesUsedByChangedElements > 1)
            {
                notes.Add(Strings.Format("RenameClassWindow_MultipleSources", stats.SourcesUsedByChangedElements));
            }

            foreach (ClassRenameSourceAction action in stats.Sources)
            {
                string key = action.Change switch
                {
                    ClassRenameSourceChange.RenamedInPlace => "RenameClassWindow_SourceRenamed",
                    ClassRenameSourceChange.Copied => "RenameClassWindow_SourceCopied",
                    _ => "RenameClassWindow_SourceUnchanged",
                };
                notes.Add(Strings.Format(key, SourceName(action.BookPath, action.StyleBlockIndex), action.Rules));
            }

            return notes;
        }
    }

    private ClassRenameStats Stats => IsSameNesting && _sameNestingStats is not null ? _sameNestingStats : _everywhereStats;

    /// <summary>Validation messages as a list (for tests).</summary>
    public IReadOnlyList<string> ValidationMessages =>
        ValidationError.Length == 0 ? Array.Empty<string>() : ValidationError.Split('\n');

    /// <summary>Computes the rename for the entered name and the selected scope (the name must be valid).</summary>
    public ClassRenameResult Rename() => _renamer.Rename(Request, NewName);

    partial void OnNewNameChanged(string value) => UpdateValidation();

    private static string SourceName(string bookPath, int styleBlockIndex) => styleBlockIndex < 0
        ? bookPath
        : Strings.Format("RenameClassWindow_StyleBlock", bookPath, styleBlockIndex + 1);

    private void UpdateValidation()
    {
        ClassNameValidation validation = _renamer.Validate(OldName, NewName);
        ValidationError = string.Join('\n', validation.Problems.Select(p => Message(p, validation)));
        CanAccept = validation.IsValid;
    }

    private string Message(ClassNameProblem problem, ClassNameValidation validation) => problem switch
    {
        ClassNameProblem.Empty => Strings.Get("RenameClass_Empty"),
        ClassNameProblem.ContainsWhitespace => Strings.Get("RenameClass_Whitespace"),
        ClassNameProblem.ContainsInvalidCharacters => Strings.Format(
            "RenameClass_InvalidCharacters", string.Join(" ", validation.InvalidCharacters.Select(c => $"'{c}'"))),
        ClassNameProblem.StartsWithDigit => Strings.Get("RenameClass_StartsWithDigit"),
        ClassNameProblem.StartsWithHyphenAndDigit => Strings.Get("RenameClass_StartsWithHyphenDigit"),
        ClassNameProblem.SingleHyphen => Strings.Get("RenameClass_SingleHyphen"),
        ClassNameProblem.SameAsOld => Strings.Get("RenameClass_SameAsOld"),
        ClassNameProblem.AlreadyExists => Strings.Format("RenameClass_AlreadyExists", NewName),
        _ => problem.ToString(),
    };
}
