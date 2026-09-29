using System.Collections.Immutable;
using System.Globalization;
using DesignSpace.Core;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

/// <summary>Reusable validated preference editor. The host owns application and per-document restoration.</summary>
public sealed class ArtboardSettingsControl : Grid,IWorkspaceDraftEditor
{
    private readonly Func<ArtboardSettings> _read;
    private readonly Action<ArtboardSettings> _apply;
    private readonly Dictionary<string,CheckBox> _checks=[];
    private readonly Dictionary<string,TextBox> _numbers=[];
    private readonly TextBlock _status=StudioTheme.Text("",11,"#B8C5D5");
    private ImmutableDictionary<string,string> _original=ImmutableDictionary<string,string>.Empty;
    private bool _syncing;
    private bool Dirty=>Fields().Any(p=>_original.GetValueOrDefault(p.Key)!=p.Value);
    public event EventHandler<string>? Error;
    public ArtboardSettingsControl(Func<ArtboardSettings> read,Action<ArtboardSettings> apply)
    {
        _read=read??throw new ArgumentNullException(nameof(read));_apply=apply??throw new ArgumentNullException(nameof(apply));
        RowDefinitions.Add(new(){Height=new GridLength(30)});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal};
        toolbar.Children.Add(new StudioButton("Apply",Apply,"Apply artboard settings"));toolbar.Children.Add(new StudioButton("Reload",Reload,"Reload artboard settings"));Children.Add(toolbar);
        var body=new StackPanel{Padding=new Thickness(10),Spacing=6};var scroll=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        AutomationProperties.SetName(scroll,"Artboard settings scroll area");SetRow(scroll,1);Children.Add(scroll);
        void Check(string key,string label)
        {
            var box=new CheckBox{Content=label,MinHeight=28};AutomationProperties.SetName(box,"Artboard "+key);_checks.Add(key,box);body.Children.Add(box);
            box.Checked+=(_,_)=>Changed();box.Unchecked+=(_,_)=>Changed();
        }
        void Number(string key,string label)
        {
            body.Children.Add(StudioTheme.Text(label));var input=StudioTheme.Input("","Artboard "+key);_numbers.Add(key,input);body.Children.Add(input);input.TextChanged+=(_,_)=>Changed();
        }
        body.Children.Add(StudioTheme.Text("Artboard · snapping",13,"#FFFFFF"));
        Check("Show grid","Show snap grid");Check("Show rulers","Show rulers");Check("Snap grid","Snap to gridlines");Number("Grid spacing","Gridline spacing (design units)");
        Check("Snap lines","Snap to snaplines");Number("Tolerance","Snap tolerance (screen pixels)");Number("Margin","Default margin (design units)");Number("Padding","Default padding (design units)");
        var note=StudioTheme.Text("Align Canvas siblings by their edges or centers. Margin guides appear between overlapping rows/columns; padding guides sit inside the parent content area. Hold Alt to bypass snapping. Snaplines take priority over grid snapping on the matching axis.",11,"#AAAAB3");note.TextWrapping=TextWrapping.Wrap;body.Children.Add(note);
        _status.TextWrapping=TextWrapping.Wrap;body.Children.Add(_status);Reload();
    }
    private ImmutableDictionary<string,string> Fields()=>_checks.ToImmutableDictionary(p=>p.Key,p=>(p.Value.IsChecked==true).ToString()).AddRange(_numbers.Select(p=>new KeyValuePair<string,string>(p.Key,p.Value.Text)));
    private static ImmutableDictionary<string,string> Values(ArtboardSettings s)=>new Dictionary<string,string>
    {
        ["Show grid"]=s.ShowGrid.ToString(),["Show rulers"]=s.ShowRulers.ToString(),["Snap grid"]=s.SnapToGrid.ToString(),["Snap lines"]=s.SnapToSnaplines.ToString(),
        ["Grid spacing"]=s.GridSize.ToString("R",CultureInfo.InvariantCulture),["Tolerance"]=s.SnapTolerance.ToString("R",CultureInfo.InvariantCulture),
        ["Margin"]=s.DefaultMargin.ToString("R",CultureInfo.InvariantCulture),["Padding"]=s.DefaultPadding.ToString("R",CultureInfo.InvariantCulture)
    }.ToImmutableDictionary();
    private static ArtboardSettings Parse(IReadOnlyDictionary<string,string> values)
    {
        double N(string key)=>double.TryParse(values.GetValueOrDefault(key),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:throw new InvalidDataException("Enter a finite "+key.ToLowerInvariant()+" value.");
        bool B(string key)=>bool.TryParse(values.GetValueOrDefault(key),out var b)?b:throw new InvalidDataException("Invalid artboard option: "+key);
        var settings=new ArtboardSettings{ShowGrid=B("Show grid"),ShowRulers=B("Show rulers"),SnapToGrid=B("Snap grid"),SnapToSnaplines=B("Snap lines"),GridSize=N("Grid spacing"),SnapTolerance=N("Tolerance"),DefaultMargin=N("Margin"),DefaultPadding=N("Padding")};
        settings.Validate();return settings;
    }
    private void SetFields(IReadOnlyDictionary<string,string> values)
    {
        _syncing=true;
        try{foreach(var (key,box) in _checks)box.IsChecked=values.GetValueOrDefault(key)=="True";foreach(var (key,input) in _numbers)input.Text=values.GetValueOrDefault(key,"");}
        finally{_syncing=false;}
    }
    private void Changed(){if(!_syncing)_status.Text=Dirty?"Unapplied artboard settings":"Artboard settings synchronized";}
    public void Refresh(){if(!Dirty)Reload();}
    public void Reload(){_original=Values(_read());SetFields(_original);_status.Text="Preferences belong to this document; geometry and undo are unchanged.";}
    private void Apply()
    {
        try
        {
            var settings=Parse(Fields());
            if(Parse(_original)!=_read())throw new InvalidOperationException("Artboard settings changed while this draft was open. Reload before applying.");
            _apply(settings);Reload();_status.Text="Artboard settings applied";
        }
        catch(Exception e){_status.Text=e.Message;Error?.Invoke(this,e.Message);}
    }
    public DesignerPanelDraft CaptureWorkspaceDraft()=>new(){Values=Fields(),Originals=_original,HasChanges=Dirty};
    public void RestoreWorkspaceDraft(DesignerPanelDraft? state)
    {
        Reload();if(state?.HasChanges!=true)return;SetFields(state.Values);_original=state.Originals;_status.Text="Retained artboard settings draft for this document";
    }
}
