using System.Collections.Generic;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.BookManipulation;
using Signet.Core.Hyphenation;

namespace Signet.App.ViewModels;

/// <summary>
/// A row of the tree of the "Add / Remove Soft Hyphens" dialog: a language with its files, the files themselves, and —
/// when removing — the soft hyphens outside the text, grouped by where they stand, one by one. A row with rows under it
/// has a check box that follows them (<c>null</c> = some are checked); a row without has its own.
/// </summary>
public abstract partial class SoftHyphensNode : ObservableObject
{
    private bool _isChecked;

    /// <summary>Creates a row.</summary>
    protected SoftHyphensNode(SoftHyphensViewModel owner, SoftHyphensMode tree, string title, bool hasCheckBox)
    {
        Owner = owner;
        Tree = tree;
        Title = title;
        HasCheckBox = hasCheckBox;
        _isChecked = hasCheckBox;
    }

    /// <summary>The dialog.</summary>
    protected SoftHyphensViewModel Owner { get; }

    /// <summary>The tree the row is in (adding, removing or converting).</summary>
    protected SoftHyphensMode Tree { get; }

    /// <summary>Whether the row is in the tree of adding.</summary>
    protected bool ForAdding => Tree == SoftHyphensMode.Add;

    /// <summary>The row above, or <c>null</c> for a row at the top.</summary>
    public SoftHyphensNode? Parent { get; private set; }

    /// <summary>The text of the first column.</summary>
    public string Title { get; }

    /// <summary>The tip of the first column (the full name, a reason).</summary>
    public virtual string? TitleTip => null;

    /// <summary>Whether the row can be checked (a language without patterns cannot be hyphenated, its files neither).</summary>
    public bool HasCheckBox { get; }

    /// <summary>The patterns column: which patterns a language is hyphenated with, or why it is not.</summary>
    public virtual string Patterns => string.Empty;

    /// <summary>The tip of the patterns column.</summary>
    public virtual string? PatternsTip => null;

    /// <summary>Whether the patterns column warns (patterns of another variant, none at all).</summary>
    public virtual bool PatternsWarn => false;

    /// <summary>The source column: where the book says the language.</summary>
    public virtual string Source => string.Empty;

    /// <summary>The count column: the words to hyphenate (adding) or the soft hyphens (removing).</summary>
    public virtual string Count => string.Empty;

    /// <summary>The line around a soft hyphen outside the text.</summary>
    public virtual string Context => string.Empty;

    /// <summary>The rows under it.</summary>
    public ObservableCollection<SoftHyphensNode> Children { get; } = new();

    /// <summary>Whether it has rows under it.</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>Whether the rows under it are shown.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// The state of its check box: its own for a row without rows under it; for one with rows, <c>true</c> when all
    /// the rows under it that can be checked are, <c>false</c> when none, <c>null</c> when some. Setting it checks or
    /// unchecks all of them.
    /// </summary>
    public bool? Check
    {
        get
        {
            if (!HasCheckBox)
            {
                return false;
            }

            if (!HasChildren)
            {
                return _isChecked;
            }

            List<bool?> states = Children.Where(c => c.HasCheckBox).Select(c => c.Check).ToList();
            return states.Count == 0 || states.All(s => s == false) ? false
                : states.All(s => s == true) ? true
                : null;
        }

        set
        {
            if (!HasCheckBox)
            {
                return;
            }

            SetCheck(value == true);
            for (SoftHyphensNode? node = Parent; node is not null; node = node.Parent)
            {
                node.OnPropertyChanged(nameof(Check));
            }

            Owner.OnChecksChanged();
        }
    }

    /// <summary>Opens the place of the row in the code (a double click); <c>false</c> = the row has no place.</summary>
    public virtual bool Navigate() => false;

    /// <summary>Announces that the count column changed (the shortest hyphenated word, or the form, did).</summary>
    internal void RefreshCount()
    {
        OnPropertyChanged(nameof(Count));
        foreach (SoftHyphensNode child in Children)
        {
            child.RefreshCount();
        }
    }

    /// <summary>Adds a row under it.</summary>
    internal void Add(SoftHyphensNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    /// <summary>The rows without rows under them, this one included when it has none.</summary>
    internal IEnumerable<SoftHyphensNode> Leaves() =>
        HasChildren ? Children.SelectMany(c => c.Leaves()) : new[] { this };

    // Checks or unchecks this row and every row under it that can be.
    private void SetCheck(bool value)
    {
        _isChecked = value;
        foreach (SoftHyphensNode child in Children.Where(c => c.HasCheckBox))
        {
            child.SetCheck(value);
        }

        OnPropertyChanged(nameof(Check));
    }
}

/// <summary>A language of the book, with the files its text is in.</summary>
public sealed class SoftHyphensLanguageNode : SoftHyphensNode
{
    internal SoftHyphensLanguageNode(
        SoftHyphensViewModel owner, SoftHyphensMode tree, SoftHyphenLanguage language, HyphenationMatch? match, string? unreadable, bool hasCheckBox)
        : base(owner, tree, language.IsUnknown ? Strings.Get("SoftHyphens_UnknownLanguage") : language.Language, hasCheckBox)
    {
        Language = language;
        Match = match;
        Unreadable = unreadable;
    }

