using System.Collections.Immutable;
using System.Globalization;
using DesignSpace.Core;
using DesignSpace.Docking.Uno;
using DesignSpace.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace DesignSpace.Controls.Uno;

/// <summary>Interactive normalized sampler; pointer movement inspects output but does not edit a document.</summary>
public sealed class EasingPreviewControl : SKCanvasElement,IDisposable
{
    private EasingCurve _curve=new(EasingFamily.Cubic);
    private readonly SKPath _path=new();
    private double _minimum,_maximum=1,_progress=.5;
    private bool _dragging;
    public EasingCurve Curve { get=>_curve;set{value.Validate();if(value==_curve&&!_path.IsEmpty)return;_curve=value;Rebuild();Invalidate();} }
    public EasingPreviewControl()
    {
        Height=166;AutomationProperties.SetName(this,"Easing function preview");
        AutomationProperties.SetHelpText(this,"Drag horizontally to inspect normalized time and easing output. Overshoot remains visible.");
        Rebuild();
        void Sample(double x){_progress=Math.Clamp((x-28)/Math.Max(1,ActualWidth-42),0,1);Invalidate();}
        PointerPressed+=(_,e)=>{_dragging=true;Sample(e.GetCurrentPoint(this).Position.X);CapturePointer(e.Pointer);e.Handled=true;};
        PointerMoved+=(_,e)=>{if(_dragging){Sample(e.GetCurrentPoint(this).Position.X);e.Handled=true;}};
        PointerReleased+=(_,e)=>{_dragging=false;ReleasePointerCapture(e.Pointer);e.Handled=true;};
        PointerCaptureLost+=(_,_)=>_dragging=false;
    }
    private void Rebuild()
    {
        _path.Reset();_minimum=0;_maximum=1;
        for(var i=0;i<=256;i++)
        {
            var x=i/256d;var y=_curve.Evaluate(x);_minimum=Math.Min(_minimum,y);_maximum=Math.Max(_maximum,y);
            if(i==0)_path.MoveTo((float)x,(float)y);else _path.LineTo((float)x,(float)y);
        }
        var margin=(_maximum-_minimum)*.08;_minimum-=margin;_maximum+=margin;
    }
    protected override void RenderOverride(SKCanvas canvas,Size area)
    {
        canvas.Clear(new SKColor(30,30,33));var width=Math.Max(1,area.Width-42);var height=Math.Max(1,area.Height-42);
        SKPoint Point(double x,double y)=>new((float)(28+x*width),(float)(14+(_maximum-y)/(_maximum-_minimum)*height));
        using var paint=new SKPaint{IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=1};
        for(var i=0;i<=4;i++){paint.Color=new SKColor(54,54,61);canvas.DrawLine(Point(i/4d,_minimum),Point(i/4d,_maximum),paint);}
        paint.Color=new SKColor(103,103,115);canvas.DrawLine(Point(0,0),Point(1,0),paint);canvas.DrawLine(Point(0,1),Point(1,1),paint);
        canvas.Save();canvas.Translate(28,(float)(14+_maximum/(_maximum-_minimum)*height));canvas.Scale((float)width,(float)(-height/(_maximum-_minimum)));
        // Hairline is intentional for graph visualization and stays device-pixel sized under scaling.
        paint.Color=new SKColor(64,174,241);paint.StrokeWidth=0;canvas.DrawPath(_path,paint);canvas.Restore();
        var value=_curve.Evaluate(_progress);paint.Style=SKPaintStyle.Fill;paint.Color=new SKColor(236,178,79);canvas.DrawCircle(Point(_progress,value),4,paint);
        using var font=new SKFont(DesignTypography.DefaultTypeface,10);paint.Color=new SKColor(185,185,195);
        canvas.DrawText("0",8,Point(0,0).Y+3,font,paint);canvas.DrawText("1",8,Point(0,1).Y+3,font,paint);
        canvas.DrawText(FormattableString.Invariant($"t {_progress:0.00}  →  {value:0.000}"),28,(float)area.Height-7,font,paint);
    }
    public void Dispose()=>_path.Dispose();
}

