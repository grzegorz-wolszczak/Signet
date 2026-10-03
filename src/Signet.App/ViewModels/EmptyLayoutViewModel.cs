using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>An entry in the layout designer's "Add File" menu (marker type + target file name).</summary>
/// <param name="Label">Menu item label.</param>
/// <param name="FileName">Name of the file to create (e.g. <c>marker.xhtml</c>, <c>content.opf</c>).</param>
public readonly record struct LayoutMarkerType(string Label, string FileName);

/// <summary>Layout tree node: a folder or a marker file.</summary>
public sealed partial class LayoutNode : ObservableObject
{
    /// <summary>Creates the node.</summary>
    public LayoutNode(string name, bool isFolder, LayoutNode? parent)
    {
        _name = name;
        IsFolder = isFolder;
        Parent = parent;
    }

    /// <summary>File or folder name.</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>Whether this is a folder (it can have children).</summary>
    public bool IsFolder { get; }

    /// <summary>Parent in the tree, or <c>null</c> for the root.</summary>
    public LayoutNode? Parent { get; }

    /// <summary>Children (folders and files).</summary>
    public ObservableCollection<LayoutNode> Children { get; } = new();

    /// <summary>Whether the node is a marker (the name starts with <c>marker.</c>).</summary>
    public bool IsMarker => Name.StartsWith("marker.", StringComparison.Ordinal);

    /// <summary>Whether this is the root (<c>EpubRoot</c>).</summary>
    public bool IsRoot => Parent is null;
}

/// <summary>
/// View model of the "Custom Epub Layout Designer" wizard — an in-memory tree
/// with a set of editing operations and validity checks.
/// </summary>
public sealed partial class EmptyLayoutViewModel : ObservableObject
{
    private readonly string _epubVersion;

    /// <summary>Creates the view model, filling the tree with the default layout for the EPUB version.</summary>
    /// <param name="epubVersion">EPUB version (<c>2.0</c> / <c>3.0</c>).</param>
    /// <param name="initialLayout">Initial book paths (e.g. from a remembered layout); <c>null</c> = the standard one.</param>
    public EmptyLayoutViewModel(string epubVersion, IReadOnlyList<string>? initialLayout = null)
    {
        _epubVersion = string.IsNullOrWhiteSpace(epubVersion) ? "2.0" : epubVersion;
        MarkerTypes = BuildMarkerTypes(_epubVersion);
        Root = new LayoutNode("EpubRoot", isFolder: true, parent: null);
        LoadBookPaths(initialLayout ?? BookCreator.StandardLayout(_epubVersion));
        _selectedNode = Root;
    }

    /// <summary>Root of the tree (<c>EpubRoot</c>).</summary>
    public LayoutNode Root { get; }

    /// <summary>The root as a one-element collection — for <c>TreeView.ItemsSource</c>.</summary>
    public IReadOnlyList<LayoutNode> RootNodes => new[] { Root };

    /// <summary>File types available in the "Add File" menu (depending on the EPUB version).</summary>
    public IReadOnlyList<LayoutMarkerType> MarkerTypes { get; }

    /// <summary>Currently selected node (the root by default).</summary>
    [ObservableProperty]
    private LayoutNode? _selectedNode;

    /// <summary>The last validation error message (empty when none).</summary>
    [ObservableProperty]
    private string _validationError = string.Empty;

    /// <summary>Whether the selected node is a folder that can be added to.</summary>
    public bool CanAddToSelection => SelectedNode is { IsFolder: true };

    /// <summary>Whether the selected node can be deleted.</summary>
    public bool CanDeleteSelection => SelectedNode is { IsRoot: false };

    /// <summary>Whether the selected node can be renamed (not the root, not a marker).</summary>
    public bool CanRenameSelection => SelectedNode is { IsRoot: false, IsMarker: false };

