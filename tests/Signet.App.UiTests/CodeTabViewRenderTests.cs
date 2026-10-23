using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Themes;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.UiTests;

/// <summary>
/// Headless render test of the Code Editor tab: the <c>TextEditor</c> control is hosted on its own
/// (outside <c>DocumentDock</c>; the tab in the real main window is covered by
/// <c>WindowRenderTests</c> in Signet.App.MainWindowUiTests).
/// </summary>
public sealed class CodeTabViewRenderTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (SettingsStore Settings, SpellChecker SpellChecker) NewSpellChecker()
    {
        string root = _disposables.Add(new TempDir("signet-uitests-")).Path;
        SettingsStore settings = new(System.IO.Path.Combine(root, "settings.xml"));
        SpellChecker spellChecker = new(
            settings,
            System.IO.Path.Combine(root, "hunspell_dictionaries"),
            System.IO.Path.Combine(root, "user_dictionaries"));
        return (settings, spellChecker);
    }

    [AvaloniaFact]
    public void Code_tab_view_renders_the_editor_with_the_resource_text()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        var tabModel = new TabManagerModel();
        OpenTab tab = tabModel.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();

        // Assert
        editor.Document.Text.Should().Be(html.GetText());
        editor.Bounds.Height.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Regression: updating the well-formed error highlight must not throw
    /// <c>NullReferenceException</c> when the <c>TextEditor.Document</c> binding is not resolved yet.
    /// This happened when Dock re-materialized the tab through <c>DeferredContentControl</c> while
    /// re-parenting a dock: <c>DataContextChanged</c> fired before the <c>Document</c> binding set
    /// the document on the control.
    /// </summary>
    [AvaloniaFact]
    public void Error_renderer_update_is_null_safe_when_the_editor_document_is_not_bound_yet()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        var tabModel = new TabManagerModel();
        OpenTab tab = tabModel.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);

        // Force a remembered well-formed error (an unclosed tag).
        vm.Document.Text = "<p>oops";

        // Act
        vm.RunWellFormedCheck();

        // Assert
        vm.WellFormedError.Should().NotBeNull();

        // Act
        var view = new CodeTabView { DataContext = vm };
        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();

        // Reproduce the race: the document disappears from the control, then a WellFormedError
        // change notification arrives (as when Dock re-materializes the tab).
        editor.Document = null;

        // Act & Assert
        vm.Invoking(x => x.RunWellFormedCheck()).Should().NotThrow();
    }

    private (Window Window, CodeTabViewModel Vm) ShowCodeTab(string xhtml, bool extended = true)
    {
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        settings.CodeEditorExtendedHighlighting = extended;
        string dir = _disposables.Add(new TempDir("signet-uitests-")).Path;
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "Section0001.xhtml");
        System.IO.File.WriteAllText(path, xhtml);
        HtmlResource html = new(dir, path);
        html.InitialLoad();

        var vm = new CodeTabViewModel(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);
        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    /// <summary>
    /// A JavaScript tab is coloured by TextMate, whose tokenizer holds a thread pool thread until TextMate is released. The
    /// view releases it when it leaves the screen (the tab closed, or moved into a window of its own) and installs it
    /// again when it comes back: a closed tab used to keep its thread, its model and its document for good.
    /// </summary>
    [AvaloniaFact]
    public void A_view_releases_TextMate_when_it_leaves_the_screen_and_installs_it_again_when_it_comes_back()
    {
        // Arrange
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        string dir = _disposables.Add(new TempDir("signet-uitests-")).Path;
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "script.js");
        System.IO.File.WriteAllText(path, "function hello() {\n  return 'world';\n}\n");
        MiscTextResource script = new(dir, path);
        script.InitialLoad();
        var vm = new CodeTabViewModel(new TabManagerModel().OpenResource(script), new StatusBarService(), settings, spellChecker);

        vm.Syntax.Should().Be(CodeEditorSyntax.JavaScript);

        // Act
        var view = new CodeTabView { DataContext = vm };
        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TextEditor editor = view.GetVisualDescendants().OfType<TextEditor>().Single();
        bool Coloured() => editor.TextArea.TextView.LineTransformers.OfType<AvaloniaEdit.TextMate.TextMateColoringTransformer>().Any();

        // Assert
        Coloured().Should().BeTrue("JavaScript is coloured by TextMate");

        // Act
        window.Content = null;
        Dispatcher.UIThread.RunJobs();

        // Assert
        Coloured().Should().BeFalse("a view off the screen holds no TextMate");

        // Act
        window.Content = view;
        Dispatcher.UIThread.RunJobs();

        // Assert
        Coloured().Should().BeTrue("the view on the screen again is coloured again");

        // Act
        window.Close();
        Dispatcher.UIThread.RunJobs();

        // Assert
        Coloured().Should().BeFalse("the closed tab holds no TextMate");
    }

    // The text color of a key in the theme in use.
    private static Color Fg(ThemeKey key) => AppTheme.Current.Foreground(key)!.Value;

    // Color of the first visible text element of the given line (after colorizing).
    private static Color ForegroundOfFirstElement(Window window, int lineNumber)
    {
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        VisualLine line = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(lineNumber));
        VisualLineElement element = line.Elements.First(e => e.DocumentLength > 0);
        return ((ISolidColorBrush)element.TextRunProperties.ForegroundBrush!).Color;
    }

    [AvaloniaFact]
    public void A_new_view_of_a_tab_puts_the_user_back_where_they_were_in_the_text()
    {
        // Arrange
        string text = string.Join('\n', Enumerable.Range(1, 400).Select(i => $"<p>line {i}</p>"));
        (Window window, CodeTabViewModel vm) = ShowCodeTab(text, extended: false);
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();

        // Act
        editor.CaretOffset = editor.Document.GetLineByNumber(300).Offset + 3;
        editor.TextArea.TextView.MakeVisible(new Avalonia.Rect(0, editor.TextArea.TextView.GetVisualPosition(
            new AvaloniaEdit.TextViewPosition(150, 1), AvaloniaEdit.Rendering.VisualYPosition.LineTop).Y, 1, editor.TextArea.TextView.Bounds.Height));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        int topLine = editor.TextArea.TextView.GetDocumentLineByVisualTop(editor.TextArea.TextView.VerticalOffset)!.LineNumber;

        // Assert
        topLine.Should().BeInRange(140, 160, "the view is scrolled to the middle of the file");
        int caret = editor.CaretOffset;

        // The tab is dragged into a window of its own: its view goes away, a new one is made for the same tab.
        // Act
        window.Content = null;
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.ViewState.Should().NotBeNull("the view that went away remembered the place");

        // Act
        window.Content = new CodeTabView { DataContext = vm };
        for (int i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }

        TextEditor again = window.GetVisualDescendants().OfType<TextEditor>().Single();

        // Assert
        again.TextArea.TextView.GetDocumentLineByVisualTop(again.TextArea.TextView.VerticalOffset)!.LineNumber
            .Should().BeInRange(topLine - 2, topLine + 2, "the first visible line is the same");
        again.CaretOffset.Should().Be(caret);
        window.Close();
    }

    [AvaloniaFact]
    public void Html_is_coloured_with_the_line_highlighter_and_the_colours_of_the_theme()
    {
        // Arrange
        (Window window, _) = ShowCodeTab("<p class=\"x\">a</p>\n<!-- c -->", extended: false);

        // Act & Assert
        ForegroundOfFirstElement(window, 1).Should().Be(Fg(ThemeKeys.XhtmlTag));
        ForegroundOfFirstElement(window, 2).Should().Be(Fg(ThemeKeys.XhtmlComment));
    }

    [AvaloniaFact]
    public void Another_theme_recolours_an_open_tab_at_once()
    {
        // Arrange
        (Window window, _) = ShowCodeTab("<p class=\"x\">a</p>", extended: false);
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        string folder = _disposables.Add(new TempDir("signet-theme-")).Path;
        try
        {
            ThemeStores stores = ThemeStores.In(folder);

            // Act
            AppTheme.Apply(Avalonia.Application.Current!, new ResolvedTheme(
                stores.Themes.Get(ThemeStore.DarkId), stores.Themes.Resolver, stores.Schemes.Get(ThemeStore.DarkId), stores.Schemes.Resolver));
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();

            // Assert
            ForegroundOfFirstElement(window, 1).Should().Be(Color.Parse("#569CD6"), "the tag color of Signet Dark");
            ((ISolidColorBrush)editor.Background!).Color.Should().Be(Color.Parse("#1E1E1E"));
            editor.TextArea.LeftMargins.OfType<Signet.App.CodeEditor.ThemedLineNumberMargin>().Single()
                .Background.Should().BeNull("Signet Dark gives the line numbers no background of their own");
        }
        finally
        {
            AppTheme.Apply(Avalonia.Application.Current!, ResolvedTheme.Defaults);
            System.IO.Directory.Delete(folder, recursive: true);
            window.Close();
        }
    }

    // A script or JSON file in its own tab (highlighted by TextMate).
    private Window ShowMiscTab(string fileName, string text)
    {
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        string dir = _disposables.Add(new TempDir("signet-uitests-")).Path;
        string path = System.IO.Path.Combine(dir, fileName);
        System.IO.File.WriteAllText(path, text);
        MiscTextResource resource = new(dir, path);
        resource.InitialLoad();

        var vm = new CodeTabViewModel(new TabManagerModel().OpenResource(resource), new StatusBarService(), settings, spellChecker);
        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        WaitForTokens(window, text.Split('\n').Length);
        return window;
    }

    // TextMate tokenizes on a background thread and then invalidates the lines: wait until no line of the document
    // is drawn in a single color any more (every test text has more than one token kind per line).
    private static void WaitForTokens(Window window, int lineCount)
    {
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < System.TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            bool colored = Enumerable.Range(1, lineCount).All(n => editor.TextArea.TextView
                .GetOrConstructVisualLine(editor.Document.GetLineByNumber(n)).Elements
                .Select(e => ((ISolidColorBrush)e.TextRunProperties.ForegroundBrush!).Color).Distinct().Count() > 1);
            if (colored)
            {
                return;
            }

            System.Threading.Thread.Sleep(20);
        }
    }

    private static Color ForegroundAt(Window window, int lineNumber, int column) =>
        ((ISolidColorBrush)ElementAt(window, lineNumber, column).TextRunProperties.ForegroundBrush!).Color;

    [AvaloniaFact]
    public void JavaScript_tokens_take_the_colours_of_the_js_keys()
    {
        // Arrange
        const string text = "var s = \"a\"; // note\nif (s) { return 42; }";
        Window window = ShowMiscTab("script.js", text);
        try
        {
            // Act & Assert
            ForegroundAt(window, 1, 0).Should().Be(Fg(ThemeKeys.JsKeyword));
            ForegroundAt(window, 1, text.IndexOf("\"a\"", System.StringComparison.Ordinal)).Should().Be(Fg(ThemeKeys.JsString));
            ForegroundAt(window, 1, text.IndexOf("note", System.StringComparison.Ordinal)).Should().Be(Fg(ThemeKeys.JsComment));
            ForegroundAt(window, 2, 0).Should().Be(Fg(ThemeKeys.JsControlKeyword));
            ForegroundAt(window, 2, "if (s) { return ".Length).Should().Be(Fg(ThemeKeys.JsNumber));
        }
        finally
        {
            window.Close();
        }
    }

    // Regression: the Document binding resolves after DataContextChanged, and a grammar set on the placeholder
    // document left the tokenizer of the real one asleep, so only the first script tab of a session was colored.
    [AvaloniaFact]
    public void Every_script_tab_is_colored_not_only_the_first()
    {
        foreach (string fileName in new[] { "first.js", "second.js", "third.js" })
        {
            // Arrange
            Window window = ShowMiscTab(fileName, "var x = 1;");
            try
            {
                // Act & Assert
                ForegroundAt(window, 1, 0).Should().Be(Fg(ThemeKeys.JsKeyword), fileName);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Json_tokens_take_the_colours_of_the_json_keys()
    {
        // Arrange
        const string text = "{ \"name\": \"x\", \"n\": 1, \"ok\": true }";
        Window window = ShowMiscTab("data.json", text);
        try
        {
            // Act & Assert
            ForegroundAt(window, 1, text.IndexOf("name", System.StringComparison.Ordinal)).Should().Be(Fg(ThemeKeys.JsonProperty));
            ForegroundAt(window, 1, text.IndexOf("\"x\"", System.StringComparison.Ordinal) + 1).Should().Be(Fg(ThemeKeys.JsonString));
            ForegroundAt(window, 1, text.IndexOf('1', System.StringComparison.Ordinal)).Should().Be(Fg(ThemeKeys.JsonNumber));
            ForegroundAt(window, 1, text.IndexOf("true", System.StringComparison.Ordinal)).Should().Be(Fg(ThemeKeys.JsonConstant));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Another_theme_recolours_an_open_script_tab_with_its_attributes()
    {
        // Arrange
        Window window = ShowMiscTab("script.js", "var x = 1;");
        Theme theme = new Theme(System.Ulid.NewUlid().ToString(), "Script").With(
            ThemeKeys.JsKeyword.Id, ThemeEntry.Of(new TextAttributes { Foreground = ThemeColor.FromRgb(0x12, 0x34, 0x56), Bold = true }));
        try
        {
            // Act
            AppTheme.Apply(Avalonia.Application.Current!, ResolvedTheme.Of(ThemeDocumentKind.Scheme, theme, new ThemeResolver(_ => null)));
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();

            VisualLineElement keyword = ElementAt(window, 1, 0);

            // Assert
            ((ISolidColorBrush)keyword.TextRunProperties.ForegroundBrush!).Color.Should().Be(Color.FromRgb(0x12, 0x34, 0x56));
            keyword.TextRunProperties.Typeface.Weight.Should().Be(FontWeight.Bold);
        }
        finally
        {
            AppTheme.Apply(Avalonia.Application.Current!, ResolvedTheme.Defaults);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Opening_a_comment_recolours_the_following_lines_after_the_edit(bool extended)
    {
        // Arrange
        (Window window, CodeTabViewModel vm) = ShowCodeTab("<p>a</p>\n<p>b</p>\n<p>c</p>", extended);

        // Act & Assert
        ForegroundOfFirstElement(window, 3).Should().Be(Fg(ThemeKeys.XhtmlTag));

        // Act
        vm.Document.Insert(0, "<!-- ");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        // Assert
        ForegroundOfFirstElement(window, 3).Should().Be(Fg(ThemeKeys.XhtmlComment),
            "the comment state from line 1 must carry over to the following lines without scrolling the view");
    }

    // The visual line element containing the given offset within the line.
    private static VisualLineElement ElementAt(Window window, int lineNumber, int column)
    {
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        VisualLine line = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(lineNumber));
        return line.Elements.First(e => column >= e.RelativeTextOffset && column < e.RelativeTextOffset + e.DocumentLength);
    }

    [AvaloniaFact]
    public void Extended_highlighting_styles_text_and_colours_css_inside_style()
    {
        // Arrange
        const string line1 = "<p><b>bold</b> <i>it</i></p>";
        const string line2 = "<style>p { color: red }</style>";
        (Window window, _) = ShowCodeTab(line1 + "\n" + line2);

        Typeface bold = ElementAt(window, 1, line1.IndexOf("bold", System.StringComparison.Ordinal)).TextRunProperties.Typeface;
        Typeface italic = ElementAt(window, 1, line1.IndexOf("it<", System.StringComparison.Ordinal)).TextRunProperties.Typeface;

        // Act & Assert
        bold.Weight.Should().Be(FontWeight.Bold);
        italic.Style.Should().Be(FontStyle.Italic);

        VisualLineElement red = ElementAt(window, 2, line2.IndexOf("red", System.StringComparison.Ordinal));
        ((ISolidColorBrush)red.TextRunProperties.ForegroundBrush!).Color.Should().Be(Fg(ThemeKeys.CssConstant));
    }

    [AvaloniaFact]
    public void Without_extended_highlighting_style_content_keeps_a_single_colour()
    {
        // Arrange
        const string text = "<style>p { color: red }</style>";
        (Window window, _) = ShowCodeTab(text, extended: false);

        VisualLineElement red = ElementAt(window, 1, text.IndexOf("red", System.StringComparison.Ordinal));

        // Act & Assert
        ((ISolidColorBrush)red.TextRunProperties.ForegroundBrush!).Color
            .Should().Be(Fg(ThemeKeys.XhtmlCss));
    }

    [AvaloniaFact]
    public void Syntax_errors_of_drawn_lines_are_remembered_for_the_squiggle_and_tooltip()
    {
        // Arrange
        (Window window, _) = ShowCodeTab("<p>a < b</p>");
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        IVisualLineTransformer colorizer = editor.TextArea.TextView.LineTransformers.Single(t => t.GetType().Name.StartsWith("LineStateSyntaxColorizer", System.StringComparison.Ordinal));

        // Act
        var issues = (System.Collections.Generic.IReadOnlyList<SyntaxSpan>)colorizer.GetType()
            .GetMethod("IssuesOf")!.Invoke(colorizer, new object[] { editor.Document.GetLineByNumber(1) })!;

        // Assert
        issues.Should().ContainSingle(s => s.Issue == SyntaxIssue.UnescapedLessThan && s.Start == 5);
    }

    [AvaloniaFact]
    public void Typing_lt_slash_in_the_editor_closes_the_open_element()
    {
        // Arrange
        (Window window, CodeTabViewModel vm) = ShowCodeTab("<p>abc<");
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.TextArea.Focus();
        editor.CaretOffset = vm.Document.TextLength;

        // Act
        window.KeyTextInput("/");
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.Document.Text.Should().Be("<p>abc</p>");
        editor.CaretOffset.Should().Be("<p>abc</p>".Length);
    }

    /// <summary>
    /// Regression: the Code Editor font family and size from Preferences (Appearance → Code Editor)
    /// must reach the editor. <c>CodeTabView</c> used to hardcode the size and family, so the
    /// setting was saved to the file but had no effect.
    /// </summary>
    [AvaloniaFact]
    public void Code_editor_font_family_and_size_come_from_the_appearance_settings()
    {
        // Arrange
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        settings.CodeEditorAppearance = settings.CodeEditorAppearance with
        {
            FontFamily = "Consolas",
            FontSize = 21,
        };

        string dir = _disposables.Add(new TempDir("signet-uitests-")).Path;
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "Section0001.xhtml");
        System.IO.File.WriteAllText(path, "<p>a</p>");
        HtmlResource html = new(dir, path);
        html.InitialLoad();

        var vm = new CodeTabViewModel(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);
        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();

        // Assert
        editor.FontSize.Should().Be(21);
        editor.FontFamily.Name.Should().Be("Consolas");

        // Zoom scales the size from the settings, not a hardcoded base value.
        // Act
        vm.ZoomFactor = 2.0;
        Dispatcher.UIThread.RunJobs();

        // Assert
        editor.FontSize.Should().Be(42);
    }

    [AvaloniaFact]
    public void Typing_in_href_shows_file_completions_and_enter_inserts_the_best_match()
    {
        // Arrange
        (Window window, CodeTabViewModel vm) = ShowCodeTab("<p><a href=\"</p>");
        vm.BookFiles = () => new[]
        {
            new LinkCompletionFile("Section0001.xhtml", "application/xhtml+xml"),
            new LinkCompletionFile("chapter-two.xhtml", "application/xhtml+xml"),
            new LinkCompletionFile("appendix.xhtml", "application/xhtml+xml"),
        };
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.TextArea.Focus();
        editor.CaretOffset = "<p><a href=\"".Length;

        // Act
        window.KeyTextInput("c");
        window.KeyTextInput("t");
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.Document.Text.Should().Be("<p><a href=\"chapter-two.xhtml</p>");
    }

    /// <summary>
    /// Regression (crash): the completion list box shows <c>CompletionData</c>, a plain list without change
    /// notifications. On a typed character AvaloniaEdit first selects the best match of the old list (here the third
    /// row, "aef"); refilling the list with fewer completions left that index past its end, and selecting the first
    /// new completion threw <see cref="System.ArgumentOutOfRangeException"/>.
    /// </summary>
    [AvaloniaFact]
    public void A_character_that_leaves_fewer_completions_than_the_best_old_row_does_not_crash()
    {
        // Arrange
        (Window window, CodeTabViewModel vm) = ShowCodeTab("<p><a href=\"</p>");
        vm.BookFiles = () => new[]
        {
            new LinkCompletionFile("abc.xhtml", "application/xhtml+xml"),
            new LinkCompletionFile("abd.xhtml", "application/xhtml+xml"),
            new LinkCompletionFile("aef.xhtml", "application/xhtml+xml"),
        };
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.TextArea.Focus();
        editor.CaretOffset = "<p><a href=\"".Length;

        // Act
        window.KeyTextInput("a"); // all three
        window.KeyTextInput("e"); // only the third one
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.Document.Text.Should().Be("<p><a href=\"aef.xhtml</p>");
    }

    /// <summary>
    /// Regression: a right click in Code Editor showed no menu when there was no misspelled word
    /// under the caret (the only context menu was the spelling menu, cancelled in that case).
    /// The menu now always has the full default layout.
    /// </summary>
    [AvaloniaFact]
    public void Context_menu_opens_with_the_default_layout_without_a_misspelled_word()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        var tabModel = new TabManagerModel();
        OpenTab tab = tabModel.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = new FakeHost() };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        ContextMenu menu = editor.ContextMenu!;
        // Like a right click / the menu key: ContextRequested from the text area (Open() from code skips Opening).
        // Act
        editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        // Assert
        menu.IsOpen.Should().BeTrue();
        string[] headers = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToArray();
        headers.Should().Equal(
            Signet.App.Resources.Strings.Get("CodeEditorMenu_Clips"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_AddToClips"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_ToggleLineWrapMode"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_GoToLinkOrStyle"),
            CodeTabViewModel.MergeContentTextFor(null),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_UnwrapDiv"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_MarkSelectedText"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_ReformatHtml"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_Undo"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_Redo"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_Cut"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_Copy"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_Paste"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_Delete"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_SelectAll"));
        menu.Close();
    }

    /// <summary>
    /// Regression: "Go To Link Or Style", "Merge Content", "Unwrap &lt;div&gt;" and "Mark Selected Text" run
    /// application actions that also have an icon in the main menu, but the context menu item did not show it.
    /// </summary>
    [AvaloniaFact]
    public void Context_menu_items_with_a_main_menu_action_show_its_icon()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        FakeHost host = new();
        foreach (string actionId in new[]
                 {
                     AppActionIds.GoToLinkOrStyle, AppActionIds.MergeContent, AppActionIds.UnwrapDiv, AppActionIds.MarkSelection,
                 })
        {
            host.Icons[actionId] = AppActionCatalog.All.Single(a => a.Id == actionId).IconKey!;
        }

        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = host };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        ContextMenu menu = editor.ContextMenu!;

        // The icon set is loaded only by the desktop app startup, so the bundled "main" set is merged in temporarily here.
        var icons = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://Signet.App/Assets/Icons/MainIcons.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(icons);
        try
        {
            // Act
            editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();

            MenuItem Item(string key) =>
                menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == Signet.App.Resources.Strings.Get(key));

            // Assert
            Item("CodeEditorMenu_GoToLinkOrStyle").Icon.Should().NotBeNull();
            menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == CodeTabViewModel.MergeContentTextFor(null))
                .Icon.Should().NotBeNull();
            Item("CodeEditorMenu_UnwrapDiv").Icon.Should().NotBeNull();
            Item("CodeEditorMenu_MarkSelectedText").Icon.Should().NotBeNull();
            menu.Close();
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(icons);
        }
    }

    /// <summary>"Rename Class…" appears when the caret is on a class name in XHTML.</summary>
    [AvaloniaFact]
    public void Context_menu_offers_rename_class_when_the_caret_is_on_a_class_name()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = new FakeHost() };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.Document.Text = "<html><body><p class=\"c1\">x</p></body></html>";
        editor.CaretOffset = "<html><body><p class=\"c1".Length;
        Dispatcher.UIThread.RunJobs();

        // Act
        editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        ContextMenu menu = editor.ContextMenu!;

        // Assert
        menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!)
            .Should().Contain(Signet.App.Resources.Strings.Get("CodeEditorMenu_RenameClass"));
        menu.Close();
    }

    /// <summary>"Remove Class…" appears right after "Find Usages" when the caret is on a class name.</summary>
    [AvaloniaFact]
    public void Context_menu_offers_remove_class_right_after_find_usages()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = new FakeHost() };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.Document.Text = "<html><body><p class=\"c1\">x</p></body></html>";
        editor.CaretOffset = "<html><body><p class=\"c1".Length;
        Dispatcher.UIThread.RunJobs();

        // Act
        editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        ContextMenu menu = editor.ContextMenu!;

        // Assert
        List<string> headers = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        int findUsages = headers.IndexOf(Signet.App.Resources.Strings.Get("CodeEditorMenu_FindUsages"));
        int removeClass = headers.IndexOf(Signet.App.Resources.Strings.Get("CodeEditorMenu_RemoveClass"));
        findUsages.Should().BeGreaterThanOrEqualTo(0);
        removeClass.Should().Be(findUsages + 1, "Remove Class sits right after Find Usages");
        menu.Close();
    }

    /// <summary>"Find Id Usages" appears when the caret is on an id attribute and asks the host to search for it.</summary>
    [AvaloniaFact]
    public void Context_menu_offers_find_id_usages_when_the_caret_is_on_an_id()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        FakeHost host = new();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = host };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.Document.Text = "<html><body><p id=\"note1\">x</p></body></html>";
        editor.CaretOffset = "<html><body><p id=\"note1".Length;
        Dispatcher.UIThread.RunJobs();

        // The icon set is loaded only by the desktop app startup, so the bundled "main" set is merged in temporarily here.
        var icons = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://Signet.App/Assets/Icons/MainIcons.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(icons);
        try
        {
            // Act — the icon lookup runs while the context menu is built, so the set must already be merged.
            editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();

            ContextMenu menu = editor.ContextMenu!;
            MenuItem item = menu.Items.OfType<MenuItem>()
                .Single(i => (string?)i.Header == Signet.App.Resources.Strings.Get("CodeEditorMenu_FindIdUsages"));
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

            // Assert
            item.Icon.Should().NotBeNull();
            host.IdUsagesRequested.Should().Equal("note1");
            menu.Close();
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(icons);
        }
    }

    /// <summary>"Rename Id..." sits right before "Find Id Usages" and opens the rename window for the id under the caret.</summary>
    [AvaloniaFact]
    public void Context_menu_offers_rename_id_right_before_find_id_usages_and_opens_its_window()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        FakeHost host = new();
        host.PrepareIdRenameFunc = () => System.Threading.Tasks.Task.FromResult<Signet.Core.BookManipulation.IdRenamer?>(
            new Signet.Core.BookManipulation.IdRenamer(
                [new Signet.Core.BookManipulation.ClassRenameSource("a.xhtml", "<html><body><p id=\"note1\">x</p></body></html>")],
                []));
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = host };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.Document.Text = "<html><body><p id=\"note1\">x</p></body></html>";
        editor.CaretOffset = "<html><body><p id=\"note1".Length;
        Dispatcher.UIThread.RunJobs();

        // Act
        editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        ContextMenu menu = editor.ContextMenu!;
        List<string> headers = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        int renameId = headers.IndexOf(Signet.App.Resources.Strings.Get("CodeEditorMenu_RenameId"));
        int findIdUsages = headers.IndexOf(Signet.App.Resources.Strings.Get("CodeEditorMenu_FindIdUsages"));

        // Assert
        renameId.Should().BeGreaterThanOrEqualTo(0);
        findIdUsages.Should().Be(renameId + 1, "Rename Id sits right before Find Id Usages");

        // Act
        menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == Signet.App.Resources.Strings.Get("CodeEditorMenu_RenameId"))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        menu.Close();

        // Assert
        Window renameWindow = window.OwnedWindows.OfType<Signet.App.Views.RenameIdWindow>().Single();
        renameWindow.Close();
    }

    /// <summary>
    /// The class items run application actions, so they show the actions' current shortcuts (like the main menu);
    /// an action without a shortcut shows none.
    /// </summary>
    [AvaloniaFact]
    public void Class_items_of_the_context_menu_show_the_shortcuts_of_their_actions()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        FakeHost host = new();
        host.Gestures[AppActionIds.FindUsages] = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.F7, Avalonia.Input.KeyModifiers.Alt);
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = host };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();

        // Act
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.Document.Text = "<html><body><p class=\"c1\">x</p></body></html>";
        editor.CaretOffset = "<html><body><p class=\"c1".Length;
        Dispatcher.UIThread.RunJobs();
        editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        ContextMenu menu = editor.ContextMenu!;
        MenuItem Item(string key) =>
            menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == Signet.App.Resources.Strings.Get(key));

        // Assert
        Item("CodeEditorMenu_FindUsages").InputGesture.Should().Be(host.Gestures[AppActionIds.FindUsages]);
        Item("CodeEditorMenu_RenameClass").InputGesture.Should().BeNull("Rename Class has no shortcut by default");
        menu.Close();
    }

    /// <summary>
    /// Regression: "Rename Class…" and "Find Usages" run application actions that also have an icon in the
    /// main menu, but the context menu item did not show it.
    /// </summary>
    [AvaloniaFact]
    public void Class_items_of_the_context_menu_show_the_icons_of_their_actions()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        FakeHost host = new();
        host.Icons[AppActionIds.RenameClass] = AppActionCatalog.All.Single(a => a.Id == AppActionIds.RenameClass).IconKey!;
        host.Icons[AppActionIds.FindUsages] = AppActionCatalog.All.Single(a => a.Id == AppActionIds.FindUsages).IconKey!;
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = host };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.Document.Text = "<html><body><p class=\"c1\">x</p></body></html>";
        editor.CaretOffset = "<html><body><p class=\"c1".Length;
        Dispatcher.UIThread.RunJobs();
        ContextMenu menu = editor.ContextMenu!;

        // The icon set is loaded only by the desktop app startup, so the bundled "main" set is merged in temporarily here.
        var icons = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://Signet.App/Assets/Icons/MainIcons.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(icons);
        try
        {
            // Act — the icon lookup runs while the context menu is built, so the set must already be merged.
            editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();

            MenuItem Item(string key) =>
                menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == Signet.App.Resources.Strings.Get(key));

            // Assert
            Item("CodeEditorMenu_RenameClass").Icon.Should().NotBeNull();
            Item("CodeEditorMenu_FindUsages").Icon.Should().NotBeNull();
            menu.Close();
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(icons);
        }
    }

    /// <summary>
    /// "Rename Class…" in a CSS stylesheet: the caret is on a class in a selector, with no selection
    /// needed.
    /// </summary>
    [AvaloniaFact]
    public void Context_menu_offers_rename_class_in_a_stylesheet_selector()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        css.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(css);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = new FakeHost() };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();

        // Act
        editor.Document.Text = "p.note { color: red }";
        editor.CaretOffset = 4;
        Dispatcher.UIThread.RunJobs();
        editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        ContextMenu menu = editor.ContextMenu!;

        // Assert
        menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!)
            .Should().Contain(Signet.App.Resources.Strings.Get("CodeEditorMenu_RenameClass"));
        menu.Close();
    }

    /// <summary>
    /// "Merge Content": the item is always present; it is enabled, showing the element name and
    /// count, when the selection spans adjacent elements of the same kind.
    /// </summary>
    [AvaloniaFact]
    public void Context_menu_always_shows_merge_content_enabled_only_for_mergeable_selection()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = new FakeHost() };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        const string text = "<html><body><p>a</p>\n<p>b</p></body></html>";
        editor.Document.Text = text;
        int start = text.IndexOf("<p>", System.StringComparison.Ordinal);
        int end = text.LastIndexOf("</p>", System.StringComparison.Ordinal) + 4;

        MenuItem MergeItem()
        {
            Dispatcher.UIThread.RunJobs();
            editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();
            ContextMenu menu = editor.ContextMenu!;
            MenuItem item = menu.Items.OfType<MenuItem>().Single(i => ((string)i.Header!).Contains(
                CodeTabViewModel.MergeContentTextFor(null), System.StringComparison.Ordinal));
            menu.Close();
            return item;
        }

        // Act
        editor.Select(start, end - start);
        MenuItem enabled = MergeItem();

        // Assert
        enabled.Header.Should().Be(Signet.App.Resources.Strings.Format("CodeEditorMenu_MergeContentCount", "p", 2));
        enabled.IsEnabled.Should().BeTrue();

        // Act
        editor.Select(start + 3, 1); // "a": both ends in one paragraph
        MenuItem disabled = MergeItem();

        // Assert
        disabled.Header.Should().Be(CodeTabViewModel.MergeContentTextFor(null));
        disabled.IsEnabled.Should().BeFalse();
    }

    /// <summary>"Unwrap div": always present; greyed out with a tooltip saying why outside a div.</summary>
    [AvaloniaFact]
    public void Context_menu_unwrap_div_is_disabled_with_a_reason_outside_a_div()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = new FakeHost() };
        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        const string text = "<html><body><div><p>a</p></div>\n<p>b</p></body></html>";
        editor.Document.Text = text;

        MenuItem UnwrapItem(int caret)
        {
            editor.CaretOffset = caret;
            Dispatcher.UIThread.RunJobs();
            editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();
            ContextMenu menu = editor.ContextMenu!;
            MenuItem item = menu.Items.OfType<MenuItem>().Single(
                i => (string)i.Header! == Signet.App.Resources.Strings.Get("CodeEditorMenu_UnwrapDiv"));
            menu.Close();
            return item;
        }

        // Act
        MenuItem inside = UnwrapItem(text.IndexOf("<p>a", System.StringComparison.Ordinal));

        // Assert
        inside.IsEnabled.Should().BeTrue();
        ToolTip.GetTip(inside).Should().BeNull();

        // Act
        MenuItem outside = UnwrapItem(text.IndexOf("<p>b", System.StringComparison.Ordinal));

        // Assert
        outside.IsEnabled.Should().BeFalse();
        ToolTip.GetTip(outside).Should().Be(Signet.App.Resources.Strings.Get("UnwrapDiv_NotInDiv"));
    }

    // Which items of the "Footnotes" submenu are enabled on a footnote with one reference.
    private static readonly bool[] OnSingleFootnoteEnabled = { true, true, false, true, true, true, true };

    // Which items are enabled where a footnote can be added, in a book without notes (and without other footnotes).
    private static readonly bool[] FreeWithoutNotesEnabled = { true, false, true, false, true };

    /// <summary>
    /// "Footnotes": on a footnote the submenu offers editing it and going to its other part; elsewhere adding one,
    /// disabled with the reason when the caret is not in a place for a reference; renumbering always.
    /// </summary>
    [AvaloniaFact]
    public void Context_menu_footnotes_submenu_follows_the_footnote_state_at_the_caret()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetHtmlResources().First(h => !ReferenceEquals(h, book.GetNavResource()));
        html.InitialLoad();
        string source = html.GetText();
        int offset = source.IndexOf("</p>", source.IndexOf("<body", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
        Signet.Core.Footnotes.FootnoteEditor.Add(
            book, html, offset, "Note.", Signet.Core.Footnotes.FootnotePlacement.Local, out Signet.Core.Footnotes.Footnote? footnote)
            .Should().Be(Signet.Core.Footnotes.FootnoteError.None);

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        FakeHost host = new();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = host };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();

        string[] Footnotes(FootnoteMenuState state, out bool[] enabled)
        {
            host.FootnoteState = (_, _) => state;
            Dispatcher.UIThread.RunJobs();
            editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();
            ContextMenu menu = editor.ContextMenu!;
            MenuItem submenu = menu.Items.OfType<MenuItem>()
                .Single(i => (string)i.Header! == Signet.App.Resources.Strings.Get("CodeEditorMenu_Footnotes"));
            MenuItem[] items = submenu.Items.OfType<MenuItem>().ToArray();
            menu.Close();
            enabled = items.Select(i => i.IsEnabled).ToArray();
            return items.Select(i => (string)i.Header!).ToArray();
        }

        // Act
        string[] onFootnote = Footnotes(
            new FootnoteMenuState(
                new Signet.Core.Footnotes.FootnoteHit(footnote!, Signet.Core.Footnotes.FootnoteSide.Reference),
                Signet.Core.Footnotes.FootnoteError.InsideLink, true, ForeignCount: 2),
            out bool[] onFootnoteEnabled);

        // Assert
        onFootnote.Should().Equal(
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnoteEdit"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnoteGoToNote"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnoteRemoveReference"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnoteRemove"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnotesRenumber"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnotesConvert"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnotesUnifyLetters"));
        onFootnoteEnabled.Should().Equal(OnSingleFootnoteEnabled, "the only reference is removed with the whole footnote");

        // Act
        string[] blocked = Footnotes(new FootnoteMenuState(null, Signet.Core.Footnotes.FootnoteError.InsideTag, false), out bool[] blockedEnabled);

        // Assert
        blocked.Should().Equal(
            Signet.App.Resources.Strings.Format(
                "CodeEditorMenu_FootnoteAddBlocked",
                Signet.App.ViewModels.FootnoteViewModel.ErrorText(Signet.Core.Footnotes.FootnoteError.InsideTag)),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnoteAddReference"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnotesRenumber"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnotesConvert"),
            Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnotesUnifyLetters"));
        blockedEnabled.Should().Equal(false, false, false, false, false);

        // Act
        string[] free = Footnotes(new FootnoteMenuState(null, Signet.Core.Footnotes.FootnoteError.None, true), out bool[] freeEnabled);

        // Assert
        free[0].Should().Be(Signet.App.Resources.Strings.Get("CodeEditorMenu_FootnoteAdd"));
        freeEnabled.Should().Equal(FreeWithoutNotesEnabled, "without notes there is nothing to add a reference to");

        // Act
        Footnotes(new FootnoteMenuState(null, Signet.Core.Footnotes.FootnoteError.None, true, ReferenceTargetCount: 3), out bool[] withNotes);

        // Assert
        withNotes[1].Should().BeTrue("a reference can lead to one of the notes");
    }

    /// <summary>
    /// The shortcuts of the code editor actions are the view's own bindings: F2 renames a class while the editor has
    /// the focus, and does nothing for it from another control of the window (a panel may use F2 there).
    /// </summary>
    [AvaloniaFact]
    public void Code_editor_shortcuts_work_only_while_the_editor_has_focus()
    {
        // Arrange
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        Signet.App.Input.KeyboardShortcutManager shortcuts = new(settings);
        AppActionRegistry registry = new(
            shortcuts, new StatusBarService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AppActionRegistry>.Instance);
        shortcuts.AddShortcut(AppActionIds.RenameClass, new Signet.App.Input.KeyStrokeShortcut(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.F2)));
        int renames = 0;
        registry.SetHandler(AppActionIds.RenameClass, () => renames++);

        string dir = _disposables.Add(new TempDir("signet-uitests-")).Path;
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "Section0001.xhtml");
        System.IO.File.WriteAllText(path, "<p class=\"x\">a</p>");
        HtmlResource html = new(dir, path);
        html.InitialLoad();
        var vm = new CodeTabViewModel(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker)
        {
            Host = new FakeHost { CodeEditorShortcutActions = new[] { registry.Require(AppActionIds.RenameClass) } },
        };
        TextBox other = new();
        DockPanel panel = new();
        DockPanel.SetDock(other, Avalonia.Controls.Dock.Top);
        panel.Children.Add(other);
        panel.Children.Add(new CodeTabView { DataContext = vm });
        var window = new Window { Width = 600, Height = 400, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();

        // Act
        editor.TextArea.Focus();
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.F2, Avalonia.Input.RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // Assert
        renames.Should().Be(1);

        // Act
        other.Focus();
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.F2, Avalonia.Input.RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // Assert
        renames.Should().Be(1, "outside the editor the shortcut is not the editor's");
    }

    private sealed class FakeHost : ICodeTabHost
    {
        public System.Collections.Generic.IReadOnlyList<AppAction> CodeEditorShortcutActions { get; set; } =
            System.Array.Empty<AppAction>();

        public Signet.Core.MiscEditors.ClipEditorNode ClipLibraryRoot { get; } = new Signet.Core.MiscEditors.ClipEditorModel().Root;

        public void PasteClip(string text)
        {
        }

        public void AddToClips(string text)
        {
        }

        public System.Collections.Generic.Dictionary<string, Avalonia.Input.KeyGesture> Gestures { get; } = new();

        public void ExecuteAction(string actionId)
        {
        }

        public Avalonia.Input.KeyGesture? GetActionGesture(string actionId) => Gestures.TryGetValue(actionId, out Avalonia.Input.KeyGesture? gesture) ? gesture : null;

        public System.Collections.Generic.Dictionary<string, string> Icons { get; } = new();

        public string? GetActionIcon(string actionId) => Icons.TryGetValue(actionId, out string? icon) ? icon : null;

        public System.Threading.Tasks.Task<string?> ReformatHtmlTextAsync(Resource resource, string text, bool toValid) =>
            System.Threading.Tasks.Task.FromResult<string?>(null);

        public System.Threading.Tasks.Task<Signet.Core.BookManipulation.ClassRenamer?> PrepareClassRenameAsync() =>
            System.Threading.Tasks.Task.FromResult<Signet.Core.BookManipulation.ClassRenamer?>(null);

        public Task ApplyClassRename(Signet.Core.BookManipulation.ClassRenameResult result, ProgressRunner? runWithProgress = null) =>
            Task.CompletedTask;

        public System.Func<System.Threading.Tasks.Task<Signet.Core.BookManipulation.IdRenamer?>> PrepareIdRenameFunc { get; set; } =
            () => System.Threading.Tasks.Task.FromResult<Signet.Core.BookManipulation.IdRenamer?>(null);

        public System.Threading.Tasks.Task<Signet.Core.BookManipulation.IdRenamer?> PrepareIdRenameAsync() => PrepareIdRenameFunc();

        public Signet.Core.BookManipulation.IdRenameResult? AppliedIdRename { get; private set; }

        public Task ApplyIdRename(Signet.Core.BookManipulation.IdRenameResult result, ProgressRunner? runWithProgress = null)
        {
            AppliedIdRename = result;
            return Task.CompletedTask;
        }

        public System.Threading.Tasks.Task<Signet.Core.BookManipulation.ClassRemover?> PrepareClassRemovalAsync() =>
            System.Threading.Tasks.Task.FromResult<Signet.Core.BookManipulation.ClassRemover?>(null);

        public Task ApplyClassRemoval(Signet.Core.BookManipulation.ClassRemovalResult result, ProgressRunner? runWithProgress = null) =>
            Task.CompletedTask;

        public System.Collections.Generic.List<string> IdUsagesRequested { get; } = new();

        public void FindIdUsages(string id) => IdUsagesRequested.Add(id);

        public int CountSameSpansInBook(HtmlResource html, string text, int offset) => 0;

        public Signet.Core.BookManipulation.SpanRemovalPlan? PlanSpanRemoval(
            HtmlResource html, string text, int offset, Signet.Core.BookManipulation.SpanRemovalScope scope) => null;

        public Task ApplySpanRemovalInBook(Signet.Core.BookManipulation.SpanRemovalPlan plan, ProgressRunner? runWithProgress = null) =>
            Task.CompletedTask;

        public void ViewImage(string bookPath)
        {
        }

        public System.Func<HtmlResource, int, FootnoteMenuState?> FootnoteState { get; set; } = (_, _) => null;

        public FootnoteMenuState? GetFootnoteState(HtmlResource html, int offset) => FootnoteState(html, offset);

        public Signet.Core.Footnotes.FootnotePlacement LastFootnotePlacement => Signet.Core.Footnotes.FootnotePlacement.Local;

        public (int Local, int Global) GetFootnoteNumbers(HtmlResource html, int offset) => (1, 1);

        public Signet.Core.Footnotes.FootnoteError ValidateFootnoteContent(string content) =>
            Signet.Core.Footnotes.FootnoteError.None;

        public Task AddFootnote(
            HtmlResource html, int offset, string content, Signet.Core.Footnotes.FootnotePlacement placement,
            ProgressRunner? runWithProgress = null) => Task.CompletedTask;

        public FootnoteEditState? GetFootnoteEdit(Signet.Core.Footnotes.FootnoteHit hit) => null;

        public Signet.Core.Footnotes.FootnoteError ValidateFootnoteEdit(Signet.Core.Footnotes.Footnote footnote, string content) =>
            Signet.Core.Footnotes.FootnoteError.None;

        public Task EditFootnote(
            Signet.Core.Footnotes.FootnoteHit hit, string content, Signet.Core.Footnotes.FootnotePlacement placement,
            ProgressRunner? runWithProgress = null) => Task.CompletedTask;

        public Task RemoveFootnote(Signet.Core.Footnotes.FootnoteHit hit, ProgressRunner? runWithProgress = null) => Task.CompletedTask;

        public Task RemoveFootnoteReference(Signet.Core.Footnotes.FootnoteHit hit, ProgressRunner? runWithProgress = null) =>
            Task.CompletedTask;

        public Signet.Core.Footnotes.ReferenceTargetList? GetFootnoteReferenceTargets(HtmlResource html, int offset) => null;

        public Task AddFootnoteReference(
            HtmlResource html, int offset, Signet.Core.Footnotes.Footnote target, ProgressRunner? runWithProgress = null) =>
            Task.CompletedTask;

        public Signet.Core.Footnotes.FootnoteMarkup? PreviewFootnote(
            HtmlResource html, int referenceStart, int referenceEnd, string content, Signet.Core.Footnotes.FootnotePlacement placement, int number,
            Signet.Core.Footnotes.Footnote? footnote) => null;

        public bool ConfirmFootnoteRemoval { get; set; } = true;

        public void GoToFootnote(Signet.Core.Footnotes.FootnoteHit hit)
        {
        }

        public Task RenumberFootnotes(ProgressRunner? runWithProgress = null) => Task.CompletedTask;

        public void ShowFootnoteLetters()
        {
        }

        public void ShowFootnoteConversion()
        {
        }
    }

    /// <summary>
    /// Repeated Find Next in long, wrapped paragraphs: the match must be fully visible, with a
    /// margin of at least one line at the top and bottom (regression: the match was "stuck" to the
    /// top edge and partially cut off).
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_next_keeps_each_match_visible_with_a_one_line_margin(bool jumpAround)
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();
        string paragraph = string.Concat(Enumerable.Repeat("Ala ma kota, a kot ma Alę i dużo innych słów. ", 12));
        string body = string.Concat(Enumerable.Range(0, 120).Select(i =>
            "  <p class=\"calibre7\">" + (i % 4 == 3 ? "krótko " + i : paragraph) + "</p>\n\n"));
        html.SetText("<html>\n<body>\n" + body + "</body>\n</html>");

        var tabModel = new TabManagerModel();
        OpenTab tab = tabModel.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { WordWrap = true };
        var window = new Window { Width = 500, Height = 300, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        TextView textView = editor.TextArea.TextView;
        const string needle = "<p class=\"calibre7\">";
        string text = editor.Document.Text;
        var starts = new System.Collections.Generic.List<int>();
        for (int i = text.IndexOf(needle, System.StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + 1, System.StringComparison.Ordinal))
        {
            starts.Add(i);
        }

        // Jumps far into text that is not rendered yet (e.g. multi-file Find Next, wrap-around).
        int[] order = jumpAround
            ? [starts[^1], starts[0], starts[starts.Count / 2], starts[^2], starts[5], starts[^1]]
            : starts.ToArray();

        foreach (int start in order)
        {
            int end = start + needle.Length;

            // Act
            vm.SelectMatch(start, end);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();

            double margin = textView.DefaultLineHeight;
            double top = textView.GetVisualPosition(
                new TextViewPosition(editor.Document.GetLocation(start)), VisualYPosition.LineTop).Y;
            double bottom = textView.GetVisualPosition(
                new TextViewPosition(editor.Document.GetLocation(end)), VisualYPosition.LineBottom).Y;
            double offset = textView.VerticalOffset;
            double viewport = textView.Bounds.Height;

            // Assert
            (top - offset).Should().BeGreaterThanOrEqualTo(margin - 0.5, $"the match at {start} has a top margin");
            (offset + viewport - bottom).Should().BeGreaterThanOrEqualTo(margin - 0.5, $"the match at {start} has a bottom margin");
        }

        starts.Should().HaveCount(120);
        window.Close();
    }

    /// <summary>
    /// Multi-file Find Next opens a file and selects the match right away, before the editor has a
    /// height. After layout the match must be visible with a margin (regression: the match was at
    /// the very top edge and partially cut off).
    /// </summary>
    [AvaloniaFact]
    public void Match_selected_before_the_editor_is_laid_out_is_scrolled_into_view_with_a_margin()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();
        string paragraph = string.Concat(Enumerable.Repeat("Ala ma kota, a kot ma Alę i dużo innych słów. ", 12));
        string body = string.Concat(Enumerable.Range(0, 60).Select(i => "  <p>" + paragraph + "</p>\n\n"));
        html.SetText("<html>\n<body>\n" + body + "  <p class=\"target\">x</p>\n" + body + "</body>\n</html>");

        var tabModel = new TabManagerModel();
        OpenTab tab = tabModel.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { WordWrap = true };
        var view = new CodeTabView { DataContext = vm };
        var window = new Window { Width = 500, Height = 300, Content = view };
        const string needle = "<p class=\"target\">";
        int start = html.GetText().IndexOf(needle, System.StringComparison.Ordinal);
        int end = start + needle.Length;

        // Act
        window.Show();
        vm.SelectMatch(start, end);
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        TextView textView = editor.TextArea.TextView;
        double margin = textView.DefaultLineHeight;
        double top = textView.GetVisualPosition(
            new TextViewPosition(editor.Document.GetLocation(start)), VisualYPosition.LineTop).Y;
        double bottom = textView.GetVisualPosition(
            new TextViewPosition(editor.Document.GetLocation(end)), VisualYPosition.LineBottom).Y;
        double offset = textView.VerticalOffset;

        // Assert
        (top - offset).Should().BeGreaterThanOrEqualTo(margin - 0.5, "top margin");
        (offset + textView.Bounds.Height - bottom).Should().BeGreaterThanOrEqualTo(margin - 0.5, "bottom margin");
        window.Close();
    }

    /// <summary>
    /// A caret jump (e.g. a click in the preview) requested right after the tab was activated,
    /// before the editor is laid out. After layout the target line must be visible with a margin
    /// (regression: the line ended up at the very top edge, partially cut off).
    /// </summary>
    [AvaloniaFact]
    public void Caret_jump_requested_before_the_editor_is_laid_out_is_scrolled_into_view_with_a_margin()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();
        string paragraph = string.Concat(Enumerable.Repeat("Ala ma kota, a kot ma Alę i dużo innych słów. ", 12));
        string body = string.Concat(Enumerable.Range(0, 60).Select(i => "  <p>" + paragraph + "</p>\n\n"));
        html.SetText("<html>\n<body>\n" + body + "  <p class=\"target\">x</p>\n" + body + "</body>\n</html>");

        var tabModel = new TabManagerModel();
        OpenTab tab = tabModel.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { WordWrap = true };
        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        css.InitialLoad();
        var cssVm = new CodeTabViewModel(tabModel.OpenResource(css), new StatusBarService(), settings, spellChecker) { WordWrap = true };
        var view = new CodeTabView { DataContext = cssVm };
        var window = new Window { Width = 500, Height = 300, Content = view };
        int target = html.GetText().IndexOf("<p class=\"target\">", System.StringComparison.Ordinal);

        // The view is laid out for another tab first (e.g. a CSS file), then switched to the HTML
        // tab — the jump arrives together with the switch, before the new document is laid out.
        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        view.DataContext = vm;
        vm.GoToOffset(target);
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        TextView textView = editor.TextArea.TextView;
        double margin = textView.DefaultLineHeight;
        TextViewPosition position = new(editor.Document.GetLocation(target));
        double top = textView.GetVisualPosition(position, VisualYPosition.LineTop).Y;
        double bottom = textView.GetVisualPosition(position, VisualYPosition.LineBottom).Y;
        double offset = textView.VerticalOffset;

        // Assert
        editor.CaretOffset.Should().Be(target);
        (top - offset).Should().BeGreaterThanOrEqualTo(margin - 0.5, "top margin");
        (offset + textView.Bounds.Height - bottom).Should().BeGreaterThanOrEqualTo(margin - 0.5, "bottom margin");
        window.Close();
    }
}
