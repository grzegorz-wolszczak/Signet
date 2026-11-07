using System;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Brings the rules a user skipped in Check Book up to date: some of Signet's own rules were replaced by EPUBCheck's messages, so a
/// code that was saved for a replaced rule becomes the code of the message that reports the same problem. A rule with no precise
/// counterpart (the replacing message would also hide other problems) is dropped: it no longer exists.
/// </summary>
public static class SkippedRuleMigration
{
    // The replaced rules and the EPUBCheck message ids that report the same situation. The checks of the OPF that EPUBCheck reports as
    // a schema error (RSC-005, which covers every schema problem) have no entry: skipping RSC-005 would hide far more.
    private static readonly Dictionary<string, string> s_replacements = new(StringComparer.Ordinal)
    {
        ["WellFormed_NotWellFormed"] = "RSC-016",
        ["Validation_DeadLink"] = "RSC-007",
        ["Validation_CssDeadUrl"] = "RSC-007",
        ["Validation_LinkCaseMismatch"] = "RSC-007",
        ["Validation_CssUrlCaseMismatch"] = "RSC-007",
        ["Validation_BadFragment"] = "RSC-012",
        ["Validation_BadFragmentSameFile"] = "RSC-012",
        ["Validation_LinkTargetNotInManifest"] = "RSC-008",
        ["Validation_CssUrlNotInManifest"] = "RSC-008",
        ["Validation_OpfDuplicateHref"] = "OPF-074",
        ["Validation_OpfItemrefUnknownIdref"] = "OPF-049",
        ["Validation_OpfItemrefNoIdref"] = "OPF-049",
        ["Validation_OpfNoUniqueIdentifier"] = "OPF-030",
        ["Validation_OpfUniqueIdentifierEmpty"] = "OPF-048",
    };

    // The replaced rules that are only reported through RSC-005 now.
    private static readonly HashSet<string> s_droppedWithoutCounterpart = new(StringComparer.Ordinal)
    {
        "Validation_OpfMetadataEmpty",
        "Validation_OpfManifestEmpty",
        "Validation_OpfSpineEmpty",
        "Validation_OpfManifestItemNoId",
        "Validation_OpfDuplicateId",
        "Validation_OpfMultipleCovers",
        "Validation_OpfUniqueIdentifierValueEmpty",
        "Validation_DuplicateIdInFile",
        "Validation_NoNav",
        "Validation_NoNcx",
    };

    /// <summary>The codes brought up to date, each once, in order of first appearance.</summary>
    public static IReadOnlyList<string> Migrate(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        List<string> result = [];
        foreach (string code in codes)
        {
            if (s_droppedWithoutCounterpart.Contains(code))
            {
                continue;
            }

            string current = s_replacements.TryGetValue(code, out string? replacement) ? replacement : code;
            if (!result.Contains(current, StringComparer.Ordinal))
            {
                result.Add(current);
            }
        }

        return result;
    }

    /// <summary>Whether <paramref name="codes"/> hold a code that <see cref="Migrate"/> would change.</summary>
    public static bool NeedsMigration(IReadOnlyList<string> codes) =>
        !codes.SequenceEqual(Migrate(codes), StringComparer.Ordinal);
}
