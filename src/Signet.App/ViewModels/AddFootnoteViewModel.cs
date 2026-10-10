using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.Footnotes;

namespace Signet.App.ViewModels;

/// <summary>What the "Add Footnote" window was accepted with.</summary>
/// <param name="Content">The text of the footnote (an XHTML fragment).</param>
/// <param name="Placement">Local (end of the file) or global (the book's notes file).</param>
public sealed record AddFootnoteRequest(string Content, FootnotePlacement Placement);

/// <summary>
/// Model of the "Add Footnote" window (Code View context menu): the text around the caret where the reference goes,
/// the text of the footnote with live validation (text with simple markup, well-formed XHTML) and its placement.
/// </summary>
public sealed partial class AddFootnoteViewModel : ObservableObject
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

    /// <summary>Creates the model for a footnote at <paramref name="offset"/> of the XHTML source <paramref name="text"/>.</summary>
    /// <param name="text">The source of the file.</param>
    /// <param name="offset">Where the reference goes.</param>
    /// <param name="placement">The initial placement.</param>
    /// <param name="localNumber">The number the footnote gets as a local one.</param>
    /// <param name="globalNumber">The number the footnote gets as a global one.</param>
    /// <param name="validate">Checks the text of the footnote.</param>
    public AddFootnoteViewModel(
        string text, int offset, FootnotePlacement placement, int localNumber, int globalNumber, Func<string, FootnoteError> validate)
    {
        _localNumber = localNumber;
        _globalNumber = globalNumber;
        ArgumentNullException.ThrowIfNull(text);
        _validate = validate ?? throw new ArgumentNullException(nameof(validate));
        offset = Math.Clamp(offset, 0, text.Length);
        (ContextBefore, ContextAfter) = Context(text, offset);
        _isGlobal = placement == FootnotePlacement.Global;
    }

    /// <summary>The text right before the caret (tags left out).</summary>
    public string ContextBefore { get; }

    /// <summary>The text right after the caret (tags left out).</summary>
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

    /// <summary>Whether the window can be accepted — the text is not empty and valid.</summary>
    [ObservableProperty]
    private bool _canAccept;

    /// <summary>The accepted request.</summary>
    public AddFootnoteRequest Request() =>
        new(Content.Trim(), IsGlobal ? FootnotePlacement.Global : FootnotePlacement.Local);

    /// <summary>The message for <paramref name="error"/> (also the reason in the context menu).</summary>
    public static string ErrorText(FootnoteError error) => Strings.Get("FootnoteError_" + error);

    partial void OnContentChanged(string value)
    {
        FootnoteError error = _validate(value);
        CanAccept = error == FootnoteError.None;
        string message = error is FootnoteError.None or FootnoteError.EmptyContent ? string.Empty : ErrorText(error);
        ValidationError = message.Length == 0 ? message : char.ToUpper(message[0], CultureInfo.CurrentCulture) + message[1..];
    }

    // The plain text around the caret: tags are left out, entities decoded and whitespace collapsed. The text before
    // the caret does not reach into <head>.
    private static (string Before, string After) Context(string text, int offset)
    {
        int body = text.Length == 0 ? -1 : text.LastIndexOf("<body", Math.Min(offset, text.Length - 1), StringComparison.OrdinalIgnoreCase);
        int start = Math.Max(Math.Max(0, offset - SourceWindow), body);
        string before = text[start..offset];
        if (before.IndexOf('>', StringComparison.Ordinal) is var close and >= 0
            && (before.IndexOf('<', StringComparison.Ordinal) is var open && (open < 0 || close < open)))
        {
            before = before[(close + 1)..];
        }

        string after = text[offset..Math.Min(text.Length, offset + SourceWindow)];
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
