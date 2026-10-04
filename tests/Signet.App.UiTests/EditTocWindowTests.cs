using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Toc;

namespace Signet.App.UiTests;

/// <summary>Rendering of the "Edit Table Of Contents" window.</summary>
public sealed class EditTocWindowTests
{
    [AvaloniaFact]
    public void The_entries_are_a_flat_table_with_resizable_title_level_and_target_columns()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        string text = html.GetText();
        int start = text.IndexOf("<body", StringComparison.Ordinal);
        int end = text.IndexOf("</body>", StringComparison.Ordinal) + "</body>".Length;
        html.SetText(text[..start] + "<body>\n  <h1>Alpha</h1>\n  <h2>Alpha One</h2>\n  <h1>Beta</h1>\n</body>" + text[end..]);
        new HeadingSelectorModel(book).Apply();
        TocGenerator.GenerateToc(book);

        EditTocWindow window = new() { DataContext = new EditTocViewModel(book) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.Columns.Select(c => c.Header).Should().Equal(
            Strings.Get("EditTocWindow_ColumnTitle"), Strings.Get("EditTocWindow_ColumnLevel"), Strings.Get("EditTocWindow_ColumnTarget"));
        grid.CanUserResizeColumns.Should().BeTrue();
        grid.CanUserSortColumns.Should().BeFalse();
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.Should().Contain("Alpha").And.Contain("Alpha One").And.Contain("Beta");
        window.Close();
    }
}
