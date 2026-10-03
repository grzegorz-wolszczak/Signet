using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.Core.BookManipulation;
using Signet.Core.Metadata;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the modal "Metadata Editor" dialog. Holds an editable tree of the recognized
/// OPF metadata elements (<see cref="MetadataEditModel"/>) and exposes primitive operations
/// (insert row / insert child / remove row / move row up / move row down) —
/// field-specific logic (default values, language/role dialogs, generating
/// a custom name) lives in the view (<c>MetadataEditorWindow</c>).
/// </summary>
public sealed class MetadataEditorViewModel : ViewModelBase
{
    private readonly Book _book;
    private readonly MetadataEditState _state;
    private readonly MetadataNodeViewModel _root;
    private MetadataNodeViewModel? _selectedNode;
    private string _message = string.Empty;

    /// <summary>Creates the view model and loads the book's current metadata.</summary>
    public MetadataEditorViewModel(Book book)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
        IsEpub3 = book.IsEpub3;
        _state = MetadataEditModel.Extract(book);
        _root = new MetadataNodeViewModel(new MetadataEntry(), IsEpub3);

        foreach (MetadataEntry element in _state.Elements)
        {
            _root.Children.Add(CreateNode(element, _root, IsEpub3));
        }

        RemoveCommand = new RelayCommand(RemoveSelected, () => SelectedNode is not null);
        MoveUpCommand = new RelayCommand(MoveSelectedUp, () => SelectedNode is not null);
        MoveDownCommand = new RelayCommand(MoveSelectedDown, () => SelectedNode is not null);
        AcceptCommand = new RelayCommand(Accept);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Raised when the dialog should close (after "OK" or "Cancel").</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Whether the book is EPUB 3 (determines the field range and refines vs epub2 attribute handling).</summary>
    public bool IsEpub3 { get; }

    /// <summary>Top-level elements of the metadata tree.</summary>
    public ObservableCollection<MetadataNodeViewModel> Nodes => _root.Children;

    /// <summary>The currently selected node (of any level), or <c>null</c>.</summary>
    public MetadataNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                RemoveCommand.NotifyCanExecuteChanged();
                MoveUpCommand.NotifyCanExecuteChanged();
                MoveDownCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>"Remove row" — delete the selected node together with its children.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>"Move row up" — swap with the previous sibling.</summary>
    public RelayCommand MoveUpCommand { get; }

    /// <summary>"Move row down" — swap with the next sibling.</summary>
    public RelayCommand MoveDownCommand { get; }

    /// <summary>"OK" — save to the OPF and close.</summary>
    public RelayCommand AcceptCommand { get; }

    /// <summary>"Cancel" — close without saving.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Whether the user accepted the dialog.</summary>
    public bool Accepted { get; private set; }

    /// <summary>Status message below the tree.</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    // --- primitive tree operations (insert row / insert child / remove row / move row up / move row down) ---

    /// <summary>
    /// Inserts a new top-level element right after the ancestor element of the current selection
    /// (or at the end when nothing is selected), and selects it.
    /// </summary>
    public MetadataNodeViewModel InsertElement(string code, string content)
    {
        MetadataNodeViewModel node = new(new MetadataEntry { Code = code, Content = content }, IsEpub3) { Parent = _root };
        MetadataNodeViewModel? top = TopLevelAncestorOf(SelectedNode);
        int index = top is null ? _root.Children.Count : _root.Children.IndexOf(top) + 1;
        _root.Children.Insert(index, node);
        SelectedNode = node;
        return node;
    }

    /// <summary>
    /// Inserts a child (attribute/refinement) at the start of the child list of the ancestor element of
    /// the current selection — it works whether the element itself or one of its existing
    /// children is selected (it always targets the nearest top-level ancestor).
    /// Returns <c>null</c> when nothing is selected.
    /// </summary>
    public MetadataNodeViewModel? InsertChild(string code, string content)
    {
        MetadataNodeViewModel? top = TopLevelAncestorOf(SelectedNode);
        if (top is null)
        {
            return null;
        }

        MetadataNodeViewModel node = new(new MetadataEntry { Code = code, Content = content }, IsEpub3) { Parent = top };
        top.Children.Insert(0, node);
        top.IsExpanded = true;
        SelectedNode = node;
        return node;
    }

