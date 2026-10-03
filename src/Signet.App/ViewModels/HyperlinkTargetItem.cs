namespace Signet.App.ViewModels;

/// <summary>
/// A single link target in the "Insert Link" dialog: an (X)HTML file, a specific id within a
/// file, or a media file. <see cref="Href"/> is already relative to the active tab's file.
/// </summary>
/// <param name="Display">List text (file name, optionally with <c># id</c>).</param>
/// <param name="Href">Value to insert into the <c>href</c> attribute.</param>
public sealed record HyperlinkTargetItem(string Display, string Href);
