using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Semantics;

namespace Signet.App.ViewModels;

/// <summary>What Tools → Accessibility does to the files that name no language.</summary>
public enum HtmlLanguageChoice
{
    /// <summary>Leave the language of the files as it is.</summary>
    Keep,

    /// <summary>Give the main language of the book to the files that name none.</summary>
    OnlyMissing,

    /// <summary>Give the main language of the book to every file, replacing what it had.</summary>
    All,
}

/// <summary>Everything the "Accessibility" dialog saves (<see cref="AccessibilityViewModel.BuildRequest"/>).</summary>
/// <param name="Languages">The new <c>dc:language</c> values (main language first), or <c>null</c> when they did not change.</param>
/// <param name="HtmlLanguage">What to do with the language of the (X)HTML files.</param>
/// <param name="ImageEdits">The images whose description or decoration changed.</param>
/// <param name="Metadata">The accessibility metadata (EPUB 3), or <c>null</c> when it did not change.</param>
public sealed record AccessibilitySaveRequest(
    IReadOnlyList<string>? Languages,
    HtmlLanguageChoice HtmlLanguage,
    IReadOnlyList<ImageDescriptionEdit> ImageEdits,
    AccessibilityMetadataValues? Metadata)
{
    /// <summary>Whether there is nothing to write.</summary>
    public bool IsEmpty => Languages is null && HtmlLanguage == HtmlLanguageChoice.Keep && ImageEdits.Count == 0 && Metadata is null;
}

/// <summary>A language of the book in the "Language" tab.</summary>
/// <param name="Code">The language code (<c>pl</c>, <c>en-GB</c>).</param>
/// <param name="Name">Its name in the UI language, or the code when it is not one the application knows.</param>
public sealed record AccessibilityLanguageItem(string Code, string Name)
{
    /// <summary>The text of the list: <c>pl — Polish</c>.</summary>
    public string Display => Name.Length > 0 && !string.Equals(Name, Code, StringComparison.Ordinal) ? $"{Code} — {Name}" : Code;
}

/// <summary>An image of the "Image descriptions" tab: its description and decoration state are editable.</summary>
public sealed partial class AccessibilityImageItem : ObservableObject
{
    private readonly Func<string?, Bitmap?>? _loadPreview;
    private Bitmap? _preview;
    private bool _previewLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    [NotifyPropertyChangedFor(nameof(NeedsDescription))]
    private string _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    [NotifyPropertyChangedFor(nameof(NeedsDescription))]
    private bool _isDecorative;

    internal AccessibilityImageItem(AccessibilityImage image, Func<string?, Bitmap?>? loadPreview)
    {
        Image = image;
        _loadPreview = loadPreview;
        _description = image.Description;
        _isDecorative = image.IsDecorative;
        FileName = image.BookPath[(image.BookPath.LastIndexOf('/') + 1)..];
    }

    /// <summary>The image as the book has it.</summary>
    public AccessibilityImage Image { get; }

    /// <summary>The (X)HTML file of the image, without the folder.</summary>
    public string FileName { get; }

    /// <summary>"page1.xhtml:42 — img" (the place and kind).</summary>
    public string Place => $"{FileName}:{Image.Line.ToString(CultureInfo.CurrentCulture)} — {(Image.Kind == AccessibilityImageKind.Img ? "img" : "svg")}";

    /// <summary>The source of an <c>img</c> (the file name of the image), or the opening tag of an inline svg.</summary>
    public string Subject => Image.Source is { } source ? source[(source.LastIndexOf('/') + 1)..] : Image.Snippet;

    /// <summary>The preview of an <c>img</c> (loaded when first asked for); <c>null</c> for an svg or an image that cannot be shown.</summary>
    public Bitmap? Preview
    {
        get
        {
            if (!_previewLoaded)
            {
                _previewLoaded = true;
                _preview = _loadPreview?.Invoke(Image.Source);
            }

            return _preview;
        }
    }

