using System.Collections.Immutable;
using DesignSpace.Core;
namespace DesignSpace.Engine;

/// <summary>Pure visual-state authoring operations; the base tree and non-target setters are retained.</summary>
public static class StateEditing
{
    private static readonly HashSet<string> Allowed=new(StringComparer.Ordinal)
    {
        "Opacity","Canvas.Left","Canvas.Top","Canvas.Right","Canvas.Bottom","Width","Height","Rotation",
        "Fill","Stroke","Background","Foreground","BorderBrush","StrokeThickness","CornerRadius","Visibility",
        "Margin","Padding","HorizontalAlignment","VerticalAlignment","Grid.Row","Grid.Column","Grid.RowSpan","Grid.ColumnSpan",
        "Text","Content","FontSize","FontFamily","FontWeight","FontStyle","TextWrapping","TextAlignment","TextDecorations"
    };
    public static DesignDocument SetProperty(DesignDocument document,string stateName,IEnumerable<Guid> targets,string property,string value)
    {
        if(!Allowed.Contains(property)) throw new InvalidOperationException("This property cannot be recorded in a visual state.");
        ArgumentNullException.ThrowIfNull(value);
        var state=document.States.FirstOrDefault(s=>s.Name==stateName) ?? throw new InvalidOperationException("The selected state no longer exists.");
        var index=DesignIndex.For(document.Root);
        var ids=targets.Where(id=>index.Find(id) is not null && !index.IsLocked(id)).ToHashSet();
        if(ids.Count==0) return document;
        // Use the same numeric/size validation as base property edits without modifying that base.
        var validationRoot=index.Transform(ids,n=>property=="Rotation" ? n with { Rotation=Numbers.Parse(value,double.NaN) } : n.Set(property,value));
        DocumentValidator.Validate(document with { Root=validationRoot });
        var setters=state.Setters.ToBuilder(); var changed=false;
        foreach(var id in ids)
        {
            if(index.Find(id)!.Get(DesignNode.NameKey,index.Find(id)!.Get("Name")).Length==0)
                throw new InvalidOperationException("Give an object a XAML name before recording state values.");
            var at=-1;for(var i=0;i<setters.Count;i++) if(setters[i].TargetId==id && setters[i].Property==property){at=i;break;}
            var next=new StateSetter(id,property,value);
            if(at>=0 && setters[at]==next) continue;
            if(at<0) setters.Add(next);else setters[at]=next;changed=true;
        }
        return changed ? document with { States=document.States.Replace(state,state with { Setters=setters.ToImmutable() }) } : document;
    }
    public static DesignDocument RemoveProperty(DesignDocument document,string stateName,IEnumerable<Guid> targets,string property)
    {
        var state=document.States.FirstOrDefault(s=>s.Name==stateName) ?? throw new InvalidOperationException("The selected state no longer exists.");
        var index=DesignIndex.For(document.Root);var ids=targets.Where(id=>!index.IsLocked(id)).ToHashSet();
        var setters=state.Setters.Where(s=>s.Property!=property || !ids.Contains(s.TargetId)).ToImmutableArray();
        return setters.Length==state.Setters.Length ? document : document with { States=document.States.Replace(state,state with { Setters=setters }) };
    }
}
