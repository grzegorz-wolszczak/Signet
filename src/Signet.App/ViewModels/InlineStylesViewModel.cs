using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>
/// A row of the tree of the "Extract Inline Styles" dialog: a class that would be created, what stops working as before
/// with it, a file that uses it, or an element that would get it. The columns show <see cref="Title"/> and <see cref="Place"/>.
/// </summary>
public abstract partial class InlineStyleNode : ObservableObject
{
    private static readonly IReadOnlyList<InlineStyleNode> NoChildren = Array.Empty<InlineStyleNode>();

    /// <summary>Whether the row's children are shown.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>The text of the first column.</summary>
    public abstract string Title { get; }

    /// <summary>The text of the second column: the declarations of a class, the tag of an element, the explanation of a warning.</summary>
    public virtual string Place => string.Empty;

    /// <summary>Whether the row has a check box.</summary>
    public virtual bool HasCheckBox => false;

    /// <summary>The state of its check box.</summary>
    public virtual bool? Check
    {
        get => false;
        set { }
    }

    /// <summary>Whether the row is a warning, marked with a sign.</summary>
    public virtual bool IsWarning => false;

    /// <summary>The rows under it.</summary>
    public virtual IReadOnlyList<InlineStyleNode> Children => NoChildren;

    /// <summary>Whether it has rows under it (the expander).</summary>
    public virtual bool HasChildren => false;

    /// <summary>A double click: opens the place in the code.</summary>
    public virtual void Open()
    {
    }

    /// <summary>Notifies that <see cref="Check"/> changed.</summary>
    internal void RefreshCheck() => OnPropertyChanged(nameof(Check));
}

/// <summary>A class that would be created: "signet-cls-1 — 2814×", with its declarations.</summary>
public sealed partial class InlineStyleClassNode : InlineStyleNode
{
    private readonly InlineStylesViewModel _owner;
    private IReadOnlyList<InlineStyleNode>? _children;

    [ObservableProperty]
    private bool _isChecked;

    internal InlineStyleClassNode(InlineStylesViewModel owner, InlineStyleGroup group, string className)
    {
        _owner = owner;
        Group = group;
        ClassName = className;
        _isChecked = group.Consequences.Count == 0;
        Title = Strings.Format(
            "InlineStylesWindow_ClassTitle", className, group.Occurrences.Count.ToString(CultureInfo.CurrentCulture));
        Place = group.Summary;
    }

    /// <summary>The value of the attribute this class stands for.</summary>
    public InlineStyleGroup Group { get; }

    /// <summary>The name of the class.</summary>
    public string ClassName { get; }

    /// <inheritdoc />
    public override string Title { get; }

    /// <inheritdoc />
    public override string Place { get; }

    /// <inheritdoc />
    public override bool HasCheckBox => true;

    /// <inheritdoc />
    public override bool? Check
    {
        get => IsChecked;
        set => IsChecked = value == true;
    }

    /// <inheritdoc />
    public override bool HasChildren => true;

    /// <inheritdoc />
    public override IReadOnlyList<InlineStyleNode> Children => _children ??= Group.Consequences
        .Select(c => (InlineStyleNode)new InlineStyleWarningNode(_owner, c))
        .Concat(Group.Occurrences
            .GroupBy(o => o.BookPath, StringComparer.Ordinal)
            .Select(file => new InlineStyleFileNode(_owner, file.Key, file.ToList())))
        .ToList();

    /// <inheritdoc />
    public override void Open() => _owner.NavigateTo(Group.Occurrences[0].BookPath, Group.Occurrences[0].Offset);

    partial void OnIsCheckedChanged(bool value)
    {
        RefreshCheck();
        _owner.OnClassToggled();
    }
}

/// <summary>A rule of the stylesheets that the class would lose to.</summary>
public sealed class InlineStyleWarningNode : InlineStyleNode
{
    private readonly InlineStylesViewModel _owner;
    private readonly CleanupConsequence _consequence;

    internal InlineStyleWarningNode(InlineStylesViewModel owner, CleanupConsequence consequence)
    {
        _owner = owner;
        _consequence = consequence;
    }

    /// <inheritdoc />
    public override string Title => _consequence.Text;

    /// <inheritdoc />
    public override bool IsWarning => true;

    /// <inheritdoc />
    public override void Open() => _owner.NavigateTo(_consequence.BookPath, _consequence.Offset);
}

