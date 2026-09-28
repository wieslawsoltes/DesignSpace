using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
namespace DesignSpace.Core;

public enum DesignLineCap { Flat, Square, Round, Triangle }
public enum DesignLineJoin { Miter, Bevel, Round }
public readonly record struct StrokeDashSpan(double Start,double End);

/// <summary>Portable XAML stroke semantics. Dash lengths and offsets are multiples of thickness.</summary>
public sealed record StrokeStyle(double Thickness=1,DesignLineCap StartCap=DesignLineCap.Flat,
    DesignLineCap EndCap=DesignLineCap.Flat,DesignLineCap DashCap=DesignLineCap.Flat,
    DesignLineJoin LineJoin=DesignLineJoin.Miter,double MiterLimit=10,string DashArray="",double DashOffset=0)
{
    public const int MaxDashValues=128,MaxDashSpans=8192;
    public const double MaxThickness=1_000_000;
    public static readonly string[] Properties=["StrokeThickness","StrokeStartLineCap","StrokeEndLineCap","StrokeDashCap","StrokeLineJoin","StrokeMiterLimit","StrokeDashArray","StrokeDashOffset"];
    private sealed record Pattern(ImmutableArray<double> Values);
    private static readonly ConditionalWeakTable<DesignNode,StrokeStyle> Nodes=new();
    private static readonly ConditionalWeakTable<StrokeStyle,Pattern> Patterns=new();
    public ImmutableArray<double> Dashes=>Patterns.GetValue(this,s=>new(ParseDashes(s.DashArray))).Values;
    public static StrokeStyle Read(DesignNode node)=>Nodes.GetValue(node,Parse);
    public static bool HasBrush(DesignNode node,string property)
    {
        var raw=node.PropertyElements.FirstOrDefault(xml=>XElement.Parse(xml).Name.LocalName.EndsWith("."+property,StringComparison.Ordinal));
        if(raw is not null)
        {
            var child=XElement.Parse(raw).Elements().FirstOrDefault();
            return child is not null && child.Name!=XName.Get("Null",DesignNode.XamlNamespace);
        }
        var value=node.Get(property).Trim();
        return value.Length>0 && value!="{x:Null}";
    }
    private static StrokeStyle Parse(DesignNode n)
    {
        foreach(var raw in n.PropertyElements)
            if(Properties.Any(key=>XElement.Parse(raw).Name.LocalName.EndsWith("."+key,StringComparison.Ordinal)))
                throw new InvalidDataException("Stroke settings currently require literal attributes; property markup is preserved.");
        double Number(string key,double fallback)
        {
            var value=n.Get(key); if(value.Length==0)return fallback;
            if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var parsed)||!double.IsFinite(parsed))throw new InvalidDataException("Unresolved or invalid "+key+".");
            return parsed;
        }
        T Choice<T>(string key,T fallback) where T:struct,Enum
        {
            var value=n.Get(key); if(value.Length==0)return fallback;
            return Enum.GetNames<T>().Contains(value) && Enum.TryParse<T>(value,out var result) ? result : throw new InvalidDataException("Invalid "+key+".");
        }
        var style=new StrokeStyle(Number("StrokeThickness",1),Choice("StrokeStartLineCap",DesignLineCap.Flat),Choice("StrokeEndLineCap",DesignLineCap.Flat),Choice("StrokeDashCap",DesignLineCap.Flat),Choice("StrokeLineJoin",DesignLineJoin.Miter),Number("StrokeMiterLimit",10),n.Get("StrokeDashArray"),Number("StrokeDashOffset",0));
        style.Validate();return style;
    }
    /// <summary>Validate literal attributes without executing or discarding preserved resource/binding values.</summary>
    public static void ValidateLiterals(DesignNode node)
    {
        if(!VectorGeometry.IsShape(node))return;
        // Remove unresolved values only from this validation view. Preview reports them rather than guessing.
        if(!Properties.Any(k=>node.Properties.ContainsKey(k)))return;
        var unresolved=Properties.Where(k=>node.Get(k).StartsWith('{')).ToArray();
        var n=unresolved.Length==0 && node.PropertyElements.IsEmpty ? node : node with { Properties=node.Properties.RemoveRange(unresolved),PropertyElements=[] };
        _=Read(n);
    }
    public void Validate()
    {
        if(!double.IsFinite(Thickness)||Thickness<0||Thickness>MaxThickness)throw new InvalidDataException("Stroke thickness must be between 0 and 1,000,000.");
        if(!double.IsFinite(MiterLimit)||MiterLimit<1||MiterLimit>MaxThickness)throw new InvalidDataException("Miter limit must be between 1 and 1,000,000.");
        if(!double.IsFinite(DashOffset)||Math.Abs(DashOffset)>VectorPathCodec.MaxCoordinate)throw new InvalidDataException("Dash offset exceeds the finite coordinate budget.");
        if(!Enum.IsDefined(StartCap)||!Enum.IsDefined(EndCap)||!Enum.IsDefined(DashCap)||!Enum.IsDefined(LineJoin))throw new InvalidDataException("Invalid cap or join.");
        _=Dashes;
    }
    private static ImmutableArray<double> ParseDashes(string text)
    {
        if(text is null || text.Length>4096)throw new InvalidDataException("Dash array exceeds its text limit.");
        if(string.IsNullOrWhiteSpace(text))return [];
        var tokens=text.Split([',',' ','\t','\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        if(tokens.Length>MaxDashValues)throw new InvalidDataException("Dash arrays are limited to 128 values.");
        var values=ImmutableArray.CreateBuilder<double>();
        foreach(var token in tokens)
        {
            if(!double.TryParse(token,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||Math.Abs(value)>VectorPathCodec.MaxCoordinate)throw new InvalidDataException("Dash values must be finite numbers within the coordinate budget.");
            values.Add(Math.Abs(value)); // WPF interprets negative lengths by magnitude.
        }
        if(values.All(v=>v==0))throw new InvalidDataException("An all-zero dash pattern has no finite period; use an empty array for Solid.");
        if(values.Count%2!=0)values.AddRange(values.ToArray());
        return values.ToImmutable();
    }
    /// <summary>Returns bounded painted intervals, including zero-length dots. Each contour starts at the same phase.</summary>
    public ImmutableArray<StrokeDashSpan> GetDashes(double length)
    {
        Validate();if(!double.IsFinite(length)||length<0||length>VectorPathCodec.MaxCoordinate*16)throw new InvalidDataException("Invalid contour length.");
        if(Thickness==0)return [];
        var values=Dashes;if(values.IsEmpty)return [new(0,length)];
        var periodUnits=values.Sum();var period=periodUnits*Thickness;
        if(!double.IsFinite(period)||period<=0)throw new InvalidDataException("The dash period is not representable.");
        var phase=((DashOffset%periodUnits)+periodUnits)%periodUnits*Thickness;
        var cycles=Math.Ceiling((length+phase)/period)+1;
        if(cycles*values.Length>MaxDashSpans*2)throw new InvalidDataException("This pattern exceeds the 8,192 dash-fragment budget.");
        var result=ImmutableArray.CreateBuilder<StrokeDashSpan>();
        for(var cycle=0;cycle<(int)cycles;cycle++)
        {
            var cursor=cycle*period-phase;
            for(var i=0;i<values.Length;i++)
            {
                var end=cursor+values[i]*Thickness;
                // A clipped nonzero dash touching an endpoint is not a dot.
                // Only genuinely zero-length dash entries receive dot caps.
                if(i%2==0&&(values[i]==0 ? cursor>=0&&cursor<=length : end>0&&cursor<length))
                {
                    var span=new StrokeDashSpan(Math.Max(0,cursor),Math.Min(length,end));
                    if(result.Count>0&&result[^1].End==span.Start)result[^1]=result[^1] with { End=span.End };
                    else result.Add(span);
                }
                cursor=end;
            }
        }
        if(result.Count>MaxDashSpans)throw new InvalidDataException("Dash-fragment budget exceeded.");
        return result.ToImmutable();
    }
}
