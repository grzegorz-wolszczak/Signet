using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using AwesomeAssertions;
using Signet.App.Actions;
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
/// Headless render test of the Code View tab: the <c>TextEditor</c> control is hosted on its own
/// (outside <c>DocumentDock</c>, which hangs tab rendering under headless; see
/// <see cref="WindowRenderTests"/>).
/// </summary>
public sealed class CodeTabViewRenderTests
{
    private static (SettingsStore Settings, SpellChecker SpellChecker) NewSpellChecker()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"signet-uitests-{System.Guid.NewGuid():N}");
        SettingsStore settings = new(System.IO.Path.Combine(root, "settings.json"));
        SpellChecker spellChecker = new(
            settings,
            System.IO.Path.Combine(root, "hunspell_dictionaries"),
            System.IO.Path.Combine(root, "user_dictionaries"));
        return (settings, spellChecker);
    }

    [AvaloniaFact]
    public void Code_tab_view_renders_the_editor_with_the_resource_text()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        var tabModel = new TabManagerModel();
        OpenTab tab = tabModel.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
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
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        var tabModel = new TabManagerModel();
        OpenTab tab = tabModel.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);

        // Force a remembered well-formed error (an unclosed tag).
        vm.Document.Text = "<p>oops";
        vm.RunWellFormedCheck();
        vm.WellFormedError.Should().NotBeNull();

        var view = new CodeTabView { DataContext = vm };
        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();

        // Reproduce the race: the document disappears from the control, then a WellFormedError
        // change notification arrives (as when Dock re-materializes the tab).
        editor.Document = null;

        vm.Invoking(x => x.RunWellFormedCheck()).Should().NotThrow();
    }

    private static (Window Window, CodeTabViewModel Vm) ShowCodeTab(string xhtml, bool extended = true)
    {
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        settings.CodeViewExtendedHighlighting = extended;
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"signet-uitests-{System.Guid.NewGuid():N}");
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

    // Color of the first visible text element of the given line (after colorizing).
    private static Color ForegroundOfFirstElement(Window window, int lineNumber)
    {
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        VisualLine line = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(lineNumber));
        VisualLineElement element = line.Elements.First(e => e.DocumentLength > 0);
        return ((ISolidColorBrush)element.TextRunProperties.ForegroundBrush!).Color;
    }

    [AvaloniaFact]
    public void Html_is_coloured_with_the_line_highlighter_and_CodeViewAppearance_colours()
    {
        (Window window, _) = ShowCodeTab("<p class=\"x\">a</p>\n<!-- c -->", extended: false);
        CodeViewAppearance colours = CodeViewAppearance.LightDefault;

        ForegroundOfFirstElement(window, 1).Should().Be(Color.Parse(colours.XhtmlHtmlColor));
        ForegroundOfFirstElement(window, 2).Should().Be(Color.Parse(colours.XhtmlHtmlCommentColor));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Opening_a_comment_recolours_the_following_lines_after_the_edit(bool extended)
    {
        (Window window, CodeTabViewModel vm) = ShowCodeTab("<p>a</p>\n<p>b</p>\n<p>c</p>", extended);
        CodeViewAppearance colours = CodeViewAppearance.LightDefault;
        ForegroundOfFirstElement(window, 3).Should().Be(Color.Parse(colours.XhtmlHtmlColor));

        vm.Document.Insert(0, "<!-- ");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        ForegroundOfFirstElement(window, 3).Should().Be(Color.Parse(colours.XhtmlHtmlCommentColor),
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
        const string line1 = "<p><b>bold</b> <i>it</i></p>";
        const string line2 = "<style>p { color: red }</style>";
        (Window window, _) = ShowCodeTab(line1 + "\n" + line2);
        CodeViewAppearance colours = CodeViewAppearance.LightDefault;

        Typeface bold = ElementAt(window, 1, line1.IndexOf("bold", System.StringComparison.Ordinal)).TextRunProperties.Typeface;
        Typeface italic = ElementAt(window, 1, line1.IndexOf("it<", System.StringComparison.Ordinal)).TextRunProperties.Typeface;
        bold.Weight.Should().Be(FontWeight.Bold);
        italic.Style.Should().Be(FontStyle.Italic);

        VisualLineElement red = ElementAt(window, 2, line2.IndexOf("red", System.StringComparison.Ordinal));
        ((ISolidColorBrush)red.TextRunProperties.ForegroundBrush!).Color.Should().Be(Color.Parse(colours.CssConstantColor));
    }

    [AvaloniaFact]
    public void Without_extended_highlighting_style_content_keeps_a_single_colour()
    {
        const string text = "<style>p { color: red }</style>";
        (Window window, _) = ShowCodeTab(text, extended: false);

        VisualLineElement red = ElementAt(window, 1, text.IndexOf("red", System.StringComparison.Ordinal));
        ((ISolidColorBrush)red.TextRunProperties.ForegroundBrush!).Color
            .Should().Be(Color.Parse(CodeViewAppearance.LightDefault.XhtmlCssColor));
    }

    [AvaloniaFact]
    public void Syntax_errors_of_drawn_lines_are_remembered_for_the_squiggle_and_tooltip()
    {
        (Window window, _) = ShowCodeTab("<p>a < b</p>");
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        IVisualLineTransformer colorizer = editor.TextArea.TextView.LineTransformers.Single(t => t.GetType().Name.StartsWith("LineStateSyntaxColorizer", System.StringComparison.Ordinal));

        var issues = (System.Collections.Generic.IReadOnlyList<SyntaxSpan>)colorizer.GetType()
            .GetMethod("IssuesOf")!.Invoke(colorizer, new object[] { editor.Document.GetLineByNumber(1) })!;

        issues.Should().ContainSingle(s => s.Issue == SyntaxIssue.UnescapedLessThan && s.Start == 5);
    }

    [AvaloniaFact]
    public void Typing_lt_slash_in_the_editor_closes_the_open_element()
    {
        (Window window, CodeTabViewModel vm) = ShowCodeTab("<p>abc<");
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.TextArea.Focus();
        editor.CaretOffset = vm.Document.TextLength;

        window.KeyTextInput("/");
        Dispatcher.UIThread.RunJobs();

        vm.Document.Text.Should().Be("<p>abc</p>");
        editor.CaretOffset.Should().Be("<p>abc</p>".Length);
    }

    /// <summary>
    /// Regression: the Code View font family and size from Preferences (Appearance → Code View)
    /// must reach the editor. <c>CodeTabView</c> used to hardcode the size and family, so the
    /// setting was saved to the file but had no effect.
    /// </summary>
    [AvaloniaFact]
    public void Code_view_font_family_and_size_come_from_the_appearance_settings()
    {
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        settings.CodeViewAppearance = settings.CodeViewAppearance with
        {
            FontFamily = "Consolas",
            FontSize = 21,
        };

        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"signet-uitests-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "Section0001.xhtml");
        System.IO.File.WriteAllText(path, "<p>a</p>");
        HtmlResource html = new(dir, path);
        html.InitialLoad();

        var vm = new CodeTabViewModel(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker);
        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.FontSize.Should().Be(21);
        editor.FontFamily.Name.Should().Be("Consolas");

        // Zoom scales the size from the settings, not a hardcoded base value.
        vm.ZoomFactor = 2.0;
        Dispatcher.UIThread.RunJobs();
        editor.FontSize.Should().Be(42);
    }

    [AvaloniaFact]
    public void Typing_in_href_shows_file_completions_and_enter_inserts_the_best_match()
    {
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

        window.KeyTextInput("c");
        window.KeyTextInput("t");
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        vm.Document.Text.Should().Be("<p><a href=\"chapter-two.xhtml</p>");
    }

    /// <summary>
    /// Regression: a right click in Code View showed no menu when there was no misspelled word
    /// under the caret (the only context menu was the spelling menu, cancelled in that case).
    /// The menu now always has the full default layout.
    /// </summary>
    [AvaloniaFact]
    public void Context_menu_opens_with_the_default_layout_without_a_misspelled_word()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
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
        editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        menu.IsOpen.Should().BeTrue();
        string[] headers = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToArray();
        headers.Should().Equal(
            Signet.App.Resources.Strings.Get("CodeViewMenu_Clips"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_AddToClips"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_ToggleLineWrapMode"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_GoToLinkOrStyle"),
            CodeTabViewModel.MergeContentTextFor(null),
            Signet.App.Resources.Strings.Get("CodeViewMenu_MarkSelectedText"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_ReformatHtml"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_Undo"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_Redo"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_Cut"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_Copy"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_Paste"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_Delete"),
            Signet.App.Resources.Strings.Get("CodeViewMenu_SelectAll"));
        menu.Close();
    }

    /// <summary>"Rename Class…" appears when the caret is on a class name in XHTML.</summary>
    [AvaloniaFact]
    public void Context_menu_offers_rename_class_when_the_caret_is_on_a_class_name()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
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
        editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        ContextMenu menu = editor.ContextMenu!;
        menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!)
            .Should().Contain(Signet.App.Resources.Strings.Get("CodeViewMenu_RenameClass"));
        menu.Close();
    }

    /// <summary>
    /// The class items run application actions, so they show the actions' current shortcuts (like the main menu);
    /// an action without a shortcut shows none.
    /// </summary>
    [AvaloniaFact]
    public void Class_items_of_the_context_menu_show_the_shortcuts_of_their_actions()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        html.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        FakeHost host = new();
        host.Gestures[AppActionIds.FindUsages] = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.F7, Avalonia.Input.KeyModifiers.Alt);
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = host };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
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
        Item("CodeViewMenu_FindUsages").InputGesture.Should().Be(host.Gestures[AppActionIds.FindUsages]);
        Item("CodeViewMenu_RenameClass").InputGesture.Should().BeNull("Rename Class has no shortcut by default");
        menu.Close();
    }

    /// <summary>
    /// "Rename Class…" in a CSS stylesheet: the caret is on a class in a selector, with no selection
    /// needed.
    /// </summary>
    [AvaloniaFact]
    public void Context_menu_offers_rename_class_in_a_stylesheet_selector()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        css.InitialLoad();

        OpenTab tab = new TabManagerModel().OpenResource(css);
        (SettingsStore settings, SpellChecker spellChecker) = NewSpellChecker();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker) { Host = new FakeHost() };

        var window = new Window { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.Document.Text = "p.note { color: red }";
        editor.CaretOffset = 4;
        Dispatcher.UIThread.RunJobs();
        editor.TextArea.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        ContextMenu menu = editor.ContextMenu!;
        menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!)
            .Should().Contain(Signet.App.Resources.Strings.Get("CodeViewMenu_RenameClass"));
        menu.Close();
    }

    /// <summary>
    /// "Merge Content": the item is always present; it is enabled, showing the element name and
    /// count, when the selection spans adjacent elements of the same kind.
    /// </summary>
    [AvaloniaFact]
    public void Context_menu_always_shows_merge_content_enabled_only_for_mergeable_selection()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
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

        editor.Select(start, end - start);
        MenuItem enabled = MergeItem();
        enabled.Header.Should().Be(Signet.App.Resources.Strings.Format("CodeViewMenu_MergeContentCount", "p", 2));
        enabled.IsEnabled.Should().BeTrue();

        editor.Select(start + 3, 1); // "a": both ends in one paragraph
        MenuItem disabled = MergeItem();
        disabled.Header.Should().Be(CodeTabViewModel.MergeContentTextFor(null));
        disabled.IsEnabled.Should().BeFalse();
    }

    private sealed class FakeHost : ICodeTabHost
    {
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

        public System.Threading.Tasks.Task<string?> ReformatHtmlTextAsync(Resource resource, string text, bool toValid) =>
            System.Threading.Tasks.Task.FromResult<string?>(null);

        public System.Threading.Tasks.Task<Signet.Core.BookManipulation.ClassRenamer?> PrepareClassRenameAsync() =>
            System.Threading.Tasks.Task.FromResult<Signet.Core.BookManipulation.ClassRenamer?>(null);

        public void ApplyClassRename(Signet.Core.BookManipulation.ClassRenameResult result)
        {
        }

        public int CountSameSpansInBook(HtmlResource html, string text, int offset) => 0;

        public Signet.Core.BookManipulation.SpanRemovalPlan? PlanSpanRemoval(
            HtmlResource html, string text, int offset, Signet.Core.BookManipulation.SpanRemovalScope scope) => null;

        public void ApplySpanRemovalInBook(Signet.Core.BookManipulation.SpanRemovalPlan plan)
        {
        }

        public void ViewImage(string bookPath)
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
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
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
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
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
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
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

        editor.CaretOffset.Should().Be(target);
        (top - offset).Should().BeGreaterThanOrEqualTo(margin - 0.5, "top margin");
        (offset + textView.Bounds.Height - bottom).Should().BeGreaterThanOrEqualTo(margin - 0.5, "bottom margin");
        window.Close();
    }
}
