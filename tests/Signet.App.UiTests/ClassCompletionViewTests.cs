using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views.Tabs;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The popup of CSS class names in Code Editor: it opens on demand, lists the classes and goes away.</summary>
public sealed class ClassCompletionViewTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (Window Window, CodeTabViewModel Vm, TextEditor Editor) Show(string marked)
    {
        string root = _disposables.Add(new TempDir("signet-uitests-")).Path;
        SettingsStore settings = new(System.IO.Path.Combine(root, "settings.xml"));
        SpellChecker spellChecker = new(
            settings, System.IO.Path.Combine(root, "hunspell_dictionaries"), System.IO.Path.Combine(root, "user_dictionaries"));
        string path = System.IO.Path.Combine(root, "Section0001.xhtml");
        int caret = marked.IndexOf('|', StringComparison.Ordinal);
        System.IO.File.WriteAllText(path, marked.Remove(caret, 1));
        HtmlResource html = new(root, path);
        html.InitialLoad();

        CodeTabViewModel vm = new(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker)
        {
            BookFiles = () => Array.Empty<LinkCompletionFile>(),
            DocumentTextOf = bookPath => bookPath == "style.css" ? ".note { x: y } .notice { x: y } .lead { x: y }" : null,
        };
        Window window = new() { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TextEditor editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.CaretOffset = caret;
        Dispatcher.UIThread.RunJobs();
        return (window, vm, editor);
    }

    // The popup of the view (a private field: the popup is a window of its own, which is not under the editor in the tree).
    private static CompletionWindow? OpenPopupOf(Window window) =>
        (CompletionWindow?)typeof(CodeTabView)
            .GetField("_completionWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(window.Content);

    [AvaloniaFact]
    public void Asking_for_completions_outside_a_class_attribute_opens_nothing_and_does_not_fail()
    {
        // Arrange
        (Window window, CodeTabViewModel vm, TextEditor editor) = Show("<html><body><p>te|xt</p></body></html>");

        // Act
        Action ask = vm.RequestCompletion;

        // Assert
        ask.Should().NotThrow();
        Dispatcher.UIThread.RunJobs();
        OpenPopupOf(window).Should().BeNull();
        window.Close();
    }

    [AvaloniaFact]
    public void Asking_for_completions_in_a_class_attribute_opens_the_list_of_the_classes()
    {
        // Arrange
        (Window window, CodeTabViewModel vm, TextEditor editor) = Show(
            "<html><head><link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/></head><body><p class=\"lead no|\">text</p></body></html>");

        // Act
        vm.RequestCompletion();
        Dispatcher.UIThread.RunJobs();

        // Assert
        CompletionWindow popup = OpenPopupOf(window).Should().NotBeNull().And.Subject.As<CompletionWindow>();
        popup.CompletionList.CompletionData.Select(d => d.Text).Should().Equal("note", "notice");
        popup.Close();
        window.Close();
    }
}