    partial void OnSelectedNodeChanged(LayoutNode? value)
    {
        OnPropertyChanged(nameof(CanAddToSelection));
        OnPropertyChanged(nameof(CanDeleteSelection));
        OnPropertyChanged(nameof(CanRenameSelection));
    }

    /// <summary>Adds a subfolder to the selected folder.</summary>
    public LayoutNode? AddFolder(string name)
    {
        if (SelectedNode is not { IsFolder: true } parent || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string cleaned = name.Trim();
        if (parent.Children.Any(c => string.Equals(c.Name, cleaned, StringComparison.Ordinal)))
        {
            return parent.Children.First(c => string.Equals(c.Name, cleaned, StringComparison.Ordinal));
        }

        LayoutNode node = new(cleaned, isFolder: true, parent);
        parent.Children.Add(node);
        return node;
    }

    /// <summary>Adds a file of the given type to the selected folder (if allowed).</summary>
    public LayoutNode? AddFile(LayoutMarkerType type)
    {
        if (SelectedNode is not { IsFolder: true } parent || !IsFileTypeAllowed(type.FileName))
        {
            return null;
        }

        if (parent.Children.Any(c => string.Equals(c.Name, type.FileName, StringComparison.Ordinal)))
        {
            return null;
        }

        LayoutNode node = new(type.FileName, isFolder: false, parent);
        parent.Children.Add(node);
        return node;
    }

    /// <summary>Whether the given file can still be added (OPF/NCX/NAV only once in the whole tree).</summary>
    public bool IsFileTypeAllowed(string fileName) => fileName switch
    {
        "content.opf" => !ContainsFile("content.opf"),
        "toc.ncx" => !ContainsFile("toc.ncx"),
        "nav.xhtml" => !ContainsFile("nav.xhtml"),
        _ => true,
    };

    /// <summary>Renames the selected node.</summary>
    public void RenameSelection(string newName)
    {
        if (SelectedNode is not { IsRoot: false, IsMarker: false } node || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        string cleaned = newName.Trim();
        if (!node.IsFolder && !cleaned.Contains('.', StringComparison.Ordinal))
        {
            cleaned += "." + node.Name.Split('.').Last();
        }

        node.Name = cleaned;
    }

    /// <summary>Deletes the selected node (except the root).</summary>
    public void DeleteSelection()
    {
        if (SelectedNode is { IsRoot: false } node && node.Parent is { } parent)
        {
            parent.Children.Remove(node);
            SelectedNode = parent;
        }
    }

    /// <summary>Replaces the whole tree with a layout loaded from a file.</summary>
    public void ReplaceWith(IReadOnlyList<string> bookPaths)
    {
        Root.Children.Clear();
        LoadBookPaths(bookPaths);
        SelectedNode = Root;
    }

    /// <summary>
    /// Collects the book paths of all files in the tree (sorted). Does not check validity —
    /// that is what <see cref="Validate"/> is for.
    /// </summary>
    public IReadOnlyList<string> GetBookPaths()
    {
        List<string> result = new();
        Collect(Root, string.Empty, result);
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>
    /// Checks the layout's validity. Returns <c>true</c> and clears
    /// <see cref="ValidationError"/>, or <c>false</c> and fills in the list of errors.
    /// </summary>
    public bool Validate()
    {
        IReadOnlyList<string> paths = GetBookPaths();
        int opf = paths.Count(p => p.EndsWith(".opf", StringComparison.Ordinal));
        int txt = paths.Count(p => p.EndsWith("marker.xhtml", StringComparison.Ordinal));
        int css = paths.Count(p => p.EndsWith("marker.css", StringComparison.Ordinal));
        int img = paths.Count(p => p.EndsWith("marker.jpg", StringComparison.Ordinal));
        int ncx = paths.Count(p => p.EndsWith(".ncx", StringComparison.Ordinal));
        int nav = paths.Count(p => p.EndsWith(".xhtml", StringComparison.Ordinal)
                                   && !p.EndsWith("marker.xhtml", StringComparison.Ordinal));

        List<string> errors = new();
        if (opf != 1)
        {
            errors.Add(Strings.Get("EmptyLayout_NeedOneOpf"));
        }

        if (txt < 1)
        {
            errors.Add(Strings.Get("EmptyLayout_NeedXhtmlMarker"));
        }

        if (img < 1)
        {
            errors.Add(Strings.Get("EmptyLayout_NeedImageMarker"));
        }

        if (css < 1)
        {
            errors.Add(Strings.Get("EmptyLayout_NeedCssMarker"));
        }

        if (_epubVersion.StartsWith('2') && ncx != 1)
        {
            errors.Add(Strings.Get("EmptyLayout_NeedOneNcx"));
        }

        if (_epubVersion.StartsWith('3') && nav != 1)
        {
            errors.Add(Strings.Get("EmptyLayout_NeedOneNav"));
        }

        ValidationError = string.Join('\n', errors);
        return errors.Count == 0;
    }

    private bool ContainsFile(string fileName)
    {
        bool found = false;
        Walk(Root, node =>
        {
            if (!node.IsFolder && string.Equals(node.Name, fileName, StringComparison.Ordinal))
            {
                found = true;
            }
        });
        return found;
    }

    private static void Walk(LayoutNode node, Action<LayoutNode> visit)
    {
        visit(node);
        foreach (LayoutNode child in node.Children)
        {
            Walk(child, visit);
        }
    }

    private static void Collect(LayoutNode node, string prefix, List<string> into)
    {
        foreach (LayoutNode child in node.Children)
        {
            string path = prefix.Length == 0 ? child.Name : prefix + "/" + child.Name;
            if (child.IsFolder)
            {
                Collect(child, path, into);
            }
            else
            {
                into.Add(path);
            }
        }
    }

    private void LoadBookPaths(IReadOnlyList<string> bookPaths)
    {
        foreach (string bookPath in bookPaths)
        {
            string trimmed = bookPath.TrimStart('/');
            if (trimmed.Length == 0)
            {
                continue;
            }

            string[] segments = trimmed.Split('/');
            LayoutNode cursor = Root;
            for (int i = 0; i < segments.Length; i++)
            {
                bool isLast = i == segments.Length - 1;
                string segment = segments[i];
                LayoutNode? existing = cursor.Children
                    .FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.Ordinal));
                if (existing is null)
                {
                    existing = new LayoutNode(segment, isFolder: !isLast, cursor);
                    cursor.Children.Add(existing);
                }

                cursor = existing;
            }
        }
    }

    private static List<LayoutMarkerType> BuildMarkerTypes(string version)
    {
        List<LayoutMarkerType> types = new()
        {
            new(Strings.Get("EmptyLayout_TypeXhtml"), "marker.xhtml"),
            new(Strings.Get("EmptyLayout_TypeCss"), "marker.css"),
            new(Strings.Get("EmptyLayout_TypeImages"), "marker.jpg"),
            new(Strings.Get("EmptyLayout_TypeFonts"), "marker.otf"),
            new(Strings.Get("EmptyLayout_TypeAudio"), "marker.mp3"),
            new(Strings.Get("EmptyLayout_TypeVideo"), "marker.mp4"),
        };

        if (version.StartsWith('3'))
        {
            types.Add(new(Strings.Get("EmptyLayout_TypeJs"), "marker.js"));
        }

        types.Add(new(Strings.Get("EmptyLayout_TypeMisc"), "marker.xml"));
        types.Add(new(Strings.Get("EmptyLayout_TypeOpf"), "content.opf"));
        types.Add(new(Strings.Get("EmptyLayout_TypeNcx"), "toc.ncx"));

        if (version.StartsWith('3'))
        {
            types.Add(new(Strings.Get("EmptyLayout_TypeNav"), "nav.xhtml"));
        }

        return types;
    }
}
