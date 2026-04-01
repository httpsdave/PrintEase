namespace PrintEase.App.Models;

public sealed class PagePreviewItem
{
    public int PageNumber { get; init; }
    public required string Snippet { get; init; }

    public string Display => $"Page {PageNumber}: {Snippet}";
}