/// <summary>Portable-value editor with inert draft capture; the host decides when to commit ReadCurve().</summary>
public sealed class EasingFunctionEditorControl : StackPanel,IDisposable
{
    private readonly ComboBox _family=new(){ItemsSource=Enum.GetNames<EasingFamily>(),SelectedItem="Cubic",MinHeight=25,FontSize=11};
    private readonly ComboBox _mode=new(){ItemsSource=Enum.GetNames<EasingDirection>(),SelectedItem="EaseOut",MinHeight=25,FontSize=11};
    private readonly Dictionary<string,TextBox> _fields=[];
    private readonly Dictionary<string,FrameworkElement> _rows=[];
    private readonly EasingPreviewControl _preview=new();
    private readonly TextBlock _error=StudioTheme.Text("",11,"#FFD09B");
    private bool _syncing;
    public event EventHandler? DraftChanged;
    public EasingFunctionEditorControl()
    {
        Spacing=5;
        void Choice(string label,ComboBox box){Children.Add(StudioTheme.Text(label));AutomationProperties.SetName(box,"Easing "+label);box.HorizontalAlignment=HorizontalAlignment.Stretch;Children.Add(box);box.SelectionChanged+=(_,_)=>Changed();}
        Choice("Family",_family);Choice("Direction",_mode);
        foreach(var (name,value) in new[]{("Amplitude","1"),("Bounces","3"),("Bounciness","2"),("Oscillations","3"),("Springiness","3"),("Exponent","2"),("Power","2")})
        {
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(96)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.Children.Add(StudioTheme.Text(name));
            var input=StudioTheme.Input(value,"Easing "+name);Grid.SetColumn(input,1);row.Children.Add(input);Children.Add(row);_fields[name]=input;_rows[name]=row;input.TextChanged+=(_,_)=>Changed();
        }
        Children.Add(_preview);_error.TextWrapping=TextWrapping.Wrap;Children.Add(_error);UpdatePreview();
    }
    public ImmutableDictionary<string,string> CaptureDraft()=>_fields.ToImmutableDictionary(p=>"Function "+p.Key,p=>p.Value.Text)
        .Add("Function Family",_family.SelectedItem as string??"").Add("Function Mode",_mode.SelectedItem as string??"");
    public void RestoreDraft(IReadOnlyDictionary<string,string> values)
    {
        _syncing=true;
        try
        {
            _family.SelectedItem=values.GetValueOrDefault("Function Family","Cubic");_mode.SelectedItem=values.GetValueOrDefault("Function Mode","EaseOut");
            foreach(var (name,fallback) in new[]{("Amplitude","1"),("Bounces","3"),("Bounciness","2"),("Oscillations","3"),("Springiness","3"),("Exponent","2"),("Power","2")})_fields[name].Text=values.GetValueOrDefault("Function "+name,fallback);
        }
        finally{_syncing=false;UpdatePreview();}
    }
    public void SetCurve(EasingCurve curve)
    {
        curve.Validate();RestoreDraft(new Dictionary<string,string>
        {
            ["Function Family"]=curve.Family.ToString(),["Function Mode"]=curve.Mode.ToString(),["Function Amplitude"]=curve.Amplitude.ToString("R",CultureInfo.InvariantCulture),
            ["Function Bounces"]=curve.Bounces.ToString(CultureInfo.InvariantCulture),["Function Bounciness"]=curve.Bounciness.ToString("R",CultureInfo.InvariantCulture),
            ["Function Oscillations"]=curve.Oscillations.ToString(CultureInfo.InvariantCulture),["Function Springiness"]=curve.Springiness.ToString("R",CultureInfo.InvariantCulture),
            ["Function Exponent"]=curve.Exponent.ToString("R",CultureInfo.InvariantCulture),["Function Power"]=curve.Power.ToString("R",CultureInfo.InvariantCulture)
        });
    }
    public EasingCurve ReadCurve()
    {
        if(!Enum.TryParse<EasingFamily>(_family.SelectedItem as string,out var family)||!Enum.TryParse<EasingDirection>(_mode.SelectedItem as string,out var mode))throw new InvalidDataException("Choose an easing family and direction.");
        double Number(string key)=>double.TryParse(_fields[key].Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:throw new InvalidDataException(key+" must be a finite number.");
        int Count(string key)=>int.TryParse(_fields[key].Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)?value:throw new InvalidDataException(key+" must be an integer.");
        // Inactive fields remain drafts, not irrelevant data in a committed function.
        var c=new EasingCurve(family,mode);
        c=family switch
        {
            EasingFamily.Back=>c with{Amplitude=Number("Amplitude")},EasingFamily.Bounce=>c with{Bounces=Count("Bounces"),Bounciness=Number("Bounciness")},
            EasingFamily.Elastic=>c with{Oscillations=Count("Oscillations"),Springiness=Number("Springiness")},EasingFamily.Exponential=>c with{Exponent=Number("Exponent")},
            EasingFamily.Power=>c with{Power=Number("Power")},_=>c
        };
        c.Validate();return c;
    }
    private void Changed(){if(_syncing)return;UpdatePreview();DraftChanged?.Invoke(this,EventArgs.Empty);}
    private void UpdatePreview()
    {
        var family=_family.SelectedItem as string;
        foreach(var row in _rows)row.Value.Visibility=(family,row.Key) switch
        {
            ("Back","Amplitude") or ("Bounce","Bounces" or "Bounciness") or ("Elastic","Oscillations" or "Springiness") or ("Exponential","Exponent") or ("Power","Power")=>Visibility.Visible,_=>Visibility.Collapsed
        };
        if(_fields.Count==0)return;
        try{_preview.Curve=ReadCurve();_error.Text="";}catch(Exception e)when(e is InvalidDataException or ArgumentException){_error.Text=e.Message;}
    }
    public void Dispose()=>_preview.Dispose();
}
