# Compatibility matrix — 0.1 preview

DesignSpace is an independent Blend-style designer. Familiar panels do not imply complete or pixel-exact Microsoft Blend, WPF, WinUI or Visual Studio compatibility. This matrix separates implemented authoring from preserved-only markup and unqualified runtime behavior.

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Workspace | Dark tools, Assets, Project, States, Data, Layout, Objects, Properties, Resources, Templates, Timing, Paths and design/split/source views | Desktop-oriented density; no pixel-exact Blend, complete accessibility or mobile/touch qualification |
| Docking | Resizable columns/stacked panes, tabs, hide/show/reset and in-application floating/dragging | No arbitrary docking graph, native floating windows, cross-window transfer or persisted floating coordinates |
| Visual editing | Creation, selection, Canvas move/resize, snapping, grouping, duplication, alignment, distribution, z-order, history and Canvas reparenting APIs | Grouping requires contiguous Canvas siblings; arbitrary panel reparenting and complete transformed editing remain unfinished |
| Picking | Inherited transforms, ellipse/path fill and stroke checks, fill-rule holes, own/ancestor geometry clipping, visibility and lock inheritance | Curves use bounded adaptive tessellation for picking; this is not pixel-exact runtime stroke/effect picking |
| Geometry | Pen and Pencil drawing, Direct Selection anchors/tangents, exact Bezier subdivision, point/segment removal, line/curve conversion, shape conversion, compound paths, fill rules and native vector booleans | Single-point editing; no multi-anchor selection, tangent-mode persistence, pressure-sensitive brushes, arc-radius handles or mesh editing; Divide takes two shapes |
| Geometry formats | Finite M/L/H/V/C/S/Q/T/A/Z and F0/F1 data; common PathGeometry/PathFigure/segment objects; explicit Line endpoints; Auto path bounds; None/Fill/Uniform/UniformToFill | No arbitrary geometry groups, per-segment stroke/join metadata, geometry transforms or reference resolution; unsupported object markup remains preserved, not silently converted |
| Geometric clipping | Make a clip from the top selected vector shape, release/undo it, and render/pick own and ancestor clips; literal path and simple RectangleGeometry/EllipseGeometry/PathGeometry clips | Geometry clipping is not an opacity mask; arbitrary GeometryGroup, animated clips and all runtime effects are unfinished |
| Layout | Cached measure/arrange, intrinsic Auto and constrained weighted Star Grid tracks, spans, min/max sizing, margins, padding, StackPanel spacing and Canvas anchors | No complete dependency-property layout system, shared-size groups, layout rounding, every cyclic/intrinsic constraint or runtime differential qualification |
| Typography | HarfBuzz glyph shaping, grapheme-safe wrapping, explicit newlines, alignment, underline, synthetic bold/italic, line height and shared styled measure/draw metrics | No complete mixed-direction paragraph engine, fallback chain, real face selection for every weight, rich inlines, OpenType authoring or all runtime-equivalent metrics |
| Typography caching | Weighted least-recently-used glyph and wrapped-line caches; repeated paragraphs reuse line breaks; reusable drawing paint | Budgets estimate retained resources rather than process memory; first-time shaping/wrapping still does CPU work |
| Brushes | ARGB color editor, basic linear/radial gradients and local resources; recorded scalar state brushes supersede inline base brushes during preview | Not every mapping mode, spread, transform, inheritance or resource-resolution rule |
| Images | Copy-imported embedded PNG/JPEG/WebP/GIF raster data, bounded decode cache, stretching and PNG export | First raster frame only; no remote fetching, project-relative asset resolver, full ImageBrush or managed image asset library |
| Controls/templates | Skia control previews, scoped implicit/explicit Style and BasedOn resolution, inert ControlTemplate expansion, TemplateBinding and ContentPresenter preview, template source authoring | Not native controls in the artboard; no arbitrary control assembly execution, full template namescopes, triggers, behaviors, item templates or complete Fluent runtime |
| Animation | Numeric position/size/opacity/rotation tracks, easing, key movement, local scrubbing, delay, speed, auto-reverse, repeat count/duration/forever, HoldEnd/Stop and safe duration scaling | Flat storyboard clock only; no nested/per-child timing, acceleration ratios, additive/cumulative or object/color animation, expression editor or runtime differential qualification |
| States | Create/delete/capture, effective-value preview, explicit property recording, reset, undo and source/native persistence without changing the base tree | No transition/trigger editor, multiple concurrent state groups or automatic template VSM runtime; inline gradient authoring remains a base operation |
| Data | JSON sample data and simple Binding property paths | No CLR providers, converters, expressions, collection templates, service adapters or full binding runtime |
| XAML | Inert validated import, isolated source drafts, named identity reconciliation, supported timing/state export and retained property elements | Not lossless XML: formatting, comments and mixed text are not preserved exactly; export is Uno/WinUI-oriented, not qualified across WPF versions |
| Files/project | Native `.designspace`, XAML and PNG export, consistent browser upload/copy import and local recovery | No solution/project compilation, debugger, multi-document project system, Git integration or cloud synchronization |
| Collaboration | Local editing requires no service/account | No multi-user synchronization, authentication, permissions or enterprise administration |

