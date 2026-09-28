using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace DesignSpace.Docking.Uno;

/// <summary>Original vector command glyphs, independent of installed symbol fonts.</summary>
public sealed class StudioCommandIcon : SKCanvasElement
{
    public string Kind { get; init; }="Play";
    public StudioCommandIcon() { Width=16;Height=16;IsHitTestVisible=false; }
    public static UIElement CreateContent(string text)
    {
        var kind=text switch
        {
            "↶"=>"Undo","↷"=>"Redo","▶"=>"Play","■"=>"Stop","●"=>"Record","|◀"=>"First",
            "◇"=>"Key","◉"=>"Eye","♙"=>"Lock","↑"=>"Up","↓"=>"Down","⊞"=>"Group","×"=>"Close","↗"=>"Float",_=>null
        };
        if(kind is not null) return new StudioCommandIcon{Kind=kind};
        if(text.StartsWith("▶",StringComparison.Ordinal))
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};
            row.Children.Add(new StudioCommandIcon{Kind="Play"});row.Children.Add(StudioTheme.Text(text.TrimStart('▶',' ')));return row;
        }
        return StudioTheme.Text(text);
    }
    protected override void RenderOverride(SKCanvas c,Size area)
    {
        c.Save();c.Scale((float)area.Width/18,(float)area.Height/18);
        using var p=new SKPaint{IsAntialias=true,Color=new SKColor(216,216,220),StrokeWidth=1.5f,Style=SKPaintStyle.Stroke,StrokeJoin=SKStrokeJoin.Round,StrokeCap=SKStrokeCap.Round};
        void Line(float x,float y,float a,float b)=>c.DrawLine(x,y,a,b,p);
        switch(Kind)
        {
            case "Undo": case "Redo":
                if(Kind=="Redo"){c.Translate(18,0);c.Scale(-1,1);}
                Line(3,4,3,9);Line(3,9,8,9);
                using(var path=new SKPath()){path.MoveTo(3,9);path.CubicTo(4,2,16,3,15,11);path.CubicTo(15,14,13,15,10,15);c.DrawPath(path,p);}break;
            case "Play":
                p.Style=SKPaintStyle.Fill;using(var path=new SKPath()){path.MoveTo(5,3);path.LineTo(15,9);path.LineTo(5,15);path.Close();c.DrawPath(path,p);}break;
            case "Stop":p.Style=SKPaintStyle.Fill;c.DrawRect(4,4,10,10,p);break;
            case "Record":p.Style=SKPaintStyle.Fill;c.DrawCircle(9,9,5,p);break;
            case "First":
                Line(3,3,3,15);p.Style=SKPaintStyle.Fill;using(var path=new SKPath()){path.MoveTo(14,3);path.LineTo(6,9);path.LineTo(14,15);path.Close();c.DrawPath(path,p);}break;
            case "Key":
                using(var path=new SKPath()){path.MoveTo(9,2);path.LineTo(15,9);path.LineTo(9,16);path.LineTo(3,9);path.Close();c.DrawPath(path,p);}break;
            case "Eye":
                using(var path=new SKPath()){path.MoveTo(2,9);path.QuadTo(9,0,16,9);path.QuadTo(9,18,2,9);path.Close();c.DrawPath(path,p);}c.DrawCircle(9,9,2.5f,p);break;
            case "Lock":c.DrawRoundRect(SKRect.Create(4,8,10,8),1,1,p);c.DrawArc(SKRect.Create(6,2,6,10),180,180,false,p);Line(9,11,9,13);break;
            case "Up": case "Down":
                if(Kind=="Down"){c.Translate(0,18);c.Scale(1,-1);}Line(9,3,9,15);Line(4,8,9,3);Line(9,3,14,8);break;
            case "Group":c.DrawRect(3,3,12,12,p);Line(9,5,9,13);Line(5,9,13,9);break;
            case "Close":Line(5,5,13,13);Line(13,5,5,13);break;
            case "Float":Line(4,14,14,4);Line(8,4,14,4);Line(14,4,14,10);break;
        }
        c.Restore();
    }
}
