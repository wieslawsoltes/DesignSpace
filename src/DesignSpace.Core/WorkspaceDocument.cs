using System.Collections.Immutable;
namespace DesignSpace.Core;

/// <summary>Inert panel input retained with a document; never interpreted as executable code.</summary>
public sealed record DesignerPanelDraft
{
    public ImmutableDictionary<string,string> Values { get; init; }=ImmutableDictionary<string,string>.Empty;
    public ImmutableDictionary<string,string> Originals { get; init; }=ImmutableDictionary<string,string>.Empty;
    public ImmutableArray<Guid> Targets { get; init; }=[];
    public bool HasChanges { get; init; }
    public bool MatchesDesign { get; init; }=true;
}
public sealed record WorkspaceEditorState
{
    public string? SourceDraft { get; init; }
    public bool DraftMatchesDesign { get; init; }=true;
    public string Mode { get; init; }="Design";
    public string SplitOrientation { get; init; }="Vertical";
    public Guid? StoryboardId { get; init; }
    public double TimelineTime { get; init; }
    public double SplitRatio { get; init; }=.5;
    public double Zoom { get; init; }=.8;
    public double PanX { get; init; }=48;
    public double PanY { get; init; }=60;
    public bool HasViewport { get; init; }
    public bool ShowGrid { get; init; }
    public bool ShowRulers { get; init; }=true;
    public bool SnapToGrid { get; init; }=true;
    public double GridSize { get; init; }=8;
    public bool SnapToSnaplines { get; init; }
    public double SnapTolerance { get; init; }=6;
    public double DefaultMargin { get; init; }=8;
    public double DefaultPadding { get; init; }=8;
    public bool RenderEffects { get; init; }=true;
    public double EffectsZoomThreshold { get; init; }=8;
    public ImmutableDictionary<string,DesignerPanelDraft> Panels { get; init; }=ImmutableDictionary<string,DesignerPanelDraft>.Empty;
    public bool HasDrafts=>SourceDraft is not null || Panels.Values.Any(p=>p.HasChanges);
}
public sealed record WorkspaceFile(Guid Id,DesignDocument Document,string FileName,bool HasUnsavedChanges,
    bool IsPinned,WorkspaceEditorState Editor,ImmutableArray<Guid> Selection);
public sealed record DesignWorkspaceSnapshot(int FormatVersion,Guid ActiveDocumentId,ImmutableArray<WorkspaceFile> Documents);

public static class WorkspaceValidator
{
    public const int MaxDocuments=32,MaxTotalNodes=64000,MaxCharacters=16*1024*1024;
    public static void Validate(DesignWorkspaceSnapshot workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if(workspace.FormatVersion!=1 || workspace.Documents.IsDefaultOrEmpty || workspace.Documents.Length>MaxDocuments)
            throw new InvalidDataException("A workspace must contain 1–32 supported documents.");
        var ids=new HashSet<Guid>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var nodes=0;long characters=0;
        foreach(var file in workspace.Documents)
        {
            if(file is null || file.Id==Guid.Empty || !ids.Add(file.Id))throw new InvalidDataException("Invalid or duplicate workspace document identity.");
            ValidateFileName(file.FileName);if(!names.Add(file.FileName))throw new InvalidDataException("Workspace file names must be unique.");DocumentValidator.Validate(file.Document);ValidateEditor(file.Editor);
            if(file.Selection.IsDefault)throw new InvalidDataException("Document selection must not be null.");
            var index=DesignIndex.For(file.Document.Root);
            if(file.Selection.Distinct().Count()!=file.Selection.Length || file.Selection.Any(id=>index.Find(id) is null))throw new InvalidDataException("Invalid workspace selection.");
            nodes+=index.Nodes.Count;if(nodes>MaxTotalNodes)throw new InvalidDataException("Workspace node budget exceeded.");
            characters+=file.Editor.SourceDraft?.Length ?? 0;
            foreach(var panel in file.Editor.Panels.Values)characters+=panel.Values.Sum(p=>(long)p.Key.Length+p.Value.Length)+panel.Originals.Sum(p=>(long)p.Key.Length+p.Value.Length);
            foreach(var node in index.Nodes.Values)characters+=node.TextContent.Length+node.Properties.Sum(p=>(long)p.Key.Length+p.Value.Length)+node.PropertyElements.Sum(p=>(long)p.Length);
            if(characters>MaxCharacters)throw new InvalidDataException("Workspace text budget exceeded.");
        }
        if(!ids.Contains(workspace.ActiveDocumentId))throw new InvalidDataException("The active workspace document is missing.");
    }
    public static void ValidateFileName(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>255||name is "." or ".."||name.IndexOfAny(['/', '\\', ':','*','?','"','<','>','|'])>=0||name.Any(char.IsControl))
            throw new InvalidDataException("Use a local file name without path separators or reserved characters.");
    }
    public static void ValidateEditor(WorkspaceEditorState state)
    {
        if(state is null||state.Panels is null||state.Panels.Count>16)throw new InvalidDataException("Invalid editor state.");
        if(state.Mode is not ("Design" or "Split" or "XAML") || state.SplitOrientation is not ("Horizontal" or "Vertical"))throw new InvalidDataException("Unknown editor view.");
        if(!double.IsFinite(state.Zoom)||state.Zoom<.1||state.Zoom>8||!double.IsFinite(state.PanX)||!double.IsFinite(state.PanY)||Math.Abs(state.PanX)>1e10||Math.Abs(state.PanY)>1e10||!double.IsFinite(state.GridSize)||state.GridSize<1||state.GridSize>10000||!double.IsFinite(state.SplitRatio)||state.SplitRatio<.1||state.SplitRatio>.9)throw new InvalidDataException("Invalid viewport or split dimensions.");
        new ArtboardSettings { GridSize=state.GridSize,SnapTolerance=state.SnapTolerance,DefaultMargin=state.DefaultMargin,DefaultPadding=state.DefaultPadding,RenderEffects=state.RenderEffects,EffectsZoomThreshold=state.EffectsZoomThreshold }.Validate();
        if(!double.IsFinite(state.TimelineTime)||state.TimelineTime<0||state.TimelineTime>86400000)throw new InvalidDataException("Invalid timeline position.");
        if(state.SourceDraft?.Length>8*1024*1024)throw new InvalidDataException("Source draft exceeds the input limit.");
        foreach(var (key,panel) in state.Panels)
        {
            if(key.Length>64||panel is null||panel.Values is null||panel.Originals is null||panel.Targets.IsDefault||panel.Values.Count>64||panel.Originals.Count>64||panel.Targets.Length>DocumentValidator.MaxNodes)throw new InvalidDataException("Invalid panel draft.");
            if(panel.Values.Concat(panel.Originals).Any(p=>p.Key.Length>128||p.Value is null||p.Value.Length>8*1024*1024))throw new InvalidDataException("Panel draft exceeds the input limit.");
        }
    }
}
