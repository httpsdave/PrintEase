using System.Printing;

namespace PrintEase.App.Models;

public sealed class PrintOptions
{
    public int Copies { get; init; } = 1;
    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;
    public OutputQuality OutputQuality { get; init; } = OutputQuality.Normal;
    public string PaperSizeName { get; init; } = "A4";
    public double? CustomPaperWidthInches { get; init; }
    public double? CustomPaperHeightInches { get; init; }
    public double MarginLeft { get; init; } = 36;
    public double MarginTop { get; init; } = 36;
    public double MarginRight { get; init; } = 36;
    public double MarginBottom { get; init; } = 36;
}
