using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Signet.App.ViewModels;

/// <summary>A section of the Preferences tree: a page of settings, possibly with subsections.</summary>
public sealed partial class PreferencesSectionNode : ObservableObject
{
    /// <summary>Creates a section showing the page <paramref name="pageIndex"/>.</summary>
    /// <param name="title">The name in the tree.</param>
    /// <param name="pageIndex">The page of the window the section shows.</param>
    /// <param name="parent">The enclosing section; <c>null</c> for a top-level one.</param>
    /// <param name="id">A name of the section that does not change with the language or the order of the pages (the
    /// <c>x:Name</c> of its page); empty when the page has none.</param>
    public PreferencesSectionNode(string title, int pageIndex, PreferencesSectionNode? parent = null, string id = "")
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        PageIndex = pageIndex;
        Parent = parent;
        Id = id ?? string.Empty;
        parent?.Children.Add(this);
    }

    /// <summary>The name in the tree.</summary>
    public string Title { get; }

    /// <summary>The page of the window the section shows.</summary>
    public int PageIndex { get; }

    /// <summary>A name that stays the same in every language and when pages are added (empty when unknown).</summary>
    public string Id { get; }

    /// <summary>The enclosing section; <c>null</c> for a top-level one.</summary>
    public PreferencesSectionNode? Parent { get; }

    /// <summary>The subsections.</summary>
    public ObservableCollection<PreferencesSectionNode> Children { get; } = new();

    /// <summary>Whether the section is top-level (shown in bold).</summary>
    public bool IsTopLevel => Parent is null;

    /// <summary>Whether the section has subsections (it can be expanded).</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>The names from the top-level section down to this one, e.g. <c>Appearance › Code Editor</c>.</summary>
    public string Path => Parent is null ? Title : Parent.Path + " › " + Title;

    /// <summary>Whether the subsections are shown.</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Whether the section passes the search filter.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>The section and its subsections, depth first.</summary>
    public IEnumerable<PreferencesSectionNode> SelfAndDescendants() =>
        new[] { this }.Concat(Children.SelectMany(c => c.SelfAndDescendants()));
}

/// <summary>
/// The section tree of the Preferences window with its search, modelled on Rider's Settings: the query keeps the
/// sections whose name or page text contains it (and their enclosing sections, expanded), and the view frames the
/// matching controls of the shown page (<see cref="Matches"/>).
/// </summary>
public sealed partial class PreferencesSectionsViewModel : ObservableObject
{
    private readonly Func<int, IEnumerable<string>> _pageTexts;

    /// <summary>Creates the tree.</summary>
    /// <param name="roots">The top-level sections.</param>
    /// <param name="pageTexts">The texts of a page (labels, check boxes, headers…) the search looks in.</param>
    public PreferencesSectionsViewModel(IEnumerable<PreferencesSectionNode> roots, Func<int, IEnumerable<string>> pageTexts)
    {
        ArgumentNullException.ThrowIfNull(roots);
        _pageTexts = pageTexts ?? throw new ArgumentNullException(nameof(pageTexts));
        Roots = new ObservableCollection<PreferencesSectionNode>(roots);
        _selected = Roots.FirstOrDefault();
    }

    /// <summary>The top-level sections.</summary>
    public ObservableCollection<PreferencesSectionNode> Roots { get; }

    /// <summary>All sections, depth first.</summary>
    public IEnumerable<PreferencesSectionNode> All => Roots.SelectMany(r => r.SelfAndDescendants());

    /// <summary>The section whose page is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Breadcrumb))]
    private PreferencesSectionNode? _selected;

    /// <summary>The path of the selected section, shown above its page.</summary>
    public string Breadcrumb => Selected?.Path ?? string.Empty;

    /// <summary>The search text.</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>Whether there is a search (the view frames the matching controls then).</summary>
    public bool IsSearching => Query.Trim().Length > 0;

    /// <summary>Whether <paramref name="text"/> contains the search text (case-insensitive); <c>false</c> without a search.</summary>
    public bool Matches(string? text) =>
        IsSearching && text is not null && text.Contains(Query.Trim(), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>The section showing <paramref name="pageIndex"/>, or <c>null</c>.</summary>
    public PreferencesSectionNode? FindByPage(int pageIndex) => All.FirstOrDefault(n => n.PageIndex == pageIndex);

    /// <summary>Applies the search again (e.g. after the texts of the pages changed).</summary>
    public void Refilter()
    {
        foreach (PreferencesSectionNode root in Roots)
        {
            Filter(root);
        }

        if (Selected is null || !Selected.IsVisible)
        {
            Selected = All.FirstOrDefault(n => n.IsVisible && MatchesItself(n)) ?? All.FirstOrDefault(n => n.IsVisible);
        }
    }

    partial void OnQueryChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        Refilter();
    }

    // Visible: no search, or the section itself matches, or one of its subsections is visible (it is then expanded).
    private bool Filter(PreferencesSectionNode node)
    {
        bool childVisible = false;
        foreach (PreferencesSectionNode child in node.Children)
        {
            childVisible |= Filter(child);
        }

        if (childVisible && IsSearching)
        {
            node.IsExpanded = true;
        }

        node.IsVisible = !IsSearching || childVisible || MatchesItself(node);
        return node.IsVisible;
    }

    private bool MatchesItself(PreferencesSectionNode node) =>
        !IsSearching || Matches(node.Title) || _pageTexts(node.PageIndex).Any(Matches);
}
