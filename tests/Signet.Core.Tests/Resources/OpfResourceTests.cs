using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;
using SysPath = System.IO.Path;

namespace Signet.Core.Tests.Resources;

/// <summary>Tests for <see cref="OpfResource"/> — the full OPF mutation logic.</summary>
public sealed class OpfResourceTests
{
    private static OpfResource NewOpf(TempDir root, string version = "2.0")
    {
        string full = root.Combine("OEBPS", "content.opf");
        System.IO.Directory.CreateDirectory(SysPath.GetDirectoryName(full)!);
        OpfResource opf = new(root.Path, full);
        opf.FillWithDefaultText(version);
        opf.SaveToDisk();
        return opf;
    }

    private static Resource Stub(TempDir root, string bookPath, string mediaType)
    {
        string full = root.Combine(bookPath.Replace('/', SysPath.DirectorySeparatorChar));
        System.IO.Directory.CreateDirectory(SysPath.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, string.Empty);
        Resource resource = ResourceFactory.Create(root.Path, full, mediaType);
        resource.MediaType = mediaType;
        return resource;
    }

    // ---- default content / version ----

    [Theory]
    [InlineData("2.0", "2.0")]
    [InlineData("3.0", "3.0")]
    [InlineData("", "2.0")]
    public void FillWithDefaultText_produces_parseable_opf(string version, string expected)
    {
        // Arrange
        using TempDir root = new();

        // Act
        OpfResource opf = NewOpf(root, version);

        // Assert
        opf.EpubVersion.Should().Be(expected);
        opf.GetOpfDocument().Version.Should().Be(expected);
        opf.GetPackageVersion().Should().Be(expected);
        opf.GetOpfDocument().Manifest.Should().BeEmpty();
    }

    [Fact]
    public void FillWithDefaultText_v3_has_dcterms_modified()
    {
        // Arrange
        using TempDir root = new();

        // Act & Assert
        NewOpf(root, "3.0").GetText().Should().Contain("dcterms:modified");
    }

    // ---- metadata ----

    [Fact]
    public void GetMainIdentifierValue_returns_unique_identifier_content()
    {
        // Arrange
        using TempDir root = new();

        // Act & Assert
        NewOpf(root).GetMainIdentifierValue().Should().StartWith("urn:uuid:");
    }

    [Fact]
    public void EnsureUuidIdentifierPresent_is_noop_when_uuid_exists()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        string before = opf.GetText();

        // Act
        opf.EnsureUuidIdentifierPresent();

