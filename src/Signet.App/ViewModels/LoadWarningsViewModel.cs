using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Load warnings" window: the warnings collected while loading a book (<see cref="LoadWarning"/>) as
/// a two-level tree. A warning about many files is a node with the translated heading and the number of files, and one
/// child per file (file, reason, line:column); a warning about a single thing is a node of its own. Activating a file
/// raises the navigation callback so that the host opens it in Code Editor.
/// </summary>
public sealed class LoadWarningsViewModel : ViewModelBase
{
    private readonly string _fileName;
    private readonly IReadOnlyList<LoadWarning> _warnings;
    private readonly Action<string, int, int>? _navigate;
    private readonly string? _header;

    /// <summary>Creates the view model and builds the tree.</summary>
    /// <param name="fileName">Name of the loaded file (for the header).</param>
    /// <param name="warnings">The warnings (already translated).</param>
    /// <param name="navigate">Opens a file at a line and column (<c>bookPath</c>, <c>line</c>, <c>column</c>); may be <c>null</c>.</param>
    /// <param name="header">
    /// A ready header to show instead of the "file loaded with warnings" one — for the report of an operation
    /// (<see cref="Signet.App.Services.SkippedFilesReport"/>) that reuses this window; <c>null</c> for the load warnings.
    /// </param>
    public LoadWarningsViewModel(
        string fileName, IReadOnlyList<LoadWarning> warnings, Action<string, int, int>? navigate, string? header = null)
    {
        _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        _warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        _navigate = navigate;
        _header = header;
        foreach (LoadWarning warning in warnings)
        {
            Roots.Add(CreateNode(warning));
        }
    }

    /// <summary>The header: the file name and the number of warnings.</summary>
    public string Header => _header ?? Strings.Format("LoadWarningsDialog_Header", _fileName, _warnings.Count);

    /// <summary>The first level of the tree: one node per warning.</summary>
    public ObservableCollection<LoadWarningNode> Roots { get; } = new();

    /// <summary>Expands every warning that has files.</summary>
    public void ExpandAll() => SetExpanded(true);

    /// <summary>Collapses every warning that has files.</summary>
    public void CollapseAll() => SetExpanded(false);

    /// <summary>Opens the file of <paramref name="node"/> at the problem; nodes without a file do nothing.</summary>
    /// <returns>Whether the node had a file to open.</returns>
    public bool Activate(LoadWarningNode? node)
    {
        if (node?.BookPath is not { } bookPath || _navigate is null)
        {
            return false;
        }

        _navigate(bookPath, node.Line, node.Column);
        return true;
    }

    /// <summary>
    /// The whole tree as text (for "Copy"): the header, then each warning with its files below it as
    /// <c>file TAB reason TAB line:column</c>.
    /// </summary>
    public string CopyText()
    {
        StringBuilder text = new();
        text.AppendLine(Header).AppendLine();
        foreach (LoadWarningNode root in Roots)
        {
            text.Append("• ").AppendLine(root.Text);
            foreach (LoadWarningNode child in root.Children)
            {
                text.Append("    ").Append(child.Text).Append('\t').Append(child.Reason);
                if (child.Location.Length > 0)
                {
                    text.Append('\t').Append(child.Location);
                }

                text.AppendLine();
            }
        }

        return text.ToString();
    }

    private void SetExpanded(bool expanded)
    {
        foreach (LoadWarningNode root in Roots.Where(r => r.HasChildren))
        {
            root.IsExpanded = expanded;
        }
    }

    private static LoadWarningNode CreateNode(LoadWarning warning)
    {
        if (warning.Items.Count == 0)
        {
            return new LoadWarningNode(null, warning.Message, string.Empty, warning.BookPath, -1, -1, warning.Message);
        }

        string title = $"{warning.Title ?? warning.Message} ({warning.Items.Count})";
        LoadWarningNode group = new(null, title, string.Empty, null, -1, -1, warning.Message);
        foreach (LoadWarningItem item in warning.Items)
        {
            _ = new LoadWarningNode(group, item.BookPath ?? string.Empty, item.Reason, item.BookPath, item.Line, item.Column, item.Reason);
        }

        return group;
    }
}

/// <summary>
/// A node of the "Load warnings" tree: a warning (a group of files or a single warning) or one file of a group. The
/// <see cref="Reason"/> and <see cref="Location"/> columns are empty for a warning.
/// </summary>
public sealed partial class LoadWarningNode : ObservableObject
{
    private readonly List<LoadWarningNode> _children = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Creates the node and appends it to the children of <paramref name="parent"/>.</summary>
    internal LoadWarningNode(
        LoadWarningNode? parent, string text, string reason, string? bookPath, int line, int column, string toolTip)
    {
        Parent = parent;
        Text = text;
        Reason = reason;
        BookPath = bookPath;
        Line = line;
        Column = column;
        ToolTip = toolTip;
        parent?._children.Add(this);
    }

    /// <summary>The parent node, or <c>null</c> for a warning.</summary>
    public LoadWarningNode? Parent { get; }

    /// <summary>The file (bookpath) of a file node; the heading with the file count, or the message, of a warning.</summary>
    public string Text { get; }

    /// <summary>Why the file is reported; empty for a warning.</summary>
    public string Reason { get; }

    /// <summary>
    /// Bookpath of the file to open: the file of a file node, or the one file a single warning is about; <c>null</c> for
    /// a warning that is not about a file (or a group of files).
    /// </summary>
    public string? BookPath { get; }

    /// <summary>Line of the problem (1-based), or <c>-1</c> when unknown.</summary>
    public int Line { get; }

    /// <summary>Column of the problem (1-based), or <c>-1</c> when unknown.</summary>
    public int Column { get; }

    /// <summary>The "Location" column: <c>line:column</c>, or empty when the position is unknown.</summary>
    public string Location => Line > 0 ? (Column > 0 ? $"{Line}:{Column}" : Line.ToString(System.Globalization.CultureInfo.InvariantCulture)) : string.Empty;

    /// <summary>The tooltip: the whole sentence of a warning, the reason of a file.</summary>
    public string ToolTip { get; }

    /// <summary>Whether the node has children (and so an expander).</summary>
    public bool HasChildren => _children.Count > 0;

    /// <summary>The child nodes (the files of a warning).</summary>
    public IReadOnlyList<LoadWarningNode> Children => _children;
}
