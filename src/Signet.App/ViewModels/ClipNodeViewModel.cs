using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Signet.Core.MiscEditors;

namespace Signet.App.ViewModels;

/// <summary>
/// Tree node of the clips panel/editor — a wrapper over <see cref="ClipEditorNode"/> from the
/// <see cref="ClipEditorModel"/>. Field edits propagate to the model.
/// </summary>
public sealed class ClipNodeViewModel : ViewModelBase
{
    private readonly ClipEditorModel _model;
    private readonly Action? _onModified;
    private bool _isExpanded;
    private bool _isVisible = true;
    private bool _isEditing;
    private string _editText = string.Empty;

    /// <summary>
    /// Creates the view node (recursively for children). <paramref name="onModified"/> is called
    /// after every name/text edit made directly on the node, so the Clip Bar/menu are refreshed
    /// not only on structural changes.
    /// </summary>
    public ClipNodeViewModel(ClipEditorNode node, ClipEditorModel model, bool isExpanded, Action? onModified = null)
    {
        Node = node;
        _model = model;
        _onModified = onModified;
        _isExpanded = isExpanded || node.IsRoot;

        var children = new List<ClipNodeViewModel>();
        foreach (ClipEditorNode child in node.Children)
        {
            children.Add(new ClipNodeViewModel(child, model, isExpanded: false, onModified));
        }

        Children = new ObservableCollection<ClipNodeViewModel>(children);
    }

    /// <summary>Underlying model node.</summary>
    public ClipEditorNode Node { get; }

    /// <summary>Whether the node is a group.</summary>
    public bool IsGroup => Node.IsGroup;

    /// <summary>Child nodes.</summary>
    public ObservableCollection<ClipNodeViewModel> Children { get; }

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
                OnPropertyChanged(nameof(TextPreview));
                _onModified?.Invoke();
            }
        }
    }

    /// <summary>Clip text (editable; empty for groups) — may contain the <c>\1</c> placeholder.</summary>
    public string Text
    {
        get => Node.Text;
        set
        {
            if (!Node.IsGroup && value is not null && value != Node.Text)
            {
                _model.SetText(Node, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(TextPreview));
                _onModified?.Invoke();
            }
        }
    }

    /// <summary>Single-line text preview (for the tree; the full text is edited separately).</summary>
    public string TextPreview => Node.Text.Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>Whether the group is expanded.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Whether the node name is being edited in the tree (double click / F2).</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    /// <summary>Name being typed — goes to <see cref="Name"/> only once committed.</summary>
    public string EditText
    {
        get => _editText;
        set => SetProperty(ref _editText, value ?? string.Empty);
    }

    /// <summary>Whether the node is visible under the active filter.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}
