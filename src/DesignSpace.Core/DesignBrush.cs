using System.Collections.Immutable;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
namespace DesignSpace.Core;

public enum DesignBrushKind { Solid, Linear, Radial }
public enum DesignBrushMapping { RelativeToBoundingBox, Absolute }
public enum DesignGradientSpread { Pad, Reflect, Repeat }
public sealed record DesignGradientStop(double Offset,string Color);

/// <summary>Immutable, non-executing brush data. Rendering and authoring share this contract.</summary>
public sealed record DesignBrush
{
    public DesignBrushKind Kind { get; init; }=DesignBrushKind.Linear;
    public string Color { get; init; }="#FF0078D4";
    public double Opacity { get; init; }=1;
    public DesignBrushMapping Mapping { get; init; }=DesignBrushMapping.RelativeToBoundingBox;
    public DesignGradientSpread Spread { get; init; }
    public DPoint Start { get; init; }=new(0,0);
    public DPoint End { get; init; }=new(1,1);
    public DPoint Center { get; init; }=new(.5,.5);
    public DPoint Origin { get; init; }=new(.5,.5);
    public double RadiusX { get; init; }=.5;
    public double RadiusY { get; init; }=.5;
    public DMatrix Transform { get; init; }=DMatrix.Identity;
    public DMatrix RelativeTransform { get; init; }=DMatrix.Identity;
    public ImmutableArray<DesignGradientStop> Stops { get; init; }=[new(0,"#FF0078D4"),new(1,"#FFFFFFFF")];
    public void Validate()
    {
        if(!Enum.IsDefined(Kind)||!Enum.IsDefined(Mapping)||!Enum.IsDefined(Spread))throw new InvalidDataException("Unknown brush kind, mapping or spread.");
        if(!double.IsFinite(Opacity)||Opacity<0||Opacity>1)throw new InvalidDataException("Brush opacity must be between zero and one.");
        if(Stops.IsDefault||Stops.Length>BrushCodec.MaxStops)throw new InvalidDataException("A gradient supports at most 128 stops.");
        Check(Color);
        foreach(var stop in Stops){if(stop is null)throw new InvalidDataException("Null gradient stop.");Finite(stop.Offset);Check(stop.Color);}
        foreach(var p in new[]{Start,End,Center,Origin}){Finite(p.X);Finite(p.Y);}
        Finite(RadiusX);Finite(RadiusY);if(RadiusX<0||RadiusY<0)throw new InvalidDataException("Gradient radii cannot be negative.");
        CheckMatrix(Transform);CheckMatrix(RelativeTransform);
    }
    private static void Check(string value){if(string.IsNullOrWhiteSpace(value)||value.Length>256||value.StartsWith('{'))throw new InvalidDataException("A brush color must be a literal color, not an unresolved expression.");_=BrushColor.Parse(value);}
    internal static void Finite(double value){if(!double.IsFinite(value)||Math.Abs(value)>1e9)throw new InvalidDataException("Brush coordinates exceed the finite coordinate budget.");}
    internal static void CheckMatrix(DMatrix m){foreach(var n in new[]{m.M11,m.M12,m.M21,m.M22,m.DX,m.DY})Finite(n);}
    /// <summary>Relative transform operates in a unit bounding box, then absolute Transform is applied.</summary>
    public DMatrix MappingTo(DRect bounds)
    {
        var box=DMatrix.Translate(bounds.X,bounds.Y)*DMatrix.Scale(bounds.Width,bounds.Height);
        if(!box.TryInvert(out var inverse))return DMatrix.Scale(0,0);
        var absolute=DMatrix.Translate(bounds.X,bounds.Y)*Transform*DMatrix.Translate(-bounds.X,-bounds.Y);
        return absolute*box*RelativeTransform*inverse*(Mapping==DesignBrushMapping.Absolute ? DMatrix.Translate(bounds.X,bounds.Y) : box);
    }
}

