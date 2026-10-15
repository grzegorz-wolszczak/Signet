using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.Footnotes;

namespace Signet.App.ViewModels;

/// <summary>What the footnote window was closed with.</summary>
/// <param name="Content">The text of the footnote (an XHTML fragment).</param>
/// <param name="Placement">Local (end of the file) or global (the book's notes file).</param>
/// <param name="Remove"><c>true</c> — the edited footnote is to be removed (the other values are its current ones).</param>
public sealed record FootnoteRequest(string Content, FootnotePlacement Placement, bool Remove = false);

/// <summary>
/// Model of the footnote window (Code Editor context menu), for adding a footnote at the caret or editing the footnote
/// under it: the text around the reference, the text of the footnote (paragraphs separated by a blank line) with live
/// validation and its placement, and a live preview of the markup Signet writes. An edited footnote can also be removed
/// (after a confirmation, unless it was turned off).
/// </summary>
public sealed partial class FootnoteViewModel : ObservableObject
{
    // How much text the window shows on each side of the caret.
    private const int ContextLength = 90;

    // How much source is read on each side of the caret to get that much text.
    private const int SourceWindow = 1500;

    // Block tags separate words; inline ones (<b>, <a>, …) do not.
    private static readonly Regex BlockTag = new(
        @"<(?:/?)(?:p|div|h[1-6]|li|ul|ol|dd|dt|dl|br|hr|body|section|aside|blockquote|table|tr|td|th)\b[^>]*>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private readonly Func<string, FootnoteError> _validate;
    private readonly int _localNumber;
    private readonly int _globalNumber;
    private readonly string _originalContent = string.Empty;
    private readonly bool _originalGlobal;
    private bool _contentValid;
    private Func<string, FootnotePlacement, int, FootnoteMarkup?>? _preview;

    /// <summary>Creates the model for adding a footnote at <paramref name="offset"/> of the XHTML source <paramref name="text"/>.</summary>
    /// <param name="text">The source of the file.</param>
    /// <param name="offset">Where the reference goes.</param>
    /// <param name="placement">The initial placement.</param>
    /// <param name="localNumber">The number the footnote gets as a local one.</param>
    /// <param name="globalNumber">The number the footnote gets as a global one.</param>
    /// <param name="validate">Checks the text of the footnote.</param>
    public FootnoteViewModel(
        string text, int offset, FootnotePlacement placement, int localNumber, int globalNumber, Func<string, FootnoteError> validate)
        : this(text, offset, offset, placement, localNumber, globalNumber, validate)
    {
    }

    private FootnoteViewModel(
        string text, int start, int end, FootnotePlacement placement, int localNumber, int globalNumber,
        Func<string, FootnoteError> validate)
    {
        ArgumentNullException.ThrowIfNull(text);
        _localNumber = localNumber;
        _globalNumber = globalNumber;
        _validate = validate ?? throw new ArgumentNullException(nameof(validate));
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        (ContextBefore, ContextAfter) = Context(text, start, end);
        _isGlobal = placement == FootnotePlacement.Global;
        _originalGlobal = _isGlobal;
    }

    private FootnoteViewModel(
        string text, int referenceStart, int referenceEnd, FootnoteContent content, FootnotePlacement placement,
        int localNumber, int globalNumber, Func<string, FootnoteError> validate)
        : this(text, referenceStart, referenceEnd, placement, localNumber, globalNumber, validate)
    {
        IsEditing = true;
        IsWholeNote = content.IsWholeNote;
        _originalContent = content.Text.Trim();
        Content = content.Text;
    }

    /// <summary>
    /// Creates the model for editing a footnote whose reference is <paramref name="referenceStart"/>..<paramref name="referenceEnd"/>
    /// of <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The source of the file of the reference.</param>
    /// <param name="referenceStart">Where the reference starts.</param>
    /// <param name="referenceEnd">Where the reference ends.</param>
    /// <param name="content">The current text of the note.</param>
    /// <param name="placement">The current placement.</param>
    /// <param name="localNumber">The number of the footnote as a local one.</param>
    /// <param name="globalNumber">The number of the footnote as a global one.</param>
    /// <param name="validate">Checks the new text of the note.</param>
    public static FootnoteViewModel ForEdit(
        string text, int referenceStart, int referenceEnd, FootnoteContent content, FootnotePlacement placement,
        int localNumber, int globalNumber, Func<string, FootnoteError> validate)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new FootnoteViewModel(text, referenceStart, referenceEnd, content, placement, localNumber, globalNumber, validate);
    }

    /// <summary>Whether an existing footnote is edited (otherwise a new one is added).</summary>
    public bool IsEditing { get; }

    /// <summary>
    /// Whether <see cref="Content"/> is the whole inside of the note, its link back included (a note not in Signet's
    /// template).
    /// </summary>
    public bool IsWholeNote { get; }

    /// <summary>The title of the window.</summary>
    public string Title => Strings.Get(IsEditing ? "FootnoteWindow_EditTitle" : "FootnoteWindow_Title");

    /// <summary>The caption of the accepting button.</summary>
    public string AcceptText => Strings.Get(IsEditing ? "FootnoteWindow_SaveButton" : "FootnoteWindow_AddButton");

    /// <summary>The hint under the text of the footnote.</summary>
    public string ContentHint => Strings.Get(IsWholeNote ? "FootnoteWindow_ContentHintWholeNote" : "FootnoteWindow_ContentHint");

    /// <summary>Whether "Remove" asks for a confirmation first (Preferences → Footnotes).</summary>
    public bool ConfirmRemoval { get; set; } = true;

