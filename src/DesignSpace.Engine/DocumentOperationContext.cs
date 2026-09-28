using System.Collections.Immutable;
namespace DesignSpace.Engine;

/// <summary>Captures the document and selection that initiated an asynchronous edit.</summary>
public sealed record DocumentOperationContext(Guid DocumentId,long Revision,ImmutableHashSet<Guid> Selection);
public sealed partial class DocumentWorkspace
{
    public DocumentOperationContext CaptureOperation()=>new(ActiveDocumentId,Session.Revision,Session.Selection);
    /// <summary>Prevents an awaited clipboard or import operation from modifying another document or newer selection.</summary>
    public void RequireCurrent(DocumentOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if(context.DocumentId!=ActiveDocumentId||context.Revision!=Session.Revision||!context.Selection.SetEquals(Session.Selection))
            throw new InvalidOperationException("The document or selection changed while the operation was pending. Nothing was applied; repeat the command in the intended document.");
    }
}