    /// <summary>Whether the description or the decoration mark differs from what the book has.</summary>
    public bool IsChanged => IsDecorative != Image.IsDecorative || (!IsDecorative && !string.Equals(Description.Trim(), Image.Description, StringComparison.Ordinal));

    /// <summary>Whether the image still has no text alternative and is not decoration (what Check Book would report).</summary>
    public bool NeedsDescription => !IsDecorative && Description.Trim().Length == 0;

    /// <summary>The edit to write for this image.</summary>
    internal ImageDescriptionEdit ToEdit() => new(Image.BookPath, Image.Offset, Description.Trim(), IsDecorative);

    internal void DisposePreview()
    {
        _preview?.Dispose();
        _preview = null;
        _previewLoaded = true;
    }
}

/// <summary>A value of a checklist of the "Accessibility metadata" tab.</summary>
public sealed partial class AccessibilityChoiceItem : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    internal AccessibilityChoiceItem(string value, string displayName, bool isChecked)
    {
        Value = value;
        DisplayName = displayName;
        _isChecked = isChecked;
    }

    /// <summary>The value written to the OPF (<c>alternativeText</c>).</summary>
    public string Value { get; }

    /// <summary>The name shown to the user, in the UI language.</summary>
    public string DisplayName { get; }
}

/// <summary>
/// The "Accessibility" dialog (Tools → Accessibility; Calibre's tool of that name): the languages of the book and of its files, the text
/// alternatives of its images, and the accessibility metadata (EPUB 3). "Save" writes it all in one step behind a checkpoint.
/// </summary>
public sealed partial class AccessibilityViewModel : ObservableObject, IDisposable
{
    private readonly Book _book;
    private readonly Action<string, int> _navigate;
    private readonly Func<AccessibilitySaveRequest, Task<bool>> _save;
    private readonly List<AccessibilityImageItem> _allImages;
    private readonly string _savedLanguages;
    private readonly string _savedMetadata;
    private bool _isBookOperationBusy;
    private readonly bool _initialized;

    /// <summary>Creates the model for <paramref name="book"/>.</summary>
    /// <param name="book">The book (read here, written by <paramref name="save"/>).</param>
    /// <param name="navigate">Opens a file (book path) at an offset in the main window.</param>
    /// <param name="save">Writes the request; returns whether it was done.</param>
    public AccessibilityViewModel(Book book, Action<string, int> navigate, Func<AccessibilitySaveRequest, Task<bool>> save)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        IsEpub3 = book.IsEpub3;

        foreach (string code in AccessibilityLanguage.GetLanguages(book))
        {
            Languages.Add(ItemOf(code));
        }

        _savedLanguages = LanguagesKey();
        FilesWithoutLanguage = AccessibilityLanguage.CountFilesWithoutLanguage(book);

        _allImages = AccessibilityImages.List(book).Select(i => new AccessibilityImageItem(i, LoadPreview)).ToList();
        foreach (AccessibilityImageItem item in _allImages)
        {
            item.PropertyChanged += (_, _) => OnImageChanged();
        }

        RefreshVisibleImages();

        if (IsEpub3)
        {
            AccessibilityMetadataValues values = AccessibilityMetadataEditor.Read(book);
            Summary = values.Summary;
            Fill(AccessModes, "Mode", AccessibilityVocabulary.AccessModes, values.AccessModes);
            Fill(AccessModesSufficient, "Mode", AccessibilityVocabulary.AccessModesSufficient, values.AccessModesSufficient);
            Fill(Features, "Feature", AccessibilityVocabulary.Features, values.Features);
            Fill(Hazards, "Hazard", AccessibilityVocabulary.Hazards, values.Hazards);
            Fill(ConformsTo, string.Empty, AccessibilityVocabulary.ConformsTo, values.ConformsTo);
        }

