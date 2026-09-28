# Multi-document workspace

## Open, switch and arrange documents

**New** and **Open** create document tabs rather than replacing the current design. The Project panel lists the same open documents. Selecting a tab restores its document, selected objects, bounded undo/redo history, source draft, editor panels, timeline position and viewport. A single active control tree and renderer are reused; inactive tabs do not create hidden application windows or composition loops.

Drag a tab to reorder it, or use its context menu's Move left/right commands. Pin a document to protect it from **Close other unpinned documents**. Pinning does not make a document read-only or prevent its own Close command. Overflow arrows keep document tabs and top-level commands accessible without a visible horizontal scrollbar. Document file names are unique within a workspace; opening the same name again creates a separately named copy, not a live file watcher.

The toolstrip is beside the design artboard. Design, Split and XAML modes remain available in every document. The split-orientation button switches between left/right and top/bottom layouts; drag the splitter to change the ratio. Mode, orientation, ratio and viewport are retained per document. **F6** switches Design and Animation workspace profiles, with a larger timeline in Animation. Each profile retains pane dimensions during the session; the active profile and layout are included in local recovery. This is not a native multi-window docking implementation.

## Save, close and recover

**Save** writes the active native `.designspace` document. An unapplied XAML draft must validate and apply before a document export can proceed. A successful asynchronous save marks the specific document version that was written, not whatever version happens to be active when the file operation returns.

**Save all** writes changed documents in sequence, applies valid primary XAML drafts, and restores the original active tab afterward. Cancellation stops the sequence. In a browser, each document is a separate download; browser download permissions and storage quotas still apply. Auxiliary panel drafts are not silently applied to native document files.

Closing a document that has changes or drafts offers **Save**, **Discard**, and **Cancel**. Save must complete before the tab is removed, and remaining auxiliary drafts prevent a silent close. Discard explicitly abandons that tab's document changes and drafts. Closing the last document leaves a clean blank tab instead of an unusable empty application.

**Save workspace** writes one `.designspace-workspace` file containing all documents, identities, selections, dirty flags, pin/order metadata, editor view state and retained source/panel drafts. Invalid drafts are retained as inert text, not parsed or executed. Saving a workspace does not mark the individual native files as separately saved. Open a workspace file to replace the open set, after validation and a confirmation when the existing documents need attention.

Local recovery retains the same document/editor data, plus the active pane layout and workspace profile. It migrates the previous single-document recovery envelope. Recovery is origin/profile-local and quota-dependent, not an encrypted backup or cloud service. Use Save workspace for a portable durable copy. Undo/redo checkpoints are in-memory only: history survives tab switching but is intentionally not serialized into recovery or workspace files. In-flight pointer gestures, active state transitions and animation playback are stopped on a document switch rather than resumed in a different document.

## Retained panel drafts

XAML source, template source, sample-data JSON, stroke settings, transition-rule inputs and timing fields have document-local draft state. Their original values and revision relationship are retained where applicable. A draft already stale before switching remains stale afterward; switching away and back does not authorize it to overwrite newer work. Unchanged generated template starter text and unchanged timing fields do not by themselves make a document require saving.

These panels remain explicit authoring tools, not a continuously executing source environment. Apply their drafts before saving a native document to include those edits in the design. Save workspace preserves unapplied inputs. Asset filters, general resources/layout authoring inputs, search text, scroll positions and every transient selection within every panel are not all independent persisted workspaces in this increment.

## Shortcuts

| Command | Shortcut |
| --- | --- |
| New document | Ctrl+N |
| Open document/workspace | Ctrl+O |
| Save active native document | Ctrl+S |
| Save all changed documents | Ctrl+Shift+S |
| Next / previous document | Ctrl+Tab / Ctrl+Shift+Tab |
| Close active document | Ctrl+F4 |
| Switch Design / Animation workspace | F6 |
| Apply primary XAML draft | Ctrl+Enter |

The Window menu and tab controls expose alternatives when a browser or operating system reserves a shortcut. Browser tab-management shortcuts cannot be guaranteed to reach the application.

## Reusable contracts

`DocumentWorkspace` and `DesignSessionCheckpoint` are Engine APIs without Uno or Skia dependencies. `WorkspaceEditorState`, `WorkspaceFile`, `DesignerPanelDraft` and `DesignWorkspaceSnapshot` are inert Core contracts. `WorkspaceCodec` uses source-generated JSON metadata so persistence is verified in a trimmed executable with reflection serialization disabled.

```csharp
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;

using var workspace = new DocumentWorkspace(DesignDocument.Empty());
var first = workspace.ActiveDocumentId;
workspace.Session.Add("Button", new DRect(40, 50, 140, 36));

workspace.Add(DesignDocument.Empty(), "Other.designspace", unsaved: true);
workspace.Session.Add("Rectangle", new DRect(20, 20, 80, 80));
workspace.Activate(first); // Restores the first document's selection and history.
workspace.Session.Undo(); // Does not affect Other.designspace.

string json = WorkspaceCodec.Write(workspace.Capture());
var snapshot = WorkspaceCodec.Read(json);
workspace.Replace(snapshot); // Validates the entire snapshot before replacing live state.
```

A host with asynchronous edit commands can capture `workspace.CaptureOperation()` before awaiting input, then call `workspace.RequireCurrent(context)` before mutating. A different document, revision or selection invalidates that operation. The workbench uses this for clipboard cut/paste and image import so a late completion cannot edit a different tab. A host performing a save should instead use `MarkSaved(documentId, writtenVersion)` to identify exactly what was written.

`DocumentTabsControl` exposes typed events for selection, close, pin and reorder. It does not read files itself. `IWorkspaceDraftEditor` transfers inert panel state; its host owns revision rebasing and persistence. `CommandBarScroller` is reusable independently of the complete designer. One stable `DesignSession` serves the active workbench; its monotonic revision prevents late callbacks from a previously displayed document from committing against a reused revision number.

These objects are UI-thread-owned, not concurrent collaboration services. Document/history trees are immutable and structurally shared, but validation, tab activation and serialization still perform work proportional to the relevant data. No constant-time tab switch or whole-application memory guarantee is implied.

## Budgets and qualification

A workspace supports up to 32 documents, 64,000 current model nodes in total, and a 16 MiB UTF-8 workspace file. Each document also retains its existing node, markup and geometry limits. Primary source drafts are limited to 8 MiB of characters; panel counts, fields and values are bounded. These limits govern current snapshots, not all allocator overhead or every historical document version retained in undo stacks. A host handling very large designs may reduce `DesignSession.HistoryLimit` and save/close inactive documents.

CI verifies isolated history, savepoints, atomic recovery, draft data, limits, late-operation rejection, trimmed persistence, and real browser input/download/upload workflows. Desktop compilation and Chromium software-backed rendering are separate from native dialog, accessibility, physical-GPU and cross-browser runtime qualification.

Microsoft's [Blend overview](https://learn.microsoft.com/en-us/visualstudio/xaml-tools/creating-a-ui-by-using-blend-for-visual-studio?view=vs-2022) and [XAML Designer options](https://learn.microsoft.com/en-us/visualstudio/xaml-tools/xaml-designer?view=vs-2022) inform the workspace organization and split-view controls. This does not establish pixel equivalence. A pixel-exact claim would require a specified Blend build/theme, fonts, DPI, window dimensions and matched reference scenes, followed by image comparisons and functional differential testing. That qualification has not been performed. Full Visual Studio solution loading/build/debugging, side-by-side multiple active document surfaces, native floating windows, advanced XAML semantics and the broader remaining boundaries in [compatibility](compatibility.md) are not supplied by this document-workspace increment.
