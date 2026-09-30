namespace DesignSpace.Core;

/// <summary>Document-workspace preferences, independent of document geometry and undo history.</summary>
public sealed record ArtboardSettings
{
    public bool ShowGrid { get; init; }
    public bool ShowRulers { get; init; }=true;
    public bool SnapToGrid { get; init; }=true;
    public double GridSize { get; init; }=8;
    public bool SnapToSnaplines { get; init; }
    public double SnapTolerance { get; init; }=6;
    public double DefaultMargin { get; init; }=8;
    public double DefaultPadding { get; init; }=8;
    public void Validate()
    {
        if(!double.IsFinite(GridSize)||GridSize<1||GridSize>10000||
           !double.IsFinite(SnapTolerance)||SnapTolerance<1||SnapTolerance>32||
           !double.IsFinite(DefaultMargin)||DefaultMargin<0||DefaultMargin>10000||
           !double.IsFinite(DefaultPadding)||DefaultPadding<0||DefaultPadding>10000)
            throw new InvalidDataException("Grid spacing must be 1–10000, snap tolerance 1–32 screen pixels, and margin/padding 0–10000 design units.");
    }
}
