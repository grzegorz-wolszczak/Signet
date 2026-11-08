using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Signet.App.Infrastructure;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>
/// Applies the fonts of Preferences › Appearance › Clips › Fonts (<see cref="ClipEditorAppearance"/>) to the clips tree of the
/// Clip Editor window and the docked Clips panel. The values are set on the cells and headers themselves (and cleared again
/// for a font that is not set): FontFamily/Size/Weight are inherited, so they reach the text inside a cell.
/// </summary>
internal sealed class ClipsLook
{
    private readonly TreeDataGrid _tree;
    private ClipEditorAppearance _look = ClipEditorAppearance.Default;

    /// <summary>Follows the fonts of <paramref name="tree"/> (the cells as they are realized, the headers once laid out).</summary>
    public ClipsLook(TreeDataGrid tree)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _tree.CellPrepared += (_, e) => SetFont(e.Cell, _look.EntriesFontFamily, _look.EntriesFontSize, _look.EntriesFontWeight);
        _tree.LayoutUpdated += (_, _) => ApplyHeaders();
    }

    /// <summary>Applies <paramref name="look"/> to the cells and headers that exist now; later ones follow it too.</summary>
    public void Apply(ClipEditorAppearance look)
    {
        _look = look;
        foreach (TreeDataGridCell cell in _tree.GetVisualDescendants().OfType<TreeDataGridCell>())
        {
            SetFont(cell, _look.EntriesFontFamily, _look.EntriesFontSize, _look.EntriesFontWeight);
        }

        ApplyHeaders();
    }

    private void ApplyHeaders()
    {
        if (_tree.ColumnHeadersPresenter is not { } presenter)
        {
            return;
        }

        foreach (TreeDataGridColumnHeader header in presenter.GetVisualChildren().OfType<TreeDataGridColumnHeader>())
        {
            SetFont(header, _look.HeaderFontFamily, _look.HeaderFontSize, _look.HeaderFontWeight);
        }
    }

    /// <summary>Sets a font on a control; an empty family / a size or weight of 0 clears it (the control follows its parent).</summary>
    public static void SetFont(Control control, string family, int size, int weight)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (family.Length > 0)
        {
            control.SetValue(TemplatedControl.FontFamilyProperty, new FontFamily(family));
        }
        else
        {
            control.ClearValue(TemplatedControl.FontFamilyProperty);
        }

        if (size > 0)
        {
            control.SetValue(TemplatedControl.FontSizeProperty, (double)size);
        }
        else
        {
            control.ClearValue(TemplatedControl.FontSizeProperty);
        }

        if (weight > 0)
        {
            control.SetValue(TemplatedControl.FontWeightProperty, FontWeightOptions.ToAvalonia(weight));
        }
        else
        {
            control.ClearValue(TemplatedControl.FontWeightProperty);
        }
    }
}
