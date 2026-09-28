using DesignSpace.Core;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;
public static class DesignTypography
{
    public static SKTypeface DefaultTypeface { get; set; }=SKTypeface.Default;
}
public sealed class DesignViewport
{
    public double Zoom { get; set; }=.8;
    public double PanX { get; set; }=48;
    public double PanY { get; set; }=60;
    public bool ShowGrid { get; set; }
    public bool ShowRulers { get; set; }=true;
    public bool SnapToGrid { get; set; }=true;
    public double GridSize { get; set; }=8;
    public DPoint ScreenToWorld(DPoint p)=>new((p.X-PanX)/Zoom,(p.Y-PanY)/Zoom);
    public DPoint WorldToScreen(DPoint p)=>new(p.X*Zoom+PanX,p.Y*Zoom+PanY);
    public void ZoomAt(DPoint screen,double zoom) { var world=ScreenToWorld(screen); Zoom=Math.Clamp(zoom,.1,8); PanX=screen.X-world.X*Zoom; PanY=screen.Y-world.Y*Zoom; }
    public void Fit(double width,double height,DRect bounds)
    {
        Zoom=Math.Clamp(Math.Min(Math.Max(20,width-96)/Math.Max(1,bounds.Width),Math.Max(20,height-100)/Math.Max(1,bounds.Height)),.1,2);
        PanX=(width-bounds.Width*Zoom)/2-bounds.X*Zoom; PanY=(height-bounds.Height*Zoom)/2-bounds.Y*Zoom+10;
    }
}
