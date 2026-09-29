# Built-in easing authoring

The Animation panel supports all eleven built-in easing families: Back, Bounce, Circle, Cubic, Elastic, Exponential, Power, Quadratic, Quartic, Quintic and Sine. Each has EaseIn, EaseOut and EaseInOut directions. This is numeric animation functionality, not a claim of complete or pixel-exact Blend compatibility.

## Edit a keyframe

Select a timeline track or key, open **Animation**, and choose **Function** under Easing. Choose the family and direction, then edit the relevant parameters: amplitude for Back; bounce count and bounciness for Bounce; oscillations and springiness for Elastic; exponent for Exponential; or power for Power. Polynomial, Circle and Sine functions need no extra parameters.

The graph displays the curve including overshoot and undershoot. Drag horizontally in the graph to inspect a normalized input/output pair without changing the document. Its vertical range adjusts to the curve; a value above one is not silently clipped. Use the fixed Time/Scrub controls to inspect the actual artboard under the parent and child clocks.

Edits remain a draft until **Apply track**. Timing, key time/value and easing commit together as one undoable change. Invalid parameters remain editable, and stale drafts cannot overwrite a newer document revision. Animation drafts—including invalid parameter text—follow their document tab and workspace recovery. **Reload** discards that draft. Inactive parameter fields retain their draft text but do not become unrelated parameters in the committed function.

Changing a key's value, moving it or rescaling its interval preserves its function. Choosing Linear, Discrete or Spline removes obsolete function metadata. Existing cubic EaseIn/EaseOut/EaseInOut aliases remain supported. A Cubic function exported to XAML may import as its equivalent legacy alias rather than a separate function object.

## Numeric semantics

Normalized function output is not restricted to [0,1]. Back and Elastic deliberately overshoot. The host property may constrain its visible result: for example, opacity painting is clamped, whereas Canvas.Left can overshoot spatially. Preview values are not written into the base document or history.

Negative amplitude, power, springiness and oscillation/bounce counts use WPF's nonnegative coercion. Bounce ratios at or below one use 1.001 to avoid a singular geometric progression. Exponent zero is linear. These coercions are distinct from safety limits; the original parameter is retained in native documents and XAML.

Degenerate Power=0 is discontinuous: when the first key has positive time, EaseIn immediately evaluates to its target contribution at clock start, and EaseInOut initially contributes halfway. An explicitly authored key at that exact time instead retains its own value. Reaching a key's timestamp uses that key's value rather than evaluating the next interval's degenerate function. Native WPF tests exercise these boundaries rather than assuming every function maps zero to zero.

## Import and preservation

The XAML reader accepts these functions in `EasingDoubleKeyFrame.EasingFunction` and `DoubleAnimation.EasingFunction`, including documented literal parameters and `EasingMode`. A simple DoubleAnimation is represented by equivalent numeric keys and its independent clock when its function has conventional endpoint values. Discontinuous cases such as Power=0 stay preserved: simple animations sample easing at the endpoints, while explicit key arrivals take their authored value, so converting these cases would change behavior. An empty EasingDoubleKeyFrame without a function is linear.

Import does not instantiate arbitrary easing classes or evaluate markup extensions. Unknown families, unsupported attributes/children, unresolved parameter expressions, invalid integers and out-of-budget values leave the storyboard as inert preserved XAML rather than dropping the function or silently substituting linear motion. Strict namespaces prevent similarly named custom elements from being treated as built-in types.

## Reusable APIs

```csharp
using DesignSpace.Core;
using DesignSpace.Animation;

var function = new EasingCurve(EasingFamily.Bounce,
    EasingDirection.EaseOut, Bounces: 5, Bounciness: 2.5);
double normalizedOutput = function.Evaluate(0.7);

var key = new AnimationKey(2, 400, "Function") { Function = function };
var track = new AnimationTrack(targetId, "Canvas.Left", [new(0, 40), key]);
double position = AnimationEngine.Evaluate(track, time: 1.4, baseValue: 40);
```

`EasingCurve` is immutable Core data and does not depend on Uno or Skia. `EasingCurveCodec` in Xaml parses/writes only supported declarative functions. `EasingFunctionEditorControl` and `EasingPreviewControl` are individually reusable Uno controls. The editor exposes ReadCurve and inert draft capture/restore; the containing host chooses when to commit. Dispose preview/editor resources when the host closes.

The scalar evaluator creates no temporary clocks or function objects per sample. Warm allocation tests cover direct function loops. This does not imply allocation-free document overlays, layout, rendering or high FPS on any particular GPU. The graph caches its sampled path until parameters change.

## Limits and qualification

Parameters must be finite. Amplitude, power and bounciness magnitudes are limited to 1,000; exponent and springiness to 100; bounce count to [-32,32]; oscillation count to [-64,64]. These limits prevent pathological preview work and overflow, and are stricter than an unconstrained native dependency property. Outside those budgets, source is preserved rather than executed.

CI compares direct scalar functions and independently timed numeric keyframes against actual Windows WPF controls. It records separate scalar and animated maxima, with tolerances of 1e-12 and 1e-9 respectively. Inspect `wpf-easing-results.json` in the `wpf-animation-reference` artifact for the exact tested parameters, sample counts and outcome of a particular commit. Portable/trimmed tests cover persistence, safety and editing; browser workflows cover the editor, graph/artboard pixels, undo and document-specific recovery. These gates do not certify every WPF property, full Blend UI pixels, complex nested timelines or physical-GPU performance.

The adapted easing mathematics is MIT licensed by the .NET Foundation. Attribution and full license text are retained in `THIRD-PARTY-NOTICES.md` and every package. Reference: [WPF easing functions](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/easing-functions) and the [WPF animation sources](https://github.com/dotnet/wpf/tree/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Animation).
