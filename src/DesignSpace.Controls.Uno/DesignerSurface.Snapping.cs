using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using SkiaSharp;
namespace DesignSpace.Controls.Uno;

public sealed partial class DesignerSurface
{
    private readonly SnaplineRenderer _snapRenderer=new();
    private SnaplineIndex? _snapIndex;
    private DRect _snapBounds;
    private DMatrix _snapToWorld=DMatrix.Identity,_snapFromWorld=DMatrix.Identity;
    private SnaplineResult _snapResult;
    public SnaplineResult SnapGuides=>_snapResult;
    public DMatrix SnapGuideTransform=>_snapToWorld;
    public int SnapTargetCount=>_snapIndex?.TargetCount??0;
    public long SnapIndexBuildCount { get; private set; }
    public void ApplyArtboardSettings(ArtboardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);settings.Validate();CancelGesture();
        Viewport.ApplyArtboardSettings(settings);Invalidate();ViewChanged?.Invoke(this,EventArgs.Empty);
    }
    private void ClearSnaplines(){_snapIndex=null;_snapResult=default;}
    private void BeginSnaplines()
    {
        ClearSnaplines();
        if(!Viewport.SnapToSnaplines||_original.Count==0||_gesture is not ("move" or "resize"))return;
        var ids=_original.Keys.ToHashSet();var parent=Session.Index.ParentOf(ids.First());
        if(parent is null||ids.Any(id=>Session.Index.ParentOf(id)?.Id!=parent.Id)||!Layout.ById.TryGetValue(parent.Id,out var host))return;
        var matrix=host.WorldTransform;
        // Current Canvas resize authoring uses axis-aligned parent coordinates.
        // Do not offer misleading guides for reflected, rotated or skewed parent spaces.
        if(matrix.M12!=0||matrix.M21!=0||matrix.M11<=0||matrix.M22<=0||!matrix.TryInvert(out var inverse))return;
        _snapToWorld=matrix;_snapFromWorld=inverse;
        _snapBounds=DRect.Union(ids.Select(id=>inverse.MapBounds(Layout.ById[id].VisualBounds)));
        var targets=Layout.Entries.Where(e=>e.ParentId==parent.Id&&!ids.Contains(e.Node.Id)&&e.IsEffectivelyVisible&&Session.Index.Find(e.Node.Id) is not null)
            .Select(e=>inverse.MapBounds(e.VisualBounds));
        var padding=Insets.Parse(parent.Get("Padding"));
        var content=new DRect(host.Bounds.X+padding.Left,host.Bounds.Y+padding.Top,Math.Max(0,host.Bounds.Width-padding.Left-padding.Right),Math.Max(0,host.Bounds.Height-padding.Top-padding.Bottom));
        _snapIndex=new(targets,content,Viewport.DefaultMargin,Viewport.DefaultPadding);SnapIndexBuildCount++;
    }
    private DPoint? SnapMove(DPoint worldDelta)
    {
        _snapResult=default;
        if(_snapIndex is null||DesignerKeys.Alt)return null;
        var delta=_snapFromWorld.MapVector(worldDelta);
        _snapResult=_snapIndex.Move(_snapBounds.Translate(delta.X,delta.Y),Viewport.SnapTolerance/(Viewport.Zoom*_snapToWorld.M11),Viewport.SnapTolerance/(Viewport.Zoom*_snapToWorld.M22));
        return new(_snapResult.Bounds.X-_snapBounds.X,_snapResult.Bounds.Y-_snapBounds.Y);
    }
    private DRect SnapResize(DRect box,DRect original)
    {
        _snapResult=default;
        if(_snapIndex is null||DesignerKeys.Alt||DesignerKeys.Shift)return box;
        var proposed=new DRect(_snapBounds.X+box.X-original.X,_snapBounds.Y+box.Y-original.Y,box.Width,box.Height);
        var edges=SnapEdges.None;
        if(_resizeHandle is 0 or 3 or 5)edges|=SnapEdges.Left;
        if(_resizeHandle is 2 or 4 or 7)edges|=SnapEdges.Right;
        if(_resizeHandle is 0 or 1 or 2)edges|=SnapEdges.Top;
        if(_resizeHandle is 5 or 6 or 7)edges|=SnapEdges.Bottom;
        _snapResult=_snapIndex.Resize(proposed,edges,Viewport.SnapTolerance/(Viewport.Zoom*_snapToWorld.M11),Viewport.SnapTolerance/(Viewport.Zoom*_snapToWorld.M22));
        return new(box.X+_snapResult.Bounds.X-proposed.X,box.Y+_snapResult.Bounds.Y-proposed.Y,_snapResult.Bounds.Width,_snapResult.Bounds.Height);
    }
    private void DrawSnaplines(SKCanvas canvas)
    {
        if(_gesture is "move" or "resize")_snapRenderer.Draw(canvas,_snapResult,_snapToWorld,Viewport);
    }
}
