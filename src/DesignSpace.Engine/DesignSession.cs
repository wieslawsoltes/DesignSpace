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
    public ImmutableHashSet<Guid> Selection { get; private set; } = [];
    public long Revision { get; private set; }
    public bool IsDirty => !ReferenceEquals(Document,_saved);
    public int HistoryLimit { get; set; } = 100;
    public bool CanUndo => _undo.Count>0;
    public bool CanRedo => _redo.Count>0;
    public string UndoLabel => _undo.LastOrDefault()?.Label ?? "";
    public event EventHandler? DocumentChanged;
    public event EventHandler? SelectionChanged;
    public DesignSession(DesignDocument? document = null) { Document=document ?? SampleDocument.Create(); DocumentValidator.Validate(Document); _saved=Document; }
    public void MarkSaved() { _saved=Document; DocumentChanged?.Invoke(this,EventArgs.Empty); }
    public void Load(DesignDocument document)
    {
        DocumentValidator.Validate(document); Document=document; _saved=document; _undo.Clear(); _redo.Clear(); Selection=[]; Revision++;
        DocumentChanged?.Invoke(this,EventArgs.Empty); SelectionChanged?.Invoke(this,EventArgs.Empty);
    }
    public void Select(IEnumerable<Guid> ids,bool additive=false)
    {
        var existing=ids.Where(id=>Document.Root.Find(id) is not null).ToImmutableHashSet();
        Selection=additive ? Selection.SymmetricExcept(existing) : existing; SelectionChanged?.Invoke(this,EventArgs.Empty);
    }
    public void Select(Guid id,bool additive=false) => Select([id],additive);
    public void Execute(string label,Func<DesignDocument,DesignDocument> change,IEnumerable<Guid>? select=null)
    {
        var before=Document; var after=change(before); if (ReferenceEquals(before,after)) return;
        DocumentValidator.Validate(after);
        var next=(select?.ToImmutableHashSet() ?? Selection).Where(id=>after.Root.Find(id) is not null).ToImmutableHashSet();
        _undo.Add(new(label,before,after,Selection,next)); while (_undo.Count>Math.Max(1,HistoryLimit)) _undo.RemoveAt(0);
        _redo.Clear(); Document=after; Selection=next; Revision++;
        DocumentChanged?.Invoke(this,EventArgs.Empty); SelectionChanged?.Invoke(this,EventArgs.Empty);
    }
    public void Undo() { if (!CanUndo) return; var e=_undo[^1]; _undo.RemoveAt(_undo.Count-1); _redo.Push(e); Document=e.Before; Selection=e.BeforeSelection; Changed(); }
    public void Redo() { if (!CanRedo) return; var e=_redo.Pop(); _undo.Add(e); Document=e.After; Selection=e.AfterSelection; Changed(); }
    private void Changed() { Revision++; DocumentChanged?.Invoke(this,EventArgs.Empty); SelectionChanged?.Invoke(this,EventArgs.Empty); }
    public string UniqueName(string prefix)
    {
        var names=Document.Root.DescendantsAndSelf().Select(n=>n.Name).ToHashSet(StringComparer.Ordinal);
        var i=1; while (names.Contains(prefix+i)) i++; return prefix+i;
    }
    public Guid Add(string type,DRect bounds,Guid? parent=null)
    {
        var host=Document.Root.Find(parent ?? Document.Root.Id) ?? Document.Root;
        var n=DesignNode.Create(type,UniqueName(type),bounds.X,bounds.Y,Math.Max(1,bounds.Width),Math.Max(1,bounds.Height));
        n=type switch
        {
            "Rectangle" or "Ellipse" or "Path" => n.Set("Fill","#FF0078D4").Set("Stroke","#FF005A9E").Set("StrokeThickness","1"),
            "TextBlock" => n.Set("Text","New text").Set("FontSize","20").Set("Foreground","#FF202838"),
            "Button" => n.Set("Content","Button").Set("Background","#FF0078D4").Set("Foreground","#FFFFFFFF"),
            "TextBox" => n.Set("Text","TextBox").Set("Background","#FFFFFFFF").Set("BorderBrush","#FF808080"),
            "Line" => n.Set("Stroke","#FF0078D4").Set("StrokeThickness","2"),
            _ => n.Set("Background","#FFF0F0F0")
        };
        Execute("Add "+type,d=>d with { Root=d.Root.Update(host.Id,p=>p with { Children=p.Children.Add(n) }) },[n.Id]); return n.Id;
    }
    public void SetProperty(string key,string value,IEnumerable<Guid>? targets=null)
    {
        var ids=(targets ?? Selection).ToHashSet();
        Execute("Set "+key,d=>d with { Root=Transform(d.Root,ids,n=>n.IsLocked ? n : key=="Rotation" ? n with { Rotation=Numbers.Parse(value,double.NaN) } : n.Set(key,value)) });
    }
    public void SetBounds(IReadOnlyDictionary<Guid,DRect> bounds)
    {
        Execute("Transform selection",d=>d with { Root=Transform(d.Root,bounds.Keys.ToHashSet(),n=>n.IsLocked ? n : n.Set("Canvas.Left",bounds[n.Id].X).Set("Canvas.Top",bounds[n.Id].Y).Set("Width",bounds[n.Id].Width).Set("Height",bounds[n.Id].Height)) });
    }
    public void Move(double x,double y)
    {
        var ids=TopLevelSelection(); Execute("Move selection",d=>d with { Root=Transform(d.Root,ids,n=>n.IsLocked ? n : n.Set("Canvas.Left",n.Number("Canvas.Left")+x).Set("Canvas.Top",n.Number("Canvas.Top")+y)) });
    }
    public void Delete()
    {
        var ids=TopLevelSelection().Where(id=>!Document.Root.Find(id)!.IsLocked).ToHashSet(); if (ids.Count==0) return;
        var removed=ids.SelectMany(id=>Document.Root.Find(id)!.DescendantsAndSelf()).Select(n=>n.Id).ToHashSet();
        Execute("Delete selection",d=>d with { Root=d.Root.Remove(ids),Storyboards=d.Storyboards.Select(b=>b with { Tracks=b.Tracks.Where(t=>!removed.Contains(t.TargetId)).ToImmutableArray() }).ToImmutableArray(),States=d.States.Select(s=>s with { Setters=s.Setters.Where(p=>!removed.Contains(p.TargetId)).ToImmutableArray() }).ToImmutableArray() },[]);
    }
    public void Duplicate()
    {
        var selected=TopLevelSelection(); var created=new List<Guid>(); var used=Document.Root.DescendantsAndSelf().Select(n=>n.Name).ToHashSet();
        DesignNode Clone(DesignNode n)
        {
            var prefix=n.Name+"Copy"; var name=prefix; var i=2; while (!used.Add(name)) name=prefix+i++;
            return (n with { Id=Guid.NewGuid(),Children=n.Children.Select(Clone).ToImmutableArray() }).Set(DesignNode.NameKey,name);
        }
        DesignNode Walk(DesignNode n)
        {
            var children=ImmutableArray.CreateBuilder<DesignNode>();
            foreach (var child in n.Children) { children.Add(Walk(child)); if (selected.Contains(child.Id)) { var clone=Clone(child).Set("Canvas.Left",child.Number("Canvas.Left")+16).Set("Canvas.Top",child.Number("Canvas.Top")+16); children.Add(clone); created.Add(clone.Id); } }
            return n with { Children=children.ToImmutable() };
        }
        var root=Walk(Document.Root); Execute("Duplicate selection",d=>d with { Root=root },created);
    }
    public void Reorder(bool forward)
    {
        var ids=TopLevelSelection(); Execute(forward ? "Bring to front" : "Send to back",d=>d with { Root=ReorderNode(d.Root) });
        DesignNode ReorderNode(DesignNode n)
        {
            var children=n.Children.Select(ReorderNode).ToArray(); var a=children.Where(c=>ids.Contains(c.Id)); var b=children.Where(c=>!ids.Contains(c.Id));
            return n with { Children=(forward ? b.Concat(a) : a.Concat(b)).ToImmutableArray() };
        }
    }
    public void Group()
    {
        var ids=TopLevelSelection(); if (ids.Count==0) return;
        var parents=ids.Select(id=>Document.Root.ParentOf(id)).DistinctBy(n=>n?.Id).ToArray();
        if (parents.Length!=1 || parents[0]?.Type!="Canvas") throw new InvalidOperationException("Grouping currently requires siblings in a Canvas.");
        var parent=parents[0]!; var nodes=parent.Children.Where(c=>ids.Contains(c.Id)).ToArray();
        if (nodes.Any(n=>n.IsLocked)) throw new InvalidOperationException("Unlock the selected objects first.");
        var box=DRect.Union(nodes.Select(n=>new DRect(n.Number("Canvas.Left"),n.Number("Canvas.Top"),n.Number("Width"),n.Number("Height"))));
        var group=DesignNode.Create("Canvas",UniqueName("Group"),box.X,box.Y,box.Width,box.Height) with { Children=nodes.Select(n=>n.Set("Canvas.Left",n.Number("Canvas.Left")-box.X).Set("Canvas.Top",n.Number("Canvas.Top")-box.Y)).ToImmutableArray() };
        Execute("Group into Canvas",d=>d with { Root=d.Root.Update(parent.Id,p=>p with { Children=p.Children.Where(c=>!ids.Contains(c.Id)).Append(group).ToImmutableArray() }) },[group.Id]);
    }
    public void Ungroup()
    {
        if (Selection.Count!=1) return; var group=Document.Root.Find(Selection.Single()); var parent=group is null ? null : Document.Root.ParentOf(group.Id);
        if (group?.Type!="Canvas" || parent?.Type!="Canvas") throw new InvalidOperationException("Select a Canvas inside another Canvas.");
        if (group.IsLocked || group.Rotation!=0 || group.Number("Opacity",1)!=1) throw new InvalidOperationException("Ungroup requires an unlocked, unrotated group at full opacity.");
        var children=group.Children.Select(n=>n.Set("Canvas.Left",n.Number("Canvas.Left")+group.Number("Canvas.Left")).Set("Canvas.Top",n.Number("Canvas.Top")+group.Number("Canvas.Top"))).ToImmutableArray();
        Execute("Ungroup",d=>d with { Root=d.Root.Update(parent.Id,p=>p with { Children=p.Children.SelectMany(n=>n.Id==group.Id ? children : [n]).ToImmutableArray() }) },children.Select(n=>n.Id));
    }
    public void Align(string alignment)
    {
        var ids=TopLevelSelection(); var nodes=ids.Select(id=>Document.Root.Find(id)!).ToArray(); if (nodes.Length<2) return;
        if (nodes.Select(n=>Document.Root.ParentOf(n.Id)?.Id).Distinct().Count()!=1) throw new InvalidOperationException("Alignment requires siblings.");
        var box=DRect.Union(nodes.Select(n=>new DRect(n.Number("Canvas.Left"),n.Number("Canvas.Top"),n.Number("Width"),n.Number("Height"))));
        Execute("Align "+alignment,d=>d with { Root=Transform(d.Root,ids,n=>alignment switch { "Left"=>n.Set("Canvas.Left",box.X),"Right"=>n.Set("Canvas.Left",box.Right-n.Number("Width")),"Center"=>n.Set("Canvas.Left",box.Center.X-n.Number("Width")/2),"Top"=>n.Set("Canvas.Top",box.Y),"Bottom"=>n.Set("Canvas.Top",box.Bottom-n.Number("Height")),"Middle"=>n.Set("Canvas.Top",box.Center.Y-n.Number("Height")/2),_=>n }) });
    }
    public void ToggleLock(Guid id) => Execute("Toggle object lock",d=>d with { Root=d.Root.Update(id,n=>n with { IsLocked=!n.IsLocked }) });
    public void ToggleVisibility(Guid id) => Execute("Toggle visibility",d=>d with { Root=d.Root.Update(id,n=>n.Set("Visibility",n.Visible ? "Collapsed" : "Visible")) });
    public HashSet<Guid> TopLevelSelection()
    {
        var ids=Selection.Where(id=>id!=Document.Root.Id).ToHashSet();
        return ids.Where(id=>!ids.Any(other=>other!=id && Document.Root.Find(other)!.Find(id) is not null)).ToHashSet();
    }
    private static DesignNode Transform(DesignNode root,IReadOnlySet<Guid> ids,Func<DesignNode,DesignNode> transform)
    {
        var n=ids.Contains(root.Id) ? transform(root) : root;
        return n with { Children=n.Children.Select(c=>Transform(c,ids,transform)).ToImmutableArray() };
    }
}