    /// <summary>The language.</summary>
    public SoftHyphenLanguage Language { get; }

    /// <summary>The patterns it is hyphenated with (adding only).</summary>
    public HyphenationMatch? Match { get; }

    // Why the file of the patterns could not be read, or null.
    private string? Unreadable { get; }

    /// <inheritdoc />
    public override string? TitleTip => Language.IsUnknown ? Strings.Get("SoftHyphens_UnknownLanguageReason") : null;

    /// <inheritdoc />
    public override string Patterns => ForAdding ? PatternsText() : string.Empty;

    /// <inheritdoc />
    public override string? PatternsTip => !ForAdding ? null
        : Language.IsUnknown ? Strings.Get("SoftHyphens_UnknownLanguageReason")
        : Unreadable is not null ? Strings.Format("SoftHyphens_PatternsUnreadableTip", Match!.File!.FileName, Unreadable)
        : Match is not { CanHyphenate: true } ? Strings.Format("SoftHyphens_PatternsMissingTip", Match!.ExpectedFileName)
        : Match.IsFallback ? Strings.Format("SoftHyphens_PatternsFallbackTip", Match.File!.Language, Language.Language)
        : Match.File!.Path;

    /// <inheritdoc />
    public override bool PatternsWarn => ForAdding && (Match is not { CanHyphenate: true } || Match.IsFallback || Unreadable is not null);

    /// <inheritdoc />
    public override string Source => SoftHyphensViewModel.SourcesText(Language.Sources);

    /// <inheritdoc />
    public override string Count => (Tree switch
    {
        SoftHyphensMode.Add => Language.WordsToHyphenate(Owner.MinWordLength),
        SoftHyphensMode.Convert => Language.SoftHyphensNotIn(Owner.Form),
        _ => Language.SoftHyphens,
    }).ToString(CultureInfo.CurrentCulture);

    private string PatternsText()
    {
        if (Language.IsUnknown)
        {
            return Strings.Get("SoftHyphens_PatternsNone");
        }

        if (Unreadable is not null)
        {
            return Strings.Format("SoftHyphens_PatternsUnreadable", Match!.File!.FileName);
        }

        return Match switch
        {
            { CanHyphenate: false } missing => Strings.Format("SoftHyphens_PatternsMissing", missing.ExpectedFileName),
            { IsFallback: true } fallback => Strings.Format("SoftHyphens_PatternsFallback", fallback.ExpectedFileName, fallback.File!.FileName),
            { } found => found.File!.FileName,
            null => string.Empty,
        };
    }
}

/// <summary>A file with text in a language.</summary>
public sealed class SoftHyphensFileNode : SoftHyphensNode
{
    internal SoftHyphensFileNode(
        SoftHyphensViewModel owner, SoftHyphensMode tree, SoftHyphenLanguage language, SoftHyphenLanguageInFile file, bool hasCheckBox)
        : base(owner, tree, file.BookPath, hasCheckBox)
    {
        Language = language;
        File = file;
    }

    /// <summary>The language.</summary>
    public SoftHyphenLanguage Language { get; }

    /// <summary>The language in the file.</summary>
    public SoftHyphenLanguageInFile File { get; }

    /// <inheritdoc />
    public override string Source => SoftHyphensViewModel.SourcesText(File.Sources.Select(s => (s, 1)).ToList(), withFiles: false);

    /// <inheritdoc />
    public override string Count => (Tree switch
    {
        SoftHyphensMode.Add => File.WordsToHyphenate(Owner.MinWordLength),
        SoftHyphensMode.Convert => File.SoftHyphensNotIn(Owner.Form),
        _ => File.SoftHyphens,
    }).ToString(CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public override bool Navigate()
    {
        Owner.NavigateTo(File.BookPath, 0);
        return true;
    }
}

/// <summary>A group of rows that only gathers others: "Outside the text", or one kind of place in it.</summary>
public sealed class SoftHyphensGroupNode : SoftHyphensNode
{
    internal SoftHyphensGroupNode(SoftHyphensViewModel owner, SoftHyphensMode tree, string title, string? tip = null)
        : base(owner, tree, title, hasCheckBox: true)
    {
        TitleTipText = tip;
    }

    private string? TitleTipText { get; }

    /// <inheritdoc />
    public override string? TitleTip => TitleTipText;

    /// <inheritdoc />
    public override string Count => Leaves().Count().ToString(CultureInfo.CurrentCulture);
}

/// <summary>A soft hyphen outside the text of any language.</summary>
public sealed class SoftHyphensPlaceNode : SoftHyphensNode
{
    internal SoftHyphensPlaceNode(SoftHyphensViewModel owner, SoftHyphensMode tree, SoftHyphenPlace place, string title)
        : base(owner, tree, title, hasCheckBox: true)
    {
        Place = place;
    }

    /// <summary>The place.</summary>
    public SoftHyphenPlace Place { get; }

    /// <inheritdoc />
    public override string? TitleTip => Strings.Get("SoftHyphens_PlaceTip");

    /// <inheritdoc />
    public override string Context => Place.Context;

    /// <inheritdoc />
    public override bool Navigate()
    {
        Owner.NavigateTo(Place.BookPath, Place.Offset);
        return true;
    }
}
