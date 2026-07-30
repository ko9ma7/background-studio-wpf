namespace BackgroundStudio.Core;

public enum MaskTool
{
    Erase,
    Restore
}

public readonly record struct MaskPoint(double X, double Y);

public sealed record MaskStroke(
    MaskTool Tool,
    double Radius,
    IReadOnlyList<MaskPoint> Points);
