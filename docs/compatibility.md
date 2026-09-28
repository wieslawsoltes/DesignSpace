# Compatibility matrix — 0.1 preview

DesignSpace is an independent Blend-style designer. Familiar panels do not imply complete or pixel-exact Microsoft Blend, WPF, WinUI or Visual Studio compatibility. This matrix separates implemented authoring from preserved-only markup and unqualified runtime behavior.

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Workspace | Dark tools, Assets, Project, States, Data, Layout, Objects, Properties, Resources, Templates, Timing and design/split/source views | Desktop-oriented density; no pixel-exact Blend, complete accessibility or mobile/touch qualification |
| Docking | Resizable columns/stacked panes, tabs, hide/show/reset and in-application floating/dragging | No arbitrary docking graph, native floating windows, cross-window transfer or persisted floating coordinates |
| Visual editing | Creation, selection, Canvas move/resize, snapping, grouping, duplication, alignment, distribution, z-order, history and Canvas reparenting APIs | Grouping requires contiguous Canvas siblings; arbitrary panel reparenting and complete transformed editing remain unfinished |
| Picking | Inherited transform matrices, inverse-coordinate picking, ellipse shape checks, ancestor clipping, visibility and lock inheritance | Path picking and full runtime clipping/effect semantics are not equivalent to WPF/WinUI |
| Geometry | Rectangle, ellipse, line, SVG-style path rendering and editable path data | No interactive Bezier anchors/handles, boolean geometry, full pen tool or mesh editing; Direct Selection is not a complete point editor |
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

## Editing time versus playback time

The timeline ruler and keyframe coordinates use the storyboard's original `Duration`. Scrubbing edits that interval directly. Playback applies `BeginTime`, `SpeedRatio`, `AutoReverse` and repeat settings through `StoryboardClock`. A completed integral forward repeat holds its final key rather than wrapping to the first one; a completed reverse cycle returns to its start. `FillBehavior=Stop` removes the animation contribution and restores the underlying base/state values.

Choose **Timing** in the timeline, edit the settings and use **Apply timing**. Shortening a duration cannot silently remove keyframes. Enable **Scale keyframes** to proportionally retime the existing keys, or move them before shortening. Invalid settings leave the document unchanged.

In **States**, select a state and enable **Record state** to edit overrides from Properties. The inspector shows the state context and effective values. Reset removes the selected override, and undo restores it. Outside state recording, ordinary edits still change base values. Inline gradient creation is blocked during state recording; use a solid color or resource reference instead.

## Import policy

Imported XAML constructs data, never executable assemblies or arbitrary markup extensions. Retention is not behavioral support. Supported flat timing syntax is extracted into the editable timeline; unsupported per-child delay, nested timing, acceleration ratios or custom easing stays as XML rather than losing metadata. Explicit parent durations that clip child keys are also preserved rather than silently lengthened.

Native documents preserve designer identities and state more completely than XAML. Newly added timing fields have defaults so earlier version-1 native files remain readable. Copy/paste remaps model identities and names but does not rewrite every arbitrary reference embedded in retained XML as a semantic dependency graph.

## Limits and evidence

Model documents are limited to 20,000 nodes and 64 nesting levels. XAML/native codecs limit input to 8 MiB of characters; the application also checks import byte sizes. Sample JSON is limited to 256 KiB and PNG export to 32 megapixels. A text block is limited to 32,768 UTF-16 characters. Glyph and line caches retain at most 512/128 entries with estimated 8 MiB/2 MiB cost budgets. These are safety and cache budgets, not performance guarantees at every limit.

CI runs portable editing, compatibility, image/layout/shaping and trimmed-persistence tests. Published-browser checks use real pointer and keyboard input and retain screenshots, logs, state snapshots and a CPU allocation comparison. Windows/macOS/Linux results are compilation checks unless separate runtime evidence is documented. Chromium uses software-backed WebGL; no physical-GPU, all-browser, screen-reader, native-dialog or independent security certification is implied.

## Largest remaining parity areas

The remaining substantial areas are complete template/style/behavior semantics; pen and vector geometry authoring; rich typography and bidi/fallback; nested animation and state transitions; multi-document project tooling; native multi-window docking; accessibility; and cross-platform runtime/GPU qualification. Extend shared libraries and tests rather than introducing a separate browser imitation.
