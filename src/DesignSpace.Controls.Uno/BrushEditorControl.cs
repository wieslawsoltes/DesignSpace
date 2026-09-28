using System.Collections.Immutable;
using System.Globalization;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

/// <summary>Reusable local-brush editor with explicit apply, inert per-document drafts and editable gradient stops.</summary>
public sealed class BrushEditorControl : Grid,IWorkspaceDraftEditor,IDisposable
{
    private readonly DesignSession _session;
    private readonly ComboBox _property=new(){ItemsSource=BrushEditing.Properties,SelectedItem="Fill",MinHeight=25,FontSize=11};
    private readonly ComboBox _kind=new(){ItemsSource=new[]{"None","Solid","Linear","Radial","Unsupported"},SelectedItem="Linear",MinHeight=25,FontSize=11};
    private readonly TextBlock _status=StudioTheme.Text("",11,"#E6B36D");
    private readonly GradientPreviewControl _preview=new();
    private readonly GradientPreviewControl _strip=new(){IsStopStrip=true,Height=48};
    private readonly StackPanel _stops=new(){Spacing=3};
    private readonly StackPanel _gradient=new(){Spacing=4};
    private readonly StackPanel _linear=new(){Spacing=4};
    private readonly StackPanel _radial=new(){Spacing=4};
    private readonly Dictionary<string,TextBox> _fields=[];
    private readonly List<(TextBox Offset,TextBox Color)> _stopFields=[];
    private readonly ComboBox _mapping=new(){ItemsSource=Enum.GetNames<DesignBrushMapping>(),SelectedIndex=0,MinHeight=25,FontSize=11};
    private readonly ComboBox _spread=new(){ItemsSource=Enum.GetNames<DesignGradientSpread>(),SelectedIndex=0,MinHeight=25,FontSize=11};
    private ImmutableDictionary<string,string> _original=ImmutableDictionary<string,string>.Empty;
    private ImmutableHashSet<Guid> _targets=[];
    private string _activeProperty="Fill";
    private long _revision;
    private DesignerPanelDraft? _orphan;
    private int _selected;
    private bool _syncing,_applying;
    public Func<bool>? CanEditBase {get;set;}
    public event EventHandler<string>? Error;
    private bool Dirty=>_orphan is not null||Fields().Any(p=>_original.GetValueOrDefault(p.Key)!=p.Value);
    public BrushEditorControl(DesignSession session)
    {
        _session=session;RowDefinitions.Add(new(){Height=new GridLength(29)});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal};toolbar.Children.Add(new StudioButton("Apply brush",Apply,"Apply gradient brush"));toolbar.Children.Add(new StudioButton("Reload",()=>Reload(true),"Reload gradient brush"));Children.Add(toolbar);
        var body=new StackPanel{Spacing=5,Padding=new Thickness(8)};var scroll=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};SetRow(scroll,1);Children.Add(scroll);
        void Choice(StackPanel panel,string label,ComboBox box)
        {
            panel.Children.Add(StudioTheme.Text(label));AutomationProperties.SetName(box,"Gradient "+label);box.HorizontalAlignment=HorizontalAlignment.Stretch;panel.Children.Add(box);
        }
        TextBox Field(StackPanel panel,string label,string value)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(88)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.Children.Add(StudioTheme.Text(label));
            var input=StudioTheme.Input(value,"Gradient "+label);SetColumn(input,1);row.Children.Add(input);panel.Children.Add(row);_fields[label]=input;input.TextChanged+=(_,_)=>ChangedDraft();return input;
        }
        Choice(body,"Property",_property);Choice(body,"Kind",_kind);_status.TextWrapping=TextWrapping.Wrap;body.Children.Add(_status);
        AutomationProperties.SetName(_preview,"Gradient brush preview");AutomationProperties.SetName(_strip,"Gradient stop strip");body.Children.Add(_preview);
        Field(body,"Color","#FF0078D4");Field(body,"Opacity","1");body.Children.Add(_gradient);_gradient.Children.Add(_strip);_gradient.Children.Add(_stops);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=2};
        tools.Children.Add(new StudioButton("+ Stop",()=>ModifyStops(b=>BrushEditing.AddStop(b,.5)),"Add gradient stop"));
        tools.Children.Add(new StudioButton("− Stop",()=>ModifyStops(b=>BrushEditing.RemoveStop(b,_selected)),"Remove gradient stop"));
        tools.Children.Add(new StudioButton("Reverse",()=>ModifyStops(BrushEditing.Reverse),"Reverse gradient stops"));_gradient.Children.Add(tools);
        Choice(_gradient,"Mapping",_mapping);Choice(_gradient,"Spread",_spread);_gradient.Children.Add(_linear);_gradient.Children.Add(_radial);
        Field(_linear,"Start","0,0");Field(_linear,"End","1,1");Field(_radial,"Center","0.5,0.5");Field(_radial,"Origin","0.5,0.5");Field(_radial,"Radius X","0.5");Field(_radial,"Radius Y","0.5");
        var directions=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(var (name,end) in new[]{("Horizontal","1,0"),("Vertical","0,1"),("Diagonal","1,1")})directions.Children.Add(new StudioButton(name,()=>{_fields["Start"].Text="0,0";_fields["End"].Text=end;},"Gradient direction "+name));_linear.Children.Add(directions);
        Field(body,"Transform","1,0,0,1,0,0");Field(body,"Relative","1,0,0,1,0,0");
        var note=StudioTheme.Text("Edit stop offsets/colors or drag their markers. Apply writes a local brush, not the shared resource. Drafts follow document tabs. Matrices use m11,m12,m21,m22,dx,dy.",11,"#AAAAB3");note.TextWrapping=TextWrapping.Wrap;body.Children.Add(note);
        _preview.Error+=(_,message)=>_status.Text=message;_strip.Error+=(_,message)=>_status.Text=message;
        _property.SelectionChanged+=(_,_)=>
        {
            if(_syncing)return;if(Dirty){_syncing=true;_property.SelectedItem=_activeProperty;_syncing=false;Report("Apply or Reload this brush draft before changing its target property.");return;}
            _activeProperty=_property.SelectedItem as string??"Fill";Reload(true);
        };
        _kind.SelectionChanged+=(_,_)=>
        {
            if(_syncing)return;
            if((_kind.SelectedItem as string) is "Linear" or "Radial" && _stopFields.Count==0)
            {
                _syncing=true;try{SetStops(new[]{("0",_fields["Color"].Text),("1","#FFFFFFFF")});}finally{_syncing=false;}
            }
            ChangedDraft();
        };_mapping.SelectionChanged+=(_,_)=>ChangedDraft();_spread.SelectionChanged+=(_,_)=>ChangedDraft();
        _strip.StopSelected+=(_,index)=>_selected=index;
        _strip.StopMoved+=(_,change)=>{if(change.Index<_stopFields.Count)_stopFields[change.Index].Offset.Text=change.Offset.ToString("0.####",CultureInfo.InvariantCulture);};
        _session.DocumentChanged+=DocumentChanged;_session.SelectionChanged+=DocumentChanged;Reload(true);
    }
    private void DocumentChanged(object? sender,EventArgs e){if(!_applying)Reload();}
    private ImmutableDictionary<string,string> Fields()=>_fields.ToImmutableDictionary(p=>p.Key,p=>p.Value.Text)
        .Add("Kind",_kind.SelectedItem as string??"Unsupported").Add("Mapping",_mapping.SelectedItem as string??"RelativeToBoundingBox").Add("Spread",_spread.SelectedItem as string??"Pad")
        .Add("Stops",string.Join("\n",_stopFields.Select(p=>p.Offset.Text+"\t"+p.Color.Text)));
    private void SetStops(IEnumerable<(string Offset,string Color)> stops)
    {
        _stops.Children.Clear();_stopFields.Clear();
        var entries=stops.ToArray();if(entries.Length>BrushCodec.MaxStops)throw new InvalidDataException("The retained draft exceeds the stop budget.");
        foreach(var stop in entries)
        {
            var index=_stopFields.Count;var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(62)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var offset=StudioTheme.Input(stop.Offset,"Gradient stop "+index+" offset");var color=StudioTheme.Input(stop.Color,"Gradient stop "+index+" color");SetColumn(color,1);row.Children.Add(offset);row.Children.Add(color);_stops.Children.Add(row);_stopFields.Add((offset,color));
            offset.GotFocus+=(_,_)=>{_selected=index;_strip.SelectedStop=index;};color.GotFocus+=(_,_)=>{_selected=index;_strip.SelectedStop=index;};offset.TextChanged+=(_,_)=>ChangedDraft();color.TextChanged+=(_,_)=>ChangedDraft();
        }
        _selected=Math.Clamp(_selected,0,Math.Max(0,_stopFields.Count-1));_strip.SelectedStop=_selected;
    }
    private static string Number(double n)=>n.ToString("R",CultureInfo.InvariantCulture);
    private static string Point(DPoint p)=>Number(p.X)+","+Number(p.Y);
    private static string Matrix(DMatrix m)=>string.Join(",",new[]{m.M11,m.M12,m.M21,m.M22,m.DX,m.DY}.Select(Number));
    private void SetBrush(DesignBrush brush,string kind)
    {
        _kind.SelectedItem=kind;_mapping.SelectedItem=brush.Mapping.ToString();_spread.SelectedItem=brush.Spread.ToString();
        foreach(var (key,value) in new Dictionary<string,string>{{"Color",brush.Color},{"Opacity",Number(brush.Opacity)},{"Start",Point(brush.Start)},{"End",Point(brush.End)},{"Center",Point(brush.Center)},{"Origin",Point(brush.Origin)},{"Radius X",Number(brush.RadiusX)},{"Radius Y",Number(brush.RadiusY)},{"Transform",Matrix(brush.Transform)},{"Relative",Matrix(brush.RelativeTransform)}})_fields[key].Text=value;
        SetStops(brush.Stops.Select(s=>(Number(s.Offset),s.Color)));
    }
    private DesignBrush? ReadBrush()
    {
        var kind=_kind.SelectedItem as string;if(kind=="None")return null;
        if(!Enum.TryParse<DesignBrushKind>(kind,out var type))throw new InvalidOperationException("Choose a supported brush kind to explicitly replace preserved markup.");
        string F(string key)=>_fields[key].Text;
        var brush=new DesignBrush{Kind=type,Color=F("Color"),Opacity=BrushCodec.ReadNumber(F("Opacity")),Mapping=Enum.Parse<DesignBrushMapping>((string)_mapping.SelectedItem),Spread=Enum.Parse<DesignGradientSpread>((string)_spread.SelectedItem),Start=BrushCodec.ReadPoint(F("Start")),End=BrushCodec.ReadPoint(F("End")),Center=BrushCodec.ReadPoint(F("Center")),Origin=BrushCodec.ReadPoint(F("Origin")),RadiusX=BrushCodec.ReadNumber(F("Radius X")),RadiusY=BrushCodec.ReadNumber(F("Radius Y")),Transform=BrushCodec.Matrix(F("Transform")),RelativeTransform=BrushCodec.Matrix(F("Relative")),Stops=_stopFields.Select(p=>new DesignGradientStop(BrushCodec.ReadNumber(p.Offset.Text),p.Color.Text)).ToImmutableArray()};
        brush.Validate();return brush;
    }
    private void Reload(bool discard=false)
    {
        if(!discard&&Dirty){_status.Text="Brush draft retained. The document or selection changed; Reload before applying.";return;}
        _syncing=true;_orphan=null;
        try
        {
            _targets=_session.Selection;_revision=_session.Revision;var node=_targets.Select(_session.Index.Find).FirstOrDefault(n=>n is not null);
            var source=node is null?null:new BrushResolver(_session.Document.Root).Resolve(node.Id,_activeProperty);
            var brush=source is null?new DesignBrush():BrushCodec.Parse(source);SetBrush(brush,source is null?"None":brush.Kind.ToString());
            _status.Text=_targets.Count==0?"Select an object. Choose a property and brush kind.":_targets.Count>1?"Apply replaces this property on every selected object.":"Local brush values. Apply creates a local copy of a referenced resource.";
        }
        catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException){SetBrush(new(),"Unsupported");_status.Text=e.Message+" Preserved source will not be changed until explicit replacement.";}
        finally{_original=Fields();_syncing=false;Preview();}
    }
    private void ChangedDraft(){if(_syncing)return;_status.Text=Dirty?"Unapplied brush draft":"Local brush values";Preview();}
    private void Preview()
    {
        var kind=_kind.SelectedItem as string;_gradient.Visibility=kind is "Linear" or "Radial"?Visibility.Visible:Visibility.Collapsed;_linear.Visibility=kind=="Linear"?Visibility.Visible:Visibility.Collapsed;_radial.Visibility=kind=="Radial"?Visibility.Visible:Visibility.Collapsed;
        try{var brush=ReadBrush()??new DesignBrush{Kind=DesignBrushKind.Solid,Color="Transparent"};_preview.Brush=brush;_strip.Brush=brush;}
        catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException){_status.Text=e.Message;}
    }
    private void ModifyStops(Func<DesignBrush,DesignBrush> change)
    {
        try{var current=ReadBrush()??throw new InvalidOperationException("Choose a gradient first.");var updated=change(current);_syncing=true;try{SetStops(updated.Stops.Select(s=>(Number(s.Offset),s.Color)));}finally{_syncing=false;}ChangedDraft();}
        catch(Exception e){Report(e.Message);}
    }
    public void Apply()
    {
        try
        {
            if(_orphan is not null)throw new InvalidOperationException("Reload explicitly to discard the unsupported retained draft before applying.");
            if(CanEditBase?.Invoke()==false)throw new InvalidOperationException("Leave state/keyframe recording before editing base brushes.");
            if(_session.Revision!=_revision||!_targets.SetEquals(_session.Selection))throw new InvalidOperationException("Brush draft is stale. Reload before applying.");
            if(!Dirty)return;var brush=ReadBrush();if(brush is not null){using var shader=SkiaBrushShader.Create(brush,new(0,0,200,100));}
            _applying=true;try{_session.Execute("Edit "+_activeProperty+" brush",d=>BrushEditing.Apply(d,_targets,_activeProperty,brush));}finally{_applying=false;}
            Reload(true);_status.Text="Brush applied";
        }
        catch(Exception e){Report(e.Message);}
    }
    private void Report(string message){_status.Text=message;Error?.Invoke(this,message);}
    public DesignerPanelDraft CaptureWorkspaceDraft()=>_orphan??new(){Values=Fields().Add("Property",_activeProperty),Originals=_original,Targets=_targets.ToImmutableArray(),HasChanges=Dirty,MatchesDesign=_revision==_session.Revision};
    public void RestoreWorkspaceDraft(DesignerPanelDraft? state)
    {
        if(state is null){_activeProperty="Fill";_syncing=true;_property.SelectedItem="Fill";_syncing=false;Reload(true);return;}
        _syncing=true;_orphan=null;
        try
        {
            var property=state.Values.GetValueOrDefault("Property","Fill");
            var kind=state.Values.GetValueOrDefault("Kind","Unsupported");
            var mapping=state.Values.GetValueOrDefault("Mapping","RelativeToBoundingBox");
            var spread=state.Values.GetValueOrDefault("Spread","Pad");
            if(!BrushEditing.Properties.Contains(property)||!new[]{"None","Solid","Linear","Radial","Unsupported"}.Contains(kind)||!Enum.GetNames<DesignBrushMapping>().Contains(mapping)||!Enum.GetNames<DesignGradientSpread>().Contains(spread))
                throw new InvalidDataException("The retained brush draft contains unsupported settings.");
            _activeProperty=property;_property.SelectedItem=_activeProperty;_targets=state.Targets.ToImmutableHashSet();_revision=state.MatchesDesign?_session.Revision:-1;
            foreach(var p in _fields)if(state.Values.TryGetValue(p.Key,out var value))p.Value.Text=value;
            _kind.SelectedItem=state.Values.GetValueOrDefault("Kind","Unsupported");_mapping.SelectedItem=state.Values.GetValueOrDefault("Mapping","RelativeToBoundingBox");_spread.SelectedItem=state.Values.GetValueOrDefault("Spread","Pad");
            var stops=state.Values.GetValueOrDefault("Stops","").Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>{var at=line.IndexOf('\t');return at<0?(line,""):(line[..at],line[(at+1)..]);});SetStops(stops);_original=state.Originals;
        }
        catch(Exception e)when(e is InvalidDataException or ArgumentException){_orphan=state with{MatchesDesign=false};_status.Text=e.Message;}
        finally{_syncing=false;}
        _status.Text=state.HasChanges?"Retained brush draft for this document":"Local brush values";Preview();
    }
    public void Dispose(){_session.DocumentChanged-=DocumentChanged;_session.SelectionChanged-=DocumentChanged;_preview.Dispose();_strip.Dispose();}
}
