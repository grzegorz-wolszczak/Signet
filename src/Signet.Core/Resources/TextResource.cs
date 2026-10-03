using System;
using System.IO;

namespace Signet.Core.Resources;

/// <summary>
/// Base class for text resources (CSS, SVG, XML, HTML, NCX, OPF, Misc). Keeps the content in memory,
/// loads it from disk on demand and saves it as UTF-8 without BOM.
/// </summary>
public class TextResource : Resource
{
    private string _text = string.Empty;

    /// <inheritdoc cref="Resource(string, string)"/>
    public TextResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <summary>Whether the content has already been loaded (from disk or set via <see cref="SetText"/>).</summary>
    public bool IsLoaded { get; private set; }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Text;

    /// <summary>Returns the resource content (an empty string when nothing has been loaded).</summary>
    public virtual string GetText()
    {
        Lock.EnterReadLock();
        try
        {
            return _text;
        }
        finally
        {
            Lock.ExitReadLock();
        }
    }

    /// <summary>Sets the resource content and raises <see cref="Resource.Modified"/>.</summary>
    public virtual void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Lock.EnterWriteLock();
        try
        {
            _text = text;
            IsLoaded = true;
            OnTextChanged();
        }
        finally
        {
            Lock.ExitWriteLock();
        }

        RaiseModified();
    }

    /// <summary>
    /// Called inside the write lock right after <see cref="SetText"/> changes the content,
    /// before <see cref="Resource.Modified"/> is raised. Subclasses invalidate their caches here.
    /// </summary>
    protected virtual void OnTextChanged()
    {
    }

    /// <summary>
    /// Loads the content from disk if nothing has been loaded yet and the file exists
    /// (lazy, on-demand loading).
    /// </summary>
    public void InitialLoad()
    {
        Lock.EnterWriteLock();
        try
        {
            if (!IsLoaded && _text.Length == 0 && File.Exists(FullPath))
            {
                _text = ReadTextFromDisk(FullPath);
                IsLoaded = true;
            }
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    /// <inheritdoc/>
    public override void SaveToDisk(bool bookWideSave = false)
    {
        Lock.EnterWriteLock();
        try
        {
            if (!IsLoaded)
            {
                return;
            }

            Utility.WriteUnicodeTextFile(_text, FullPath);
        }
        finally
        {
            Lock.ExitWriteLock();
        }

        if (!bookWideSave)
        {
            RaiseResourceUpdatedOnDisk();
        }

        base.SaveToDisk(bookWideSave);
    }

    /// <inheritdoc/>
    protected override bool LoadFromDisk()
    {
        string text;
        try
        {
            text = ReadTextFromDisk(FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        SetText(text);
        return true;
    }

    /// <summary>
    /// Reads the file from disk with encoding detection. (X)HTML/XML subclasses override this to use
    /// <see cref="HtmlEncodingResolver"/>; the base uses <see cref="Utility.ReadUnicodeTextFile"/>.
    /// </summary>
    protected virtual string ReadTextFromDisk(string fullFilePath) =>
        Utility.ReadUnicodeTextFile(fullFilePath);
}
