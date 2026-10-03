using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Metadata;
using Signet.Core.Semantics;

namespace Signet.App.Views;

/// <summary>
/// The modal "Metadata Editor" dialog. A tree of the
/// recognized OPF metadata elements; the "Add Metadata Element"/"Add Property to Element" logic
/// (default values, language/role picker dialogs, custom name) lives here.
/// </summary>
public partial class MetadataEditorWindow : Window
{
    private static readonly Regex ValidXmlName = new(
        "^(?![Xx][Mm][Ll])([A-Za-z_][A-Za-z0-9._-]*)(:([A-Za-z_][A-Za-z0-9._-]*))?$", RegexOptions.Compiled);

    private MetadataEditorViewModel? _bound;

    /// <summary>Initializes the window.</summary>
    public MetadataEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Tree.SelectionChanged += (_, _) => SyncSelectionToViewModel();
        AddElementButton.Click += async (_, _) => await AddElementAsync();
        AddPropertyButton.Click += async (_, _) => await AddPropertyAsync();
        AddElementMenuItem.Click += async (_, _) => await AddElementAsync();
        AddPropertyMenuItem.Click += async (_, _) => await AddPropertyAsync();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Shows the dialog modally; returns <c>true</c> when the user accepted and saving succeeded.</summary>
    public static async Task<bool> RunAsync(Window owner, MetadataEditorViewModel viewModel)
    {
        MetadataEditorWindow window = new() { DataContext = viewModel };
        await window.ShowDialog(owner);
        return viewModel.Accepted;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.CloseRequested -= OnCloseRequested;
        }

        _bound = DataContext as MetadataEditorViewModel;

