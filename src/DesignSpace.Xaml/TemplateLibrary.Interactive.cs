using System.Xml.Linq;
namespace DesignSpace.Xaml;

public static partial class TemplateLibrary
{
    /// <summary>Editable example with named-part hover/press/focus/disabled triggers.</summary>
    public static string CreateInteractiveDefault(string key="InteractiveButtonTemplate")
    {
        var template=XElement.Parse(CreateDefault(key));var border=template.Elements().Single();
        border.SetAttributeValue(X+"Name","Chrome");border.SetAttributeValue("BorderBrush","Transparent");border.SetAttributeValue("BorderThickness","2");
        XElement Rule(string property,string value,string targetProperty,string setting)=>new(Ns+"Trigger",new XAttribute("Property",property),new XAttribute("Value",value),
            new XElement(Ns+"Setter",new XAttribute("TargetName","Chrome"),new XAttribute("Property",targetProperty),new XAttribute("Value",setting)));
        template.Add(new XElement(Ns+"ControlTemplate.Triggers",
            Rule("IsMouseOver","True","Background","#FF236CBD"),
            Rule("IsPressed","True","Background","#FF134475"),
            Rule("IsKeyboardFocused","True","BorderBrush","#FF8CD3FF"),
            Rule("IsEnabled","False","Opacity","0.45")));
        return template.ToString();
    }
}
