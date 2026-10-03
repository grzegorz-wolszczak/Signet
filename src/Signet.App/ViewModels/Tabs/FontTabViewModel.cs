using System.Globalization;
using System;
using Avalonia.Media.Imaging;
using Signet.App.Imaging;
using Signet.App.Resources;
using Signet.Core.BookManipulation;
using Signet.Core.Fonts;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core;

namespace Signet.App.ViewModels.Tabs;

/// <summary>Font obfuscation method in the UI (the "Font Obfuscation" menu).</summary>
public enum FontObfuscationMethod
{
    /// <summary>No obfuscation.</summary>
    None,

    /// <summary>The IDPF method (<c>http://www.idpf.org/2008/embedding</c>).</summary>
    Idpf,

    /// <summary>The Adobe method (<c>http://ns.adobe.com/pdf/enc#RC</c>).</summary>
    Adobe,
}

/// <summary>
/// Typeface preview tab: font metadata (family, subfamily, file size), a sample preview
/// rendered from the font file, and an obfuscation method switch.
/// </summary>
public sealed class FontTabViewModel : ContentTabViewModel
{
    private readonly FontResource _font;
    private readonly Book? _book;

    private FontFileInfo _info;
    private Bitmap? _preview;
    private bool _previewDark;

    /// <summary>Creates the typeface preview tab.</summary>
    public FontTabViewModel(OpenTab tab, Book? book)
        : base(tab)
    {
        _book = book;

        if (tab.Resource is not FontResource font)
        {
            throw new ArgumentException(
                $"FontTab requires a font resource, got {tab.Resource.GetType().Name}.", nameof(tab));
        }

        _font = font;
        _font.ResourceUpdatedOnDisk += (_, _) => LoadMetadata();
        LoadMetadata();
    }

    /// <summary>Font family (nameID 1), or the file name when it could not be read.</summary>
    public string FamilyName =>
        _info.Family.Length > 0 ? _info.Family : _font.Filename;

    /// <summary>Subfamily / style (nameID 2), e.g. <c>Regular</c> / <c>Bold Italic</c>.</summary>
    public string Subfamily =>
        _info.Subfamily.Length > 0 ? _info.Subfamily : "—";

    /// <summary>Info line: file name, size, MIME type, version.</summary>
    public string InfoLine
    {
        get
        {
            string size = string.Create(CultureInfo.InvariantCulture, $"{(FileSizeBytes + 512) / 1024} KB");
            string version = _info.Version.Length > 0 ? $" | {_info.Version}" : string.Empty;
            return $"{_font.Filename} | {size} | {_font.MediaType}{version}";
        }
    }

    /// <summary>Font file size in bytes.</summary>
    public long FileSizeBytes => System.IO.File.Exists(_font.FullPath)
        ? new System.IO.FileInfo(_font.FullPath).Length
        : 0;

    /// <summary>Bitmap with the typeface sample, or <c>null</c> when SkiaSharp could not load the font.</summary>
    public Bitmap? Preview => _preview;

    /// <summary>Whether a preview was rendered for this font.</summary>
    public bool HasPreview => _preview is not null;

    /// <summary>The font's current obfuscation method.</summary>
    public FontObfuscationMethod ObfuscationMethod
    {
        get => _font.ObfuscationAlgorithm switch
        {
            OcfReader.IdpfFontAlgorithmId => FontObfuscationMethod.Idpf,
            OcfReader.AdobeFontAlgorithmId => FontObfuscationMethod.Adobe,
            _ => FontObfuscationMethod.None,
        };
        set
        {
            if (value == ObfuscationMethod)
            {
                return;
            }

            string algorithm = value switch
            {
                FontObfuscationMethod.Idpf => OcfReader.IdpfFontAlgorithmId,
                FontObfuscationMethod.Adobe => OcfReader.AdobeFontAlgorithmId,
                _ => string.Empty,
            };

            if (_book is not null)
            {
                _book.SetFontObfuscation(_font, algorithm);
            }
            else
            {
                _font.ObfuscationAlgorithm = algorithm;
            }

            OnPropertyChanged(nameof(ObfuscationMethod));
            OnPropertyChanged(nameof(ObfuscationStatus));
        }
    }

    /// <summary>Description of the obfuscation method for the info bar.</summary>
    public string ObfuscationStatus => ObfuscationMethod switch
    {
        FontObfuscationMethod.Idpf => Strings.Format("FontTab_Obfuscation", "IDPF"),
        FontObfuscationMethod.Adobe => Strings.Format("FontTab_Obfuscation", "Adobe"),
        _ => Strings.Format("FontTab_Obfuscation", Strings.Get("FontTab_ObfuscationNone")),
    };

    /// <summary>Switches the preview color variant (light / dark) and re-renders the sample.</summary>
    public void SetPreviewTheme(bool dark)
    {
        if (dark == _previewDark && _preview is not null)
        {
            return;
        }

        _previewDark = dark;
        RenderPreview();
    }

    /// <inheritdoc />
    public override void Reload() => LoadMetadata();

    private void LoadMetadata()
    {
        _info = _font.GetFontInfo();
        RenderPreview();

        OnPropertyChanged(nameof(FamilyName));
        OnPropertyChanged(nameof(Subfamily));
        OnPropertyChanged(nameof(InfoLine));
        OnPropertyChanged(nameof(ObfuscationMethod));
        OnPropertyChanged(nameof(ObfuscationStatus));
    }

    private void RenderPreview()
    {
        _preview?.Dispose();
        _preview = FontPreviewRenderer.Render(_font.FullPath, _previewDark);
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(HasPreview));
    }
}
