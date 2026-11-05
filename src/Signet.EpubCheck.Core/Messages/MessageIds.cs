using System;
using System.Collections.Generic;

namespace Signet.EpubCheck.Core.Messages;

public static partial class MessageIds
{
    private static readonly Dictionary<string, MessageId> s_byText = BuildIndex();

    private static readonly HashSet<MessageId> s_localizedArgument = [.. s_withLocalizedArgument];

    /// <summary>All the ids, in the order of upstream's <c>MessageId</c> (the order of the <c>--listChecks</c> dump).</summary>
    public static IReadOnlyList<MessageId> All { get; } = Enum.GetValues<MessageId>();

    /// <summary>The text form of the id: three letters, a hyphen, three digits and sometimes a letter (<c>OPF-014</c>, <c>OPF-004a</c>).</summary>
    public static string ToText(this MessageId id)
    {
        int index = (int)id;
        return index >= 0 && index < s_texts.Length ? s_texts[index] : throw new ArgumentOutOfRangeException(nameof(id), id, null);
    }

    /// <summary>
    /// Parses the text form of an id; <c>false</c> when it names no id. Upstream prints 23 ids with an underscore by mistake
    /// (<c>HTM_053</c>, <c>MED_006</c>, …); that spelling is accepted too, so the custom messages files made from upstream's
    /// <c>--listChecks</c> keep working.
    /// </summary>
    public static bool TryParse(string? text, out MessageId id)
    {
        id = default;
        return text is not null && (s_byText.TryGetValue(text, out id) || s_byText.TryGetValue(text.Replace('_', '-'), out id));
    }

    /// <summary>Whether the first argument of the message is a localized string (upstream: <c>LOCALIZED_ARG.YES</c>).</summary>
    public static bool HasLocalizedArgument(this MessageId id) => s_localizedArgument.Contains(id);

    private static Dictionary<string, MessageId> BuildIndex()
    {
        Dictionary<string, MessageId> index = new(s_texts.Length, StringComparer.Ordinal);
        for (int i = 0; i < s_texts.Length; i++)
        {
            index.Add(s_texts[i], (MessageId)i);
        }

        return index;
    }
}
