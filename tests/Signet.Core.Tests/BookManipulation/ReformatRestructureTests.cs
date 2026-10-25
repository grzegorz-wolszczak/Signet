using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of the whole-book tidying operations: <see cref="Book.MendAllHtml"/>,
/// <see cref="Book.PrettyPrintAllHtml"/>, <see cref="Book.ReformatAllCss"/>,
/// <see cref="Book.RestructureToSignetNorm"/>, <see cref="Book.UseStandardFileExtensions"/>,
/// <see cref="Book.RebaseManifestIds"/>.
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
        // Arrange
        using TempDir temp = new();
        using Book book = LoadMalformed(temp);
        HtmlResource chapter = Html(book, "chapter1.xhtml");
        string broken = chapter.GetText();

        // Act
        book.SafePrettyPrintHtmlText(chapter, broken).Should().BeNull();

        string mended = book.MendHtmlText(chapter, broken);

        // Assert
        WellFormedChecker.CheckXhtmlStructure(mended, chapter.EpubVersion).IsWellFormed.Should().BeTrue();

        // Act & Assert
        book.SafePrettyPrintHtmlText(chapter, mended).Should().NotBeNull();
    }

    [Fact]
    public void MendAllHtml_fixes_malformed_structure_without_well_formed_guard()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadMalformed(temp);
        HtmlResource chapter = Html(book, "chapter1.xhtml");

        WellFormedChecker.CheckXhtmlStructure(chapter.GetText(), chapter.EpubVersion).IsWellFormed.Should().BeFalse();

        // Act
        FileBatchResult result = book.MendAllHtml();

        // Assert
        result.Modified.Should().BeTrue();
        result.Skipped.Should().BeEmpty();
        book.Modified.Should().BeTrue();
        WellFormedChecker.CheckXhtmlStructure(chapter.GetText(), chapter.EpubVersion).IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void MendAllHtml_forwards_entity_overrides_Task_T51()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource chapter = book.GetHtmlResources().First();
        chapter.SetText(chapter.GetText().Replace("</body>", "<p>a—b</p></body>", StringComparison.Ordinal));
        IReadOnlyDictionary<char, string> overrides = new Dictionary<char, string> { ['—'] = "&#8212;" };

        // Act
        book.MendAllHtml(overrides);

        // Assert
        chapter.GetText().Should().Contain("a&#8212;b");
    }

    [Fact]
    public void PrettyPrintAllHtml_forwards_entity_overrides_Task_T51()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource chapter = book.GetHtmlResources().First();
        chapter.SetText(chapter.GetText().Replace("</body>", "<p>a—b</p></body>", StringComparison.Ordinal));
        IReadOnlyDictionary<char, string> overrides = new Dictionary<char, string> { ['—'] = "&#8212;" };

        // Act
        book.PrettyPrintAllHtml(overrides);

        // Assert
        chapter.GetText().Should().Contain("a&#8212;b");
    }

    /// <summary>
    /// A file whose linked stylesheet sets <c>white-space</c> keeps its spaces; the others have them collapsed. Every
    /// file linking the stylesheet keeps them, also the ones after the first (the answer for a stylesheet is remembered
    /// for the whole operation).
    /// </summary>
    [Theory]
    [InlineData("p { white-space: pre-wrap; }")]
    [InlineData("p { white-space-collapse: preserve; }")]
    public void PrettyPrintAllHtml_keeps_the_spaces_of_every_file_whose_stylesheet_sets_white_space(string css)
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        book.GetCssResources().Single().SetText(css);
        HtmlResource[] files = { Html(book, "ch01.xhtml"), Html(book, "title.xhtml"), Html(book, "nav.xhtml") };
        foreach (HtmlResource file in files)
        {
            file.SetText(file.GetText().Replace("</body>", "<p>a   b</p></body>", StringComparison.Ordinal));
        }

        // Act
        book.PrettyPrintAllHtml();

        // Assert
        files[0].GetText().Should().Contain("a   b", "the chapter links the stylesheet");
        files[1].GetText().Should().Contain("a   b", "the title page links the same stylesheet");
        files[2].GetText().Should().Contain("a b").And.NotContain("a   b", "the nav does not link it");
    }

    // ---------------------------------------------------------------------- Reformat CSS --- //

    [Fact]
    public void ReformatAllCss_puts_every_property_of_every_stylesheet_on_its_own_line()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        CssResource linked = book.GetCssResources().Single();
        CssResource added = book.CreateEmptyCssFile();
        linked.SetText("p { margin: 0; text-indent: 1em; }");
        added.SetText("h1 { color: red; font-size: 2em; }");

        // Act
        FileBatchResult result = book.ReformatAllCss(multipleLineFormat: true);

        // Assert
        result.Processed.Should().Be(2);
        result.Modified.Should().BeTrue();
        result.Skipped.Should().BeEmpty();
        linked.GetText().Should().Contain("margin: 0;\n").And.Contain("text-indent: 1em;\n");
        added.GetText().Should().Contain("color: red;\n").And.Contain("font-size: 2em;\n");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void ReformatAllCss_puts_a_whole_rule_on_one_line_in_the_single_line_form()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        CssResource css = book.GetCssResources().Single();
        css.SetText("p {\n    margin: 0;\n    text-indent: 1em;\n}\n");

        // Act
        book.ReformatAllCss(multipleLineFormat: false);

        // Assert
        css.GetText().Should().Contain("p{margin:0;text-indent:1em;}");
    }

    /// <summary>
    /// A stylesheet whose structure is broken is reported as skipped instead of silently coming back unchanged
    /// (<c>CssInfo.GetReformattedCssText</c> returns the source text on a structural error).
    /// </summary>
    [Fact]
    public void ReformatAllCss_skips_a_stylesheet_whose_structure_is_broken_and_formats_the_rest()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        CssResource broken = book.GetCssResources().Single();
        CssResource sound = book.CreateEmptyCssFile();
        broken.SetText("p { margin: 0;");
        sound.SetText("h1 { color: red; font-size: 2em; }");

        // Act
        FileBatchResult result = book.ReformatAllCss(multipleLineFormat: true);

        // Assert
        result.Processed.Should().Be(1);
        result.Skipped.Should().ContainSingle().Which.BookPath.Should().Be(broken.BookPath);
        broken.GetText().Should().Be("p { margin: 0;");
        sound.GetText().Should().Contain("color: red;\n");
    }

    [Fact]
    public void ReformatAllCss_is_idempotent()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        book.GetCssResources().Single().SetText("p { margin: 0; text-indent: 1em; }");

        // Act
        book.ReformatAllCss(multipleLineFormat: true);
        string[] afterFirstPass = book.GetCssResources().Select(r => r.GetText()).ToArray();

        FileBatchResult secondPass = book.ReformatAllCss(multipleLineFormat: true);

        // Assert
        secondPass.Modified.Should().BeFalse();
        book.GetCssResources().Select(r => r.GetText()).Should().Equal(afterFirstPass);
    }

    [Fact]
    public void ReformatAllCss_reports_every_stylesheet_before_it_is_processed()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        book.CreateEmptyCssFile();
        List<CssResource> files = book.GetCssResources().ToList();
        List<FileBatchProgress> reports = new();

        // Act
        book.ReformatAllCss(multipleLineFormat: true, new FileBatchRun(new Progress(reports), CancellationToken.None));

        // Assert
        reports.Select(r => r.BookPath).Should().Equal(files.Select(f => f.BookPath));
        reports.Should().OnlyContain(r => r.Total == files.Count);
    }

    private sealed class Progress : IProgress<FileBatchProgress>
    {
        private readonly List<FileBatchProgress> _reports;

        public Progress(List<FileBatchProgress> reports) => _reports = reports;

        public void Report(FileBatchProgress value) => _reports.Add(value);
    }

    [Fact]
    public void MendAllHtml_is_idempotent()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);

        // Act
        book.MendAllHtml();
        string[] afterFirstPass = book.GetHtmlResources().Select(r => r.GetText()).ToArray();

        FileBatchResult secondPass = book.MendAllHtml();

        // Assert
        secondPass.Modified.Should().BeFalse();
        book.GetHtmlResources().Select(r => r.GetText()).Should().Equal(afterFirstPass);
    }

    [Fact]
    public void PrettyPrintAllHtml_skips_a_file_that_is_not_well_formed_and_formats_the_rest()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        IReadOnlyList<HtmlResource> chapters = book.GetHtmlResources();
        chapters.Should().HaveCountGreaterThan(1);
        HtmlResource broken = chapters[0];
        HtmlResource compact = chapters[1];
        broken.SetText("<html><head><body><p>broken");
        compact.SetText(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE html>"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head>"
            + "<body><h1>Title</h1><p>Hello world.</p></body></html>");
        string compactText = compact.GetText();

        // Act
        FileBatchResult result = book.PrettyPrintAllHtml();

        // Assert
        result.Skipped.Should().ContainSingle();
        result.Skipped[0].BookPath.Should().Be(broken.BookPath);
        result.Skipped[0].Line.Should().BePositive();
        result.Processed.Should().Be(chapters.Count - 1);
        result.Modified.Should().BeTrue();
        result.NothingProcessed.Should().BeFalse();
        broken.GetText().Should().Be("<html><head><body><p>broken");
        compact.GetText().Should().NotBe(compactText);
    }

    [Fact]
    public void PrettyPrintAllHtml_reports_nothing_processed_when_every_file_is_not_well_formed()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            html.SetText("<html><head><body><p>broken " + html.Filename);
        }

        string[] before = book.GetHtmlResources().Select(h => h.GetText()).ToArray();

        // Act
        FileBatchResult result = book.PrettyPrintAllHtml();

        // Assert
        result.NothingProcessed.Should().BeTrue();
        result.Processed.Should().Be(0);
        result.Modified.Should().BeFalse();
        result.Skipped.Select(s => s.BookPath).Should().BeEquivalentTo(book.GetHtmlResources().Select(h => h.BookPath));
        book.GetHtmlResources().Select(h => h.GetText()).Should().Equal(before);
    }

    [Fact]
    public void PrettyPrintAllHtml_reformats_compact_html_into_indented_markup()
    {
        // Arrange
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

        // Act
        FileBatchResult result = book.PrettyPrintAllHtml();

        // Assert
        result.Skipped.Should().BeEmpty();
        chapter.GetText().Should().NotBe(compact);
        chapter.GetText().Should().Contain("<h1>Chapter 1</h1>");
    }

    [Fact]
    public void RemoveSoftHyphens_skips_a_file_that_is_not_well_formed_and_processes_the_rest()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        IReadOnlyList<HtmlResource> chapters = book.GetHtmlResources();
        chapters.Should().HaveCountGreaterThan(1);
        chapters[0].SetText("<html><head><body><p>bro&#173;ken");
        chapters[1].SetText(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE html>"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head>"
            + "<body><p>hy&#173;phen</p></body></html>");

        // Act
        FileBatchResult result = book.RemoveSoftHyphens();

        // Assert
        result.Skipped.Should().ContainSingle().Which.BookPath.Should().Be(chapters[0].BookPath);
        result.Modified.Should().BeTrue();
        chapters[0].GetText().Should().Contain("&#173;");
        chapters[1].GetText().Should().NotContain("&#173;");
    }

    [Fact]
    public void AddSoftHyphens_skips_a_file_that_is_not_well_formed_and_processes_the_rest()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        IReadOnlyList<HtmlResource> chapters = book.GetHtmlResources();
        chapters.Should().HaveCountGreaterThan(1);
        chapters[0].SetText("<html><head><body><p>broken");

        // Act
        FileBatchResult result = book.AddSoftHyphens();

        // Assert
        result.Skipped.Should().ContainSingle().Which.BookPath.Should().Be(chapters[0].BookPath);
        result.Processed.Should().Be(chapters.Count - 1);
        chapters[0].GetText().Should().Be("<html><head><body><p>broken");
    }

    // ----------------------------------------------------------------------- Restructure --- //

    [Fact]
    public void RestructureToSignetNorm_cancels_when_any_html_not_well_formed()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadMalformed(temp);
        HtmlResource chapter = Html(book, "chapter1.xhtml");
        string opfFilename = book.GetOpf().Filename;

        // Act
        MaintenanceOperationResult result = book.RestructureToSignetNorm();

        // Assert
        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(chapter);
        book.GetOpf().Filename.Should().Be(opfFilename);
    }

    [Fact]
    public void RestructureToSignetNorm_moves_everything_to_std_folders_and_updates_all_references()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);

        // Act
        MaintenanceOperationResult result = book.RestructureToSignetNorm();

        // Assert
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
        // Arrange
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

        // Act
        MaintenanceOperationResult result = book.RestructureToSignetNorm();

        // Assert
        result.Applied.Should().BeTrue();
        book.GetOpf().Filename.Should().Be("content.opf");
        book.GetNcx()!.Filename.Should().Be("toc.ncx");
        book.GetNcx()!.BookPath.Should().Be("OEBPS/toc.ncx");
    }

    [Fact]
    public void RestructureToSignetNorm_resolves_case_insensitive_filename_collisions_before_moving()
    {
        // Arrange
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

        // Act
        MaintenanceOperationResult result = book.RestructureToSignetNorm();

        // Assert
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
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedDeepFolders(temp, tree =>
        {
            string opfPath = Path.Combine(tree, "content", "book.opf");
            File.WriteAllText(opfPath, File.ReadAllText(opfPath).Replace("</package>", string.Empty, StringComparison.Ordinal));
        });

        // Act
        MaintenanceOperationResult result = book.UseStandardFileExtensions();

        // Assert
        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(book.GetOpf());
    }

    [Fact]
    public void UseStandardFileExtensions_is_noop_when_all_extensions_already_match_media_types()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);

        book.GetOpf().Filename.Should().Be("book.opf");

        IReadOnlyList<string> bookPathsBefore = book.GetAllResources().Select(r => r.BookPath).ToList();

        // Act
        MaintenanceOperationResult result = book.UseStandardFileExtensions();

        // "book.opf" already has the correct ".opf" extension — only the name stem is non-standard,
        // and that is normalized only by Restructure Epub to Signet Norm, not by this operation.
        // Assert
        result.Applied.Should().BeTrue();
        book.GetOpf().Filename.Should().Be("book.opf");
        book.GetAllResources().Select(r => r.BookPath).Should().Equal(bookPathsBefore);
    }

    [Fact]
    public void UseStandardFileExtensions_fixes_mismatched_extension_and_updates_references()
    {
        // Arrange
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

        // Act
        MaintenanceOperationResult result = book.UseStandardFileExtensions();

        // Assert
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
        // Arrange
        using TempDir temp = new();
        using Book book = LoadModifiedDeepFolders(temp, tree =>
        {
            string opfPath = Path.Combine(tree, "content", "book.opf");
            File.WriteAllText(opfPath, File.ReadAllText(opfPath).Replace("</package>", string.Empty, StringComparison.Ordinal));
        });

        // Act
        MaintenanceOperationResult result = book.RebaseManifestIds();

        // Assert
        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(book.GetOpf());
    }

    [Fact]
    public void RebaseManifestIds_renumbers_ids_from_filenames_and_keeps_spine_consistent()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        int spineCountBefore = book.GetOpf().GetSpineOrderBookPaths().Count;

        // Act
        MaintenanceOperationResult result = book.RebaseManifestIds();

        // Assert
        result.Applied.Should().BeTrue();
        book.Modified.Should().BeTrue();
        book.GetOpf().GetSpineOrderBookPaths().Should().HaveCount(spineCountBefore);
    }
}
