using System.Collections.Immutable;
using System.Globalization;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;
using DesignSpace.Rendering.Skia;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace DesignSpace.Controls.Uno;

/// <summary>Host-canvas sample with retained native filters. It never edits a document.</summary>
public sealed class EffectPreviewControl : SKCanvasElement,IDisposable
{
    private readonly EffectFilterCache _cache=new();
    private readonly SKPaint _fill=new(){IsAntialias=true,Color=new SKColor(0,120,212)};
    private readonly SKPaint _text=new(){IsAntialias=true,Color=SKColors.White};
    private readonly SKFont _font=new(DesignTypography.DefaultTypeface,23);
    private string? _source;
    public EffectPreviewControl(){Height=145;AutomationProperties.SetName(this,"Effect appearance preview");}
    public void SetEffect(DesignEffect? value)
    {
        var source=value is null?null:EffectCodec.Write(value);
        if(source==_source)return;_source=source;Invalidate();
    }
    protected override void RenderOverride(SKCanvas canvas,Size area)
    {
        canvas.Clear(new SKColor(229,233,240));
        using var paint=new SKPaint{ImageFilter=_source is null?null:_cache.GetForCanvas(_source,canvas)};
        var saved=canvas.SaveCount;canvas.SaveLayer(paint);
        try
        {
            var x=(float)area.Width/2-46;canvas.DrawRoundRect(SKRect.Create(x,37,92,62),9,9,_fill);
            canvas.DrawText("Aa",x+29,78,_font,_text);
        }
        finally{canvas.RestoreToCount(saved);}
    }
    public void Dispose(){_cache.Dispose();_fill.Dispose();_text.Dispose();_font.Dispose();}
}

