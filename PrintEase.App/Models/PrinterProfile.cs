namespace PrintEase.App.Models;

public sealed class PrinterProfile
{
    public required string PrinterName { get; init; }
    public required PrintOptions Options { get; init; }
    public required string PreviewContent { get; init; }
    public int? PageRangeStart { get; init; }
    public int? PageRangeEnd { get; init; }
    public DateTime UpdatedUtc { get; init; }
}