        _savedMetadata = MetadataKey();
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
        AddLanguageCommand = new RelayCommand(AddLanguage, () => CanAddLanguage);
        RemoveLanguageCommand = new RelayCommand<AccessibilityLanguageItem>(RemoveLanguage);
        MakeMainLanguageCommand = new RelayCommand<AccessibilityLanguageItem>(MakeMain);
        Languages.CollectionChanged += (_, _) => OnStateChanged();
        _initialized = true;
    }

    /// <summary>Whether the book is EPUB 3 (the "Accessibility metadata" tab exists).</summary>
    public bool IsEpub3 { get; }

    // --- language ---

    /// <summary>The languages of the book; the first is the main language.</summary>
    public ObservableCollection<AccessibilityLanguageItem> Languages { get; } = new();

    /// <summary>The names of the languages the application knows, for the box in which a language is chosen.</summary>
    public IReadOnlyList<string> KnownLanguageNames { get; } = Language.GetSortedPrimaryLanguageNames();

    /// <summary>The language being added: a code (<c>pl</c>) or the name of a language.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddLanguage))]
    private string _newLanguage = string.Empty;

    /// <summary>What happens to the language of the (X)HTML files when the dialog is saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private HtmlLanguageChoice _htmlLanguage;

    /// <summary>The "leave as it is" option of <see cref="HtmlLanguage"/>, for a radio button.</summary>
    public bool HtmlLanguageKeep
    {
        get => HtmlLanguage == HtmlLanguageChoice.Keep;
        set => ChooseHtmlLanguage(value, HtmlLanguageChoice.Keep);
    }

    /// <summary>The "only the files that have none" option of <see cref="HtmlLanguage"/>, for a radio button.</summary>
    public bool HtmlLanguageOnlyMissing
    {
        get => HtmlLanguage == HtmlLanguageChoice.OnlyMissing;
        set => ChooseHtmlLanguage(value, HtmlLanguageChoice.OnlyMissing);
    }

    /// <summary>The "all files" option of <see cref="HtmlLanguage"/>, for a radio button.</summary>
    public bool HtmlLanguageAll
    {
        get => HtmlLanguage == HtmlLanguageChoice.All;
        set => ChooseHtmlLanguage(value, HtmlLanguageChoice.All);
    }

    private void ChooseHtmlLanguage(bool selected, HtmlLanguageChoice choice)
    {
        if (selected)
        {
            HtmlLanguage = choice;
        }
    }

    /// <summary>How many (X)HTML files name no language.</summary>
    public int FilesWithoutLanguage { get; }

    /// <summary>"3 files have no language".</summary>
    public string FilesWithoutLanguageText => Strings.Format("AccessibilityWindow_FilesWithoutLanguage", FilesWithoutLanguage.ToString(CultureInfo.CurrentCulture));

    /// <summary>Whether the language being added is something that can be added.</summary>
    public bool CanAddLanguage => ResolveLanguage(NewLanguage) is { } code && Languages.All(l => !string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds <see cref="NewLanguage"/> to the languages.</summary>
    public IRelayCommand AddLanguageCommand { get; }

    /// <summary>Removes a language (not the last one).</summary>
    public IRelayCommand<AccessibilityLanguageItem> RemoveLanguageCommand { get; }

    /// <summary>Makes a language the main one (the first).</summary>
    public IRelayCommand<AccessibilityLanguageItem> MakeMainLanguageCommand { get; }

    // --- images ---

    /// <summary>The images shown (all of them, or only the ones that still need a description).</summary>
    public ObservableCollection<AccessibilityImageItem> Images { get; } = new();

    /// <summary>Whether only the images that still need a description are shown.</summary>
    [ObservableProperty]
    private bool _showOnlyUndescribed;

    /// <summary>Whether the book has any image.</summary>
    public bool HasImages => _allImages.Count > 0;

    /// <summary>"12 of 40 images have no description".</summary>
    public string ImagesSummary => Strings.Format(
        "AccessibilityWindow_ImagesSummary",
        _allImages.Count(i => i.NeedsDescription).ToString(CultureInfo.CurrentCulture),
        _allImages.Count.ToString(CultureInfo.CurrentCulture));

    // --- metadata ---

    /// <summary>The accessibility summary.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _summary = string.Empty;

    /// <summary><c>schema:accessMode</c>.</summary>
    public ObservableCollection<AccessibilityChoiceItem> AccessModes { get; } = new();

    /// <summary><c>schema:accessModeSufficient</c>.</summary>
    public ObservableCollection<AccessibilityChoiceItem> AccessModesSufficient { get; } = new();

    /// <summary><c>schema:accessibilityFeature</c>.</summary>
    public ObservableCollection<AccessibilityChoiceItem> Features { get; } = new();

    /// <summary><c>schema:accessibilityHazard</c>.</summary>
    public ObservableCollection<AccessibilityChoiceItem> Hazards { get; } = new();

    /// <summary><c>dcterms:conformsTo</c>.</summary>
    public ObservableCollection<AccessibilityChoiceItem> ConformsTo { get; } = new();

    // --- state ---

    /// <summary>Whether something was changed since the dialog was opened.</summary>
    public bool HasUnsavedChanges => !BuildRequest().IsEmpty;

    /// <summary>Whether another operation on the book is running elsewhere (set by the host window).</summary>
    public bool IsBookOperationBusy
    {
        get => _isBookOperationBusy;
        set
        {
            if (SetProperty(ref _isBookOperationBusy, value))
            {
                OnPropertyChanged(nameof(CanSave));
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Whether "Save" can run: something changed and nothing else is working on the book.</summary>
    public bool CanSave => HasUnsavedChanges && !IsBookOperationBusy;

    /// <summary>"Save" — writes everything and closes the dialog (<see cref="Done"/>).</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>Raised when the dialog was saved (the window closes).</summary>
    public event EventHandler? Done;

    /// <summary>What "Save" would write (empty when nothing changed).</summary>
    public AccessibilitySaveRequest BuildRequest()
    {
        IReadOnlyList<string>? languages = string.Equals(LanguagesKey(), _savedLanguages, StringComparison.Ordinal)
            ? null
            : Languages.Select(l => l.Code).ToList();
        AccessibilityMetadataValues? metadata = IsEpub3 && !string.Equals(MetadataKey(), _savedMetadata, StringComparison.Ordinal)
            ? new AccessibilityMetadataValues
            {
                Summary = Summary,
                AccessModes = Checked(AccessModes),
                AccessModesSufficient = Checked(AccessModesSufficient),
                Features = Checked(Features),
                Hazards = Checked(Hazards),
                ConformsTo = Checked(ConformsTo),
            }
            : null;
        return new AccessibilitySaveRequest(languages, HtmlLanguage, _allImages.Where(i => i.IsChanged).Select(i => i.ToEdit()).ToList(), metadata);
    }

    /// <summary>Opens the place of an image in the code editor.</summary>
    public void OpenImage(AccessibilityImageItem image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _navigate(image.Image.BookPath, image.Image.Offset);
    }

    /// <summary>Releases the previews that were loaded.</summary>
    public void Dispose()
    {
        foreach (AccessibilityImageItem item in _allImages)
        {
            item.DisposePreview();
        }
    }

    partial void OnShowOnlyUndescribedChanged(bool value) => RefreshVisibleImages();

    partial void OnHtmlLanguageChanged(HtmlLanguageChoice value)
    {
        OnPropertyChanged(nameof(HtmlLanguageKeep));
        OnPropertyChanged(nameof(HtmlLanguageOnlyMissing));
        OnPropertyChanged(nameof(HtmlLanguageAll));
        OnStateChanged();
    }

    partial void OnSummaryChanged(string value) => OnStateChanged();

    private void OnImageChanged()
    {
        if (!_initialized)
        {
            return;
        }

        OnPropertyChanged(nameof(ImagesSummary));
        OnStateChanged();
    }

    private void OnStateChanged()
    {
        if (!_initialized)
        {
            return;
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanAddLanguage));
        SaveCommand.NotifyCanExecuteChanged();
        AddLanguageCommand.NotifyCanExecuteChanged();
    }

    private void RefreshVisibleImages()
    {
        Images.Clear();
        foreach (AccessibilityImageItem item in _allImages.Where(i => !ShowOnlyUndescribed || i.NeedsDescription))
        {
            Images.Add(item);
        }
    }

    private async Task SaveAsync()
    {
        AccessibilitySaveRequest request = BuildRequest();
        if (!request.IsEmpty && await _save(request).ConfigureAwait(true))
        {
            Done?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AddLanguage()
    {
        if (ResolveLanguage(NewLanguage) is { } code && Languages.All(l => !string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            Languages.Add(ItemOf(code));
            NewLanguage = string.Empty;
        }
    }

    private void RemoveLanguage(AccessibilityLanguageItem? item)
    {
        if (item is not null && Languages.Count > 1)
        {
            Languages.Remove(item);
        }
    }

    private void MakeMain(AccessibilityLanguageItem? item)
    {
        int index = item is null ? -1 : Languages.IndexOf(item);
        if (index > 0)
        {
            Languages.Move(index, 0);
        }
    }

    // A language as the user typed it: a code, or the name of a language the application knows; null for anything else.
    private static string? ResolveLanguage(string text)
    {
        string typed = text.Trim();
        if (typed.Length == 0)
        {
            return null;
        }

        string byName = Language.GetLanguageCode(typed);
        if (byName.Length > 0)
        {
            return byName;
        }

        return typed.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && char.IsAsciiLetter(typed[0]) ? typed : null;
    }

    private static AccessibilityLanguageItem ItemOf(string code) => new(code, Language.GetLanguageName(code, code));

    private string LanguagesKey() => string.Join("|", Languages.Select(l => l.Code));

    private string MetadataKey() => string.Join(
        "\n", Summary.Trim(), Key(AccessModes), Key(AccessModesSufficient), Key(Features), Key(Hazards), Key(ConformsTo));

    private static string Key(IEnumerable<AccessibilityChoiceItem> items) => string.Join("|", items.Where(i => i.IsChecked).Select(i => i.Value));

    private static List<string> Checked(IEnumerable<AccessibilityChoiceItem> items) => items.Where(i => i.IsChecked).Select(i => i.Value).ToList();

    // The known values, then the ones the book has that are not in the vocabulary (so saving does not drop them).
    private void Fill(ObservableCollection<AccessibilityChoiceItem> target, string group, IReadOnlyList<string> known, IReadOnlyList<string> current)
    {
        foreach (string value in known)
        {
            target.Add(new AccessibilityChoiceItem(value, DisplayName(group, value), current.Contains(value, StringComparer.Ordinal)));
        }

        foreach (string value in current.Where(v => !known.Contains(v, StringComparer.Ordinal)))
        {
            target.Add(new AccessibilityChoiceItem(value, value, isChecked: true));
        }

        foreach (AccessibilityChoiceItem item in target)
        {
            item.PropertyChanged += (_, _) => OnStateChanged();
        }
    }

    private static string DisplayName(string group, string value) =>
        group.Length == 0 ? value : Strings.TryGet($"Accessibility_{group}_{value}") ?? value;

    // The preview of an image of the book; null for what Avalonia cannot decode (svg) or a file that is not there.
    private Bitmap? LoadPreview(string? bookPath)
    {
        if (bookPath is null || _book.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath) is not ImageResource image)
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(image.FullPath);
            return new Bitmap(stream);
        }
#pragma warning disable CA1031 // An image that cannot be decoded just has no preview.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
