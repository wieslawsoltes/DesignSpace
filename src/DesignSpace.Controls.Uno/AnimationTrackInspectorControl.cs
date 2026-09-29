using System.Collections.Immutable;
using System.Globalization;
using DesignSpace.Animation;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

/// <summary>Child-clock and keyframe inspector with revision-guarded, per-document inert drafts.</summary>
public sealed class AnimationTrackInspectorControl : Grid,IWorkspaceDraftEditor,IDisposable
{
    private readonly DesignSession _session;
    private readonly TimelineControl _timeline;
    private readonly TextBlock _status=StudioTheme.Text("",11,"#E6B36D");
    private readonly TextBlock _title=StudioTheme.Text("",12);
    private readonly Dictionary<string,TextBox> _fields=[];
    private readonly ComboBox _keys=new(){MinHeight=25,FontSize=11};
    private readonly ComboBox _easing=new(){ItemsSource=new[]{"Linear","EaseIn","EaseOut","EaseInOut","Discrete","Spline","Function"},SelectedItem="Linear",MinHeight=25,FontSize=11};
    private readonly ComboBox _repeat=new(){ItemsSource=new[]{"Count","Duration","Forever"},SelectedItem="Count",MinHeight=25,FontSize=11};
    private readonly ComboBox _fill=new(){ItemsSource=new[]{"HoldEnd","Stop"},SelectedItem="HoldEnd",MinHeight=25,FontSize=11};
    private readonly CheckBox _independent=new(){Content="Independent track clock",MinHeight=25};
    private readonly CheckBox _reverse=new(){Content="Auto reverse",MinHeight=25};
    private readonly CheckBox _scale=new(){Content="Scale track keys",MinHeight=25};
    private readonly AnimationCompositionControl _composition=new();
    private readonly KeySplineEditorControl _curve=new();
    private readonly EasingFunctionEditorControl _function=new();
    private readonly StackPanel _splinePresets=new(){Orientation=Orientation.Horizontal};
    private readonly TextBox _previewTime=StudioTheme.Input("0","Animation preview time");
    private ImmutableDictionary<string,string> _original=ImmutableDictionary<string,string>.Empty;
    private DesignStoryboard? _board;
    private AnimationTrack? _track;
    private double? _selectedKey;
    private long _revision;
    private DesignerPanelDraft? _orphan;
    private bool _syncing,_applying;
    public event EventHandler<string>? Error;
    private bool Dirty=>_orphan is not null||Fields().Any(p=>_original.GetValueOrDefault(p.Key)!=p.Value);
    public AnimationTrackInspectorControl(DesignSession session,TimelineControl timeline)
    {
        _session=session;_timeline=timeline;_syncing=true;
        RowDefinitions.Add(new(){Height=new GridLength(29)});RowDefinitions.Add(new(){Height=new GridLength(30)});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal};toolbar.Children.Add(new StudioButton("Apply track",Apply,"Apply animation track"));toolbar.Children.Add(new StudioButton("Reload",()=>Reload(true),"Reload animation track"));Children.Add(toolbar);
        var body=new StackPanel{Spacing=5,Padding=new Thickness(8)};var scroll=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};SetRow(scroll,2);Children.Add(scroll);
        _status.TextWrapping=TextWrapping.Wrap;body.Children.Add(_title);body.Children.Add(_status);
        void Field(string label,string value)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(96)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.Children.Add(StudioTheme.Text(label));
            var input=StudioTheme.Input(value,"Animation "+label);SetColumn(input,1);row.Children.Add(input);body.Children.Add(row);_fields[label]=input;input.TextChanged+=(_,_)=>DraftChanged();
        }
        void Choice(string label,ComboBox box){AutomationProperties.SetName(box,"Animation "+label);box.HorizontalAlignment=HorizontalAlignment.Stretch;body.Children.Add(StudioTheme.Text(label));body.Children.Add(box);}
        AutomationProperties.SetName(_independent,"Independent track clock");body.Children.Add(_independent);
        Field("Duration","2");Field("Begin","0");Field("Speed","1");Choice("Repeat mode",_repeat);Field("Repeat amount","1");
        AutomationProperties.SetName(_reverse,"Animation auto reverse");body.Children.Add(_reverse);Choice("Fill behavior",_fill);
        AutomationProperties.SetName(_scale,"Scale track keys");body.Children.Add(_scale);
        body.Children.Add(_composition);_composition.DraftChanged+=(_,_)=>DraftChanged();
        Choice("Key",_keys);Field("Key time","0");Field("Value","0");Choice("Easing",_easing);Field("Key spline","0,0 1,1");body.Children.Add(_curve);
        var presets=_splinePresets;
        foreach(var (name,text) in new[]{("Linear","0,0 1,1"),("Ease in","0.42,0 1,1"),("Ease out","0,0 0.58,1"),("Ease both","0.42,0 0.58,1")})
            presets.Children.Add(new StudioButton(name,()=>{_syncing=true;_easing.SelectedItem="Spline";_fields["Key spline"].Text=text;_syncing=false;DraftChanged();},"Spline preset "+name));
        body.Children.Add(presets);body.Children.Add(_function);
        _function.DraftChanged+=(_,_)=>DraftChanged();
        // Keep preview actions fixed while the key/clock inspector scrolls. They never alter a draft.
        var preview=new Grid{ColumnSpacing=4,Padding=new Thickness(8,2,8,2)};
        preview.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        preview.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        preview.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        preview.Children.Add(StudioTheme.Text("Time (s)"));
        SetColumn(_previewTime,1);preview.Children.Add(_previewTime);
        var scrub=new StudioButton("Scrub",ScrubPreview,"Scrub animation preview");SetColumn(scrub,2);preview.Children.Add(scrub);
        SetRow(preview,1);Children.Add(preview);
        _previewTime.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Enter){ScrubPreview();e.Handled=true;}};
        var note=StudioTheme.Text("Key times are child-local. Timeline diamonds show the first forward iteration in parent time. Apply commits timing and the selected key together; drafts follow document tabs.",11,"#AAAAB3");note.TextWrapping=TextWrapping.Wrap;body.Children.Add(note);
        foreach(var box in new[]{_repeat,_fill,_easing})box.SelectionChanged+=(_,_)=>DraftChanged();
        foreach(var check in new[]{_independent,_reverse,_scale}){check.Checked+=(_,_)=>DraftChanged();check.Unchecked+=(_,_)=>DraftChanged();}
        _keys.SelectionChanged+=(_,_)=>
        {
            if(_syncing)return;
            if(Dirty){_syncing=true;_keys.SelectedItem=_selectedKey?.ToString("R",CultureInfo.InvariantCulture);_syncing=false;Report("Apply or Reload this draft before selecting another key.");return;}
            if(_track is not null&&_keys.SelectedItem is string text){_syncing=true;_timeline.SelectTrack(_track.TargetId,_track.Property,Read(text));_syncing=false;Reload(true);}
        };
        _curve.CurveChanged+=(_,spline)=>{_syncing=true;_easing.SelectedItem="Spline";_fields["Key spline"].Text=spline.ToXaml();_syncing=false;DraftChanged();};
        _session.DocumentChanged+=Changed;_timeline.AnimationSelectionChanged+=Changed;_timeline.SelectedStoryboardChanged+=Changed;
        _syncing=false;Reload(true);
    }
    private void ScrubPreview()
    {
        try
        {
            var time=Read(_previewTime.Text);
            if(time<0||time>(_timeline.ActiveStoryboard?.Duration??0))throw new InvalidDataException("Preview time is outside the parent interval.");
            _timeline.Scrub(time);
        }
        catch(Exception e){Report(e.Message);}
    }
    private static double Read(string text)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:throw new InvalidDataException("Enter a finite animation value.");
    private ImmutableDictionary<string,string> Fields()=>_fields.ToImmutableDictionary(p=>p.Key,p=>p.Value.Text)
        .Add("Independent",(_independent.IsChecked==true).ToString()).Add("Reverse",(_reverse.IsChecked==true).ToString()).Add("Scale",(_scale.IsChecked==true).ToString())
        .Add("Repeat mode",_repeat.SelectedItem as string??"Count").Add("Fill behavior",_fill.SelectedItem as string??"HoldEnd").Add("Easing",_easing.SelectedItem as string??"Linear").AddRange(_function.CaptureDraft()).AddRange(_composition.CaptureDraft());
    private void SetFields(IReadOnlyDictionary<string,string> values)
    {
        _function.RestoreDraft(values);_composition.RestoreDraft(values);
        foreach(var pair in _fields)if(values.TryGetValue(pair.Key,out var text))pair.Value.Text=text;
        _independent.IsChecked=values.GetValueOrDefault("Independent")=="True";_reverse.IsChecked=values.GetValueOrDefault("Reverse")=="True";_scale.IsChecked=values.GetValueOrDefault("Scale")=="True";
        _repeat.SelectedItem=values.GetValueOrDefault("Repeat mode","Count");_fill.SelectedItem=values.GetValueOrDefault("Fill behavior","HoldEnd");_easing.SelectedItem=values.GetValueOrDefault("Easing","Linear");
    }
    private void Changed(object? sender,EventArgs e)
    {
        if(_syncing||_applying)return;
        var track=_timeline.SelectedTrack;
        var key=_timeline.SelectedKey??track?.Keys.OrderBy(k=>k.Time).LastOrDefault();
        // Saving only changes the dirty marker, not the animation. Keep the live
        // editors and their scroll/focus state instead of resetting all fields.
        if(_revision==_session.Revision&&ReferenceEquals(_board,_timeline.ActiveStoryboard)&&
           ReferenceEquals(_track,track)&&_selectedKey==key?.Time)return;
        Reload();
    }
    private void Reload(bool discard=false)
    {
        if(!discard&&Dirty){_status.Text="Animation draft retained. Apply to its original revision or Reload.";return;}
        _orphan=null;_syncing=true;
        try
        {
            _board=_timeline.ActiveStoryboard;_track=_timeline.SelectedTrack;_revision=_session.Revision;
            _title.Text=_track is null?"Select an animation track":(_session.Index.Find(_track.TargetId)?.Name??"?")+" · "+_track.Property;
            var t=_track?.Timing??new TrackTiming(_board?.Duration??2);var key=_timeline.SelectedKey??_track?.Keys.OrderBy(k=>k.Time).LastOrDefault();_selectedKey=key?.Time;
            var values=new Dictionary<string,string>{{"Duration",t.Duration.ToString("R",CultureInfo.InvariantCulture)},{"Begin",t.BeginTime.ToString("R",CultureInfo.InvariantCulture)},{"Speed",t.SpeedRatio.ToString("R",CultureInfo.InvariantCulture)},
                {"Repeat amount",(t.RepeatDuration??t.RepeatCount).ToString("R",CultureInfo.InvariantCulture)},{"Independent",(_track?.Timing is not null).ToString()},{"Reverse",t.AutoReverse.ToString()},{"Scale","False"},
                {"Additive",(_track?.IsAdditive??false).ToString()},{"Cumulative",(_track?.IsCumulative??false).ToString()},
                {"Repeat mode",t.Loop?"Forever":t.RepeatDuration is not null?"Duration":"Count"},{"Fill behavior",t.FillBehavior},{"Key time",(key?.Time??0).ToString("R",CultureInfo.InvariantCulture)},
                {"Value",(key?.Value??0).ToString("R",CultureInfo.InvariantCulture)},{"Easing",key?.Easing??"Linear"},{"Key spline",(key?.Spline??KeySpline.Linear).ToXaml()}};
            SetFields(values);_function.SetCurve(key?.Function??new(EasingFamily.Cubic,Enum.TryParse<EasingDirection>(key?.Easing,out var direction)?direction:EasingDirection.EaseOut));_keys.ItemsSource=_track?.Keys.OrderBy(k=>k.Time).Select(k=>k.Time.ToString("R",CultureInfo.InvariantCulture)).ToArray()??[];_keys.SelectedItem=_selectedKey?.ToString("R",CultureInfo.InvariantCulture);
            _original=Fields();_status.Text=_track is null?"Create or select a numeric animation track first.":"Track and key values synchronized";
        }
        finally{_syncing=false;UpdateCurve();}
    }
    private void DraftChanged(){if(_syncing)return;_status.Text=Dirty?"Unapplied animation draft":"Track and key values synchronized";UpdateCurve();}
    private void UpdateCurve()
    {
        var function=_easing.SelectedItem as string=="Function";
        _function.Visibility=function?Visibility.Visible:Visibility.Collapsed;
        _curve.Visibility=_splinePresets.Visibility=function?Visibility.Collapsed:Visibility.Visible;
        if(_fields["Key spline"].Parent is FrameworkElement row)row.Visibility=function?Visibility.Collapsed:Visibility.Visible;
        _fields["Key spline"].IsEnabled=_easing.SelectedItem as string=="Spline";
        if(function)return;
        try{_curve.Curve=KeySpline.Parse(_fields["Key spline"].Text);}catch(Exception e)when(e is InvalidDataException or FormatException or OverflowException){_status.Text=e.Message;}
    }
    public void Apply()
    {
        try
        {
            if(_orphan is not null||_board is null||_track is null||_revision!=_session.Revision||!ReferenceEquals(_board,_timeline.ActiveStoryboard))throw new InvalidOperationException("This animation draft is stale. Reload before applying.");
            if(_session.Index.IsLocked(_track.TargetId))throw new InvalidOperationException("The animation target or an ancestor is locked.");
            if(!Dirty)return;
            var mode=_repeat.SelectedItem as string;var amount=Read(_fields["Repeat amount"].Text);
            var timing=_independent.IsChecked==true?new TrackTiming(Read(_fields["Duration"].Text),Read(_fields["Begin"].Text),Read(_fields["Speed"].Text),_reverse.IsChecked==true,mode=="Count"?amount:1,mode=="Duration"?amount:null,mode=="Forever",_fill.SelectedItem as string??"HoldEnd"):null;
            var updated=AnimationEngine.ChangeTrackTiming(_board,_track.TargetId,_track.Property,timing,_scale.IsChecked==true);
            updated=AnimationEngine.ChangeTrackComposition(updated,_track.TargetId,_track.Property,_composition.IsAdditive,_composition.IsCumulative);
            var keyTime=_selectedKey;
            if(_selectedKey is { } oldTime)
            {
                var scaledTime=_scale.IsChecked==true?oldTime/(_track.Timing?.Duration??_board.Duration)*(timing?.Duration??_board.Duration):oldTime;
                keyTime=_fields["Key time"].Text==_original["Key time"]?scaledTime:Read(_fields["Key time"].Text);
                if(keyTime<0||keyTime>(timing?.Duration??_board.Duration))throw new InvalidDataException("Key time is outside the track interval.");
                updated=AnimationEngine.MoveKey(updated,_track.TargetId,_track.Property,scaledTime,keyTime.Value);
                var easing=_easing.SelectedItem as string??"Linear";
                updated=AnimationEngine.SetKey(updated,_track.TargetId,_track.Property,keyTime.Value,Read(_fields["Value"].Text),easing,easing=="Spline"?KeySpline.Parse(_fields["Key spline"].Text):null,easing=="Function"?_function.ReadCurve():null);
            }
            _applying=true;
            try
            {
                _session.Execute("Edit animation track and key",d=>d with{Storyboards=d.Storyboards.Select(b=>b.Id==updated.Id?updated:b).ToImmutableArray()});
                _timeline.SelectTrack(_track.TargetId,_track.Property,keyTime);_timeline.Scrub(_timeline.Time);
            }
            finally{_applying=false;}
            Reload(true);_status.Text="Animation track applied";
        }
        catch(Exception e){Report(e.Message);}
    }
    private void Report(string text){_status.Text=text;Error?.Invoke(this,text);}
    public DesignerPanelDraft CaptureWorkspaceDraft()=>_orphan??new()
    {
        Values=Fields().Add("Board",_board?.Id.ToString()??"").Add("Target",_track?.TargetId.ToString()??"").Add("Property",_track?.Property??"").Add("Selected key",_selectedKey?.ToString("R",CultureInfo.InvariantCulture)??""),
        Originals=_original,HasChanges=Dirty,MatchesDesign=_revision==_session.Revision,Targets=_track is null?[]:[_track.TargetId]
    };
    public void RestoreWorkspaceDraft(DesignerPanelDraft? state)
    {
        Reload(true);if(state?.HasChanges!=true)return;
        _syncing=true;
        try
        {
            _board=Guid.TryParse(state.Values.GetValueOrDefault("Board"),out var boardId)?_session.Document.Storyboards.FirstOrDefault(b=>b.Id==boardId):null;
            _track=Guid.TryParse(state.Values.GetValueOrDefault("Target"),out var target)?_board?.Tracks.FirstOrDefault(t=>t.TargetId==target&&t.Property==state.Values.GetValueOrDefault("Property")):null;
            _selectedKey=double.TryParse(state.Values.GetValueOrDefault("Selected key"),NumberStyles.Float,CultureInfo.InvariantCulture,out var key)?key:null;
            SetFields(state.Values);_keys.ItemsSource=_track?.Keys.OrderBy(k=>k.Time).Select(k=>k.Time.ToString("R",CultureInfo.InvariantCulture)).ToArray()??[];_keys.SelectedItem=_selectedKey?.ToString("R",CultureInfo.InvariantCulture);
            _original=state.Originals;
            // Earlier saved drafts predate the function fields; their defaults are not new edits.
            foreach(var pair in _function.CaptureDraft().AddRange(_composition.CaptureDraft()))if(!_original.ContainsKey(pair.Key))_original=_original.Add(pair.Key,pair.Value);
            _revision=state.MatchesDesign?_session.Revision:-1;
            if(_board is null||_track is null)_orphan=state with{MatchesDesign=false};
            _title.Text=_track is null?"Missing animation target":(_session.Index.Find(_track.TargetId)?.Name??"?")+" · "+_track.Property;
            _status.Text=_orphan is null?"Retained animation draft for this document":"Missing track draft retained; Reload discards it.";
        }
        finally{_syncing=false;UpdateCurve();}
    }
    public void Dispose(){_function.Dispose();_session.DocumentChanged-=Changed;_timeline.AnimationSelectionChanged-=Changed;_timeline.SelectedStoryboardChanged-=Changed;}
}
