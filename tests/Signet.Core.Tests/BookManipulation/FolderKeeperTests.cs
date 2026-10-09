using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="FolderKeeper"/> — the book resource registry and collection operations.</summary>
public sealed class FolderKeeperTests
{
    private static FolderKeeper NewKeeper(TempDir root, bool watch = false) =>
        new(new TempFolder(root.Path), watch);

    private static string MakeSourceFile(TempDir root, string name, string content = "x")
    {
        string path = root.Combine("src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Creates_and_removes_main_folder()
    {
        using TempDir root = new();
        FolderKeeper keeper = NewKeeper(root);
        string mainFolder = keeper.MainFolderPath;

        Directory.Exists(mainFolder).Should().BeTrue();
        mainFolder.Should().NotEndWith(Path.DirectorySeparatorChar.ToString());

        keeper.Dispose();
        Directory.Exists(mainFolder).Should().BeFalse();
    }

    [Fact]
    public void Two_instances_are_isolated()
    {
        using TempDir root = new();
        using FolderKeeper a = NewKeeper(root);
        using FolderKeeper b = NewKeeper(root);

        a.MainFolderPath.Should().NotBe(b.MainFolderPath);

        string source = MakeSourceFile(root, "one.css");
        a.AddContentFileToFolder(source);

        a.GetResourceList().Should().HaveCount(1);
        b.GetResourceList().Should().BeEmpty();
    }

    [Theory]
    [InlineData("chapter.xhtml", "OEBPS/Text/chapter.xhtml")]
    [InlineData("style.css", "OEBPS/Styles/style.css")]
    [InlineData("pic.png", "OEBPS/Images/pic.png")]
    [InlineData("font.otf", "OEBPS/Fonts/font.otf")]
    public void AddContentFileToFolder_places_file_in_default_group_folder(string name, string expectedBookPath)
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        string source = MakeSourceFile(root, name);

        Resource resource = keeper.AddContentFileToFolder(source, updateOpf: false);

        resource.BookPath.Should().Be(expectedBookPath);
        File.Exists(Path.Combine(keeper.MainFolderPath, expectedBookPath.Replace('/', Path.DirectorySeparatorChar)))
            .Should().BeTrue();
        keeper.GetResourceByBookPath(expectedBookPath).Should().BeSameAs(resource);
        keeper.GetResourceByIdentifier(resource.Identifier).Should().BeSameAs(resource);
    }

    [Fact]
    public void AddContentFileToFolder_honours_explicit_book_path()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        string source = MakeSourceFile(root, "c.xhtml");

        Resource resource = keeper.AddContentFileToFolder(source, updateOpf: false, bookPath: "OEBPS/nested/deep/c.xhtml");

        resource.BookPath.Should().Be("OEBPS/nested/deep/c.xhtml");
        File.Exists(resource.FullPath).Should().BeTrue();
    }

