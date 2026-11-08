using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AwesomeAssertions;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Signet.App.Docking;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.MainWindowUiTests;

/// <summary>
/// A tab docked at a side of the tab area splits the area in two. The first version of this broke the layout (the area
/// was in the row twice, one half had no room and its editor drew garbage), so every part of the split must be there
/// once, with room, and its editor laid out.
/// </summary>
public sealed class DocumentSplitTests
{
    private sealed class Scenario : IDisposable
    {
        private readonly TempDir _temp = new();
        private readonly Book _book;
        private readonly MainWindow _window;
        private readonly MainWindowViewModel _vm;
        private readonly DockControl _control;

        public Scenario()
        {
            _book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, _temp)).GetBook();
            _window = new MainWindow { DataContext = _vm = new MainWindowViewModel() };
            _window.Show();
            Settle();
            _vm.LoadBook(_book, string.Empty);
            foreach (TextResource resource in _book.GetAllResources().OfType<TextResource>().Take(4))
            {
                _vm.NavigateToBookPathAtOffset(resource.BookPath, 0);
            }

            Settle();
            _control = _window.GetVisualDescendants().OfType<DockControl>().First();
        }

        public MainDockFactory Factory => (MainDockFactory)_vm.DockFactory;

        public IDocumentDock Main => Factory.DocumentDock!;

        public void Settle()
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                _window.CaptureRenderedFrame();
            }
        }

        // Docks the second tab of the area at a side of it, as dropping it on the target of the drag overlay does.
        public void DockSecondTab(IDock target, DockOperation operation)
        {
            IDockable tab = target.VisibleDockables!.OfType<ContentTabViewModel>().ElementAt(1);
            _control.DockManager!.ValidateDockable(tab, target, DragAction.Move, operation, true).Should().BeTrue();
            Settle();
        }

        public IEnumerable<IDockable> Layout() => Walk(_vm.Layout);

        public List<DocumentControl> Areas() => _window.GetVisualDescendants().OfType<DocumentControl>().ToList();

        public List<TextEditor> Editors() => _window.GetVisualDescendants().OfType<TextEditor>().ToList();

        private static IEnumerable<IDockable> Walk(IDockable root)
        {
            yield return root;
            if (root is IDock { VisibleDockables: { } children })
            {
                foreach (IDockable child in children.SelectMany(Walk))
                {
                    yield return child;
                }
            }
        }

        public void Dispose()
        {
            ((IBookWorkspace)_vm).CloseBook();
            _window.Close();
            _temp.Dispose();
        }
    }

    private static void AssertSplit(Scenario s, int areas)
    {
        s.Layout().OfType<IDocumentDock>().Should().HaveCount(areas).And.OnlyHaveUniqueItems("an area is in the layout once");
        s.Areas().Should().HaveCount(areas);
        s.Areas().Should().OnlyContain(a => a.Bounds.Width > 50 && a.Bounds.Height > 50, "every area has room");
        s.Editors().Should().OnlyContain(e => e.TextArea.TextView.Bounds.Width > 50 && e.TextArea.TextView.Bounds.Height > 50);
    }

    [AvaloniaTheory]
    [InlineData(DockOperation.Bottom)]
    [InlineData(DockOperation.Top)]
    [InlineData(DockOperation.Left)]
    [InlineData(DockOperation.Right)]
    public void A_tab_docked_at_a_side_of_the_area_splits_it_in_two(DockOperation side)
    {
        // Arrange
        using Scenario s = new();

        // Act
        s.DockSecondTab(s.Main, side);

        // Assert
        AssertSplit(s, 2);
    }

    [AvaloniaFact]
    public void The_area_split_to_the_right_can_be_split_again_at_the_bottom()
    {
        // Arrange
        using Scenario s = new();
        s.DockSecondTab(s.Main, DockOperation.Right);

        // Act
        s.DockSecondTab(s.Main, DockOperation.Bottom);

        // Assert
        AssertSplit(s, 3);
    }
}
