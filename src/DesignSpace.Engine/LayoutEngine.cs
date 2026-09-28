using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Engine;

public interface ITextMetrics { DSize Measure(string text,double size,string family); }
public sealed class ApproximateTextMetrics : ITextMetrics
{
    public DSize Measure(string text,double size,string family) => new(text.Length*size*.53,size*1.35);
}
public sealed record LayoutEntry(DesignNode Node,DRect Bounds,double Opacity,int Depth,Guid? ParentId);
public sealed class LayoutSnapshot(IReadOnlyList<LayoutEntry> entries)
{
    public IReadOnlyList<LayoutEntry> Entries { get; } = entries;
    public IReadOnlyDictionary<Guid,LayoutEntry> ById { get; } = entries.ToDictionary(e=>e.Node.Id);
    public LayoutEntry? HitTest(DPoint point,bool includeLocked=false) => Entries.Reverse().FirstOrDefault(e=>e.Depth>0 && (includeLocked || !e.Node.IsLocked) && e.Bounds.ContainsRotated(point,e.Node.Rotation));
    public DRect BoundsOf(IEnumerable<Guid> ids) => DRect.Union(ids.Where(ById.ContainsKey).Select(id=>ById[id].Bounds));
}
/// <summary>Portable measure/arrange implementation for the documented design-time XAML subset.</summary>
public sealed class LayoutEngine(ITextMetrics? metrics = null)
{
    private readonly ITextMetrics _metrics=metrics ?? new ApproximateTextMetrics();
    public DSize Measure(DesignNode node,DSize available)
    {
        var text=_metrics.Measure(node.Get("Text",node.Get("Content",node.TextContent)),node.Number("FontSize",14),node.Get("FontFamily","sans-serif"));
        var content=node.Type switch
        {
            "TextBlock" => text,
            "Button" or "TextBox" => new DSize(text.Width+24,Math.Max(30,text.Height+12)),
            "StackPanel" when node.Get("Orientation") == "Horizontal" => new(node.Children.Sum(c=>Measure(c,available).Width),node.Children.Select(c=>Measure(c,available).Height).DefaultIfEmpty(0).Max()),
            "StackPanel" => new(node.Children.Select(c=>Measure(c,available).Width).DefaultIfEmpty(0).Max(),node.Children.Sum(c=>Measure(c,available).Height)),
            _ => new DSize(available.Width,available.Height)
        };
        return new(Math.Max(0,node.Number("Width",content.Width)),Math.Max(0,node.Number("Height",content.Height)));
    }
    public LayoutSnapshot Arrange(DesignNode root)
    {
        var entries=new List<LayoutEntry>();
        void Walk(DesignNode n,DRect bounds,int depth,Guid? parent,double opacity)
        {
            if (!n.Visible) return;
            opacity*=Math.Clamp(n.Number("Opacity",1),0,1);
            entries.Add(new(n,bounds,opacity,depth,parent));
            var padding=Insets.Parse(n.Get("Padding"));
            var inner=new DRect(bounds.X+padding.Left,bounds.Y+padding.Top,Math.Max(0,bounds.Width-padding.Left-padding.Right),Math.Max(0,bounds.Height-padding.Top-padding.Bottom));
            var rows=Definitions(n,"RowDefinitions","RowDefinition","Height",inner.Height);
            var cols=Definitions(n,"ColumnDefinitions","ColumnDefinition","Width",inner.Width);
            var cursor=0d;
            foreach (var c in n.Children)
            {
                if (!c.Visible) continue;
                var cell=inner; var desired=Measure(c,new(inner.Width,inner.Height)); var margin=Insets.Parse(c.Get("Margin"));
                if (n.Type=="Canvas") cell=new(inner.X+c.Number("Canvas.Left"),inner.Y+c.Number("Canvas.Top"),desired.Width,desired.Height);
                else if (n.Type=="StackPanel")
                {
                    var horizontal=n.Get("Orientation")=="Horizontal";
                    cell=horizontal ? new(inner.X+cursor,inner.Y,desired.Width+margin.Left+margin.Right,inner.Height) : new(inner.X,inner.Y+cursor,inner.Width,desired.Height+margin.Top+margin.Bottom);
                    cursor+=(horizontal ? cell.Width : cell.Height)+n.Number("Spacing");
                }
                else if (n.Type=="Grid")
                {
                    var row=Math.Clamp((int)c.Number("Grid.Row"),0,rows.Length-1); var col=Math.Clamp((int)c.Number("Grid.Column"),0,cols.Length-1);
                    var rs=Math.Clamp((int)c.Number("Grid.RowSpan",1),1,rows.Length-row); var cs=Math.Clamp((int)c.Number("Grid.ColumnSpan",1),1,cols.Length-col);
                    cell=new(inner.X+cols.Take(col).Sum(),inner.Y+rows.Take(row).Sum(),cols.Skip(col).Take(cs).Sum(),rows.Skip(row).Take(rs).Sum());
                }
                var width=Math.Min(Math.Max(0,cell.Width-margin.Left-margin.Right),desired.Width);
                var height=Math.Min(Math.Max(0,cell.Height-margin.Top-margin.Bottom),desired.Height);
                if (n.Type=="Canvas") { width=desired.Width; height=desired.Height; }
                var ha=c.Get("HorizontalAlignment","Stretch"); var va=c.Get("VerticalAlignment","Stretch");
                if (ha=="Stretch" && c.Get("Width").Length==0) width=Math.Max(0,cell.Width-margin.Left-margin.Right);
                if (va=="Stretch" && c.Get("Height").Length==0) height=Math.Max(0,cell.Height-margin.Top-margin.Bottom);
                var x=cell.X+margin.Left+(ha=="Center" ? (cell.Width-margin.Left-margin.Right-width)/2 : ha=="Right" ? cell.Width-margin.Left-margin.Right-width : 0);
                var y=cell.Y+margin.Top+(va=="Center" ? (cell.Height-margin.Top-margin.Bottom-height)/2 : va=="Bottom" ? cell.Height-margin.Top-margin.Bottom-height : 0);
                Walk(c,new(x,y,width,height),depth+1,n.Id,opacity);
            }
        }
        Walk(root,new(0,0,root.Number("Width",960),root.Number("Height",640)),0,null,1);
        return new(entries);
    }
    private static double[] Definitions(DesignNode n,string collection,string item,string property,double total)
    {
        var raw=n.PropertyElements.FirstOrDefault(s=>s.Contains("."+collection,StringComparison.Ordinal));
        if (raw is null) return [total];
        try
        {
            var values=XElement.Parse(raw).Elements().Where(e=>e.Name.LocalName==item).Select(e=>(string?)e.Attribute(property) ?? "*").ToArray();
            if (values.Length==0) return [total];
            var fixedTotal=values.Where(s=>!s.Contains('*')).Sum(s=>s=="Auto" ? 32 : Numbers.Parse(s));
            var stars=values.Where(s=>s.Contains('*')).Sum(s=>s=="*" ? 1 : Numbers.Parse(s.TrimEnd('*'),1));
            return values.Select(s=>s.Contains('*') ? Math.Max(0,total-fixedTotal)*(s=="*" ? 1 : Numbers.Parse(s.TrimEnd('*'),1))/Math.Max(1,stars) : s=="Auto" ? 32 : Numbers.Parse(s)).ToArray();
        }
        catch (System.Xml.XmlException) { return [total]; }
    }
}