    private void RemoveSelected()
    {
        if (SelectedNode is not { } node)
        {
            return;
        }

        (node.Parent ?? _root).Children.Remove(node);
        SelectedNode = null;
    }

    private void MoveSelectedUp()
    {
        if (SelectedNode is not { Parent: { } parent } node)
        {
            return;
        }

        int index = parent.Children.IndexOf(node);
        if (index > 0)
        {
            parent.Children.Move(index, index - 1);
        }
    }

    private void MoveSelectedDown()
    {
        if (SelectedNode is not { Parent: { } parent } node)
        {
            return;
        }

        int index = parent.Children.IndexOf(node);
        if (index >= 0 && index < parent.Children.Count - 1)
        {
            parent.Children.Move(index, index + 1);
        }
    }

    private static MetadataNodeViewModel? TopLevelAncestorOf(MetadataNodeViewModel? node)
    {
        if (node is null)
        {
            return null;
        }

        MetadataNodeViewModel current = node;
        while (current.Parent is { Parent: not null } parent)
        {
            current = parent;
        }

        return current;
    }

    private static MetadataNodeViewModel CreateNode(MetadataEntry entry, MetadataNodeViewModel parent, bool isEpub3)
    {
        MetadataNodeViewModel node = new(entry, isEpub3) { Parent = parent };
        foreach (MetadataEntry child in entry.Children)
        {
            node.Children.Add(CreateNode(child, node, isEpub3));
        }

        return node;
    }

    // --- saving ---

    private void Accept()
    {
        _state.Elements.Clear();
        foreach (MetadataNodeViewModel node in _root.Children)
        {
            _state.Elements.Add(BuildEntry(node));
        }

        MetadataEditModel.Save(_book, _state);
        Accepted = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static MetadataEntry BuildEntry(MetadataNodeViewModel node)
    {
        MetadataEntry entry = new() { Code = node.Code, Content = node.Content };
        foreach (MetadataNodeViewModel child in node.Children)
        {
            entry.Children.Add(BuildEntry(child));
        }

        return entry;
    }
}

/// <summary>
/// Node of the editable metadata tree in the "Metadata Editor" dialog. The "Name" column is
/// read-only and shows the translation of the code into a display name (<see cref="NameDisplay"/>);
/// the "Value" column (<see cref="Content"/>) is edited as raw text/code (a deliberate
/// simplification compared to a combobox/textarea editor).
/// </summary>
public sealed class MetadataNodeViewModel : ObservableObject
{
    private readonly bool _isEpub3;
    private string _content;
    private bool _isExpanded = true;

    /// <summary>Creates a node from the given Core tree entry.</summary>
    public MetadataNodeViewModel(MetadataEntry source, bool isEpub3)
    {
        ArgumentNullException.ThrowIfNull(source);
        Code = source.Code;
        _content = source.Content;
        _isEpub3 = isEpub3;
    }

    /// <summary>Parent in the tree (an artificial root for top-level elements).</summary>
    public MetadataNodeViewModel? Parent { get; set; }

    /// <summary>Child nodes (attributes/refinements of a top-level element).</summary>
    public ObservableCollection<MetadataNodeViewModel> Children { get; } = new();

    /// <summary>Internal code (e.g. <c>dc:creator</c>, <c>role</c>, <c>id</c>) — not editable.</summary>
    public string Code { get; }

    /// <summary>Node value/content — editable as raw text.</summary>
    public string Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ValuePreview));
            }
        }
    }

    /// <summary>Whether the node is expanded in the tree.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Whether this is a top-level element (a child of the artificial root).</summary>
    public bool IsTopLevel => Parent?.Parent is null;

    /// <summary>Display name for the "Name" column — the code translated through the field catalog.</summary>
    public string NameDisplay => IsTopLevel ? MetadataDisplay.EName(_isEpub3, Code) : MetadataDisplay.PName(_isEpub3, Code);

    /// <summary>Value preview using a translation for dictionary-based fields (role/language).</summary>
    public string ValuePreview => Code switch
    {
        "role" or "opf:role" => MetadataDisplay.RName(_content),
        "xml:lang" or "altlang" => MetadataDisplay.LName(_content),
        "dc:language" when IsTopLevel => MetadataDisplay.LName(_content),
        _ => _content,
    };
}
