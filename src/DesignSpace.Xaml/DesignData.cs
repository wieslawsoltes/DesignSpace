using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

/// <summary>Non-executing JSON sample-data preview for simple Binding paths. Not a CLR binding engine.</summary>
public static partial class DesignData
{
    public const string Namespace="https://designspace.dev/designer";
    public static readonly string DataKey="{"+Namespace+"}SampleData";
    [GeneratedRegex(@"^\{Binding\s+(?:Path=)?([A-Za-z_][A-Za-z0-9_.]*)\s*\}$")]
    private static partial Regex BindingPath();
    public static string Read(DesignNode root)=>root.Get(DataKey,"{\n  \"Title\": \"Design with real data\",\n  \"Customer\": { \"Name\": \"Alex Morgan\" }\n}");
    public static DesignNode Set(DesignNode root,string json)
    {
        if(json.Length>256*1024) throw new InvalidDataException("Sample data is limited to 256 KiB.");
        using var data=JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=32 });
        if(data.RootElement.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("Sample data must be a JSON object.");
        var xmlns=XNamespace.Xmlns.NamespaceName;
        var mc="http://schemas.openxmlformats.org/markup-compatibility/2006";
        var ignorable=root.Get("{"+mc+"}Ignorable").Split(' ',StringSplitOptions.RemoveEmptyEntries).Append("ds").Distinct();
        return root.Set("{"+xmlns+"}ds",Namespace).Set("{"+xmlns+"}mc",mc).Set("{"+mc+"}Ignorable",string.Join(' ',ignorable)).Set(DataKey,json);
    }
    public static DesignNode Resolve(DesignNode root)
    {
        var json=root.Get(DataKey); if(json.Length==0) return root;
        using var data=JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=32 });
        string? ResolveValue(string value)
        {
            var match=BindingPath().Match(value); if(!match.Success) return null;
            var current=data.RootElement;
            foreach(var part in match.Groups[1].Value.Split('.')) { if(current.ValueKind!=JsonValueKind.Object || !current.TryGetProperty(part,out current)) return null; }
            return current.ValueKind switch { JsonValueKind.String=>current.GetString(),JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False=>current.GetRawText(),JsonValueKind.Null=>"",_=>null };
        }
        DesignNode Walk(DesignNode n)
        {
            var props=n.Properties;
            foreach(var property in n.Properties) if(ResolveValue(property.Value) is { } value) props=props.SetItem(property.Key,value);
            return n with { Properties=props,Children=n.Children.Select(Walk).ToImmutableArray() };
        }
        return Walk(root);
    }
}
