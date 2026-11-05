using System;
using System.IO;
using System.Text;

namespace Signet.EpubCheck.Core.Messages;

/// <summary>Writes every message of a dictionary as tab-separated lines (<c>--listChecks</c>; upstream's <c>MessageDictionaryDumper</c>).</summary>
public sealed class MessageDictionaryDumper
{
    private readonly IMessageDictionary _dictionary;

    /// <summary>Creates a dumper of <paramref name="dictionary"/>.</summary>
    public MessageDictionaryDumper(IMessageDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        _dictionary = dictionary;
    }

    /// <summary>
    /// Writes the header <c>ID&lt;TAB&gt;Severity&lt;TAB&gt;Message&lt;TAB&gt;Suggestion</c> and a line per message id, in the
    /// order of <see cref="MessageIds.All"/>; each line ends with <c>\n</c> whatever the platform. The result can be
    /// given back as a custom messages file.
    /// </summary>
    public void Dump(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("ID\tSeverity\tMessage\tSuggestion\n");
        foreach (MessageId id in MessageIds.All)
        {
            Message message = _dictionary.GetMessage(id);
            StringBuilder line = new();
            line.Append(id.ToText()).Append('\t')
                .Append(message.Severity.ToText()).Append('\t')
                .Append(message.Text).Append('\t')
                .Append(message.Suggestion).Append('\n');
            writer.Write(line.ToString());
        }
    }
}
