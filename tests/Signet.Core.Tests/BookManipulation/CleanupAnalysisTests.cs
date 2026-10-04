using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of the whole-book Cleanup (<see cref="CleanupAnalysis"/>, <see cref="Book.ApplyCleanup"/>): each step on
/// its own, the order-dependent interactions between the steps and applying the simulated plan.
/// </summary>
public sealed class CleanupAnalysisTests
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();

    private static Book LoadModified(string corpus, TempDir temp, Action<string> mutate)
    {
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(corpus, tree);
        mutate(tree);
        string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
        return new ImportEpub(epub).GetBook();
    }

    private static Book LoadMedia(TempDir temp, Action<string> mutate) => LoadModified(CorpusPaths.Epub3Media, temp, mutate);

    private static Book LoadMinimal(TempDir temp, Action<string> mutate) => LoadModified(CorpusPaths.Epub3Minimal, temp, mutate);

    private static void AddManifestItem(string tree, string id, string href, string mediaType)
    {
        string opfPath = Path.Combine(tree, "EPUB", "package.opf");
        File.WriteAllText(
            opfPath,
            File.ReadAllText(opfPath).Replace(
                "  </manifest>",
                $"    <item id=\"{id}\" href=\"{href}\" media-type=\"{mediaType}\"/>\n  </manifest>",
                StringComparison.Ordinal));
    }

    private static void AddOrphanImage(string tree, string filename)
    {
        AddManifestItem(tree, Path.GetFileNameWithoutExtension(filename), $"images/{filename}", "image/png");
        File.Copy(Path.Combine(tree, "EPUB", "images", "cover.png"), Path.Combine(tree, "EPUB", "images", filename));
    }

    private static void AddOrphanStylesheet(string tree, string filename, string css)
    {
        File.WriteAllText(Path.Combine(tree, "EPUB", "styles", filename), css);
        AddManifestItem(tree, Path.GetFileNameWithoutExtension(filename) + "-css", $"styles/{filename}", "text/css");
    }

    private static void AppendCss(string tree, string css) =>
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n" + css + "\n");

    private static void ReplaceInChapter1(string tree, string oldText, string newText)
    {
        string path = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(path, File.ReadAllText(path).Replace(oldText, newText, StringComparison.Ordinal));
    }

    private static void AppendToChapter1Body(string tree, string html) => ReplaceInChapter1(tree, "</body>", html + "\n</body>");

    private static CleanupAnalysis Analyse(Book book)
    {
        CleanupPreparation preparation = CleanupAnalysis.Prepare(book);
        preparation.NotWellFormed.Should().BeNull();
        return preparation.Analysis!;
    }

    private static CleanupPlan Plan(Book book, params CleanupStep[] steps) =>
        Analyse(book).Plan(steps.ToHashSet(), NoExclusions);

    private static CleanupStepResult Step(CleanupPlan plan, CleanupStep step) => plan.GetStep(step)!;

    // -----------------------------------------------------------------
    //  Preparation / applying
    // -----------------------------------------------------------------

    [Fact]
    public void Prepare_is_held_back_when_any_html_is_not_well_formed()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, _ => { });
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText("<html><head><body><p>broken");

        CleanupPreparation preparation = CleanupAnalysis.Prepare(book);

        preparation.Analysis.Should().BeNull();
        preparation.NotWellFormed.Should().BeSameAs(chapter);
    }

    [Fact]
    public void Plan_lists_only_enabled_steps_and_does_not_modify_the_book()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree => AppendCss(tree, ".ghost { color: red }"));
        string cssBefore = book.GetCssResources().Single().GetText();

        CleanupPlan plan = Plan(book, CleanupStep.UnusedSelectors);

        plan.Steps.Select(s => s.Step).Should().Equal(CleanupStep.UnusedSelectors);
        plan.HasChanges.Should().BeTrue();
        book.GetCssResources().Single().GetText().Should().Be(cssBefore);
        book.Modified.Should().BeFalse();
    }

    [Fact]
    public void ApplyCleanup_with_an_empty_plan_returns_false_and_does_not_modify()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, _ => { });

        CleanupPlan plan = Plan(book);

        book.ApplyCleanup(plan).Should().BeFalse();
        book.Modified.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    //  Unused media
    // -----------------------------------------------------------------

    [Fact]
    public void UnusedMedia_does_not_flag_the_cover_or_used_media()
    {
        using TempDir temp = new();
        using Book book = LoadMedia(temp, _ => { });

        Step(Plan(book, CleanupStep.UnusedMedia), CleanupStep.UnusedMedia).Items.Should().BeEmpty();
    }

    [Fact]
    public void UnusedMedia_flags_a_resource_referenced_from_nowhere_and_apply_removes_it()
    {
        using TempDir temp = new();
        using Book book = LoadMedia(temp, tree => AddOrphanImage(tree, "orphan.png"));
        Resource orphan = book.GetMediaResources().Single(r => r.Filename == "orphan.png");
        string fullPath = orphan.FullPath;

        CleanupPlan plan = Plan(book, CleanupStep.UnusedMedia);
        Step(plan, CleanupStep.UnusedMedia).Items.Select(i => i.BookPath).Should().Equal(orphan.BookPath);

        book.ApplyCleanup(plan).Should().BeTrue();

        book.GetMediaResources().Should().NotContain(orphan);
        File.Exists(fullPath).Should().BeFalse();
        book.Modified.Should().BeTrue();
    }

    [Theory]
    [InlineData("attribute")]
    [InlineData("stylesheet")]
    [InlineData("style-block")]
    public void UnusedMedia_does_not_flag_an_image_used_only_from_css(string where)
    {
        using TempDir temp = new();
        using Book book = LoadMedia(temp, tree =>
        {
            AddOrphanImage(tree, "bg.png");
            switch (where)
            {
                case "attribute":
                    AppendToChapter1Body(tree, "<div style=\"background-image:url(../images/bg.png)\"></div>");
                    break;
                case "stylesheet":
                    AppendCss(tree, "body { background-image: url(../images/bg.png); }");
                    break;
                default:
                    ReplaceInChapter1(tree, "</head>", "<style>body { background: url(../images/bg.png); }</style>\n</head>");
                    break;
            }
        });

        Step(Plan(book, CleanupStep.UnusedMedia), CleanupStep.UnusedMedia).Items
            .Should().NotContain(i => i.BookPath.EndsWith("bg.png", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------
    //  Unused selectors
    // -----------------------------------------------------------------

    [Fact]
    public void UnusedSelectors_flags_only_selectors_that_match_nothing()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AppendCss(tree, ".ghost { color: red }\n.tag { color: green }");
            ReplaceInChapter1(tree, "<p>Hello, world.</p>", "<p class=\"tag\">Hello, world.</p>");
        });

        Step(Plan(book, CleanupStep.UnusedSelectors), CleanupStep.UnusedSelectors).Items
            .Select(i => i.Text).Should().Equal(".ghost");
    }

    [Fact]
    public void UnusedSelectors_apply_removes_the_selector_from_the_stylesheet_and_keeps_the_others()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree => AppendCss(tree, ".ghost { color: red }"));

        book.ApplyCleanup(Plan(book, CleanupStep.UnusedSelectors)).Should().BeTrue();

        string css = book.GetCssResources().Single().GetText();
        css.Should().NotContain(".ghost");
        css.Should().Contain("body").And.Contain("h1");
    }

    [Fact]
    public void UnusedSelectors_apply_removes_the_selector_from_a_style_block()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
            ReplaceInChapter1(tree, "</head>", "<style>.ghost2 { color: blue }\nh1 { color: purple }</style>\n</head>"));

        book.ApplyCleanup(Plan(book, CleanupStep.UnusedSelectors)).Should().BeTrue();

        string chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText();
        chapter.Should().NotContain(".ghost2");
        chapter.Should().Contain("purple");
    }

    [Fact]
    public void An_excluded_item_is_listed_but_not_applied()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree => AppendCss(tree, ".ghost { color: red }\n.ghost2 { color: red }"));
        CleanupAnalysis analysis = Analyse(book);
        HashSet<CleanupStep> steps = new() { CleanupStep.UnusedSelectors };
        string ghostKey = analysis.Plan(steps, NoExclusions).GetStep(CleanupStep.UnusedSelectors)!
            .Items.Single(i => i.Text == ".ghost").Key;

        CleanupPlan plan = analysis.Plan(steps, new HashSet<string> { ghostKey });

        Step(plan, CleanupStep.UnusedSelectors).Items.Should().HaveCount(2);
        Step(plan, CleanupStep.UnusedSelectors).AppliedCount.Should().Be(1);
        book.ApplyCleanup(plan);
        string css = book.GetCssResources().Single().GetText();
        css.Should().Contain(".ghost {").And.NotContain(".ghost2");
    }

    // -----------------------------------------------------------------
    //  Merges
    // -----------------------------------------------------------------

    [Fact]
    public void MergeSameSelectors_finds_and_merges_rules_with_the_same_selector()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree => AppendCss(tree, "body { padding: 1px }"));

        CleanupPlan plan = Plan(book, CleanupStep.MergeSameSelectors);
        CleanupStepResult step = Step(plan, CleanupStep.MergeSameSelectors);
        step.Items.Should().ContainSingle(i => i.Text.Contains("body", StringComparison.Ordinal));
        step.AppliedRuleCount.Should().Be(2);

        book.ApplyCleanup(plan).Should().BeTrue();

        string css = book.GetCssResources().Single().GetText();
        css.Should().Contain("padding: 1px");
        new CssInfo(css).Rules.Count(r => r.SelectorText == "body").Should().Be(1);
    }

    [Fact]
    public void MergeSameProperties_finds_rules_with_identical_properties()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree => AppendCss(tree, ".twin1 { color: teal } .twin2 { color: teal }"));

        Step(Plan(book, CleanupStep.MergeSameProperties), CleanupStep.MergeSameProperties).Items.Should().ContainSingle();
    }

    // -----------------------------------------------------------------
    //  Unreferenced stylesheets
    // -----------------------------------------------------------------

    [Fact]
    public void UnreferencedStylesheets_flags_a_stylesheet_no_html_links_and_apply_removes_it()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree => AddOrphanStylesheet(tree, "orphan.css", "body { color: pink }"));
        CssResource orphan = book.GetCssResources().Single(c => c.Filename == "orphan.css");
        string fullPath = orphan.FullPath;

        CleanupPlan plan = Plan(book, CleanupStep.UnreferencedStylesheets);
        Step(plan, CleanupStep.UnreferencedStylesheets).Items.Select(i => i.BookPath).Should().Equal(orphan.BookPath);

        book.ApplyCleanup(plan).Should().BeTrue();

        book.GetCssResources().Should().NotContain(orphan);
        File.Exists(fullPath).Should().BeFalse();
    }

    // -----------------------------------------------------------------
    //  Risky merges
    // -----------------------------------------------------------------

    [Fact]
    public void A_merge_is_placed_where_it_keeps_the_styling()
    {
        // <p class="a b">: the last ".a" wins (blue). At the first ".a" the merge would let ".b" (green) win, so it
        // goes to the place of the last ".a" instead — and is safe.
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AppendCss(tree, ".a { color: red; }\n.b { color: green; }\n.a { color: blue; }");
            ReplaceInChapter1(tree, "<p>Hello, world.</p>", "<p class=\"a b\">Hello, world.</p>");
        });

        CleanupPlan plan = Plan(book, CleanupStep.MergeSameSelectors);
        CleanupItem merge = Step(plan, CleanupStep.MergeSameSelectors).Items.Single();

        merge.IsRisky.Should().BeFalse();
        merge.IsApplied.Should().BeTrue();
        book.ApplyCleanup(plan);
        string css = book.GetCssResources().Single().GetText();
        css.IndexOf(".b", StringComparison.Ordinal).Should().BeLessThan(css.LastIndexOf(".a", StringComparison.Ordinal));
        new CssInfo(css).Rules.Count(r => r.SelectorText == ".a").Should().Be(1);
    }

    [Fact]
    public void A_merge_is_risky_when_no_place_keeps_the_styling_and_applied_only_when_accepted()
    {
        // <p class="a b">: color comes from ".b" (after the first ".a"), background from the last ".a" (after ".b").
        // At the first ".a" the background changes, at the last ".a" the color does.
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AppendCss(tree, ".a { color: red; }\n.b { color: green; background: white; }\n.a { background: blue; }");
            ReplaceInChapter1(tree, "<p>Hello, world.</p>", "<p class=\"a b\">Hello, world.</p>");
        });
        CleanupAnalysis analysis = Analyse(book);
        HashSet<CleanupStep> steps = new() { CleanupStep.MergeSameSelectors };

        CleanupItem merge = analysis.Plan(steps, NoExclusions).GetStep(CleanupStep.MergeSameSelectors)!.Items.Single();

        merge.IsRisky.Should().BeTrue();
        merge.IsApplied.Should().BeFalse();
        merge.Consequences.Should().ContainSingle();
        merge.Consequences[0].Text.Should().Contain("chapter1.xhtml").And.Contain("background").And.Contain("blue").And.Contain("white");
        merge.Consequences[0].BookPath.Should().EndWith("chapter1.xhtml");

        CleanupPlan accepted = analysis.Plan(steps, NoExclusions, new HashSet<string> { merge.Key });
        accepted.GetStep(CleanupStep.MergeSameSelectors)!.Items.Single().IsApplied.Should().BeTrue();
        accepted.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void A_merge_is_safe_when_no_element_of_the_book_changes()
    {
        // The same rules, but no element has both classes.
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AppendCss(tree, ".a { color: red; }\n.b { color: green; background: white; }\n.a { background: blue; }");
            ReplaceInChapter1(tree, "<p>Hello, world.</p>", "<p class=\"a\">Hello,</p><p class=\"b\">world.</p>");
        });

        CleanupItem merge = Step(Plan(book, CleanupStep.MergeSameSelectors), CleanupStep.MergeSameSelectors).Items.Single();

        merge.IsRisky.Should().BeFalse();
        merge.IsApplied.Should().BeTrue();
    }

    [Theory]
    [InlineData("<p><span class=\"b c\">y</span></p>", false)]
    [InlineData("<p><span class=\"b c\">y</span><span class=\"a b\">z</span></p>", true)]
    public void A_properties_merge_is_risky_only_when_both_places_change_an_element(string body, bool risky)
    {
        // ".a, .c" at the place of ".a" moves ".c" before ".b" (breaks "b c"); at the place of ".c" it moves ".a"
        // after ".b" (breaks "a b").
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AppendCss(tree, ".a { color: red; }\n.b { color: blue; }\n.c { color: red; }");
            ReplaceInChapter1(tree, "<p>Hello, world.</p>", body);
        });

        CleanupItem merge = Step(Plan(book, CleanupStep.MergeSameProperties), CleanupStep.MergeSameProperties).Items.Single();

        merge.IsRisky.Should().Be(risky);
        if (risky)
        {
            merge.Consequences.Single().Text.Should().Contain("span").And.Contain("red").And.Contain("blue");
        }
    }

    [Fact]
    public void A_change_caused_by_a_rule_inside_media_names_the_condition()
    {
        // At the first "p" the margin of the last "p" moves before @media; at the last "p" the color of the first
        // moves after it — on wide screens something changes either way.
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AppendCss(tree, "p { margin: 0; color: red; }\n@media (min-width: 600px) { p { margin: 1em; color: blue; } }\np { margin: 2em; }");
        });

        CleanupItem merge = Step(Plan(book, CleanupStep.MergeSameSelectors), CleanupStep.MergeSameSelectors).Items.Single();

        merge.IsRisky.Should().BeTrue();
        merge.Consequences.Single().Text.Should().Contain("min-width: 600px");
    }

    [Fact]
    public void Merging_a_vendor_prefixed_selector_into_a_list_is_risky()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AppendCss(tree, ".note { display: none; }\np::-moz-selection { display: none; }");
            ReplaceInChapter1(tree, "<p>Hello, world.</p>", "<p class=\"note\">Hello, world.</p>");
        });

        CleanupItem merge = Step(Plan(book, CleanupStep.MergeSameProperties), CleanupStep.MergeSameProperties).Items.Single();

        merge.IsRisky.Should().BeTrue();
        merge.Consequences.Should().Contain(c => c.Text.Contains("p::-moz-selection", StringComparison.Ordinal)
            && c.Text.Contains(".note", StringComparison.Ordinal));
    }

    [Fact]
    public void An_excluded_risky_merge_stays_excluded_even_when_accepted()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AppendCss(tree, ".a { color: red; }\n.b { color: green; background: white; }\n.a { background: blue; }");
            ReplaceInChapter1(tree, "<p>Hello, world.</p>", "<p class=\"a b\">Hello, world.</p>");
        });
        CleanupAnalysis analysis = Analyse(book);
        HashSet<CleanupStep> steps = new() { CleanupStep.MergeSameSelectors };
        string key = analysis.Plan(steps, NoExclusions).GetStep(CleanupStep.MergeSameSelectors)!.Items.Single().Key;

        CleanupPlan plan = analysis.Plan(steps, new HashSet<string> { key }, new HashSet<string> { key });

        plan.HasChanges.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    //  Selectors and references that must not be removed
    // -----------------------------------------------------------------

    [Fact]
    public void UnusedSelectors_keeps_state_pseudo_classes_and_pseudo_elements_of_existing_elements()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
            AppendCss(tree, "p:hover { color: red }\np::before { content: '*' }\np::first-letter { font-size: 2em }\n::selection { color: red }\nblockquote:hover { color: red }"));

        Step(Plan(book, CleanupStep.UnusedSelectors), CleanupStep.UnusedSelectors).Items
            .Select(i => i.Text).Should().Equal("blockquote:hover");
    }

    [Theory]
    [InlineData("<img src=\"../images/cover.png\" srcset=\"../images/ref.png 2x\" alt=\"\"/>")]
    [InlineData("<video src=\"v.mp4\" poster=\"../images/ref.png\"></video>")]
    [InlineData("<object data=\"../images/ref.png\" type=\"image/png\"></object>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><image xlink:href=\"../images/ref.png\"/></svg>")]
    [InlineData("<a href=\"../images/ref.png\">full size</a>")]
    public void UnusedMedia_keeps_media_referenced_from_any_attribute(string html)
    {
        using TempDir temp = new();
        using Book book = LoadMedia(temp, tree =>
        {
            AddOrphanImage(tree, "ref.png");
            AppendToChapter1Body(tree, html);
        });

        Step(Plan(book, CleanupStep.UnusedMedia), CleanupStep.UnusedMedia).Items
            .Should().NotContain(i => i.BookPath.EndsWith("ref.png", StringComparison.Ordinal));
    }

    [Fact]
    public void UnusedMedia_keeps_an_image_used_only_by_an_svg_file()
    {
        using TempDir temp = new();
        using Book book = LoadMedia(temp, tree =>
        {
            AddOrphanImage(tree, "ref.png");
            File.WriteAllText(
                Path.Combine(tree, "EPUB", "images", "frame.svg"),
                "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><image xlink:href=\"ref.png\"/></svg>");
            AddManifestItem(tree, "frame", "images/frame.svg", "image/svg+xml");
            AppendToChapter1Body(tree, "<img src=\"../images/frame.svg\" alt=\"\"/>");
        });

        Step(Plan(book, CleanupStep.UnusedMedia), CleanupStep.UnusedMedia).Items
            .Should().NotContain(i => i.BookPath.EndsWith("ref.png", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------
    //  @import
    // -----------------------------------------------------------------

    [Fact]
    public void A_stylesheet_imported_by_a_linked_one_is_referenced_and_its_selectors_are_checked()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AddOrphanStylesheet(tree, "imported.css", "h1 { color: navy }\n.nowhere { color: red }");
            string cssPath = Path.Combine(tree, "EPUB", "styles", "style.css");
            File.WriteAllText(cssPath, "@import url(\"imported.css\");\n" + File.ReadAllText(cssPath));
        });

        CleanupPlan plan = Plan(book, CleanupStep.UnreferencedStylesheets, CleanupStep.UnusedSelectors);

        Step(plan, CleanupStep.UnreferencedStylesheets).Items.Should().BeEmpty();
        Step(plan, CleanupStep.UnusedSelectors).Items.Select(i => i.Text).Should().Equal(".nowhere");
    }

    [Fact]
    public void A_stylesheet_imported_by_a_style_block_is_referenced()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AddOrphanStylesheet(tree, "imported.css", "h1 { color: navy }");
            ReplaceInChapter1(tree, "</head>", "<style>@import \"../styles/imported.css\";</style>\n</head>");
        });

        CleanupPlan plan = Plan(book, CleanupStep.UnreferencedStylesheets, CleanupStep.UnusedSelectors);

        Step(plan, CleanupStep.UnreferencedStylesheets).Items.Should().BeEmpty();
        Step(plan, CleanupStep.UnusedSelectors).Items.Should().NotContain(i => i.Text == "h1");
    }

    [Fact]
    public void A_stylesheet_imported_only_by_an_unreferenced_one_is_unreferenced_too()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AddOrphanStylesheet(tree, "orphan.css", "@import \"chained.css\";");
            AddOrphanStylesheet(tree, "chained.css", "h1 { color: navy }");
        });

        Step(Plan(book, CleanupStep.UnreferencedStylesheets), CleanupStep.UnreferencedStylesheets).Items
            .Select(i => Path.GetFileName(i.BookPath)).Should().BeEquivalentTo("orphan.css", "chained.css");
    }

    // -----------------------------------------------------------------
    //  Interactions between the steps
    // -----------------------------------------------------------------

    [Fact]
    public void Selectors_of_a_removed_stylesheet_are_not_listed_as_unused_selectors()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree => AddOrphanStylesheet(tree, "orphan.css", ".lonely { color: pink }"));

        CleanupPlan selectorsOnly = Plan(book, CleanupStep.UnusedSelectors);
        CleanupPlan both = Plan(book, CleanupStep.UnreferencedStylesheets, CleanupStep.UnusedSelectors);

        Step(selectorsOnly, CleanupStep.UnusedSelectors).Items.Should().Contain(i => i.Text == ".lonely");
        Step(both, CleanupStep.UnusedSelectors).Items.Should().NotContain(i => i.Text == ".lonely");
    }

    [Fact]
    public void Media_used_only_by_a_removed_selector_becomes_unused()
    {
        using TempDir temp = new();
        using Book book = LoadMedia(temp, tree =>
        {
            AddOrphanImage(tree, "bg.png");
            AppendCss(tree, ".ghost { background: url(../images/bg.png); }");
        });
        CleanupAnalysis analysis = Analyse(book);
        HashSet<CleanupStep> both = new() { CleanupStep.UnusedSelectors, CleanupStep.UnusedMedia };

        CleanupPlan mediaOnly = analysis.Plan(new HashSet<CleanupStep> { CleanupStep.UnusedMedia }, NoExclusions);
        CleanupPlan withSelectors = analysis.Plan(both, NoExclusions);
        string ghostKey = withSelectors.GetStep(CleanupStep.UnusedSelectors)!.Items.Single(i => i.Text == ".ghost").Key;
        CleanupPlan ghostKept = analysis.Plan(both, new HashSet<string> { ghostKey });

        Step(mediaOnly, CleanupStep.UnusedMedia).Items.Should().BeEmpty();
        Step(withSelectors, CleanupStep.UnusedMedia).Items.Should().ContainSingle(i => i.BookPath.EndsWith("bg.png", StringComparison.Ordinal));
        Step(ghostKept, CleanupStep.UnusedMedia).Items.Should().BeEmpty();
    }

    [Fact]
    public void Removing_an_unused_selector_can_create_a_new_merge_group()
    {
        // ".ghost, h1" becomes "h1" — the same selector as the corpus "h1" rule.
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree => AppendCss(tree, ".ghost, h1 { color: red }"));

        CleanupPlan mergeOnly = Plan(book, CleanupStep.MergeSameSelectors);
        CleanupPlan both = Plan(book, CleanupStep.UnusedSelectors, CleanupStep.MergeSameSelectors);

        Step(mergeOnly, CleanupStep.MergeSameSelectors).Items.Should().BeEmpty();
        Step(both, CleanupStep.MergeSameSelectors).Items.Should().ContainSingle(i => i.Text.Contains("h1", StringComparison.Ordinal));

        book.ApplyCleanup(both);
        CssInfo info = new(book.GetCssResources().Single().GetText());
        info.Rules.Count(r => r.SelectorText == "h1").Should().Be(1);
        info.Selectors.Should().NotContain(s => s.Text == ".ghost");
    }

    [Fact]
    public void A_merge_item_found_after_earlier_changes_points_to_the_rule_in_the_unchanged_file()
    {
        using TempDir temp = new();
        using Book book = LoadMinimal(temp, tree =>
        {
            AppendCss(tree, ".ghost { color: red }\n.p1 { color: teal }\n.p2 { color: teal }");
            AppendToChapter1Body(tree, "<p class=\"p1\">a</p><p class=\"p2\">b</p>");
        });
        string original = book.GetCssResources().Single().GetText();

        CleanupPlan plan = Plan(book, CleanupStep.UnusedSelectors, CleanupStep.MergeSameProperties);

        CleanupItem merge = Step(plan, CleanupStep.MergeSameProperties).Items.Single();
        merge.Offset.Should().Be(original.IndexOf(".p1", StringComparison.Ordinal));
    }

    private const string NestedChain = "<div class=\"w\">\n  <div class=\"w\">\n    <p>Hello, world.</p>\n  </div>\n</div>";

    private static Book LoadNested(TempDir temp, string css, string chain = NestedChain) => LoadMinimal(temp, tree =>
    {
        if (css.Length > 0)
        {
            AppendCss(tree, css);
        }

        ReplaceInChapter1(tree, "  <p>Hello, world.</p>", chain);
    });

    private static string Chapter1(Book book) =>
        book.GetHtmlResources().Single(h => h.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal)).GetText();

    [Fact]
    public void NestedDivs_lists_the_safe_chains_of_a_file_as_one_item_and_apply_collapses_them()
    {
        using TempDir temp = new();
        using Book book = LoadNested(temp, ".w { color: red; margin: 0; }");

        CleanupPlan plan = Plan(book, CleanupStep.NestedDivs);

        CleanupItem item = Step(plan, CleanupStep.NestedDivs).Items.Should().ContainSingle().Subject;
        item.IsRisky.Should().BeFalse();
        item.IsApplied.Should().BeTrue();
        item.RuleCount.Should().Be(1);
        item.BookPath.Should().EndWith("chapter1.xhtml");
        item.Offset.Should().Be(Chapter1(book).IndexOf("<div", StringComparison.Ordinal));
        Step(plan, CleanupStep.NestedDivs).AppliedRuleCount.Should().Be(1);

        book.ApplyCleanup(plan).Should().BeTrue();
        Chapter1(book).Should().Contain("<div class=\"w\">\n  <p>Hello, world.</p>\n</div>");
    }

    [Fact]
    public void NestedDivs_a_chain_with_a_comment_is_risky_and_applied_only_when_accepted()
    {
        using TempDir temp = new();
        using Book book = LoadNested(temp, string.Empty, "<div class=\"w\">\n  <div class=\"w\">\n    <p>Hello, world.</p>\n  </div>\n  <!-- note -->\n</div>");
        CleanupAnalysis analysis = Analyse(book);
        HashSet<CleanupStep> steps = new() { CleanupStep.NestedDivs };

        CleanupItem chain = analysis.Plan(steps, NoExclusions).GetStep(CleanupStep.NestedDivs)!.Items.Single();

        chain.IsRisky.Should().BeTrue();
        chain.IsApplied.Should().BeFalse();
        chain.Consequences.Should().ContainSingle().Which.Text.Should().Contain("<!-- note -->");

        CleanupPlan accepted = analysis.Plan(steps, NoExclusions, new HashSet<string> { chain.Key });
        book.ApplyCleanup(accepted).Should().BeTrue();
        Chapter1(book).Should().Contain("<div class=\"w\">\n  <p>Hello, world.</p>\n<!-- note -->\n</div>");
    }

    [Theory]
    [InlineData(".w { margin-left: 1em; }", "margin-left")]
    [InlineData(".w { padding: 2px 0; }", "padding")]
    [InlineData(".w { font-size: 1.2em; }", "font-size")]
    [InlineData(".w .w { color: red; }", "color")]
    [InlineData(".w .w p { color: blue; }", "blue")]
    [InlineData(".w > .w { border: 1px solid; }", "border")]
    public void NestedDivs_a_chain_whose_collapse_changes_the_styling_is_risky(string css, string expected)
    {
        using TempDir temp = new();
        using Book book = LoadNested(temp, css);

        CleanupItem chain = Step(Plan(book, CleanupStep.NestedDivs), CleanupStep.NestedDivs).Items.Single();

        chain.IsRisky.Should().BeTrue();
        chain.IsApplied.Should().BeFalse();
        chain.Consequences.Should().Contain(c => c.Text.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(".w { color: red; font-family: serif; }")]
    [InlineData(".w { margin: 0; padding: 0 0; border: none; background: transparent; display: block; }")]
    [InlineData(".w p { color: blue; }")]
    [InlineData(".w { text-indent: 1.5em; border-top: currentColor none 0; border-bottom: currentColor none 0; }")]
    [InlineData(".w { border-left: 0 solid red; outline: hidden; letter-spacing: 0.1em; line-height: 120%; }")]
    public void NestedDivs_a_chain_whose_collapse_keeps_the_styling_is_safe(string css)
    {
        using TempDir temp = new();
        using Book book = LoadNested(temp, css);

        CleanupItem item = Step(Plan(book, CleanupStep.NestedDivs), CleanupStep.NestedDivs).Items.Single();

        item.IsRisky.Should().BeFalse();
    }

    [Fact]
    public void NestedDivs_an_excluded_file_item_is_not_applied()
    {
        using TempDir temp = new();
        using Book book = LoadNested(temp, string.Empty);
        CleanupAnalysis analysis = Analyse(book);
        HashSet<CleanupStep> steps = new() { CleanupStep.NestedDivs };
        string key = analysis.Plan(steps, NoExclusions).GetStep(CleanupStep.NestedDivs)!.Items.Single().Key;

        CleanupPlan plan = analysis.Plan(steps, new HashSet<string> { key });

        plan.HasChanges.Should().BeFalse();
        plan.GetStep(CleanupStep.NestedDivs)!.Items.Single().IsApplied.Should().BeFalse();
    }
}
