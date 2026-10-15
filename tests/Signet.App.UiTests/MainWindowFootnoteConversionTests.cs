using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// The footnote conversion dialog of the main window. It lives here, not in Signet.App.Tests, because it awaits
/// (<c>await Task.Run(...).ConfigureAwait(true)</c>) and then touches the AvaloniaEdit documents of the open tabs,
/// which may only be used from the thread that created them: [AvaloniaFact] runs the test on the UI thread with its
/// synchronization context, as in the application. Without it the continuation lands on a thread-pool thread and the
/// test fails with "Call from invalid thread".
/// </summary>
public sealed class MainWindowFootnoteConversionTests
{
    private sealed class UiCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public UiCultureScope(string name) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }

    // The minimal chapter with a footnote of other origin after "Hello" (marked [7], its note at the end of the body).
    private static (MainWindowViewModel Sut, HtmlResource Chapter) OpenChapterWithForeignFootnote(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = new();
        Book book = new ImportEpub(epub).GetBook();
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        string text = chapter.GetText().Replace(
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"",
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"",
            StringComparison.Ordinal);
        text = text.Insert(text.IndexOf("Hello", StringComparison.Ordinal) + 5, "<a epub:type=\"noteref\" href=\"#n7\" id=\"n7-back\">[7]</a>");
        int body = text.LastIndexOf("</body>", StringComparison.Ordinal);
        chapter.SetText(text.Insert(body, "<aside epub:type=\"footnote\" id=\"n7\"><p><a href=\"#n7-back\">7.</a> Other note.</p></aside>\n"));
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { chapter });
        return (sut, chapter);
    }

    [AvaloniaFact]
    public async Task The_conversion_dialog_converts_behind_a_checkpoint_and_shows_what_is_left()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp);
        bool asked = false;
        FootnoteConversionViewModel dialog = (await sut.PrepareFootnoteConversionAsync(() =>
        {
            asked = true;
            return Task.FromResult(false);
        }))!;
        FootnoteConversionItemViewModel item = dialog.Items.Single();
        item.Item.Warnings.Should().Equal(ConversionWarning.NumberChanges);
        item.IsChecked.Should().BeFalse("a footnote whose number changes needs a look");
        item.IsChecked = true;

        await dialog.ConvertCommand.ExecuteAsync(null);

        asked.Should().BeFalse("the checkpoint was created");
        chapter.GetText().Should().Contain("Hello<a class=\"signet-fn-ref\"").And.Contain("Other note.");
        FootnoteScanner.Scan(((IBookWorkspace)sut).CurrentBook!).Should().ContainSingle().Which.IsSignet.Should().BeTrue();
        sut.ActiveCodeTab!.DocumentText.Should().Be(chapter.GetText());
        sut.StatusMessage.Should().Be(Strings.Format("Status_FootnotesConverted", 1));
        sut.CheckpointHistory.UndoMessage.Should().Be(Strings.Format("Checkpoint_Before", Strings.Get("CheckpointOp_ConvertFootnotes")));
        dialog.HasItems.Should().BeFalse();
        dialog.HasProblems.Should().BeFalse();
    }
}
