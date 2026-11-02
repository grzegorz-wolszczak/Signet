using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.Core.BookManipulation;
using Signet.Core.Hyphenation;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// Hyphenation patterns written for the tests (the files of LibreOffice are not kept in the repository), with breaks
/// that can be worked out by hand.
/// </summary>
public static class TestHyphenation
{
    /// <summary>Liang's own example: "hyphenation" breaks as "hy-phen-ation".</summary>
    public const string LiangPatterns = "UTF-8\nhy3ph\nhe2n\nhena4\nhen5at\n1na\nn2at\n1tio\n2io\no2n\n";

    /// <summary>A break before every "c" (two letters kept at each edge): "cierpliwości" → "cierpliwoś-ci".</summary>
    public const string BeforeEveryCPatterns = "UTF-8\n1c\n";

    /// <summary>The patterns of <see cref="LiangPatterns"/>.</summary>
    public static HyphenationPatterns Liang { get; } = Parse(LiangPatterns);

    /// <summary>The patterns of <see cref="BeforeEveryCPatterns"/>.</summary>
    public static HyphenationPatterns BeforeEveryC { get; } = Parse(BeforeEveryCPatterns);

    /// <summary>Patterns by language key: English by Liang, Polish before every "c".</summary>
    public static IReadOnlyDictionary<string, HyphenationPatterns> EnglishAndPolish { get; } =
        new Dictionary<string, HyphenationPatterns> { ["en"] = Liang, ["pl"] = BeforeEveryC };

    // Every XHTML file of the book with every language of its survey and the unknown one: a plan that goes through
    // every file, also one with no language (e.g. one that is not well-formed), for the tests of the batch itself.
    private static HashSet<(string BookPath, string Language)> EveryFileAndLanguage(Book book, SoftHyphenSurvey survey) =>
        book.GetHtmlResources()
            .SelectMany(h => survey.Languages.Select(l => l.Key).Append(string.Empty).Select(key => (h.BookPath, key)))
            .ToHashSet();

    /// <summary>A plan adding soft hyphens (Liang's patterns) to every XHTML file of a book, in every language.</summary>
    public static SoftHyphenAddPlan AddEverywhere(Book book)
    {
        HashSet<(string BookPath, string Language)> chosen = EveryFileAndLanguage(book, book.SurveySoftHyphens());
        return new SoftHyphenAddPlan(chosen, chosen.Select(c => c.Language).Distinct().ToDictionary(key => key, _ => Liang));
    }

    /// <summary>A plan removing every soft hyphen of a book, going through every XHTML file.</summary>
    public static SoftHyphenRemovePlan RemoveEverywhere(Book book)
    {
        SoftHyphenSurvey survey = book.SurveySoftHyphens();
        return new SoftHyphenRemovePlan(EveryFileAndLanguage(book, survey), survey.OutsideText.ToList());
    }

    /// <summary>The files a plan names.</summary>
    public static int FilesOf(IEnumerable<(string BookPath, string Language)> chosen) => chosen.Select(c => c.BookPath).Distinct().Count();

    /// <summary>Reads patterns from their text.</summary>
    public static HyphenationPatterns Parse(string text) => HyphenationPatterns.Parse(Encoding.UTF8.GetBytes(text));
}
