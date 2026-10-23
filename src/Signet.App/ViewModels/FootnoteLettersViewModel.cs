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
/// A row of the tree of the "Unify Letters of Shared Footnotes" dialog: a file, a shared note whose letters change, or
/// one of its references or of the links to them. The columns show <see cref="Title"/> and <see cref="Place"/>.
/// </summary>
public abstract partial class FootnoteLetterNode : ObservableObject
{
    private static readonly IReadOnlyList<FootnoteLetterNode> NoChildren = Array.Empty<FootnoteLetterNode>();

    /// <summary>Whether the row's children are shown.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>The text of the first column.</summary>
    public abstract string Title { get; }

    /// <summary>The text around the place (a reference or a link), or the start of the note.</summary>
    public virtual string Place => string.Empty;

    /// <summary>Whether the row has a check box.</summary>
    public virtual bool HasCheckBox => false;

    /// <summary>The state of its check box; <c>null</c> = some of the notes under it are checked.</summary>
    public virtual bool? Check
    {
        get => false;
        set { }
    }

    /// <summary>Whether the row is for information only (a link that is redirected), marked ℹ.</summary>
    public virtual bool IsNotice => false;

    /// <summary>The rows under it.</summary>
    public virtual IReadOnlyList<FootnoteLetterNode> Children => NoChildren;

    /// <summary>Whether it has rows under it (the expander).</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>A double click: opens the place in the code.</summary>
    public virtual void Open()
    {
    }

    /// <summary>Notifies that <see cref="Check"/> changed.</summary>
    internal void RefreshCheck() => OnPropertyChanged(nameof(Check));
}

/// <summary>A file with shared notes whose letters change.</summary>
public sealed class FootnoteLetterFileNode : FootnoteLetterNode
{
    private readonly FootnoteLettersViewModel _owner;
    private readonly IReadOnlyList<FootnoteLetterNoteNode> _notes;

    internal FootnoteLetterFileNode(FootnoteLettersViewModel owner, HtmlResource file, IReadOnlyList<FootnoteLetterNoteNode> notes)
    {
        _owner = owner;
        File = file;
        _notes = notes;
        foreach (FootnoteLetterNoteNode note in notes)
        {
            note.Parent = this;
        }

        Title = Strings.Format("FootnoteLettersWindow_FileTitle", file.Filename, notes.Count.ToString(CultureInfo.CurrentCulture));
        IsExpanded = true;
    }

    /// <summary>The file of the first references of the notes.</summary>
    public HtmlResource File { get; }

    /// <inheritdoc />
    public override string Title { get; }

    /// <inheritdoc />
    public override bool HasCheckBox => true;

    /// <inheritdoc />
    public override bool? Check
    {
        get => _notes.All(n => n.IsChecked) ? true : _notes.Any(n => n.IsChecked) ? null : false;
        set => _owner.SetChecked(_notes, value == true);
    }

    /// <inheritdoc />
    public override IReadOnlyList<FootnoteLetterNode> Children => _notes;

    /// <inheritdoc />
    public override void Open() => _owner.NavigateTo(File, -1);
}

/// <summary>A shared note whose letters change: "[4] i, ii → a, b".</summary>
public sealed partial class FootnoteLetterNoteNode : FootnoteLetterNode
{
    // How much of the note the row shows.
    private const int NoteLength = 120;

    private readonly FootnoteLettersViewModel _owner;

    [ObservableProperty]
    private bool _isChecked = true;

    internal FootnoteLetterNoteNode(FootnoteLettersViewModel owner, LetterItem item, FootnoteLetterStyle style)
    {
        _owner = owner;
        Item = item;
        string number = item.Number.ToString(CultureInfo.InvariantCulture);
        IEnumerable<string> Letters(FootnoteLetterStyle letters) =>
            item.References.Select((_, i) => FootnoteLetters.Of(letters, i));
        Title = Strings.Format(
            "FootnoteLettersWindow_NoteTitle", number, string.Join(", ", Letters(item.Current)), string.Join(", ", Letters(style)));
        string text = FootnoteEditor.GetContent(item.Footnote)?.Text.Replace("\n\n", " ", StringComparison.Ordinal) ?? string.Empty;
        Place = text.Length > NoteLength ? text[..NoteLength] + "…" : text;
        Children = item.References
            .Select((reference, i) => (FootnoteLetterNode)new FootnoteLetterReferenceNode(
                owner, reference, "[" + number + FootnoteLetters.Of(item.Current, i) + "]", "[" + number + FootnoteLetters.Of(style, i) + "]"))
            .Concat(item.Links.Select(l => new FootnoteLetterLinkNode(owner, l)))
            .ToArray();
    }