        if (_bound is not null)
        {
            _bound.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void SyncSelectionToViewModel()
    {
        if (_bound is not null)
        {
            _bound.SelectedNode = Tree.SelectedItem as MetadataNodeViewModel;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_bound is null)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Delete)
        {
            if (_bound.RemoveCommand.CanExecute(null))
            {
                _bound.RemoveCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void Select(MetadataNodeViewModel? node)
    {
        if (node is not null)
        {
            Tree.SelectedItem = node;
        }
    }

    // --- "Add Metadata Element" ---

    private async Task AddElementAsync()
    {
        if (_bound is null)
        {
            return;
        }

        var table = _bound.IsEpub3 ? MetadataFieldCatalog.Epub3Elements : MetadataFieldCatalog.Epub2Elements;
        System.Collections.Generic.IReadOnlyList<string> codes =
            await PickMetadataFieldWindow.AskAsync(this, Strings.Get("MetadataEditorWindow_AddElementMenu"), table);

        foreach (string code in codes)
        {
            if (_bound.IsEpub3)
            {
                await AddElementEpub3Async(code);
            }
            else
            {
                await AddElementEpub2Async(code);
            }
        }
    }

    private async Task AddElementEpub3Async(string code)
    {
        if (_bound is null)
        {
            return;
        }

        switch (code)
        {
            case "dc:language":
                string? lang = await PickMetadataFieldWindow.AskOneAsync(this, Strings.Get("MetadataEditorWindow_SelectLanguage"), Language.GetLangMap());
                Select(_bound.InsertElement("dc:language", lang ?? "en"));
                break;
            case "dc:identifier-isbn":
                Select(_bound.InsertElement("dc:identifier", "urn:isbn:" + Strings.Get("MetadataEditorWindow_IsbnPlaceholder")));
                break;
            case "dc:identifier-issn":
                Select(_bound.InsertElement("dc:identifier", "urn:issn:" + Strings.Get("MetadataEditorWindow_IssnPlaceholder")));
                break;
            case "dc:identifier-doi":
                Select(_bound.InsertElement("dc:identifier", "urn:doi:" + Strings.Get("MetadataEditorWindow_DoiPlaceholder")));
                break;
            case "dc:identifier-uuid":
                Select(_bound.InsertElement("dc:identifier", "urn:uuid:" + Strings.Get("MetadataEditorWindow_UuidPlaceholder")));
                break;
            case "dc:identifier-amazon":
                Select(_bound.InsertElement("dc:identifier", "urn:AMAZON:" + Strings.Get("MetadataEditorWindow_AsinPlaceholder")));
                break;
            case "dc:identifier-custom":
                Select(_bound.InsertElement("dc:identifier", Strings.Get("MetadataEditorWindow_CustomIdentifierPlaceholder")));
                break;
            case "dc:date":
            case "dcterms:created":
                Select(_bound.InsertElement(code, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                break;
            case "dcterms:modified":
                Select(_bound.InsertElement(code, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
                break;
            case "dc:type":
                Select(_bound.InsertElement(code, Strings.Get("MetadataEditorWindow_TypePlaceholder")));
                break;
            case "dc:creator-aut":
                Select(_bound.InsertElement("dc:creator", Strings.Get("MetadataEditorWindow_AuthorPlaceholder")));
                _bound.InsertChild("role", "aut");
                _bound.InsertChild("scheme", "marc:relators");
                break;
            case "dc:creator":
                Select(_bound.InsertElement("dc:creator", Strings.Get("MetadataEditorWindow_CreatorPlaceholder")));
                break;
            case "dc:contributor":
                Select(_bound.InsertElement("dc:contributor", Strings.Get("MetadataEditorWindow_ContributorPlaceholder")));
                break;
            case "meta":
                Select(_bound.InsertElement("meta", Strings.Get("MetadataEditorWindow_ValuePlaceholder")));
                _bound.InsertChild("property", Strings.Get("MetadataEditorWindow_NamePlaceholder"));
                break;
            case "custom-element":
                await AddCustomElementAsync(validateXmlName: true);
                break;
            default:
                Select(_bound.InsertElement(code, Strings.Get("MetadataEditorWindow_YourValuePlaceholder")));
                break;
        }
    }

    private async Task AddElementEpub2Async(string code)
    {
        if (_bound is null)
        {
            return;
        }

        switch (code)
        {
            case "dc:language":
                string? lang = await PickMetadataFieldWindow.AskOneAsync(this, Strings.Get("MetadataEditorWindow_SelectLanguage"), Language.GetLangMap());
                Select(_bound.InsertElement("dc:language", lang ?? "en"));
                break;
            case "dc:identifier-isbn":
                Select(_bound.InsertElement("dc:identifier", Strings.Get("MetadataEditorWindow_IsbnPlaceholder")));
                _bound.InsertChild("opf:scheme", "ISBN");
                break;
            case "dc:identifier-issn":
                Select(_bound.InsertElement("dc:identifier", Strings.Get("MetadataEditorWindow_IssnPlaceholder")));
                _bound.InsertChild("opf:scheme", "ISSN");
                break;
            case "dc:identifier-doi":
                Select(_bound.InsertElement("dc:identifier", Strings.Get("MetadataEditorWindow_DoiPlaceholder")));
                _bound.InsertChild("opf:scheme", "DOI");
                break;
            case "dc:identifier-uuid":
                Select(_bound.InsertElement("dc:identifier", Strings.Get("MetadataEditorWindow_UuidPlaceholder")));
                _bound.InsertChild("opf:scheme", "UUID");
                break;
            case "dc:identifier-amazon":
                Select(_bound.InsertElement("dc:identifier", Strings.Get("MetadataEditorWindow_AsinPlaceholder")));
                _bound.InsertChild("opf:scheme", "AMAZON");
                break;
            case "dc:identifier-custom":
                Select(_bound.InsertElement("dc:identifier", Strings.Get("MetadataEditorWindow_CustomIdentifierPlaceholder")));
                _bound.InsertChild("opf:scheme", string.Empty);
                break;
            case "dc:date-publication":
            case "dc:date-creation":
            case "dc:date-modification":
                string opfEvent = code["dc:date-".Length..];
                Select(_bound.InsertElement("dc:date", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                _bound.InsertChild("opf:event", opfEvent);
                break;
            case "dc:creator-aut":
                Select(_bound.InsertElement("dc:creator", Strings.Get("MetadataEditorWindow_AuthorPlaceholder")));
                _bound.InsertChild("opf:role", "aut");
                break;
            case "dc:creator":
                Select(_bound.InsertElement("dc:creator", Strings.Get("MetadataEditorWindow_CreatorPlaceholder")));
                break;
            case "dc:contributor":
                Select(_bound.InsertElement("dc:contributor", Strings.Get("MetadataEditorWindow_ContributorPlaceholder")));
                break;
            case "custom-element":
                await AddCustomElementAsync(validateXmlName: false);
                break;
            default:
                Select(_bound.InsertElement(code, Strings.Get("MetadataEditorWindow_YourValuePlaceholder")));
                break;
        }
    }

    private async Task AddCustomElementAsync(bool validateXmlName)
    {
        if (_bound is null)
        {
            return;
        }

        string? name = await TextPromptWindow.AskAsync(
            this,
            Strings.Get("MetadataEditorWindow_CustomElement"),
            Strings.Get("MetadataEditorWindow_CustomElement"),
            Strings.Get("MetadataEditorWindow_CustomElementPlaceholder"));
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return;
        }

        if (validateXmlName && !ValidXmlName.IsMatch(name))
        {
            await MessageDialog.ShowAsync(
                this,
                Strings.Get("MetadataEditorWindow_InvalidElementNameTitle"),
                Strings.Get("MetadataEditorWindow_InvalidElementNameMessage"));
            return;
        }

        Select(_bound.InsertElement(name, Strings.Get("MetadataEditorWindow_ValuePlaceholder")));
    }

    // --- "Add Property to Element" ---

    private async Task AddPropertyAsync()
    {
        if (_bound is null || _bound.SelectedNode is null)
        {
            return;
        }

        var table = _bound.IsEpub3 ? MetadataFieldCatalog.Epub3Properties : MetadataFieldCatalog.Epub2Properties;
        System.Collections.Generic.IReadOnlyList<string> codes =
            await PickMetadataFieldWindow.AskAsync(this, Strings.Get("MetadataEditorWindow_AddPropertyMenu"), table);

        foreach (string code in codes)
        {
            if (_bound.IsEpub3)
            {
                await AddPropertyEpub3Async(code);
            }
            else
            {
                await AddPropertyEpub2Async(code);
            }
        }
    }

    private async Task AddPropertyEpub3Async(string code)
    {
        if (_bound is null)
        {
            return;
        }

        if (code.StartsWith("title-type:", StringComparison.Ordinal))
        {
            Select(_bound.InsertChild("title-type", code["title-type:".Length..]));
        }
        else if (code.StartsWith("collection-type:", StringComparison.Ordinal))
        {
            Select(_bound.InsertChild("collection-type", code["collection-type:".Length..]));
        }
        else if (code.StartsWith("dir:", StringComparison.Ordinal))
        {
            Select(_bound.InsertChild("dir", code["dir:".Length..]));
        }
        else if (code == "source-of")
        {
            Select(_bound.InsertChild("source-of", "pagination"));
        }
        else if (code is "group-position" or "display-seq")
        {
            Select(_bound.InsertChild(code, "1"));
        }
        else if (code == "scheme")
        {
            Select(_bound.InsertChild("scheme", string.Empty));
        }
        else if (code == "alternate-script")
        {
            Select(_bound.InsertChild("alternate-script", string.Empty));
            string? lang = await PickMetadataFieldWindow.AskOneAsync(this, Strings.Get("MetadataEditorWindow_SelectLanguage"), Language.GetLangMap());
            _bound.InsertChild("altlang", lang ?? "en");
        }
        else if (code is "xml:lang" or "altlang")
        {
            string? lang = await PickMetadataFieldWindow.AskOneAsync(this, Strings.Get("MetadataEditorWindow_SelectLanguage"), Language.GetLangMap());
            Select(_bound.InsertChild(code, lang ?? "en"));
        }
        else if (code == "role")
        {
            string? role = await PickMetadataFieldWindow.AskOneAsync(this, Strings.Get("MetadataEditorWindow_SelectRole"), MarcRelators.GetCodeMap());
            Select(_bound.InsertChild("role", role ?? "aut"));
            _bound.InsertChild("scheme", "marc:relators");
        }
        else if (code == "identifier-type")
        {
            Select(_bound.InsertChild("identifier-type", Strings.Get("MetadataEditorWindow_IdentifierTypePlaceholder")));
            _bound.InsertChild("scheme", Strings.Get("MetadataEditorWindow_SchemePlaceholder"));
        }
        else if (code == "custom-property")
        {
            await AddCustomPropertyAsync(
                Strings.Get("MetadataEditorWindow_CustomProperty"),
                Strings.Get("MetadataEditorWindow_CustomPropertyPlaceholder"));
        }
        else
        {
            Select(_bound.InsertChild(code, string.Empty));
        }
    }

    private async Task AddPropertyEpub2Async(string code)
    {
        if (_bound is null)
        {
            return;
        }

        if (code == "opf:scheme")
        {
            Select(_bound.InsertChild("opf:scheme", string.Empty));
        }
        else if (code == "xml:lang")
        {
            string? lang = await PickMetadataFieldWindow.AskOneAsync(this, Strings.Get("MetadataEditorWindow_SelectLanguage"), Language.GetLangMap());
            Select(_bound.InsertChild("xml:lang", lang ?? "en"));
        }
        else if (code == "opf:role")
        {
            string? role = await PickMetadataFieldWindow.AskOneAsync(this, Strings.Get("MetadataEditorWindow_SelectRole"), MarcRelators.GetCodeMap());
            Select(_bound.InsertChild("opf:role", role ?? "aut"));
        }
        else if (code == "custom-property")
        {
            await AddCustomPropertyAsync(
                Strings.Get("MetadataEditorWindow_CustomAttribute"),
                Strings.Get("MetadataEditorWindow_CustomAttributePlaceholder"));
        }
        else
        {
            Select(_bound.InsertChild(code, string.Empty));
        }
    }

    private async Task AddCustomPropertyAsync(string title, string initialValue)
    {
        if (_bound is null)
        {
            return;
        }

        string? name = await TextPromptWindow.AskAsync(this, title, title, initialValue);
        name = name?.Trim() ?? string.Empty;
        if (name.Length > 0)
        {
            Select(_bound.InsertChild(name, Strings.Get("MetadataEditorWindow_ValuePlaceholder")));
        }
    }
}
