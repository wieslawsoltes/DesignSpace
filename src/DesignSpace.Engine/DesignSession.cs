using System.Collections.Immutable;
using DesignSpace.Core;
namespace DesignSpace.Engine;

public sealed class DesignSession
{
    private sealed record HistoryEntry(string Label,DesignDocument Before,DesignDocument After,ImmutableHashSet<Guid> BeforeSelection,ImmutableHashSet<Guid> AfterSelection);
    private readonly List<HistoryEntry> _undo=[];
    private readonly Stack<HistoryEntry> _redo=[];
    private DesignDocument _saved;
    public DesignDocument Document { get; private set; }
    public DesignIndex Index=>DesignIndex.For(Document.Root);
    public ImmutableHashSet<Guid> Selection { get; private set; }=[];
    public long Revision { get; private set; }
    public bool IsDirty=>!ReferenceEquals(Document,_saved);
    public int HistoryLimit { get; set; }=100;
    public bool CanUndo=>_undo.Count>0;
    public bool CanRedo=>_redo.Count>0;
    public string UndoLabel=>_undo.LastOrDefault()?.Label ?? "";
    public event EventHandler? DocumentChanged;
    public event EventHandler? SelectionChanged;
    public DesignSession(DesignDocument? document=null) { Document=document ?? SampleDocument.Create(); DocumentValidator.Validate(Document); _saved=Document; }
    public void MarkSaved() { _saved=Document; DocumentChanged?.Invoke(this,EventArgs.Empty); }
    public void Load(DesignDocument document)
    {
        DocumentValidator.Validate(document); Document=document; _saved=document; _undo.Clear(); _redo.Clear(); Selection=[]; Changed();
    }
    public void Select(IEnumerable<Guid> ids,bool additive=false)
    {
        var existing=ids.Where(id=>Index.Find(id) is not null).ToImmutableHashSet(); var next=additive ? Selection.SymmetricExcept(existing) : existing;
        if(Selection.SetEquals(next)) return; Selection=next; SelectionChanged?.Invoke(this,EventArgs.Empty);
    }
    public void Select(Guid id,bool additive=false)=>Select([id],additive);
    public void Execute(string label,Func<DesignDocument,DesignDocument> change,IEnumerable<Guid>? select=null)
    {
        var before=Document; var after=change(before);
        if(Equals(before,after)) { if(select is not null) Select(select); return; }
        DocumentValidator.Validate(after); var index=DesignIndex.For(after.Root);
        var next=(select?.ToImmutableHashSet() ?? Selection).Where(id=>index.Find(id) is not null).ToImmutableHashSet();
        _undo.Add(new(label,before,after,Selection,next)); while(_undo.Count>Math.Max(1,HistoryLimit)) _undo.RemoveAt(0);
        _redo.Clear(); Document=after; Selection=next; Changed();
    }
    public void Undo() { if(!CanUndo) return; var e=_undo[^1]; _undo.RemoveAt(_undo.Count-1); _redo.Push(e); Document=e.Before; Selection=e.BeforeSelection; Changed(); }
    public void Redo() { if(!CanRedo) return; var e=_redo.Pop(); _undo.Add(e); Document=e.After; Selection=e.AfterSelection; Changed(); }
    private void Changed() { Revision++; DocumentChanged?.Invoke(this,EventArgs.Empty); SelectionChanged?.Invoke(this,EventArgs.Empty); }
    public string UniqueName(string prefix)
    {
        var names=Index.Nodes.Values.Select(n=>n.Name).ToHashSet(StringComparer.Ordinal); var i=1; while(names.Contains(prefix+i)) i++; return prefix+i;
    }
    private static bool Container(DesignNode n)=>n.Type is "Canvas" or "Grid" or "StackPanel" or "Border" or "Page" or "UserControl" or "ContentControl";
    public Guid Add(string type,DRect bounds,Guid? parent=null)
    {
        var host=Index.Find(parent ?? Document.Root.Id) ?? throw new InvalidOperationException("The target container no longer exists.");
        if(Index.IsLocked(host.Id)) throw new InvalidOperationException("Unlock the target container first.");
        if(!Container(host)) throw new InvalidOperationException("Choose a panel or content container.");
        if(host.Type is "Border" or "Page" or "UserControl" or "ContentControl" && !host.Children.IsEmpty) throw new InvalidOperationException("This container already has content. Add a panel inside it to host multiple children.");
        var n=DesignNode.Create(type,UniqueName(type),bounds.X,bounds.Y,Math.Max(1,bounds.Width),Math.Max(1,bounds.Height));
        n=type switch
        {
            "Rectangle" or "Ellipse" or "Path"=>n.Set("Fill","#FF0078D4").Set("Stroke","#FF005A9E").Set("StrokeThickness","1"),
            "TextBlock"=>n.Set("Text","New text").Set("FontSize","20").Set("Foreground","#FF202838"),
            "Button"=>n.Set("Content","Button").Set("Background","#FF0078D4").Set("Foreground","#FFFFFFFF"),
            "TextBox"=>n.Set("Text","TextBox").Set("Background","#FFFFFFFF").Set("BorderBrush","#FF808080"),
            "Line"=>n.Set("Stroke","#FF0078D4").Set("StrokeThickness","2"),
            _=>n.Set("Background","#FFF0F0F0")
        };
        if(type=="Path")n=n.Set("Data","M 0 100 L 50 0 L 100 100 Z").Set("Stretch","Fill");
        Execute("Add "+type,d=>d with { Root=d.Root.Update(host.Id,p=>p with { Children=p.Children.Add(n) }) },[n.Id]); return n.Id;
    }
    private void Transform(string label,IReadOnlySet<Guid> ids,Func<DesignNode,DesignNode> edit)
        =>Execute(label,d=>d with { Root=DesignIndex.For(d.Root).Transform(ids,edit,respectLocks:true) });
    public void SetProperty(string key,string value,IEnumerable<Guid>? targets=null)
    {
        if(key=="Data"&&!value.StartsWith('{'))VectorPathCodec.Parse(value);
        var ids=(targets ?? Selection).ToHashSet();
        Transform("Set "+key,ids,n=>key=="Rotation" ? n.Rotation==Numbers.Parse(value,double.NaN) ? n : n with { Rotation=Numbers.Parse(value,double.NaN) } : n.Set(key,value));
    }
    public void SetBounds(IReadOnlyDictionary<Guid,DRect> bounds)=>Transform("Transform selection",bounds.Keys.ToHashSet(),n=>n.Set("Canvas.Left",bounds[n.Id].X).Set("Canvas.Top",bounds[n.Id].Y).Set("Width",bounds[n.Id].Width).Set("Height",bounds[n.Id].Height));
    public void Move(double x,double y)
    {
        var ids=TopLevelSelection();
        if(ids.Any(id=>!Index.IsLocked(id) && Index.ParentOf(id)?.Type!="Canvas")) throw new InvalidOperationException("Free movement requires Canvas children. Edit margin or grid placement for auto-layout children.");
        Transform("Move selection",ids,n=>n.Set("Canvas.Left",n.Number("Canvas.Left")+x).Set("Canvas.Top",n.Number("Canvas.Top")+y));
    }
    public void Delete()
    {
        var ids=TopLevelSelection().Where(id=>!Index.IsLocked(id)).ToHashSet(); if(ids.Count==0) return;
        var removed=ids.SelectMany(id=>Index.Find(id)!.DescendantsAndSelf()).Select(n=>n.Id).ToHashSet();
        Execute("Delete selection",d=>d with { Root=d.Root.Remove(ids),Storyboards=d.Storyboards.Select(b=>b with { Tracks=b.Tracks.Where(t=>!removed.Contains(t.TargetId)).ToImmutableArray() }).ToImmutableArray(),States=d.States.Select(s=>s with { Setters=s.Setters.Where(p=>!removed.Contains(p.TargetId)).ToImmutableArray() }).ToImmutableArray() },[]);
    }
    public void Duplicate()
    {
        var selected=TopLevelSelection().Where(id=>!Index.IsLocked(id)).ToHashSet(); if(selected.Count==0) return;
        var created=new List<Guid>(); var used=Index.Nodes.Values.Select(n=>n.Name).ToHashSet();
        DesignNode Clone(DesignNode n)
        {
            var prefix=n.Name+"Copy"; var name=prefix; var i=2; while(!used.Add(name)) name=prefix+i++;
            return (n with { Id=Guid.NewGuid(),Children=n.Children.Select(Clone).ToImmutableArray() }).Set(DesignNode.NameKey,name);
        }
        var parents=selected.Select(id=>Index.ParentOf(id)!.Id).ToHashSet();
        var root=Index.Transform(parents,n=>
        {
            var children=ImmutableArray.CreateBuilder<DesignNode>();
            foreach(var child in n.Children)
            {
                children.Add(child); if(!selected.Contains(child.Id)) continue;
                var clone=Clone(child).Set("Canvas.Left",child.Number("Canvas.Left")+16).Set("Canvas.Top",child.Number("Canvas.Top")+16); children.Add(clone); created.Add(clone.Id);
            }
            return n with { Children=children.ToImmutable() };
        });
        Execute("Duplicate selection",d=>d with { Root=root },created);
    }
    public void Reorder(bool forward)
    {
        var ids=TopLevelSelection().Where(id=>!Index.IsLocked(id)).ToHashSet();
        var parents=ids.Select(id=>Index.ParentOf(id)!.Id).ToHashSet();
        Transform(forward ? "Bring to front" : "Send to back",parents,n=>
        {
            var a=n.Children.Where(c=>ids.Contains(c.Id)); var b=n.Children.Where(c=>!ids.Contains(c.Id)); var children=(forward ? b.Concat(a) : a.Concat(b)).ToImmutableArray();
            return n.Children.SequenceEqual(children) ? n : n with { Children=children };
        });
    }
    private DesignNode[] CanvasSiblings(int minimum=1)
    {
        var nodes=TopLevelSelection().Select(id=>Index.Find(id)!).ToArray(); if(nodes.Length<minimum) return [];
        if(nodes.Any(n=>Index.IsLocked(n.Id))) throw new InvalidOperationException("Unlock the selection and its ancestors first.");
        var parents=nodes.Select(n=>Index.ParentOf(n.Id)).DistinctBy(n=>n?.Id).ToArray();
        if(parents.Length!=1 || parents[0]?.Type!="Canvas") throw new InvalidOperationException("This operation requires siblings in a Canvas.");
        return nodes;
    }
    private static DRect Box(DesignNode n)=>new(n.Number("Canvas.Left"),n.Number("Canvas.Top"),n.Number("Width"),n.Number("Height"));
    public void Group()
    {
        var nodes=CanvasSiblings(); if(nodes.Length==0) return; var ids=nodes.Select(n=>n.Id).ToHashSet(); var parent=Index.ParentOf(nodes[0].Id)!;
        var ordered=parent.Children.Where(c=>ids.Contains(c.Id)).ToArray(); var positions=Enumerable.Range(0,parent.Children.Length).Where(i=>ids.Contains(parent.Children[i].Id)).ToArray();
        if(positions[^1]-positions[0]+1!=positions.Length) throw new InvalidOperationException("Group contiguous siblings to preserve their order relative to unselected objects.");
        var box=DRect.Union(ordered.Select(Box));
        var group=DesignNode.Create("Canvas",UniqueName("Group"),box.X,box.Y,box.Width,box.Height) with { Children=ordered.Select(n=>n.Set("Canvas.Left",n.Number("Canvas.Left")-box.X).Set("Canvas.Top",n.Number("Canvas.Top")-box.Y)).ToImmutableArray() };
        Execute("Group into Canvas",d=>d with { Root=d.Root.Update(parent.Id,p=>p with { Children=p.Children.Take(positions[0]).Append(group).Concat(p.Children.Skip(positions[^1]+1)).ToImmutableArray() }) },[group.Id]);
    }
    public void Ungroup()
    {
        if(Selection.Count!=1) return; var group=Index.Find(Selection.Single()); var parent=group is null ? null : Index.ParentOf(group.Id);
        if(group?.Type!="Canvas" || parent?.Type!="Canvas") throw new InvalidOperationException("Select a Canvas inside another Canvas.");
        if(Index.IsLocked(group.Id) || group.Rotation!=0 || group.Number("Opacity",1)!=1 || group.Get("Padding","0")!="0" || group.PropertyElements.Any(p=>p.Contains(".RenderTransform",StringComparison.Ordinal))) throw new InvalidOperationException("Ungroup requires an unlocked group without transforms, padding or group opacity.");
        if(Document.Storyboards.Any(b=>b.Tracks.Any(t=>t.TargetId==group.Id)) || Document.States.Any(s=>s.Setters.Any(p=>p.TargetId==group.Id))) throw new InvalidOperationException("Remove group animation/state references before ungrouping.");
        var children=group.Children.Select(n=>n.Set("Canvas.Left",n.Number("Canvas.Left")+group.Number("Canvas.Left")).Set("Canvas.Top",n.Number("Canvas.Top")+group.Number("Canvas.Top"))).ToImmutableArray();
        Execute("Ungroup",d=>d with { Root=d.Root.Update(parent.Id,p=>p with { Children=p.Children.SelectMany(n=>n.Id==group.Id ? children : [n]).ToImmutableArray() }) },children.Select(n=>n.Id));
    }
    public void Align(string alignment)
    {
        var nodes=CanvasSiblings(2); if(nodes.Length<2) return; var box=DRect.Union(nodes.Select(Box));
        Transform("Align "+alignment,nodes.Select(n=>n.Id).ToHashSet(),n=>alignment switch
        {
            "Left"=>n.Set("Canvas.Left",box.X),"Right"=>n.Set("Canvas.Left",box.Right-n.Number("Width")),"Center"=>n.Set("Canvas.Left",box.Center.X-n.Number("Width")/2),
            "Top"=>n.Set("Canvas.Top",box.Y),"Bottom"=>n.Set("Canvas.Top",box.Bottom-n.Number("Height")),"Middle"=>n.Set("Canvas.Top",box.Center.Y-n.Number("Height")/2),_=>n
        });
    }
    public void Distribute(bool horizontal)
    {
        var nodes=CanvasSiblings(3); if(nodes.Length<3) return;
        nodes=nodes.OrderBy(n=>horizontal ? Box(n).X : Box(n).Y).ToArray(); var first=Box(nodes[0]); var last=Box(nodes[^1]);
        var start=horizontal ? first.X : first.Y; var end=horizontal ? last.Right : last.Bottom; var total=nodes.Sum(n=>horizontal ? Box(n).Width : Box(n).Height); var gap=(end-start-total)/(nodes.Length-1);
        var positions=new Dictionary<Guid,double>(); var cursor=start;
        foreach(var n in nodes) { positions[n.Id]=cursor; cursor+=(horizontal ? Box(n).Width : Box(n).Height)+gap; }
        Transform(horizontal ? "Distribute horizontally" : "Distribute vertically",positions.Keys.ToHashSet(),n=>n.Set(horizontal ? "Canvas.Left" : "Canvas.Top",positions[n.Id]));
    }
    public void Reparent(Guid id,Guid targetId,int position=-1)
    {
        var node=Index.Find(id) ?? throw new ArgumentException("Missing element."); var target=Index.Find(targetId) ?? throw new ArgumentException("Missing target.");
        if(id==Document.Root.Id || id==targetId || Index.Ancestors(targetId).Contains(id)) throw new InvalidOperationException("Reparenting must not create a cycle.");
        if(Index.IsLocked(id) || Index.IsLocked(targetId) || !Container(target)) throw new InvalidOperationException("Choose an unlocked element and container.");
        var oldParent=Index.ParentOf(id)!;
        if(target.Type is "Border" or "Page" or "UserControl" or "ContentControl" && target.Children.Any(n=>n.Id!=id)) throw new InvalidOperationException("The target already has content.");
        if(oldParent.Id==targetId)
        {
            var others=target.Children.Where(n=>n.Id!=id).ToImmutableArray(); var at=position<0 ? others.Length : Math.Clamp(position,0,others.Length);
            Execute("Reorder child",d=>d with { Root=d.Root.Update(targetId,n=>n with { Children=others.Insert(at,node) }) }); return;
        }
        if(target.Type=="Canvas")
        {
            var layout=new LayoutEngine().Arrange(Document.Root);
            if(!layout.ById.TryGetValue(id,out var source) || !layout.ById.TryGetValue(targetId,out var host) || source.WorldTransform!=DMatrix.Identity || host.WorldTransform!=DMatrix.Identity) throw new InvalidOperationException("Canvas reparenting currently requires visible, untransformed source and destination.");
            node=node.Set("Canvas.Left",source.Bounds.X-host.Bounds.X).Set("Canvas.Top",source.Bounds.Y-host.Bounds.Y);
        }
        var moved=node;
        Execute("Reparent element",d=>
        {
            var root=d.Root.Remove(new HashSet<Guid>{id});
            return d with { Root=root.Update(targetId,n=>n with { Children=n.Children.Insert(position<0 ? n.Children.Length : Math.Clamp(position,0,n.Children.Length),moved) }) };
        },[id]);
    }
    public void ToggleLock(Guid id)
    {
        if(Index.Ancestors(id).Any(parent=>Index.Find(parent)!.IsLocked)) throw new InvalidOperationException("Unlock the ancestor first.");
        Execute("Toggle object lock",d=>d with { Root=d.Root.Update(id,n=>n with { IsLocked=!n.IsLocked }) });
    }
    public void ToggleVisibility(Guid id)=>Transform("Toggle visibility",new HashSet<Guid>{id},n=>n.Set("Visibility",n.Visible ? "Collapsed" : "Visible"));
    public HashSet<Guid> TopLevelSelection()
    {
        var ids=Selection.Where(id=>id!=Document.Root.Id).ToHashSet(); return ids.Where(id=>!Index.Ancestors(id).Any(ids.Contains)).ToHashSet();
    }
}
