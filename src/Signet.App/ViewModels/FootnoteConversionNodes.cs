using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Footnotes;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// A row of the tree of the "Convert Footnotes of Other Origin" dialog: a file with footnotes of other origin, a
/// footnote (<see cref="FootnoteConversionItemViewModel"/>) or another link to a footnote. The columns show
/// <see cref="Title"/>, <see cref="Kind"/>, <see cref="Reference"/> and <see cref="Note"/>; the panel under the tree
/// shows <see cref="DetailTitle"/>, <see cref="DetailWarnings"/>, <see cref="DetailNotices"/> and <see cref="Places"/> of the
/// selected row.
/// </summary>
public abstract partial class FootnoteConversionNode : ObservableObject
{
    private static readonly IReadOnlyList<FootnoteConversionNode> NoChildren = Array.Empty<FootnoteConversionNode>();

    private IReadOnlyList<FootnoteConversionPlace>? _places;

    /// <summary>Whether the row's children are shown.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>The text of the first column.</summary>
    public abstract string Title { get; }

    /// <summary>Local or global (footnotes only).</summary>
    public virtual string Kind => string.Empty;

    /// <summary>The reference column: the text around the reference or the link.</summary>
    public virtual string Reference => string.Empty;

    /// <summary>The note column: the start of the text of the note.</summary>
    public virtual string Note => string.Empty;

    /// <summary>Whether the row needs a look (marked ⚠).</summary>
    public virtual bool HasWarnings => false;

    /// <summary>Whether the row has a check box.</summary>
    public virtual bool HasCheckBox => false;

    /// <summary>Whether its check box can be changed.</summary>
    public virtual bool CanCheck => false;

    /// <summary>The state of its check box; <c>null</c> = some of the footnotes under it are checked.</summary>
    public virtual bool? Check
    {
        get => false;
        set { }
    }

    /// <summary>The rows under it.</summary>
    public virtual IReadOnlyList<FootnoteConversionNode> Children => NoChildren;

    /// <summary>Whether it has rows under it (the expander).</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>The heading of the panel under the tree.</summary>
    public abstract string DetailTitle { get; }

    /// <summary>What needs a look, or why it cannot be converted (the panel under the tree).</summary>
    public virtual IReadOnlyList<string> DetailWarnings => Array.Empty<string>();

    /// <summary>What the conversion does besides the usual, for information (the panel under the tree).</summary>
    public virtual IReadOnlyList<string> DetailNotices => Array.Empty<string>();

    /// <summary>The places of the row (file, line, text) with "Go to" — computed when the row is first shown in the panel.</summary>
    public IReadOnlyList<FootnoteConversionPlace> Places => _places ??= CreatePlaces();

    /// <summary>A double click in the reference column: opens the reference or the link in the code.</summary>
    public abstract void GoToReference();

    /// <summary>A double click in the note column: opens the note in the code (when the row has one).</summary>
    public virtual void GoToNote()
    {
    }

    /// <summary>The text of the first column in full (its tooltip).</summary>
    public virtual string TitleTip => Title;

    /// <summary>Creates <see cref="Places"/>.</summary>
    protected abstract IReadOnlyList<FootnoteConversionPlace> CreatePlaces();

    // How much of the text before a reference a cell shows, so that the reference itself stays in sight.
    private const int CellBeforeLength = 30;

    /// <summary>
    /// "…before [marker] after…" — in full for the details, or with the text before shortened for a cell (the cell cuts
    /// its end, and the reference should stay in sight).
    /// </summary>
    internal static string ReferenceText(string before, string marker, string after, bool cell)
    {
        if (cell && before.Length > CellBeforeLength)
        {
            before = "…" + before[^CellBeforeLength..].TrimStart();
        }

        return before + "[" + marker + "]" + after;
    }

    /// <summary>A place in a file: "path, line N" (a negative offset = the file without a place in it).</summary>
    internal static string Location(Resource file, int offset)
    {
        if (offset < 0 || file is not TextResource text)
        {
            return file.BookPath;
        }

        string content = text.GetText();
        int end = Math.Min(offset, content.Length);
        int line = 1;
        for (int i = content.IndexOf('\n', 0, end); i >= 0; i = content.IndexOf('\n', i + 1, end - i - 1))
        {
            line++;
        }

        return Strings.Format("FootnoteConversionWindow_Location", file.BookPath, line.ToString(CultureInfo.CurrentCulture));
    }
}

/// <summary>A place of a row in the panel under the tree: what it is, where, its text and "Go to".</summary>
/// <param name="Label">"Reference", "Note", "Link"…</param>
/// <param name="Location">The file and the line.</param>
/// <param name="Text">The text there (the context of a reference, the whole text of a note).</param>
/// <param name="GoTo">Opens the place in the code.</param>
public sealed record FootnoteConversionPlace(string Label, string Location, string Text, IRelayCommand GoTo);

/// <summary>A file with footnotes of other origin: its check box checks the footnotes under it.</summary>
public sealed class FootnoteConversionFileNode : FootnoteConversionNode
{
    private readonly FootnoteConversionViewModel _owner;
    private readonly HtmlResource _file;
    private readonly IReadOnlyList<FootnoteConversionItemViewModel> _items;

