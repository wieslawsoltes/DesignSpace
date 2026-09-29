# Animation tracks, clocks and spline authoring

DesignSpace's numeric animation engine and its Uno controls are independently reusable. This increment implements a parent storyboard with independently timed numeric child tracks. It does not implement the complete Microsoft Blend or WPF animation system.

## Authoring

Select a track or key in Objects and Timeline, then open **Animation** in the toolbar or **Track** in the timeline. The panel edits a draft: **Apply track** commits timing and the selected key together; **Reload** discards that draft. Existing keys can be selected from the Key list. Apply or reload a changed draft before choosing a different key.

Enable **Independent track clock** to set the track's Duration, Begin delay, Speed, Auto reverse, repeat count/duration/forever and HoldEnd/Stop. Without an independent clock, the track retains the original shared parent interval. The ruler is always in parent time. Diamonds show keys in the child's first forward iteration, including its start delay and speed; repeated/reversed occurrences are previews, not duplicated editable keys. Very short or overlapping keys can be selected precisely in the key list.

Key time and Value are edited in the child's own interval. Easing belongs to the destination key of each segment. Choose Spline and edit four control coordinates, use a preset, or drag either handle in the graph. The graph's endpoints remain `(0,0)` and `(1,1)`. The white midpoint indicates the output at half the elapsed segment time, not half the Bezier parameter. Pointer edits change only the draft until Apply.

The fixed **Time (s)** field and **Scrub** action remain accessible while the inspector scrolls. They scrub the actual artboard without changing source, history or the base document. Scrubbing bypasses the parent's delay/repeat settings but retains child timing. Playback uses both clock levels. Width/position changes still require normal design-time layout; the clock optimization does not bypass layout or render the complete application on a compute shader.

Changing a child duration rejects keys outside its new interval unless **Scale track keys** is selected. Scaling preserves easing and control points. Scaling the parent retimes child delays, durations and repeat durations as well as key times; it does not multiply Speed or repeat counts. Shortening a parent without scaling can deliberately clip an explicit child without deleting its keys. Moving a track's sole key keeps its timing and spline metadata. Moving onto another key is rejected rather than silently deleting that key; explicit value replacement remains available through SetKey.

## Clock coordinates

A child starts when its parent's local time reaches Begin. Its own active time then advances at Speed. Repetition and reverse playback map that active time to the child's original keyframe interval. Begin is applied once per child activation, not before every child repetition; parent repetition restarts the relative child delay. Parent reversal reverses the coordinates supplied to its children.

HoldEnd keeps a completed track's last sampled value. Stop removes that track's contribution and reveals the base/state value. A stopped parent removes its children's contribution. An explicit finite parent can clip a longer or forever-repeating child. The current finite-ruler model does not extract an Automatic parent whose child makes its duration infinite.

These relationships are based on Microsoft's [timing behavior documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/timing-behaviors-overview). The independent Windows reference tests exercise selected numeric cases against WPF; the implemented subset and test cases are not a complete compatibility certification.

## Spline calculation and XAML

`KeySpline` validates finite control coordinates in `[0,1]`. Evaluation solves the cubic's x coordinate for the time parameter, then evaluates y. Safeguarded Newton steps retain a bracket and fall back to bisection; work is bounded to 48 iterations. Equal diagonal control coordinates use the linear shortcut. The contract follows Microsoft's [key-frame animation description](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/key-frame-animations-overview).

Supported XAML includes `SplineDoubleKeyFrame` with a `KeySpline` attribute or property-element form, linear/discrete/cubic-easing double keys, supported numeric property targets, and per-child timing attributes. Fixed-child-duration percentage key times are resolved to local seconds. Numeric values use invariant round-trip formatting; XAML time values are limited by TimeSpan's 100-nanosecond resolution. Native files retain double values and explicit clock/spline metadata; old native tracks without the new fields remain readable.

Import uses exact presentation-namespace and attribute checks. Unsupported nested storyboards, animation types, easing functions, unknown attributes, Uniform/Paced key times, percentages without an explicit child duration, and unresolved infinite parent duration retain the complete storyboard as inert XML rather than partially losing its semantics. No arbitrary markup extension or external assembly executes.

## Reusable APIs

```csharp
using DesignSpace.Core;
using DesignSpace.Animation;

var node = DesignNode.Create("Rectangle", "Animated", 20, 20, 100, 60)
    .Set("Opacity", 0.25);
var root = DesignNode.Create("Canvas", "Root", 0, 0, 800, 600)
    with { Children = [node] };
var track = new AnimationTrack(node.Id, "Opacity",
    [new(0, 0), new(2, 1, "Spline")
        { Spline = new KeySpline(0.42, 0, 0.58, 1) }])
{
    Timing = new TrackTiming(Duration: 2, BeginTime: 1,
        SpeedRatio: 2, AutoReverse: true, RepeatCount: 2)
};
var storyboard = new DesignStoryboard(Guid.NewGuid(), "Entrance", 6, [track]);
var preview = AnimationEngine.Evaluate(root, storyboard, time: 1.5);
var childClock = StoryboardClock.Sample(track.Timing, parentTime: 1.5);
```

