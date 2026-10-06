using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Signet.App.Resources;
using Signet.Controls.TreeDataGrid.Models;

namespace Signet.App.Infrastructure;

/// <summary>
/// Creates the columns of a <c>TreeDataGrid</c> source whose headers come from <c>Strings.resx</c> and follow a UI
/// language switch — the counterpart of <c>{loc:Loc}</c> in XAML headers. Every column is also registered with
/// <see cref="TreeDataGridHeaderSizing"/>, so its minimum width can be fitted to its header.
/// </summary>
/// <remarks>The owner (a view model) must keep the instance alive: language listeners are held weakly.</remarks>
/// <typeparam name="TModel">The row model.</typeparam>
public sealed class LocalizedColumns<TModel> : ILanguageAware
    where TModel : class
{
    private readonly List<(ColumnBase<TModel> Column, string HeaderKey)> _columns = new();

    /// <summary>Creates the factory and subscribes it to language changes.</summary>
    public LocalizedColumns()
    {
        Strings.RegisterLanguageAware(this);
    }

    /// <summary>A read-only (or, with <paramref name="setter"/>, editable) text column.</summary>
    public TextColumn<TModel, TValue> Text<TValue>(
        string headerKey,
        Expression<Func<TModel, TValue?>> getter,
        GridLength? width = null,
        TextColumnOptions<TModel>? options = null,
        Action<TModel, TValue?>? setter = null)
    {
        TextColumn<TModel, TValue> column = setter is null
            ? new TextColumn<TModel, TValue>(Strings.Get(headerKey), getter, width, options)
            : new TextColumn<TModel, TValue>(Strings.Get(headerKey), getter, setter, width, options);
        return Track(column, headerKey);
    }

    /// <summary>A column whose cells are built from <paramref name="cellTemplate"/>.</summary>
    public TemplateColumn<TModel> Template(
        string headerKey,
        IDataTemplate cellTemplate,
        GridLength? width = null,
        TemplateColumnOptions<TModel>? options = null)
    {
        return Track(new TemplateColumn<TModel>(Strings.Get(headerKey), cellTemplate, null, width, options), headerKey);
    }

    /// <summary>
    /// A column whose cells are built from the data template found under <paramref name="cellTemplateResourceKey"/>
    /// (looked up from the cell, e.g. in the window's resources — so the template can stay in XAML); with
    /// <paramref name="cellEditingTemplateResourceKey"/> the cells are editable with that template.
    /// </summary>
    public TemplateColumn<TModel> Template(
        string headerKey,
        object cellTemplateResourceKey,
        GridLength? width = null,
        TemplateColumnOptions<TModel>? options = null,
        object? cellEditingTemplateResourceKey = null)
    {
        return Track(
            new TemplateColumn<TModel>(
                Strings.Get(headerKey), cellTemplateResourceKey, cellEditingTemplateResourceKey, width, options),
            headerKey);
    }

    /// <summary>A check box column (editable when <paramref name="setter"/> is given).</summary>
    public CheckBoxColumn<TModel> CheckBox(
        string headerKey,
        Expression<Func<TModel, bool>> getter,
        Action<TModel, bool>? setter = null,
        GridLength? width = null,
        CheckBoxColumnOptions<TModel>? options = null)
    {
        return Track(new CheckBoxColumn<TModel>(Strings.Get(headerKey), getter, setter, width, options), headerKey);
    }

    /// <summary>
    /// The expander column of a hierarchical source around <paramref name="inner"/> (created by this factory, so its
    /// header is localized); the minimum width of the expander column is the inner column's.
    /// </summary>
    public HierarchicalExpanderColumn<TModel> Expander(
        ColumnBase<TModel> inner,
        Func<TModel, IEnumerable<TModel>?> childSelector,
        Expression<Func<TModel, bool>>? hasChildrenSelector = null,
        Expression<Func<TModel, bool>>? isExpandedSelector = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        HierarchicalExpanderColumn<TModel> column = new(inner, childSelector, hasChildrenSelector, isExpandedSelector);
        TreeDataGridHeaderSizing.Register(column, () => inner.Options.MinWidth, w => inner.Options.MinWidth = w);
        return column;
    }

    /// <inheritdoc />
    public void OnLanguageChanged()
    {
        foreach ((ColumnBase<TModel> column, string headerKey) in _columns)
        {
            column.Header = Strings.Get(headerKey);
        }
    }

    private TColumn Track<TColumn>(TColumn column, string headerKey)
        where TColumn : ColumnBase<TModel>
    {
        _columns.Add((column, headerKey));
        TreeDataGridHeaderSizing.Register(column, () => column.Options.MinWidth, w => column.Options.MinWidth = w);
        return column;
    }
}
