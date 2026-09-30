using System.Collections.Immutable;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

/// <summary>Transient authoring-preview input. Never stored in the design, XAML or undo history.</summary>
public sealed record PreviewInteractionState
{
    public Guid? Hovered { get; init; }
    public Guid? Pressed { get; init; }
    public Guid? Focused { get; init; }
    public ImmutableDictionary<Guid,bool?> Checked { get; init; }=ImmutableDictionary<Guid,bool?>.Empty;
    public static PreviewInteractionState Empty { get; }=new();
}
/// <summary>Host-owned single-preview cache. Repeated identical input/root reuses the whole expansion.
/// Call Clear when leaving preview, changing documents or disposing a host.</summary>
public sealed class DesignPreviewContext
{
    private DesignNode? _root;
    private PreviewResult? _result;
    public PreviewInteractionState State { get; private set; }=PreviewInteractionState.Empty;
    public long Builds { get; private set; }
    public IReadOnlyList<string> Diagnostics=>_result?.Diagnostics??Array.Empty<string>();
    public bool Update(PreviewInteractionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if(state.Checked is null||state.Checked.Count>DocumentValidator.MaxNodes||state.Checked.Keys.Any(id=>id==Guid.Empty)||state.Hovered==Guid.Empty||state.Pressed==Guid.Empty||state.Focused==Guid.Empty)
            throw new ArgumentException("Invalid preview interaction state.",nameof(state));
        if(state==State)return false;
        State=state;_root=null;_result=null;return true;
    }
    public DesignNode Resolve(DesignNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if(ReferenceEquals(root,_root))return _result!.Root;
        _result=State==PreviewInteractionState.Empty?DesignPreview.Get(root):DesignPreview.Build(root,State);
        _root=root;Builds++;return _result.Root;
    }
    public void Clear(){State=PreviewInteractionState.Empty;_root=null;_result=null;}
}