/// <summary>Strict brush XML codec. Unsupported metadata is rejected, never silently stripped by authoring.</summary>
public static class BrushCodec
{
    public const int MaxCharacters=65536,MaxStops=128;
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    public static XElement ReadXml(string text)
    {
        if(text.Length>MaxCharacters)throw new InvalidDataException("Brush XML exceeds 64 KiB.");
        using var reader=XmlReader.Create(new StringReader(text),new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaxCharacters});
        while(reader.Read())if(reader.Depth>32)throw new InvalidDataException("Brush XML nesting exceeds 32 levels.");
        using var safe=XmlReader.Create(new StringReader(text),new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaxCharacters});
        return XElement.Load(safe);
    }
    public static DesignBrush Parse(string text)
    {
        if(!text.TrimStart().StartsWith('<')){var solid=new DesignBrush{Kind=DesignBrushKind.Solid,Color=text.Trim()};solid.Validate();return solid;}
        return Parse(ReadXml(text));
    }
    public static DesignBrush Parse(XElement e)
    {
        var kind=e.Name.LocalName switch{"SolidColorBrush"=>DesignBrushKind.Solid,"LinearGradientBrush"=>DesignBrushKind.Linear,"RadialGradientBrush"=>DesignBrushKind.Radial,_=>throw new InvalidDataException("Unsupported brush type: "+e.Name.LocalName)};
        Attributes(e,kind switch {DesignBrushKind.Solid=>["Color","Opacity","Transform","RelativeTransform"],DesignBrushKind.Linear=>["Opacity","StartPoint","EndPoint","MappingMode","SpreadMethod","Transform","RelativeTransform","ColorInterpolationMode"],_=>["Opacity","Center","GradientOrigin","RadiusX","RadiusY","MappingMode","SpreadMethod","Transform","RelativeTransform","ColorInterpolationMode"]});
        if(e.Attribute("ColorInterpolationMode") is { } interpolation && interpolation.Value!="SRgbLinearInterpolation")throw new InvalidDataException("Only SRgbLinearInterpolation is supported; other interpolation modes remain preserved XML.");
        var stops=ImmutableArray.CreateBuilder<DesignGradientStop>();var transform=DMatrix.Identity;var relative=DMatrix.Identity;var seen=new HashSet<string>();
        foreach(var child in e.Elements())
        {
            if(child.Name.LocalName=="GradientStop"&&kind!=DesignBrushKind.Solid)AddStop(child);
            else if(child.Name.LocalName==e.Name.LocalName+".GradientStops"&&kind!=DesignBrushKind.Solid)
            {if(!seen.Add("Stops"))throw new InvalidDataException("Duplicate gradient-stop collection.");Attributes(child,[]);foreach(var stop in child.Elements())AddStop(stop);}
            else if(child.Name.LocalName==e.Name.LocalName+".Transform"||child.Name.LocalName==e.Name.LocalName+".RelativeTransform")
            {
                var key=child.Name.LocalName.Split('.').Last();if(!seen.Add(key)||e.Attribute(key) is not null)throw new InvalidDataException("Duplicate brush transform.");
                Attributes(child,[]);var children=child.Elements().ToArray();if(children.Length!=1)throw new InvalidDataException("A brush transform requires one transform object.");
                var matrix=ParseTransform(children[0],0);if(key=="Transform")transform=matrix;else relative=matrix;
            }
            else throw new InvalidDataException("Unsupported brush property: "+child.Name.LocalName);
        }
        if(e.Attribute("Transform") is { } t)transform=Matrix(t.Value);
        if(e.Attribute("RelativeTransform") is { } rt)relative=Matrix(rt.Value);
        var result=new DesignBrush{Kind=kind,Color=(string?)e.Attribute("Color")??"Transparent",Opacity=Number(e,"Opacity",1),
            Start=Point(e,"StartPoint",new(0,0)),End=Point(e,"EndPoint",new(1,1)),Center=Point(e,"Center",new(.5,.5)),Origin=Point(e,"GradientOrigin",new(.5,.5)),
            RadiusX=Number(e,"RadiusX",.5),RadiusY=Number(e,"RadiusY",.5),Mapping=Choice(e,"MappingMode",DesignBrushMapping.RelativeToBoundingBox),Spread=Choice(e,"SpreadMethod",DesignGradientSpread.Pad),Transform=transform,RelativeTransform=relative,Stops=stops.ToImmutable()};
        result.Validate();return result;
        void AddStop(XElement stop)
        {
            if(stop.Name.LocalName!="GradientStop"||stop.HasElements||stops.Count>=MaxStops)throw new InvalidDataException("Invalid gradient stop or stop budget exceeded.");
            Attributes(stop,["Offset","Color"]);stops.Add(new(Number(stop,"Offset",0),(string?)stop.Attribute("Color")??"Transparent"));
        }
    }
    private static T Choice<T>(XElement e,string property,T fallback) where T:struct,Enum
    {
        var text=(string?)e.Attribute(property);if(text is null)return fallback;
        return Enum.GetNames<T>().Contains(text)&&Enum.TryParse<T>(text,out var value)?value:throw new InvalidDataException("Invalid "+property+".");
    }
    private static void Attributes(XElement e,string[] allowed)
    {
        if(e.Name.NamespaceName is not ("" or DesignNode.PresentationNamespace))throw new InvalidDataException("Custom brush namespaces are not executed.");
        foreach(var a in e.Attributes())if(!a.IsNamespaceDeclaration&&a.Name!=XName.Get("Key",DesignNode.XamlNamespace)&&a.Name!=XName.Get("Name",DesignNode.XamlNamespace)&&(a.Name.NamespaceName.Length>0||!allowed.Contains(a.Name.LocalName)))throw new InvalidDataException("Unsupported brush attribute: "+a.Name);
        if(e.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))throw new InvalidDataException("Unexpected brush text.");
    }
    private static double Number(XElement e,string name,double fallback)=>(string?)e.Attribute(name) is { } value?ReadNumber(value):fallback;
    public static double ReadNumber(string text)
    {
        if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n))throw new InvalidDataException("Expected a finite numeric brush value.");DesignBrush.Finite(n);return n;
    }
    public static DPoint ReadPoint(string text)
    {
        var p=text.Split([',',' '],StringSplitOptions.RemoveEmptyEntries);if(p.Length!=2)throw new InvalidDataException("Enter a point as x,y.");return new(ReadNumber(p[0]),ReadNumber(p[1]));
    }
    private static DPoint Point(XElement e,string name,DPoint fallback)=>(string?)e.Attribute(name) is { } text?ReadPoint(text):fallback;
    public static DMatrix Matrix(string text)
    {
        if(text=="Identity")return DMatrix.Identity;var p=text.Split([',',' '],StringSplitOptions.RemoveEmptyEntries);if(p.Length!=6)throw new InvalidDataException("A matrix needs six finite values.");var n=p.Select(ReadNumber).ToArray();return new(n[0],n[1],n[2],n[3],n[4],n[5]);
    }
    private static DMatrix ParseTransform(XElement e,int depth)
    {
        if(depth>16)throw new InvalidDataException("Transform nesting limit exceeded.");
        double N(string name,double fallback=0)=>Number(e,name,fallback);
        var result=DMatrix.Identity;var center=DMatrix.Translate(N("CenterX"),N("CenterY"));var uncenter=DMatrix.Translate(-N("CenterX"),-N("CenterY"));
        switch(e.Name.LocalName)
        {
            case "MatrixTransform":Attributes(e,["Matrix"]);result=Matrix((string?)e.Attribute("Matrix")??"Identity");break;
            case "TranslateTransform":Attributes(e,["X","Y"]);result=DMatrix.Translate(N("X"),N("Y"));break;
            case "RotateTransform":Attributes(e,["Angle","CenterX","CenterY"]);result=DMatrix.Rotate(N("Angle"),N("CenterX"),N("CenterY"));break;
            case "ScaleTransform":Attributes(e,["ScaleX","ScaleY","CenterX","CenterY"]);result=center*DMatrix.Scale(N("ScaleX",1),N("ScaleY",1))*uncenter;break;
            case "SkewTransform":Attributes(e,["AngleX","AngleY","CenterX","CenterY"]);result=center*new DMatrix(1,Math.Tan(N("AngleY")*Math.PI/180),Math.Tan(N("AngleX")*Math.PI/180),1,0,0)*uncenter;break;
            case "TransformGroup":
                Attributes(e,[]);foreach(var child in e.Elements())
                {
                    if(child.Name.LocalName=="TransformGroup.Children"){Attributes(child,[]);foreach(var item in child.Elements())result=ParseTransform(item,depth+1)*result;}
                    else result=ParseTransform(child,depth+1)*result;
                }
                break;
            default:throw new InvalidDataException("Unsupported brush transform: "+e.Name.LocalName);
        }
        if(e.Name.LocalName!="TransformGroup"&&e.HasElements)throw new InvalidDataException("Unexpected nested transform properties.");DesignBrush.CheckMatrix(result);return result;
    }
    public static string Write(DesignBrush brush)
    {
        brush.Validate();var name=brush.Kind switch{DesignBrushKind.Solid=>"SolidColorBrush",DesignBrushKind.Linear=>"LinearGradientBrush",_=>"RadialGradientBrush"};var e=new XElement(Ns+name);
        string N(double n)=>n.ToString("R",CultureInfo.InvariantCulture);string P(DPoint p)=>N(p.X)+","+N(p.Y);
        e.SetAttributeValue("Opacity",N(brush.Opacity));
        if(brush.Kind==DesignBrushKind.Solid)e.SetAttributeValue("Color",brush.Color);
        else
        {
            e.SetAttributeValue("MappingMode",brush.Mapping);e.SetAttributeValue("SpreadMethod",brush.Spread);
            if(brush.Kind==DesignBrushKind.Linear){e.SetAttributeValue("StartPoint",P(brush.Start));e.SetAttributeValue("EndPoint",P(brush.End));}
            else{e.SetAttributeValue("Center",P(brush.Center));e.SetAttributeValue("GradientOrigin",P(brush.Origin));e.SetAttributeValue("RadiusX",N(brush.RadiusX));e.SetAttributeValue("RadiusY",N(brush.RadiusY));}
            foreach(var stop in brush.Stops)e.Add(new XElement(Ns+"GradientStop",new XAttribute("Offset",N(stop.Offset)),new XAttribute("Color",stop.Color)));
        }
        void Transform(string key,DMatrix m){if(m!=DMatrix.Identity)e.Add(new XElement(Ns+(name+"."+key),new XElement(Ns+"MatrixTransform",new XAttribute("Matrix",string.Join(",",new[]{m.M11,m.M12,m.M21,m.M22,m.DX,m.DY}.Select(N))))));}
        Transform("Transform",brush.Transform);Transform("RelativeTransform",brush.RelativeTransform);return e.ToString(SaveOptions.DisableFormatting);
    }
}
