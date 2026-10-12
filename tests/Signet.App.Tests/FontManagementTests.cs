using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The font workflows of the Book Browser: the fonts context menu, adding fonts with <c>@font-face</c> rules,
/// deleting them with the stylesheet cleanup, renaming a family and "Manage Fonts".
/// </summary>
public sealed class FontManagementTests
{
    private static BookBrowserViewModel NewViewModel() =>
        new(new SettingsStore(Path.Combine(Path.GetTempPath(), $"signet-fonts-{System.Guid.NewGuid():N}.json")), new StatusBarService());

    private static Book Load(string corpusDir, TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(corpusDir, temp)).GetBook();

    private static List<string> WriteFonts(TempDir temp)
    {
        string regular = temp.Combine("Acme-Regular.ttf");
        string italic = temp.Combine("Acme-Italic.ttf");
        File.WriteAllBytes(regular, FontFileBuilder.Ttf("Acme Text", "Regular", 400, false));
        File.WriteAllBytes(italic, FontFileBuilder.Ttf("Acme Text", "Italic", 400, true));
        return new List<string> { regular, italic };
    }

    [Fact]
    public void The_fonts_menu_is_used_for_the_Fonts_folder_and_font_files_only()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel();
        sut.SetBook(book);
        BookBrowserNode fonts = sut.Nodes.Single(n => n.FolderKind == Signet.Core.MainUI.OpfModelGroupKind.Fonts);
        BookBrowserNode text = sut.Nodes.Single(n => n.FolderKind == Signet.Core.MainUI.OpfModelGroupKind.Text);

        sut.UpdateSelection(new object[] { fonts });
        sut.IsFontMenu.Should().BeTrue();
        sut.IsFontFolderMenu.Should().BeTrue();

        sut.UpdateSelection(new object[] { fonts.Children[0] });
        sut.IsFontMenu.Should().BeTrue();
        sut.IsFontFolderMenu.Should().BeFalse();

        sut.UpdateSelection(new object[] { fonts.Children[0], text.Children[0] });
        sut.IsFontMenu.Should().BeFalse();

        sut.UpdateSelection(new object[] { text });
        sut.IsFontMenu.Should().BeFalse();
    }

    [Fact]
    public void Select_all_on_the_Fonts_folder_selects_its_files()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        BookBrowserViewModel sut = NewViewModel();
        sut.SetBook(book);
        BookBrowserNode fonts = sut.Nodes.Single(n => n.FolderKind == Signet.Core.MainUI.OpfModelGroupKind.Fonts);
        IReadOnlyList<BookBrowserNode>? selected = null;
        sut.TreeSelectionRequested += (_, nodes) => selected = nodes;

        sut.UpdateSelection(new object[] { fonts });
        sut.SelectAllInFolderCommand.Execute(null);

        selected.Should().Equal(fonts.Children);
    }

    [Fact]
    public void Added_fonts_get_rules_and_deleting_them_cleans_the_stylesheet()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Minimal, temp);
        BookBrowserViewModel sut = NewViewModel();
        sut.SetBook(book);
        CssResource css = book.GetCssResources().Single();

        List<FontResource> fonts = sut.AddFiles(WriteFonts(temp)).OfType<FontResource>().ToList();
        FontFaceDialogViewModel dialog = sut.PrepareFontFaces(fonts)!;
        dialog.Rows.Select(r => (r.Family, r.Weight, r.Style)).Should().Equal(("Acme Text", "normal", "normal"), ("Acme Text", "normal", "italic"));
        dialog.SelectedStylesheet.Stylesheet.Should().BeSameAs(css);
        dialog.SetBodyFont = true;
        sut.ApplyFontFaces(dialog.BuildResult()).Should().BeSameAs(css);

        css.GetText().Should().Contain("font-family: \"Acme Text\";").And.Contain("font-style: italic;")
            .And.Contain("font-family: \"Acme Text\", serif;");

        FontRemovalViewModel removal = sut.PrepareFontRemoval(fonts.Cast<Resource>().ToList())!;
        removal.RuleItems.Should().HaveCount(2);
        removal.UsageItems.Should().ContainSingle();
        sut.DeleteResources(fonts.Cast<Resource>().ToList(), removal.Plan, removal.ExcludedKeys());

        book.GetAllResources().OfType<FontResource>().Should().BeEmpty();
        css.GetText().Should().NotContain("Acme");
    }

    [Fact]
    public void A_face_typed_twice_in_the_dialog_is_skipped_with_a_reason()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Minimal, temp);
        BookBrowserViewModel sut = NewViewModel();
        sut.SetBook(book);
        List<FontResource> fonts = sut.AddFiles(WriteFonts(temp)).OfType<FontResource>().ToList();
        FontFaceDialogViewModel dialog = sut.PrepareFontFaces(fonts)!;

        dialog.Rows[1].Style = "normal";

        dialog.Rows[1].IsConflict.Should().BeTrue();
        dialog.Rows[1].ConflictText.Should().Contain("Acme-Regular.ttf");
        dialog.BuildResult().Requests.Should().ContainSingle();

        dialog.Rows[1].Family = "Acme Display";
        dialog.Rows[1].IsConflict.Should().BeFalse();
        dialog.BodyFamilies.Should().Equal("Acme Text", "Acme Display");
    }

    [Fact]
    public void Deleting_a_font_no_stylesheet_uses_needs_no_font_window()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Minimal, temp);
        BookBrowserViewModel sut = NewViewModel();
        sut.SetBook(book);
        List<Resource> fonts = sut.AddFiles(WriteFonts(temp)).ToList();

        sut.PrepareFontRemoval(fonts).Should().BeNull();
    }

    [Fact]
    public void Manage_fonts_lists_files_and_families_and_renames_a_family()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Minimal, temp);
        BookBrowserViewModel sut = NewViewModel();
        sut.SetBook(book);
        List<FontResource> fonts = sut.AddFiles(WriteFonts(temp)).OfType<FontResource>().ToList();
        FontFaceDialogViewModel dialog = sut.PrepareFontFaces(fonts.Take(1).ToList())!;
        dialog.SetBodyFont = true;
        sut.ApplyFontFaces(dialog.BuildResult());

        ManageFontsViewModel manage = new(sut, null);

        manage.Files.Select(f => (f.FileName, f.RuleCount)).Should().Equal(("Acme-Regular.ttf", 1), ("Acme-Italic.ttf", 0));
        manage.Files[1].StatusText.Should().NotBeEmpty();
        manage.SelectedFamily!.Family.Should().Be("Acme Text");
        manage.Details.Should().Contain(d => d.Kind == "@font-face").And.Contain(d => d.Kind == "font-family");

        sut.RenameFontFamily("Acme Text", "Acme").Should().BeTrue();
        manage.Refresh();
        manage.Families.Select(f => f.Family).Should().Contain("Acme").And.NotContain("Acme Text");
    }
}