## Vector authoring

**Pen (P)** creates a draft: click for corner points and drag for cubic tangents. Click the first point to close, press Enter to finish an open path, or Escape to discard the draft. Backspace removes the last draft point. The document changes once on completion. **Pencil (Y)** records and simplifies one freehand contour; it remains an editable path.

**Direct Selection (A)** moves one anchor or tangent handle at a time. Anchors move adjacent handles; tangent dragging links an adjacent cubic handle unless Alt is held. Arrow keys nudge the selected point, Shift increases the step, and Delete removes it. Click an edge and use Delete to remove that segment without connecting across the gap. Pen clicks on existing anchors remove them; clicks on edges subdivide them. Line, quadratic and cubic subdivision is exact; arc insertion retains elliptical arc parameters with corrected radii.

Use **Paths** or the **Path** menu for conversion and combinations. Canvas siblings are transformed into their common parent's coordinates. Subtract uses the bottom shape minus the upper operands. The bottom shape supplies appearance, and results remain editable paths. Divide currently requires exactly two shapes. Make compound uses EvenOdd filling; Break apart separates contours and can therefore remove the visual effect of holes. Destructive operations reject locks, animation/state references and unresolved explicit style/opacity/effect combinations rather than silently discarding them.

A path's `Stretch=None` preserves its coordinate system; the other stretch modes transform geometry into the layout box without multiplying stroke thickness. Interactive rectangle resizing transforms path geometry explicitly. Geometric clipping maps the top shape to the target's local coordinates and removes the mask shape in one undoable transaction.

Arcs remain native elliptical segments for ordinary rendering and simple translation/uniform scaling. Flattening an arbitrary affine transform into editable path data approximates arcs with cubic segments spanning at most 45 degrees; it is not algebraically exact. Boolean results inherit Skia's floating-point geometric semantics. Direct editing is a base-document operation, not a new animation or state-recording mode.

## Editing time versus playback time

The timeline ruler and keyframe coordinates use the storyboard's original `Duration`. Scrubbing edits that interval directly. Playback applies `BeginTime`, `SpeedRatio`, `AutoReverse` and repeat settings through `StoryboardClock`. A completed integral forward repeat holds its final key rather than wrapping to the first one; a completed reverse cycle returns to its start. `FillBehavior=Stop` removes the animation contribution and restores the underlying base/state values.

Choose **Timing** in the timeline, edit the settings and use **Apply timing**. Shortening a duration cannot silently remove keyframes. Enable **Scale keyframes** to proportionally retime the existing keys, or move them before shortening. Invalid settings leave the document unchanged.

In **States**, select a state and enable **Record state** to edit overrides from Properties. The inspector shows the state context and effective values. Reset removes the selected override, and undo restores it. Outside state recording, ordinary edits still change base values. Inline gradient creation is blocked during state recording; use a solid color or resource reference instead.

## Import policy

Imported XAML constructs data, never executable assemblies or arbitrary markup extensions. Retention is not behavioral support. Supported flat timing syntax is extracted into the editable timeline; unsupported per-child delay, nested timing, acceleration ratios or custom easing stays as XML rather than losing metadata. Explicit parent durations that clip child keys are also preserved rather than silently lengthened.

Native documents preserve designer identities and state more completely than XAML. Newly added timing fields have defaults so earlier version-1 native files remain readable. Copy/paste remaps model identities and names but does not rewrite every arbitrary reference embedded in retained XML as a semantic dependency graph.

## Limits and evidence

Model documents are limited to 20,000 nodes and 64 nesting levels. XAML/native codecs limit input to 8 MiB of characters; the application also checks import byte sizes. Sample JSON is limited to 256 KiB and PNG export to 32 megapixels. A text block is limited to 32,768 UTF-16 characters. Glyph and line caches retain at most 512/128 entries with estimated 8 MiB/2 MiB cost budgets. Vector paths additionally limit data to 1 MiB of characters, 8,192 segments and coordinate magnitude of one billion units. Hit-test tessellation is capped at 131,072 edges and recursion depth 12. Native path caching uses at most 256 entries with an estimated 8 MiB budget. These are safety and cache budgets, not performance guarantees at every limit.

CI runs portable editing, compatibility, image/layout/shaping, vector geometry/pixel fidelity and trimmed-persistence tests. Published-browser checks use real pointer and keyboard input and retain screenshots, logs, state snapshots and a CPU allocation comparison. Windows/macOS/Linux results are compilation checks unless separate runtime evidence is documented. Chromium uses software-backed WebGL; no physical-GPU, all-browser, screen-reader, native-dialog or independent security certification is implied.

## Largest remaining parity areas

The remaining substantial areas are complete template/style/behavior semantics; advanced multi-point/vector and brush authoring; rich typography and bidi/fallback; nested animation and state transitions; multi-document project tooling; native multi-window docking; accessibility; and cross-platform runtime/GPU qualification. Extend shared libraries and tests rather than introducing a separate browser imitation.
