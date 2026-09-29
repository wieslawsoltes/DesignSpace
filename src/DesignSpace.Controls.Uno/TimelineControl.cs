using System.Collections.Immutable;
using System.Diagnostics;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Animation;
using DesignSpace.Rendering.Skia;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace DesignSpace.Controls.Uno;

public sealed partial class TimelineControl : Grid,IDisposable
{
    private sealed class TimelineSurface(TimelineControl owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas,Size area)=>owner.Draw(canvas,area.Width,area.Height);
    }
    private readonly DesignSession _session;
    private readonly TimelineSurface _surface;
    private readonly ComboBox _boards=new() { MinWidth=110,MinHeight=24,FontSize=11,Padding=new Thickness(5,1,5,1),Background=StudioTheme.Field };
    private readonly StudioButton _record;
    private readonly TextBlock _timeText=StudioTheme.Text("0:00.000",11,"#E6B36D");
    private readonly ComboBox _easing=new() { ItemsSource=new[]{"Linear","EaseIn","EaseOut","EaseInOut","Discrete","Spline","Function"},SelectedIndex=0,MinWidth=95,MinHeight=24,FontSize=11,Padding=new Thickness(4,1,4,1),Background=StudioTheme.Field };
    private Guid? _boardId; private bool _refreshing; private bool _playing; private bool _dragging;
    private readonly Stopwatch _clock=new(); private double _startTime; private double _scale=160;
    private (Guid Target,string Property,double Time)? _key;
    private double? _dragTime;
    public double Time { get; private set; }
    public double PreviewTime { get; private set; }
    public bool IsClockPreview { get; private set; }
    private DesignStoryboard? _scrubSource,_scrubPreview;
    public DesignStoryboard? PreviewStoryboard
    {
        get
        {
            var board=ActiveStoryboard;if(IsClockPreview || board is null) return board;
            if(!ReferenceEquals(board,_scrubSource))
            {
                _scrubSource=board;_scrubPreview=board with { Loop=false,AutoReverse=false,BeginTime=0,SpeedRatio=1,RepeatCount=1,RepeatDuration=null,FillBehavior="HoldEnd" };
            }
            return _scrubPreview;
        }
    }
    public bool IsRecording { get; private set; }
    public bool IsPlaying=>_playing;
    public string PropertyToRecord { get; set; }="Opacity";
    public DesignStoryboard? ActiveStoryboard=>_session.Document.Storyboards.FirstOrDefault(b=>b.Id==_boardId);
    public event EventHandler? TimeChanged;
    public event EventHandler? SelectedStoryboardChanged;
    public event EventHandler? SettingsRequested;
    public event EventHandler<string>? Error;
    public TimelineControl(DesignSession session)
    {
        _session=session; RowDefinitions.Add(new(){Height=new GridLength(29)}); RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var toolbar=new StackPanel { Orientation=Orientation.Horizontal,Spacing=2,Background=StudioTheme.Background,Padding=new Thickness(3,2,3,2) };
        toolbar.Children.Add(_boards); toolbar.Children.Add(new StudioButton("+",AddStoryboard,"New storyboard"));
        _record=new StudioButton("●",()=> { IsRecording=!IsRecording; _record!.IsSelected=IsRecording; _surface!.Invalidate(); },"Record keyframes"); toolbar.Children.Add(_record);
        toolbar.Children.Add(new StudioButton("|◀",()=>Scrub(0),"Go to first frame")); toolbar.Children.Add(new StudioButton("▶",TogglePlay,"Play or pause storyboard")); toolbar.Children.Add(new StudioButton("■",Stop,"Stop storyboard"));
        toolbar.Children.Add(new StudioButton("◇",()=>AddKey(PropertyToRecord),"Add keyframe at playhead")); toolbar.Children.Add(new StudioButton("−",DeleteKey,"Delete selected keyframe"));
        _timeText.Width=68; toolbar.Children.Add(_timeText); toolbar.Children.Add(_easing);
        toolbar.Children.Add(new StudioButton("Timing",()=>SettingsRequested?.Invoke(this,EventArgs.Empty),"Edit storyboard timing"));
        toolbar.Children.Add(new StudioButton("Track",()=>TrackSettingsRequested?.Invoke(this,EventArgs.Empty),"Edit animation track"));
        toolbar.Children.Add(new StudioButton("Fit",Fit,"Fit timeline")); Children.Add(new CommandBarScroller(toolbar));
        _surface=new(this); SetRow(_surface,1); Children.Add(_surface); AutomationProperties.SetName(_surface,"Animation timeline");
        _boards.SelectionChanged+=(_,_)=> { if(_refreshing) return; var name=_boards.SelectedItem as string; _boardId=_session.Document.Storyboards.FirstOrDefault(b=>b.Name==name)?.Id; _key=null; _trackSelection=null; Scrub(0); SelectedStoryboardChanged?.Invoke(this,EventArgs.Empty); };
        _easing.SelectionChanged+=(_,_)=>
        {
            if(_refreshing||_key is not { } key||ActiveStoryboard is not { } board)return;
            try
            {
                if(_session.Index.IsLocked(key.Target))throw new InvalidOperationException("The animation target is locked.");
                var k=SelectedKey;if(k is not null)ReplaceBoard(AnimationEngine.SetKey(board,key.Target,key.Property,key.Time,k.Value,_easing.SelectedItem as string??"Linear",k.Spline,k.Function),"Change keyframe easing");
            }
            catch(Exception e){Error?.Invoke(this,e.Message);}
        };
        _surface.PointerPressed+=(_,e)=>
        {
            StopPlayback();var p=e.GetCurrentPoint(_surface).Position;var board=ActiveStoryboard;if(board is null)return;
            var row=(int)((p.Y-28)/25);_key=null;_dragTime=null;
            if(row>=0&&row<board.Tracks.Length&&p.Y>=28)
            {
                var track=board.Tracks[row];_trackSelection=(track.TargetId,track.Property);_session.Select(track.TargetId);PropertyToRecord=track.Property;
                if(p.X>=150)
                {
                    var key=track.Keys.FirstOrDefault(k=>Math.Abs(150+AnimationEngine.KeyToParentTime(track,k.Time)*_scale-p.X)<8);
                    if(key is not null){_key=(track.TargetId,track.Property,key.Time);_dragTime=key.Time;_refreshing=true;_easing.SelectedItem=key.Easing;_refreshing=false;}
                }
                AnimationSelectionChanged?.Invoke(this,EventArgs.Empty);
            }
            if(p.X<150){_surface.Invalidate();e.Handled=true;return;}
            _dragBoard=board;_dragRevision=_session.Revision;_dragging=true;Scrub(Math.Max(0,(p.X-150)/_scale));_surface.CapturePointer(e.Pointer);e.Handled=true;
        };
        _surface.PointerMoved+=(_,e)=>
        {
            if(!_dragging)return;
            if(!ReferenceEquals(_dragBoard,ActiveStoryboard)||_dragRevision!=_session.Revision){_dragging=false;_dragTime=null;return;}
            var p=e.GetCurrentPoint(_surface).Position;var time=Math.Clamp(Math.Round(Math.Max(0,(p.X-150)/_scale)*60)/60,0,ActiveStoryboard?.Duration??2);
            if(_key is not null&&SelectedTrack is { } track)_dragTime=AnimationEngine.ParentToKeyTime(track,time,ActiveStoryboard!.Duration);
            Scrub(time);
        };
        _surface.PointerReleased+=(_,e)=>
        {
            if(!_dragging)return;_dragging=false;
            try
            {
                if(_key is { } key&&_dragTime is { } time&&ActiveStoryboard is { } board&&Math.Abs(time-key.Time)>.0001)
                {
                    if(!ReferenceEquals(_dragBoard,board)||_dragRevision!=_session.Revision||_session.Index.IsLocked(key.Target))throw new InvalidOperationException("The animation changed or is locked; drag was not applied.");
                    ReplaceBoard(AnimationEngine.MoveKey(board,key.Target,key.Property,key.Time,time),"Move keyframe");_key=(key.Target,key.Property,time);AnimationSelectionChanged?.Invoke(this,EventArgs.Empty);
                }
            }
            catch(Exception ex){Error?.Invoke(this,ex.Message);}
            _dragTime=null;_dragBoard=null;_surface.ReleasePointerCapture(e.Pointer);_surface.Invalidate();e.Handled=true;
        };
        _surface.PointerCaptureLost+=(_,_)=>{_dragging=false;_dragTime=null;_dragBoard=null;};
        _surface.PointerWheelChanged+=(_,e)=>{_scale=Math.Clamp(_scale*Math.Pow(1.2,e.GetCurrentPoint(_surface).Properties.MouseWheelDelta/120d),20,800);_surface.Invalidate();e.Handled=true;};
        SizeChanged+=(_,_)=>_surface.Invalidate(); Unloaded+=(_,_)=>StopPlayback(); _session.DocumentChanged+=Changed; Refresh();
    }
    private void Changed(object? sender,EventArgs e) { StopPlayback(); IsClockPreview=false; Refresh(); PreviewTime=Time; }
    public void Refresh()
    {
        _refreshing=true; _boards.ItemsSource=_session.Document.Storyboards.Select(b=>b.Name).ToArray();
        if(ActiveStoryboard is null) _boardId=_session.Document.Storyboards.FirstOrDefault()?.Id;
        _boards.SelectedItem=ActiveStoryboard?.Name; _refreshing=false; Time=Math.Clamp(Time,0,ActiveStoryboard?.Duration ?? 2); _surface.Invalidate();
    }
    public void AddStoryboard()
    {
        var name="Storyboard"; var i=1; while(_session.Document.Storyboards.Any(b=>b.Name==name+i)) i++;
        var board=new DesignStoryboard(Guid.NewGuid(),name+i,2,[]); _boardId=board.Id; _session.Execute("Add storyboard",d=>d with { Storyboards=d.Storyboards.Add(board) }); Scrub(0);
    }
    public void AddKey(string property,double? value=null)
    {
        try
        {
            if(ActiveStoryboard is null) AddStoryboard(); var board=ActiveStoryboard!;
            foreach(var id in _session.Selection)
            {
                var node=_session.Document.Root.Find(id)!; var baseline=property=="Rotation" ? node.Rotation : node.Number(property,property=="Opacity" ? 1 : 0);
                if(_session.Index.IsLocked(id))throw new InvalidOperationException("The animation target is locked.");
                var track=board.Tracks.FirstOrDefault(t=>t.TargetId==id&&t.Property==property);
                var sample=track is null?new StoryboardClockSample(Time,true,false):StoryboardClock.Sample(track,Time,board.Duration);
                if(!sample.Applies)throw new InvalidOperationException("Choose a time when the child track contributes before recording a key.");
                board=AnimationEngine.SetKey(board,id,property,sample.LocalTime,value??baseline,_easing.SelectedItem as string??"Linear");
            }
            ReplaceBoard(board,"Record "+property); TimeChanged?.Invoke(this,EventArgs.Empty);
        }
        catch(Exception e) { Error?.Invoke(this,e.Message); }
    }
    private void DeleteKey()
    {
        if(_key is not { } key || ActiveStoryboard is not { } board) return;
        try
        {
            if(_session.Index.IsLocked(key.Target))throw new InvalidOperationException("The animation target is locked.");
            ReplaceBoard(AnimationEngine.RemoveKey(board,key.Target,key.Property,key.Time),"Delete keyframe");_key=null;TimeChanged?.Invoke(this,EventArgs.Empty);AnimationSelectionChanged?.Invoke(this,EventArgs.Empty);
        }
        catch(Exception e){Error?.Invoke(this,e.Message);}
    }
    private void ReplaceBoard(DesignStoryboard board,string label)=>_session.Execute(label,d=>d with { Storyboards=d.Storyboards.Select(b=>b.Id==board.Id ? board : b).ToImmutableArray() });
    public void Scrub(double time)
    {
        StopPlayback(); IsClockPreview=false; Time=Math.Clamp(time,0,ActiveStoryboard?.Duration ?? 2); PreviewTime=Time; NotifyTime();
    }
    private void NotifyTime()
    {
        _timeText.Text=TimeSpan.FromSeconds(Time).ToString(@"m\:ss\.fff");
        _surface.Invalidate(); TimeChanged?.Invoke(this,EventArgs.Empty);
    }
    public void TogglePlay()
    {
        if(_playing) { StopPlayback(); return; } if(ActiveStoryboard is not { } board) return;
        _startTime=IsClockPreview && !StoryboardClock.Sample(board,PreviewTime).IsCompleted ? PreviewTime :
            Time>0 && Time<board.Duration ? board.BeginTime+Time/board.SpeedRatio : 0;
        IsClockPreview=true; _clock.Restart(); _playing=true; CompositionTarget.Rendering+=Frame;
    }
    private void Frame(object? sender,object args)
    {
        if(!_playing || ActiveStoryboard is not { } board) { StopPlayback(); return; }
        PreviewTime=Math.Min(_startTime+_clock.Elapsed.TotalSeconds,StoryboardClock.EndTime(board));
        var sample=StoryboardClock.Sample(board,PreviewTime); Time=sample.LocalTime; IsClockPreview=true;
        if(sample.IsCompleted) StopPlayback();
        NotifyTime();
    }
    private void StopPlayback() { if(_playing) CompositionTarget.Rendering-=Frame; _playing=false; _clock.Stop(); }
    public void Stop() { StopPlayback(); Scrub(0); }
    public void Fit() { _scale=Math.Max(20,(_surface.ActualWidth-180)/(ActiveStoryboard?.Duration ?? 2)); _surface.Invalidate(); }
    private void Draw(SKCanvas c,double width,double height)
    {
        c.Clear(new SKColor(32,32,34)); using var paint=new SKPaint { IsAntialias=true }; using var font=new SKFont(DesignTypography.DefaultTypeface,10);
        void Text(string text,float x,float y,SKColor color) { paint.Style=SKPaintStyle.Fill; paint.Color=color; c.DrawText(text,x,y,font,paint); }
        var board=ActiveStoryboard;
        paint.Color=new SKColor(42,42,45); c.DrawRect(0,0,(float)width,27,paint);
        Text(board is null ? "No storyboard" : board.Name,8,18,new SKColor(210,210,215));
        var duration=board?.Duration ?? 2; var step=_scale>150 ? .2 : .5;
        for(var t=0d;150+t*_scale<width;t+=step)
        {
            var x=(float)(150+t*_scale); paint.Color=new SKColor(59,59,63); paint.StrokeWidth=1; c.DrawLine(x,22,x,(float)height,paint); Text(t.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),x+3,14,new SKColor(155,155,163));
        }
        if(board is not null) for(var i=0;i<board.Tracks.Length;i++)
        {
            var track=board.Tracks[i]; var y=40+i*25; if(y>height) break;
            paint.Color=i%2==0 ? new SKColor(38,38,41) : new SKColor(34,34,36); c.DrawRect(0,y-11,148,25,paint);
            var name=_session.Document.Root.Find(track.TargetId)?.Name??"?";
            c.Save();c.ClipRect(SKRect.Create(0,y-11,148,25));Text(name+"."+track.Property,8,y+4,new SKColor(196,196,202));c.Restore();
            if(track.Timing is { } timing)
            {
                var start=Math.Clamp(timing.BeginTime,0,duration);var end=Math.Clamp(StoryboardClock.EndTime(timing),0,duration);
                paint.Color=new SKColor(70,133,176);c.DrawRect((float)(150+start*_scale),y+8,(float)Math.Max(0,(end-start)*_scale),2,paint);
            }
            foreach(var key in track.Keys)
            {
                var selected=_key is { } k && k.Target==track.TargetId && k.Property==track.Property && Math.Abs(k.Time-key.Time)<.0001;
                var time=selected && _dragTime is not null ? _dragTime.Value : key.Time; var x=(float)(150+AnimationEngine.KeyToParentTime(track,time)*_scale);
                if(x<150||x>width)continue;
                using var path=new SKPath(); path.MoveTo(x,y-5); path.LineTo(x+5,y); path.LineTo(x,y+5); path.LineTo(x-5,y); path.Close();
                paint.Color=selected ? SKColors.White : new SKColor(225,167,74); c.DrawPath(path,paint);
            }
        }
        if(board?.Tracks.IsEmpty!=false) Text("Select an object, choose a property, then add a keyframe.",160,49,new SKColor(130,130,138));
        var head=(float)(150+Time*_scale); paint.Color=IsRecording ? new SKColor(219,65,65) : new SKColor(0,154,230); paint.StrokeWidth=1.5f; c.DrawLine(head,18,head,(float)height,paint);
        using var triangle=new SKPath(); triangle.MoveTo(head-5,17); triangle.LineTo(head+5,17); triangle.LineTo(head,24); triangle.Close(); c.DrawPath(triangle,paint);
    }
    public void Dispose() { StopPlayback(); _session.DocumentChanged-=Changed; }
}
