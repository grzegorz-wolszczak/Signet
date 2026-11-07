using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Signet.EpubCheck.Core.Jing.Sax;

/// <summary>
/// The wording of Xerces for the well-formedness errors that <see cref="XmlReader"/> words in its own way. EPUBCheck reports the
/// message of the parser as it is (<c>RSC-016</c>) and its scenarios check some of them, so the errors that have a message there are
/// worded as Xerces words them; any other error keeps the text of <see cref="XmlReader"/>.
/// </summary>
internal static partial class XercesMessages
{
    private const string UndeclaredEntityPrefix = "Reference to undeclared entity '";

    /// <summary>
    /// Xerces' message for <paramref name="error"/> in the document <paramref name="source"/>, or <c>null</c> when the error is
    /// not one that has a message of Xerces here.
    /// </summary>
    public static string? Translate(XmlException error, InputSource source)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(source);
        string message = error.Message;

        // "The entity "{0}" was referenced, but not declared."
        int start = message.IndexOf(UndeclaredEntityPrefix, StringComparison.Ordinal);
        if (start >= 0)
        {
            int nameStart = start + UndeclaredEntityPrefix.Length;
            int end = message.IndexOf('\'', nameStart);
            if (end > nameStart)
            {
                return $"The entity \"{message[nameStart..end]}\" was referenced, but not declared.";
            }
        }

        // "The reference to entity "{0}" must end with the ';' delimiter."
        if (MissingSemicolon().IsMatch(message) && EntityNameBefore(source, error.LineNumber, error.LinePosition) is { } name)
        {
            return $"The reference to entity \"{name}\" must end with the ';' delimiter.";
        }

        return null;
    }

    // The name of the entity reference the offending token (at the 1-based line and position) follows, if there is one.
    private static string? EntityNameBefore(InputSource source, int lineNumber, int linePosition)
    {
        if (lineNumber < 1 || linePosition < 1)
        {
            return null;
        }

        string? line;
        try
        {
            using Stream stream = source.Open();
            using StreamReader reader = new(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            line = null;
            for (int i = 1; i <= lineNumber; i++)
            {
                line = reader.ReadLine();
                if (line is null)
                {
                    return null;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }

        if (line is null || linePosition - 1 > line.Length)
        {
            return null;
        }

        Match match = EntityReferenceEnd().Match(line[..(linePosition - 1)]);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"is an unexpected token\. The expected token is ';'\.", RegexOptions.CultureInvariant)]
    private static partial Regex MissingSemicolon();

    // '&' and a name, at the end of the text before the token.
    [GeneratedRegex(@"&([\p{L}_:][\p{L}\p{N}._:\-]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityReferenceEnd();
}
