using System.Collections.Immutable;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

/// <summary>A compiled, inert property-trigger collection. Conditions are read from a stable
/// effective-value snapshot; setters are returned in declaration order. No actions or code run.</summary>
public sealed class PropertyTriggerProgram
{
    public sealed record Condition(string Property,string Value,string? SourceName);
    public sealed record Assignment(string Property,string Value,string? TargetName,bool IsObject);
    private sealed record Rule(ImmutableArray<Condition> Conditions,ImmutableArray<Assignment> Setters);
    private readonly ImmutableArray<Rule> _rules;
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    private static readonly HashSet<string> BooleanProperties=new(StringComparer.Ordinal)
        {"IsEnabled","IsMouseOver","IsPressed","IsFocused","IsKeyboardFocused","IsKeyboardFocusWithin","IsChecked","IsSelected","HasContent"};
    private static readonly HashSet<string> NumericProperties=new(StringComparer.Ordinal)
        {"Width","Height","MinWidth","MinHeight","MaxWidth","MaxHeight","Opacity","FontSize","StrokeThickness","Canvas.Left","Canvas.Top"};
    private static readonly HashSet<string> TextProperties=new(StringComparer.Ordinal)
        {"Text","Content","Tag","Visibility","HorizontalAlignment","VerticalAlignment","HorizontalContentAlignment","VerticalContentAlignment","Orientation","FontWeight","FontStyle"};
    private static readonly HashSet<string> AppearanceProperties=new(StringComparer.Ordinal)
        {"Background","Foreground","Fill","Stroke","BorderBrush","OpacityMask","Effect","Margin","Padding","BorderThickness","CornerRadius"};
    private static readonly HashSet<string> ReadOnlyProperties=new(StringComparer.Ordinal)
        {"IsMouseOver","IsPressed","IsFocused","IsKeyboardFocused","IsKeyboardFocusWithin","HasContent"};
    public int Count=>_rules.Length;
    public int ConditionCount { get; }
    public int SetterCount { get; }
    private PropertyTriggerProgram(ImmutableArray<Rule> rules)
    { _rules=rules;ConditionCount=rules.Sum(r=>r.Conditions.Length);SetterCount=rules.Sum(r=>r.Setters.Length); }
    public static PropertyTriggerProgram Empty { get; }=new([]);