/// <summary>A file with elements that would get the class: "page1.xhtml — 120×".</summary>
public sealed class InlineStyleFileNode : InlineStyleNode
{
    private readonly InlineStylesViewModel _owner;
    private readonly IReadOnlyList<InlineStyleOccurrence> _occurrences;
    private IReadOnlyList<InlineStyleNode>? _children;

    internal InlineStyleFileNode(InlineStylesViewModel owner, string bookPath, IReadOnlyList<InlineStyleOccurrence> occurrences)
    {
        _owner = owner;
        _occurrences = occurrences;
        Title = Strings.Format("InlineStylesWindow_FileTitle", bookPath, occurrences.Count.ToString(CultureInfo.CurrentCulture));
    }

    /// <inheritdoc />
    public override string Title { get; }

    /// <inheritdoc />
    public override bool HasChildren => true;

    /// <inheritdoc />
    public override IReadOnlyList<InlineStyleNode> Children => _children ??= _occurrences
        .Select(o => (InlineStyleNode)new InlineStyleElementNode(_owner, o))
        .ToList();

    /// <inheritdoc />
    public override void Open() => _owner.NavigateTo(_occurrences[0].BookPath, _occurrences[0].Offset);
}

/// <summary>An element that would get the class: "&lt;p&gt; — line 42" and its opening tag.</summary>
public sealed class InlineStyleElementNode : InlineStyleNode
{
    private readonly InlineStylesViewModel _owner;
    private readonly InlineStyleOccurrence _occurrence;

    internal InlineStyleElementNode(InlineStylesViewModel owner, InlineStyleOccurrence occurrence)
    {
        _owner = owner;
        _occurrence = occurrence;
        Title = Strings.Format("InlineStylesWindow_ElementTitle", occurrence.TagName, occurrence.Line.ToString(CultureInfo.CurrentCulture));
    }

    /// <inheritdoc />
    public override string Title { get; }

    /// <inheritdoc />
    public override string Place => _occurrence.Snippet;

    /// <inheritdoc />
    public override void Open() => _owner.NavigateTo(_occurrence.BookPath, _occurrence.Offset);
}

/// <summary>
/// The "Extract Inline Styles" dialog: the values of the <c>style</c> attribute that several elements of the book share,
/// as a tree of the classes they would become (with the files and elements they are taken from, and the rules a class
/// would lose to), each class with a check box. "Extract" turns the checked ones into classes of the stylesheet. Classes
/// that something would override are unchecked at the start.
/// </summary>
public sealed partial class InlineStylesViewModel : ObservableObject
{
    /// <summary>The fewest elements a value must be on to become a class.</summary>
    public const int MinimumOfMinOccurrences = 1;

    /// <summary>The default for <see cref="MinOccurrences"/>: only values that repeat.</summary>
    public const int DefaultMinOccurrences = 2;

    private readonly InlineStyleSurvey _survey;
    private readonly Action<string, int> _navigate;
    private readonly Func<InlineStyleExtractionPlan, Task<bool>> _apply;
    private bool _updating;
    private bool _isBookOperationBusy;

    /// <summary>Creates the model.</summary>
    /// <param name="survey">The values found in the book (<see cref="InlineStyleExtraction.Survey"/>), with a stylesheet.</param>
    /// <param name="navigate">Opens a file (book path) at an offset in the main window (a negative offset = the file).</param>
    /// <param name="apply">Extracts the classes of the plan; returns whether it was done.</param>
    public InlineStylesViewModel(
        InlineStyleSurvey survey, Action<string, int> navigate, Func<InlineStyleExtractionPlan, Task<bool>> apply)
    {
        _survey = survey ?? throw new ArgumentNullException(nameof(survey));
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        if (survey.StylesheetBookPath is null)
        {
            throw new ArgumentException("The survey has no stylesheet.", nameof(survey));
        }

        ExtractCommand = new AsyncRelayCommand(ExtractAsync, () => CanExtract);
        SelectAllCommand = new RelayCommand(() => SetChecked(Classes, true));
        SelectNoneCommand = new RelayCommand(() => SetChecked(Classes, false));
        Build();
    }

    /// <summary>The fewest elements a value must be on to become a class; changing it builds the tree again.</summary>
    [ObservableProperty]
    private int _minOccurrences = DefaultMinOccurrences;

    /// <summary>The classes (the roots of the tree).</summary>
    public ObservableCollection<InlineStyleClassNode> Roots { get; } = new();

    /// <summary>The classes in the tree.</summary>
    public IReadOnlyList<InlineStyleClassNode> Classes { get; private set; } = Array.Empty<InlineStyleClassNode>();

