using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the modal "Generate Table Of Contents" dialog (<c>HeadingSelector</c>):
/// the tree of the book's headings with an "Include" checkbox, title editing, level change (◀ / ▶),
/// a quick "Up to level N" and the "Show TOC items only" filter.
/// </summary>
/// <remarks>
/// The "TOC items only" filter shows only the headings included in the TOC; the children of an
/// excluded heading are "promoted" to the nearest visible ancestor.
/// </remarks>
public sealed class HeadingSelectorViewModel : ViewModelBase
{
    private const string LevelChoicePrompt = "<Select headings to include in TOC>";

    private readonly HeadingSelectorModel _model;
    private bool _tocItemsOnly = true;
    private HeadingNodeViewModel? _selectedNode;
    private int _selectedLevelChoiceIndex;
    private string _countsSummary = string.Empty;

    /// <summary>Creates the view model for a book.</summary>
    public HeadingSelectorViewModel(Book book)
        : this(new HeadingSelectorModel(book))
    {
    }

    /// <summary>Creates the view model for a ready core model (for tests).</summary>
    public HeadingSelectorViewModel(HeadingSelectorModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));

        IncreaseLevelCommand = new RelayCommand(() => ChangeLevel(1), () => _selectedNode is not null);
        DecreaseLevelCommand = new RelayCommand(() => ChangeLevel(-1), () => _selectedNode is not null);
        AcceptCommand = new RelayCommand(Accept);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));

        RebuildLevelChoices();
        RebuildNodes();
    }

    /// <summary>Raised when the dialog should close (after "OK" or "Cancel").</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Visible nodes of the heading tree (top level).</summary>
    public ObservableCollection<HeadingNodeViewModel> Nodes { get; } = new();

    /// <summary>Labels of the "Up to level N" list (the first item is a prompt that does nothing).</summary>
    public ObservableCollection<string> LevelChoices { get; } = new();

    /// <summary>The "◀" command — decrease the selected heading's level by 1.</summary>
    public RelayCommand DecreaseLevelCommand { get; }

    /// <summary>The "▶" command — increase the selected heading's level by 1.</summary>
    public RelayCommand IncreaseLevelCommand { get; }

    /// <summary>The "OK" command — write the changes to the sources and close.</summary>
    public RelayCommand AcceptCommand { get; }

    /// <summary>The "Cancel" command — close without saving.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Whether the user accepted the dialog ("OK").</summary>
    public bool Accepted { get; private set; }

    /// <summary>Whether <see cref="AcceptCommand"/> actually changed any XHTML resource.</summary>
    public bool BookChanged { get; private set; }

    /// <summary>Show only the headings that will go into the TOC.</summary>
    public bool TocItemsOnly
    {
        get => _tocItemsOnly;
        set
        {
            if (SetProperty(ref _tocItemsOnly, value))
            {
                RebuildNodes();
            }
        }
    }

    /// <summary>Selected tree node (for the level change buttons).</summary>
    public HeadingNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                IncreaseLevelCommand.NotifyCanExecuteChanged();
                DecreaseLevelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Selected item of the "Up to level N" list. Changing it applies the selection immediately.</summary>
    public int SelectedLevelChoiceIndex
    {
        get => _selectedLevelChoiceIndex;
        set
        {
            if (!SetProperty(ref _selectedLevelChoiceIndex, value))
            {
                return;
            }

            IReadOnlyList<HeadingLevelChoice> choices = _model.GetLevelChoices();
            int choiceIndex = value - 1;
            if (choiceIndex >= 0 && choiceIndex < choices.Count)
            {
                _model.SetAllHeadingInclusion(choices[choiceIndex].Level);
                RebuildNodes();
                _selectedLevelChoiceIndex = 0;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Summary of the number of included / hidden headings per level (the bar below the tree).</summary>
    public string CountsSummary
    {
        get => _countsSummary;
        private set => SetProperty(ref _countsSummary, value);
    }

    private void ChangeLevel(int delta)
    {
        if (_selectedNode is null)
        {
            return;
        }

        Heading target = _selectedNode.Heading;
        if (_model.ChangeHeadingLevel(target, delta))
        {
            RebuildNodes();
            SelectedNode = FindNode(Nodes, target);
            RebuildLevelChoices();
        }
    }

    private void Accept()
    {
        BookChanged = _model.Apply();
        Accepted = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildLevelChoices()
    {
        LevelChoices.Clear();
        LevelChoices.Add(LevelChoicePrompt);
        foreach (HeadingLevelChoice choice in _model.GetLevelChoices())
        {
            LevelChoices.Add(choice.Label);
        }

        _selectedLevelChoiceIndex = 0;
        OnPropertyChanged(nameof(SelectedLevelChoiceIndex));
    }

    private void RebuildNodes()
    {
        Nodes.Clear();
        foreach (Heading heading in _model.RootHeadings)
        {
            AppendNode(heading, Nodes);
        }

        RefreshCounts();
    }

    private void AppendNode(Heading heading, ObservableCollection<HeadingNodeViewModel> parent)
    {
        if (_tocItemsOnly && !heading.IncludeInToc)
        {
            // A hidden heading — its children move up to the parent's level.
            foreach (Heading child in heading.Children)
            {
                AppendNode(child, parent);
            }

            return;
        }

        HeadingNodeViewModel node = new(_model, heading, OnNodeInclusionChanged);
        parent.Add(node);
        foreach (Heading child in heading.Children)
        {
            AppendNode(child, node.Children);
        }
    }

    private void OnNodeInclusionChanged()
    {
        if (_tocItemsOnly)
        {
            RebuildNodes();
        }
        else
        {
            RefreshCounts();
        }
    }

    private void RefreshCounts()
    {
        StringBuilder sb = new();
        foreach (KeyValuePair<string, (int Included, int Hidden)> pair in _model.GetCounts())
        {
            if (pair.Value.Included == 0 && pair.Value.Hidden == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append("    ");
            }

            sb.Append(pair.Key).Append(": ").Append(pair.Value.Included).Append(" / ").Append(pair.Value.Hidden);
        }

        CountsSummary = sb.Length > 0
            ? "Poziom: włączone / ukryte —    " + sb
            : "Brak nagłówków w publikacji.";
    }

    private static HeadingNodeViewModel? FindNode(IEnumerable<HeadingNodeViewModel> nodes, Heading heading)
    {
        foreach (HeadingNodeViewModel node in nodes)
        {
            if (ReferenceEquals(node.Heading, heading))
            {
                return node;
            }

            if (FindNode(node.Children, heading) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}

/// <summary>A heading tree node in the "Generate Table Of Contents" dialog.</summary>
public sealed class HeadingNodeViewModel : ObservableObject
{
    private readonly HeadingSelectorModel _model;
    private readonly Action _onInclusionChanged;

    /// <summary>Creates a node for a heading.</summary>
    public HeadingNodeViewModel(HeadingSelectorModel model, Heading heading, Action onInclusionChanged)
    {
        _model = model;
        Heading = heading;
        _onInclusionChanged = onInclusionChanged;
    }

    /// <summary>The heading represented by this node.</summary>
    public Heading Heading { get; }

    /// <summary>Child nodes.</summary>
    public ObservableCollection<HeadingNodeViewModel> Children { get; } = new();

    /// <summary>Level label, e.g. <c>h2</c>.</summary>
    public string LevelLabel => "h" + Heading.Level;

    /// <summary>TOC entry text (editable — saved as the <c>title</c> attribute).</summary>
    public string TitleText
    {
        get => Heading.Title.Length > 0 ? Heading.Title : Heading.Text;
        set
        {
            if (!string.Equals(value, TitleText, StringComparison.Ordinal))
            {
                _model.SetTitle(Heading, value ?? string.Empty);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Whether the heading will go into the TOC.</summary>
    public bool IncludeInToc
    {
        get => Heading.IncludeInToc;
        set
        {
            if (value != Heading.IncludeInToc)
            {
                _model.SetInclusion(Heading, value);
                OnPropertyChanged();
                _onInclusionChanged();
            }
        }
    }
}
