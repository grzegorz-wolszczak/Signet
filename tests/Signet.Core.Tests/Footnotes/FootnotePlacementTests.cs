using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.Footnotes;

/// <summary>Tests of <see cref="FootnoteEditor.PlanPlacement"/> and <see cref="FootnoteEditor.ChangePlacement"/> — moving many footnotes between local and global at once.</summary>
public sealed class FootnotePlacementTests
{
    private const string Body = "<p>First sentence.</p>\n<p>Second sentence.</p>\n<p>Third sentence.</p>";

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    private static int After(HtmlResource html, string text) =>
        html.GetText().IndexOf(text, StringComparison.Ordinal) + text.Length;

    // A book of two chapters with two local footnotes each (notes "a1", "a2" in the first, "b1", "b2" in the second).
    private static (Book Book, HtmlResource First, HtmlResource Second) NewBook()
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource first = book.GetHtmlResourcesExcludingNav()[0];
        first.SetText(Xhtml(Body));
        HtmlResource second = book.CreateEmptyHtmlFile();
        second.SetText(Xhtml(Body));
        FootnoteEditor.Add(book, first, After(first, "First"), "a1", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        FootnoteEditor.Add(book, first, After(first, "Second"), "a2", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        FootnoteEditor.Add(book, second, After(second, "First"), "b1", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        FootnoteEditor.Add(book, second, After(second, "Second"), "b2", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        return (book, first, second);
    }

    private static string NoteText(Footnote footnote) => FootnoteEditor.GetContent(footnote)!.Text;

    private static string[] Texts(Book book) => book.GetHtmlResources().Select(h => h.BookPath + "\n" + h.GetText()).ToArray();

    [Fact]
    public void The_plan_lists_each_note_of_Signet_once_with_its_marker()
    {
        // Arrange
        (Book book, HtmlResource first, _) = NewBook();
        using (book)
        {
            first.SetText(first.GetText().Replace("</body>", "<p>Foreign<a href=\"#x\" id=\"r\">[9]</a></p>\n<p id=\"x\"><a href=\"#r\">9</a> foreign note</p>\n</body>", StringComparison.Ordinal));

            // Act
            IReadOnlyList<PlacementItem> plan = FootnoteEditor.PlanPlacement(book);

            // Assert
            plan.Select(i => NoteText(i.Footnote)).Should().Equal("a1", "a2", "b1", "b2");
            plan.Select(i => i.Marker).Should().Equal("[1]", "[2]", "[1]", "[2]");
            plan.Should().OnlyContain(i => i.CanMove && i.Placement == FootnotePlacement.Local);
        }
    }

    [Fact]
    public void Planning_reports_a_file_scanned_of_the_books_total()
    {
        // Arrange
        (Book book, _, _) = NewBook();
        using (book)
        {
            int totalFiles = book.GetHtmlResources().Count;
            List<ScanProgress> reported = new();

            // Act
            FootnoteEditor.PlanPlacement(book, new ScanSyncProgress(reported.Add));

            // Assert
            reported.Select(r => r.Done).Should().Equal(Enumerable.Range(0, totalFiles + 1));
            reported.Should().OnlyContain(r => r.Total == totalFiles);
        }
    }

    [Fact]
    public void The_plan_blocks_a_global_note_with_references_from_several_files()
    {
        // Arrange
        (Book book, HtmlResource first, HtmlResource second) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, first, After(first, "Third"), "shared", FootnotePlacement.Global, out Footnote? added);
            FootnoteEditor.AddReference(book, second, After(second, "Third"), added!, out _).Should().Be(FootnoteError.None);

            // Act
            IReadOnlyList<PlacementItem> plan = FootnoteEditor.PlanPlacement(book);

            // Assert
            PlacementItem shared = plan.Single(i => NoteText(i.Footnote) == "shared");
            shared.Blocker.Should().Be(FootnoteError.SeveralFiles);
            shared.CanMove.Should().BeFalse();
            shared.References.Should().HaveCount(2);
            plan.Where(i => i != shared).Should().OnlyContain(i => i.CanMove);

            // Act
            PlacementOutcome outcome = FootnoteEditor.ChangePlacement(book, new[] { shared });

            // Assert
            outcome.Should().Be(new PlacementOutcome(0, 1));
        }
    }

    [Fact]
    public void Moving_all_the_notes_to_global_and_back_gives_what_moving_them_one_by_one_gives()
    {
        // Arrange
        (Book batch, _, _) = NewBook();
        (Book single, _, _) = NewBook();
        using (batch)
        using (single)
        {
            // Act
            PlacementOutcome toGlobal = FootnoteEditor.ChangePlacement(batch, FootnoteEditor.PlanPlacement(batch));
            while (FootnoteScanner.Scan(single).FirstOrDefault(f => f.Placement == FootnotePlacement.Local) is { } local)
            {
                FootnoteEditor.Move(single, local).Should().BeTrue();
            }

            // Assert
            toGlobal.Should().Be(new PlacementOutcome(4, 0));
            Texts(batch).Should().Equal(Texts(single));
            FootnoteScanner.Scan(batch).Should().OnlyContain(f => f.Placement == FootnotePlacement.Global);
            FootnoteValidator.Validate(batch).Should().BeEmpty();

            // Act
            PlacementOutcome toLocal = FootnoteEditor.ChangePlacement(batch, FootnoteEditor.PlanPlacement(batch));
            while (FootnoteScanner.Scan(single).FirstOrDefault(f => f.Placement == FootnotePlacement.Global) is { } global)
            {
                FootnoteEditor.Move(single, global).Should().BeTrue();
            }

            // Assert
            toLocal.Should().Be(new PlacementOutcome(4, 0));
            Texts(batch).Should().Equal(Texts(single));
            FootnoteEditor.FindGlobalFile(batch).Should().BeNull("the emptied notes file is removed");
            FootnoteScanner.Scan(batch).Select(NoteText).Should().Equal("a1", "a2", "b1", "b2");
            FootnoteValidator.Validate(batch).Should().BeEmpty();
        }
    }

    [Fact]
    public void Only_the_chosen_notes_move()
    {
        // Arrange
        (Book book, HtmlResource first, HtmlResource second) = NewBook();
        using (book)
        {
            IReadOnlyList<PlacementItem> plan = FootnoteEditor.PlanPlacement(book);

            // Act
            PlacementOutcome outcome = FootnoteEditor.ChangePlacement(book, new[] { plan[1], plan[2] });

            // Assert
            outcome.Should().Be(new PlacementOutcome(2, 0));
            FootnoteScanner.Scan(book).Select(f => (NoteText(f), f.Placement)).Should().Equal(
                ("a1", FootnotePlacement.Local), ("a2", FootnotePlacement.Global),
                ("b1", FootnotePlacement.Global), ("b2", FootnotePlacement.Local));
            FootnoteEditor.FindGlobalFile(book)!.GetText().Should().Contain("a2").And.Contain("b1").And.NotContain("a1").And.NotContain("b2");
            first.GetText().Should().Contain("<section class=\"signet-footnotes\"");
            second.GetText().Should().Contain("<section class=\"signet-footnotes\"");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void The_notes_that_become_global_go_to_the_chosen_file()
    {
        // Arrange
        (Book book, HtmlResource first, HtmlResource second) = NewBook();
        using (book)
        {
            HtmlResource notes = book.CreateEmptyHtmlFile();
            notes.SetText(Xhtml("<h1>Endnotes</h1>"));

            // Act
            PlacementOutcome outcome = FootnoteEditor.ChangePlacement(book, FootnoteEditor.PlanPlacement(book), notes);

            // Assert
            outcome.Moved.Should().Be(4);
            FootnoteEditor.FindGlobalFile(book).Should().BeSameAs(notes);
            notes.GetText().Should().Contain("<h1>Endnotes</h1>").And.Contain("signet-endnotes").And.Contain("a1").And.Contain("b2");
            book.GetHtmlResources().Should().NotContain(h => h.Filename == FootnoteEditor.GlobalFileName);
            first.GetText().Should().NotContain("<section class=\"signet-footnotes\"");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void Another_notes_file_takes_the_global_notes_that_were_there_too()
    {
        // Arrange
        (Book book, _, _) = NewBook();
        using (book)
        {
            IReadOnlyList<PlacementItem> plan = FootnoteEditor.PlanPlacement(book);
            FootnoteEditor.ChangePlacement(book, new[] { plan[0] });
            HtmlResource oldNotes = FootnoteEditor.FindGlobalFile(book)!;
            HtmlResource notes = book.CreateEmptyHtmlFile();
            notes.SetText(Xhtml("<h1>Endnotes</h1>"));
            PlacementItem local = FootnoteEditor.PlanPlacement(book).Single(i => NoteText(i.Footnote) == "b1");

            // Act
            PlacementOutcome outcome = FootnoteEditor.ChangePlacement(book, new[] { local }, notes);

            // Assert
            outcome.Moved.Should().Be(1);
            FootnoteEditor.FindGlobalFile(book).Should().BeSameAs(notes);
            FootnoteScanner.Scan(book).Where(f => f.Placement == FootnotePlacement.Global).Select(f => f.NoteFile)
                .Should().OnlyContain(f => ReferenceEquals(f, notes)).And.HaveCount(2);
            book.GetHtmlResources().Should().NotContain(oldNotes, "the old notes file was left empty");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_shared_local_note_moves_with_all_its_references()
    {
        // Arrange
        (Book book, HtmlResource first, _) = NewBook();
        using (book)
        {
            PlacementItem a1 = FootnoteEditor.PlanPlacement(book).First();
            FootnoteEditor.AddReference(book, first, After(first, "Third"), a1.Footnote, out _).Should().Be(FootnoteError.None);
            PlacementItem shared = FootnoteEditor.PlanPlacement(book).Single(i => i.References.Count == 2);
            shared.Marker.Should().Be("[1a], [1b]");

            // Act
            PlacementOutcome outcome = FootnoteEditor.ChangePlacement(book, new[] { shared });

            // Assert
            outcome.Should().Be(new PlacementOutcome(1, 0));
            FootnoteScanner.Scan(book).Where(f => NoteText(f) == "a1").Should().HaveCount(2).And.OnlyContain(f => f.Placement == FootnotePlacement.Global);
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void The_progress_is_reported_step_by_step()
    {
        // Arrange
        (Book book, _, _) = NewBook();
        using (book)
        {
            List<PlacementProgress> reported = new();

            // Act
            FootnoteEditor.ChangePlacement(book, FootnoteEditor.PlanPlacement(book), null, null, new PlacementRun(new SyncProgress(reported.Add)));

            // Assert
            reported.Select(p => p.Stage).Should().StartWith(PlacementStage.Preparing).And.Contain(PlacementStage.Moving).And.EndWith(PlacementStage.Finishing);
            reported[^1].Total.Should().Be(4);
        }
    }

    [Fact]
    public void A_cancelled_token_stops_the_work()
    {
        // Arrange
        (Book book, _, _) = NewBook();
        using (book)
        {
            IReadOnlyList<PlacementItem> plan = FootnoteEditor.PlanPlacement(book);
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();

            // Act & Assert
            Action act = () => FootnoteEditor.ChangePlacement(book, plan, null, null, new PlacementRun(null, cancelled.Token));
            act.Should().Throw<OperationCanceledException>();
        }
    }

    private sealed class SyncProgress : IProgress<PlacementProgress>
    {
        private readonly Action<PlacementProgress> _report;

        public SyncProgress(Action<PlacementProgress> report) => _report = report;

        public void Report(PlacementProgress value) => _report(value);
    }

    private sealed class ScanSyncProgress : IProgress<ScanProgress>
    {
        private readonly Action<ScanProgress> _report;

        public ScanSyncProgress(Action<ScanProgress> report) => _report = report;

        public void Report(ScanProgress value) => _report(value);
    }
}