    /// <summary>Whether some value is on enough elements.</summary>
    public bool HasItems => Classes.Count > 0;

    /// <summary>"The classes go to OEBPS/style.css".</summary>
    public string StylesheetText => Strings.Format("InlineStylesWindow_Stylesheet", _survey.StylesheetBookPath!);

    /// <summary>Whether some elements are in files that do not link the stylesheet.</summary>
    public bool HasOutside => _survey.ElementsOutsideStylesheet > 0;

    /// <summary>"12 elements in files that do not link the stylesheet are left alone".</summary>
    public string OutsideText => Strings.Format(
        "InlineStylesWindow_Outside", _survey.ElementsOutsideStylesheet.ToString(CultureInfo.CurrentCulture));

    /// <summary>"3 of 5 classes checked (120 elements)".</summary>
    public string Summary => Strings.Format(
        "InlineStylesWindow_Summary",
        Classes.Count(c => c.IsChecked).ToString(CultureInfo.CurrentCulture),
        Classes.Count.ToString(CultureInfo.CurrentCulture),
        Classes.Where(c => c.IsChecked).Sum(c => c.Group.Occurrences.Count).ToString(CultureInfo.CurrentCulture));

    /// <summary>Whether a checked class can be extracted.</summary>
    public bool CanExtract => Classes.Any(c => c.IsChecked) && !IsBookOperationBusy;

    /// <summary>
    /// Whether another operation on the book is running elsewhere (set by the host window from
    /// <c>MainWindowViewModel.IsBookOperationBusy</c>) — disables <see cref="ExtractCommand"/> until it finishes.
    /// </summary>
    public bool IsBookOperationBusy
    {
        get => _isBookOperationBusy;
        set
        {
            if (SetProperty(ref _isBookOperationBusy, value))
            {
                OnPropertyChanged(nameof(CanExtract));
                ExtractCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>"Extract" — turns the checked classes into classes of the stylesheet and closes the dialog (<see cref="Done"/>).</summary>
    public IAsyncRelayCommand ExtractCommand { get; }

    /// <summary>Checks every class.</summary>
    public IRelayCommand SelectAllCommand { get; }

    /// <summary>Unchecks every class.</summary>
    public IRelayCommand SelectNoneCommand { get; }

    /// <summary>Raised when the classes were extracted (the dialog closes).</summary>
    public event EventHandler? Done;

    /// <summary>The plan for the checked classes.</summary>
    public InlineStyleExtractionPlan CreatePlan() => new(
        _survey.StylesheetBookPath!,
        Classes.Where(c => c.IsChecked).ToDictionary(c => c.Group.Key, c => c.ClassName, StringComparer.Ordinal));

    /// <summary>Opens <paramref name="bookPath"/> at <paramref name="offset"/> (a negative offset = only the file).</summary>
    internal void NavigateTo(string bookPath, int offset) => _navigate(bookPath, offset);

    /// <summary>Updates the summary after a class was (un)checked.</summary>
    internal void OnClassToggled()
    {
        if (_updating)
        {
            return;
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanExtract));
        ExtractCommand.NotifyCanExecuteChanged();
    }

    partial void OnMinOccurrencesChanged(int value)
    {
        if (value < MinimumOfMinOccurrences)
        {
            MinOccurrences = MinimumOfMinOccurrences;
            return;
        }

        Build();
    }

    private void SetChecked(IEnumerable<InlineStyleClassNode> classes, bool isChecked)
    {
        _updating = true;
        try
        {
            foreach (InlineStyleClassNode node in classes)
            {
                node.IsChecked = isChecked;
            }
        }
        finally
        {
            _updating = false;
        }

        OnClassToggled();
    }

    // The tree for the chosen minimum: a class for every value on enough elements, numbered in the order of the survey.
    private void Build()
    {
        Classes = _survey.Groups
            .Where(g => g.Occurrences.Count >= MinOccurrences)
            .Select((group, i) => new InlineStyleClassNode(this, group, InlineStyleExtraction.ClassName(_survey.FirstFreeNumber + i)))
            .ToList();
        Roots.Clear();
        foreach (InlineStyleClassNode node in Classes)
        {
            Roots.Add(node);
        }

        OnPropertyChanged(nameof(Classes));
        OnPropertyChanged(nameof(HasItems));
        OnClassToggled();
    }

    private async Task ExtractAsync()
    {
        if (await _apply(CreatePlan()).ConfigureAwait(true))
        {
            Done?.Invoke(this, EventArgs.Empty);
        }
    }
}
