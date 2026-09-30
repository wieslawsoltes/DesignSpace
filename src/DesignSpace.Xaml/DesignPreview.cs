using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

public sealed record PreviewResult(DesignNode Root,IReadOnlyList<string> Diagnostics);
/// <summary>Non-executing style/template expansion. Saved documents remain unchanged.</summary>
public static class DesignPreview
{
    public const string OwnerKey="{https://designspace.dev/designer}TemplateOwner";
    public const string ExpandedKey="{https://designspace.dev/designer}TemplateExpanded";
    private static readonly ConditionalWeakTable<DesignNode,PreviewResult> Cache=new();
    // Values are read-only after construction. Immutable edits retain unchanged markup strings,
    // so templates and trigger programs survive transient hover/press and geometry previews.
    private static readonly ConditionalWeakTable<string,XElement> Markup=new();
    private static readonly ConditionalWeakTable<XElement,TriggerCompilation> Programs=new();
    private static readonly ConditionalWeakTable<XElement,DesignNode> Visuals=new();
    private static readonly XNamespace X=DesignNode.XamlNamespace;
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    private sealed record TriggerCompilation(PropertyTriggerProgram Program,string? Error);
    private sealed class Scope(Scope? parent)
    {
        public Dictionary<string,XElement> Resources { get; }=new(StringComparer.Ordinal);
        public (XElement Value,Scope Scope)? FindEntry(string key)=>Resources.TryGetValue(key,out var value)?(value,this):parent?.FindEntry(key);
        public XElement? Find(string key)=>FindEntry(key)?.Value;
    }
    private static XElement Read(string source)=>Markup.GetValue(source,XElement.Parse);
    public static DesignNode Resolve(DesignNode root)=>Get(root).Root;
    public static PreviewResult Get(DesignNode root)=>Cache.GetValue(root,n=>Build(n,PreviewInteractionState.Empty));
    private static string? ResourceKey(string value)
    {
        value=value.Trim();if(!value.EndsWith('}'))return null;
        var prefix=value.StartsWith("{StaticResource ",StringComparison.Ordinal)?16:value.StartsWith("{ThemeResource ",StringComparison.Ordinal)?15:0;
        if(prefix==0)return null;
        var key=value[prefix..^1].Trim();if(key.StartsWith("ResourceKey=",StringComparison.Ordinal))key=key[12..].Trim();
        if(key.Length==0||key.IndexOfAny(['{','}',','])>=0)throw new InvalidDataException("Unsupported resource key syntax.");return key;
    }
    private static string Target(string text)=>text.Replace("{x:Type ","",StringComparison.Ordinal).TrimEnd('}').Split(':').Last().Trim();
    private static HashSet<string> Locals(DesignNode n)=>n.Properties.Keys.Concat(n.PropertyElements.Select(p=>Read(p).Name.LocalName.Split('.').Last())).ToHashSet(StringComparer.Ordinal);
    private static DesignNode Assign(DesignNode node,PropertyTriggerProgram.Assignment setter)
    {
        var elements=node.PropertyElements.Where(p=>Read(p).Name.LocalName.Split('.').Last()!=setter.Property).ToImmutableArray();
        if(setter.IsObject)
            return node with{Properties=node.Properties.Remove(setter.Property),PropertyElements=elements.Add(new XElement(XName.Get(node.Type+"."+setter.Property,node.Namespace),new XElement(Read(setter.Value))).ToString(SaveOptions.DisableFormatting))};
        return node.Set(setter.Property,setter.Value) with{PropertyElements=elements};
    }
    // Resolve a setter at its declaration scope, not the consumer's potentially shadowing scope.
    private static PropertyTriggerProgram.Assignment Materialize(PropertyTriggerProgram.Assignment setter,Scope scope)
    {
        if(!setter.IsObject&&ResourceKey(setter.Value) is { } key)
        {
            var entry=scope.FindEntry(key)??throw new InvalidDataException("Trigger resource not found: "+key);
            if(entry.Value.Name.LocalName is "String" or "Double" or "Color" or "Thickness" or "Boolean")return setter with{Value=entry.Value.Value};
            if(entry.Value.Name==X+"Null")return setter with{Value="{x:Null}"};
            return Materialize(setter with{Value=entry.Value.ToString(SaveOptions.DisableFormatting),IsObject=true},entry.Scope);
        }
        if(!setter.IsObject)return setter;
        var value=new XElement(Read(setter.Value));value.Attribute(X+"Key")?.Remove();
        foreach(var element in value.DescendantsAndSelf())foreach(var attribute in element.Attributes().Where(a=>!a.IsNamespaceDeclaration).ToArray())
            if(ResourceKey(attribute.Value) is { } nestedKey)
            {
                var entry=scope.FindEntry(nestedKey)??throw new InvalidDataException("Trigger value resource not found: "+nestedKey);
                if(entry.Value.HasElements||entry.Value.Name.LocalName is not ("Color" or "Double" or "Point" or "String" or "Boolean"))throw new InvalidDataException("Only literal value resources are supported inside trigger value objects.");
                attribute.Value=entry.Value.Value;
            }
        return setter with{Value=value.ToString(SaveOptions.DisableFormatting)};
    }
    internal static PreviewResult Build(DesignNode root,PreviewInteractionState input)
    {
        var warnings=new List<string>();var warningSet=new HashSet<string>();var expanded=0;
        void Warn(string message){if(warnings.Count<100&&warningSet.Add(message))warnings.Add(message);}
        var sourceIndex=DesignIndex.For(root);
        HashSet<Guid> Ancestors(Guid? target)
        {
            var set=new HashSet<Guid>();
            if(target is { } id)for(var n=sourceIndex.Find(id);n is not null;n=sourceIndex.ParentOf(n.Id))set.Add(n.Id);
            return set;
        }
        var hovered=Ancestors(input.Hovered);var focused=Ancestors(input.Focused);
        Scope Resources(DesignNode n,Scope? parent)
        {
            var scope=new Scope(parent);
            foreach(var raw in n.PropertyElements)
            {
                var property=Read(raw);if(!property.Name.LocalName.EndsWith(".Resources",StringComparison.Ordinal))continue;
                var container=property.Elements().FirstOrDefault(e=>e.Name.LocalName=="ResourceDictionary")??property;
                foreach(var e in container.Elements())
                {
                    var key=(string?)e.Attribute(X+"Key");
                    if(key is null&&e.Name==Ns+"Style")key="@"+Target((string?)e.Attribute("TargetType")??"");
                    if(key is not null)scope.Resources[key]=e;
                }
            }
            return scope;
        }
        PropertyTriggerProgram Triggers(XElement owner,bool template)
        {
            var compiled=Programs.GetValue(owner,e=>
            {
                try
                {
                    var collections=e.Elements(Ns+(template?"ControlTemplate.Triggers":"Style.Triggers")).ToArray();
                    if(collections.Length>1)throw new InvalidDataException("Duplicate trigger collection.");
                    return new(collections.Length==0?PropertyTriggerProgram.Empty:PropertyTriggerProgram.Compile(collections[0],template),null);
                }
                catch(Exception ex)when(ex is InvalidDataException or System.Xml.XmlException or ArgumentException)
                { return new(PropertyTriggerProgram.Empty,ex.Message); }
            });
            if(compiled.Error is not null)Warn(compiled.Error);return compiled.Program;
        }
        DesignNode Input(DesignNode n,bool parentEnabled)
        {
            if(hovered.Contains(n.Id))n=n.Set("IsMouseOver","True");
            if(focused.Contains(n.Id))n=n.Set("IsKeyboardFocusWithin","True");
            if(input.Pressed==n.Id)n=n.Set("IsPressed","True");
            if(input.Focused==n.Id)n=n.Set("IsFocused","True").Set("IsKeyboardFocused","True");
            if(input.Checked.TryGetValue(n.Id,out var check))n=n.Set("IsChecked",check?.ToString()??"{x:Null}");
            return parentEnabled?n:n.Set("IsEnabled","False");
        }
        (DesignNode Node,HashSet<string> TriggerKeys) Style(DesignNode source,DesignNode n,Scope scope,bool parentEnabled)
        {
            var local=Locals(source);var setters=new Dictionary<string,(XElement Setter,Scope Scope)>(StringComparer.Ordinal);
            var styles=new List<(XElement Style,Scope Scope)>();var seen=new HashSet<XElement>();
            void Collect(XElement? style,Scope definition,int depth)
            {
                if(style is null||style.Name!=Ns+"Style")return;
                if(depth>32||!seen.Add(style))throw new InvalidDataException("Cyclic or over-deep BasedOn style chain.");
                var based=ResourceKey((string?)style.Attribute("BasedOn")??"");
                if(based is not null&&definition.FindEntry(based) is { } entry)Collect(entry.Value,entry.Scope,depth+1);
                foreach(var setter in style.Elements().SelectMany(e=>e.Name.LocalName=="Style.Setters"?e.Elements():new[]{e}).Where(e=>e.Name==Ns+"Setter"))
                    if((string?)setter.Attribute("Property") is { } property)setters[property]=(setter,definition);
                styles.Add((style,definition));
            }
            var inline=source.PropertyElements.FirstOrDefault(p=>Read(p).Name.LocalName.EndsWith(".Style",StringComparison.Ordinal));
            var styleKey=ResourceKey(source.Get("Style"));var selected=scope.FindEntry(styleKey??"@"+source.Type);
            if(inline is not null)Collect(Read(inline).Elements().FirstOrDefault(),scope,0);
            else if(source.Get("Style")!="{x:Null}"&&selected is { } definition)Collect(definition.Value,definition.Scope,0);
            foreach(var (property,item) in setters)
            {
                if(local.Contains(property))continue;
                if(item.Setter.Attribute("Value") is { } value)n=Assign(n,new(property,value.Value,null,false));
                else if(item.Setter.Elements().FirstOrDefault()?.Elements().FirstOrDefault() is { } element)
                    n=Assign(n,new(property,element.ToString(SaveOptions.DisableFormatting),null,true));
            }
            // All rules read one immutable, styled snapshot. Feedback within a collection rejects.
            if(!parentEnabled)n=n.Set("IsEnabled","False");
            var snapshot=n;var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in styles)
            {
                try
                {
                    var changes=new List<PropertyTriggerProgram.Assignment>();
                    Triggers(item.Style,false).Evaluate((_,p)=>PropertyTriggerProgram.Value(snapshot,p),s=>changes.Add(Materialize(s,item.Scope)));
                    foreach(var setter in changes)if(!local.Contains(setter.Property)){n=Assign(n,setter);keys.Add(setter.Property);}
                }
                catch(InvalidDataException ex){Warn(ex.Message);}
            }
            foreach(var p in n.Properties)
            {
                var key=ResourceKey(p.Value);var resource=key is null?null:scope.Find(key);
                if(resource?.Name.LocalName=="SolidColorBrush"&&!resource.HasElements&&resource.Attributes().All(a=>a.IsNamespaceDeclaration||a.Name.LocalName is "Key" or "Name" or "Color"))n=n.Set(p.Key,(string?)resource.Attribute("Color")??"Transparent");
                else if(resource?.Name.LocalName is "String" or "Double" or "Color" or "Thickness")n=n.Set(p.Key,resource.Value);
            }
            return(n,keys);
        }
        DesignNode Walk(DesignNode source,Scope? parent,int depth,bool allowTemplate=true,bool parentEnabled=true)
        {
            if(depth>64||++expanded>40000)throw new InvalidDataException("Preview expansion exceeds its node/depth budget.");
            var scope=Resources(source,parent);var styled=Style(source,Input(source,parentEnabled),scope,parentEnabled);var n=styled.Node;
            if(!parentEnabled)n=n.Set("IsEnabled","False");
            var enabled=!bool.TryParse(n.Get("IsEnabled"),out var isEnabled)||isEnabled;
            var children=n.Children.Select(c=>Walk(c,scope,depth+1,parentEnabled:enabled)).ToImmutableArray();
            if(!children.SequenceEqual(n.Children))n=n with{Children=children};
            if(!allowTemplate)return n;
            var inline=n.PropertyElements.FirstOrDefault(p=>Read(p).Name.LocalName.EndsWith(".Template",StringComparison.Ordinal));
            var key=ResourceKey(n.Get("Template"));var entry=key is null?null:scope.FindEntry(key);
            var template=inline is not null?Read(inline).Elements().FirstOrDefault():entry?.Value;
            if(template?.Name!=Ns+"ControlTemplate")return n;
            var visuals=template.Elements().Where(e=>!e.Name.LocalName.Contains('.')).ToArray();
            if(visuals.Length!=1)throw new InvalidDataException("A ControlTemplate requires one visual root.");
            var parsed=Visuals.GetValue(template,_=>XamlCodec.Parse(visuals[0].ToString()).Document.Root);
            var templateScope=new Scope(inline is null?entry?.Scope??scope:scope);
            foreach(var resource in template.Elements(Ns+"ControlTemplate.Resources").SelectMany(e=>e.Elements().FirstOrDefault(c=>c.Name==Ns+"ResourceDictionary")?.Elements()??e.Elements()))
                if((string?)resource.Attribute(X+"Key") is { } resourceKey)templateScope.Resources[resourceKey]=resource;
            var names=new Dictionary<string,Guid>(StringComparer.Ordinal);var program=Triggers(template,true);
            DesignNode CreateVisual(DesignNode owner)
            {
                var ordinal=0;var contentUsed=false;names.Clear();
                DesignNode Instantiate(DesignNode part)
                {
                    var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(owner.Id.ToString("N")+":"+ordinal++)).AsSpan(0,16));
                    var next=part with{Id=id};
                    foreach(var property in part.Properties)
                    {
                        if(property.Value.StartsWith("{TemplateBinding ",StringComparison.Ordinal)&&property.Value.EndsWith('}'))
                        {
                            var path=property.Value[17..^1].Trim();var value=BrushResolver.LocalSource(owner,path)??PropertyTriggerProgram.Value(owner,path)??"{x:Null}";
                            next=Assign(next,new(property.Key,value,null,value.TrimStart().StartsWith('<')));
                        }
                        else if(ResourceKey(property.Value) is not null&&property.Key is "Background" or "Foreground" or "Fill" or "Stroke" or "BorderBrush" or "OpacityMask" or "Effect")
                        {
                            try{next=Assign(next,Materialize(new(property.Key,property.Value,null,false),templateScope));}
                            catch(InvalidDataException ex){Warn(ex.Message);}
                        }
                    }
                    var name=part.Get(DesignNode.NameKey,part.Get("Name"));
                    if(name.Length>0){names.Add(name,id);next=next.Set(DesignNode.NameKey,owner.Name+"_"+name);}
                    next=next.Set(OwnerKey,owner.Id.ToString()) with{Children=part.Children.Select(Instantiate).ToImmutableArray()};
                    if(part.Type=="ContentPresenter")
                    {
                        if(!contentUsed&&!owner.Children.IsEmpty){next=next with{Type="Grid",Children=owner.Children};contentUsed=true;}
                        else
                        {
                            next=(next with{Type="TextBlock"}).Set("Text",owner.Get("Content",owner.Get("Text")));
                            if(!next.Properties.ContainsKey("Foreground")&&!Locals(next).Contains("Foreground"))next=Assign(next,new("Foreground",BrushResolver.LocalSource(owner,"Foreground")??"#FF202838",null,(BrushResolver.LocalSource(owner,"Foreground")??"").StartsWith('<')));
                            if(!next.Properties.ContainsKey("FontSize"))next=next.Set("FontSize",owner.Number("FontSize",14));
                        }
                    }
                    return next;
                }
                return Walk(Instantiate(parsed),templateScope,depth+1,false,enabled);
            }
            var visual=CreateVisual(n);
            try
            {
                program.ValidateNames(names.Keys.ToHashSet(StringComparer.Ordinal));
                var parts=DesignIndex.For(visual);var owner=n;var changes=new List<PropertyTriggerProgram.Assignment>();
                program.Evaluate((name,p)=>PropertyTriggerProgram.Value(name is null?owner:parts.Find(names[name])!,p),s=>changes.Add(Materialize(s,templateScope)));
                var local=Locals(source);var next=n;
                foreach(var setter in changes)if(setter.TargetName is null&&!local.Contains(setter.Property)&&!styled.TriggerKeys.Contains(setter.Property))next=Assign(next,setter);
                if(!ReferenceEquals(n,next)){n=next;visual=CreateVisual(n);}
                foreach(var group in changes.Where(s=>s.TargetName is not null).GroupBy(s=>names[s.TargetName!]))
                    visual=visual.Update(group.Key,part=>{foreach(var setter in group)part=Assign(part,setter);return part;});
            }
            catch(InvalidDataException ex){Warn(ex.Message);}
            return n.Set(ExpandedKey,"True") with{Children=[visual]};
        }
        DesignNode resolved;
        try{resolved=Walk(DesignData.Resolve(root),null,0);}
        catch(Exception e)when(e is InvalidDataException or System.Xml.XmlException or ArgumentException){Warn(e.Message);resolved=root;}
        return new(DesignData.Resolve(resolved),warnings);
    }
}