    [Fact]
    public void AddContentFileToFolder_uniquifies_colliding_filenames()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);

        Resource first = keeper.AddContentFileToFolder(MakeSourceFile(root, "Section.xhtml"), updateOpf: false);
        Resource second = keeper.AddContentFileToFolder(MakeSourceFile(root, "Section.xhtml"), updateOpf: false);

        first.BookPath.Should().Be("OEBPS/Text/Section.xhtml");
        second.BookPath.Should().Be("OEBPS/Text/Section0001.xhtml");
    }

    [Theory]
    [InlineData("Section0001.xhtml", new[] { "Section0001.xhtml" }, "Section0002.xhtml")]
    [InlineData("cover.png", new[] { "cover.png" }, "cover0001.png")]
    [InlineData("fresh.css", new string[0], "fresh.css")]
    public void GetUniqueFilenameVersion_follows_the_naming_rules(string candidate, string[] existing, string expected)
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        foreach (string name in existing)
        {
            keeper.AddContentFileToFolder(MakeSourceFile(root, name), updateOpf: false);
        }

        keeper.GetUniqueFilenameVersion(candidate).Should().Be(expected);
    }

    [Fact]
    public void DetermineFileGroup_classifies_by_media_type_then_extension()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);

        keeper.DetermineFileGroup("a.xhtml", "").Should().Be("Text");
        keeper.DetermineFileGroup("a.css", "").Should().Be("Styles");
        keeper.DetermineFileGroup("a.bin", "").Should().Be("Misc");
        keeper.DetermineFileGroup("META-INF/com.apple.ibooks.display-options.xml", "").Should().Be("other");
    }

    [Fact]
    public void AddOpfToFolder_creates_parseable_default_opf_and_container_xml()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);

        OpfResource opf = keeper.AddOpfToFolder("2.0");

        opf.Should().BeSameAs(keeper.Opf);
        opf.BookPath.Should().Be("OEBPS/content.opf");
        opf.MediaType.Should().Be("application/oebps-package+xml");
        File.Exists(opf.FullPath).Should().BeTrue();

        OpfDocument document = opf.GetOpfDocument();
        document.Version.Should().Be("2.0");
        document.GetMetadataXml().Should().Contain("dc:title");

        string containerXml = File.ReadAllText(Path.Combine(keeper.MainFolderPath, "META-INF", "container.xml"));
        containerXml.Should().Contain("full-path=\"OEBPS/content.opf\"");
    }

    [Fact]
    public void AddNcxToFolder_creates_default_ncx_with_main_id()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("2.0");

        NcxResource ncx = keeper.AddNcxToFolder("2.0");

        ncx.Should().BeSameAs(keeper.Ncx);
        ncx.BookPath.Should().Be("OEBPS/toc.ncx");
        ncx.MediaType.Should().Be("application/x-dtbncx+xml");
        ncx.GetText().Should().NotContain("ID_UNKNOWN");
        ncx.GetText().Should().Contain(keeper.Opf!.GetMainIdentifierValue());
    }

    [Fact]
    public void RemoveNcxFromFolder_unregisters_and_deletes()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("3.0");
        NcxResource ncx = keeper.AddNcxToFolder("3.0");
        string ncxPath = ncx.FullPath;

        keeper.RemoveNcxFromFolder();

        keeper.Ncx.Should().BeNull();
        keeper.GetResourceByBookPathNoThrow("OEBPS/toc.ncx").Should().BeNull();
        File.Exists(ncxPath).Should().BeFalse();
    }

    [Fact]
    public void AddContentFileToFolder_with_updateOpf_adds_manifest_item()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("2.0");

        Resource resource = keeper.AddContentFileToFolder(MakeSourceFile(root, "ch1.xhtml"), updateOpf: true);

        OpfDocument document = keeper.Opf!.GetOpfDocument();
        ManifestEntry entry = document.Manifest.Should().ContainSingle().Subject;
        entry.Href.Should().Be("Text/ch1.xhtml");
        entry.MediaType.Should().Be("application/xhtml+xml");
        entry.Id.Should().Be("ch1.xhtml");
        _ = resource;
    }

    [Fact]
    public void AddContentFileToFolder_raises_ResourceAdded_only_when_updateOpf()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("2.0");
        List<Resource> added = new();
        keeper.ResourceAdded += (_, e) => added.Add(e.Resource);

        keeper.AddContentFileToFolder(MakeSourceFile(root, "a.css"), updateOpf: false);
        keeper.AddContentFileToFolder(MakeSourceFile(root, "b.css"), updateOpf: true);

        added.Should().ContainSingle().Which.BookPath.Should().Be("OEBPS/Styles/b.css");
    }

    [Fact]
    public void Renaming_a_resource_updates_the_path_map_and_manifest_href()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("2.0");
        Resource resource = keeper.AddContentFileToFolder(MakeSourceFile(root, "old.xhtml"), updateOpf: true);

        resource.RenameTo("new.xhtml").Should().BeTrue();

        keeper.GetResourceByBookPathNoThrow("OEBPS/Text/old.xhtml").Should().BeNull();
        keeper.GetResourceByBookPath("OEBPS/Text/new.xhtml").Should().BeSameAs(resource);
        keeper.Opf!.GetOpfDocument().Manifest.Single().Href.Should().Be("Text/new.xhtml");
    }

    [Fact]
    public void Renaming_a_resource_reports_its_old_bookpath()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("2.0");
        Resource resource = keeper.AddContentFileToFolder(MakeSourceFile(root, "old.xhtml"), updateOpf: true);
        List<ResourceBookPathChangedEventArgs> changes = new();
        keeper.ResourceBookPathChanged += (_, e) => changes.Add(e);

        resource.RenameTo("new.xhtml");

        changes.Should().ContainSingle().Which.Should().Match<ResourceBookPathChangedEventArgs>(e =>
            e.OldBookPath == "OEBPS/Text/old.xhtml" && e.Resource == resource && resource.BookPath == "OEBPS/Text/new.xhtml");
    }

    [Fact]
    public void Deleting_a_resource_removes_it_from_registry_and_manifest()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("2.0");
        Resource keep = keeper.AddContentFileToFolder(MakeSourceFile(root, "keep.xhtml"), updateOpf: true);
        Resource drop = keeper.AddContentFileToFolder(MakeSourceFile(root, "drop.xhtml"), updateOpf: true);
        List<Resource> removed = new();
        keeper.ResourceRemoved += (_, e) => removed.Add(e.Resource);

        drop.Delete().Should().BeTrue();

        keeper.GetResourceList().Should().Contain(keep).And.NotContain(drop);
        keeper.GetResourceByBookPathNoThrow("OEBPS/Text/drop.xhtml").Should().BeNull();
        removed.Should().ContainSingle().Which.Should().BeSameAs(drop);
        keeper.Opf!.GetOpfDocument().Manifest.Single().Href.Should().Be("Text/keep.xhtml");
    }

    [Fact]
    public void BulkRemoveResources_deletes_files_and_manifest_items()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("2.0");
        Resource a = keeper.AddContentFileToFolder(MakeSourceFile(root, "a.xhtml"), updateOpf: true);
        Resource b = keeper.AddContentFileToFolder(MakeSourceFile(root, "b.xhtml"), updateOpf: true);
        string pathA = a.FullPath;

        Resource[] toRemove = { a, b };
        keeper.BulkRemoveResources(toRemove);

        keeper.GetResourceList().Should().ContainSingle().Which.Should().BeSameAs(keeper.Opf);
        File.Exists(pathA).Should().BeFalse();
        keeper.Opf!.GetOpfDocument().Manifest.Should().BeEmpty();
    }

    [Fact]
    public void GetResourceTypeList_of_html_follows_spine_order_when_opf_present()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("2.0");
        Resource a = keeper.AddContentFileToFolder(MakeSourceFile(root, "a.xhtml"), updateOpf: true);
        Resource b = keeper.AddContentFileToFolder(MakeSourceFile(root, "b.xhtml"), updateOpf: true);

        // Przestaw spine recznie: b przed a.
        OpfDocument document = keeper.Opf!.GetOpfDocument();
        document.Spine.Clear();
        document.Spine.Add(new SpineEntry { IdRef = document.Manifest.Single(m => m.Href.EndsWith("b.xhtml", System.StringComparison.Ordinal)).Id });
        document.Spine.Add(new SpineEntry { IdRef = document.Manifest.Single(m => m.Href.EndsWith("a.xhtml", System.StringComparison.Ordinal)).Id });
        keeper.Opf!.SetOpfDocument(document);

        IReadOnlyList<HtmlResource> ordered = keeper.GetResourceTypeList<HtmlResource>(sorted: true);

        ordered.Select(r => r.Filename).Should().Equal("b.xhtml", "a.xhtml");
        _ = (a, b);
    }

    [Fact]
    public void GetHighestReadingOrder_counts_html_resources()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("2.0");
        keeper.GetHighestReadingOrder().Should().Be(-1);

        keeper.AddContentFileToFolder(MakeSourceFile(root, "a.xhtml"), updateOpf: false);
        keeper.AddContentFileToFolder(MakeSourceFile(root, "b.xhtml"), updateOpf: false);
        keeper.AddContentFileToFolder(MakeSourceFile(root, "s.css"), updateOpf: false);

        keeper.GetHighestReadingOrder().Should().Be(1);
    }

    [Fact]
    public void GetBookPathByPathEnd_matches_on_full_filename()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddContentFileToFolder(MakeSourceFile(root, "chapter1.xhtml"), updateOpf: false);

        keeper.GetBookPathByPathEnd("Text/chapter1.xhtml").Should().Be("OEBPS/Text/chapter1.xhtml");
        keeper.GetBookPathByPathEnd("apter1.xhtml").Should().BeEmpty();
        keeper.GetBookPathByPathEnd("nope.xhtml").Should().BeEmpty();
    }

    [Fact]
    public void RefreshGroupFolders_picks_dominant_folder_per_group()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddContentFileToFolder(MakeSourceFile(root, "a.xhtml"), updateOpf: false, bookPath: "book/chapters/a.xhtml");
        keeper.AddContentFileToFolder(MakeSourceFile(root, "b.xhtml"), updateOpf: false, bookPath: "book/chapters/b.xhtml");
        keeper.AddContentFileToFolder(MakeSourceFile(root, "c.xhtml"), updateOpf: false, bookPath: "book/other/c.xhtml");

        keeper.RefreshGroupFolders();

        keeper.GetDefaultFolderForGroup("Text").Should().Be("book/chapters");
    }

    [Fact]
    public void SetGroupFolders_backfills_missing_groups_from_common_base()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);

        string[] bookPaths = { "EPUB/xhtml/a.xhtml", "EPUB/css/a.css" };
        string[] mediaTypes = { "application/xhtml+xml", "text/css" };
        keeper.SetGroupFolders(bookPaths, mediaTypes);

        keeper.GetDefaultFolderForGroup("Text").Should().Be("EPUB/xhtml");
        keeper.GetDefaultFolderForGroup("Styles").Should().Be("EPUB/css");
        keeper.GetDefaultFolderForGroup("Images").Should().Be("EPUB/Images");
    }

    [Fact]
    public void EpubInSignetStandardForm_true_for_default_layout()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        keeper.AddOpfToFolder("2.0");
        keeper.AddNcxToFolder("2.0");

        keeper.EpubInSignetStandardForm().Should().BeTrue();
    }

    [Fact]
    public void EpubInSignetStandardForm_false_without_opf()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);

        keeper.EpubInSignetStandardForm().Should().BeFalse();
    }

    [Fact]
    public void UpdateShortPathNames_disambiguates_duplicate_filenames()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        Resource one = keeper.AddContentFileToFolder(MakeSourceFile(root, "index.xhtml"), updateOpf: false, bookPath: "OEBPS/partA/index.xhtml");
        Resource two = keeper.AddContentFileToFolder(MakeSourceFile(root, "index.xhtml"), updateOpf: false, bookPath: "OEBPS/partB/index.xhtml");
        Resource solo = keeper.AddContentFileToFolder(MakeSourceFile(root, "solo.xhtml"), updateOpf: false, bookPath: "OEBPS/partA/solo.xhtml");

        keeper.UpdateShortPathNames();

        one.ShortPathName.Should().Be("partA/index.xhtml");
        two.ShortPathName.Should().Be("partB/index.xhtml");
        solo.ShortPathName.Should().Be("solo.xhtml");
    }

    [Fact]
    public void PerformInitialLoads_loads_non_html_text_resources()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        Resource css = keeper.AddContentFileToFolder(MakeSourceFile(root, "s.css", "body{}"), updateOpf: false);

        keeper.PerformInitialLoads();

        ((TextResource)css).IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void GetLinkedResources_resolves_known_book_paths_only()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        Resource css = keeper.AddContentFileToFolder(MakeSourceFile(root, "s.css"), updateOpf: false);

        string[] queried = { "OEBPS/Styles/s.css", "OEBPS/missing.css" };
        IReadOnlyList<Resource> linked = keeper.GetLinkedResources(queried);

        linked.Should().ContainSingle().Which.Should().BeSameAs(css);
    }

    [Fact]
    public void Watching_disabled_by_default_makes_watch_calls_harmless()
    {
        using TempDir root = new();
        using FolderKeeper keeper = NewKeeper(root);
        Resource css = keeper.AddContentFileToFolder(MakeSourceFile(root, "s.css"), updateOpf: false);

        System.Action act = () =>
        {
            keeper.WatchResourceFile(css);
            keeper.SuspendWatchingResources();
            keeper.ResumeWatchingResources();
        };

        act.Should().NotThrow();
    }
}
