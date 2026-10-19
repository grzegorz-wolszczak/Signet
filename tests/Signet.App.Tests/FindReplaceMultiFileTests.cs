using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for the multi-file mode of <see cref="FindReplaceViewModel"/>: <see cref="LookWhere"/>
/// scopes, Count/Replace All aggregation, the well-formed guard, the Find Next "jump".
/// </summary>
public sealed class FindReplaceMultiFileTests : IDisposable
{
    private readonly Disposables _disposables = new();

    private readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), $"signet-frm-{Guid.NewGuid():N}.xml");

    private readonly Book _book = BookCreator.CreateNewBook("2.0");
    private readonly TabManagerModel _tabModel = new();

    public void Dispose()
    {
        _disposables.Dispose();
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
        (SettingsStore settings, SpellChecker spellChecker) = _disposables.Add(SpellCheckTestFactory.New());
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

    private FindReplaceViewModel NewPanel(
        FakeMultiFileSearchHost host, Func<CodeTabViewModel?> activeTab, out SettingsStore settings)
    {
        settings = new SettingsStore(_settingsPath);
        var vm = new FindReplaceViewModel(settings, new StatusBarService(), activeTab, host);
        vm.AttachToActiveTab(activeTab());
        return vm;
    }

    [Fact]
    public void CountAll_AggregatesAcrossResolvedFiles()
    {
        // Arrange
        HtmlResource a = Html("<p>cat cat</p>");
        HtmlResource b = Html("<p>cat</p>");
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a, b },
        };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);

        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "cat";

        // Act & Assert
        panel.Count().Should().Be(3);

        // Assert
        panel.HasReport.Should().BeFalse();
        panel.Message.Should().Be(Strings.Format("FindReplace_MatchesInFiles", 3, 2));
        host.FlushCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountAll_ShowsTheMatchesInTheFindUsagesWindow()
    {
        // Arrange
        HtmlResource a = Html("<p>cat cat</p>\n<p>dog</p>");
        HtmlResource b = Html("<p>cat</p>");
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a, b },
        };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "cat";

        // Act
        panel.Count();

        // Assert
        host.ShownUsages.Should().ContainSingle();
        IReadOnlyList<ClassUsage> usages = host.ShownUsages[0].Usages;
        usages.Select(u => (u.BookPath, u.Offset, u.Length, u.Line, u.Column, u.Kind)).Should().Equal(
            (a.BookPath, 3, 3, 1, 4, ClassUsageKind.Text),
            (a.BookPath, 7, 3, 1, 8, ClassUsageKind.Text),
            (b.BookPath, 3, 3, 1, 4, ClassUsageKind.Text));
        usages[0].Context.Should().Be("<p>cat cat</p>");
        host.ShownUsages[0].Header().Should().Be(Strings.Format(
            "FindUsages_TextHeader",
            "cat",
            Strings.Get("FindReplaceView_ModeNormal"),
            Strings.Get("FindReplace_LookInAllHtml"),
            string.Empty));
    }

    [Fact]
    public void CountAll_WithoutMatches_DoesNotOpenTheWindow()
    {
        // Arrange
        HtmlResource a = Html("<p>dog</p>");
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a },
        };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "cat";

        // Act & Assert
        panel.Count().Should().Be(0);

        // Assert
        host.ShownUsages.Should().BeEmpty();
        panel.Message.Should().Be(Strings.Get("FindReplace_NotFoundInScope"));
    }

    [Fact]
    public void CountAll_InASingleFile_ShowsTheWindowAlsoForOneFile()
    {
        // Arrange
        HtmlResource a = Html("<p>cat</p>");
        CodeTabViewModel tab = OpenTab(a);
        var host = new FakeMultiFileSearchHost { BookLoaded = true };
        host.OpenTabs.Add(tab);
        FindReplaceViewModel panel = NewPanel(host, () => tab, out _);
        panel.LookWhereIndex = (int)LookWhere.CurrentFile;
        panel.FindText = "cat";

        // Act & Assert
        panel.Count().Should().Be(1);

        // Assert
        host.ShownUsages.Should().ContainSingle();
        host.ShownUsages[0].Usages.Should().ContainSingle().Which.BookPath.Should().Be(a.BookPath);
        host.ShownUsages[0].Header().Should().Contain(Strings.Get("FindReplace_LookInCurrentFile"));
    }

    [Fact]
    public void CountAll_HeaderListsTheRegexOptionsThatAreOnWithoutTheirSyntax()
    {
        // Arrange
        HtmlResource a = Html("<p>cat</p>");
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a },
        };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.ModeIndex = (int)SearchMode.Regex;
        panel.FindText = "c.t";
        panel.RegexDotAll = true;
        panel.RegexAutoTokenise = true;

        // Act
        panel.Count();

        string options = Strings.Get("FindReplaceView_DotMatchesAll").Replace(" (?s)", string.Empty, StringComparison.Ordinal)
            + " | " + Strings.Get("FindReplaceView_AutoTokenise");

        // Assert
        host.ShownUsages[0].Header().Should().Be(Strings.Format(
            "FindUsages_TextHeader",
            "c.t",
            Strings.Get("FindReplaceView_ModeRegex"),
            Strings.Get("FindReplace_LookInAllHtml"),
            Strings.Format("FindUsages_TextHeaderRegexOptions", options)));
    }

    [Fact]
    public void CountAll_RerunSearchesTheCurrentTextAgain()
    {
        // Arrange
        HtmlResource a = Html("<p>cat</p>");
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a },
        };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "cat";

        // Act
        panel.Count();
        a.SetText("<p>cat cat cat</p>");

        IReadOnlyList<ClassUsage> again = host.ShownUsages[0].Rerun();

        // Assert
        again.Should().HaveCount(3);
    }

    [Fact]
    public void SavedCountAll_DoesNotOpenTheWindow()
    {
        // Arrange
        HtmlResource a = Html("<p>cat</p>");
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a },
        };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        SearchEntry entry = panel.CurrentAsEntry("cats") with { Find = "cat" };

        // Act & Assert
        panel.CountForEntry(entry).Should().Be(1);

        // Assert
        host.ShownUsages.Should().BeEmpty();
    }

    private FindReplaceViewModel PanelWithCountReport()  // a report as Replace All leaves it
    {
        HtmlResource a = Html("<p>cat cat</p>");
        HtmlResource b = Html("<p>cat</p>");
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a, b },
        };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "cat";
        panel.ReplaceText = "cat";
        panel.ReplaceAll();
        panel.HasReport.Should().BeTrue();
        return panel;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restart_StartsTheNextFindAtTheBeginningOfTheScope(bool restart)
    {
        // Arrange
        HtmlResource a = Html("<p>cat</p>");
        HtmlResource b = Html("<p>cat</p>");
        CodeTabViewModel tabB = OpenTab(b);
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a, b },
        };
        FindReplaceViewModel panel = NewPanel(host, () => tabB, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "cat";
        panel.OptionWrap = false;

        // The caret at the start of file b: without "Restart" the match in b is next, with it the one in a.
        tabB.UpdateSelection(0, 0);
        tabB.UpdateCaret(1, 1, 0, -1);

        // Act
        if (restart)
        {
            panel.RestartCommand.Execute(null);
            panel.Message.Should().Be(Strings.Get("FindReplace_SearchWillRestart"));
        }

        // Act & Assert
        panel.FindNext().Should().BeTrue();

        // Assert
        host.Jumps[^1].BookPath.Should().Be(restart ? a.BookPath : b.BookPath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Wrap_decides_whether_the_search_goes_on_past_the_end_of_the_scope(bool wrap)
    {
        // Arrange
        HtmlResource a = Html("<p>cat</p>");
        HtmlResource b = Html("<p>dog</p>");
        CodeTabViewModel tabB = OpenTab(b);
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a, b },
        };
        FindReplaceViewModel panel = NewPanel(host, () => tabB, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "cat";
        panel.OptionWrap = wrap;

        // Act & Assert
        panel.FindNext().Should().Be(wrap);

        // Assert
        panel.Message.Should().Be(wrap ? Strings.Get("FindReplace_WrappedScope") : Strings.Get("FindReplace_NotFoundEnd"));
    }

    [Fact]
    public void CloseReportCommand_ClearsTheReport()
    {
        // Arrange
        FindReplaceViewModel panel = PanelWithCountReport();

        // Act
        panel.CloseReportCommand.Execute(null);

        // Assert
        panel.HasReport.Should().BeFalse();
        panel.ReportSummary.Should().BeEmpty();
        panel.ReportRows.Should().BeEmpty();
    }

    [Fact]
    public void ChangingFindText_ClearsTheStaleReport()
    {
        // Arrange
        FindReplaceViewModel panel = PanelWithCountReport();

        // Act
        panel.FindText = "dog";

        // Assert
        panel.HasReport.Should().BeFalse();
        panel.ReportRows.Should().BeEmpty();
    }

    [Fact]
    public void ChangingLookWhere_ClearsTheStaleReport()
    {
        // Arrange
        FindReplaceViewModel panel = PanelWithCountReport();

        // Act
        panel.LookWhereIndex = (int)LookWhere.TabbedHtmlFiles;

        // Assert
        panel.HasReport.Should().BeFalse();
        panel.ReportRows.Should().BeEmpty();
    }

    [Fact]
    public void SettingTheSameFindText_KeepsTheReport()
    {
        // Arrange
        FindReplaceViewModel panel = PanelWithCountReport();

        // Act
        panel.FindText = "cat";

        // Assert
        panel.HasReport.Should().BeTrue();
    }

    [Fact]
    public void ReplaceAll_ClosedFilesViaResource_OpenFilesViaDocument()
    {
        // Arrange
        HtmlResource closed = Html("<p>needle needle</p>");
        HtmlResource opened = Html("<p>needle</p>");
        CodeTabViewModel openedTab = OpenTab(opened);

        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { closed, opened },
        };
        host.OpenTabs.Add(openedTab);

        FindReplaceViewModel panel = NewPanel(host, () => openedTab, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "needle";
        panel.ReplaceText = "pin";

        // Act
        panel.ReplaceAll().Should().Be(3);

        // Assert
        closed.GetText().Should().Be("<p>pin pin</p>");
        openedTab.Document.Text.Should().Be("<p>pin</p>");
        openedTab.CanUndo.Should().BeTrue();
        panel.ReportRows.Where(r => !r.Skipped).Should().HaveCount(2);
    }

    [Fact]
    public void ReplaceAll_SkipsFilesThatAreNotWellFormed()
    {
        // Arrange
        HtmlResource ok = Html("<p>term</p>");
        HtmlResource broken = Html("<p>term <b>oops</p>");

        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { ok, broken },
        };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "term";
        panel.ReplaceText = "word";

        // Act
        panel.ReplaceAll().Should().Be(1);

        // Assert
        ok.GetText().Should().Contain("word");
        broken.GetText().Should().Contain("term");
        panel.ReportRows.Should().Contain(r => r.Skipped);
        panel.ReportSummary.Should().Contain(Strings.Format("FindReplace_FilesSkipped", 1));
    }

    [Fact]
    public void FindNext_MultiFile_JumpsToMatchingResource()
    {
        // Arrange
        HtmlResource a = Html("<p>nothing</p>");
        HtmlResource b = Html("<p>the target here</p>");

        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a, b },
        };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "target";

        // Act & Assert
        panel.FindNext().Should().BeTrue();

        // Assert
        host.Jumps.Should().ContainSingle();
        host.Jumps[0].BookPath.Should().Be(b.BookPath);
        b.GetText().Substring(host.Jumps[0].Start, host.Jumps[0].End - host.Jumps[0].Start)
            .Should().Be("target");
    }

    [Fact]
    public void Replace_after_multi_file_FindNext_replaces_the_found_match()
    {
        // Arrange
        HtmlResource a = Html("<p>nothing</p>");
        HtmlResource b = Html("<p>the target here</p>");
        CodeTabViewModel? active = null;

        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a, b },
        };
        FindReplaceViewModel panel = NewPanel(host, () => active, out _);
        host.OnJump = (path, start, end) =>
        {
            active = OpenTab(path == b.BookPath ? b : a);
            active.SelectMatch(start, end);
        };
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "</p>";
        panel.ReplaceText = string.Empty;

        // Act & Assert
        panel.FindNext().Should().BeTrue();

        // Regression: "No selected match to replace." was reported even though a match was selected.
        // Act & Assert
        panel.Replace().Should().BeTrue();

        // Assert
        active!.Document.Text.Should().Be("<p>nothing");
    }

    [Fact]
    public void EmptyScope_ReportsAndDoesNothing()
    {
        // Arrange
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => Array.Empty<TextResource>(),
        };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.SelectedCssFiles;
        panel.FindText = "x";

        // Act & Assert
        panel.FindNext().Should().BeFalse();
        panel.Count().Should().Be(0);

        // Assert
        panel.Message.Should().Be(Strings.Get("FindReplace_EmptyScope"));
    }

    [Fact]
    public void MarkedText_ForcesCurrentFileEvenWhenScopeIsMultiFile()
    {
        // Arrange
        HtmlResource a = Html("<p>cat cat cat</p>");
        HtmlResource other = Html("<p>cat</p>");
        CodeTabViewModel tab = OpenTab(a);
        tab.UpdateSelection(3, 10);
        tab.ToggleMarkSelection();

        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a, other },
        };
        FindReplaceViewModel panel = NewPanel(host, () => tab, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "cat";
        panel.ReplaceText = "dog";

        // The "marked text" region takes precedence: replace only in the current file, within the region.
        // Act & Assert
        panel.ReplaceAll().Should().Be(2);

        // Assert
        tab.Document.Text.Should().Be("<p>dog dog cat</p>");
        other.GetText().Should().Be("<p>cat</p>");
    }

    [Fact]
    public void LookWhere_IsPersistedAndReloaded()
    {
        // Arrange
        var host = new FakeMultiFileSearchHost { BookLoaded = true };
        FindReplaceViewModel panel = NewPanel(host, () => null, out SettingsStore settings);

        // Act
        panel.LookWhereIndex = (int)LookWhere.OpfFile;

        var reloaded = new FindReplaceViewModel(settings, new StatusBarService(), () => null, host);

        // Assert
        reloaded.LookWhereIndex.Should().Be((int)LookWhere.OpfFile);
        reloaded.IsMultiFileScope.Should().BeTrue();
    }

    [Fact]
    public void ReplaceAll_creates_a_checkpoint_before_replacing()
    {
        // Arrange
        HtmlResource a = Html("<p>needle</p>");
        var host = new FakeMultiFileSearchHost { BookLoaded = true, Resolver = _ => new TextResource[] { a } };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "needle";
        panel.ReplaceText = "pin";

        // Act & Assert
        panel.ReplaceAll().Should().Be(1);

        // Assert
        host.Checkpoints.Should().Equal(Strings.Get("CheckpointOp_ReplaceAll"));
        host.Rewinds.Should().Be(0);
    }

    [Fact]
    public void ReplaceAll_without_matches_rewinds_its_checkpoint()
    {
        // Arrange
        HtmlResource a = Html("<p>hay</p>");
        var host = new FakeMultiFileSearchHost { BookLoaded = true, Resolver = _ => new TextResource[] { a } };
        FindReplaceViewModel panel = NewPanel(host, () => null, out _);
        panel.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
        panel.FindText = "needle";
        panel.ReplaceText = "pin";

        // Act & Assert
        panel.ReplaceAll().Should().Be(0);

        // Assert
        host.Checkpoints.Should().ContainSingle();
        host.Rewinds.Should().Be(1);
    }

    [Fact]
    public void ReplaceAll_in_marked_text_does_not_create_a_checkpoint()
    {
        // Arrange
        HtmlResource a = Html("<p>cat cat cat</p>");
        CodeTabViewModel tab = OpenTab(a);
        tab.UpdateSelection(3, 10);
        tab.ToggleMarkSelection();
        var host = new FakeMultiFileSearchHost { BookLoaded = true, Resolver = _ => new TextResource[] { a } };
        FindReplaceViewModel panel = NewPanel(host, () => tab, out _);
        panel.FindText = "cat";
        panel.ReplaceText = "dog";

        // Act & Assert
        panel.ReplaceAll().Should().Be(2);

        // Assert
        host.Checkpoints.Should().BeEmpty();
    }
}
