using System.Collections.Immutable;
using DesignSpace.Core;
namespace DesignSpace.Engine;

/// <summary>Immutable in-memory session checkpoint. History is retained by reference, never serialized into files.</summary>
public sealed class DesignSessionCheckpoint
{
    public DesignDocument Document { get; }
    public ImmutableHashSet<Guid> Selection { get; }
    public bool IsDirty=>!ReferenceEquals(Document,Saved);
    public int UndoCount=>Undo.Length;
    public int RedoCount=>Redo.Length;
    internal DesignDocument? Saved { get; }
    internal ImmutableArray<DesignSession.HistoryEntry> Undo { get; }
    internal ImmutableArray<DesignSession.HistoryEntry> Redo { get; }
    internal DesignSessionCheckpoint(DesignDocument document,DesignDocument? saved,ImmutableHashSet<Guid> selection,
        ImmutableArray<DesignSession.HistoryEntry> undo,ImmutableArray<DesignSession.HistoryEntry> redo)
        =>(Document,Saved,Selection,Undo,Redo)=(document,saved,selection,undo,redo);
    internal DesignSessionCheckpoint WithSaved(DesignDocument saved)=>new(Document,saved,Selection,Undo,Redo);
    internal static DesignSessionCheckpoint Open(DesignDocument document,bool dirty,ImmutableArray<Guid> selection)
        =>new(document,dirty ? null : document,selection.ToImmutableHashSet(),[],[]);
}
public sealed partial class DesignSession
{
    public DesignSessionCheckpoint CaptureCheckpoint()=>new(Document,_saved,Selection,_undo.ToImmutableArray(),_redo.ToImmutableArray());
    public void RestoreCheckpoint(DesignSessionCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);DocumentValidator.Validate(checkpoint.Document);
        Document=checkpoint.Document;_saved=checkpoint.Saved;Selection=checkpoint.Selection;
        _undo.Clear();_undo.AddRange(checkpoint.Undo);_redo.Clear();
        foreach(var item in checkpoint.Redo.Reverse())_redo.Push(item);
        // Revisions never move backwards across documents. Late UI callbacks stay stale.
        Changed();
    }
    /// <summary>Marks only the bytes actually saved, even if the document changed while a picker was open.</summary>
    public void MarkSavedVersion(DesignDocument savedVersion)
    {
        ArgumentNullException.ThrowIfNull(savedVersion);_saved=savedVersion;DocumentChanged?.Invoke(this,EventArgs.Empty);
    }
}
