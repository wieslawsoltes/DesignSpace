using System.Globalization;
using System.Xml.Linq;
using System.Xml;
namespace DesignSpace.Core;

public enum DesignEffectKind { Blur, DropShadow }
public enum DesignBlurKernel { Gaussian, Box }

/// <summary>Inert effect settings. No executable shader or assembly is loaded.</summary>
public sealed record DesignEffect
{
    public DesignEffectKind Kind { get; init; }
    public double Radius { get; init; }=5;
    public DesignBlurKernel Kernel { get; init; }
    public double Direction { get; init; }=315;
    public double ShadowDepth { get; init; }=5;
    public double Opacity { get; init; }=1;
    public string Color { get; init; }="Black";
    public string RenderingBias { get; init; }="Performance";
    public const double MaxRadius=128,MaxDepth=10000;
    public void Validate()
    {
        if(!Enum.IsDefined(Kind)||!Enum.IsDefined(Kernel))throw new InvalidDataException("Unsupported effect or blur kernel.");
        if(!double.IsFinite(Radius)||Radius<0||Radius>MaxRadius)throw new InvalidDataException("Effect radius must be between 0 and 128 design units.");
        if(!double.IsFinite(Direction)||Math.Abs(Direction)>1e9||!double.IsFinite(ShadowDepth)||ShadowDepth<0||ShadowDepth>MaxDepth)
            throw new InvalidDataException("Effect direction/depth exceeds the finite coordinate budget.");
        if(!double.IsFinite(Opacity)||Opacity<0||Opacity>1)throw new InvalidDataException("Shadow opacity must be between zero and one.");
        if(RenderingBias is not ("Performance" or "Quality"))throw new InvalidDataException("Unknown effect rendering bias.");
        if(string.IsNullOrWhiteSpace(Color)||Color.Length>256||Color.StartsWith('{'))throw new InvalidDataException("Effect color must be a resolved literal.");
        _=BrushColor.Parse(Color);
    }
    /// <summary>WPF angles advance counterclockwise while canvas y increases downwards.</summary>
    public DPoint ShadowOffset { get { var radians=(Direction%360)*Math.PI/180;return new(ShadowDepth*Math.Cos(radians),-ShadowDepth*Math.Sin(radians)); } }
}

/// <summary>Strict literal effect XML. Unknown properties remain preserved by the document codec.</summary>
public static class EffectCodec
{
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    public static DesignEffect Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if(text.Length>16384)throw new InvalidDataException("Effect XML exceeds 16 KiB.");
        XElement element;
        try{element=BrushCodec.ReadXml(text);}
        catch(XmlException e){throw new InvalidDataException("Malformed effect XML.",e);}
        if(element.Name.Namespace!=Ns||element.HasElements||element.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))
            throw new InvalidDataException("Only literal presentation-namespace effects are supported.");
        var kind=element.Name.LocalName switch { "BlurEffect"=>DesignEffectKind.Blur,"DropShadowEffect"=>DesignEffectKind.DropShadow,_=>throw new InvalidDataException("Unsupported effect: "+element.Name.LocalName) };
        string[] allowed=kind==DesignEffectKind.Blur?["Radius","KernelType","RenderingBias"]:["BlurRadius","Direction","ShadowDepth","Opacity","Color","RenderingBias"];
        foreach(var a in element.Attributes())
            if(!a.IsNamespaceDeclaration&&a.Name!=XName.Get("Key",DesignNode.XamlNamespace)&&(a.Name.NamespaceName.Length!=0||!allowed.Contains(a.Name.LocalName)))
                throw new InvalidDataException("Unsupported or named effect metadata: "+a.Name);
        double Number(string name,double fallback)
        {
            var raw=(string?)element.Attribute(name);if(raw is null)return fallback;
            return double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:throw new InvalidDataException("Invalid or unresolved effect "+name+".");
        }
        var kernel=(string?)element.Attribute("KernelType")??"Gaussian";
        if(!Enum.GetNames<DesignBlurKernel>().Contains(kernel))throw new InvalidDataException("Unknown blur kernel.");
        var result=new DesignEffect
        {
            Kind=kind,Radius=Number(kind==DesignEffectKind.Blur?"Radius":"BlurRadius",5),Kernel=Enum.Parse<DesignBlurKernel>(kernel),
            Direction=Number("Direction",315),ShadowDepth=Number("ShadowDepth",5),Opacity=Number("Opacity",1),
            Color=(string?)element.Attribute("Color")??"Black",RenderingBias=(string?)element.Attribute("RenderingBias")??"Performance"
        };
        result.Validate();return result;
    }
    public static string Write(DesignEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);effect.Validate();
        string N(double n)=>n.ToString("R",CultureInfo.InvariantCulture);
        var element=new XElement(Ns+(effect.Kind==DesignEffectKind.Blur?"BlurEffect":"DropShadowEffect"));
        element.SetAttributeValue(effect.Kind==DesignEffectKind.Blur?"Radius":"BlurRadius",N(effect.Radius));
        element.SetAttributeValue("RenderingBias",effect.RenderingBias);
        if(effect.Kind==DesignEffectKind.Blur)element.SetAttributeValue("KernelType",effect.Kernel);
        else
        {
            element.SetAttributeValue("Direction",N(effect.Direction));element.SetAttributeValue("ShadowDepth",N(effect.ShadowDepth));
            element.SetAttributeValue("Opacity",N(effect.Opacity));element.SetAttributeValue("Color",effect.Color);
        }
        return element.ToString(SaveOptions.DisableFormatting);
    }
}
