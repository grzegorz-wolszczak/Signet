using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.UiTests;

/// <summary>
/// What the Code Editor costs as the file it holds grows: opening the file (first frame), one edit with its redraw, and scrolling to the end, for a file
/// of about 100 KB, 500 KB and 2 MB. As in the other benchmarks nothing fails over a number; the table is printed (and appended to
/// <c>SIGNET_BENCHMARK_OUT</c>) and <c>BASELINE-SIGNET.md</c> keeps what was measured. The window is headless: the cost is the editor's (parsing, the
/// highlighting, the renderers, layout), not the screen's.
/// </summary>
/// <remarks>Run with: <c>SIGNET_BENCHMARKS=1 dotnet test tests/Signet.App.UiTests -c Release --filter CodeEditorScaleBenchmarks</c>.</remarks>
public sealed class CodeEditorScaleBenchmarks : IDisposable
{
    private const int Edits = 20;
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    [AvaloniaFact]
    public void Measure()
    {
        if (Environment.GetEnvironmentVariable("SIGNET_BENCHMARKS") is null)
        {
            Assert.Skip("Set SIGNET_BENCHMARKS=1 to run the benchmarks.");
        }

        System.Text.StringBuilder report = new();
        report.AppendLine("### Code Editor, one XHTML file of growing size (headless, Release)");
        report.AppendLine();
        report.AppendLine("| file | open, first frame, ms | one edit and its redraw, ms | one edit, no frame captured, ms | scroll to the end, ms | all text, ms |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");

        // A small file first, so the first row does not pay for the first use of everything.
        foreach (int paragraphs in new[] { 100, 400, 2000, 8000 })
        {
            TempDir temp = _disposables.Add(new TempDir());
            string epub = SyntheticBook.BuildEpub(temp, 1, 0, SyntheticNotes.None, paragraphs: paragraphs);
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            HtmlResource html = book.GetHtmlResources().First();
            html.InitialLoad();
            int kb = html.GetText().Length / 1024;

            TabManagerModel tabModel = new();
            OpenTab tab = tabModel.OpenResource(html);
            string root = _disposables.Add(new TempDir("signet-uitests-")).Path;
            SettingsStore settings = new(Path.Combine(root, "settings.xml"));
            SpellChecker spellChecker = new(settings, Path.Combine(root, "hunspell_dictionaries"), Path.Combine(root, "user_dictionaries"));
            CodeTabViewModel vm = new(tab, new StatusBarService(), settings, spellChecker);
            Window window = new() { Width = 900, Height = 700, Content = new CodeTabView { DataContext = vm } };

            Stopwatch watch = Stopwatch.StartNew();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
            double open = watch.Elapsed.TotalMilliseconds;

            TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
            int middle = editor.Document.TextLength / 2;
            watch.Restart();
            for (int i = 0; i < Edits; i++)
            {
                editor.Document.Insert(middle + i, "x");
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
            }

            double edit = watch.Elapsed.TotalMilliseconds / Edits;

            // The same edits without drawing the frame: what the editor does with the change itself.
            watch.Restart();
            for (int i = 0; i < Edits; i++)
            {
                editor.Document.Insert(middle + Edits + i, "y");
                Dispatcher.UIThread.RunJobs();
            }

            double editOnly = watch.Elapsed.TotalMilliseconds / Edits;

            watch.Restart();
            editor.ScrollToLine(editor.Document.LineCount);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            double scroll = watch.Elapsed.TotalMilliseconds;

            watch.Restart();
            editor.Document.Text = html.GetText();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            double replaceAll = watch.Elapsed.TotalMilliseconds;

            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {kb} KB, {editor.Document.LineCount} lines | {open:F0} | {edit:F1} | {editOnly:F1} | {scroll:F0} | {replaceAll:F0} |"));
            window.Close();
        }

        string text = report.ToString();
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        if (Environment.GetEnvironmentVariable("SIGNET_BENCHMARK_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, text + "\n");
        }

        text.Should().Contain("KB");
    }
}