        // Assert
        opf.GetText().Should().Be(before);
        opf.GetUuidIdentifierValue().Should().NotBeEmpty();
    }

    [Fact]
    public void GetPrimaryBookTitle_and_language_read_first_dc_values()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);

        // Act & Assert
        opf.GetPrimaryBookTitle().Should().Be("[Title here]");

        // Act & Assert
        opf.GetPrimaryBookLanguage().Should().Be("en");
    }

    [Fact]
    public void SetDcMetadata_replaces_dc_elements_but_keeps_main_identifier()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        string mainId = opf.GetMainIdentifierValue();

        List<MetaEntry> metadata = new()
        {
            new MetaEntry { Name = "dc:title", Content = "Nowy tytul" },
            new MetaEntry { Name = "dc:creator", Content = "Autor" },
        };

        // Act
        opf.SetDcMetadata(metadata);

        // Assert
        opf.GetPrimaryBookTitle().Should().Be("Nowy tytul");
        opf.GetDcMetadataValues("dc:creator").Should().ContainSingle().Which.Should().Be("Autor");
        opf.GetMainIdentifierValue().Should().Be(mainId);
    }

    [Fact]
    public void AddModificationDateMeta_v3_sets_dcterms_modified()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root, "3.0");

        // Act
        string stamp = opf.AddModificationDateMeta();

        // Assert
        stamp.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$");
        opf.GetOpfDocument().Metadata
            .Count(m => m.Name == "meta" && m.Attributes.Value("property") == "dcterms:modified")
            .Should().Be(1);

        // Act
        opf.AddModificationDateMeta();

        // Assert
        opf.GetOpfDocument().Metadata
            .Count(m => m.Name == "meta" && m.Attributes.Value("property") == "dcterms:modified")
            .Should().Be(1);
    }

    [Fact]
    public void AddModificationDateMeta_v2_sets_dc_date_modification()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);

        // Act
        opf.AddModificationDateMeta();

        // Assert
        MetaEntry entry = opf.GetOpfDocument().Metadata
            .Should().ContainSingle(m => m.Name == "dc:date").Subject;
        entry.Attributes.Value("opf:event").Should().Be("modification");
    }

    [Fact]
    public void AddSignetVersionMeta_adds_then_updates_single_entry()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);

        // Act
        opf.AddSignetVersionMeta();
        opf.AddSignetVersionMeta();

        // Assert
        MetaEntry entry = opf.GetOpfDocument().Metadata
            .Should().ContainSingle(m => m.Name == "meta" && m.Attributes.Value("name") == "Signet version").Subject;
        entry.Attributes.Value("content").Should().Contain("Signet");
    }

    // ---- manifest ----

    [Fact]
    public void AddResource_appends_manifest_item_and_spine_entry_for_html()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource html = Stub(root, "OEBPS/Text/ch1.xhtml", "application/xhtml+xml");
        Resource css = Stub(root, "OEBPS/Styles/s.css", "text/css");

        // Act
        opf.AddResource(html);
        opf.AddResource(css);

        // Assert
        OpfDocument doc = opf.GetOpfDocument();
        doc.Manifest.Select(m => m.Href).Should().BeEquivalentTo("Text/ch1.xhtml", "Styles/s.css");
        doc.Spine.Should().ContainSingle();
        doc.Spine[0].IdRef.Should().Be(doc.Manifest.Single(m => m.Href == "Text/ch1.xhtml").Id);
    }

    [Fact]
    public void BulkAddResources_generates_unique_ids_for_same_filename()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource a = Stub(root, "OEBPS/Text/index.xhtml", "application/xhtml+xml");
        Resource b = Stub(root, "OEBPS/More/index.xhtml", "application/xhtml+xml");

        // Act
        opf.BulkAddResources(new[] { a, b });

        // Assert
        OpfDocument doc = opf.GetOpfDocument();
        doc.Manifest.Should().HaveCount(2);
        doc.Manifest.Select(m => m.Id).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void RemoveResource_drops_manifest_spine_and_guide_entries()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource html = Stub(root, "OEBPS/Text/ch.xhtml", "application/xhtml+xml");
        opf.AddResource(html);
        opf.AddGuideSemanticCode(html, "text");

        // Act
        opf.RemoveResource(html);

        // Assert
        OpfDocument doc = opf.GetOpfDocument();
        doc.Manifest.Should().BeEmpty();
        doc.Spine.Should().BeEmpty();
        doc.Guide.Should().BeEmpty();
    }

    [Fact]
    public void ResourceRenamed_updates_href_new_id_and_spine_idref()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource html = Stub(root, "OEBPS/Text/old.xhtml", "application/xhtml+xml");
        opf.AddResource(html);
        string oldFull = html.FullPath;

        // Act
        html.RenameTo("new.xhtml").Should().BeTrue();
        opf.ResourceRenamed(html, oldFull);

        // Assert
        OpfDocument doc = opf.GetOpfDocument();
        ManifestEntry entry = doc.Manifest.Single();
        entry.Href.Should().Be("Text/new.xhtml");
        entry.Id.Should().Be("new.xhtml");
        doc.Spine.Single().IdRef.Should().Be("new.xhtml");
    }

    [Fact]
    public void ResourceMoved_updates_href_only_keeping_id()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource html = Stub(root, "OEBPS/Text/ch.xhtml", "application/xhtml+xml");
        opf.AddResource(html);
        string id = opf.GetOpfDocument().Manifest.Single().Id;
        string oldFull = html.FullPath;

        // Act
        html.MoveTo("OEBPS/Chapters/ch.xhtml").Should().BeTrue();
        opf.ResourceMoved(html, oldFull);

        // Assert
        ManifestEntry entry = opf.GetOpfDocument().Manifest.Single();
        entry.Href.Should().Be("Chapters/ch.xhtml");
        entry.Id.Should().Be(id);
    }

    [Fact]
    public void UpdateManifestMediaTypes_corrects_stale_mime_type()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource res = Stub(root, "OEBPS/Misc/data.xml", "application/xml");
        opf.AddResource(res);
        res.MediaType = "application/oebps-page-map+xml";

        // Act
        opf.UpdateManifestMediaTypes(new[] { res });

        // Assert
        opf.GetOpfDocument().Manifest.Single().MediaType.Should().Be("application/oebps-page-map+xml");
    }

    [Fact]
    public void RebaseManifestIds_renames_ids_from_filenames_and_fixes_spine()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        OpfDocument doc = opf.GetOpfDocument();
        doc.Manifest.Add(new ManifestEntry { Id = "item1", Href = "Text/chapter.xhtml", MediaType = "application/xhtml+xml" });
        doc.Manifest.Add(new ManifestEntry { Id = "css7", Href = "Styles/main.css", MediaType = "text/css" });
        doc.Spine.Add(new SpineEntry { IdRef = "item1" });
        opf.SetOpfDocument(doc);

        // Act
        opf.RebaseManifestIds();

        OpfDocument after = opf.GetOpfDocument();

        // Assert
        after.Manifest.Single(m => m.Href == "Text/chapter.xhtml").Id.Should().Be("chapter_xhtml");
        after.Manifest.Single(m => m.Href == "Styles/main.css").Id.Should().Be("main_css");
        after.Spine.Single().IdRef.Should().Be("chapter_xhtml");
    }

    // ---- spine ----

    [Fact]
    public void AppendResourceToSpine_moves_existing_entry_to_end()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource a = Stub(root, "OEBPS/Text/a.xhtml", "application/xhtml+xml");
        Resource b = Stub(root, "OEBPS/Text/b.xhtml", "application/xhtml+xml");
        opf.AddResource(a);
        opf.AddResource(b);

        // Act
        opf.AppendResourceToSpine(a, nonlinear: true);

        // Assert
        OpfDocument doc = opf.GetOpfDocument();
        doc.Spine.Select(s => s.IdRef).Should().Equal("b.xhtml", "a.xhtml");
        doc.Spine[^1].Attributes.Value("linear").Should().Be("no");
    }

    [Fact]
    public void MoveReadingOrder_places_from_after_target()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        HtmlResource a = (HtmlResource)Stub(root, "OEBPS/Text/a.xhtml", "application/xhtml+xml");
        HtmlResource b = (HtmlResource)Stub(root, "OEBPS/Text/b.xhtml", "application/xhtml+xml");
        HtmlResource c = (HtmlResource)Stub(root, "OEBPS/Text/c.xhtml", "application/xhtml+xml");
        opf.AddResource(a);
        opf.AddResource(b);
        opf.AddResource(c);

        // Act
        opf.MoveReadingOrder(a, c);

        // Assert
        opf.GetSpineOrderBookPaths().Select(SysPath.GetFileName)
            .Should().Equal("b.xhtml", "c.xhtml", "a.xhtml");
    }

    [Fact]
    public void SetItemRefLinear_toggles_linear_attribute()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource a = Stub(root, "OEBPS/Text/a.xhtml", "application/xhtml+xml");
        opf.AddResource(a);

        // Act
        opf.SetItemRefLinear(a, linear: false);

        // Assert
        opf.GetOpfDocument().Spine.Single().Attributes.Value("linear").Should().Be("no");

        // Act
        opf.SetItemRefLinear(a, linear: true);

        // Assert
        opf.GetOpfDocument().Spine.Single().Attributes.Contains("linear").Should().BeFalse();
    }

    [Fact]
    public void GetReadingOrder_returns_spine_position_or_minus_one()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource a = Stub(root, "OEBPS/Text/a.xhtml", "application/xhtml+xml");
        Resource css = Stub(root, "OEBPS/Styles/s.css", "text/css");
        opf.AddResource(a);
        opf.AddResource(css);

        // Act & Assert
        opf.GetReadingOrder(a).Should().Be(0);

        // Act & Assert
        opf.GetReadingOrder(css).Should().Be(-1);
    }

    // ---- NCX ----

    [Fact]
    public void AddNcxItem_and_UpdateNcxOnSpine_wire_the_toc_attribute()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);

        // Act
        string ncxId = opf.AddNcxItem("OEBPS/toc.ncx");
        opf.UpdateNcxOnSpine(ncxId);

        OpfDocument doc = opf.GetOpfDocument();

        // Assert
        doc.Manifest.Single().MediaType.Should().Be("application/x-dtbncx+xml");
        doc.SpineAttributes.Attributes.Value("toc").Should().Be(ncxId);

        // Act
        opf.RemoveNcxOnSpine();

        // Assert
        opf.GetOpfDocument().SpineAttributes.Attributes.Contains("toc").Should().BeFalse();
    }

    // ---- guide / landmarks ----

    [Fact]
    public void AddGuideSemanticCode_toggles_reference_on_and_off()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource html = Stub(root, "OEBPS/Text/cover.xhtml", "application/xhtml+xml");
        opf.AddResource(html);

        // Act
        opf.AddGuideSemanticCode(html, "cover");

        // Assert
        opf.GetGuideSemanticCodeForResource(html).Should().Be("cover");
        opf.GetAllGuideInfoByBookPath().Should().ContainSingle()
            .Which.BookPath.Should().Be("OEBPS/Text/cover.xhtml");

        // Act
        opf.AddGuideSemanticCode(html, "cover");

        // Assert
        opf.GetGuideSemanticCodeForResource(html).Should().BeEmpty();
    }

    [Fact]
    public void AddGuideSemanticCode_replaces_previous_holder_of_same_code()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource a = Stub(root, "OEBPS/Text/a.xhtml", "application/xhtml+xml");
        Resource b = Stub(root, "OEBPS/Text/b.xhtml", "application/xhtml+xml");
        opf.AddResource(a);
        opf.AddResource(b);

        // Act
        opf.AddGuideSemanticCode(a, "text");
        opf.AddGuideSemanticCode(b, "text");

        // Assert
        opf.GetGuideSemanticCodeForResource(a).Should().BeEmpty();
        opf.GetGuideSemanticCodeForResource(b).Should().Be("text");
        opf.GetOpfDocument().Guide.Should().ContainSingle();
    }

    [Fact]
    public void ClearSemanticCodesInGuide_removes_all_entries()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        Resource a = Stub(root, "OEBPS/Text/a.xhtml", "application/xhtml+xml");
        opf.AddResource(a);
        opf.AddGuideSemanticCode(a, "text");

        // Act
        opf.ClearSemanticCodesInGuide();

        // Assert
        opf.GetOpfDocument().Guide.Should().BeEmpty();
    }

    // ---- cover ----

    [Fact]
    public void SetResourceAsCoverImage_adds_meta_and_epub3_property()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root, "3.0");
        ImageResource image = (ImageResource)Stub(root, "OEBPS/Images/cover.png", "image/png");
        opf.AddResource(image);

        // Act
        opf.SetResourceAsCoverImage(image);

        // Assert
        opf.IsCoverImage(image).Should().BeTrue();
        opf.GetCoverImagePath().Should().Be("OEBPS/Images/cover.png");
        opf.GetOpfDocument().Manifest.Single().Attributes.Value("properties").Should().Contain("cover-image");
    }

    [Fact]
    public void SetResourceAsCoverImage_switches_cover_between_images()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root, "3.0");
        ImageResource first = (ImageResource)Stub(root, "OEBPS/Images/a.png", "image/png");
        ImageResource second = (ImageResource)Stub(root, "OEBPS/Images/b.png", "image/png");
        opf.AddResource(first);
        opf.AddResource(second);

        // Act
        opf.SetResourceAsCoverImage(first);
        opf.SetResourceAsCoverImage(second);

        // Assert
        opf.IsCoverImage(first).Should().BeFalse();
        opf.IsCoverImage(second).Should().BeTrue();
        opf.GetOpfDocument().Manifest.Single(m => m.Href == "Images/a.png")
            .Attributes.Value("properties").Should().NotContain("cover-image");
    }

    // ---- well-formed ----

    [Fact]
    public void AutoFixWellFormedErrors_fills_empty_spine_from_xhtml_manifest_items()
    {
        // Arrange
        using TempDir root = new();
        OpfResource opf = NewOpf(root);
        OpfDocument doc = opf.GetOpfDocument();
        doc.Manifest.Add(new ManifestEntry { Id = "b", Href = "Text/b.xhtml", MediaType = "application/xhtml+xml" });
        doc.Manifest.Add(new ManifestEntry { Id = "a", Href = "Text/a.xhtml", MediaType = "application/xhtml+xml" });
        doc.Manifest.Add(new ManifestEntry { Id = "s", Href = "Styles/s.css", MediaType = "text/css" });
        opf.SetOpfDocument(doc);

        // Act
        opf.AutoFixWellFormedErrors();

        // Assert
        opf.GetOpfDocument().Spine.Select(s => s.IdRef).Should().Equal("a", "b");
    }

    [Fact]
    public void UpdateManifestProperties_keeps_the_nav_property_of_the_nav_document()
    {
        // Arrange
        using Signet.Core.BookManipulation.Book book = Signet.Core.BookManipulation.BookCreator.CreateNewBook("3.0");
        string navBookPath = book.GetOpf().GetNavResourceBookPath();

        navBookPath.Should().NotBeEmpty();

        // Act
        book.GetOpf().UpdateManifestProperties(book.GetHtmlResources());

        // Assert
        book.GetOpf().GetNavResourceBookPath().Should().Be(navBookPath);
    }
}
