using DesignSpace.Rendering.Skia;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace DesignSpace.Controls.Uno;

public sealed class ColorEditorControl : Grid
{
    private sealed class ColorSurface(ColorEditorControl owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas,Size area)
        {
            var width=(float)area.Width-24; var height=(float)area.Height;
            using var paint=new SKPaint { IsAntialias=true };
            using(var shader=SKShader.CreateLinearGradient(new(0,0),new(width,0),[SKColors.White,SKColor.FromHsv(owner._hue,100,100)],null,SKShaderTileMode.Clamp)) { paint.Shader=shader; canvas.DrawRect(0,0,width,height,paint); }
            using(var shader=SKShader.CreateLinearGradient(new(0,0),new(0,height),[SKColors.Transparent,SKColors.Black],null,SKShaderTileMode.Clamp)) { paint.Shader=shader; canvas.DrawRect(0,0,width,height,paint); }
            using(var shader=SKShader.CreateLinearGradient(new(0,0),new(0,height),[SKColors.Red,SKColors.Yellow,SKColors.Lime,SKColors.Cyan,SKColors.Blue,SKColors.Magenta,SKColors.Red],null,SKShaderTileMode.Clamp)) { paint.Shader=shader; canvas.DrawRect(width+8,0,16,height,paint); }
            paint.Shader=null; paint.Style=SKPaintStyle.Stroke; paint.StrokeWidth=1.5f; paint.Color=SKColors.White;
            canvas.DrawCircle(width*owner._saturation/100,height*(1-owner._value/100),4,paint);
            var y=height*owner._hue/360; canvas.DrawRect(width+6,y-2,20,4,paint);
        }
    }
    private float _hue=205,_saturation=100,_value=83; private byte _alpha=255;
    private bool _dragging; private bool _hueDrag; private readonly ColorSurface _surface;
    private readonly TextBox _hex;
    public event EventHandler<string>? ColorChanged;
    public ColorEditorControl()
    {
        RowDefinitions.Add(new(){Height=new GridLength(112)}); RowDefinitions.Add(new(){Height=new GridLength(29)});
        _surface=new(this); Children.Add(_surface);
        _hex=StudioTheme.Input("#FF0078D4","Brush hexadecimal ARGB color"); _hex.Margin=new Thickness(0,4,0,0); SetRow(_hex,1); Children.Add(_hex);
        _hex.LostFocus+=(_,_)=>CommitHex(); _hex.KeyDown+=(_,e)=> { if(e.Key==Windows.System.VirtualKey.Enter) { CommitHex(); e.Handled=true; } };
        _surface.PointerPressed+=(_,e)=> { _dragging=true; var p=e.GetCurrentPoint(_surface).Position; _hueDrag=p.X>=_surface.ActualWidth-24; Update(p); _surface.CapturePointer(e.Pointer); e.Handled=true; };
        _surface.PointerMoved+=(_,e)=> { if(_dragging) Update(e.GetCurrentPoint(_surface).Position); };
        _surface.PointerReleased+=(_,e)=> { if(!_dragging) return; _dragging=false; _surface.ReleasePointerCapture(e.Pointer); ColorChanged?.Invoke(this,_hex.Text); e.Handled=true; };
        _surface.PointerCaptureLost+=(_,_)=>_dragging=false;
    }
    public string Value
    {
        get=>_hex.Text;
        set { var color=DesignRenderer.Color(value,new SKColor(0,120,212)); color.ToHsv(out _hue,out _saturation,out _value); _alpha=color.Alpha; _hex.Text=Hex(color); _surface.Invalidate(); }
    }
    private static string Hex(SKColor color)=>$"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    private void Update(Point p)
    {
        if(_hueDrag) _hue=(float)Math.Clamp(p.Y/Math.Max(1,_surface.ActualHeight)*360,0,359.99);
        else { _saturation=(float)Math.Clamp(p.X/Math.Max(1,_surface.ActualWidth-24)*100,0,100); _value=(float)Math.Clamp((1-p.Y/Math.Max(1,_surface.ActualHeight))*100,0,100); }
        _hex.Text=Hex(SKColor.FromHsv(_hue,_saturation,_value,_alpha)); _surface.Invalidate();
    }
    private void CommitHex() { Value=_hex.Text; ColorChanged?.Invoke(this,_hex.Text); }
}
