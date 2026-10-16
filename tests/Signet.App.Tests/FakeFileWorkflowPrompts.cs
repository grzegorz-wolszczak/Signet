using System.Collections.Generic;
using System.Threading.Tasks;
using Signet.App.Services;
using Signet.Core.BookManipulation;

namespace Signet.App.Tests;

/// <summary>Fake File menu dialogs for <see cref="FileWorkflow"/> / <c>MainWindowViewModel</c> tests.</summary>
public sealed class FakeFileWorkflowPrompts : IFileWorkflowPrompts
{
    /// <summary>Path returned from <see cref="AskOpenPathAsync"/> (or <c>null</c> = cancelled).</summary>
    public string? OpenPath { get; set; }

    /// <summary>Path returned from <see cref="AskSavePathAsync"/> (or <c>null</c> = cancelled).</summary>
    public string? SavePath { get; set; }

    /// <summary>Choice returned from <see cref="AskSaveChangesAsync"/>.</summary>
    public SaveChangesChoice SaveChoice { get; set; } = SaveChangesChoice.Discard;

    /// <summary>Layout returned from <see cref="DesignCustomLayoutAsync"/> (or <c>null</c> = cancelled).</summary>
    public IReadOnlyList<string>? CustomLayout { get; set; }

    /// <summary>Answer to the prompt about removing a missing file from the "Recent" list.</summary>
    public bool RemoveMissingRecent { get; set; } = true;

    /// <summary>The messages of the <see cref="ShowProgressAsync"/> calls.</summary>
    public List<string> ProgressMessages { get; } = new();

    /// <inheritdoc />
    public Task<T> ShowProgressAsync<T>(string message, System.Func<Task<T>> work)
    {
        ProgressMessages.Add(message);
        return work();
    }

    /// <summary>The answers to <see cref="AskFileInUseAsync"/>, in order (the dialog closed = <see cref="FileInUseChoice.Close"/>).</summary>
    public System.Collections.Generic.Queue<FileInUseChoice> InUseChoices { get; } = new();

    /// <summary>The file names <see cref="AskFileInUseAsync"/> was asked about.</summary>
    public List<string> FilesInUse { get; } = new();

    /// <summary>Number of <see cref="CloseApplication"/> calls.</summary>
    public int CloseApplicationCalls { get; private set; }

    /// <inheritdoc />
    public Task<FileInUseChoice> AskFileInUseAsync(string fileName)
    {
        FilesInUse.Add(fileName);
        return Task.FromResult(InUseChoices.Count > 0 ? InUseChoices.Dequeue() : FileInUseChoice.Close);
    }

    /// <inheritdoc />
    public void CloseApplication() => CloseApplicationCalls++;

    /// <summary>Number of <see cref="AskOpenPathAsync"/> calls.</summary>
    public int AskOpenPathCalls { get; private set; }

    /// <summary>Number of <see cref="AskSavePathAsync"/> calls.</summary>
    public int AskSavePathCalls { get; private set; }

    /// <summary>Number of <see cref="AskSaveChangesAsync"/> calls.</summary>
    public int AskSaveChangesCalls { get; private set; }

    /// <summary>The last error message.</summary>
    public string? LastError { get; private set; }

    /// <summary>Warnings from the last <see cref="ShowLoadWarningsAsync"/> call (file name, list).</summary>
    public (string FileName, IReadOnlyList<LoadWarning> Warnings)? LastLoadWarnings { get; private set; }

    /// <inheritdoc />
    public Task ShowLoadWarningsAsync(string fileName, IReadOnlyList<LoadWarning> warnings)
    {
        LastLoadWarnings = (fileName, warnings);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> AskOpenPathAsync(string startFolder)
    {
        AskOpenPathCalls++;
        return Task.FromResult(OpenPath);
    }

    /// <inheritdoc />
    public Task<string?> AskSavePathAsync(string suggestedPath)
    {
        AskSavePathCalls++;
        return Task.FromResult(SavePath);
    }

    /// <inheritdoc />
    public Task<SaveChangesChoice> AskSaveChangesAsync()
    {
        AskSaveChangesCalls++;
        return Task.FromResult(SaveChoice);
    }

    /// <inheritdoc />
    public Task ShowErrorAsync(string title, string message)
    {
        LastError = message;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ConfirmRemoveMissingRecentAsync(string path) => Task.FromResult(RemoveMissingRecent);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>?> DesignCustomLayoutAsync(string version) => Task.FromResult(CustomLayout);
}
