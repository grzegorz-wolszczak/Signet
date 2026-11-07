using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.Core.Resources;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The automatic fixes ("Fix" in the Validation Results panel) that go with the problems EPUBCheck reports. A fix is bound to a
/// message id and made from the arguments of the message, so it does not depend on the language of the text.
/// </summary>
internal sealed partial class EpubCheckFixes
{
    private readonly Book _book;
    private Dictionary<string, string>? _byLowerCasePath;

    public EpubCheckFixes(Book book)
    {
        _book = book;
    }

    /// <summary>The fix for the problem, or <c>null</c> when there is none.</summary>
    /// <param name="id">The message id.</param>
    /// <param name="location">Where the message points.</param>
    /// <param name="args">The arguments of the message.</param>
    public ValidationFix? Create(MessageId id, EpubLocation location, IReadOnlyList<object?> args)
    {
        string? first = args.Count > 0 ? args[0]?.ToString() : null;
        return id switch
        {
            // A file the book has but the OPF manifest does not list.
            MessageId.RSC_008 or MessageId.OPF_003 => first is not null && Has(first) ? new AddToManifestFix(first) : null,

            // A reference to a file that is not there: when the book has it under a name that differs in letter case only.
            MessageId.RSC_007 or MessageId.RSC_007w => CaseFix(location, first),

            // The package document's unique identifier is missing or empty.
            MessageId.OPF_030 => new UniqueIdentifierFix(),
            MessageId.RSC_005 => SchemaFix(location, first),
            _ => null,
        };
    }

    private bool Has(string bookPath) => _book.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath) is not null;

    private ReferenceCaseFix? CaseFix(EpubLocation location, string? missing)
    {
        if (missing is null || !Has(location.Path) || Has(missing))
        {
            return null;
        }

        _byLowerCasePath ??= BuildLowerCaseIndex();
        return _byLowerCasePath.TryGetValue(missing.ToLowerInvariant(), out string? actual)
            ? new ReferenceCaseFix(location.Path, missing, actual)
            : null;
    }

    // The error of a schema: the ones Signet can repair are recognized by the names in the schema's own text (an id that is
    // not unique, the identifier of the publication), which the port gives in the schema's wording in every language.
    private ValidationFix? SchemaFix(EpubLocation location, string? text)
    {
        if (text is null)
        {
            return null;
        }

        Resource? resource = _book.GetFolderKeeper().GetResourceByBookPathNoThrow(location.Path);
        if (resource is HtmlResource && DuplicateId().Match(text) is { Success: true } duplicate)
        {
            return new DuplicateIdFix(location.Path, duplicate.Groups[1].Value);
        }

        if (resource is OpfResource && (text.Contains("dc:identifier", StringComparison.Ordinal) || text.Contains("unique-identifier", StringComparison.Ordinal)))
        {
            return new UniqueIdentifierFix();
        }

        return null;
    }

    private Dictionary<string, string> BuildLowerCaseIndex()
    {
        Dictionary<string, string> index = new(StringComparer.Ordinal);
        foreach (string bookPath in _book.GetAllResources().Select(r => r.BookPath))
        {
            index.TryAdd(bookPath.ToLowerInvariant(), bookPath);
        }

        return index;
    }

    [GeneratedRegex("^Duplicate ID \"(.+)\"$")]
    private static partial Regex DuplicateId();
}
