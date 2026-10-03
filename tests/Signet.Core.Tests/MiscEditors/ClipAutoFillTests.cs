using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MiscEditors;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MiscEditors;

/// <summary>
/// Tests of <see cref="ClipAutoFill"/> — generating
/// clips from the class selectors found in the book CSS stylesheets.
/// </summary>
public sealed class ClipAutoFillTests
{
    private static Book LoadWithCss(TempDir temp, string css)
    {
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        File.WriteAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), css);
        string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
        return new ImportEpub(epub).GetBook();
    }

    [Fact]
    public void BareClassSelector_ProducesThreeElementVariants()
    {
        using var temp = new TempDir();
        Book book = LoadWithCss(temp, ".example { color: red; }");

        var entries = ClipAutoFill.BuildAutofillEntries(book);

        entries.Select(e => e.FullName).Should().Equal(
            "Autofill/p.example", "Autofill/span.example", "Autofill/div.example");
        entries.Should().OnlyContain(e => e.Text.Contains("\\1"));
        entries[0].Text.Should().Be("<p class=\"example\">\\1</p>");
        entries[1].Text.Should().Be("<span class=\"example\">\\1</span>");
        entries[2].Text.Should().Be("<div class=\"example\">\\1</div>");
    }

    [Fact]
    public void ElementQualifiedClassSelector_ProducesSingleEntry()
    {
        using var temp = new TempDir();
        Book book = LoadWithCss(temp, "h1.heading { font-weight: bold; }");

        var entries = ClipAutoFill.BuildAutofillEntries(book);

        entries.Select(e => e.FullName).Should().Equal("Autofill/h1.heading");
        entries.Single().Text.Should().Be("<h1 class=\"heading\">\\1</h1>");
    }

    [Fact]
    public void SelectorsWithoutClass_AreIgnored()
    {
        using var temp = new TempDir();
        Book book = LoadWithCss(temp, "body { margin: 0; } #id { color: blue; }");

        ClipAutoFill.BuildAutofillEntries(book).Should().BeEmpty();
    }

    [Fact]
    public void Entries_AddedThroughModel_MergeIntoOneAutofillGroup()
    {
        using var temp = new TempDir();
        Book book = LoadWithCss(temp, ".a { } .b { }");
        var model = new ClipEditorModel();

        foreach (ClipEntry entry in ClipAutoFill.BuildAutofillEntries(book))
        {
            model.AddFullNameEntry(entry);
        }

        model.Root.Children.Should().ContainSingle(n => n.IsGroup && n.Name == "Autofill");
    }
}
