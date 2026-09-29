using DesignSpace.Core;
using DesignSpace.Animation;
namespace DesignSpace.Controls.Uno;

public sealed partial class TimelineControl
{
    private (Guid Target,string Property)? _trackSelection;
    private DesignStoryboard? _dragBoard;
    private long _dragRevision;
    public event EventHandler? TrackSettingsRequested;
    public event EventHandler? AnimationSelectionChanged;
    public AnimationTrack? SelectedTrack
    {
        get
        {
            var tracks=ActiveStoryboard?.Tracks;if(tracks is null)return null;
            var selected=_key is { } k?(k.Target,k.Property):_trackSelection;
            if(selected is { } id&&tracks.Value.FirstOrDefault(t=>t.TargetId==id.Item1&&t.Property==id.Item2) is { } found)return found;
            return tracks.Value.FirstOrDefault(t=>_session.Selection.Contains(t.TargetId)&&t.Property==PropertyToRecord)
                ??tracks.Value.FirstOrDefault(t=>_session.Selection.Contains(t.TargetId))??tracks.Value.FirstOrDefault();
        }
    }
    public AnimationKey? SelectedKey=>_key is { } key?SelectedTrack?.Keys.FirstOrDefault(k=>Math.Abs(k.Time-key.Time)<.00001):null;
    public object AnimationDiagnostics=>new
    {
        selectedTarget=SelectedTrack?.TargetId,selectedProperty=SelectedTrack?.Property,
        tracks=ActiveStoryboard?.Tracks.Select((track,index)=>new
        {
            target=track.TargetId,property=track.Property,
            timing=track.Timing is { } t?new{duration=t.Duration,begin=t.BeginTime,speed=t.SpeedRatio,reverse=t.AutoReverse,count=t.RepeatCount,span=t.RepeatDuration,forever=t.Loop,fill=t.FillBehavior}:null,
            keys=track.Keys.Select(key=>new{time=key.Time,value=key.Value,easing=key.Easing,spline=key.Spline?.ToXaml(),function=key.Function is { } f?new{family=f.Family.ToString(),mode=f.Mode.ToString(),amplitude=f.Amplitude,bounces=f.Bounces,bounciness=f.Bounciness,oscillations=f.Oscillations,springiness=f.Springiness,exponent=f.Exponent,power=f.Power}:null,x=150+AnimationEngine.KeyToParentTime(track,key.Time)*_scale,y=40+index*25}).ToArray()
        }).ToArray()
    };
    public void SelectTrack(Guid target,string property,double? keyTime=null)
    {
        var track=ActiveStoryboard?.Tracks.FirstOrDefault(t=>t.TargetId==target&&t.Property==property)??throw new InvalidOperationException("The track no longer exists.");
        _trackSelection=(target,property);_key=keyTime is { } time&&track.Keys.Any(k=>Math.Abs(k.Time-time)<.00001)?(target,property,time):null;
        PropertyToRecord=property;_session.Select(target);_surface.Invalidate();AnimationSelectionChanged?.Invoke(this,EventArgs.Empty);
    }
}