    /// <summary>The note in the plan.</summary>
    public LetterItem Item { get; }

    /// <summary>The file row it is under.</summary>
    internal FootnoteLetterFileNode? Parent { get; set; }

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
    public override IReadOnlyList<FootnoteLetterNode> Children { get; }

    /// <inheritdoc />
    public override void Open() => _owner.Navigate(Item.Footnote, FootnoteSide.Note);

    partial void OnIsCheckedChanged(bool value)
    {
        RefreshCheck();
        Parent?.RefreshCheck();
        _owner.OnNoteToggled();
    }
}

/// <summary>A reference of a shared note: "[4ii] → [4b]" and the text around it.</summary>
public sealed class FootnoteLetterReferenceNode : FootnoteLetterNode
{
    private readonly FootnoteLettersViewModel _owner;
    private readonly Footnote _reference;
    private readonly string _marker;
    private string? _place;

    internal FootnoteLetterReferenceNode(FootnoteLettersViewModel owner, Footnote reference, string current, string next)
    {
        _owner = owner;
        _reference = reference;
        _marker = current;
        Title = Strings.Format("FootnoteLettersWindow_ReferenceTitle", current, next);
    }

    /// <inheritdoc />
    public override string Title { get; }

    /// <inheritdoc />
    public override string Place => _place ??= FootnoteEditor.LocateReferenceRange(_reference) is { } range
        ? Around(range.File.GetText(), range.Start, range.End, _marker)
        : string.Empty;

    /// <inheritdoc />
    public override void Open() => _owner.Navigate(_reference, FootnoteSide.Reference);

    // "…before[4ii] after…": the text around start..end with `marker` in its place.
    internal static string Around(string text, int start, int end, string marker)
    {
        (string before, string after) = FootnoteViewModel.Context(text, start, end);
        return before + marker + after;
    }
}

/// <summary>A link from outside the note to the id of one of its references: it is redirected to the new id.</summary>
public sealed class FootnoteLetterLinkNode : FootnoteLetterNode
{
    private readonly FootnoteLettersViewModel _owner;
    private readonly ConversionLink _link;
    private string? _place;

    internal FootnoteLetterLinkNode(FootnoteLettersViewModel owner, ConversionLink link)
    {
        _owner = owner;
        _link = link;
        Title = Strings.Format("FootnoteLettersWindow_LinkTitle", link.File.Filename);
    }

    /// <inheritdoc />
    public override string Title { get; }

    /// <inheritdoc />
    public override bool IsNotice => true;

    /// <inheritdoc />
    public override string Place => _place ??= _link.Offset >= 0 && _link.File is HtmlResource html
        ? FootnoteLetterReferenceNode.Around(html.GetText(), _link.Offset, LinkEnd(html.GetText(), _link.Offset), "[…]")
        : _link.File.BookPath;

    /// <inheritdoc />
    public override void Open() => _owner.NavigateTo(_link.File, _link.Offset);

    private static int LinkEnd(string text, int start)
    {
        int end = text.IndexOf("</a>", start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? start : end + 4;
    }
}

/// <summary>
/// The "Unify Letters of Shared Footnotes" dialog: the shared Signet notes whose letters are not written in the chosen
/// style (the style of Preferences at the start), as a tree of files, notes and their references and links, with what
/// changes in the book. "Unify" rewrites the checked notes.
/// </summary>
public sealed partial class FootnoteLettersViewModel : ObservableObject
{
    private readonly Func<FootnoteLetterStyle, IReadOnlyList<LetterItem>> _plan;
    private readonly Action<string, int> _navigate;
    private readonly Func<IReadOnlyList<LetterItem>, FootnoteLetterStyle, Task<bool>> _apply;
    private bool _updating;

    /// <summary>Creates the model.</summary>
    /// <param name="style">The style chosen at the start (the one of Preferences).</param>
    /// <param name="plan">The notes whose letters are not in a style (<see cref="FootnoteEditor.PlanLetters"/>).</param>
    /// <param name="navigate">Opens a file (book path) at an offset in the main window (a negative offset = the file).</param>
    /// <param name="apply">Rewrites the letters of the notes in the style; returns whether it was done.</param>
    public FootnoteLettersViewModel(
        FootnoteLetterStyle style,
        Func<FootnoteLetterStyle, IReadOnlyList<LetterItem>> plan,
        Action<string, int> navigate,
        Func<IReadOnlyList<LetterItem>, FootnoteLetterStyle, Task<bool>> apply)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _style = style;
        UnifyCommand = new AsyncRelayCommand(UnifyAsync, () => CanUnify);
        SelectAllCommand = new RelayCommand(() => SetChecked(Notes, true));
        SelectNoneCommand = new RelayCommand(() => SetChecked(Notes, false));
        Build();
    }

