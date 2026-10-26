using System.Collections.Generic;
using System.Collections.ObjectModel;
using Signet.Core.MiscEditors;

namespace Signet.App.ViewModels;

/// <summary>
/// Tree node of the "Saved Searches" panel — a wrapper over a <see cref="SearchEditorNode"/> from
/// the <see cref="SearchEditorModel"/>. Field edits propagate to the model.
/// </summary>
public sealed class SearchEntryNodeViewModel : ViewModelBase
{
    private readonly SearchEditorModel _model;
    private bool _isExpanded;
    private bool _isVisible = true;
    private bool _isEditing;
    private string _editText = string.Empty;

    /// <summary>Creates the view node (recursively for the children).</summary>
    public SearchEntryNodeViewModel(SearchEditorNode node, SearchEditorModel model, bool isExpanded)
    {
        Node = node;
        _model = model;
        _isExpanded = isExpanded || node.IsRoot;

        var children = new List<SearchEntryNodeViewModel>();
        foreach (SearchEditorNode child in node.Children)
        {
            children.Add(new SearchEntryNodeViewModel(child, model, isExpanded: false));
        }

        Children = new ObservableCollection<SearchEntryNodeViewModel>(children);
    }

    /// <summary>The underlying model node.</summary>
    public SearchEditorNode Node { get; }

    /// <summary>Whether the node is a group.</summary>
    public bool IsGroup => Node.IsGroup;

    /// <summary>Child nodes.</summary>
    public ObservableCollection<SearchEntryNodeViewModel> Children { get; }

    /// <summary>Displayed / editable name.</summary>
    public string Name
    {
        get => Node.Name;
        set
        {
            if (value is not null && value != Node.Name)
            {
                _model.Rename(Node, value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Find text / pattern (editable; empty for groups).</summary>
    public string Find
    {
        get => Node.Find;
        set
        {
            if (!Node.IsGroup && value is not null && value != Node.Find)
            {
                _model.SetFind(Node, value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Replace text (editable; empty for groups).</summary>
    public string Replace
    {
        get => Node.Replace;
        set
        {
            if (!Node.IsGroup && value is not null && value != Node.Replace)
            {
                _model.SetReplace(Node, value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Encoded search options (editable; empty for groups).</summary>
    public string Controls
    {
        get => Node.Controls;
        set
        {
            if (!Node.IsGroup && value is not null && value != Node.Controls)
            {
                _model.SetControls(Node, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ControlsToolTip));
            }
        }
    }

    /// <summary>Human-readable description of the tokens in <see cref="Controls"/>.</summary>
    public string ControlsToolTip => SearchControls.BuildToolTip(Node.Controls);

    /// <summary>Whether the group is expanded.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Whether the node is visible with the active filter.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    /// <summary>
    /// Whether the name is being edited in place in the tree (a double click on a group — an entry's name
    /// is changed through Edit Search instead, never in the tree).
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    /// <summary>Name being typed in place — goes to <see cref="Name"/> only once committed.</summary>
    public string EditText
    {
        get => _editText;
        set => SetProperty(ref _editText, value ?? string.Empty);
    }

    /// <summary>Starts in-place rename (double click on a group).</summary>
    public void BeginRename()
    {
        EditText = Name;
        IsEditing = true;
    }

    /// <summary>Applies the typed name (Enter / losing focus).</summary>
    public void CommitRename()
    {
        if (!_isEditing)
        {
            return;
        }

        Name = EditText;
        IsEditing = false;
    }

    /// <summary>Discards the typed name (Escape).</summary>
    public void CancelRename() => IsEditing = false;
}
