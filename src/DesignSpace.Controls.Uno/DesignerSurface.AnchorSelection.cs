using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using Microsoft.UI.Xaml.Input;
using Windows.System;
namespace DesignSpace.Controls.Uno;

public sealed partial class DesignerSurface
{
    private ImmutableHashSet<PathHandle> _anchors=[];
    private ImmutableHashSet<PathHandle> _marqueeAnchorBase=[];
    private EditInfo? _anchorMarqueeInfo;
    private DPoint _anchorMarqueeStart;
    private bool _anchorMarqueeAdditive;
    public IReadOnlySet<PathHandle> SelectedPathAnchors=>_anchors;

    private bool SelectPathHandle(PathHandle handle)
    {
        _pathSegment=null;
        if(handle.Kind!=PathHandleKind.Anchor) { _anchors=[];_pathHandle=handle;return true; }
        if(DesignerKeys.Shift)
        {
            if(_anchors.Contains(handle)) { _anchors=_anchors.Remove(handle);_pathHandle=null;return false; }
            _anchors=_anchors.Add(handle);
        }
        else if(!_anchors.Contains(handle)) _anchors=[handle];
        _pathHandle=handle; return true;
    }
    private void BeginAnchorMarquee(PointerRoutedEventArgs e,DPoint world,EditInfo info)
    {
        _anchorMarqueeInfo=info;_marqueeAnchorBase=_anchors;_anchorMarqueeAdditive=DesignerKeys.Shift;
        _anchorMarqueeStart=world;_pathRevision=Session.Revision;_pathOwner=info.Node.Id;
        _pathHandle=null;_pathSegment=null;_gesture="path-marquee";_marquee=new(world.X,world.Y,0,0);
        if(!_anchorMarqueeAdditive) _anchors=[];
        _surface.CapturePointer(e.Pointer);NotifyPaths();
    }
    private bool MoveAnchorMarquee(DPoint world)
    {
        if(_gesture!="path-marquee" || _anchorMarqueeInfo is not { } info) return false;
        if(Session.Revision!=_pathRevision) throw new InvalidOperationException("The path changed during point selection.");
        _marquee=DRect.FromPoints(_anchorMarqueeStart,world);
        var found=PathAnchorEditing.SelectInRectangle(info.Geometry,_marquee.Value,info.ToWorld);
        _anchors=_anchorMarqueeAdditive ? _marqueeAnchorBase.Union(found) : found;
        NotifyPaths();return true;
    }
    private void CancelAnchorMarquee()
    {
        if(_anchorMarqueeInfo is null) return;
        _anchors=_marqueeAnchorBase;_anchorMarqueeInfo=null;_marquee=null;
    }
    private bool CompleteAnchorMarquee(string gesture)
    {
        if(gesture!="path-marquee") return false;
        _anchorMarqueeInfo=null;_marquee=null;NotifyPaths();return true;
    }
    private VectorPath MoveSelectedAnchors(EditInfo info,PathHandle primary,DPoint point)
    {
        var original=PathEditing.Position(info.Geometry,primary);var delta=point-original;
        // Clicking an anchor must not snap it or make a history entry.
        if(VectorMath.Length(info.ToWorld.MapVector(delta))*Viewport.Zoom<1) return info.Geometry;
        delta=SnapPoint(point)-original;
        return PathAnchorEditing.Translate(info.Geometry,_anchors.Count>0 ? _anchors : [primary],delta);
    }
    private void ClearAnchorSelection() { _anchors=[];_pathHandle=null;_pathSegment=null; }
    private bool HandleAnchorCommand(string command)
    {
        if(command is not ("Select points" or "Clear points" or "Delete points" or "Align Left" or "Align Center" or "Align Right" or "Align Top" or "Align Middle" or "Align Bottom" or "Distribute X" or "Distribute Y")) return false;
        if(command=="Clear points") { ClearAnchorSelection();NotifyPaths();return true; }
        var info=CurrentPath() ?? throw new InvalidOperationException("Select a path first.");
        if(command=="Select points")
        {
            Tool="Direct Selection";_pathOwner=info.Node.Id;_pathHandle=null;_pathSegment=null;
            _anchors=PathAnchorEditing.Selection(info.Geometry,PathAnchorEditing.Anchors(info.Geometry));NotifyPaths();return true;
        }
        if(_anchors.IsEmpty) throw new InvalidOperationException("Select anchor points with Direct Selection first.");
        var next=command=="Delete points" ? PathAnchorEditing.Remove(info.Geometry,_anchors) : command.StartsWith("Align ",StringComparison.Ordinal)
            ? PathAnchorEditing.Align(info.Geometry,_anchors,command[6..],info.ToWorld)
            : PathAnchorEditing.Distribute(info.Geometry,_anchors,command=="Distribute X",info.ToWorld);
        if(!ReferenceEquals(next,info.Geometry)) CommitPath(info,next,Session.Revision,command+" on path");
        if(command=="Delete points") ClearAnchorSelection();
        _editInfo=null;NotifyPaths();return true;
    }
    private bool HandleAnchorKey(VirtualKey key,bool control,bool shift)
    {
        if(Tool!="Direct Selection") return false;
        if(key==VirtualKey.Escape && (_anchorMarqueeInfo is not null || _gesture=="path-edit"))
        {
            CancelGesture();return true;
        }
        if(control && key==VirtualKey.A && CurrentPath() is not null) return HandleAnchorCommand(shift ? "Clear points" : "Select points");
        if(key==VirtualKey.Escape && !_anchors.IsEmpty) return HandleAnchorCommand("Clear points");
        if(control || _anchors.IsEmpty || CurrentPath() is not { } info) return false;
        if(key is VirtualKey.Delete or VirtualKey.Back) return HandleAnchorCommand("Delete points");
        var step=shift ? 10 : 1;
        var delta=key switch { VirtualKey.Left=>new DPoint(-step,0),VirtualKey.Right=>new(step,0),VirtualKey.Up=>new(0,-step),VirtualKey.Down=>new(0,step),_=>default };
        if(delta==default) return false;
        if(!info.ToWorld.TryInvert(out var inverse)) throw new InvalidOperationException("The path transform is not invertible.");
        var next=PathAnchorEditing.Translate(info.Geometry,_anchors,inverse.MapVector(delta));
        CommitPath(info,next,Session.Revision,"Nudge selected path points");NotifyPaths();return true;
    }
}