    /// <summary>The styles to choose from.</summary>
    public static IReadOnlyList<FootnoteLetterStyle> Styles { get; } = Enum.GetValues<FootnoteLetterStyle>();

    /// <summary>The style the letters are written in.</summary>
    [ObservableProperty]
    private FootnoteLetterStyle _style;

    /// <summary>The files with the notes whose letters change (the roots of the tree).</summary>
    public ObservableCollection<FootnoteLetterFileNode> Roots { get; } = new();

    /// <summary>The notes whose letters change.</summary>
    public IReadOnlyList<FootnoteLetterNoteNode> Notes { get; private set; } = Array.Empty<FootnoteLetterNoteNode>();

    /// <summary>Whether some note is not in the style.</summary>
    public bool HasItems => Notes.Count > 0;

    /// <summary>"3 of 5 footnotes checked".</summary>
    public string Summary => Strings.Format(
        "FootnoteLettersWindow_Summary",
        Notes.Count(n => n.IsChecked).ToString(CultureInfo.CurrentCulture), Notes.Count.ToString(CultureInfo.CurrentCulture));

    /// <summary>Whether a checked note can be rewritten.</summary>
    public bool CanUnify => Notes.Any(n => n.IsChecked) && !IsBookOperationBusy;

    private bool _isBookOperationBusy;

    /// <summary>
    /// Whether another operation on the book is running elsewhere (set by the host window from
    /// <c>MainWindowViewModel.IsBookOperationBusy</c>) — disables <see cref="UnifyCommand"/> until it finishes.
    /// </summary>
    public bool IsBookOperationBusy
    {
        get => _isBookOperationBusy;
        set
        {
            if (SetProperty(ref _isBookOperationBusy, value))
            {
                OnPropertyChanged(nameof(CanUnify));
                UnifyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>"Unify" — rewrites the checked notes and closes the dialog (<see cref="Done"/>).</summary>
    public IAsyncRelayCommand UnifyCommand { get; }

    /// <summary>Checks every note.</summary>
    public IRelayCommand SelectAllCommand { get; }

    /// <summary>Unchecks every note.</summary>
    public IRelayCommand SelectNoneCommand { get; }

    /// <summary>Raised when the letters were rewritten (the dialog closes).</summary>
    public event EventHandler? Done;

    /// <summary>Opens the file of the reference or of the note of <paramref name="footnote"/>.</summary>
    internal void Navigate(Footnote footnote, FootnoteSide side)
    {
        if (FootnoteEditor.Locate(footnote, side) is { } place)
        {
            _navigate(place.File.BookPath, place.Offset);
        }
    }

    /// <summary>Opens <paramref name="file"/> at <paramref name="offset"/> (a negative offset = only the file).</summary>
    internal void NavigateTo(Resource file, int offset) => _navigate(file.BookPath, offset);

    /// <summary>Checks or unchecks <paramref name="notes"/>.</summary>
    internal void SetChecked(IEnumerable<FootnoteLetterNoteNode> notes, bool isChecked)
    {
        _updating = true;
        try
        {
            foreach (FootnoteLetterNoteNode note in notes)
            {
                note.IsChecked = isChecked;
            }
        }
        finally
        {
            _updating = false;
        }

        OnNoteToggled();
    }

    /// <summary>Updates the summary and the files' check boxes after a note was (un)checked.</summary>
    internal void OnNoteToggled()
    {
        if (_updating)
        {
            return;
        }

        foreach (FootnoteLetterFileNode file in Roots)
        {
            file.RefreshCheck();
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanUnify));
        UnifyCommand.NotifyCanExecuteChanged();
    }

    partial void OnStyleChanged(FootnoteLetterStyle value) => Build();

    // The tree for the chosen style: the notes under the files of their first references.
    private void Build()
    {
        Notes = _plan(Style).Select(item => new FootnoteLetterNoteNode(this, item, Style)).ToList();
        Roots.Clear();
        foreach (IGrouping<HtmlResource, FootnoteLetterNoteNode> file in Notes.GroupBy(n => n.Item.Footnote.ReferenceFile))
        {
            Roots.Add(new FootnoteLetterFileNode(this, file.Key, file.ToList()));
        }

        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(HasItems));
        OnNoteToggled();
    }

    private async Task UnifyAsync()
    {
        if (await _apply(Notes.Where(n => n.IsChecked).Select(n => n.Item).ToList(), Style).ConfigureAwait(true))
        {
            Done?.Invoke(this, EventArgs.Empty);
        }
    }
}
