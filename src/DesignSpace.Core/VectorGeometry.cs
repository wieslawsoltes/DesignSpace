using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
namespace DesignSpace.Core;

/// <summary>Shared geometry adapters. Unsupported object markup is rejected rather than silently flattened away.</summary>
public static class VectorGeometry
{
    private sealed record Cached(VectorPath Path);
    private static readonly ConditionalWeakTable<DesignNode,Cached> Cache=new();
    private static readonly ConditionalWeakTable<string,Cached> Strings=new();
    public static VectorPath ParseCached(string data)=>Strings.GetValue(data,s=>new(VectorPathCodec.Parse(s))).Path;
    public static bool IsShape(DesignNode node)=>node.Namespace==DesignNode.PresentationNamespace && node.Type is "Path" or "Rectangle" or "Ellipse" or "Line" or "Polygon" or "Polyline";
    public static VectorPath ReadPath(DesignNode node)=>Cache.GetValue(node,n=>new(Read(n))).Path;
    public static VectorPath? ReadClip(DesignNode node)
    {
        var raw=node.PropertyElements.FirstOrDefault(p=>XElement.Parse(p).Name.LocalName.EndsWith(".Clip",StringComparison.Ordinal));
        if(raw is not null)return Strings.GetValue(raw,s=>
        {
            var elements=XElement.Parse(s).Elements().ToArray();
            if(elements.Length!=1)throw new InvalidDataException("Clip needs exactly one geometry.");
            return new(ReadElement(elements[0]));
        }).Path;
        var value=node.Get("Clip");return value.Length==0 ? null : ParseCached(value);
    }
    private static VectorPath Read(DesignNode node)
    {
        var raw=node.PropertyElements.FirstOrDefault(p=>XElement.Parse(p).Name.LocalName==node.Type+".Data");
        if(raw is null)return ParseCached(node.Get("Data"));
        var holder=XElement.Parse(raw);var elements=holder.Elements().ToArray();
        if(elements.Length!=1)throw new InvalidDataException("Path.Data needs one geometry.");
        return ReadElement(elements[0]);
    }
    private static void Attributes(XElement e,params string[] allowed)
    {
        if(e.Name.NamespaceName!=DesignNode.PresentationNamespace||e.Attributes().Any(a=>!a.IsNamespaceDeclaration&&(a.Name.NamespaceName.Length>0||!allowed.Contains(a.Name.LocalName))))throw new InvalidDataException("Unsupported geometry metadata on "+e.Name.LocalName);
    }
    private static DPoint[] CoordinatePairs(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return [];
        if(text.Any(c=>char.IsLetter(c)&&c is not ('e' or 'E')))throw new InvalidDataException("A coordinate list cannot contain path commands.");
        var path=VectorPathCodec.Parse("M "+text);
        if(path.Figures.Length!=1||path.Figures[0].Closed||path.Figures[0].Segments.Any(s=>s.Kind!=VectorSegmentKind.Line))throw new InvalidDataException("A list of coordinate pairs was expected.");
        return new[]{path.Figures[0].Start}.Concat(path.Figures[0].Segments.Select(s=>s.End)).ToArray();
    }
    private static DPoint Point(string? text)
    {
        var points=CoordinatePairs(text ?? "0,0");
        if(points.Length!=1)throw new InvalidDataException("Exactly one coordinate pair was expected.");
        return points[0];
    }
    private static bool Bool(XElement e,string name,bool fallback=false)
    {
        var s=(string?)e.Attribute(name);if(s is null)return fallback;
        return bool.TryParse(s,out var b) ? b : throw new InvalidDataException("Invalid "+name);
    }
    private static IEnumerable<XElement> Children(XElement e,string wrapper)
    {
        foreach(var child in e.Elements())
        {
            if(child.Name.LocalName==wrapper){Attributes(child);foreach(var item in child.Elements())yield return item;}
            else yield return child;
        }
    }
    public static VectorPath ReadElement(XElement e)
    {
        if(e.Name.NamespaceName!=DesignNode.PresentationNamespace)throw new InvalidDataException("Custom geometry namespaces are not executable.");
        if(e.Name.LocalName=="StreamGeometry") { Attributes(e);if(e.HasElements)throw new InvalidDataException("StreamGeometry must contain path text only.");return VectorPathCodec.Parse(e.Value); }
        if(e.Name.LocalName=="RectangleGeometry")
        {
            Attributes(e,"Rect","RadiusX","RadiusY");
            if(e.HasElements)throw new InvalidDataException("Nested rectangle geometry properties are not yet editable.");
            var rect=CoordinatePairs((string?)e.Attribute("Rect") ?? "0,0,0,0");
            if(rect.Length!=2)throw new InvalidDataException("RectangleGeometry.Rect needs four numbers.");
            var size=rect[1];if(size.X<0||size.Y<0)throw new InvalidDataException("Rectangle sizes cannot be negative.");
            var node=new DesignNode{Type="Rectangle"}.Set("RadiusX",(string?)e.Attribute("RadiusX") ?? "0").Set("RadiusY",(string?)e.Attribute("RadiusY") ?? "0");
            return VectorMath.Transform(Local(node,new(size.X,size.Y)),DMatrix.Translate(rect[0].X,rect[0].Y));
        }
        if(e.Name.LocalName=="EllipseGeometry")
        {
            Attributes(e,"Center","RadiusX","RadiusY");if(e.HasElements)throw new InvalidDataException("Nested ellipse geometry properties are not yet editable.");var center=Point((string?)e.Attribute("Center"));
            var rx=Numbers.Parse((string?)e.Attribute("RadiusX"),double.NaN);var ry=Numbers.Parse((string?)e.Attribute("RadiusY"),double.NaN);
            if(!double.IsFinite(rx)||!double.IsFinite(ry)||rx<0||ry<0)throw new InvalidDataException("Invalid ellipse radii.");
            return VectorMath.Transform(Local(new DesignNode{Type="Ellipse"},new(rx*2,ry*2)),DMatrix.Translate(center.X-rx,center.Y-ry));
        }
        if(e.Name.LocalName!="PathGeometry")throw new InvalidDataException("Only finite PathGeometry/StreamGeometry objects can be edited as paths.");
        Attributes(e,"Figures","FillRule");var fill=(string?)e.Attribute("FillRule") ?? "EvenOdd";
        if(fill is not ("EvenOdd" or "Nonzero"))throw new InvalidDataException("Unsupported path fill rule.");
        if(e.Attribute("Figures") is { } figures)
        {
            if(e.HasElements)throw new InvalidDataException("Figures are specified twice.");return VectorPathCodec.Parse(figures.Value) with { NonZero=fill=="Nonzero" };
        }
        var result=ImmutableArray.CreateBuilder<VectorFigure>();
        foreach(var figure in Children(e,"PathGeometry.Figures"))
        {
            if(figure.Name.LocalName!="PathFigure")throw new InvalidDataException("Unsupported geometry figure.");
            Attributes(figure,"StartPoint","IsClosed","IsFilled");if(!Bool(figure,"IsFilled",true))throw new InvalidDataException("Unfilled figures are preserved, but not editable in this path adapter.");
            var segments=ImmutableArray.CreateBuilder<VectorSegment>();
            foreach(var s in Children(figure,"PathFigure.Segments"))
            {
                var allowed=s.Name.LocalName switch
                {
                    "LineSegment"=>new[]{"Point"},"BezierSegment"=>new[]{"Point1","Point2","Point3"},"QuadraticBezierSegment"=>new[]{"Point1","Point2"},
                    "ArcSegment"=>new[]{"Point","Size","RotationAngle","IsLargeArc","SweepDirection"},
                    "PolyLineSegment" or "PolyBezierSegment" or "PolyQuadraticBezierSegment"=>new[]{"Points"},
                    _=>throw new InvalidDataException("Unsupported path segment: "+s.Name.LocalName)
                };
                Attributes(s,allowed.Concat(new[]{"IsStroked","IsSmoothJoin"}).ToArray());
                if(!Bool(s,"IsStroked",true)||Bool(s,"IsSmoothJoin"))throw new InvalidDataException("Per-segment stroke/join metadata is not yet editable.");
                if(s.HasElements)throw new InvalidDataException("Nested segment property markup is not editable.");
                DPoint P(string key)=>Point((string?)s.Attribute(key));
                switch(s.Name.LocalName)
                {
                    case "LineSegment":segments.Add(VectorSegment.Line(P("Point")));break;
                    case "BezierSegment":segments.Add(VectorSegment.Cubic(P("Point1"),P("Point2"),P("Point3")));break;
                    case "QuadraticBezierSegment":segments.Add(new(VectorSegmentKind.Quadratic,P("Point2")){Control1=P("Point1")});break;
                    case "ArcSegment":
                        var size=P("Size");var direction=(string?)s.Attribute("SweepDirection") ?? "Counterclockwise";
                        if(direction is not ("Clockwise" or "Counterclockwise" or "CounterClockwise"))throw new InvalidDataException("Invalid sweep direction.");
                        segments.Add(new(VectorSegmentKind.Arc,P("Point")){Radius=new(size.X,size.Y),Angle=Numbers.Parse((string?)s.Attribute("RotationAngle") ?? "0",double.NaN),LargeArc=Bool(s,"IsLargeArc"),Clockwise=direction=="Clockwise"});break;
                    case "PolyLineSegment":case "PolyBezierSegment":case "PolyQuadraticBezierSegment":
                        var array=CoordinatePairs((string?)s.Attribute("Points"));
                        var stride=s.Name.LocalName=="PolyBezierSegment" ? 3 : s.Name.LocalName=="PolyQuadraticBezierSegment" ? 2 : 1;
                        if(array.Length%stride!=0)throw new InvalidDataException("Incomplete poly segment point group.");
                        for(var i=0;i<array.Length;i+=stride)segments.Add(stride==3 ? VectorSegment.Cubic(array[i],array[i+1],array[i+2]) : stride==2 ? new(VectorSegmentKind.Quadratic,array[i+1]){Control1=array[i]} : VectorSegment.Line(array[i]));break;
                    default:throw new InvalidDataException("Unsupported path segment: "+s.Name.LocalName);
                }
            }
            result.Add(new(Point((string?)figure.Attribute("StartPoint")),segments.ToImmutableArray(),Bool(figure,"IsClosed")));
        }
        var path=new VectorPath(result.ToImmutable(),fill=="Nonzero");VectorPathCodec.Validate(path);return path;
    }
    /// <summary>Map raw path coordinates into the local layout box. Stroke width is not scaled by Stretch.</summary>
    public static DMatrix Mapping(DesignNode node,DSize size,VectorPath path)
    {
        var mode=node.Get("Stretch","None");if(mode=="None")return DMatrix.Identity;
        if(mode is not ("Fill" or "Uniform" or "UniformToFill"))throw new InvalidDataException("Invalid path Stretch value.");
        var bounds=VectorMath.Bounds(path);var stroke=Math.Max(0,node.Number("StrokeThickness",1));
        var width=Math.Max(0,size.Width-stroke);var height=Math.Max(0,size.Height-stroke);
        var sx=bounds.Width>1e-12 ? width/bounds.Width : double.PositiveInfinity;var sy=bounds.Height>1e-12 ? height/bounds.Height : double.PositiveInfinity;
        if(mode=="Uniform")sx=sy=Math.Min(sx,sy);else if(mode=="UniformToFill")sx=sy=Math.Max(double.IsFinite(sx)?sx:0,double.IsFinite(sy)?sy:0);
        if(!double.IsFinite(sx))sx=1;if(!double.IsFinite(sy))sy=1;
        var offsetX=stroke/2+(width-bounds.Width*sx)/2;var offsetY=stroke/2+(height-bounds.Height*sy)/2;
        return DMatrix.Translate(offsetX,offsetY)*DMatrix.Scale(sx,sy)*DMatrix.Translate(-bounds.X,-bounds.Y);
    }
    public static VectorPath Local(DesignNode node,DSize size)
    {
        if(node.Type=="Path") { var path=ReadPath(node);return VectorMath.Transform(path,Mapping(node,size,path)); }
        var width=Math.Max(0,size.Width);var height=Math.Max(0,size.Height);
        if(node.Type=="Line")return new([new(new(node.Number("X1"),node.Number("Y1")),[VectorSegment.Line(new(node.Number("X2",width),node.Number("Y2",height)))])]);
        if(node.Type is "Polygon" or "Polyline")
        {
            var points=CoordinatePairs(node.Get("Points"));if(points.Length==0)return VectorPath.Empty;
            return new([new(points[0],points.Skip(1).Select(VectorSegment.Line).ToImmutableArray(),node.Type=="Polygon")]);
        }
        if(node.Type=="Ellipse")
        {
            var radius=new DSize(width/2,height/2);VectorSegment Arc(DPoint end)=>new(VectorSegmentKind.Arc,end){Radius=radius,Clockwise=true};
            return new([new(new(width,height/2),[Arc(new(width/2,height)),Arc(new(0,height/2)),Arc(new(width/2,0)),Arc(new(width,height/2))],true)]);
        }
        if(node.Type=="Rectangle")
        {
            var rx=Math.Clamp(node.Number("RadiusX",node.Number("CornerRadius")),0,width/2);var ry=Math.Clamp(node.Number("RadiusY",rx),0,height/2);
            if(rx<=0||ry<=0)return new([new(new(0,0),[VectorSegment.Line(new(width,0)),VectorSegment.Line(new(width,height)),VectorSegment.Line(new(0,height))],true)]);
            VectorSegment Arc(DPoint end)=>new(VectorSegmentKind.Arc,end){Radius=new(rx,ry),Clockwise=true};
            return new([new(new(rx,0),[VectorSegment.Line(new(width-rx,0)),Arc(new(width,ry)),VectorSegment.Line(new(width,height-ry)),Arc(new(width-rx,height)),VectorSegment.Line(new(rx,height)),Arc(new(0,height-ry)),VectorSegment.Line(new(0,ry)),Arc(new(rx,0))],true)]);
        }
        throw new InvalidOperationException("Select a rectangle, ellipse, line, polygon, polyline or path.");
    }
    public static DesignNode WithPath(DesignNode node,VectorPath path)
    {
        var properties=node.Set("Data",VectorPathCodec.Write(path)).Set("Stretch","None").Properties.RemoveRange(new[]{"RadiusX","RadiusY","CornerRadius","X1","Y1","X2","Y2","Points"});
        var elements=node.PropertyElements.IsEmpty ? node.PropertyElements : node.PropertyElements.Select(raw=>XElement.Parse(raw)).Where(e=>!e.Name.LocalName.EndsWith(".Data",StringComparison.Ordinal)).Select(e=>
        {
            if(e.Name.LocalName.StartsWith(node.Type+".",StringComparison.Ordinal))e.Name=e.Name.Namespace+("Path"+e.Name.LocalName[node.Type.Length..]);
            return e.ToString(SaveOptions.DisableFormatting);
        }).ToImmutableArray();
        return node with { Type="Path",Properties=properties,PropertyElements=elements };
    }
}
