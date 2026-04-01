namespace PrintEase.App.Models;

public sealed class PrintPreset
{
    public required string Name { get; init; }
    public required PrintOptions Options { get; init; }
    public int? PageRangeStart { get; init; }
    public int? PageRangeEnd { get; init; }
}
