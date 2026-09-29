using System.Globalization;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

/// <summary>Strict, non-executing built-in easing codec. Unsupported markup is left to the caller to preserve.</summary>
public static class EasingCurveCodec
{
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    public static bool TryRead(XElement element,out EasingCurve? curve)
    {
        curve=null;
        if(element.Name.Namespace!=Ns||!element.Name.LocalName.EndsWith("Ease",StringComparison.Ordinal)||element.HasElements||
           element.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))return false;
        var name=element.Name.LocalName[..^4];
        if(!Enum.GetNames<EasingFamily>().Contains(name)||!Enum.TryParse<EasingFamily>(name,out var family))return false;
        var allowed=family switch
        {
            EasingFamily.Back=>new[]{"Amplitude"},EasingFamily.Bounce=>["Bounces","Bounciness"],
            EasingFamily.Elastic=>["Oscillations","Springiness"],EasingFamily.Exponential=>["Exponent"],EasingFamily.Power=>["Power"],_=>[]
        };
        if(element.Attributes().Any(a=>!a.IsNamespaceDeclaration&&a.Name!="EasingMode"&&!allowed.Contains(a.Name.ToString())))return false;
        var modeText=(string?)element.Attribute("EasingMode")??"EaseOut";
        if(!Enum.GetNames<EasingDirection>().Contains(modeText)||!Enum.TryParse<EasingDirection>(modeText,out var mode))return false;
        double Number(string key,double fallback)=>element.Attribute(key) is { } a?double.Parse(a.Value,NumberStyles.Float,CultureInfo.InvariantCulture):fallback;
        int Count(string key,int fallback)=>element.Attribute(key) is { } a?int.Parse(a.Value,NumberStyles.Integer,CultureInfo.InvariantCulture):fallback;
        try
        {
            var candidate=new EasingCurve(family,mode,Number("Amplitude",1),Count("Bounces",3),Number("Bounciness",2),Count("Oscillations",3),Number("Springiness",3),Number("Exponent",2),Number("Power",2));
            candidate.Validate();curve=candidate;return true;
        }
        catch(Exception e)when(e is FormatException or OverflowException or InvalidDataException){return false;}
    }
    public static XElement Write(EasingCurve curve)
    {
        curve.Validate();var element=new XElement(Ns+(curve.Family+"Ease"),new XAttribute("EasingMode",curve.Mode));
        void Number(string name,double value)=>element.SetAttributeValue(name,value.ToString("R",CultureInfo.InvariantCulture));
        switch(curve.Family)
        {
            case EasingFamily.Back:Number("Amplitude",curve.Amplitude);break;
            case EasingFamily.Bounce:element.SetAttributeValue("Bounces",curve.Bounces);Number("Bounciness",curve.Bounciness);break;
            case EasingFamily.Elastic:element.SetAttributeValue("Oscillations",curve.Oscillations);Number("Springiness",curve.Springiness);break;
            case EasingFamily.Exponential:Number("Exponent",curve.Exponent);break;
            case EasingFamily.Power:Number("Power",curve.Power);break;
        }
        return element;
    }
}