    /// <summary>Compile a Style.Triggers or ControlTemplate.Triggers element. Unsupported metadata
    /// rejects the whole collection rather than partially executing it.</summary>
    public static PropertyTriggerProgram Parse(string source,bool template)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var reader=XmlReader.Create(new StringReader(source),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});
        return Compile(XElement.Load(reader),template);
    }
    internal static PropertyTriggerProgram Compile(XElement collection,bool template)
    {
        Require(collection,template?"ControlTemplate.Triggers":"Style.Triggers");Attributes(collection);
        var rules=ImmutableArray.CreateBuilder<Rule>();var total=0;
        foreach(var trigger in Children(collection))
        {
            if(rules.Count>=256)throw new InvalidDataException("Property triggers exceed 256 rules.");
            var conditions=ImmutableArray.CreateBuilder<Condition>();var setters=ImmutableArray.CreateBuilder<Assignment>();
            if(trigger.Name==Ns+"Trigger")
            {
                Attributes(trigger,"Property","Value",template?"SourceName":"");
                conditions.Add(ReadCondition(trigger,template));
            }
            else if(trigger.Name==Ns+"MultiTrigger")
            {
                Attributes(trigger);var groups=trigger.Elements(Ns+"MultiTrigger.Conditions").ToArray();
                if(groups.Length!=1)throw new InvalidDataException("A MultiTrigger requires one Conditions collection.");
                Attributes(groups[0]);
                foreach(var condition in Children(groups[0]))
                {
                    Require(condition,"Condition");Attributes(condition,"Property","Value",template?"SourceName":"");
                    if(condition.HasElements)throw new InvalidDataException("Object-valued trigger conditions are not supported.");
                    conditions.Add(ReadCondition(condition,template));
                }
                if(conditions.Count is <1 or >32)throw new InvalidDataException("A MultiTrigger requires 1–32 conditions.");
            }
            else throw new InvalidDataException("Unsupported trigger type: "+trigger.Name.LocalName+". Markup is preserved, not executed.");
            foreach(var element in Children(trigger))
            {
                if(element.Name==Ns+"MultiTrigger.Conditions"&&trigger.Name==Ns+"MultiTrigger")continue;
                if(element.Name==Ns+(trigger.Name.LocalName+".Setters"))
                { Attributes(element);foreach(var setter in Children(element))setters.Add(ReadSetter(setter,template)); }
                else setters.Add(ReadSetter(element,template));
            }
            if(setters.Count==0)throw new InvalidDataException("A property trigger must contain setters.");
            total+=conditions.Count+setters.Count;
            if(total>2048)throw new InvalidDataException("Property trigger condition/setter budget exceeded.");
            rules.Add(new(conditions.ToImmutable(),setters.ToImmutable()));
        }
        var result=new PropertyTriggerProgram(rules.ToImmutable());
        // Feedback requires a dependency-property invalidation engine. Reject these collections
        // explicitly instead of selecting an order-dependent fixed point or looping forever.
        var writes=result._rules.SelectMany(r=>r.Setters).Select(s=>(s.TargetName,s.Property)).ToHashSet();
        if(result._rules.SelectMany(r=>r.Conditions).Any(c=>writes.Contains((c.SourceName,c.Property))))
            throw new InvalidDataException("Trigger feedback is not supported: a condition property is also assigned by this collection.");
        return result;
    }
    private static Condition ReadCondition(XElement e,bool template)
    {
        var property=(string?)e.Attribute("Property")??throw new InvalidDataException("Trigger Property is required.");
        var value=(string?)e.Attribute("Value")??throw new InvalidDataException("Trigger Value is required.");
        if(!BooleanProperties.Contains(property)&&!NumericProperties.Contains(property)&&!TextProperties.Contains(property))
            throw new InvalidDataException("Unsupported trigger condition property: "+property);
        ValidateLiteral(property,value,condition:true);
        return new(property,value,Name(e,"SourceName",template));
    }
    private static Assignment ReadSetter(XElement e,bool template)
    {
        Require(e,"Setter");Attributes(e,"Property","Value",template?"TargetName":"");
        var property=(string?)e.Attribute("Property")??throw new InvalidDataException("Trigger Setter.Property is required.");
        if(ReadOnlyProperties.Contains(property)||(!BooleanProperties.Contains(property)&&!NumericProperties.Contains(property)&&!TextProperties.Contains(property)&&!AppearanceProperties.Contains(property)))
            throw new InvalidDataException("Unsupported trigger setter property: "+property);
        var name=Name(e,"TargetName",template);var literal=(string?)e.Attribute("Value");var objects=Children(e).ToArray();
        if(literal is not null)
        {
            if(objects.Length!=0)throw new InvalidDataException("A trigger setter cannot specify both Value and Setter.Value.");
            ValidateLiteral(property,literal,condition:false);return new(property,literal,name,false);
        }
        if(objects.Length!=1||objects[0].Name!=Ns+"Setter.Value")throw new InvalidDataException("A trigger setter requires Value or one Setter.Value.");
        Attributes(objects[0]);var values=Children(objects[0]).ToArray();
        if(values.Length!=1)throw new InvalidDataException("Setter.Value requires exactly one inert value object.");
        if(property is not ("Fill" or "Stroke" or "Background" or "Foreground" or "BorderBrush" or "OpacityMask" or "Effect"))
            throw new InvalidDataException("Object-valued trigger setters are supported for brushes and effects only.");
        // Existing codecs validate actual supported values when rendered. No value object executes.
        return new(property,values[0].ToString(SaveOptions.DisableFormatting),name,true);
    }
    private static void ValidateLiteral(string property,string value,bool condition)
    {
        if(value.Length>65536)throw new InvalidDataException("Trigger literal is too large.");
        if(value=="{x:Null}"&&(property=="IsChecked"||!condition&&AppearanceProperties.Contains(property)))return;
        if(!condition&&(value.StartsWith("{StaticResource ",StringComparison.Ordinal)||value.StartsWith("{ThemeResource ",StringComparison.Ordinal))&&value.EndsWith('}'))return;
        if(value.StartsWith('{'))throw new InvalidDataException("Unsupported trigger value expression.");
        if(BooleanProperties.Contains(property)&&!bool.TryParse(value,out _))throw new InvalidDataException("A Boolean trigger value is required for "+property);
        if(NumericProperties.Contains(property)&&!(value=="Auto"&&property is "Width" or "Height"))
        {
            if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number))throw new InvalidDataException("A finite trigger value is required for "+property);
            if(!condition&&(property=="Opacity"&&(number<0||number>1)||property is "Width" or "Height" or "MinWidth" or "MinHeight" or "MaxWidth" or "MaxHeight" or "FontSize" or "StrokeThickness"&&number<0))
                throw new InvalidDataException("Trigger value is outside the supported range for "+property);
        }
    }
    private static string? Name(XElement element,string attribute,bool allowed)
    {
        if(element.Attribute(attribute) is not { } name)return null;
        if(!allowed||string.IsNullOrWhiteSpace(name.Value))throw new InvalidDataException("Unsupported "+attribute+" in this trigger.");
        XmlConvert.VerifyNCName(name.Value);return name.Value;
    }
    private static void Require(XElement element,string name)
    { if(element.Name!=Ns+name)throw new InvalidDataException("Unsupported trigger element: "+element.Name); }
    private static void Attributes(XElement element,params string[] allowed)
    { if(element.Attributes().Any(a=>!a.IsNamespaceDeclaration&&(a.Name.NamespaceName.Length!=0||!allowed.Contains(a.Name.LocalName))))throw new InvalidDataException("Unsupported attributes on "+element.Name.LocalName); }
    private static IEnumerable<XElement> Children(XElement element)
    {
        if(element.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))throw new InvalidDataException("Unexpected text inside "+element.Name.LocalName);
        return element.Elements();
    }
    /// <summary>Validate template names before any matching setters are applied.</summary>
    public void ValidateNames(IReadOnlySet<string> names)
    {
        foreach(var name in _rules.SelectMany(r=>r.Conditions.Select(c=>c.SourceName).Concat(r.Setters.Select(s=>s.TargetName))).Where(n=>n is not null))
            if(!names.Contains(name!))throw new InvalidDataException("Template trigger name not found in this template: "+name);
    }
    /// <summary>The caller supplies effective literal values and receives matching assignments.
    /// The read delegate must remain stable during this evaluation.</summary>
    public void Evaluate(Func<string?,string,string?> read,Action<Assignment> assign)
    {
        ArgumentNullException.ThrowIfNull(read);ArgumentNullException.ThrowIfNull(assign);
        foreach(var rule in _rules)
        {
            var matches=true;
            foreach(var condition in rule.Conditions)
                if(!EqualsValue(condition.Property,read(condition.SourceName,condition.Property),condition.Value)){matches=false;break;}
            if(matches)foreach(var setter in rule.Setters)assign(setter);
        }
    }
    private static bool EqualsValue(string property,string? actual,string expected)
    {
        if(actual is null)return expected=="{x:Null}";
        if(BooleanProperties.Contains(property))return bool.TryParse(actual,out var a)&&bool.TryParse(expected,out var b)?a==b:actual==expected;
        if(NumericProperties.Contains(property)&&double.TryParse(actual,NumberStyles.Float,CultureInfo.InvariantCulture,out var x)&&double.TryParse(expected,NumberStyles.Float,CultureInfo.InvariantCulture,out var y))return double.IsFinite(x)&&x==y;
        return actual==expected;
    }
    internal static string? Value(DesignNode node,string property)
    {
        if(node.Properties.TryGetValue(property,out var value))return value=="{x:Null}"?null:value;
        return property switch
        {
            "IsEnabled"=>"True", "IsMouseOver" or "IsPressed" or "IsFocused" or "IsKeyboardFocused" or "IsKeyboardFocusWithin" or "IsChecked" or "IsSelected"=>"False",
            "HasContent"=>(node.Children.Length>0||node.Get("Content",node.Get("Text")).Length>0).ToString(),
            "Opacity"=>"1", "Width" or "Height"=>"Auto", "Visibility"=>"Visible", "FontSize"=>"14",
            "MinWidth" or "MinHeight" or "Canvas.Left" or "Canvas.Top"=>"0", _=>node.Get(property)
        };
    }
}
