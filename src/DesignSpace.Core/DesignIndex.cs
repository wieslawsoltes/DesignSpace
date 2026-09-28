using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
namespace DesignSpace.Core;

/// <summary>A weakly cached identity/ancestry index for an immutable document root.</summary>
public sealed class DesignIndex
{
    private static readonly ConditionalWeakTable<DesignNode,DesignIndex> Cache=new();
    private readonly Dictionary<Guid,DesignNode> _nodes=[];
    private readonly Dictionary<Guid,Guid> _parents=[];
    private readonly HashSet<Guid> _locked=[];
    public DesignNode Root { get; }
    public IReadOnlyDictionary<Guid,DesignNode> Nodes=>_nodes;
    public static DesignIndex For(DesignNode root)=>Cache.GetValue(root,n=>new DesignIndex(n));
    private DesignIndex(DesignNode root)
    {
        Root=root;
        void Walk(DesignNode n,Guid? parent,bool locked)
        {
            _nodes.Add(n.Id,n); if(parent is { } id) _parents.Add(n.Id,id);
            locked|=n.IsLocked; if(locked) _locked.Add(n.Id);
            foreach(var child in n.Children) Walk(child,n.Id,locked);
        }
        Walk(root,null,false);
    }
    public DesignNode? Find(Guid id)=>_nodes.GetValueOrDefault(id);
    public DesignNode? ParentOf(Guid id)=>_parents.TryGetValue(id,out var parent) ? _nodes[parent] : null;
    public bool IsLocked(Guid id)=>_locked.Contains(id);
    public IEnumerable<Guid> Ancestors(Guid id)
    {
        while(_parents.TryGetValue(id,out var parent)) { yield return parent; id=parent; }
    }
    /// <summary>Visits only affected ancestor paths and preserves untouched subtree references.</summary>
    public DesignNode Transform(IReadOnlySet<Guid> targets,Func<DesignNode,DesignNode> transform,bool respectLocks=false)
    {
        var active=targets.Where(id=>_nodes.ContainsKey(id) && (!respectLocks || !IsLocked(id))).ToHashSet();
        if(active.Count==0) return Root;
        var affected=new HashSet<Guid>(active);
        foreach(var id in active) foreach(var ancestor in Ancestors(id)) affected.Add(ancestor);
        DesignNode Walk(DesignNode n)
        {
            if(!affected.Contains(n.Id)) return n;
            var result=active.Contains(n.Id) ? transform(n) : n;
            ImmutableArray<DesignNode>.Builder? children=null;
            for(var i=0;i<result.Children.Length;i++)
            {
                var old=result.Children[i]; var next=Walk(old);
                if(!ReferenceEquals(old,next)) { children ??= result.Children.ToBuilder(); children[i]=next; }
            }
            return children is null ? result : result with { Children=children.ToImmutable() };
        }
        return Walk(Root);
    }
}

/// <summary>Structural-sharing batch edits usable without a session or UI framework.</summary>
public static class DesignTree
{
    private static readonly ConditionalWeakTable<DesignNode,string[]> PropertyNames=new();
    public static DesignNode SetProperties(DesignNode root,IReadOnlyDictionary<Guid,IReadOnlyDictionary<string,string>> changes,bool replacePropertyElements=false)
    {
        return DesignIndex.For(root).Transform(changes.Keys.ToHashSet(),n=>
        {
            if(replacePropertyElements && !n.PropertyElements.IsEmpty)
            {
                var names=PropertyNames.GetValue(n,node=>node.PropertyElements.Select(raw=>XElement.Parse(raw).Name.LocalName).ToArray());
                var keys=changes[n.Id].Keys.Select(key=>key.Contains('.') ? key : n.Type+"."+key).ToHashSet();
                var retained=n.PropertyElements.Where((raw,i)=>!keys.Contains(names[i])).ToImmutableArray();
                if(retained.Length!=n.PropertyElements.Length) n=n with { PropertyElements=retained };
            }
            foreach(var p in changes[n.Id]) n=p.Key=="Rotation" ? n.Rotation==Numbers.Parse(p.Value) ? n : n with { Rotation=Numbers.Parse(p.Value) } : n.Set(p.Key,p.Value);
            return n;
        });
    }
    public static DesignNode Remove(DesignNode root,IReadOnlySet<Guid> ids)
    {
        var index=DesignIndex.For(root);
        var parents=ids.Select(index.ParentOf).OfType<DesignNode>().Select(n=>n.Id).ToHashSet();
        return index.Transform(parents,n=>n with { Children=n.Children.Where(c=>!ids.Contains(c.Id)).ToImmutableArray() });
    }
}
