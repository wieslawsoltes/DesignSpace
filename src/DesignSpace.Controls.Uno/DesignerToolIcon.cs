using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace DesignSpace.Controls.Uno;

/// <summary>Original vector tool glyphs; no product icon assets or icon fonts are required.</summary>
public sealed class DesignerToolIcon : SKCanvasElement
{
    public string Kind { get; set; }="Selection";
    protected override void RenderOverride(SKCanvas c,Size area)
    {
        c.Save(); c.Translate((float)(area.Width-22)/2,(float)(area.Height-22)/2);
        using var p=new SKPaint { IsAntialias=true,Color=new SKColor(216,216,220),StrokeWidth=1.2f,Style=SKPaintStyle.Stroke,StrokeJoin=SKStrokeJoin.Round,StrokeCap=SKStrokeCap.Round };
        void Line(float x,float y,float a,float b)=>c.DrawLine(x,y,a,b,p);
        switch(Kind)
        {
            case "Selection": case "Direct Selection":
                using(var path=new SKPath()) { path.MoveTo(5,3); path.LineTo(17,13); path.LineTo(11,13); path.LineTo(9,19); path.Close(); if(Kind=="Selection") p.Style=SKPaintStyle.Fill; c.DrawPath(path,p); } break;
            case "Hand":
                using(var path=new SKPath()) { path.MoveTo(5,12); path.LineTo(4,9); path.CubicTo(3,7,5,7,6,9); path.LineTo(8,11); path.LineTo(8,4); path.CubicTo(8,2,10,2,10,4); path.LineTo(10,8); path.LineTo(10,3); path.CubicTo(10,1,12,1,12,3); path.LineTo(12,8); path.LineTo(12,4); path.CubicTo(12,2,14,2,14,4); path.LineTo(14,9); path.LineTo(14,6); path.CubicTo(14,4,16,4,16,6); path.LineTo(16,13); path.CubicTo(16,20,8,21,5,12); path.Close(); c.DrawPath(path,p); } break;
            case "Zoom": c.DrawCircle(9,9,6,p); Line(14,14,19,19); break;
            case "Rectangle": c.DrawRect(4,5,14,12,p); break;
            case "Ellipse": c.DrawOval(4,4,14,14,p); break;
            case "Line": Line(4,18,18,4); break;
            case "Path":
                using(var path=new SKPath()) { path.MoveTo(4,17); path.CubicTo(7,0,15,22,18,5); c.DrawPath(path,p); } c.DrawRect(2,15,4,4,p); c.DrawRect(16,3,4,4,p); break;
            case "TextBlock": Line(4,4,18,4); Line(11,4,11,19); Line(7,19,15,19); Line(4,4,4,7); Line(18,4,18,7); break;
            case "Grid": c.DrawRect(3,3,16,16,p); Line(3,8,19,8); Line(3,13,19,13); Line(8,3,8,19); Line(13,3,13,19); break;
            case "Button": c.DrawRect(3,6,16,11,p); Line(7,11,15,11); break;
        }
        c.Restore();
    }
}
