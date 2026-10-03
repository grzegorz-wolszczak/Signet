namespace Signet.Core.Search;

/// <summary>
/// A single match in the replace preview ("Dry Run Replace All" / "Filter Replacements").
/// </summary>
/// <param name="BookPath">Book path of the resource containing the match.</param>
/// <param name="Offset">Offset of the match start in the resource text (UTF-16 code units).</param>
/// <param name="MatchLength">Length of the matched fragment.</param>
/// <param name="MatchText">The matched text fragment.</param>
/// <param name="ReplacementText">
/// The expanded replacement text. When replacement is impossible (<see cref="CanReplace"/> = <see langword="false"/>),
/// equal to <see cref="MatchText"/>.
/// </param>
/// <param name="PriorContext">Context before the match (newlines replaced with spaces).</param>
/// <param name="PostContext">Context after the match (newlines replaced with spaces).</param>
/// <param name="CanReplace">
/// Whether the replacement pattern could be expanded for this match (always <see langword="false"/>
/// for <c>\F&lt;…&gt;</c> — Python functions — which are not supported).
/// </param>
public sealed record ReplacePreviewRow(
    string BookPath,
    int Offset,
    int MatchLength,
    string MatchText,
    string ReplacementText,
    string PriorContext,
    string PostContext,
    bool CanReplace)
{
    /// <summary>The "before" snippet: context + match + context.</summary>
    public string BeforeSnippet => PriorContext + MatchText + PostContext;

    /// <summary>The "after" snippet: context + replacement text + context (equal to <see cref="BeforeSnippet"/> when replacement is impossible).</summary>
    public string AfterSnippet => CanReplace ? PriorContext + ReplacementText + PostContext : BeforeSnippet;

    /// <summary>Offset of the match start within <see cref="BeforeSnippet"/> / <see cref="AfterSnippet"/>.</summary>
    public int HighlightStart => PriorContext.Length;

    /// <summary>Offset of the match end within <see cref="BeforeSnippet"/>.</summary>
    public int BeforeHighlightEnd => PriorContext.Length + MatchText.Length;

    /// <summary>Offset of the replacement text end within <see cref="AfterSnippet"/>.</summary>
    public int AfterHighlightEnd => PriorContext.Length + ReplacementText.Length;
}
