using System.Diagnostics.CodeAnalysis;

namespace Signet.EpubCheck.Core.Messages;

/// <summary>A message as the dictionary holds it: its id, its (possibly overridden) severity and its texts.</summary>
/// <param name="Id">The id of the message.</param>
/// <param name="Severity">The severity the message is reported with (the default, or the one a custom messages file set).</param>
/// <param name="OriginalSeverity">The default severity; differs from <paramref name="Severity"/> when it was overridden.</param>
/// <param name="Text">The text, with the placeholders of its arguments (<c>%1$s</c>).</param>
/// <param name="Suggestion">The suggestion of how to fix the problem, with the placeholders of its arguments; empty when there is none.</param>
public sealed record Message(MessageId Id, Severity Severity, Severity OriginalSeverity, string Text, string Suggestion)
{
    /// <summary>A message that keeps its default severity.</summary>
    public Message(MessageId id, Severity severity, string text, string suggestion)
        : this(id, severity, severity, text, suggestion)
    {
    }
}

/// <summary>Gives the <see cref="Message"/> of an id and fills its text (upstream's <c>MessageDictionary</c>).</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Keeps the name of the upstream type (MessageDictionary).")]
public interface IMessageDictionary
{
    /// <summary>The message of <paramref name="id"/>.</summary>
    Message GetMessage(MessageId id);

    /// <summary>The text of <paramref name="message"/> with its placeholders filled by <paramref name="args"/>.</summary>
    string Format(Message message, params object?[] args);
}
