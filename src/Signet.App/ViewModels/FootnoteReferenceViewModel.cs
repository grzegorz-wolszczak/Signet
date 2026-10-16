using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.Footnotes;

namespace Signet.App.ViewModels;

/// <summary>A note in the "Add Reference to Footnote" window: its number, local or global, its file and its text.</summary>
public sealed class FootnoteReferenceRow
{
    // How much of the note the row shows.
    private const int TextLength = 160;

    internal FootnoteReferenceRow(ReferenceTarget target)
    {
        Target = target;
        bool global = target.Footnote.Placement == FootnotePlacement.Global;
        Number = "[" + target.Number.ToString(CultureInfo.InvariantCulture) + "]";
        Kind = Strings.Get(global ? "FootnoteConversionWindow_Global" : "FootnoteConversionWindow_Local");
        File = global ? target.Footnote.ReferenceFile.Filename : string.Empty;
        Text = target.Text.Length > TextLength ? target.Text[..TextLength] + "…" : target.Text;
    }

    /// <summary>The note (its first reference) and its number.</summary>
    public ReferenceTarget Target { get; }

    /// <summary>"[4]".</summary>
    public string Number { get; }

    /// <summary>"local" / "global".</summary>
    public string Kind { get; }

    /// <summary>The file of the first reference of a global note (empty for a local one: it is this file).</summary>
    public string File { get; }

    /// <summary>The start of the text of the note.</summary>
    public string Text { get; }

    /// <summary>Whether the row matches the filter: its number, file or text contains it (case-insensitive).</summary>
    public bool Matches(string filter) =>
        filter.Length == 0
        || Number.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || File.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || Target.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>
/// The "Add Reference to Footnote" window: the Signet notes a reference at the caret can lead to (the local notes of the
/// file and the global notes), filtered by a text; the note with the nearest reference before the caret is selected at
/// the start. Accepting adds a reference to the selected note: the note becomes shared (<c>[4a]</c>, <c>[4b]</c>).
/// </summary>
public sealed partial class FootnoteReferenceViewModel : ObservableObject
{
    private readonly List<FootnoteReferenceRow> _rows;

    /// <summary>Creates the model for <paramref name="targets"/>.</summary>
    public FootnoteReferenceViewModel(ReferenceTargetList targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        _rows = targets.Targets.Select(t => new FootnoteReferenceRow(t)).ToList();
        Visible = new ObservableCollection<FootnoteReferenceRow>(_rows);
        _selected = targets.Nearest >= 0 ? _rows[targets.Nearest] : null;
    }

    /// <summary>The rows that match <see cref="Filter"/>.</summary>
    public ObservableCollection<FootnoteReferenceRow> Visible { get; }

    /// <summary>The text the rows are filtered by.</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>The chosen note.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    private FootnoteReferenceRow? _selected;

    /// <summary>Whether a note is chosen.</summary>
    public bool CanAccept => Selected is not null;

    /// <summary>"Showing N of M notes".</summary>
    public string Summary => Strings.Format(
        "FootnoteReferenceWindow_Summary", Visible.Count.ToString(CultureInfo.CurrentCulture), _rows.Count.ToString(CultureInfo.CurrentCulture));

    partial void OnFilterChanged(string value)
    {
        string filter = value.Trim();
        FootnoteReferenceRow? selected = Selected;
        Visible.Clear();
        foreach (FootnoteReferenceRow row in _rows.Where(r => r.Matches(filter)))
        {
            Visible.Add(row);
        }

        Selected = selected is not null && Visible.Contains(selected) ? selected : Visible.FirstOrDefault();
        OnPropertyChanged(nameof(Summary));
    }
}
