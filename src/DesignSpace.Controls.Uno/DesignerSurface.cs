using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Animation;
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
public sealed class DesignerSurface : Grid,IDisposable
{
    private sealed class Surface(DesignerSurface owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas,Size area) { owner.Renderer.Draw(canvas,area.Width,area.Height,owner.Layout,owner.Viewport,owner.Session.Selection,owner._marquee,owner.IsPreview); owner.Rendered?.Invoke(owner,EventArgs.Empty); }
    }
    private readonly Surface _surface;
    private readonly Button _focus=new() { Width=1,Height=1,Opacity=.001,MinHeight=0,MinWidth=0,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top };
    private LayoutSnapshot? _layout;
    private DesignNode? _previewRoot;
    private DPoint _start,_screenStart;
    private DPoint _panStart;
    private Dictionary<Guid,DRect> _original=[];
    private DRect? _marquee;
    private string _gesture="";
    private int _resizeHandle=-1;
    private bool _fitPending=true;
    public DesignSession Session { get; }
    public DesignRenderer Renderer { get; }=new();
    public DesignViewport Viewport { get; }=new();
    public LayoutSnapshot Layout=>_layout ??=new LayoutEngine(Renderer).Arrange(PreviewResolver?.Invoke(_previewRoot ?? Session.Document.Root) ?? _previewRoot ?? Session.Document.Root);
    public Func<DesignNode,DesignNode>? PreviewResolver { get; set; }
    public string Tool { get; set; }="Selection";
    public bool IsPreview { get; set; }
    public bool IsPlaying { get; private set; }
    public double Playhead { get; private set; }
    public DesignStoryboard? Storyboard { get; private set; }
    public DesignState? State { get; private set; }
    public event EventHandler? Rendered;
    public event EventHandler? ViewChanged;
    public event EventHandler<Guid>? PreviewClicked;
    public event EventHandler<string>? Error;
    public DesignerSurface(DesignSession session)
    {
        Session=session; Background=StudioTheme.Brush("#2D2D30"); _surface=new(this); Children.Add(_surface); Children.Add(_focus);
        AutomationProperties.SetName(_surface,"Design artboard"); AutomationProperties.SetName(_focus,"Designer keyboard focus");
        _surface.PointerPressed+=Pressed; _surface.PointerMoved+=Moved; _surface.PointerReleased+=Released;
        _surface.PointerCaptureLost+=(_,_)=> { if(_gesture.Length>0) CancelGesture(); };
        _surface.PointerWheelChanged+=(_,e)=> { var p=e.GetCurrentPoint(_surface); var pos=new DPoint(p.Position.X,p.Position.Y); if(DesignerKeys.Control) Viewport.ZoomAt(pos,Viewport.Zoom*Math.Pow(1.12,p.Properties.MouseWheelDelta/120d)); else if(DesignerKeys.Shift) Viewport.PanX+=p.Properties.MouseWheelDelta*.35; else Viewport.PanY+=p.Properties.MouseWheelDelta*.35; Invalidate(); ViewChanged?.Invoke(this,EventArgs.Empty); e.Handled=true; };
        _surface.DoubleTapped+=(_,e)=> { var p=e.GetPosition(_surface); var hit=Layout.HitTest(Viewport.ScreenToWorld(new(p.X,p.Y))); if(hit is not null) Session.Select(hit.Node.Id); e.Handled=true; };
        Session.DocumentChanged+=DocumentChanged; Session.SelectionChanged+=SelectionChanged;
        SizeChanged+=(_,_)=> { if(_fitPending && ActualWidth>100 && ActualHeight>100) { Fit(); _fitPending=false; } else Invalidate(); };
    }
    private void DocumentChanged(object? sender,EventArgs e) { _previewRoot=null; Storyboard=null; State=null; _layout=null; Invalidate(); }
    private void SelectionChanged(object? sender,EventArgs e)=>Invalidate();
    public void FocusDesigner()=>_focus.Focus(FocusState.Programmatic);
    public void Invalidate()=>_surface.Invalidate();
    public void InvalidateLayout() { _layout=null; Invalidate(); }
    public void Fit() { Viewport.Fit(ActualWidth,ActualHeight,Layout.Entries[0].Bounds); Invalidate(); ViewChanged?.Invoke(this,EventArgs.Empty); }
    public void Zoom(double factor) { Viewport.ZoomAt(new(ActualWidth/2,ActualHeight/2),Viewport.Zoom*factor); Invalidate(); ViewChanged?.Invoke(this,EventArgs.Empty); }
    public void SetPreview(DesignStoryboard? board,double time,DesignState? state=null)
    {
        Storyboard=board; Playhead=time; State=state; _previewRoot=AnimationEngine.Evaluate(Session.Document.Root,board,time,state); _layout=null; Invalidate();
    }
    public void ClearPreview() { Storyboard=null; State=null; _previewRoot=null; _layout=null; Playhead=0; Invalidate(); }
    public void CancelGesture() { _gesture=""; _marquee=null; _previewRoot=null; _layout=null; _original.Clear(); Invalidate(); }
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        var point=e.GetCurrentPoint(_surface); var p=new DPoint(point.Position.X,point.Position.Y); _start=Viewport.ScreenToWorld(p); _screenStart=p; _panStart=new(Viewport.PanX,Viewport.PanY); FocusDesigner();
        if(point.Properties.IsMiddleButtonPressed || Tool=="Hand" || DesignerKeys.Down(VirtualKey.Space)) _gesture="pan";
        else if(!point.Properties.IsLeftButtonPressed) return;
        else if(IsPreview) { var hit=Layout.HitTest(_start); if(hit is not null) PreviewClicked?.Invoke(this,hit.Node.Id); return; }
        else if(Tool=="Zoom") { Viewport.ZoomAt(p,Viewport.Zoom*(DesignerKeys.Alt ? .8 : 1.25)); Invalidate(); return; }
        else if(Tool!="Selection" && Tool!="Direct Selection") { _gesture="draw"; _marquee=new(_start.X,_start.Y,0,0); }
        else
        {
            _resizeHandle=-1;
            if(Session.Selection.Count==1)
            {
                var id=Session.Selection.Single(); if(Layout.ById.TryGetValue(id,out var selected) && !selected.Node.IsLocked && Session.Document.Root.ParentOf(id)?.Type=="Canvas" && selected.Node.Rotation==0)
                {
                    var b=selected.Bounds; var points=new[]{new DPoint(b.X,b.Y),new(b.Center.X,b.Y),new(b.Right,b.Y),new(b.X,b.Center.Y),new(b.Right,b.Center.Y),new(b.X,b.Bottom),new(b.Center.X,b.Bottom),new(b.Right,b.Bottom)};
                    for(var i=0;i<points.Length;i++) if(Math.Abs(_start.X-points[i].X)*Viewport.Zoom<7 && Math.Abs(_start.Y-points[i].Y)*Viewport.Zoom<7) _resizeHandle=i;
                }
            }
            if(_resizeHandle>=0) _gesture="resize";
            else
            {
                var hit=Layout.HitTest(_start);
                if(hit is not null)
                {
                    if(!Session.Selection.Contains(hit.Node.Id) || DesignerKeys.Control) Session.Select(hit.Node.Id,DesignerKeys.Control);
                    _gesture="move";
                }
                else { if(!DesignerKeys.Control) Session.Select([]); _gesture="marquee"; _marquee=new(_start.X,_start.Y,0,0); }
            }
            _original=Session.TopLevelSelection().Select(id=>Session.Document.Root.Find(id)!).Where(n=>!n.IsLocked && Session.Document.Root.ParentOf(n.Id)?.Type=="Canvas").ToDictionary(n=>n.Id,n=>new DRect(n.Number("Canvas.Left"),n.Number("Canvas.Top"),n.Number("Width"),n.Number("Height")));
        }
        _surface.CapturePointer(e.Pointer); e.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        if(_gesture.Length==0) return;
        var point=e.GetCurrentPoint(_surface).Position; var screen=new DPoint(point.X,point.Y); var p=Viewport.ScreenToWorld(screen); var delta=p-_start;
        if(_gesture=="pan") { Viewport.PanX=_panStart.X+screen.X-_screenStart.X; Viewport.PanY=_panStart.Y+screen.Y-_screenStart.Y; Invalidate(); return; }
        if(_gesture is "draw" or "marquee") { _marquee=DRect.FromPoints(_start,p); Invalidate(); return; }
        var root=Session.Document.Root;
        foreach(var pair in _original)
        {
            var b=pair.Value;
            if(_gesture=="move") b=b.Translate(delta.X,delta.Y);
            else
            {
                var left=b.X; var top=b.Y; var right=b.Right; var bottom=b.Bottom;
                if(_resizeHandle is 0 or 3 or 5) left=Math.Min(right-1,left+delta.X);
                if(_resizeHandle is 2 or 4 or 7) right=Math.Max(left+1,right+delta.X);
                if(_resizeHandle is 0 or 1 or 2) top=Math.Min(bottom-1,top+delta.Y);
                if(_resizeHandle is 5 or 6 or 7) bottom=Math.Max(top+1,bottom+delta.Y);
                b=new(left,top,right-left,bottom-top);
                if(DesignerKeys.Shift) b=b with { Height=b.Width*pair.Value.Height/Math.Max(1,pair.Value.Width) };
            }
            if(Viewport.SnapToGrid && !DesignerKeys.Alt) b=b with { X=Numbers.Snap(b.X,Viewport.GridSize),Y=Numbers.Snap(b.Y,Viewport.GridSize),Width=Math.Max(1,Numbers.Snap(b.Width,Viewport.GridSize)),Height=Math.Max(1,Numbers.Snap(b.Height,Viewport.GridSize)) };
            root=root.Update(pair.Key,n=>n.Set("Canvas.Left",b.X).Set("Canvas.Top",b.Y).Set("Width",b.Width).Set("Height",b.Height));
        }
        _previewRoot=root; _layout=null; Invalidate(); e.Handled=true;
    }
    private void Released(object sender,PointerRoutedEventArgs e)
    {
        var gesture=_gesture; _gesture="";
        try
        {
            if(gesture=="draw" && _marquee is { } box)
            {
                if(box.Width<3 || box.Height<3) box=new(box.X,box.Y,Tool=="TextBlock" ? 200 : 120,Tool is "TextBlock" or "Button" ? 40 : 80);
                Session.Add(Tool,box);
            }
            else if(gesture=="marquee" && _marquee is { } selection) Session.Select(Layout.Entries.Where(x=>x.Depth>0 && !x.Node.IsLocked && selection.Intersects(x.Bounds)).Select(x=>x.Node.Id),DesignerKeys.Control);
            else if((gesture is "move" or "resize") && _previewRoot is not null)
            {
                var next=_original.Keys.ToDictionary(id=>id,id=> { var n=_previewRoot.Find(id)!; return new DRect(n.Number("Canvas.Left"),n.Number("Canvas.Top"),n.Number("Width"),n.Number("Height")); });
                if(next.Any(p=>p.Value!=_original[p.Key])) Session.SetBounds(next);
            }
        }
        catch(Exception ex) { Error?.Invoke(this,ex.Message); }
        finally { _marquee=null; _previewRoot=null; _layout=null; _original.Clear(); _surface.ReleasePointerCapture(e.Pointer); Invalidate(); ViewChanged?.Invoke(this,EventArgs.Empty); }
        e.Handled=true;
    }
    public void Dispose() { Session.DocumentChanged-=DocumentChanged; Session.SelectionChanged-=SelectionChanged; Renderer.Dispose(); }
}
