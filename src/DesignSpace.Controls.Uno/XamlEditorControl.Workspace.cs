using DesignSpace.Core;
namespace DesignSpace.Controls.Uno;
public sealed partial class XamlEditorControl
{
    /// <summary>Starts a different source context; failed export cannot leave the previous document's source attached.</summary>
    public void BeginDocument(DesignDocument document,long revision)
    {
        _syncing=true;_editor.Text="";_canonical="";_baseline=null;_baseRevision=revision;_syncing=false;
        Synchronize(document,revision,true);
    }
}
