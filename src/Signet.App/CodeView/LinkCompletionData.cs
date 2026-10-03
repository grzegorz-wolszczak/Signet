using System;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Signet.Core.MainUI;

namespace Signet.App.CodeView;

/// <summary>
/// Link completion window item: a file href or an anchor id, with a description
/// (file kind / element text).
/// </summary>
internal sealed class LinkCompletionData : ICompletionData
{
    public LinkCompletionData(LinkCompletionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Text = item.Text;
        Description = item.Description;
    }

    /// <inheritdoc />
    public IImage? Image => null;

    /// <inheritdoc />
    public string Text { get; }

    /// <inheritdoc />
    public object Content => Description is string { Length: > 0 } d ? $"{Text}    {d}" : Text;

    /// <inheritdoc />
    public object Description { get; }

    /// <inheritdoc />
    public double Priority => 0;

    /// <inheritdoc />
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        textArea.Document.Replace(completionSegment, Text);
    }
}
