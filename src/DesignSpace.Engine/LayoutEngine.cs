using System.Runtime.CompilerServices;
using DesignSpace.Core;
namespace DesignSpace.Engine;

public interface ITextMetrics { DSize Measure(string text,double size,string family); }
public interface IConstrainedTextMetrics : ITextMetrics { DSize Measure(string text,double size,string family,double width,bool wrap); }
public sealed class ApproximateTextMetrics : IConstrainedTextMetrics
{
    public DSize Measure(string text,double size,string family)=>Measure(text,size,family,double.PositiveInfinity,false);
    public DSize Measure(string text,double size,string family,double width,bool wrap)
    {
        var lines=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Split('\n');
        var max=lines.Select(s=>s.Length*size*.53).DefaultIfEmpty(0).Max();
        var count=wrap && double.IsFinite(width) && width>0 ? lines.Sum(s=>Math.Max(1,(int)Math.Ceiling(s.Length*size*.53/width))) : lines.Length;
        return new(wrap ? Math.Min(max,width) : max,count*size*1.35);
    }
}
/// <summary>Cached two-phase design layout with intrinsic Auto tracks, constrained Star tracks and transformed picking.</summary>
public sealed class LayoutEngine(ITextMetrics? metrics=null)
{
    private readonly ITextMetrics _metrics=metrics ?? new ApproximateTextMetrics();
    private sealed class NodeCache
    {
        public Dictionary<DSize,DSize> Sizes { get; }=[];
        public GridTrack[]? Columns,Rows;
    }
    private readonly ConditionalWeakTable<DesignNode,NodeCache> _cache=new();
    public long MeasureEvaluations { get; private set; }
    public long MeasureCacheHits { get; private set; }
    public void ClearCache()=>_cache.Clear();
    private static double Limit(double value,double min,double max)=>Math.Clamp(Math.Max(0,value),Math.Max(0,min),Math.Max(min,max));
    private static DSize Inner(DSize size,Insets insets)=>new(Math.Max(0,size.Width-insets.Left-insets.Right),Math.Max(0,size.Height-insets.Top-insets.Bottom));
    private static Insets Padding(DesignNode n)
    {
        var p=Insets.Parse(n.Get("Padding")); var border=Insets.Parse(n.Get("BorderThickness"));
        return new(p.Left+border.Left,p.Top+border.Top,p.Right+border.Right,p.Bottom+border.Bottom);
    }
    private DSize Outer(DesignNode n,DSize available)
    {
        if(!n.Visible) return default; var margin=Insets.Parse(n.Get("Margin")); var size=Measure(n,Inner(available,margin));
        return new(Math.Max(0,size.Width+margin.Left+margin.Right),Math.Max(0,size.Height+margin.Top+margin.Bottom));
    }
    public DSize Measure(DesignNode node,DSize available)
    {
        if(!node.Visible) return default;
        var cache=_cache.GetOrCreateValue(node); if(cache.Sizes.TryGetValue(available,out var cached)) { MeasureCacheHits++; return cached; }
        MeasureEvaluations++;
        var pad=Padding(node);
        var width=node.Number("Width",available.Width); var height=node.Number("Height",available.Height);
        width=Limit(width,node.Number("MinWidth"),node.Number("MaxWidth",double.PositiveInfinity));
        height=Limit(height,node.Number("MinHeight"),node.Number("MaxHeight",double.PositiveInfinity));
        var inner=Inner(new(width,height),pad); var desired=new DSize(0,0);
        var text=node.Get("Text",node.Get("Content",node.TextContent)); var size=node.Number("FontSize",14); var family=node.Get("FontFamily","sans-serif");
        switch(node.Type)
        {
            case "TextBlock": case "TextBox": case "Button": case "CheckBox": case "RadioButton": case "ContentPresenter":
                desired=_metrics is IConstrainedTextMetrics constrained ? constrained.Measure(text,size,family,inner.Width,node.Get("TextWrapping")=="Wrap") : _metrics.Measure(text,size,family);
                if(node.Type is "Button" or "TextBox") desired=new(desired.Width+24,Math.Max(30,desired.Height+12));
                if(node.Type is "CheckBox" or "RadioButton") desired=new(desired.Width+24,Math.Max(20,desired.Height));
                if(!node.Children.IsEmpty) desired=ContentSize(node,inner); break;
            case "StackPanel":
                var horizontal=node.Get("Orientation")=="Horizontal"; var cursor=0d; var cross=0d; var count=0;
                foreach(var child in node.Children.Where(c=>c.Visible))
                {
                    var childSize=Outer(child,horizontal ? new(double.PositiveInfinity,inner.Height) : new(inner.Width,double.PositiveInfinity));
                    cursor+=horizontal ? childSize.Width : childSize.Height; cross=Math.Max(cross,horizontal ? childSize.Height : childSize.Width); count++;
                }
                cursor+=Math.Max(0,count-1)*node.Number("Spacing"); desired=horizontal ? new(cursor,cross) : new(cross,cursor); break;
            case "Canvas": desired=default; break;
            case "Grid":
                var plan=Plan(node,inner,false); desired=new(plan.Columns.Sum()+Math.Max(0,plan.Columns.Length-1)*plan.ColumnSpacing,plan.Rows.Sum()+Math.Max(0,plan.Rows.Length-1)*plan.RowSpacing); break;
            default: desired=ContentSize(node,inner); break;
        }
        desired=new(Limit(node.Number("Width",desired.Width+pad.Left+pad.Right),node.Number("MinWidth"),node.Number("MaxWidth",double.PositiveInfinity)),Limit(node.Number("Height",desired.Height+pad.Top+pad.Bottom),node.Number("MinHeight"),node.Number("MaxHeight",double.PositiveInfinity)));
        if(cache.Sizes.Count>=8) cache.Sizes.Clear(); cache.Sizes[available]=desired; return desired;
    }
    private DSize ContentSize(DesignNode n,DSize available)
    {
        var result=new DSize(0,0);
        foreach(var child in n.Children) { var measured=Outer(child,available); result=new(Math.Max(result.Width,measured.Width),Math.Max(result.Height,measured.Height)); }
        return result;
    }
    private GridPlan Plan(DesignNode node,DSize available,bool arrange)
    {
        var cache=_cache.GetOrCreateValue(node); var columns=cache.Columns ??= GridTracks.Read(node,false); var rows=cache.Rows ??= GridTracks.Read(node,true);
        var cs=node.Number("ColumnSpacing"); var rs=node.Number("RowSpacing"); var cw=GridTracks.Initial(columns); var rh=GridTracks.Initial(rows);
        GridTracks.Resolve(columns,cw,available.Width,cs); GridTracks.Resolve(rows,rh,available.Height,rs);
        for(var pass=0;pass<2;pass++)
        {
            foreach(var child in node.Children.Where(c=>c.Visible))
            {
                var col=GridTracks.Index(child,"Grid.Column",cw.Length); var row=GridTracks.Index(child,"Grid.Row",rh.Length);
                var colspan=GridTracks.Span(child,"Grid.ColumnSpan",cw.Length,col); var rowspan=GridTracks.Span(child,"Grid.RowSpan",rh.Length,row);
                var autoColumn=Enumerable.Range(col,colspan).Any(i=>columns[i].Unit=="Auto"); var autoRow=Enumerable.Range(row,rowspan).Any(i=>rows[i].Unit=="Auto");
                var width=(!double.IsFinite(available.Width) || (pass==0 && autoColumn)) ? double.PositiveInfinity : GridTracks.Sum(cw,col,colspan,cs);
                var height=(!double.IsFinite(available.Height) || autoRow) ? double.PositiveInfinity : GridTracks.Sum(rh,row,rowspan,rs);
                var measured=Outer(child,new(width,height));
                GridTracks.Demand(columns,cw,col,colspan,measured.Width,cs,!double.IsFinite(available.Width));
                GridTracks.Demand(rows,rh,row,rowspan,measured.Height,rs,!double.IsFinite(available.Height));
            }
            GridTracks.Resolve(columns,cw,available.Width,cs); GridTracks.Resolve(rows,rh,available.Height,rs);
        }
        return new(cw,rh,cs,rs);
    }
    public LayoutSnapshot Arrange(DesignNode root)
    {
        var entries=new List<LayoutEntry>();
        void Walk(DesignNode n,DRect bounds,int depth,Guid? parent,double opacity,DMatrix inherited,bool locked,bool visible,bool hitVisible)
        {
            if(!n.Visible) return;
            var local=DesignTransforms.Read(n,bounds); var world=inherited*local;
            locked|=n.IsLocked; visible&=n.Get("Visibility","Visible")!="Hidden"; hitVisible&=n.Get("IsHitTestVisible","True")!="False";
            opacity*=Math.Clamp(n.Number("Opacity",1),0,1);
            entries.Add(new(n,bounds,visible ? opacity : 0,depth,parent){LocalTransform=local,WorldTransform=world,IsEffectivelyLocked=locked,IsEffectivelyVisible=visible,IsHitTestVisible=hitVisible});
            var pad=Padding(n); var inner=new DRect(bounds.X+pad.Left,bounds.Y+pad.Top,Math.Max(0,bounds.Width-pad.Left-pad.Right),Math.Max(0,bounds.Height-pad.Top-pad.Bottom));
            var grid=n.Type=="Grid" ? Plan(n,new(inner.Width,inner.Height),true) : null; var cursor=0d;
            var horizontal=n.Type=="StackPanel" && n.Get("Orientation")=="Horizontal";
            var arranged=new List<(DesignNode Node,DRect Bounds)>();
            foreach(var child in n.Children.Where(c=>c.Visible))
            {
                var slot=inner; var margin=Insets.Parse(child.Get("Margin"));
                var constraint=n.Type=="Canvas" ? new DSize(double.PositiveInfinity,double.PositiveInfinity) : n.Type=="StackPanel" ? horizontal ? new(double.PositiveInfinity,inner.Height) : new(inner.Width,double.PositiveInfinity) : new(inner.Width,inner.Height);
                if(grid is not null)
                {
                    var col=GridTracks.Index(child,"Grid.Column",grid.Columns.Length); var row=GridTracks.Index(child,"Grid.Row",grid.Rows.Length);
                    var cs=GridTracks.Span(child,"Grid.ColumnSpan",grid.Columns.Length,col); var rs=GridTracks.Span(child,"Grid.RowSpan",grid.Rows.Length,row);
                    slot=new(inner.X+grid.Columns.Take(col).Sum()+col*grid.ColumnSpacing,inner.Y+grid.Rows.Take(row).Sum()+row*grid.RowSpacing,GridTracks.Sum(grid.Columns,col,cs,grid.ColumnSpacing),GridTracks.Sum(grid.Rows,row,rs,grid.RowSpacing)); constraint=new(slot.Width,slot.Height);
                }
                var desired=Measure(child,Inner(constraint,margin));
                if(n.Type=="Canvas")
                {
                    var x=child.Get("Canvas.Left").Length>0 ? child.Number("Canvas.Left") : child.Get("Canvas.Right").Length>0 ? inner.Width-child.Number("Canvas.Right")-desired.Width-margin.Left-margin.Right : 0;
                    var y=child.Get("Canvas.Top").Length>0 ? child.Number("Canvas.Top") : child.Get("Canvas.Bottom").Length>0 ? inner.Height-child.Number("Canvas.Bottom")-desired.Height-margin.Top-margin.Bottom : 0;
                    arranged.Add((child,new(inner.X+x+margin.Left,inner.Y+y+margin.Top,desired.Width,desired.Height))); continue;
                }
                if(n.Type=="StackPanel")
                {
                    slot=horizontal ? new(inner.X+cursor,inner.Y,desired.Width+margin.Left+margin.Right,inner.Height) : new(inner.X,inner.Y+cursor,inner.Width,desired.Height+margin.Top+margin.Bottom);
                    cursor+=(horizontal ? slot.Width : slot.Height)+n.Number("Spacing");
                }
                var available=Inner(new(slot.Width,slot.Height),margin); var ha=child.Get("HorizontalAlignment","Stretch"); var va=child.Get("VerticalAlignment","Stretch");
                var width=child.Number("Width",ha=="Stretch" ? available.Width : Math.Min(desired.Width,available.Width));
                var height=child.Number("Height",va=="Stretch" ? available.Height : Math.Min(desired.Height,available.Height));
                width=Limit(width,child.Number("MinWidth"),child.Number("MaxWidth",double.PositiveInfinity)); height=Limit(height,child.Number("MinHeight"),child.Number("MaxHeight",double.PositiveInfinity));
                var offsetX=ha is "Center" or "Stretch" ? (available.Width-width)/2 : ha=="Right" ? available.Width-width : 0;
                var offsetY=va is "Center" or "Stretch" ? (available.Height-height)/2 : va=="Bottom" ? available.Height-height : 0;
                arranged.Add((child,new(slot.X+margin.Left+offsetX,slot.Y+margin.Top+offsetY,width,height)));
            }
            foreach(var (node,box) in arranged.OrderBy(e=>e.Node.Number("Canvas.ZIndex",e.Node.Number("Panel.ZIndex")))) Walk(node,box,depth+1,n.Id,opacity,world,locked,visible,hitVisible);
        }
        var size=new DSize(root.Number("Width",960),root.Number("Height",640));
        Walk(root,new(0,0,size.Width,size.Height),0,null,1,DMatrix.Identity,false,true,true);
        return new(entries);
    }
}
