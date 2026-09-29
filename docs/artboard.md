# Artboard snaplines and preferences

The designer provides edge and center snaplines, default margin/padding guides, resize snapping, and per-document artboard preferences. These are working authoring features, not a claim of full or pixel-exact Microsoft Blend parity. Microsoft's [Artboard options](https://learn.microsoft.com/en-us/visualstudio/xaml-tools/xaml-designer?view=vs-2022#artboard-blend-only) document the corresponding grid, snapline, margin and padding concepts.

## Editing

Use **Guides** above the artboard to toggle snapline snapping. **Snap** toggles grid snapping independently; **Grid** toggles grid visibility. **Options** opens the Artboard settings panel. The command strip scrolls horizontally when it cannot fit all actions.

With Guides enabled, drag a Canvas child near a visible sibling's left/center/right or top/center/bottom coordinate. The closest matching coordinate wins. Alignment guides are pink. Green distance guides mark the configured margin between facing objects in overlapping rows or columns, or padding inside the parent content area. These guides do not assign Margin or Padding properties to the document. Existing container padding is respected; the configured guide padding is measured inside that content area.

A multi-selection with a common Canvas parent snaps as a group: its union bounds choose the correction, and all movable members retain their relative positions. Locked siblings can be alignment references but do not move. Hidden/collapsed siblings and generated template visuals are not snap targets. The current selection is excluded from the target index.

Dragging a resize handle snaps only its moving edge or edges; the opposite edge remains fixed. Hold **Alt** during the gesture to bypass both grid and snaplines. A matched snapline takes priority over grid snapping on that axis; the other axis can still use the grid. Shift-resizing preserves the existing aspect-ratio workflow and does not engage snaplines.

The base document and source remain unchanged during a drag. Pointer release creates one geometry transaction; Undo restores it. Escape, a document/selection change, or returning within the three-logical-pixel pointer dead zone cancels the pending geometry. A click without movement never snaps an object merely because it is near a guide. Guides disappear after completion/cancellation.

## Preferences and persistence

The Artboard panel edits an inert draft. Apply validates all values before changing preferences; Reload discards the draft. A toolbar change while a conflicting draft is open makes that draft stale, rather than silently replacing the toolbar setting.

Grid spacing accepts 1–10000 design units, snap tolerance 1–32 logical screen pixels, and default margin/padding 0–10000 design units. The default tolerance is 6 pixels; default spacing, margin and padding are 8 design units. A zero margin or padding disables that spacing guide. Snaplines default to **off** to preserve the earlier grid-only behavior of existing workspaces.

Applied preferences and unapplied text belong to their document tab. Workspace export and local recovery retain both separately. Native design/XAML exports contain geometry, not editor preferences. Preference changes do not add geometry revisions or undo entries. Save workspace for a durable copy; browser-origin recovery is not a backup.

Version-1 workspace files that omit the newly added tolerance/margin/padding fields receive the documented defaults. The codec distinguishes omission from an explicitly invalid value: zero tolerance, nonfinite values, invalid types and null metadata are not silently repaired. Source-generated serialization and trimmed tests cover this migration.

## Reusable APIs

`ArtboardSettings` lives in Core. `SnaplineIndex` lives in Engine and has no Skia/Uno dependency. The caller supplies rectangles in one coordinate system and keeps the immutable index for the duration of a gesture:

```csharp
using DesignSpace.Core;
using DesignSpace.Engine;

var index = new SnaplineIndex(
    targets: [new DRect(300, 140, 100, 80)],
    container: new DRect(0, 0, 960, 560),
    margin: 8, padding: 12);

var moved = index.Move(new DRect(297, 300, 60, 50), 6, 6);
// moved.Bounds.X == 300; XGuide describes the alignment.
var resized = index.Resize(new DRect(100, 300, 197, 50),
    SnapEdges.Right, 6, 6);
// The left edge remains 100; the right edge becomes 300.
```

The separate axis tolerances support positive nonuniform scale: divide screen tolerance by viewport zoom and the relevant parent scale. The Uno surface restricts its gesture integration to a common Canvas parent with an axis-aligned, non-reflected world transform. Rotated/skewed/reflected parent spaces retain ordinary editing without misleading snaplines. Rotated children use visual bounding boxes for movement; the existing resize restrictions remain.

`SnaplineResult` is a value type with optional X/Y guides. `SnaplineRenderer` draws into a host-owned Skia canvas and restores its state. It owns/disposes its paints and label font, but not the host canvas. `ArtboardSettingsControl` accepts host read/apply callbacks and implements `IWorkspaceDraftEditor`; it does not own a DesignSession. `DesignerSurface.ApplyArtboardSettings` cancels any active gesture before applying validated settings.

## Performance and verification

The index sorts target coordinates once per gesture and binary-searches each query range. Equal-coordinate alignment buckets coalesce their guide extents, preventing a column of aligned controls from becoming repeated identical alignment candidates. Spacing candidates retain their perpendicular overlap information. Dense overlapping spacing ranges can still require scanning multiple candidates; this is not a worst-case constant-time claim.

Warm geometry queries return structs and reuse the sorted arrays. A regression constructs 20,000 targets and measures 10,000 warmed movement queries. Other tests compare 500 fixed-seed randomized proposals with an independent exhaustive alignment oracle and check guide coordinates, margins, padding, resize edges, ties, invalid settings and exact host-canvas pixels. Those allocation measurements do not cover the surrounding immutable document overlays, design layout, typography or GPU submission.

The browser suite uses actual file-picker, pointer and keyboard input. Read-only diagnostics observe model/preview separation, target counts, index reuse, history, guides and saved workspace data. Browser screenshots use software-backed Chromium, not physical GPU or native desktop qualification. Inspect the artifacts for the exact commit rather than interpreting the presence of tests as a pass.

Remaining areas include authored ruler guides, text-baseline snapping, smart equal-distance distribution, rotated/skewed parent snapping, general Grid/StackPanel reparenting, native multi-window docking and the broader compatibility matrix. No matched Blend build/theme/DPI screenshot corpus has been verified, so pixel-exact Blend UI equivalence is not asserted.