    internal FootnoteConversionFileNode(
        FootnoteConversionViewModel owner, HtmlResource file, IReadOnlyList<FootnoteConversionItemViewModel> items)
    {
        _owner = owner;
        _file = file;
        _items = items;
        IsExpanded = true;
        int warnings = items.Count(i => i.HasWarnings);
        string count = items.Count.ToString(CultureInfo.CurrentCulture);
        string Format(string name) => warnings == 0
            ? Strings.Format("FootnoteConversionWindow_FileTitle", name, count)
            : Strings.Format("FootnoteConversionWindow_FileTitleWarnings", name, count, warnings.ToString(CultureInfo.CurrentCulture));
        Title = Format(file.Filename);
        TitleTip = Format(file.BookPath);
    }

    /// <summary>The name of the file and how many footnotes (and ⚠) are under it.</summary>
    public override string Title { get; }

    /// <summary>The same with the path of the file in the book (the details, the tooltip).</summary>
    public override string TitleTip { get; }

    /// <summary>The file.</summary>
    public HtmlResource File => _file;

    /// <inheritdoc />
    public override IReadOnlyList<FootnoteConversionNode> Children => _items;

    /// <inheritdoc />
    public override bool HasWarnings => _items.Any(i => i.HasWarnings);

    /// <inheritdoc />
    public override bool HasCheckBox => true;

    /// <inheritdoc />
    public override bool CanCheck => _items.Any(i => i.CanConvert);

    /// <inheritdoc />
    public override bool? Check
    {
        get
        {
            int convertible = 0;
            int checkedCount = 0;
            foreach (FootnoteConversionItemViewModel item in _items)
            {
                if (item.CanConvert)
                {
                    convertible++;
                    checkedCount += item.IsChecked ? 1 : 0;
                }
            }

            return checkedCount == 0 ? false : checkedCount == convertible ? true : null;
        }

        set => _owner.SetChecked(_items, value == true);
    }

    /// <inheritdoc />
    public override string DetailTitle => TitleTip;

    /// <summary>Announces that the footnotes under it were checked or unchecked.</summary>
    internal void RefreshCheck() => OnPropertyChanged(nameof(Check));

    /// <inheritdoc />
    public override void GoToReference() => _owner.NavigateTo(_file, -1);

    /// <inheritdoc />
    protected override IReadOnlyList<FootnoteConversionPlace> CreatePlaces() => new[]
    {
        new FootnoteConversionPlace(
            Strings.Get("FootnoteConversionWindow_PlaceFile"), _file.BookPath, string.Empty, new RelayCommand(GoToReference)),
    };
}

/// <summary>
/// Another link to a footnote of other origin (<see cref="ConversionLink"/>), under the footnote: another reference to
/// its note (why it cannot be converted) or a link the conversion rewrites.
/// </summary>
public sealed partial class FootnoteConversionLinkNode : FootnoteConversionNode
{
    private readonly FootnoteConversionViewModel _owner;
    private readonly ConversionLink _link;
    private readonly HtmlResource _footnoteFile;
    private (string Before, string Marker, string After)? _context;

    internal FootnoteConversionLinkNode(FootnoteConversionViewModel owner, ConversionLink link, HtmlResource footnoteFile)
    {
        _owner = owner;
        _link = link;
        _footnoteFile = footnoteFile;
        Title = Strings.Get(link.IsReference ? "FootnoteConversionWindow_OtherReference" : "FootnoteConversionWindow_OtherLink");
    }

    /// <summary>The link.</summary>
    public ConversionLink Link => _link;

    /// <inheritdoc />
    public override string Title { get; }

    /// <inheritdoc />
    public override bool HasWarnings => _link.IsReference;

    /// <summary>The text around the link, with the file in front when it is not the footnote's.</summary>
    public override string Reference
    {
        get
        {
            string context = ContextText(cell: true);
            return ReferenceEquals(_link.File, _footnoteFile) ? context : _link.File.Filename + ": " + context;
        }
    }

    /// <inheritdoc />
    public override string DetailTitle => Title;

    /// <inheritdoc />
    public override void GoToReference() => _owner.NavigateTo(_link.File, _link.Offset);

    /// <inheritdoc />
    protected override IReadOnlyList<FootnoteConversionPlace> CreatePlaces() => new[]
    {
        new FootnoteConversionPlace(Title, Location(_link.File, _link.Offset), ContextText(cell: false), new RelayCommand(GoToReference)),
    };

    // "…text before [link text] text after…"; empty for a link outside a text (the guide of the OPF).
    private string ContextText(bool cell)
    {
        _context ??= LinkContext(_link);
        return _context is ({ } before, { } marker, { } after) && marker.Length + before.Length + after.Length > 0
            ? ReferenceText(before, marker, after, cell)
            : string.Empty;
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    private static (string Before, string Marker, string After) LinkContext(ConversionLink link)
    {
        if (link.Offset < 0 || link.File is not TextResource resource)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        string text = resource.GetText();
        int open = text.IndexOf('>', link.Offset);
        int close = open < 0 ? -1 : text.IndexOf("</a>", open, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        string inner = System.Net.WebUtility.HtmlDecode(Tag().Replace(text[(open + 1)..close], string.Empty)).Trim();
        (string before, string after) = FootnoteViewModel.Context(text, link.Offset, close + "</a>".Length);
        return (before, inner, after);
    }
}
