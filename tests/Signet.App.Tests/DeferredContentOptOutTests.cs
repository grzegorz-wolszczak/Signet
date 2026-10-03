using System.Linq;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;
using Signet.App.Docking;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Dockable view models opt out of Dock's deferred content, so <c>DeferredContentControl</c>
/// materializes their content synchronously. Deferred materialization through the dispatcher queue
/// caused re-entrancy in the views' <c>DataContextChanged</c> handlers when docks were re-parented
/// (e.g. a <c>NullReferenceException</c> in <c>CodeTabView</c>).
/// </summary>
public sealed class DeferredContentOptOutTests
{
    public static TheoryData<Tool> Tools => new()
    {
        new BookBrowserTool(),
        new ClipsTool(),
        new PreviewTool(),
        new TableOfContentsTool(),
        new ValidationResultsTool(),
    };

    [Theory]
    [MemberData(nameof(Tools))]
    public void Tool_panels_opt_out_of_deferred_content(Tool tool)
    {
        tool.Should().BeAssignableTo<IDeferredContentPresentation>();
        ((IDeferredContentPresentation)tool).DeferContentPresentation.Should().BeFalse();
    }

    [Fact]
    public void Document_tabs_opt_out_of_deferred_content()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();

        var model = new TabManagerModel();
        OpenTab tab = model.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);

        vm.Should().BeAssignableTo<IDeferredContentPresentation>();
        ((IDeferredContentPresentation)vm).DeferContentPresentation.Should().BeFalse();
    }
}
