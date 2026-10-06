namespace Signet.Controls.TreeDataGrid.Models
{
    public interface ITextSearchableColumn<TModel>
    {
        public bool IsTextSearchEnabled { get; }
        internal string? SelectValue(TModel model);
    }
}
