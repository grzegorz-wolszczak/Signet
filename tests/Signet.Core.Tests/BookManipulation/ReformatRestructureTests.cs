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
/// Tests of the whole-book tidying operations: <see cref="Book.MendAllHtml"/>,
/// <see cref="Book.PrettyPrintAllHtml"/>, <see cref="Book.RestructureToSignetNorm"/>,
/// <see cref="Book.UseStandardFileExtensions"/>, <see cref="Book.RebaseManifestIds"/>.
/// </summary>
public sealed class ReformatRestructureTests
{
    private static Book LoadMalformed(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
        return new ImportEpub(epub).GetBook();
    }

    private static Book LoadDeepFolders(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.EdgeDeepFolders, temp);
        return new ImportEpub(epub).GetBook();
    }

    private static Book LoadModifiedDeepFolders(TempDir temp, Action<string> mutate)
    {
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.EdgeDeepFolders, tree);
        mutate(tree);
        string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
        return new ImportEpub(epub).GetBook();
    }

    private static HtmlResource Html(Book book, string bookPathEnd) =>
        book.GetAllResources().OfType<HtmlResource>().Single(r => r.BookPath.EndsWith(bookPathEnd, StringComparison.Ordinal));

    // ----------------------------------------------------------------- Mend / Prettify --- //

    [Fact]
    public void SafePrettyPrintHtmlText_refuses_text_that_is_not_well_formed_and_MendHtmlText_repairs_it()
    {
        using TempDir temp = new();
        using Book book = LoadMalformed(temp);
        HtmlResource chapter = Html(book, "chapter1.xhtml");
        string broken = chapter.GetText();

        book.SafePrettyPrintHtmlText(chapter, broken).Should().BeNull();

        string mended = book.MendHtmlText(chapter, broken);
        WellFormedChecker.CheckXhtmlStructure(mended, chapter.EpubVersion).IsWellFormed.Should().BeTrue();
        book.SafePrettyPrintHtmlText(chapter, mended).Should().NotBeNull();
    }

    [Fact]
    public void MendAllHtml_fixes_malformed_structure_without_well_formed_guard()
    {
        using TempDir temp = new();
        using Book book = LoadMalformed(temp);
        HtmlResource chapter = Html(book, "chapter1.xhtml");
        WellFormedChecker.CheckXhtmlStructure(chapter.GetText(), chapter.EpubVersion).IsWellFormed.Should().BeFalse();

        bool modified = book.MendAllHtml();

        modified.Should().BeTrue();
        book.Modified.Should().BeTrue();
        WellFormedChecker.CheckXhtmlStructure(chapter.GetText(), chapter.EpubVersion).IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void MendAllHtml_forwards_entity_overrides_Task_T51()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource chapter = book.GetHtmlResources().First();
        chapter.SetText(chapter.GetText().Replace("</body>", "<p>a—b</p></body>", StringComparison.Ordinal));
        IReadOnlyDictionary<char, string> overrides = new Dictionary<char, string> { ['—'] = "&#8212;" };

        book.MendAllHtml(overrides);

        chapter.GetText().Should().Contain("a&#8212;b");
    }

    [Fact]
    public void PrettyPrintAllHtml_forwards_entity_overrides_Task_T51()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource chapter = book.GetHtmlResources().First();
        chapter.SetText(chapter.GetText().Replace("</body>", "<p>a—b</p></body>", StringComparison.Ordinal));
        IReadOnlyDictionary<char, string> overrides = new Dictionary<char, string> { ['—'] = "&#8212;" };

        book.PrettyPrintAllHtml(overrides);

        chapter.GetText().Should().Contain("a&#8212;b");
    }

    [Fact]
    public void MendAllHtml_is_idempotent()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        book.MendAllHtml();
        string[] afterFirstPass = book.GetHtmlResources().Select(r => r.GetText()).ToArray();

        bool modifiedOnSecondPass = book.MendAllHtml();

        modifiedOnSecondPass.Should().BeFalse();
        book.GetHtmlResources().Select(r => r.GetText()).Should().Equal(afterFirstPass);
    }

    [Fact]
    public void PrettyPrintAllHtml_cancels_when_any_html_not_well_formed()
    {
        using TempDir temp = new();
        using Book book = LoadMalformed(temp);
        HtmlResource chapter = Html(book, "chapter1.xhtml");
        string originalText = chapter.GetText();

        MaintenanceOperationResult result = book.PrettyPrintAllHtml();

        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(chapter);
        chapter.GetText().Should().Be(originalText);
    }

    [Fact]
    public void PrettyPrintAllHtml_reformats_compact_html_into_indented_markup()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedDeepFolders(temp, tree =>
        {
            string path = Path.Combine(tree, "content", "pages", "body", "ch01.xhtml");
            File.WriteAllText(
                path,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE html>"
                + "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Chapter 1</title></head>"
                + "<body><h1>Chapter 1</h1><p>Hello world.</p></body></html>");
        });
        HtmlResource chapter = Html(book, "ch01.xhtml");
        string compact = chapter.GetText();
        compact.Should().NotContain("\n  <h1>");

        MaintenanceOperationResult result = book.PrettyPrintAllHtml();

        result.Applied.Should().BeTrue();
        chapter.GetText().Should().NotBe(compact);
        chapter.GetText().Should().Contain("<h1>Chapter 1</h1>");
    }

    // ----------------------------------------------------------------------- Restructure --- //

    [Fact]
    public void RestructureToSignetNorm_cancels_when_any_html_not_well_formed()
    {
        using TempDir temp = new();
        using Book book = LoadMalformed(temp);
        HtmlResource chapter = Html(book, "chapter1.xhtml");
        string opfFilename = book.GetOpf().Filename;

        MaintenanceOperationResult result = book.RestructureToSignetNorm();

        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(chapter);
        book.GetOpf().Filename.Should().Be(opfFilename);
    }

    [Fact]
    public void RestructureToSignetNorm_moves_everything_to_std_folders_and_updates_all_references()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);

        MaintenanceOperationResult result = book.RestructureToSignetNorm();

        result.Applied.Should().BeTrue();
        book.Modified.Should().BeTrue();

        book.GetOpf().BookPath.Should().Be("OEBPS/content.opf");

        HtmlResource nav = Html(book, "nav.xhtml");
        HtmlResource title = Html(book, "title.xhtml");
        HtmlResource ch01 = Html(book, "ch01.xhtml");
        CssResource css = book.GetCssResources().Single();

        nav.BookPath.Should().Be("OEBPS/Text/nav.xhtml");
        title.BookPath.Should().Be("OEBPS/Text/title.xhtml");
        ch01.BookPath.Should().Be("OEBPS/Text/ch01.xhtml");
        css.BookPath.Should().Be("OEBPS/Styles/main.css");

        // Nav and chapter linked relative to the old, different folders — after moving to
        // the common OEBPS/Text the references become same-folder file references.
        nav.GetText().Should().Contain("href=\"title.xhtml\"");
        nav.GetText().Should().Contain("href=\"ch01.xhtml\"");
        ch01.GetText().Should().Contain("href=\"title.xhtml\"");

        // Arkusz stylow przeniesiony do OEBPS/Styles — odwolania z Text musza wskazywac w gore.
        title.GetText().Should().Contain("href=\"../Styles/main.css\"");
        ch01.GetText().Should().Contain("href=\"../Styles/main.css\"");
    }

    [Fact]
    public void RestructureToSignetNorm_renames_opf_and_ncx_to_standard_filenames()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedDeepFolders(temp, tree =>
        {
            string opfPath = Path.Combine(tree, "content", "book.opf");
            string opf = File.ReadAllText(opfPath);
            opf = opf.Replace(
                "<spine>",
                "<spine toc=\"ncx\">",
                StringComparison.Ordinal);
            opf = opf.Replace(
                "  </manifest>",
                "    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>\n  </manifest>",
                StringComparison.Ordinal);
            File.WriteAllText(opfPath, opf);
            File.WriteAllText(
                Path.Combine(tree, "content", "toc.ncx"),
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                + "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">\n"
                + "  <head><meta name=\"dtb:uid\" content=\"urn:uuid:55555555-5555-4555-8555-555555555555\"/></head>\n"
                + "  <docTitle><text>Deeply Nested Folders</text></docTitle>\n"
                + "  <navMap>\n"
                + "    <navPoint id=\"n1\" playOrder=\"1\"><navLabel><text>Chapter 1</text></navLabel><content src=\"pages/body/ch01.xhtml\"/></navPoint>\n"
                + "  </navMap>\n"
                + "</ncx>\n");
        });
        book.GetNcx().Should().NotBeNull();

        MaintenanceOperationResult result = book.RestructureToSignetNorm();

        result.Applied.Should().BeTrue();
        book.GetOpf().Filename.Should().Be("content.opf");
        book.GetNcx()!.Filename.Should().Be("toc.ncx");
        book.GetNcx()!.BookPath.Should().Be("OEBPS/toc.ncx");
    }

    [Fact]
    public void RestructureToSignetNorm_resolves_case_insensitive_filename_collisions_before_moving()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedDeepFolders(temp, tree =>
        {
            string altDir = Path.Combine(tree, "content", "resources", "styles", "alt");
            Directory.CreateDirectory(altDir);
            File.Copy(
                Path.Combine(tree, "content", "resources", "styles", "main.css"),
                Path.Combine(altDir, "main.css"));

            string opfPath = Path.Combine(tree, "content", "book.opf");
            string opf = File.ReadAllText(opfPath);
            opf = opf.Replace(
                "  </manifest>",
                "    <item id=\"css2\" href=\"resources/styles/alt/main.css\" media-type=\"text/css\"/>\n  </manifest>",
                StringComparison.Ordinal);
            File.WriteAllText(opfPath, opf);
        });
        book.GetCssResources().Should().HaveCount(2);

        MaintenanceOperationResult result = book.RestructureToSignetNorm();

        result.Applied.Should().BeTrue();
        book.GetCssResources().Select(r => r.BookPath).Should().OnlyHaveUniqueItems();
        book.GetCssResources().Should().Contain(r => r.BookPath == "OEBPS/Styles/main.css");
        book.GetCssResources().Should().Contain(r => r.BookPath != "OEBPS/Styles/main.css"
            && r.BookPath.StartsWith("OEBPS/Styles/", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ Use Standard File Extensions --- //

    [Fact]
    public void UseStandardFileExtensions_cancels_when_opf_not_well_formed()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedDeepFolders(temp, tree =>
        {
            string opfPath = Path.Combine(tree, "content", "book.opf");
            File.WriteAllText(opfPath, File.ReadAllText(opfPath).Replace("</package>", string.Empty, StringComparison.Ordinal));
        });

        MaintenanceOperationResult result = book.UseStandardFileExtensions();

        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(book.GetOpf());
    }

    [Fact]
    public void UseStandardFileExtensions_is_noop_when_all_extensions_already_match_media_types()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        book.GetOpf().Filename.Should().Be("book.opf");
        IReadOnlyList<string> bookPathsBefore = book.GetAllResources().Select(r => r.BookPath).ToList();

        MaintenanceOperationResult result = book.UseStandardFileExtensions();

        // "book.opf" already has the correct ".opf" extension — only the name stem is non-standard,
        // and that is normalized only by Restructure Epub to Signet Norm, not by this operation.
        result.Applied.Should().BeTrue();
        book.GetOpf().Filename.Should().Be("book.opf");
        book.GetAllResources().Select(r => r.BookPath).Should().Equal(bookPathsBefore);
    }

    [Fact]
    public void UseStandardFileExtensions_fixes_mismatched_extension_and_updates_references()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedDeepFolders(temp, tree =>
        {
            string oldPath = Path.Combine(tree, "content", "resources", "styles", "main.css");
            string newPath = Path.Combine(tree, "content", "resources", "styles", "main.txt");
            File.Move(oldPath, newPath);

            string opfPath = Path.Combine(tree, "content", "book.opf");
            string opf = File.ReadAllText(opfPath);
            opf = opf.Replace("resources/styles/main.css", "resources/styles/main.txt", StringComparison.Ordinal);
            File.WriteAllText(opfPath, opf);

            string titlePath = Path.Combine(tree, "content", "pages", "front", "title.xhtml");
            string chapterPath = Path.Combine(tree, "content", "pages", "body", "ch01.xhtml");
            foreach (string htmlPath in new List<string> { titlePath, chapterPath })
            {
                File.WriteAllText(
                    htmlPath,
                    File.ReadAllText(htmlPath).Replace("main.css", "main.txt", StringComparison.Ordinal));
            }
        });
        CssResource css = book.GetCssResources().Single();
        css.Filename.Should().Be("main.txt");

        MaintenanceOperationResult result = book.UseStandardFileExtensions();

        result.Applied.Should().BeTrue();
        book.Modified.Should().BeTrue();
        css.Filename.Should().Be("main.css");
        Html(book, "title.xhtml").GetText().Should().Contain("main.css");
        Html(book, "ch01.xhtml").GetText().Should().Contain("main.css");
    }

    // -------------------------------------------------------------------- Rebase Manifest IDs --- //

    [Fact]
    public void RebaseManifestIds_cancels_when_opf_not_well_formed()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedDeepFolders(temp, tree =>
        {
            string opfPath = Path.Combine(tree, "content", "book.opf");
            File.WriteAllText(opfPath, File.ReadAllText(opfPath).Replace("</package>", string.Empty, StringComparison.Ordinal));
        });

        MaintenanceOperationResult result = book.RebaseManifestIds();

        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(book.GetOpf());
    }

    [Fact]
    public void RebaseManifestIds_renumbers_ids_from_filenames_and_keeps_spine_consistent()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        int spineCountBefore = book.GetOpf().GetSpineOrderBookPaths().Count;

        MaintenanceOperationResult result = book.RebaseManifestIds();

        result.Applied.Should().BeTrue();
        book.Modified.Should().BeTrue();
        book.GetOpf().GetSpineOrderBookPaths().Should().HaveCount(spineCountBefore);
    }
}
