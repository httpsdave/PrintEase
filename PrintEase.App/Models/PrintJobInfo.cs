namespace PrintEase.App.Models;

public sealed class PrintJobInfo
{
    public int Id { get; init; }
    public required string Document { get; init; }
    public required string User { get; init; }
    public int TotalPages { get; init; }
    public DateTime SubmittedOn { get; init; }
    public required string Status { get; init; }
}
