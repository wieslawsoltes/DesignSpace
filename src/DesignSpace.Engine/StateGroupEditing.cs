using System.Collections.Immutable;
using System.Xml;
using DesignSpace.Core;
namespace DesignSpace.Engine;

/// <summary>Atomic, portable state/group/transition authoring. No UI or clocks are owned here.</summary>
public static class StateGroupEditing
{
    private static DesignDocument Checked(DesignDocument document){DocumentValidator.Validate(document);return document;}
    private static void Name(string name){ArgumentException.ThrowIfNullOrWhiteSpace(name);XmlConvert.VerifyNCName(name);}
    public static DesignDocument AddGroup(DesignDocument document,string name)
    {
        Name(name);if(VisualStateGroups.Get(document).Any(g=>g.Name==name)) throw new InvalidOperationException("Choose a unique group name.");
        return Checked(document with { StateGroups=document.StateGroups.Add(new(name,[])) });
    }
    public static DesignDocument RenameGroup(DesignDocument document,string name,string replacement)
    {
        Name(replacement);var groups=VisualStateGroups.Get(document);var group=groups.FirstOrDefault(g=>g.Name==name) ?? throw new InvalidOperationException("The group no longer exists.");
        if(name==replacement)return document;
        if(groups.Any(g=>g.Name==replacement))throw new InvalidOperationException("Choose a unique group name.");
        return Checked(document with { StateGroups=groups.Replace(group,group with { Name=replacement }),States=document.States.Select(s=>s.Group==name ? s with { Group=replacement } : s).ToImmutableArray() });
    }
    public static DesignDocument RemoveGroup(DesignDocument document,string name)=>Checked(document with
    {
        StateGroups=document.StateGroups.Where(g=>g.Name!=name).ToImmutableArray(),States=document.States.Where(s=>s.Group!=name).ToImmutableArray()
    });
    public static DesignDocument AddState(DesignDocument document,string group,string name)
    {
        Name(name);if(document.States.Any(s=>s.Name==name))throw new InvalidOperationException("Choose a unique state name.");
        if(!VisualStateGroups.Get(document).Any(g=>g.Name==group)) document=AddGroup(document,group);
        return Checked(document with { States=document.States.Add(new(name,[]) { Group=group }) });
    }
    public static DesignDocument RenameState(DesignDocument document,string name,string replacement)
    {
        Name(replacement);var state=document.States.FirstOrDefault(s=>s.Name==name) ?? throw new InvalidOperationException("The state no longer exists.");
        if(name==replacement)return document;
        if(document.States.Any(s=>s.Name==replacement))throw new InvalidOperationException("Choose a unique state name.");
        return Checked(document with { States=document.States.Replace(state,state with { Name=replacement }),StateGroups=document.StateGroups.Select(g=>g.Name!=state.Group ? g : g with
        {
            Transitions=g.Transitions.Select(t=>t with { From=t.From==name ? replacement : t.From,To=t.To==name ? replacement : t.To }).ToImmutableArray()
        }).ToImmutableArray() });
    }
    public static DesignDocument RemoveState(DesignDocument document,string name)=>Checked(document with
    {
        States=document.States.Where(s=>s.Name!=name).ToImmutableArray(),StateGroups=document.StateGroups.Select(g=>g with
        { Transitions=g.Transitions.Where(t=>t.From!=name && t.To!=name).ToImmutableArray() }).ToImmutableArray()
    });
    public static DesignDocument SetTransition(DesignDocument document,string groupName,DesignTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var groups=VisualStateGroups.Get(document);var group=groups.FirstOrDefault(g=>g.Name==groupName) ?? throw new InvalidOperationException("The group no longer exists.");
        var old=group.Transitions.FirstOrDefault(t=>t.From==transition.From && t.To==transition.To);
        if(old==transition)return document;
        var next=group with { Transitions=old is null ? group.Transitions.Add(transition) : group.Transitions.Replace(old,transition) };
        return Checked(document with { StateGroups=groups.Replace(group,next) });
    }
    public static DesignDocument RemoveTransition(DesignDocument document,string groupName,string? from,string? to)
    {
        var group=document.StateGroups.FirstOrDefault(g=>g.Name==groupName);if(group is null)return document;
        var transitions=group.Transitions.Where(t=>t.From!=from || t.To!=to).ToImmutableArray();if(transitions.Length==group.Transitions.Length)return document;
        return Checked(document with { StateGroups=document.StateGroups.Replace(group,group with { Transitions=transitions }) });
    }
}