`AnimationEngine.Evaluate(track, time, baseValue)` accepts **child-local** time. `Evaluate(root, storyboard, elapsed)` maps the parent and child clocks. `EvaluateLocal` bypasses only parent timing. `KeyToParentTime` and `ParentToKeyTime` project the first forward iteration for authoring. Do not confuse these coordinate systems in a custom timeline.

`ChangeTrackTiming`, `MoveKey`, `SetKey` and `ChangeDuration` return immutable storyboard replacements; commit them through the host's validated session transaction. Core contracts and clocks do not create timers or controls. The Uno `AnimationTrackInspectorControl` accepts a session and TimelineControl, while `KeySplineEditorControl` can be embedded by itself and reports draft changes through `CurveChanged`.

## Exact and WPF-compatible spline sampling

The default `KeySpline.Evaluate` and animation-engine overloads are deterministic: the same progress produces the same value regardless of previous samples. The workbench uses this exact mode. It does not implicitly maintain WPF's numerical history.

WPF's native KeySpline implementation instead reuses the previous parameter guess and stops using its derivative-scaled numerical criterion. Near a stationary tangent, its output can differ from exact cubic inversion and depend on sample order. A fixed 0.003 progress tolerance is not a general bound for those cases. Do not call a stateless exact evaluator bitwise WPF-compatible solely because the curves look the same.

For a host requiring the native numerical behavior, keep an explicit context for each playback:

```csharp
var context = new AnimationSamplingContext(SplineSamplingMode.WpfCompatible);
var first = AnimationEngine.Evaluate(root, storyboard, time: 1.5, context: context);
var next = AnimationEngine.Evaluate(root, storyboard, time: 1.6, context: context);
context.Reset(); // Begin a new playback; a seek within playback normally retains its history.
```

`WpfKeySplineSampler` also exposes the stateful numerical algorithm for a single immutable curve. Contexts own weakly keyed per-key samplers; different keys and playbacks do not share previous guesses. Do not share a context across threads or unrelated animations. Exact mode does not allocate samplers. This compatibility option matches the tested finite unit-square controls; it does not expand the model to out-of-range y controls or arbitrary WPF timeline semantics. The adapted MIT-licensed algorithm and license are recorded in `THIRD-PARTY-NOTICES.md`, included in all package archives.

## Draft and resource ownership

Animation drafts belong to the document where they were created. They retain their board/track/key identity, original values and revision, including invalid text. Switching documents or exporting/recovering a workspace transfers inert drafts rather than applying them. Missing-target drafts remain recoverable until Reload. Locks and stale revisions prevent mutation. Save design saves applied model values, while Save workspace includes unapplied drafts.

The sorted-key cache uses immutable track references and weak ownership. Sampling warm keys/splines/clocks avoids allocating temporary storyboard objects. Native graph drawing remains a host-owned canvas operation. It is UI-thread-owned; neither controls nor the mutable DesignSession are concurrent services.

## Browser upload lifetime

The application host copies user-selected files with a page-owned upload control. File data is read in the same JavaScript page realm; no worker-bound JavaScript promise is retained across reloads. The native desktop picker is unchanged. Browser uploads support UTF-8 and BOM-marked UTF-16 text, enforce 8 MiB document / 16 MiB workspace / 4 MiB image limits before reading, and clean up on completion, cancellation or the ten-minute selection timeout. Uploads never fetch unselected filesystem paths or remote resources. The independent animation browser suite checks cancellation, oversized input, and subsequent valid import in addition to the existing reload/upload workflows.

## Verification and boundaries

Portable regressions cover parent/child clock interactions, spline inversion against independent parametric values, import/preservation, invalid data, sole-key metadata, scaling, no-op sharing and warm allocation. The existing compatibility suite is also published trimmed and run with reflection serialization disabled. Browser workflows use real pointer/keyboard/upload/download input and check artboard values/pixels, spline handles, source isolation, undo and recovery.

`tests/DesignSpace.WpfParity.Tests` is a separate Windows-only native WPF reference executable, run by the Windows build job. It compares like-for-like ordered spline samples using the WPF-compatible context, including forward, reverse and nonsequential seeks. A separate synchronous-seek matrix compares parent/child clocks and animated values. The retained tolerances are 1e-12 for normalized spline progress, 1e-9 for animated spline values and 1e-6 for other numeric values. Exact-mode differences are measured independently and are not relabeled as compatibility passes; independent parametric tests continue to verify exact inversion.

The JSON evidence includes sample counts, maximum observed differences for each mode, the reference-source link and individual case outcomes. Runtime preview values use round-trip formatting instead of the three-decimal inspector formatter. Run it on Windows with `dotnet run --project tests/DesignSpace.WpfParity.Tests -c Release`. It is deliberately not in the portable solution, avoiding a Windows desktop dependency in the reusable libraries. The numerical algorithm follows [WPF's source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Animation/KeySpline.cs); a native test result is still limited to its stated curves, sample sequences and clock scenarios.

Remaining boundaries include arbitrary nested timeline hierarchies, additive/cumulative animation, By/from-only and color/object/point/path animations, acceleration/deceleration ratios, complete property paths, automatic triggers, explicit state storyboards, and complete template/runtime semantics. Track delay/duration/repeat-duration settings have a one-year safety limit. No matched Blend build/theme/DPI screenshot comparison or full native-runtime/physical-GPU qualification is implied by numerical WPF tests or software-backed browser screenshots.
