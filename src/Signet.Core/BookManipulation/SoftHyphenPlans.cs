using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Hyphenation;

namespace Signet.Core.BookManipulation;

/// <summary>What "Add Soft Hyphens" does (<see cref="Book.AddSoftHyphens(SoftHyphenAddPlan, FileBatchRun)"/>).</summary>
/// <param name="Chosen">The files and languages to hyphenate: a book path and a language key (<see cref="SoftHyphenLanguage.KeyOf"/>).</param>
/// <param name="Patterns">The patterns of each chosen language, by its key.</param>
/// <param name="MinWordLength">The fewest letters of a word that is hyphenated.</param>
/// <param name="Form">How the soft hyphens are written.</param>
public sealed record SoftHyphenAddPlan(
    IReadOnlySet<(string BookPath, string Language)> Chosen,
    IReadOnlyDictionary<string, HyphenationPatterns> Patterns,
    int MinWordLength = SoftHyphenInserter.DefaultMinWordLength,
    SoftHyphenForm Form = SoftHyphenForm.Character)
{
    /// <summary>Every file of every language of the survey that has patterns.</summary>
    public static SoftHyphenAddPlan ForEveryLanguageWithPatterns(
        SoftHyphenSurvey survey,
        IReadOnlyDictionary<string, HyphenationPatterns> patterns,
        int minWordLength = SoftHyphenInserter.DefaultMinWordLength,
        SoftHyphenForm form = SoftHyphenForm.Character)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(patterns);
        return new SoftHyphenAddPlan(
            survey.Languages.Where(l => patterns.ContainsKey(l.Key))
                .SelectMany(l => l.Files.Select(f => (f.BookPath, l.Key)))
                .ToHashSet(),
            patterns,
            minWordLength,
            form);
    }
}

/// <summary>What "Remove Soft Hyphens" does (<see cref="Book.RemoveSoftHyphens(SoftHyphenRemovePlan, FileBatchRun)"/>).</summary>
/// <param name="Chosen">The files and languages whose text loses its soft hyphens: a book path and a language key.</param>
/// <param name="OutsideText">The soft hyphens outside the text to remove (from <see cref="SoftHyphenSurvey.OutsideText"/>).</param>
public sealed record SoftHyphenRemovePlan(
    IReadOnlySet<(string BookPath, string Language)> Chosen,
    IReadOnlyCollection<SoftHyphenPlace> OutsideText)
{
    /// <summary>Every soft hyphen of the survey: the text of every language in every file, and every place outside the text.</summary>
    public static SoftHyphenRemovePlan Everything(SoftHyphenSurvey survey)
    {
        ArgumentNullException.ThrowIfNull(survey);
        return new SoftHyphenRemovePlan(
            survey.Languages.SelectMany(l => l.Files.Select(f => (f.BookPath, l.Key))).ToHashSet(),
            survey.OutsideText.ToList());
    }
}

/// <summary>What converting soft hyphens does (<see cref="Book.ConvertSoftHyphens(SoftHyphenConvertPlan, FileBatchRun)"/>).</summary>
/// <param name="Chosen">The files and languages whose text has its soft hyphens rewritten: a book path and a language key.</param>
/// <param name="OutsideText">The soft hyphens outside the text to rewrite (from <see cref="SoftHyphenSurvey.OutsideText"/>).</param>
/// <param name="Form">The form to write them in.</param>
public sealed record SoftHyphenConvertPlan(
    IReadOnlySet<(string BookPath, string Language)> Chosen,
    IReadOnlyCollection<SoftHyphenPlace> OutsideText,
    SoftHyphenForm Form)
{
    /// <summary>Every soft hyphen of the survey not written in <paramref name="form"/> yet.</summary>
    public static SoftHyphenConvertPlan Everything(SoftHyphenSurvey survey, SoftHyphenForm form)
    {
        ArgumentNullException.ThrowIfNull(survey);
        return new SoftHyphenConvertPlan(
            survey.Languages.SelectMany(l => l.Files.Where(f => f.SoftHyphensNotIn(form) > 0).Select(f => (f.BookPath, l.Key))).ToHashSet(),
            survey.OutsideText.Where(p => !p.IsIn(form)).ToList(),
            form);
    }
}
