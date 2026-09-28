# Compatibility matrix — 0.1 preview

DesignSpace is an independent Blend-style designer, not a reimplementation of every Blend, WPF, WinUI or Visual Studio subsystem. A familiar panel arrangement does not imply full feature or pixel-level parity. This matrix describes current source, not future promises.

| Area | Implemented | Boundary |
| --- | --- | --- |
| Workspace | Dark tools, assets, object outline, properties, states, data, resources, design/split/source views and timeline | Not pixel-exact Blend; desktop-oriented density; no full accessibility or mobile/touch qualification |
| Docking | Resizable columns/stacked panes, tabs, hide/show/reset, in-window floating and dragging | No arbitrary dock-target graph, native OS floating windows, cross-window transfer or multi-monitor qualification; floating coordinates are not recovered |
| Visual editing | Pointer creation, multi-selection, marquee, Canvas movement/resizing, aspect constraint, grouping, duplication, alignment, z-order and history | Grouping requires Canvas siblings; full transformed-ancestor picking, arbitrary panel reparenting and every locked-ancestor interaction are not implemented |
| Geometry | Rectangle, ellipse, line and SVG-style path drawing; editable path data string | The path tool creates a starter path; Direct Selection does not implement a Bezier point/handle editor, boolean geometry, pen authoring or mesh editing |
| Layout | Canvas, StackPanel, simple Grid tracks, padding, margins, alignment and explicit sizes | Grid Auto tracks use a fixed design-time estimate; no complete two-pass intrinsic measure, shared-size groups, dependency properties, layout rounding or full runtime equivalence |
| Controls | Visual previews for buttons, text boxes, text, common selection controls, sliders and progress bars | These are Skia design-time previews, not complete runtime control templates, input semantics, Fluent styling or native controls inside the artboard |
| Typography | Cached typefaces/fonts, sizing, clipping and explicit newlines | Complete shaping, bidi, font-family/weight/style fidelity, font fallback, wrapping, rich inlines, typography/OpenType editing and runtime-equivalent metrics remain unfinished |
| Brushes | Solid ARGB, color editor, basic linear/radial gradients, local brush resources | Full mapping modes, transforms, spread modes, nested resource scopes and all brush semantics are not supported |
| Images | Image properties/source markup retained | Image decoding, asset embedding/import and full ImageBrush rendering are not implemented; the artboard displays a labeled placeholder |
| Animation | Position, size, opacity and rotation tracks; playback, scrubbing, keyframe movement, record mode and easing | No complete nested timelines, additive/cumulative animation, repeat/auto-reverse semantics, object/color animation or full expression editing; unsupported animation markup stays outside the editable timeline |
| States | Create/delete, capture property values, preview overlays and XAML setters | No complete transition editor, trigger system or automatic template/VSM runtime. Ordinary property edits affect base values unless numeric keyframe recording is active |
| Data | JSON sample data and simple `{Binding Path}` property preview | No CLR data provider, converters, expressions, collections/items templates, live service adapters or full binding engine |
| XAML | Validated inert import, explicit source apply, named identity reconciliation, supported storyboard/state export and retained property elements | Not lossless XML: formatting, comments and mixed text/inlines are not preserved exactly. Dialect-specific state/animation output is Uno/WinUI-oriented; WPF runtime qualification has not been performed |
| Templates/styles | XML property-element preservation and manual source editing | No full control-template execution/designer scope, style inheritance editor, triggers, behaviors or custom assembly loading |
| Files | Native `.designspace`, XAML export, PNG export, browser downloads and local recovery | No `.sln`/`.csproj` project compilation, build/debugger, full asset project system, multi-document workspace, Git integration or cloud synchronization |
| Collaboration | None required for local editing | No multi-user synchronization, identity, permissions, shared storage or enterprise administration |

## Import policy

Imported XAML is data, never an instruction to execute assemblies, markup extensions or application code. Unknown elements and supported property-element structures can be retained, but their behavior is not simulated. A parsed document is not evidence that the original application will behave identically in the preview.

Only supported storyboard syntax is extracted into the editable timeline. For example, a storyboard using unsupported `AutoReverse` or custom easing stays as raw XAML rather than losing that metadata. Export rejects a collision between an editable resource and a retained resource using the same key.

Native documents preserve designer state more completely than XAML and are the recommended working format. Copy/paste remaps object identities and names, but arbitrary references embedded in retained XML are not rewritten as a full semantic dependency graph.

## Limits and qualification

Documents are limited to 20,000 model nodes and 64 nesting levels. XAML/native input is limited to 8 MiB of characters in the codecs; the app's file import also checks an 8 MiB byte limit. Sample data is limited to 256 KiB. PNG export is limited to 32 megapixels. These are safety budgets, not performance guarantees for every document at those limits.

Browser CI uses Chromium with software-backed WebGL. It verifies visible pixels and real editing, serialization and recovery workflows. It does not certify physical-GPU speed, every browser engine, screen-reader behavior, native desktop file-dialog interaction, or production security. Windows/macOS/Linux build results are compilation results unless a separate runtime test is documented.

## Next substantial parity areas

The largest remaining investments are runtime-qualified layout and typography; real template/style/behavior authoring; image/vector asset pipelines and pen editing; advanced animation/state transitions; multi-document project tooling; richer docking and accessibility; and cross-platform GPU/runtime qualification. Each should extend the shared libraries and regression suites rather than introducing an unrelated browser implementation.
