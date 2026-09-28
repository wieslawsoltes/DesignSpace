using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace DesignSpace.Controls.Uno;

/// <summary>Reusable stroke sample. It previews draft settings without modifying the design document.</summary>
public sealed class StrokePreviewControl : SKCanvasElement
{
    private StrokeStyle _style=new(4);
    public StrokeStyle Stroke { get=>_style; set{_style=value;Invalidate();} }
    public StrokePreviewControl(){Height=84;AutomationProperties.SetName(this,"Stroke appearance preview");}
    protected override void RenderOverride(SKCanvas canvas,Size area)
    {
        canvas.Clear(new SKColor(35,35,38));using var center=new SKPath();
        center.MoveTo(22,56);center.LineTo((float)area.Width*.45f,56);center.LineTo((float)area.Width*.6f,25);center.LineTo((float)area.Width-22,25);
        using var paint=new SKPaint{IsAntialias=true,Color=new SKColor(105,184,245)};
        try{using var outline=SkiaStrokeGeometry.Create(center,_style);canvas.DrawPath(outline,paint);}
        catch(Exception e)when(e is ArgumentException or InvalidDataException or InvalidOperationException){ /* Keep invalid drafts editable; the editor reports validation errors. */ }
    }
}

/// <summary>Draft-safe atomic stroke authoring, independently composable around a DesignSession.</summary>
public sealed partial class StrokeEditorControl : Grid,IDisposable
{
    private const string Mixed="<multiple>";
    private readonly DesignSession _session;
    private readonly StackPanel _body=new(){Spacing=6,Padding=new Thickness(8)};
    private readonly TextBlock _status=StudioTheme.Text("Base stroke values",11,"#E6B36D");
    private readonly StrokePreviewControl _preview=new();
    private readonly Dictionary<string,Func<string>> _values=[];
    private readonly Dictionary<string,Action<string>> _setters=[];
    private readonly Dictionary<string,string> _original=[];
    private ImmutableHashSet<Guid> _targets=[];
    private long _revision;
    private bool _syncing,_dirty,_applying;
    private ImmutableHashSet<Guid>? _restoreTargets;
    public Func<bool>? CanEditBase { get; set; }
    public event EventHandler? OutlineRequested;
    public event EventHandler<string>? Error;
    public StrokeEditorControl(DesignSession session)
    {
        _session=session;RowDefinitions.Add(new(){Height=new GridLength(28)});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal};toolbar.Children.Add(new StudioButton("Apply stroke",Apply,"Apply stroke settings"));toolbar.Children.Add(new StudioButton("Reload",()=>Refresh(true),"Reload stroke settings"));Children.Add(toolbar);
        var scroll=new ScrollViewer{Content=_body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};SetRow(scroll,1);Children.Add(scroll);
        _session.DocumentChanged+=Changed;_session.SelectionChanged+=Changed;Refresh(true);
    }
    private void Changed(object? sender,EventArgs e){if(!_applying)Refresh();}
    private void Refresh(bool discard=false)
    {
        if(discard)_workspaceOrphan=null;
        if(_dirty&&!discard){_status.Text="Draft retained. Apply only to its original revision, or Reload.";return;}
        _syncing=true;
        try
        {
            _body.Children.Clear();_values.Clear();_setters.Clear();_original.Clear();_targets=_restoreTargets ?? _session.Selection;_revision=_session.Revision;_dirty=false;
            _status.Text="Base stroke values";_status.TextWrapping=TextWrapping.Wrap;_body.Children.Add(_status);
            var nodes=_targets.Select(_session.Index.Find).OfType<DesignNode>().ToArray();
            if(nodes.Length==0||nodes.Any(n=>!VectorGeometry.IsShape(n))){_body.Children.Add(StudioTheme.Text("Select vector shapes to edit their strokes."));return;}
            _body.Children.Add(StudioTheme.Text(nodes.Length==1 ? nodes[0].Name : nodes.Length+" shapes",13));
            string Common(string key,string fallback)=>nodes.Select(n=>n.Get(key,fallback)).Distinct().Count()==1 ? nodes[0].Get(key,fallback) : Mixed;
            void Field(string label,string key,string fallback,string[]? choices=null)
            {
                var value=Common(key,fallback);_original[key]=value;
                var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(99)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.Children.Add(StudioTheme.Text(label));
                FrameworkElement control;
                if(choices is null)
                {
                    var input=StudioTheme.Input(value,"Stroke "+label);input.TextChanged+=(_,_)=>DraftChanged();_values[key]=()=>input.Text;_setters[key]=v=>input.Text=v;control=input;
                }
                else
                {
                    var box=new ComboBox{ItemsSource=choices.Contains(value)?choices:new[]{value}.Concat(choices).ToArray(),SelectedItem=value,MinHeight=25,MinWidth=0,FontSize=11,Padding=new Thickness(4,1,4,1),HorizontalAlignment=HorizontalAlignment.Stretch};
                    AutomationProperties.SetName(box,"Stroke "+label);box.SelectionChanged+=(_,_)=>DraftChanged();_values[key]=()=>box.SelectedItem as string??value;_setters[key]=v=>box.SelectedItem=v;control=box;
                }
                SetColumn(control,1);row.Children.Add(control);_body.Children.Add(row);
            }
            Field("Brush","Stroke","");Field("Width","StrokeThickness","1");
            Field("Start cap","StrokeStartLineCap","Flat",Enum.GetNames<DesignLineCap>());Field("End cap","StrokeEndLineCap","Flat",Enum.GetNames<DesignLineCap>());
            Field("Dash cap","StrokeDashCap","Flat",Enum.GetNames<DesignLineCap>());Field("Join","StrokeLineJoin","Miter",Enum.GetNames<DesignLineJoin>());
            Field("Miter limit","StrokeMiterLimit","10");Field("Dash array","StrokeDashArray","");Field("Dash offset","StrokeDashOffset","0");
            var presets=new StackPanel{Orientation=Orientation.Horizontal,Spacing=2};
            foreach(var (name,pattern,cap) in new[]{("Solid","","Flat"),("Dash","2 2","Flat"),("Dot","0 2","Round"),("Dash dot","2 2 0 2","Round")})
                presets.Children.Add(new StudioButton(name,()=>{_syncing=true;_setters["StrokeDashArray"](pattern);_setters["StrokeDashCap"](cap);_syncing=false;DraftChanged();},"Stroke preset "+name));
            _body.Children.Add(presets);_body.Children.Add(_preview);
            var note=StudioTheme.Text("Dash values and offset are multiples of Width. Changes stay in this draft until Apply. This panel edits base values, not recorded states or keyframes.",11,"#AAAAB3");note.TextWrapping=TextWrapping.Wrap;_body.Children.Add(note);
            _body.Children.Add(new StudioButton("Convert stroke to path",()=>
            {
                try{if(_dirty)throw new InvalidOperationException("Apply or reload the stroke draft before outlining.");GuardContext();if(OutlineRequested is null)throw new InvalidOperationException("The host has not supplied stroke outlining.");OutlineRequested.Invoke(this,EventArgs.Empty);}
                catch(Exception e){Report(e.Message);}
            },"Convert stroke to path"));
        }
        finally{_syncing=false;UpdatePreview();}
    }
    private void DraftChanged()
    {
        if(_syncing)return;_dirty=_values.Any(p=>p.Value()!=_original[p.Key]);_status.Text=_dirty?"Unapplied stroke draft":"Base stroke values";UpdatePreview();
    }
    private void UpdatePreview()
    {
        if(_values.Count==0)return;
        try
        {
            var node=new DesignNode();foreach(var p in _values){var value=p.Value();if(value!=Mixed)node=node.Set(p.Key,value);}_preview.Stroke=StrokeStyle.Read(node);
        }
        catch(Exception e)when(e is InvalidDataException or ArgumentException){_status.Text=e.Message;}
    }
    private void GuardContext()
    {
        if(CanEditBase?.Invoke()==false)throw new InvalidOperationException("Leave state/keyframe recording before editing base stroke settings.");
        if(_revision!=_session.Revision||!_targets.SetEquals(_session.Selection))throw new InvalidOperationException("The selection or document changed. Reload the stroke draft before applying.");
    }
    public void Apply()
    {
        try
        {
            GuardContext();var changes=_values.Where(p=>p.Value()!=Mixed&&p.Value()!=_original[p.Key]).ToDictionary(p=>p.Key,p=>p.Value());
            if(changes.Count==0)return;
            _applying=true;
            try{_session.Execute("Edit stroke settings",d=>StrokeEditing.Apply(d,_targets,changes));}
            finally{_applying=false;}
            _dirty=false;Refresh(true);_status.Text="Stroke settings applied";
        }
        catch(Exception e){Report(e.Message);}
    }
    private void Report(string message){_status.Text=message;Error?.Invoke(this,message);}
    public void Dispose(){_session.DocumentChanged-=Changed;_session.SelectionChanged-=Changed;}
}
