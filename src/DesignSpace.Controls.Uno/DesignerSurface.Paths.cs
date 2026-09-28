using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Windows.System;
namespace DesignSpace.Controls.Uno;

public sealed partial class DesignerSurface
{
    private sealed record EditInfo(DesignNode Node,DRect Bounds,DMatrix ToWorld,VectorPath Geometry);
    private EditInfo? _editInfo,_dragInfo;
    private PathHandle? _pathHandle;
    private PathLocation? _pathSegment;
    private VectorPath? _pathPreview;
    private readonly PathAdornerRenderer _adorners=new();
    private readonly List<(DPoint Point,DPoint Handle)> _pen=[];
    private readonly List<DPoint> _pencil=[];
    private Guid? _pathOwner,_penParent;
    private long _pathRevision,_penRevision;
    private DMatrix _penToWorld=DMatrix.Identity;
    private DPoint _pathDown,_penDown,_penHandle;
    private DPoint? _penHover;
    private bool _pathCommitting;
    public event EventHandler? PathEditingChanged;
    public int DraftPointCount=>_pen.Count+_pencil.Count;
    public PathHandle? SelectedPathHandle=>_pathHandle;
    private EditInfo? CurrentPath()
    {
        if(_dragInfo is not null&&_pathPreview is not null)return _dragInfo with { Geometry=_pathPreview };
        if(Session.Selection.Count!=1)return null;
        var n=Session.Index.Find(Session.Selection.Single());if(n?.Type!="Path"||!Layout.ById.TryGetValue(n.Id,out var entry))return null;
        var matrix=entry.WorldTransform*DMatrix.Translate(entry.Bounds.X,entry.Bounds.Y);
        if(_editInfo is not null&&ReferenceEquals(n,_editInfo.Node)&&entry.Bounds==_editInfo.Bounds&&matrix==_editInfo.ToWorld)return _editInfo;
        return _editInfo=new(n,entry.Bounds,matrix,VectorGeometry.Local(n,new(entry.Bounds.Width,entry.Bounds.Height)));
    }
    public object PathDiagnostics
    {
        get
        {
            EditInfo? info;try{info=CurrentPath();}catch{info=null;}
            var screen=DMatrix.Translate(Viewport.PanX,Viewport.PanY)*DMatrix.Scale(Viewport.Zoom,Viewport.Zoom)*(info?.ToWorld ?? DMatrix.Identity);
            return new
            {
                selectedAnchors=_anchors.Select(h=>new{figure=h.Figure,segment=h.Segment}).ToArray(),anchorCount=_anchors.Count,
                draftPoints=DraftPointCount,gesture=_gesture,owner=_pathOwner,selected=_pathHandle?.ToString(),
                figures=info?.Geometry.Figures.Length ?? 0,segments=info?.Geometry.SegmentCount ?? 0,
                handles=info is null ? [] : PathEditing.Handles(info.Geometry).Select(h=>new{figure=h.Handle.Figure,segment=h.Handle.Segment,kind=h.Handle.Kind.ToString(),selected=_anchors.Contains(h.Handle)||_pathHandle==h.Handle,x=screen.Map(h.Point).X,y=screen.Map(h.Point).Y}).ToArray()
            };
        }
    }
    private void NotifyPaths(){Invalidate();PathEditingChanged?.Invoke(this,EventArgs.Empty);}
    private void CancelPathDraft()
    {
        CancelAnchorMarquee();
        _pen.Clear();_pencil.Clear();_penParent=null;_penHover=null;_dragInfo=null;_pathPreview=null;
        if(_gesture.StartsWith("path-",StringComparison.Ordinal)||_gesture is "pen-add" or "pencil")_gesture="";
        NotifyPaths();
    }
    private void PathDocumentChanged()
    {
        if(!_pathCommitting){CancelPathDraft();ClearAnchorSelection();}
        _editInfo=null;NotifyPaths();
    }
    private void PathSelectionChanged()
    {
        if(_pathOwner is { } id&&(Session.Selection.Count!=1||!Session.Selection.Contains(id))){_pathOwner=null;ClearAnchorSelection();_editInfo=null;}
        NotifyPaths();
    }
    private void DrawPathOverlay(SKCanvas canvas)
    {
        if(IsPreview)return;
        try
        {
            if(_penParent is not null)
            {
                var path=_gesture=="pencil" ? new VectorPath([new(_pencil[0],_pencil.Skip(1).Select(VectorSegment.Line).ToImmutableArray())]) : PenGeometry(false,_gesture=="pen-add");
                _adorners.Draw(canvas,path,_penToWorld,Viewport,null,_penHover,true);return;
            }
            if(Tool is "Direct Selection" or "Pen" or "Path"&&CurrentPath() is { } info)_adorners.Draw(canvas,info.Geometry,info.ToWorld,Viewport,_pathHandle,null,true,_anchors);
        }
        catch(InvalidDataException){ /* Unsupported preserved geometry is diagnosed by the scene renderer. */ }
    }
    private static PathHandle? HitHandle(EditInfo info,DPoint world,double tolerance)
    {
        PathHandle? best=null;var distance=tolerance;
        foreach(var (handle,point,anchor) in PathEditing.Handles(info.Geometry))
        {
            if(handle.Kind!=PathHandleKind.Anchor&&VectorMath.Length(info.ToWorld.Map(point)-info.ToWorld.Map(anchor))<tolerance*.2)continue;
            var d=VectorMath.Length(info.ToWorld.Map(point)-world);if(d<=distance){distance=d;best=handle;}
        }
        return best;
    }
    private DPoint SnapPoint(DPoint point)=>Viewport.SnapToGrid&&!DesignerKeys.Alt ? new(Numbers.Snap(point.X,Viewport.GridSize),Numbers.Snap(point.Y,Viewport.GridSize)) : point;
    private bool PathPressed(PointerRoutedEventArgs e,DPoint world)
    {
        if(Tool is not ("Pen" or "Path" or "Pencil" or "Direct Selection"))return false;
        try
        {
            var info=CurrentPath();
            if(_pen.Count==0&&Tool!="Pencil"&&info is not null&&!Session.Index.IsLocked(info.Node.Id))
            {
                var handle=HitHandle(info,world,7/Viewport.Zoom);
                var hit=VectorMath.Nearest(info.Geometry,world,info.ToWorld,6/Viewport.Zoom);
                if(handle is not null||hit is not null)
                {
                    _pathOwner=info.Node.Id;_pathHandle=handle;_pathSegment=handle is null ? hit : null;
                    if(handle is null)_anchors=[];
                    if(Tool is "Pen" or "Path")
                    {
                        var next=handle is { Kind:PathHandleKind.Anchor } anchor ? PathEditing.RemoveAnchor(info.Geometry,anchor) : hit is { } segment ? PathEditing.Insert(info.Geometry,segment.Figure,segment.Segment,Math.Clamp(segment.Time,.001,.999)) : info.Geometry;
                        CommitPath(info,next,Session.Revision,handle is { Kind:PathHandleKind.Anchor } ? "Remove path point" : "Insert path point");ClearAnchorSelection();NotifyPaths();return true;
                    }
                    if(handle is not null)
                    {
                        if(!SelectPathHandle(handle.Value)){NotifyPaths();return true;}
                        if(!info.ToWorld.TryInvert(out var inverse))throw new InvalidOperationException("Path transform is not invertible.");
                        _dragInfo=info;_pathPreview=info.Geometry;_pathRevision=Session.Revision;_pathDown=inverse.Map(world);_gesture="path-edit";_surface.CapturePointer(e.Pointer);
                    }
                    NotifyPaths();return true;
                }
            }
            if(Tool=="Direct Selection"&&info is not null&&!Session.Index.IsLocked(info.Node.Id))
            {
                var under=Layout.HitTest(world);
                if(DesignerKeys.Shift||under is null||under.Node.Id==info.Node.Id){BeginAnchorMarquee(e,world,info);return true;}
            }
            if(Tool=="Direct Selection")
            {
                var hit=Layout.HitStack(world).FirstOrDefault(entry=>Session.Index.Find(entry.Node.Id)?.Type=="Path");
                if(hit is null)return false;Session.Select(hit.Node.Id);_pathOwner=hit.Node.Id;ClearAnchorSelection();NotifyPaths();return true;
            }
            if(_penParent is null)
            {
                var parent=Session.Selection.Select(Session.Index.Find).FirstOrDefault(n=>n?.Type=="Canvas") ?? Session.Document.Root.DescendantsAndSelf().FirstOrDefault(n=>n.Type=="Canvas");
                if(parent is null||Session.Index.IsLocked(parent.Id)||!Layout.ById.TryGetValue(parent.Id,out var entry))throw new InvalidOperationException("Select an unlocked Canvas to draw inside.");
                var pad=Insets.Parse(parent.Get("Padding"));var border=Insets.Parse(parent.Get("BorderThickness"));
                _penToWorld=entry.WorldTransform*DMatrix.Translate(entry.Bounds.X+pad.Left+border.Left,entry.Bounds.Y+pad.Top+border.Top);_penParent=parent.Id;_penRevision=Session.Revision;
            }
            if(!_penToWorld.TryInvert(out var inv))throw new InvalidOperationException("Drawing container is not invertible.");
            if(_pen.Count>=2&&VectorMath.Length(_penToWorld.Map(_pen[0].Point)-world)*Viewport.Zoom<=8){FinishPath(true);return true;}
            _penDown=_penHandle=SnapPoint(inv.Map(world));_penHover=null;
            if(Tool=="Pencil"){_pencil.Clear();_pencil.Add(_penDown);_gesture="pencil";}else _gesture="pen-add";
            _surface.CapturePointer(e.Pointer);NotifyPaths();return true;
        }
        catch(Exception ex){Error?.Invoke(this,ex.Message);CancelPathDraft();return true;}
    }
    private bool PathMoved(DPoint world)
    {
        try
        {
            if(MoveAnchorMarquee(world))return true;
            if(_gesture=="path-edit"&&_dragInfo is { } info&&_pathHandle is { } handle)
            {
                if(Session.Revision!=_pathRevision)throw new InvalidOperationException("The document changed during path editing.");
                info.ToWorld.TryInvert(out var inv);var point=PathEditing.Position(info.Geometry,handle)+inv.Map(world)-_pathDown;
                _pathPreview=handle.Kind==PathHandleKind.Anchor ? MoveSelectedAnchors(info,handle,point) : PathEditing.Move(info.Geometry,handle,point,mirror:!DesignerKeys.Alt);
                _previewRoot=Session.Document.Root.Update(info.Node.Id,n=>VectorGeometry.WithPath(n,_pathPreview));_layout=null;NotifyPaths();return true;
            }
            if(_penParent is not null)
            {
                _penToWorld.TryInvert(out var inverse);var point=inverse.Map(world);
                if(_gesture=="pencil")
                {
                    if(VectorMath.Length(point-_pencil[^1])*Viewport.Zoom>=1.5)
                    {
                        if(_pencil.Count>=VectorPathCodec.MaxSegments)throw new InvalidOperationException("The freehand stroke reached its point limit.");
                        _pencil.Add(point);
                    }
                }
                else if(_gesture=="pen-add")_penHandle=point;else _penHover=point;
                NotifyPaths();return true;
            }
        }
        catch(Exception ex){Error?.Invoke(this,ex.Message);CancelPathDraft();ClearPreview();return true;}
        return false;
    }
    private bool PathReleased(string gesture)
    {
        if(CompleteAnchorMarquee(gesture))return true;
        if(gesture is not ("path-edit" or "pen-add" or "pencil"))return false;
        try
        {
            if(gesture=="path-edit"&&_dragInfo is { } info&&_pathPreview is { } edited)
            {
                if(!ReferenceEquals(info.Geometry,edited))CommitPath(info,edited,_pathRevision,"Move path "+(_pathHandle?.Kind==PathHandleKind.Anchor ? (_anchors.Count>1 ? "points" : "point") : "tangent"));
                _dragInfo=null;_pathPreview=null;_editInfo=null;
            }
            else if(gesture=="pen-add")
            {
                if(_pen.Count>=VectorPathCodec.MaxSegments)throw new InvalidOperationException("The pen path reached its point limit.");
                var tangent=VectorMath.Length(_penHandle-_penDown)*Viewport.Zoom>=3 ? _penHandle : _penDown;
                _pen.Add((_penDown,tangent));
            }
            else if(gesture=="pencil")
            {
                var path=PathEditing.Freehand(_pencil,1.25/Viewport.Zoom);if(path.SegmentCount>0)CommitNewPath(path,"Draw pencil path");CancelPathDraft();
            }
            NotifyPaths();return true;
        }
        catch(Exception ex){Error?.Invoke(this,ex.Message);CancelPathDraft();return true;}
    }
    private VectorPath PenGeometry(bool closed,bool includePending=false)
    {
        var points=_pen.ToList();if(includePending)points.Add((_penDown,_penHandle));if(points.Count==0)return VectorPath.Empty;
        var segments=ImmutableArray.CreateBuilder<VectorSegment>();
        for(var i=1;i<points.Count+(closed ? 1 : 0);i++)
        {
            var a=points[i-1];var b=points[i%points.Count];var incoming=VectorMath.Lerp(b.Handle,b.Point,2);
            if(a.Handle==a.Point&&b.Handle==b.Point){if(i<points.Count)segments.Add(VectorSegment.Line(b.Point));}
            else segments.Add(VectorSegment.Cubic(a.Handle,incoming,b.Point));
        }
        return new([new(points[0].Point,segments.ToImmutable(),closed)]);
    }
    private void CommitNewPath(VectorPath geometry,string label)
    {
        if(_penParent is not { } parent||Session.Revision!=_penRevision||Session.Index.IsLocked(parent))throw new InvalidOperationException("The drawing container changed; the draft was not applied.");
        var node=PathCommands.CreateNode(Session.UniqueName("Path"),geometry);
        _pathCommitting=true;
        try{Session.Execute(label,d=>d with { Root=d.Root.Update(parent,n=>n with { Children=n.Children.Add(node) }) },[node.Id]);_pathOwner=node.Id;_pathHandle=null;_pathSegment=null;}
        finally{_pathCommitting=false;}
    }
    private void CommitPath(EditInfo info,VectorPath geometry,long revision,string label)
    {
        if(Session.Revision!=revision||!ReferenceEquals(Session.Index.Find(info.Node.Id),info.Node)||Session.Index.IsLocked(info.Node.Id))throw new InvalidOperationException("The path changed; the edit was not applied.");
        _pathCommitting=true;
        try{Session.Execute(label,d=>d with { Root=d.Root.Update(info.Node.Id,n=>VectorGeometry.WithPath(n,geometry)) });}
        finally{_pathCommitting=false;}
    }
    public void FinishPath(bool closed=false)
    {
        if(_pen.Count<2){CancelPathDraft();return;}
        CommitNewPath(PenGeometry(closed),closed ? "Draw closed pen path" : "Draw pen path");CancelPathDraft();ClearPreview();
    }
    public void PathCommand(string command)
    {
        if(command=="Delete point"&&!_anchors.IsEmpty)command="Delete points";
        if(HandleAnchorCommand(command))return;
        if(command=="Finish"){FinishPath();return;}if(command=="Cancel"){CancelPathDraft();ClearPreview();return;}
        if(command=="Convert"){PathCommands.Convert(Session,Layout);Tool="Direct Selection";NotifyPaths();return;}
        if(command is "Unite" or "Subtract" or "Intersect" or "Exclude" or "Divide" or "Compound"){PathCommands.Combine(Session,Layout,command);return;}
        if(command=="Make clip"){PathCommands.ApplyClip(Session,Layout);return;}
        if(command=="Release clip"){PathCommands.ReleaseClip(Session);return;}
        if(command=="Break apart"){PathCommands.BreakApart(Session,Layout);return;}
        var info=CurrentPath() ?? throw new InvalidOperationException("Select a path first.");
        var figure=_pathHandle?.Figure ?? _pathSegment?.Figure ?? 0;var segment=_pathHandle?.Segment ?? _pathSegment?.Segment ?? -1;
        if(info.Geometry.Figures.IsEmpty)throw new InvalidOperationException("The selected path is empty.");
        var next=command switch
        {
            "Insert"=>PathEditing.Insert(info.Geometry,figure,segment<0 ? 0 : segment,.5),
            "Delete segment"=>PathEditing.RemoveSegment(info.Geometry,figure,segment),
            "Delete point"=>PathEditing.RemoveAnchor(info.Geometry,_pathHandle ?? throw new InvalidOperationException("Select a path anchor.")),
            "Curve"=>PathEditing.SetSegmentKind(info.Geometry,figure,segment,true),
            "Straight"=>PathEditing.SetSegmentKind(info.Geometry,figure,segment,false),
            "Close"=>PathEditing.SetClosed(info.Geometry,figure,true),"Open"=>PathEditing.SetClosed(info.Geometry,figure,false),
            "EvenOdd"=>info.Geometry with { NonZero=false },"Nonzero"=>info.Geometry with { NonZero=true },
            _=>throw new InvalidOperationException("Unknown path command.")
        };
        CommitPath(info,next,Session.Revision,command+" path");ClearAnchorSelection();NotifyPaths();
    }
    public bool HandlePathKey(VirtualKey key,bool control,bool shift)
    {
        if(HandleAnchorKey(key,control,shift))return true;
        if(_penParent is not null)
        {
            if(key==VirtualKey.Escape){CancelPathDraft();ClearPreview();return true;}
            if(key==VirtualKey.Enter){FinishPath();return true;}
            if(key is VirtualKey.Back or VirtualKey.Delete||control&&key==VirtualKey.Z){if(_pen.Count>0)_pen.RemoveAt(_pen.Count-1);if(_pen.Count==0)CancelPathDraft();NotifyPaths();return true;}
        }
        if(Tool=="Direct Selection"&&_pathSegment is not null&&!control&&key is VirtualKey.Delete or VirtualKey.Back){PathCommand("Delete segment");return true;}
        if(Tool=="Direct Selection"&&_pathHandle is { } handle&&CurrentPath() is { } info&&!control)
        {
            if(key is VirtualKey.Delete or VirtualKey.Back){PathCommand("Delete point");return true;}
            var step=shift ? 10 : 1;var delta=key switch { VirtualKey.Left=>new DPoint(-step,0),VirtualKey.Right=>new(step,0),VirtualKey.Up=>new(0,-step),VirtualKey.Down=>new(0,step),_=>default };
            if(delta!=default){CommitPath(info,PathEditing.Move(info.Geometry,handle,PathEditing.Position(info.Geometry,handle)+delta),Session.Revision,"Nudge path point");NotifyPaths();return true;}
        }
        return false;
    }
}
