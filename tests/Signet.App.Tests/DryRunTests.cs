using System;
using System.IO;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.App.Resources;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Spellcheck;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for "Dry Run Replace All" and "Filter Replacements": <see cref="DryRunReplaceViewModel"/>,
/// <see cref="ReplacementChooserViewModel"/> and the hooks in <see cref="FindReplaceViewModel"/>.
/// </summary>
public sealed class DryRunTests : IDisposable
{
    private readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), $"signet-dryrun-{Guid.NewGuid():N}.json");

    private readonly Book _book = BookCreator.CreateNewBook("2.0");
    private readonly TabManagerModel _tabModel = new();

    public void Dispose()
    {
        _book.Dispose();
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }
    }

    private HtmlResource Html(string text)
    {
        HtmlResource html = _book.CreateEmptyHtmlFile();
        html.SetText(text);
        return html;
    }

    private CodeTabViewModel OpenTab(HtmlResource html)
    {
        OpenTab tab = _tabModel.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);
        vm.Document.Text = html.GetText();
        vm.UpdateSelection(0, 0);
        vm.UpdateCaret(1, 1, 0, -1);
        vm.SearchResultRequested += (s, e, _) =>
        {
            vm.UpdateSelection(s, e);
            vm.UpdateCaret(1, 1, e, -1);
        };
        return vm;
    }

    private FindReplaceViewModel NewPanel(FakeMultiFileSearchHost host, Func<CodeTabViewModel?> activeTab)
    {
        var vm = new FindReplaceViewModel(
            new SettingsStore(_settingsPath), new StatusBarService(), activeTab, host);
        vm.AttachToActiveTab(activeTab());
        return vm;
    }

    private ReplacePreviewRequest BuildRequest(string find, params TextResource[] files)
    {
        var host = new FakeMultiFileSearchHost { BookLoaded = true, Resolver = _ => files };
        FindReplaceViewModel panel = NewPanel(host, () => null);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = find;

        ReplacePreviewRequest? request = panel.TryBuildReplacePreviewRequest();
        request.Should().NotBeNull();
        return request!;
    }

    [Fact]
    public void DryRun_BuildsRows_WithMatchCountAndTrimmedContext()
    {
        HtmlResource a = Html("<p>the quick brown fox</p>");
        HtmlResource b = Html("<p>quick</p>");
        ReplacePreviewRequest request = BuildRequest("quick", a, b);

        var dryRun = new DryRunReplaceViewModel(request.Resources, request.SearchRegex, "slow");

        dryRun.MatchCount.Should().Be(2);
        dryRun.Rows.Should().HaveCount(2);
        dryRun.Rows[0].MatchText.Should().Be("quick");
        dryRun.Rows[0].AfterSnippet.Should().Contain("slow");
        dryRun.Summary.Should().Contain("2");
    }

    [Fact]
    public void DryRun_Filter_LimitsVisibleRows()
    {
        HtmlResource a = Html("<p>xx yy match</p>");
        HtmlResource b = Html("<p>zz match</p>");
        ReplacePreviewRequest request = BuildRequest("match", a, b);
        var dryRun = new DryRunReplaceViewModel(request.Resources, request.SearchRegex, "X");

        dryRun.FilterText = "yy";

        dryRun.Rows.Should().ContainSingle();
        dryRun.Rows[0].BeforeSnippet.Should().Contain("yy");
        dryRun.MatchCount.Should().Be(2);
    }

    [Fact]
    public void DryRun_ChangingContextAmount_Rebuilds()
    {
        HtmlResource a = Html("<p>needle aa bb cc dd ee ff gg hh</p>");
        ReplacePreviewRequest request = BuildRequest("needle", a);
        var dryRun = new DryRunReplaceViewModel(request.Resources, request.SearchRegex, "X") { ContextAmount = 5 };

        int shortPost = dryRun.Rows[0].PostContext.Length;
        dryRun.ContextAmount = 40;

        dryRun.Rows[0].PostContext.Length.Should().BeGreaterThan(shortPost);
    }

    [Fact]
    public void TryBuildReplacePreviewRequest_NoFindText_ReturnsNullWithMessage()
    {
        var host = new FakeMultiFileSearchHost { BookLoaded = true };
        FindReplaceViewModel panel = NewPanel(host, () => null);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;

        panel.TryBuildReplacePreviewRequest().Should().BeNull();
        panel.Message.Should().Be(Strings.Get("FindReplace_EnterSearchText"));
    }

    [Fact]
    public void ChooseReplacements_DeselectingRow_SkipsItOnApply()
    {
        HtmlResource a = Html("<p>cat cat</p>");
        HtmlResource b = Html("<p>cat</p>");
        var host = new FakeMultiFileSearchHost { BookLoaded = true, Resolver = _ => new TextResource[] { a, b } };
        FindReplaceViewModel panel = NewPanel(host, () => null);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "cat";
        panel.ReplaceText = "dog";

        ReplacePreviewRequest request = panel.TryBuildReplacePreviewRequest()!;
        var chooser = new ReplacementChooserViewModel(
            request.Resources, request.SearchRegex, request.ReplaceText, panel.ApplyChosenReplacements);

        chooser.Rows.Should().HaveCount(3);
        chooser.Rows[0].IsChecked = false; // the first "cat" in file a

        chooser.ApplyCommand.Execute(null);

        chooser.ReplacementCount.Should().Be(2);
        a.GetText().Should().Be("<p>cat dog</p>");
        b.GetText().Should().Be("<p>dog</p>");
    }

    [Fact]
    public void ChooseReplacements_Apply_OpenTab_KeepsSingleUndoStep()
    {
        HtmlResource closed = Html("<p>needle needle</p>");
        HtmlResource opened = Html("<p>needle</p>");
        CodeTabViewModel openedTab = OpenTab(opened);

        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { closed, opened },
        };
        host.OpenTabs.Add(openedTab);

        FindReplaceViewModel panel = NewPanel(host, () => openedTab);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "needle";
        panel.ReplaceText = "pin";

        ReplacePreviewRequest request = panel.TryBuildReplacePreviewRequest()!;
        var chooser = new ReplacementChooserViewModel(
            request.Resources, request.SearchRegex, request.ReplaceText, panel.ApplyChosenReplacements);

        bool closeRaised = false;
        chooser.CloseRequested += (_, _) => closeRaised = true;

        chooser.ApplyCommand.Execute(null);

        chooser.ReplacementCount.Should().Be(3);
        closed.GetText().Should().Be("<p>pin pin</p>");
        openedTab.Document.Text.Should().Be("<p>pin</p>");
        openedTab.CanUndo.Should().BeTrue();
        closeRaised.Should().BeTrue();
    }

    [Fact]
    public void ChooseReplacements_SelectAll_TogglesEveryRow()
    {
        HtmlResource a = Html("<p>x x x</p>");
        ReplacePreviewRequest request = BuildRequest("x", a);
        var chooser = new ReplacementChooserViewModel(
            request.Resources, request.SearchRegex, "y",
            (_, _) => 0);

        chooser.SelectedCount.Should().Be(3);

        chooser.SelectAll = false;
        chooser.SelectedCount.Should().Be(0);
        chooser.Rows.Should().OnlyContain(r => !r.IsChecked);

        chooser.Rows[1].IsChecked = true;
        chooser.SelectAll.Should().BeFalse();
        chooser.SelectedCount.Should().Be(1);
    }
}
