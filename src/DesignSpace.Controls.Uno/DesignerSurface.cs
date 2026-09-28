using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Animation;
using DesignSpace.Xaml;
using DesignSpace.Rendering.Skia;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
namespace DesignSpace.Controls.Uno;

public static class DesignerKeys
{
    public static Func<VirtualKey,bool?>? StateOverride { get; set; }
    public static bool Down(VirtualKey key)=>StateOverride?.Invoke(key) ?? ((InputKeyboardSource.GetKeyStateForCurrentThread(key)&CoreVirtualKeyStates.Down)!=0);
    public static bool Control=>Down(VirtualKey.Control)||Down(VirtualKey.LeftWindows)||Down(VirtualKey.RightWindows);
    public static bool Shift=>Down(VirtualKey.Shift);
    public static bool Alt=>Down(VirtualKey.Menu);
}
public sealed partial class DesignerSurface : Grid,IDisposable
{
    private static readonly IReadOnlySet<Guid> NoSelection=new HashSet<Guid>();
    private sealed class Surface(DesignerSurface owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas,Size area)
        {
            owner.Renderer.Draw(canvas,area.Width,area.Height,owner.Layout,owner.Viewport,(owner.Tool is "Direct Selection" or "Pen" or "Path" && owner.Session.Selection.Count==1 && owner.Session.Index.Find(owner.Session.Selection.Single())?.Type=="Path") ? NoSelection : owner.Session.Selection,owner._marquee,owner.IsPreview);
            owner.DrawPathOverlay(canvas);
            owner.Rendered?.Invoke(owner,EventArgs.Empty);
        }
    }
    private sealed record Original(DRect Box,DMatrix ParentInverse);
    private readonly Surface _surface;
    private readonly LayoutEngine _layoutEngine;
    private readonly Button _focus=new(){Width=1,Height=1,Opacity=.001,MinHeight=0,MinWidth=0,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
    private LayoutSnapshot? _layout;
    private DesignNode? _previewRoot;
    private DPoint _start,_screenStart,_panStart;
    private Dictionary<Guid,Original> _original=[];
    private IReadOnlyDictionary<Guid,IReadOnlyDictionary<string,string>>? _changes;
    private DRect? _marquee;
    private string _gesture="";
    private int _resizeHandle=-1;
    private bool _fitPending=true;
    private Guid _drawParent;
    public DesignSession Session { get; }
    public DesignRenderer Renderer { get; }=new();
    public DesignViewport Viewport { get; }=new();
    public LayoutSnapshot Layout=>_layout ??= _layoutEngine.Arrange(PreviewResolver?.Invoke(_previewRoot ?? Session.Document.Root) ?? _previewRoot ?? Session.Document.Root);
    public long MeasureCacheHits=>_layoutEngine.MeasureCacheHits;
    public Func<DesignNode,DesignNode>? PreviewResolver { get; set; }=DesignPreview.Resolve;
    private string _tool="Selection";
    public string Tool { get=>_tool;set{if(_tool==value)return;CancelGesture();_tool=value;NotifyPaths();} }
    public bool IsPreview { get; set; }
    public double Playhead { get; private set; }
    public DesignStoryboard? Storyboard { get; private set; }
    public DesignState? State { get; private set; }
    public event EventHandler? Rendered;
    public event EventHandler? ViewChanged;
    public event EventHandler? EditingStarted;
    public event EventHandler<Guid>? PreviewClicked;
    public event EventHandler<string>? Error;
    public DesignerSurface(DesignSession session)
    {
        Session=session; _layoutEngine=new(Renderer); Background=StudioTheme.Brush("#2D2D30"); _surface=new(this); Children.Add(_surface); Children.Add(_focus);
        AutomationProperties.SetName(_surface,"Design artboard"); AutomationProperties.SetName(_focus,"Designer keyboard focus");
        _surface.PointerPressed+=Pressed; _surface.PointerMoved+=Moved; _surface.PointerReleased+=Released;
        _surface.PointerCaptureLost+=(_,_)=> { if(_gesture.Length>0) CancelGesture(); };
        _surface.PointerWheelChanged+=(_,e)=>
        {
            var p=e.GetCurrentPoint(_surface); var pos=new DPoint(p.Position.X,p.Position.Y);
            if(DesignerKeys.Control) Viewport.ZoomAt(pos,Viewport.Zoom*Math.Pow(1.12,p.Properties.MouseWheelDelta/120d));
            else if(DesignerKeys.Shift) Viewport.PanX+=p.Properties.MouseWheelDelta*.35; else Viewport.PanY+=p.Properties.MouseWheelDelta*.35;
            Invalidate(); ViewChanged?.Invoke(this,EventArgs.Empty); e.Handled=true;
        };
        Session.DocumentChanged+=DocumentChanged; Session.SelectionChanged+=SelectionChanged;
        SizeChanged+=(_,_)=> { if(_fitPending && ActualWidth>100 && ActualHeight>100) { Fit(); _fitPending=false; } else Invalidate(); };
    }
    private void DocumentChanged(object? sender,EventArgs e) { PathDocumentChanged(); _previewRoot=null; Storyboard=null; State=null; _layout=null; Invalidate(); }
    private void SelectionChanged(object? sender,EventArgs e){PathSelectionChanged();Invalidate();}
    public void FocusDesigner()=>_focus.Focus(FocusState.Programmatic);
    public void Invalidate()=>_surface.Invalidate();
    public void InvalidateLayout() { _layout=null; Invalidate(); }
    public void Fit() { if(Layout.Entries.Count==0) return; Viewport.Fit(ActualWidth,ActualHeight,Layout.Entries[0].Bounds); Invalidate(); ViewChanged?.Invoke(this,EventArgs.Empty); }
    public void Zoom(double factor) { Viewport.ZoomAt(new(ActualWidth/2,ActualHeight/2),Viewport.Zoom*factor); Invalidate(); ViewChanged?.Invoke(this,EventArgs.Empty); }
    public void SetPreview(DesignStoryboard? board,double time,DesignState? state=null)
    {
        if(_gesture is "move" or "resize" or "draw" or "path-edit" or "pen-add" or "pencil" || _penParent is not null) return;
        Storyboard=board; Playhead=time; State=state; _previewRoot=AnimationEngine.Evaluate(Session.Document.Root,board,time,state); _layout=null; Invalidate();
    }
    public void ClearPreview() { Storyboard=null; State=null; _previewRoot=null; _layout=null; Playhead=0; Invalidate(); }
    public void CancelGesture() { CancelPathDraft();_gesture=""; _marquee=null; _previewRoot=null; _layout=null; _changes=null; _original.Clear(); Invalidate(); }
    private static DPoint[] Handles(DRect b)=>[new(b.X,b.Y),new(b.Center.X,b.Y),new(b.Right,b.Y),new(b.X,b.Center.Y),new(b.Right,b.Center.Y),new(b.X,b.Bottom),new(b.Center.X,b.Bottom),new(b.Right,b.Bottom)];
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        var point=e.GetCurrentPoint(_surface); var p=new DPoint(point.Position.X,point.Position.Y); _start=Viewport.ScreenToWorld(p); _screenStart=p; _panStart=new(Viewport.PanX,Viewport.PanY); FocusDesigner();
        if(point.Properties.IsMiddleButtonPressed || Tool=="Hand" || DesignerKeys.Down(VirtualKey.Space)) _gesture="pan";
        else if(!point.Properties.IsLeftButtonPressed) return;
        else if(IsPreview) { var hit=Layout.HitTest(_start); if(hit is not null) PreviewClicked?.Invoke(this,hit.Node.Id); return; }
        else if(Tool=="Zoom") { Viewport.ZoomAt(p,Viewport.Zoom*(DesignerKeys.Alt ? .8 : 1.25)); Invalidate(); ViewChanged?.Invoke(this,EventArgs.Empty); return; }
        else
        {
            EditingStarted?.Invoke(this,EventArgs.Empty); ClearPreview();
            if(PathPressed(e,_start)){e.Handled=true;return;}
            if(Tool is not ("Selection" or "Direct Selection"))
            {
                var parent=Session.Selection.Select(Session.Index.Find).FirstOrDefault(n=>n?.Type=="Canvas") ?? Session.Document.Root;
                if(parent.Type!="Canvas" || Session.Index.IsLocked(parent.Id)) { Error?.Invoke(this,"Select an unlocked Canvas to draw inside."); return; }
                _drawParent=parent.Id; _gesture="draw"; _marquee=new(_start.X,_start.Y,0,0);
            }
            else
            {
                _resizeHandle=-1;
                if(Session.Selection.Count==1 && Layout.ById.TryGetValue(Session.Selection.Single(),out var selected) && !selected.IsEffectivelyLocked && Session.Index.ParentOf(selected.Node.Id)?.Type=="Canvas" && selected.LocalTransform==DMatrix.Identity)
                {
                    var handles=Handles(selected.Bounds).Select(selected.WorldTransform.Map).ToArray();
                    for(var i=0;i<handles.Length;i++) if(Math.Abs(_start.X-handles[i].X)*Viewport.Zoom<7 && Math.Abs(_start.Y-handles[i].Y)*Viewport.Zoom<7) _resizeHandle=i;
                }
                if(_resizeHandle>=0) _gesture="resize";
                else
                {
                    var hits=Layout.HitStack(_start).Where(h=>Session.Index.Find(h.Node.Id) is not null).ToArray();
                    var hit=hits.FirstOrDefault();
                    if(DesignerKeys.Alt && hits.Length>1)
                    {
                        var current=Array.FindIndex(hits,h=>Session.Selection.Contains(h.Node.Id)); hit=hits[(current+1)%hits.Length];
                    }
                    if(hit is not null)
                    {
                        if(!Session.Selection.Contains(hit.Node.Id) || DesignerKeys.Control) Session.Select(hit.Node.Id,DesignerKeys.Control);
                        _gesture="move";
                    }
                    else { if(!DesignerKeys.Control) Session.Select([]); _gesture="marquee"; _marquee=new(_start.X,_start.Y,0,0); }
                }
                _original.Clear();
                foreach(var id in Session.TopLevelSelection())
                {
                    var n=Session.Index.Find(id)!; var parent=Session.Index.ParentOf(id);
                    if(Session.Index.IsLocked(id) || parent?.Type!="Canvas" || !Layout.ById.TryGetValue(id,out var entry) || !Layout.ById.TryGetValue(parent.Id,out var host) || !host.WorldTransform.TryInvert(out var inverse)) continue;
                    var padding=Insets.Parse(parent.Get("Padding")); var margin=Insets.Parse(n.Get("Margin"));
                    var x=entry.Bounds.X-host.Bounds.X-padding.Left-margin.Left; var y=entry.Bounds.Y-host.Bounds.Y-padding.Top-margin.Top;
                    _original[id]=new(new(x,y,entry.Bounds.Width,entry.Bounds.Height),inverse);
                }
            }
        }
        _surface.CapturePointer(e.Pointer); e.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        var position=e.GetCurrentPoint(_surface).Position; var screen=new DPoint(position.X,position.Y); var world=Viewport.ScreenToWorld(screen);
        if(_gesture!="pan"&&PathMoved(world)){e.Handled=true;return;}
        if(_gesture.Length==0)return;
        if(_gesture=="pan") { Viewport.PanX=_panStart.X+screen.X-_screenStart.X; Viewport.PanY=_panStart.Y+screen.Y-_screenStart.Y; Invalidate(); return; }
        if(_gesture is "draw" or "marquee") { _marquee=DRect.FromPoints(_start,world); Invalidate(); return; }
        var changes=new Dictionary<Guid,IReadOnlyDictionary<string,string>>();
        foreach(var (id,original) in _original)
        {
            var delta=original.ParentInverse.MapVector(world-_start); var b=original.Box;
            if(_gesture=="move")
            {
                if(Viewport.SnapToGrid && !DesignerKeys.Alt) delta=new(Numbers.Snap(delta.X,Viewport.GridSize),Numbers.Snap(delta.Y,Viewport.GridSize));
                b=b.Translate(delta.X,delta.Y);
            }
            else
            {
                var left=b.X;var top=b.Y;var right=b.Right;var bottom=b.Bottom;
                if(_resizeHandle is 0 or 3 or 5) left=Math.Min(right-1,left+delta.X);
                if(_resizeHandle is 2 or 4 or 7) right=Math.Max(left+1,right+delta.X);
                if(_resizeHandle is 0 or 1 or 2) top=Math.Min(bottom-1,top+delta.Y);
                if(_resizeHandle is 5 or 6 or 7) bottom=Math.Max(top+1,bottom+delta.Y);
                if(Viewport.SnapToGrid && !DesignerKeys.Alt)
                {
                    if(_resizeHandle is 0 or 3 or 5) left=Numbers.Snap(left,Viewport.GridSize);
                    if(_resizeHandle is 2 or 4 or 7) right=Numbers.Snap(right,Viewport.GridSize);
                    if(_resizeHandle is 0 or 1 or 2) top=Numbers.Snap(top,Viewport.GridSize);
                    if(_resizeHandle is 5 or 6 or 7) bottom=Numbers.Snap(bottom,Viewport.GridSize);
                }
                b=new(left,top,Math.Max(1,right-left),Math.Max(1,bottom-top));
                if(DesignerKeys.Shift) b=b with { Height=b.Width*original.Box.Height/Math.Max(1,original.Box.Width) };
            }
            if(b==original.Box) continue;
            var values=new Dictionary<string,string> { ["Canvas.Left"]=Numbers.Format(b.X),["Canvas.Top"]=Numbers.Format(b.Y) };
            if(_gesture=="resize")
            {
                values["Width"]=Numbers.Format(b.Width); values["Height"]=Numbers.Format(b.Height);
                if(Session.Index.Find(id) is { Type:"Path" } pathNode)
                {
                    var geometry=VectorGeometry.Local(pathNode,new(original.Box.Width,original.Box.Height));
                    values["Data"]=VectorPathCodec.Write(VectorMath.Transform(geometry,DMatrix.Scale(b.Width/Math.Max(1,original.Box.Width),b.Height/Math.Max(1,original.Box.Height))));values["Stretch"]="None";
                }
            }
            changes[id]=values;
        }
        _changes=changes; _previewRoot=DesignTree.SetProperties(Session.Document.Root,changes,replacePropertyElements:true); _layout=null; Invalidate(); e.Handled=true;
    }
    private void Released(object sender,PointerRoutedEventArgs e)
    {
        var gesture=_gesture; _gesture="";
        try
        {
            if(PathReleased(gesture))return;
            if(gesture=="draw" && _marquee is { } box)
            {
                if(!Layout.ById.TryGetValue(_drawParent,out var parent) || !parent.WorldTransform.TryInvert(out var inverse)) throw new InvalidOperationException("The drawing container is unavailable.");
                var local=inverse.MapBounds(box); var padding=Insets.Parse(parent.Node.Get("Padding")); local=local.Translate(-parent.Bounds.X-padding.Left,-parent.Bounds.Y-padding.Top);
                if(local.Width<3 || local.Height<3) local=new(local.X,local.Y,Tool=="TextBlock" ? 200 : 120,Tool is "TextBlock" or "Button" ? 40 : 80);
                if(Viewport.SnapToGrid && !DesignerKeys.Alt) local=local with { X=Numbers.Snap(local.X,Viewport.GridSize),Y=Numbers.Snap(local.Y,Viewport.GridSize),Width=Math.Max(1,Numbers.Snap(local.Width,Viewport.GridSize)),Height=Math.Max(1,Numbers.Snap(local.Height,Viewport.GridSize)) };
                Session.Add(Tool,local,_drawParent);
            }
            else if(gesture=="marquee" && _marquee is { } box2)
                Session.Select(Layout.Entries.Where(x=>x.Depth>0 && !x.IsEffectivelyLocked && x.IsEffectivelyVisible && Session.Index.Find(x.Node.Id) is not null && box2.Intersects(x.VisualBounds)).Select(x=>x.Node.Id),DesignerKeys.Control);
            else if(gesture is "move" or "resize" && _changes is { Count:>0 } changes)
                Session.Execute(gesture=="move" ? "Move selection" : "Resize selection",d=>d with { Root=DesignTree.SetProperties(d.Root,changes,replacePropertyElements:true) });
        }
        catch(Exception ex) { Error?.Invoke(this,ex.Message); }
        finally { _marquee=null; _previewRoot=null; _layout=null; _changes=null; _original.Clear(); _surface.ReleasePointerCapture(e.Pointer); Invalidate(); ViewChanged?.Invoke(this,EventArgs.Empty); }
        e.Handled=true;
    }
    public void Dispose() { Session.DocumentChanged-=DocumentChanged; Session.SelectionChanged-=SelectionChanged; _adorners.Dispose();Renderer.Dispose(); }
}