    /// <summary>Called when "Don't ask again" was checked in the confirmation of "Remove".</summary>
    public Action? StopConfirmingRemoval { get; set; }

    /// <summary>The text right before the reference (tags left out).</summary>
    public string ContextBefore { get; }

    /// <summary>The text right after the reference (tags left out).</summary>
    public string ContextAfter { get; }

    /// <summary>The marker the reference gets for the chosen placement, e.g. <c>[3]</c> (shown between the context texts).</summary>
    public string Marker => "[" + (IsGlobal ? _globalNumber : _localNumber).ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>The text of the footnote.</summary>
    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary><c>true</c> — global (the book's notes file); <c>false</c> — local (the end of this file).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    [NotifyPropertyChangedFor(nameof(Marker))]
    private bool _isGlobal;

    /// <summary>Inverse of <see cref="IsGlobal"/> (for the "local" radio button).</summary>
    public bool IsLocal
    {
        get => !IsGlobal;
        set => IsGlobal = !value;
    }

    /// <summary>Why the text cannot be used, or an empty string (also for an empty text, which only disables OK).</summary>
    [ObservableProperty]
    private string _validationError = string.Empty;

    /// <summary>
    /// Whether the window can be accepted — the text is not empty and valid, and when editing, the text or the placement
    /// differs from the footnote's.
    /// </summary>
    [ObservableProperty]
    private bool _canAccept;

    /// <summary>
    /// Builds the preview from the text, the placement and the number (<c>null</c> — no preview); set once the model is
    /// created, and run on every change of the text or the placement.
    /// </summary>
    public Func<string, FootnotePlacement, int, FootnoteMarkup?>? Preview
    {
        get => _preview;
        set
        {
            _preview = value;
            UpdatePreview();
        }
    }

    /// <summary>The markup of the preview: the reference and the note, or an empty string while the text is not valid.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string _previewCode = string.Empty;

    /// <summary>The page of the "Appearance" preview (<see cref="FootnoteMarkup.Page"/>), or <c>null</c>.</summary>
    [ObservableProperty]
    private string? _previewPage;

    /// <summary>Whether there is a preview (the text is valid).</summary>
    public bool HasPreview => PreviewCode.Length > 0;

    /// <summary>The accepted request.</summary>
    public FootnoteRequest Request() =>
        new(Content.Trim(), IsGlobal ? FootnotePlacement.Global : FootnotePlacement.Local);

    /// <summary>The request to remove the edited footnote.</summary>
    public FootnoteRequest RemoveRequest() =>
        new(_originalContent, _originalGlobal ? FootnotePlacement.Global : FootnotePlacement.Local, Remove: true);

    /// <summary>The message for <paramref name="error"/> (also the reason in the context menu).</summary>
    public static string ErrorText(FootnoteError error) => Strings.Get("FootnoteError_" + error);

    partial void OnContentChanged(string value)
    {
        FootnoteError error = _validate(value);
        _contentValid = error == FootnoteError.None;
        UpdateCanAccept();
        string message = error is FootnoteError.None or FootnoteError.EmptyContent ? string.Empty : ErrorText(error);
        ValidationError = message.Length == 0 ? message : char.ToUpper(message[0], CultureInfo.CurrentCulture) + message[1..];
        UpdatePreview();
    }

    partial void OnIsGlobalChanged(bool value)
    {
        UpdateCanAccept();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        FootnoteMarkup? markup = _contentValid && _preview is not null
            ? _preview(Content, IsGlobal ? FootnotePlacement.Global : FootnotePlacement.Local, IsGlobal ? _globalNumber : _localNumber)
            : null;
        PreviewCode = markup is null ? string.Empty : markup.Reference + "\n\n" + markup.Note;
        PreviewPage = markup?.Page;
    }

    private void UpdateCanAccept() =>
        CanAccept = _contentValid && (!IsEditing || IsGlobal != _originalGlobal || Content.Trim() != _originalContent);

    // The plain text before `offset` and after `end` (the caret, or the range of the reference): tags are left out,
    // entities decoded and whitespace collapsed. The text before does not reach into <head>.
    internal static (string Before, string After) Context(string text, int offset, int end)
    {
        int body = text.Length == 0 ? -1 : text.LastIndexOf("<body", Math.Min(offset, text.Length - 1), StringComparison.OrdinalIgnoreCase);
        int start = Math.Max(Math.Max(0, offset - SourceWindow), body);
        string before = text[start..offset];
        if (before.IndexOf('>', StringComparison.Ordinal) is var close and >= 0
            && (before.IndexOf('<', StringComparison.Ordinal) is var open && (open < 0 || close < open)))
        {
            before = before[(close + 1)..];
        }

        string after = text[end..Math.Min(text.Length, end + SourceWindow)];
        if (after.LastIndexOf('<') is var lastOpen and >= 0 && after.IndexOf('>', lastOpen) < 0)
        {
            after = after[..lastOpen];
        }

        string plainBefore = Plain(before).TrimStart();
        string plainAfter = Plain(after).TrimEnd();
        if (plainBefore.Length > ContextLength)
        {
            plainBefore = "…" + plainBefore[^ContextLength..];
        }

        if (plainAfter.Length > ContextLength)
        {
            plainAfter = plainAfter[..ContextLength] + "…";
        }

        return (plainBefore, plainAfter);
    }

    private static string Plain(string source)
    {
        string text = WebUtility.HtmlDecode(Tag.Replace(BlockTag.Replace(source, " "), string.Empty));
        return Whitespace.Replace(text, " ");
    }
}
