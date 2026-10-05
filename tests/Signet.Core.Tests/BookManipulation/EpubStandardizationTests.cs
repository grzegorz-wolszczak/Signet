using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="EpubStandardization"/> — the "Standardize EPUB" dialog: the preview (planned on a simulated
/// state) must show exactly what applying does.
/// </summary>
public sealed class EpubStandardizationTests
{
    private const string ChapterOpfHref = "pages/body/ch01.xhtml";

    /// <summary>
    /// The deep-folders book with a non-standard chapter extension (<c>ch01.html</c> holding XHTML), so every step
    /// has something to do.
    /// </summary>
    private static Book LoadNonStandardBook(TempDir temp, Action<string>? mutate = null)
    {
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.EdgeDeepFolders, tree);
        string body = Path.Combine(tree, "content", "pages", "body");
        File.Move(Path.Combine(body, "ch01.xhtml"), Path.Combine(body, "ch01.html"));
        ReplaceInFile(Path.Combine(tree, "content", "book.opf"), ChapterOpfHref, "pages/body/ch01.html");
        ReplaceInFile(Path.Combine(tree, "content", "pages", "nav", "nav.xhtml"), "ch01.xhtml", "ch01.html");
        mutate?.Invoke(tree);
        string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
        return new ImportEpub(epub).GetBook();
    }

    private static void ReplaceInFile(string path, string oldValue, string newValue) =>
        File.WriteAllText(path, File.ReadAllText(path).Replace(oldValue, newValue, StringComparison.Ordinal));

    /// <summary>Makes the manifest media type of the stylesheet differ from the resource's actual type.</summary>
    private static void BreakStylesheetManifestType(Book book)
    {
        OpfResource opf = book.GetOpf();
        opf.SetText(opf.GetText().Replace("media-type=\"text/css\"", "media-type=\"text/x-css\"", StringComparison.Ordinal));
    }

    private static List<string> ManifestIds(Book book) => book.GetOpf().GetOpfDocument().Manifest.Select(e => e.Id).ToList();

    [Fact]
    public void Planning_does_not_change_the_book()
    {
        using TempDir temp = new();
        using Book book = LoadNonStandardBook(temp);
        BreakStylesheetManifestType(book);
        book.Modified = false;
        List<string> bookPaths = book.GetAllResources().Select(r => r.BookPath).ToList();
        string opfText = book.GetOpf().GetText();

        StandardizationPlan plan = EpubStandardization.Plan(book, EpubStandardization.AllSteps);

        plan.Steps.Should().OnlyContain(s => s.Changes.Count > 0, "every step has something to do in this book");
        book.GetAllResources().Select(r => r.BookPath).Should().Equal(bookPaths);
        book.GetOpf().GetText().Should().Be(opfText);
        book.Modified.Should().BeFalse();
    }

    [Fact]
    public void Plan_lists_only_the_checked_steps_in_the_fixed_order()
    {
        using TempDir temp = new();
        using Book book = LoadNonStandardBook(temp);

        StandardizationPlan plan = EpubStandardization.Plan(
            book,
            new[] { StandardizationStep.ManifestMediaTypes, StandardizationStep.StandardFolders });

        plan.Steps.Select(s => s.Step).Should().Equal(StandardizationStep.StandardFolders, StandardizationStep.ManifestMediaTypes);
        plan.For(StandardizationStep.RebaseManifestIds).Should().BeNull();
    }

    [Fact]
    public void Applying_does_exactly_what_the_preview_shows()
    {
        using TempDir temp = new();
        using Book book = LoadNonStandardBook(temp);
        BreakStylesheetManifestType(book);
        Dictionary<Resource, string> expectedPaths = book.GetAllResources().ToDictionary(r => r, r => r.BookPath);
        List<string> expectedIds = ManifestIds(book);

        StandardizationPlan plan = EpubStandardization.Plan(book, EpubStandardization.AllSteps);
        foreach (StandardizationChange change in plan.Steps.SelectMany(s => s.Changes))
        {
            switch (change.Kind)
            {
                case StandardizationChangeKind.Path:
                    Resource moved = expectedPaths.Single(p => p.Value == change.Before).Key;
                    expectedPaths[moved] = change.After;
                    break;
                case StandardizationChangeKind.ManifestId:
                    expectedIds[expectedIds.IndexOf(change.Before)] = change.After;
                    break;
            }
        }

        MaintenanceOperationResult result = EpubStandardization.Apply(book, EpubStandardization.AllSteps);

        result.Applied.Should().BeTrue();
        book.Modified.Should().BeTrue();
        book.GetAllResources().Should().OnlyContain(r => r.BookPath == expectedPaths[r]);
        ManifestIds(book).Should().Equal(expectedIds);
        book.GetOpf().GetText().Should().NotContain("text/x-css");
        EpubStandardization.Plan(book, EpubStandardization.AllSteps).HasChanges.Should().BeFalse("everything is standard now");
    }

    [Fact]
    public void Later_steps_are_planned_on_the_result_of_the_earlier_ones()
    {
        using TempDir temp = new();
        using Book book = LoadNonStandardBook(temp);

        StandardizationPlan plan = EpubStandardization.Plan(
            book,
            new[] { StandardizationStep.StandardFolders, StandardizationStep.StandardFileExtensions, StandardizationStep.RebaseManifestIds });

        // Renaming a file also renames its manifest identifier (as the rename handler of the OPF does).
        plan.For(StandardizationStep.StandardFileExtensions)!.Changes.Should().Equal(
            new StandardizationChange(StandardizationChangeKind.Path, "OEBPS/Text/ch01.html", "OEBPS/Text/ch01.xhtml", "OEBPS/Text/ch01.xhtml"),
            new StandardizationChange(StandardizationChangeKind.ManifestId, "ch01", "ch01.xhtml", "OEBPS/Text/ch01.xhtml"));
        plan.For(StandardizationStep.RebaseManifestIds)!.Changes.Should()
            .ContainSingle(c => c.Before == "ch01.xhtml")
            .Which.Should().Be(new StandardizationChange(StandardizationChangeKind.ManifestId, "ch01.xhtml", "ch01_xhtml", "OEBPS/Text/ch01.xhtml"));
    }

    [Fact]
    public void A_new_extension_never_overwrites_another_file_of_the_same_folder()
    {
        using TempDir temp = new();
        using Book book = LoadNonStandardBook(temp, tree =>
        {
            string body = Path.Combine(tree, "content", "pages", "body");
            File.Copy(Path.Combine(body, "ch01.html"), Path.Combine(body, "ch01.xhtml"));
            ReplaceInFile(
                Path.Combine(tree, "content", "book.opf"),
                "  </manifest>",
                "    <item id=\"ch01b\" href=\"" + ChapterOpfHref + "\" media-type=\"application/xhtml+xml\"/>\n  </manifest>");
        });

        StandardizationPlan plan = EpubStandardization.Plan(book, new[] { StandardizationStep.StandardFileExtensions });
        EpubStandardization.Apply(book, new[] { StandardizationStep.StandardFileExtensions });

        StandardizationChange change = plan.Steps.Single().Changes.Should().ContainSingle(c => c.Kind == StandardizationChangeKind.Path).Subject;
        change.Before.Should().Be("content/pages/body/ch01.html");
        change.After.Should().NotBe("content/pages/body/ch01.xhtml").And.EndWith(".xhtml");
        book.GetAllResources().Select(r => r.BookPath).Should()
            .Contain(new[] { "content/pages/body/ch01.xhtml", change.After }).And.OnlyHaveUniqueItems();
        File.Exists(book.GetAllResources().Single(r => r.BookPath == change.After).FullPath).Should().BeTrue();
    }

    [Fact]
    public void Moving_the_opf_to_another_folder_keeps_the_manifest_pointing_at_the_files()
    {
        using TempDir temp = new();
        using Book book = LoadNonStandardBook(temp);
        book.GetOpf().BookPath.Should().Be("content/book.opf");

        EpubStandardization.Apply(book, new[] { StandardizationStep.StandardFolders });

        OpfResource opf = book.GetOpf();
        opf.BookPath.Should().Be("OEBPS/content.opf");
        opf.GetSpineOrderBookPaths().Should().Equal("OEBPS/Text/title.xhtml", "OEBPS/Text/ch01.html");
        opf.GetText().Should().Contain("href=\"Text/nav.xhtml\"").And.Contain("href=\"Styles/main.css\"");
    }

    [Fact]
    public void Applying_is_refused_when_a_file_is_not_well_formed()
    {
        using TempDir temp = new();
        using Book book = LoadNonStandardBook(temp, tree =>
            ReplaceInFile(Path.Combine(tree, "content", "pages", "front", "title.xhtml"), "</body>", string.Empty));
        List<string> bookPaths = book.GetAllResources().Select(r => r.BookPath).ToList();

        MaintenanceOperationResult result = EpubStandardization.Apply(book, EpubStandardization.AllSteps);

        result.Applied.Should().BeFalse();
        result.NotWellFormed!.Filename.Should().Be("title.xhtml");
        book.GetAllResources().Select(r => r.BookPath).Should().Equal(bookPaths);
    }
}