/// <summary>Validated local-effect authoring with explicit replacement, revision guards and document-owned drafts.</summary>
public sealed class EffectEditorControl : Grid,IWorkspaceDraftEditor,IDisposable
{
    private readonly DesignSession _session;
    private readonly ComboBox _kind=new(){ItemsSource=new[]{"None","Blur","DropShadow","Mixed","Unsupported"},SelectedIndex=0,MinHeight=26,FontSize=11};
    private readonly ComboBox _kernel=new(){ItemsSource=Enum.GetNames<DesignBlurKernel>(),SelectedIndex=0,MinHeight=26,FontSize=11};
    private readonly ComboBox _bias=new(){ItemsSource=new[]{"Performance","Quality"},SelectedIndex=0,MinHeight=26,FontSize=11};
    private readonly Dictionary<string,TextBox> _fields=[];
    private readonly Dictionary<string,FrameworkElement> _rows=[];
    private readonly TextBlock _status=StudioTheme.Text("",11,"#E6B36D");
    private readonly TextBlock _title=StudioTheme.Text("Effects",13,"#FFFFFF");
    private readonly EffectPreviewControl _preview=new();
    private ImmutableHashSet<Guid> _targets=[];
    private ImmutableDictionary<string,string> _original=ImmutableDictionary<string,string>.Empty;
    private DesignerPanelDraft? _orphan;
    private long _revision;
    private bool _syncing,_applying;
    private bool Dirty=>_orphan is not null||Fields().Any(p=>_original.GetValueOrDefault(p.Key)!=p.Value);
    public Func<bool>? CanEditBase { get; set; }
    public event EventHandler<string>? Error;
    public EffectEditorControl(DesignSession session)
    {
        _session=session;_syncing=true;
        RowDefinitions.Add(new(){Height=new GridLength(30)});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var commands=new StackPanel{Orientation=Orientation.Horizontal};
        commands.Children.Add(new StudioButton("Apply effect",Apply,"Apply effect settings"));
        commands.Children.Add(new StudioButton("Reload",()=>Reload(true),"Reload effect settings"));Children.Add(commands);
        var body=new StackPanel{Spacing=7,Padding=new Thickness(9)};
        var scroll=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};AutomationProperties.SetName(scroll,"Effect settings scroll area");SetRow(scroll,1);Children.Add(scroll);
        body.Children.Add(_title);_status.TextWrapping=TextWrapping.Wrap;body.Children.Add(_status);
        void Choice(string key,ComboBox box)
        {
            var row=new StackPanel{Spacing=3};row.Children.Add(StudioTheme.Text(key));row.Children.Add(box);box.HorizontalAlignment=HorizontalAlignment.Stretch;
            AutomationProperties.SetName(box,"Effect "+key);box.SelectionChanged+=(_,_)=>DraftChanged();body.Children.Add(row);_rows[key]=row;
        }
        void Field(string key,string value)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(91)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            row.Children.Add(StudioTheme.Text(key));var input=StudioTheme.Input(value,"Effect "+key);SetColumn(input,1);row.Children.Add(input);_fields[key]=input;_rows[key]=row;
            input.TextChanged+=(_,_)=>DraftChanged();body.Children.Add(row);
        }
        Choice("Type",_kind);Field("Radius","5");Choice("Kernel",_kernel);
        Field("Direction","315");Field("Depth","5");Field("Opacity","1");Field("Color","Black");Choice("Rendering bias",_bias);
        var presets=new StackPanel{Orientation=Orientation.Horizontal,Spacing=2};
        foreach(var (name,effect) in new[]{("Soft shadow",new DesignEffect{Kind=DesignEffectKind.DropShadow,Radius=14,ShadowDepth=8,Opacity=.45}),
            ("Hard shadow",new DesignEffect{Kind=DesignEffectKind.DropShadow,Radius=0,ShadowDepth=16,Direction=0}),
            ("Blur",new DesignEffect{Radius=8})})
            presets.Children.Add(new StudioButton(name,()=>{SetFields(Values(effect,effect.Kind.ToString()));DraftChanged();},"Effect preset "+name));
        body.Children.Add(presets);body.Children.Add(_preview);
        var note=StudioTheme.Text("Effects apply to the object and its children. Apply creates a local copy of a referenced effect. None disables the effect; Reset to style removes the local override. Preview settings do not change PNG export. Rendering bias is retained metadata; both modes currently use the same kernel.",11,"#AAAAB3");note.TextWrapping=TextWrapping.Wrap;body.Children.Add(note);
        body.Children.Add(new StudioButton("Reset to style",Reset,"Reset effect to style"));
        _session.DocumentChanged+=Changed;_session.SelectionChanged+=Changed;_syncing=false;Reload(true);
    }
    private static string N(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
    private static ImmutableDictionary<string,string> Values(DesignEffect effect,string kind)=>new Dictionary<string,string>
    {
        ["Type"]=kind,["Radius"]=N(effect.Radius),["Kernel"]=effect.Kernel.ToString(),["Direction"]=N(effect.Direction),["Depth"]=N(effect.ShadowDepth),
        ["Opacity"]=N(effect.Opacity),["Color"]=effect.Color,["Rendering bias"]=effect.RenderingBias
    }.ToImmutableDictionary();
    private ImmutableDictionary<string,string> Fields()=>_fields.ToImmutableDictionary(p=>p.Key,p=>p.Value.Text)
        .Add("Type",_kind.SelectedItem as string??"Unsupported").Add("Kernel",_kernel.SelectedItem as string??"Gaussian").Add("Rendering bias",_bias.SelectedItem as string??"Performance");
    private void SetFields(IReadOnlyDictionary<string,string> values)
    {
        _syncing=true;
        try
        {
            foreach(var (key,input) in _fields)if(values.TryGetValue(key,out var value))input.Text=value;
            _kind.SelectedItem=values.GetValueOrDefault("Type","None");_kernel.SelectedItem=values.GetValueOrDefault("Kernel","Gaussian");_bias.SelectedItem=values.GetValueOrDefault("Rendering bias","Performance");
        }
        finally{_syncing=false;}
    }
    private DesignEffect? ReadEffect()
    {
        var kind=_kind.SelectedItem as string;if(kind=="None")return null;
        if(kind is not ("Blur" or "DropShadow"))throw new InvalidOperationException("Choose an effect type or preset to explicitly replace mixed/unsupported values.");
        double Number(string name)=>double.TryParse(_fields[name].Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:throw new InvalidDataException("Enter a finite effect "+name.ToLowerInvariant()+".");
        var effect=new DesignEffect{Kind=Enum.Parse<DesignEffectKind>(kind),Radius=Number("Radius"),RenderingBias=_bias.SelectedItem as string??"Performance"};
        // Inactive fields remain draft text but cannot alter the selected effect kind.
        effect=kind=="Blur"?effect with{Kernel=Enum.Parse<DesignBlurKernel>((string)_kernel.SelectedItem)}:
            effect with{Direction=Number("Direction"),ShadowDepth=Number("Depth"),Opacity=Number("Opacity"),Color=_fields["Color"].Text};
        effect.Validate();return effect;
    }
    private void Changed(object? sender,EventArgs args)
    {
        if(_syncing||_applying)return;
        if(_revision==_session.Revision&&_targets.SetEquals(_session.Selection))return;
        Reload();
    }
    private void Reload(bool discard=false)
    {
        if(!discard&&Dirty){_status.Text="Effect draft retained. Reload if the selection or document changed.";return;}
        _orphan=null;_targets=_session.Selection;_revision=_session.Revision;
        try
        {
            var root=DesignPreview.Resolve(_session.Document.Root);var resolver=new BrushResolver(root);var effects=_targets.Select(id=>resolver.Resolve(id,"Effect")).Select(s=>s is null?null:EffectCodec.Parse(s)).Distinct().ToArray();
            var effect=effects.FirstOrDefault();var kind=effects.Length>1?"Mixed":effect?.Kind.ToString()??"None";
            SetFields(Values(effect??new(),kind));_title.Text=_targets.Count==1?"Effect · "+(_session.Index.Find(_targets.Single())?.Name??"?"):"Effects · "+_targets.Count+" objects";
            _status.Text=_targets.Count==0?"Select an object to edit its effect.":kind=="Mixed"?"Different effects selected. Choose a type to replace all selected effects.":"Effective values. Apply writes a local override.";
        }
        catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException)
        {SetFields(Values(new(),"Unsupported"));_status.Text=e.Message+" The original markup is preserved until explicit replacement.";}
        _original=Fields();UpdatePreview();
    }
    private void DraftChanged(){if(_syncing)return;_status.Text=Dirty?"Unapplied effect draft":"Effective effect values";UpdatePreview();}
    private void UpdatePreview()
    {
        var kind=_kind.SelectedItem as string;var visible=kind is "Blur" or "DropShadow";
        foreach(var key in new[]{"Radius","Rendering bias"})_rows[key].Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        _rows["Kernel"].Visibility=kind=="Blur"?Visibility.Visible:Visibility.Collapsed;
        foreach(var key in new[]{"Direction","Depth","Opacity","Color"})_rows[key].Visibility=kind=="DropShadow"?Visibility.Visible:Visibility.Collapsed;
        if(kind is "Mixed" or "Unsupported"){_preview.SetEffect(null);return;}
        try{_preview.SetEffect(ReadEffect());}catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException){_status.Text=e.Message;}
    }
    private void Guard()
    {
        if(_orphan is not null)throw new InvalidOperationException("Retained draft has unsupported metadata. Reload before applying.");
        if(CanEditBase?.Invoke()==false)throw new InvalidOperationException("Leave state/keyframe recording before editing base effects.");
        if(_revision!=_session.Revision||!_targets.SetEquals(_session.Selection))throw new InvalidOperationException("Effect draft is stale. Reload before applying.");
    }
    public void Apply()
    {
        try
        {
            Guard();if(!Dirty)return;var effect=ReadEffect();using var check=effect is null?null:EffectFilterCache.Create(effect);
            _applying=true;try{_session.Execute("Edit effect",d=>EffectEditing.Apply(d,_targets,effect));}finally{_applying=false;}
            Reload(true);_status.Text="Effect applied";
        }
        catch(Exception e){Report(e.Message);}
    }
    private void Reset()
    {
        try
        {
            Guard();if(Dirty)throw new InvalidOperationException("Apply or Reload the effect draft before resetting to style.");
            _applying=true;try{_session.Execute("Reset effect to style",d=>EffectEditing.Reset(d,_targets));}finally{_applying=false;}
            Reload(true);_status.Text="Local effect override removed";
        }
        catch(Exception e){Report(e.Message);}
    }
    private void Report(string message){_status.Text=message;Error?.Invoke(this,message);}
    public DesignerPanelDraft CaptureWorkspaceDraft()=>_orphan??new(){Values=Fields(),Originals=_original,Targets=_targets.ToImmutableArray(),HasChanges=Dirty,MatchesDesign=_revision==_session.Revision};
    public void RestoreWorkspaceDraft(DesignerPanelDraft? state)
    {
        Reload(true);if(state?.HasChanges!=true)return;
        if(!new[]{"None","Blur","DropShadow","Mixed","Unsupported"}.Contains(state.Values.GetValueOrDefault("Type"))||
           !Enum.GetNames<DesignBlurKernel>().Contains(state.Values.GetValueOrDefault("Kernel"))||
           state.Values.GetValueOrDefault("Rendering bias") is not ("Performance" or "Quality"))
        {_orphan=state with{MatchesDesign=false};_status.Text="Unsupported effect draft preserved; Reload explicitly discards it.";return;}
        _targets=state.Targets.ToImmutableHashSet();_revision=state.MatchesDesign?_session.Revision:-1;SetFields(state.Values);_original=state.Originals;
        _status.Text="Retained effect draft for this document";UpdatePreview();
    }
    public void Dispose(){_session.DocumentChanged-=Changed;_session.SelectionChanged-=Changed;_preview.Dispose();}
}
