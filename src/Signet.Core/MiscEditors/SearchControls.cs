using System;
using System.Collections.Generic;
using Signet.Core.Search;
using Signet.Core.Localization;

namespace Signet.Core.MiscEditors;

/// <summary>
/// The decoded search options stored in the <see cref="SearchEntry.Controls"/> field.
/// </summary>
/// <param name="Mode">The search mode.</param>
/// <param name="Direction">The search direction.</param>
/// <param name="LookWhere">The search scope.</param>
/// <param name="Wrap">Search wrapping (token <c>WR</c>).</param>
/// <param name="DotAll">Regex <c>(?s)</c> (token <c>DA</c>).</param>
/// <param name="MinimalMatch">Regex <c>(?U)</c> (token <c>MM</c>).</param>
/// <param name="AutoTokenise">
/// "Auto Tokenise" (token <c>AT</c>) — when selected text from
/// Code View is inserted into the Find field in Regex mode, metacharacters are escaped automatically
/// (<see cref="Search.RegexTokeniser.TokeniseForRegex"/>). It does not affect how the search
/// pattern itself is built — only what lands in the Find field.
/// </param>
/// <param name="UnicodeProperty">Regex <c>(*UCP)</c> (token <c>UN</c>).</param>
/// <param name="TextOnly">"Search in text, not tags" (token <c>TO</c>).</param>
public readonly record struct SearchControlValues(
    SearchMode Mode,
    SearchDirection Direction,
    LookWhere LookWhere,
    bool Wrap,
    bool DotAll,
    bool MinimalMatch,
    bool AutoTokenise,
    bool UnicodeProperty,
    bool TextOnly)
{
    /// <summary>The default values (like a freshly opened F&amp;R panel): Normal, Down, the current file, wrapping enabled.</summary>
    public static SearchControlValues Defaults { get; } = new(
        SearchMode.Normal,
        SearchDirection.Down,
        LookWhere.CurrentFile,
        Wrap: true,
        DotAll: false,
        MinimalMatch: false,
        AutoTokenise: false,
        UnicodeProperty: false,
        TextOnly: false);
}

/// <summary>
/// Encoding / decoding of the "Controls" string of saved searches (the mode, direction and
/// look-where tokens, plus the tooltip describing them).
/// </summary>
public static class SearchControls
{
    private static readonly string[] ModeTokens = { "NL", "CS", "RX" };
    private static readonly string[] DirectionTokens = { "DN", "UP" };

    // The order matches the LookWhere enum.
    private static readonly string[] LookWhereTokens =
    {
        "CF", "AH", "SH", "TH", "AC", "SC", "TC", "OP", "NX", "SV", "SJ", "SX",
    };

    /// <summary>Builds the "Controls" string from the options.</summary>
    public static string Build(SearchControlValues values)
    {
        var parts = new List<string> { ModeTokens[(int)values.Mode] };
        if (values.DotAll)
        {
            parts.Add("DA");
        }

        if (values.MinimalMatch)
        {
            parts.Add("MM");
        }

        if (values.AutoTokenise)
        {
            parts.Add("AT");
        }

        if (values.UnicodeProperty)
        {
            parts.Add("UN");
        }

        if (values.Wrap)
        {
            parts.Add("WR");
        }

        if (values.TextOnly)
        {
            parts.Add("TO");
        }

        parts.Add(DirectionTokens[(int)values.Direction]);
        parts.Add(LookWhereTokens[(int)values.LookWhere]);
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Decodes the "Controls" string. Missing
    /// tokens take their default values; an empty string yields <see cref="SearchControlValues.Defaults"/>.
    /// </summary>
    public static SearchControlValues Parse(string? controls)
    {
        if (string.IsNullOrEmpty(controls))
        {
            return SearchControlValues.Defaults;
        }

        SearchMode mode = SearchControlValues.Defaults.Mode;
        if (controls.Contains("NL", StringComparison.Ordinal))
        {
            mode = SearchMode.Normal;
        }
        else if (controls.Contains("RX", StringComparison.Ordinal))
        {
            mode = SearchMode.Regex;
        }
        else if (controls.Contains("CS", StringComparison.Ordinal))
        {
            mode = SearchMode.CaseSensitive;
        }

        LookWhere lookWhere = SearchControlValues.Defaults.LookWhere;
        for (int i = 0; i < LookWhereTokens.Length; i++)
        {
            if (controls.Contains(LookWhereTokens[i], StringComparison.Ordinal))
            {
                lookWhere = (LookWhere)i;
                break;
            }
        }

        SearchDirection direction = SearchControlValues.Defaults.Direction;
        if (controls.Contains("UP", StringComparison.Ordinal))
        {
            direction = SearchDirection.Up;
        }
        else if (controls.Contains("DN", StringComparison.Ordinal))
        {
            direction = SearchDirection.Down;
        }

        return new SearchControlValues(
            mode,
            direction,
            lookWhere,
            Wrap: controls.Contains("WR", StringComparison.Ordinal),
            DotAll: controls.Contains("DA", StringComparison.Ordinal),
            MinimalMatch: controls.Contains("MM", StringComparison.Ordinal),
            AutoTokenise: controls.Contains("AT", StringComparison.Ordinal),
            UnicodeProperty: controls.Contains("UN", StringComparison.Ordinal),
            TextOnly: controls.Contains("TO", StringComparison.Ordinal));
    }

    /// <summary>Converts to the <see cref="SearchOptions"/> used by <see cref="SearchRegexBuilder"/>.</summary>
    public static SearchOptions ToSearchOptions(SearchControlValues values) => new(
        values.DotAll,
        values.MinimalMatch,
        values.UnicodeProperty,
        values.TextOnly);

    /// <summary>
    /// Builds a multi-line tooltip describing the tokens.
    /// </summary>
    public static string BuildToolTip(string? controls)
    {
        if (string.IsNullOrEmpty(controls))
        {
            return string.Empty;
        }

        var lines = new List<string>();
        void Add(string token)
        {
            if (controls.Contains(token, StringComparison.Ordinal))
            {
                lines.Add($"{token} - {CoreStrings.Get("SearchControl_" + token)}");
            }
        }

        Add("NL");
        Add("RX");
        Add("CS");
        Add("UP");
        Add("DN");
        Add("CF");
        Add("AH");
        Add("SH");
        Add("TH");
        Add("AC");
        Add("SC");
        Add("TC");
        Add("OP");
        Add("NX");
        Add("SV");
        Add("SJ");
        Add("SX");
        Add("DA");
        Add("MM");
        Add("AT");
        Add("UN");
        Add("WR");
        Add("TO");
        return string.Join('\n', lines);
    }
}
