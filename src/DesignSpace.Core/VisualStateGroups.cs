using System.Collections.Immutable;
namespace DesignSpace.Core;

public sealed partial record DesignState
{
    // Version-1 files without group metadata retain their original group.
    public string Group { get; init; }=VisualStateGroups.DefaultName;
}
public sealed record DesignTransition(string? From,string? To,double Duration,string Easing="Linear");
public sealed record DesignStateGroup(string Name,ImmutableArray<DesignTransition> Transitions);
public sealed partial record DesignDocument
{
    public ImmutableArray<DesignStateGroup> StateGroups { get; init; }=[];
}

/// <summary>Declared groups, followed by legacy implicit groups in document order.</summary>
public static class VisualStateGroups
{
    public const string DefaultName="DesignSpaceStates";
    public static ImmutableArray<DesignStateGroup> Get(DesignDocument document)
    {
        var groups=document.StateGroups.ToBuilder();
        var names=groups.Select(g=>g.Name).ToHashSet(StringComparer.Ordinal);
        foreach(var state in document.States) if(names.Add(state.Group)) groups.Add(new(state.Group,[]));
        return groups.ToImmutable();
    }
    /// <summary>Specific pair &gt; destination &gt; source &gt; default. First equal match wins.</summary>
    public static DesignTransition? Match(DesignStateGroup group,string? from,string? to)
    {
        DesignTransition? best=null;var score=-1;
        foreach(var transition in group.Transitions)
        {
            if(transition.From is not null && transition.From!=from || transition.To is not null && transition.To!=to) continue;
            var candidate=(transition.From is null ? 0 : 1)+(transition.To is null ? 0 : 2);
            if(candidate>score){score=candidate;best=transition;}
        }
        return best;
    }
}
public static partial class DocumentValidator
{
    private static void ValidateStateGroups(DesignDocument doc)
    {
        if(doc.StateGroups.IsDefault || doc.StateGroups.Length>256 || doc.States.Length>4096) throw new InvalidDataException("Visual-state collections exceed their limits or are null.");
        var declared=new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in doc.StateGroups)
        {
            if(group is null || group.Name is null || !Identifier().IsMatch(group.Name) || !declared.Add(group.Name) || group.Transitions.IsDefault || group.Transitions.Length>4096)
                throw new InvalidDataException("Invalid or duplicate visual-state group.");
        }
        var members=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        foreach(var state in doc.States)
        {
            if(state is null || state.Name is null || state.Group is null || !Identifier().IsMatch(state.Group)) throw new InvalidDataException("Invalid visual-state group membership.");
            if(!members.TryGetValue(state.Group,out var names)) members.Add(state.Group,names=new(StringComparer.Ordinal));
            names.Add(state.Name);
        }
        if(declared.Union(members.Keys).Count()>256) throw new InvalidDataException("Too many visual-state groups.");
        foreach(var group in doc.StateGroups)
        {
            var pairs=new HashSet<(string?,string?)>();
            foreach(var t in group.Transitions)
            {
                if(t is null || !pairs.Add((t.From,t.To)) || !double.IsFinite(t.Duration) || t.Duration<0 || t.Duration>86400 || t.Easing is not ("Linear" or "EaseIn" or "EaseOut" or "EaseInOut"))
                    throw new InvalidDataException("Invalid, duplicate or unsupported visual transition.");
                if((t.From is not null && (!members.TryGetValue(group.Name,out var from) || !from.Contains(t.From))) ||
                   (t.To is not null && (!members.TryGetValue(group.Name,out var to) || !to.Contains(t.To))))
                    throw new InvalidDataException("Transitions must reference states in their own group.");
            }
        }
    }
}
