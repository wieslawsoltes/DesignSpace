# Additive and cumulative numeric keyframes

The Animation inspector supports `IsAdditive` and `IsCumulative` on numeric keyframe tracks. These options implement the semantics of `DoubleAnimationUsingKeyFrames`, not the different From/To rules of simple `DoubleAnimation`. This is a defined compatibility increment, not complete or pixel-exact Blend parity.

## Authoring

Select a numeric track, open **Animation**, and choose **Add to base value**, **Accumulate each repeat**, or both. These options belong to the entire selected track, not an individual key. They remain drafts until **Apply track**, which commits flags, timing and the selected key in one validated transaction. **Reload** discards the draft.

With an independent child clock, repeats accumulate within that child. A parent repeat restarts its child clocks and their accumulation. A complete forward-and-back auto-reverse cycle counts as one child iteration. Without an independent child clock, the track shares the parent interval and does not invent an extra accumulation counter across parent repeats.

Drafts follow their document tabs and workspace recovery, including invalid numeric text. Saved native documents contain applied flags, while workspace files can also contain unapplied flag drafts. Invalid values, locked targets and stale revisions prevent partial application. Changing a key value, retiming keys, or changing child timing retains the composition flags.

## Calculation

Let `B` be the underlying base or active-state value, `F(t)` the interpolated keyframe value in the current child iteration, `L` the last key's value, and `n` the one-based child iteration.

- Ordinary keyframes produce `F(t)`.
- Additive keyframes produce `B + F(t)`.
- Cumulative keyframes produce `F(t) + (n - 1) * L`.
- Both options produce `B + F(t) + (n - 1) * L`.

Before the first positive-time key, ordinary interpolation starts from `B`; additive interpolation starts from zero, before adding `B` at the end. Explicit key arrivals retain their authored value. Easing, including overshoot, remains part of interpolation.

Cumulative **keyframes add the final key value, not the final-minus-first difference**. For keys from 10 to 110, an additive base of 160 and three two-second child cycles, the midpoints at parent times 1, 3 and 5 are 220, 330 and 440. At completion the held value is 490. A nonzero first key can create an intentional repeat-boundary jump.

`FillBehavior=Stop` removes the animation contribution, restoring the underlying value. A finite parent can clip an explicit repeating child without discarding keys. Fractional counts and duration-based repeats retain their partial terminal sample. Seeking is stateless for composition: samples are computed from the current clock iteration, not by adding again each time the caller samples.

## Reverse boundaries

Boundary selection depends on the intrinsic direction of the parent clock. When a parent is auto-reversing at a child's exact repeat boundary, WPF uses the preceding child iteration's end instead of the next iteration's beginning. The clock propagates this direction separately from local time and reports `CurrentIteration` and `IsReversed`.

A user seeking to an earlier timestamp is not automatically a backwards-progressing parent clock. The parent's auto-reverse phase determines that direction. `EvaluateLocal` deliberately scrubs the original parent interval in its forward coordinate system, while normal `Evaluate` maps the parent's actual playback phase.

## Reusable APIs

```csharp
using DesignSpace.Core;
using DesignSpace.Animation;

var node = DesignNode.Create("Rectangle", "Block", 160, 165, 70, 70);
var root = DesignNode.Create("Canvas", "Root") with { Children = [node] };
var track = new AnimationTrack(node.Id, "Canvas.Left",
    [new(0, 10), new(2, 110)])
{
    Timing = new TrackTiming(Duration: 2, RepeatCount: 3),
    IsAdditive = true,
    IsCumulative = true
};
var board = new DesignStoryboard(Guid.NewGuid(), "Composition", 8, [track]);
var preview = AnimationEngine.Evaluate(root, board, time: 3);
// The preview Block has Canvas.Left = 330; root still has Canvas.Left = 160.

double value = AnimationEngine.EvaluateIteration(
    track, time: 1, baseValue: 160, currentIteration: 2); // 330
```

`Evaluate(track, localTime, baseValue)` preserves its existing signature and samples the first child iteration. `EvaluateIteration` accepts an explicit one-based child iteration. `ChangeTrackComposition` returns an immutable replacement without modifying clocks, key metadata or other tracks; commit it through `DesignSession.Execute`. No-op flag updates preserve the original reference.

`AnimationCompositionControl` is an independently reusable Uno control. It exposes two values, `DraftChanged`, and inert draft capture/restore. It never edits a document by itself. The full Animation inspector integrates it with revision and ownership checks.

## Persistence and preservation

Supported `DoubleAnimationUsingKeyFrames` attributes round-trip through inert XAML parsing/export. False defaults are omitted from native JSON so earlier version-1 files remain readable without adding irrelevant fields. Unknown flag expressions and invalid Boolean literals preserve the complete storyboard rather than guessing their value.

Simple `DoubleAnimation` with `IsAdditive`, `IsCumulative`, `By` or From-only semantics remains preserved markup in this increment. In particular, converting a cumulative From/To animation to cumulative keyframes would change its repeat contribution; it is not silently rewritten. Arbitrary nested clock hierarchies, animation composition chains, and object/color/point/path animation remain outside this model.

## Verification and limits

The Windows test executable compares a matrix of native WPF numeric keyframe animations against the portable engine, including additive/cumulative combinations, negative and zero terminal values, implicit starting intervals, repeats, reverse playback, clipping, Stop, and reordered seeks. `wpf-composition-results.json` records actual case counts, sample counts, maximum error and a fixed absolute tolerance of `1e-8`. Inspect the artifact for a specific commit's result; the presence of a test does not itself imply it passed.

Portable tests cover the formulas, backward-compatible persistence, transactional edits, metadata retention and warm scalar allocation. Browser tests use real file-picker, pointer and keyboard input to check flags, preview values and pixels, undo, invalid drafts, export and tab/recovery isolation. These are numerical and workflow checks, not matched Blend screenshot, native desktop interaction, or physical-GPU certification.

The scalar API requires finite values and an exactly representable positive integer iteration no greater than 9,007,199,254,740,991. Nonfinite composed results fail explicitly rather than contaminating the document. Existing clock duration and document limits remain in effect. Warm sampling reuses weakly cached sorted keys; this does not imply allocation-free document overlays, layout, drawing or GPU submission.

References: [WPF numeric keyframe source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Animation/Generated/DoubleAnimationUsingKeyFrames.cs) and [WPF clock source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Animation/Clock.cs). Existing upstream attribution is retained in the package notices.
