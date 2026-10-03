using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for the "Saved Searches" panel (<see cref="SearchEditorViewModel"/>): CRUD, persistence,
/// batch Replace All / Count All, import, "Load Search" into Find &amp; Replace.
/// </summary>
public sealed class SearchEditorTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"signet-searcheditor-{Guid.NewGuid():N}");

    private readonly Book _book = BookCreator.CreateNewBook("2.0");

    public SearchEditorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _book.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort
        }
    }

    private SavedSearchStore Store() => new(Path.Combine(_dir, "searches.json"));

    private FindReplaceViewModel NewFindReplace(FakeMultiFileSearchHost host) => new(
        new SettingsStore(Path.Combine(_dir, $"settings-{Guid.NewGuid():N}.json")),
        new StatusBarService(),
        () => null,
        host);

    private HtmlResource Html(string text)
    {
        HtmlResource html = _book.CreateEmptyHtmlFile();
        html.SetText(text);
        return html;
    }

    [Fact]
    public void AddEntriesAndGroups_ThenSave_PersistsAcrossReload()
    {
        SavedSearchStore store = Store();
        var host = new FakeMultiFileSearchHost();
        var panel = new SearchEditorViewModel(store, NewFindReplace(host));

        panel.AddGroupCommand.Execute(null);
        SearchEntryNodeViewModel group = panel.Nodes.Single();
        group.Name = "Typografia";
        panel.SetSelectedNodes(new[] { group });
        panel.AddEntryCommand.Execute(null);

        // RebuildNodes creates new VM instances, so take fresh ones.
        SearchEntryNodeViewModel entry = panel.Nodes.Single().Children.Single();
        entry.Name = "Cudzysłowy";
        entry.Find = "\"";
        entry.Replace = "„";
        entry.Controls = "NL DN AH";

        panel.SaveCommand.Execute(null);

        // "Restart": a new panel instance on the same file.
        var reloaded = new SearchEditorViewModel(store, NewFindReplace(new FakeMultiFileSearchHost()));
        SearchEntryNodeViewModel reloadedEntry = reloaded.Nodes.Single().Children.Single();
        reloadedEntry.Name.Should().Be("Cudzysłowy");
        reloadedEntry.Find.Should().Be("\"");
        reloadedEntry.Replace.Should().Be("„");
        reloadedEntry.Controls.Should().Be("NL DN AH");
    }

    [Fact]
    public void ReplaceAll_FromListOfThreeSearches_RunsThemSequentially()
    {
        HtmlResource a = Html("<p>alpha beta gamma</p>");
        HtmlResource b = Html("<p>alpha alpha</p>");
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a, b },
        };

        SavedSearchStore store = Store();
        store.Save(new SearchEntry[]
        {
            new(false, "seq/one", "one", "alpha", "ALPHA", "NL DN AH"),
            new(false, "seq/two", "two", "beta", "BETA", "NL DN AH"),
            new(false, "seq/three", "three", "gamma", "GAMMA", "NL DN AH"),
        });

        var panel = new SearchEditorViewModel(store, NewFindReplace(host));
        panel.SetSelectedNodes(new[] { panel.Nodes.Single() }); // group "seq" → all 3 entries

        panel.ReplaceAllCommand.Execute(null);

        a.GetText().Should().Be("<p>ALPHA BETA GAMMA</p>");
        b.GetText().Should().Be("<p>ALPHA ALPHA</p>");
        panel.Message.Should().Contain("5"); // 3 (alpha) + 1 (beta) + 1 (gamma)
    }

    [Fact]
    public void CountsReport_ListsPerEntryCounts()
    {
        HtmlResource a = Html("<p>cat cat dog</p>");
        var host = new FakeMultiFileSearchHost
        {
            BookLoaded = true,
            Resolver = _ => new TextResource[] { a },
        };

        SavedSearchStore store = Store();
        store.Save(new SearchEntry[]
        {
            new(false, "cats", "cats", "cat", "", "NL DN AH"),
            new(false, "dogs", "dogs", "dog", "", "NL DN AH"),
        });

        var panel = new SearchEditorViewModel(store, NewFindReplace(host));
        panel.SetSelectedNodes(panel.Nodes.ToList());

        panel.CountsReportCommand.Execute(null);

        panel.HasReport.Should().BeTrue();
        panel.ReportRows.Should().HaveCount(2);
        panel.ReportRows.Should().Contain(r => r.Contains("cats") && r.EndsWith('2'));
        panel.ReportRows.Should().Contain(r => r.Contains("dogs") && r.EndsWith('1'));

        panel.CloseReportCommand.Execute(null);

        panel.HasReport.Should().BeFalse();
        panel.ReportSummary.Should().BeEmpty();
        panel.ReportRows.Should().BeEmpty();
    }

    [Fact]
    public void ImportFile_Json_AddsEntriesUnderImportedGroup()
    {
        using UiCultureScope culture = new("en");
        SavedSearchStore store = Store();
        var panel = new SearchEditorViewModel(store, NewFindReplace(new FakeMultiFileSearchHost()));

        string importPath = Path.Combine(_dir, "external.json");
        File.WriteAllText(importPath, SavedSearchIo.WriteJson(new SearchEntry[]
        {
            new(false, "Foo", "Foo", "x", "y", "NL DN CF"),
        }));

        panel.ImportFile(importPath);

        SearchEntryNodeViewModel imported = panel.Nodes.Single();
        imported.Name.Should().Be("Imported");
        imported.Children.Single().Name.Should().Be("Foo");
    }

    [Fact]
    public void LoadSearch_PushesEntryIntoFindReplacePanel()
    {
        SavedSearchStore store = Store();
        store.Save(new SearchEntry[]
        {
            new(false, "regexy", "regexy", @"\d+", "N", "RX WR DN AH"),
        });

        var host = new FakeMultiFileSearchHost { BookLoaded = true };
        FindReplaceViewModel findReplace = NewFindReplace(host);
        var panel = new SearchEditorViewModel(store, findReplace);
        panel.SetSelectedNodes(new[] { panel.Nodes.Single() });

        panel.LoadSearchCommand.Execute(null);

        findReplace.FindText.Should().Be(@"\d+");
        findReplace.ReplaceText.Should().Be("N");
        findReplace.ModeIndex.Should().Be(2); // Regex
        findReplace.LookWhereIndex.Should().Be((int)LookWhere.AllHtmlFiles);
        findReplace.OptionWrap.Should().BeTrue();
    }

    [Fact]
    public void MoveRight_ThenSave_KeepsEntryInsideGroup()
    {
        SavedSearchStore store = Store();
        store.Save(new SearchEntry[]
        {
            new(true, "G/", "G", "", "", ""),
            new(false, "loose", "loose", "a", "b", "NL DN CF"),
        });

        var panel = new SearchEditorViewModel(store, NewFindReplace(new FakeMultiFileSearchHost()));
        SearchEntryNodeViewModel loose = panel.Nodes.Single(n => n.Name == "loose");
        panel.SetSelectedNodes(new[] { loose });

        panel.MoveRightCommand.Execute(null);
        panel.SaveCommand.Execute(null);

        store.Load().Select(e => e.FullName).Should().Contain("G/loose");
    }
}
