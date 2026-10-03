using System.Collections.Generic;

namespace Signet.Core.Search;

/// <summary>
/// Maps PCRE2 compilation error messages to versions intended for display to the user.
/// </summary>
/// <remarks>
/// The map is a localization hook; it currently holds the English text (identity mapping).
/// <see cref="GetError"/> returns the given fallback text when the code is unknown, so newer
/// native PCRE2 messages (not present in the 2021 table) are not lost.
/// </remarks>
public sealed class PcreErrors
{
    private static readonly PcreErrors SharedInstance = new();

    private readonly Dictionary<string, string> _xlateError;

    private PcreErrors()
    {
        _xlateError = BuildErrorMap();
    }

    /// <summary>The shared instance.</summary>
    public static PcreErrors Instance => SharedInstance;

    /// <summary>
    /// Returns the message to display for the given PCRE2 error code.
    /// </summary>
    /// <param name="code">Raw error message from PCRE2.</param>
    /// <param name="fallback">Text returned when <paramref name="code"/> is not in the map.</param>
    public string GetError(string code, string fallback)
    {
        return _xlateError.TryGetValue(code, out string? value) ? value : fallback;
    }

    private static Dictionary<string, string> BuildErrorMap()
    {
        // Key == value: the table of PCRE2 compile error messages.
        string[] messages =
        {
            "no error",
            "\\ at end of pattern",
            "\\c at end of pattern",
            "unrecognized character follows \\",
            "numbers out of order in {} quantifier",
            "number too big in {} quantifier",
            "missing terminating ] for character class",
            "escape sequence is invalid in character class",
            "range out of order in character class",
            "quantifier does not follow a repeatable item",
            "internal error: unexpected repeat",
            "unrecognized character after (? or (?-",
            "POSIX named classes are supported only within a class",
            "POSIX collating elements are not supported",
            "missing closing parenthesis",
            "reference to non-existent subpattern",
            "pattern passed as NULL",
            "unrecognised compile-time option bit(s)",
            "missing ) after (?# comment",
            "parentheses are too deeply nested",
            "regular expression is too large",
            "failed to allocate heap memory",
            "unmatched closing parenthesis",
            "internal error: code overflow",
            "missing closing parenthesis for condition",
            "lookbehind assertion is not fixed length",
            "a relative value of zero is not allowed",
            "conditional subpattern contains more than two branches",
            "assertion expected after (?( or (?(?C)",
            "digit expected after (?+ or (?-\0",
            "unknown POSIX class name",
            "internal error in pcre2_study(): should not occur",
            "this version of PCRE2 does not have Unicode support",
            "parentheses are too deeply nested (stack check)",
            "character code point value in \\x{} or \\o{} is too large",
            "lookbehind is too complicated",
            "\\C is not allowed in a lookbehind assertion in UTF-16 mode",
            "PCRE2 does not support \\F, \\L, \\l, \\N{name}, \\U, or \\u",
            "number after (?C is greater than 255",
            "closing parenthesis for (?C expected",
            "invalid escape sequence in (*VERB) name",
            "unrecognized character after (?P",
            "syntax error in subpattern name (missing terminator?)",
            "two named subpatterns have the same name (PCRE2_DUPNAMES not set)",
            "subpattern name must start with a non-digit",
            "this version of PCRE2 does not have support for \\P, \\p, or \\X",
            "malformed \\P or \\p sequence",
            "unknown property name after \\P or \\p",
            "subpattern name is too long (maximum 32 code units)",
            "too many named subpatterns (maximum 10000)",
            "invalid range in character class",
            "octal value is greater than \\377 in 8-bit non-UTF-8 mode",
            "internal error: overran compiling workspace",
            "internal error: previously-checked referenced subpattern not found",
            "DEFINE subpattern contains more than one branch",
            "missing opening brace after \\o",
            "internal error: unknown newline setting",
            "\\g is not followed by a braced, angle-bracketed, or quoted name/number or by a plain number",
            "(?R (recursive pattern call) must be followed by a closing parenthesis",
            "obsolete error (should not occur)",
            "(*VERB) not recognized or malformed",
            "subpattern number is too big",
            "subpattern name expected",
            "internal error: parsed pattern overflow",
            "non-octal character in \\o{} (closing brace missing?)",
            "different names for subpatterns of the same number are not allowed",
            "(*MARK) must have an argument",
            "non-hex character in \\x{} (closing brace missing?)",
            "\\c must be followed by a printable ASCII character",
            "\\k is not followed by a braced, angle-bracketed, or quoted name",
            "internal error: unknown meta code in check_lookbehinds()",
            "\\N is not supported in a class",
            "callout string is too long",
            "disallowed Unicode code point (>= 0xd800 && <= 0xdfff)",
            "using UTF is disabled by the application",
            "using UCP is disabled by the application",
            "name is too long in (*MARK), (*PRUNE), (*SKIP), or (*THEN)",
            "character code point value in \\u.... sequence is too large",
            "digits missing in \\x{} or \\o{} or \\N{U+}",
            "syntax error or number too big in (?(VERSION condition",
            "internal error: unknown opcode in auto_possessify()",
            "missing terminating delimiter for callout with string argument",
            "unrecognized string delimiter follows (?C",
            "using \\C is disabled by the application",
            "(?| and/or (?J: or (?x: parentheses are too deeply nested",
            "using \\C is disabled in this PCRE2 library",
            "regular expression is too complicated",
            "lookbehind assertion is too long",
            "pattern string is longer than the limit set by the application",
            "internal error: unknown code in parsed pattern",
            "internal error: bad code value in parsed_skip()",
            "PCRE2_EXTRA_ALLOW_SURROGATE_ESCAPES is not allowed in UTF-16 mode",
            "invalid option bits with PCRE2_LITERAL",
            "\\N{U+dddd} is supported only in Unicode (UTF) mode",
            "invalid hyphen in option setting",
            "(*alpha_assertion) not recognized",
            "script runs require Unicode support, which this version of PCRE2 does not have",
            "too many capturing groups (maximum 65535)",
            "atomic assertion expected after (?( or (?(?C)",
            "\\K is not allowed in lookarounds (but see PCRE2_EXTRA_ALLOW_LOOKAROUND_BSK)",
        };

        var map = new Dictionary<string, string>(messages.Length);
        foreach (string message in messages)
        {
            map[message] = message;
        }

        return map;
    }
}
