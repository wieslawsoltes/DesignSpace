# Property triggers and interactive template preview

DesignSpace previews supported `Style.Triggers` and `ControlTemplate.Triggers` as inert data. The Templates panel authors source resources, and **Test controls** supplies transient pointer and keyboard states to the shared preview engine. This is not a complete WPF dependency-property runtime or pixel-exact Microsoft Blend interface.

## Authoring and testing a template

Select a Button, open **Templates**, and choose **Sample**. The editable example contains a named Border and hover, pressed, keyboard-focus and disabled triggers. **Save** writes the resource; **Apply** assigns the saved resource to the selection. **Reload** explicitly discards an unapplied draft. Existing source can also be edited directly, including Trigger and MultiTrigger declarations.

Template source remains a draft until saved. A change elsewhere in the document makes a modified draft stale; Save rejects it rather than overwriting newer work. Drafts and their revision-match flag follow document tabs and workspace recovery. Saving a workspace does not apply the draft. A generated, untouched New/Sample draft is not treated as unsaved user input; it may be regenerated when the document changes.

Template application validates the complete selection before editing. Missing targets, locked ancestors and incompatible exact TargetType values reject the transaction. Applying a resource removes a conflicting inline Template property element. Repeating an unchanged application preserves the document reference and history. The current compatibility check accepts an exact type name, not the full WPF base-type hierarchy.

**Test controls** enters control preview without starting a storyboard. Pointer hover and captured presses can drive templates. Moving outside the pressed object removes its pressed appearance; releasing outside does not activate it. Tab and Shift+Tab cycle eligible controls, skipping disabled ones. The default workbench may replay its active storyboard through the host preview-click callback. Space and Enter activate the focused control on key release; held-key repeat does not create repeated activations. Escape clears transient input and returns to design. F5 retains the existing storyboard-preview command.

Checkbox/ToggleButton state and radio-group selection are transient. Three-state checkboxes cycle false → true → null → false. Unnamed radio buttons group under the same authored parent; explicit GroupName is currently document-wide. These changes do not write IsChecked to saved XAML or create undo entries. Document changes, switching documents and leaving preview clear input. Preview shortcuts cannot nudge, duplicate or delete the underlying objects. Save and document-tab commands remain host/workbench operations.

Preview callbacks are exposed to the host, but imported event handlers, actions, commands or arbitrary assemblies are never executed. RepeatButton currently receives ordinary button activation; periodic repeat behavior is not implemented.

## Supported rules and precedence

A `Trigger` matches one Property/Value condition. A `MultiTrigger` requires every condition in its Conditions collection. Direct setters and the corresponding Setters property wrapper are supported. Template rules can use `SourceName` and `TargetName` within that template instance; names cannot reach another consumer's generated parts.

Conditions use the explicitly supported Boolean, finite numeric and literal text properties. Boolean values compare as Booleans; numeric values compare numerically; text values compare ordinally. IsChecked also supports `{x:Null}`. Read-only interaction properties include IsMouseOver, IsPressed, IsKeyboardFocused and IsKeyboardFocusWithin. Live input currently targets authored owners, not independently hovered generated parts.

Matching setters retain declaration order. The last matching assignment to the same property wins within a collection. Derived BasedOn rules follow base rules. Supported precedence is: owner local values before style triggers, style triggers before template self-triggers, and template self-triggers before normal style setters. A named template-part trigger can replace a value in that part's template declaration. A self-trigger changing an owner's appearance refreshes TemplateBinding consumers. These ordering rules follow [Microsoft's dependency-property precedence documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/properties/dependency-property-value-precedence).

Brush and effect Setter.Value objects are preserved as objects rather than converted to plain colors. Direct TemplateBinding can forward inline appearance objects, including brush opacity. Trigger resource references resolve against the declaration scope, including supported template-local resources. This does not provide a complete dynamic-resource, binding, inherited-property or style-resource implementation.

All conditions in a collection read one stable effective-value snapshot. Feedback within that collection, where a condition property is also assigned, is rejected. Cross-collection dependency propagation, style/template feedback, re-evaluation after named-part property changes, data/event triggers, enter/exit actions, arbitrary bindings, automatic visual-state transitions and full nested-template namescopes are not implemented. Unsupported collections remain stored as source and produce diagnostics instead of being partially executed. Missing named targets reject the whole collection's preview assignments.

## Reusable APIs

```csharp
using DesignSpace.Core;
using DesignSpace.Xaml;

var context = new DesignPreviewContext();
context.Update(new PreviewInteractionState { Hovered = buttonId });
DesignNode previewRoot = context.Resolve(document.Root);
// Arrange/draw previewRoot using the existing Engine/Rendering.Skia services.
// document.Root remains unchanged.
context.Clear();
```

`DesignPreviewContext` is owned by one host/preview. Repeated input and root references reuse the same expanded tree. `DesignerSurface` uses its own context by default and exposes `PreviewClicked`; a custom PreviewResolver replaces that integration and must supply its own interaction behavior. `TemplateLibrary.CreateInteractiveDefault`, Save and Apply remain portable APIs in DesignSpace.Xaml.

`PropertyTriggerProgram.Parse(source, template)` compiles a trigger collection independently of Uno or Skia. Call `ValidateNames` before evaluating template rules. `Evaluate(read, assign)` uses caller-provided callbacks: `read` must return stable values for the entire evaluation, and `assign` receives matching assignments in order. The caller owns precedence and object-value application. Reuse callbacks to avoid closure/delegate allocation. The compiled program is immutable, but host contexts and rendering services are not concurrent shared workspaces.

## Performance, limits and verification

Unchanged markup strings reuse weakly keyed XML, template-visual and trigger-program caches. Pointer movement inside the same target does not rebuild the preview tree. Actual target/state changes still require expansion and layout; the warm scalar-program allocation measurement is not a promise of allocation-free control transitions or a GPU frame-rate claim.

A program accepts at most 256 rules, 32 conditions per MultiTrigger and 2,048 total conditions/setters. Standalone trigger XML is limited to 1 MiB; each literal to 65,536 characters. Document and preview node/depth limits still apply. Unsupported executable metadata is not loaded.

The portable suite covers input isolation, instance identities, precedence, resource/object values, unsupported metadata, undo separation, real renderer pixels and 10,000 warmed evaluations. The native Windows suite compares effective brush values and reset behavior in 64 combinations of parent enablement, local/style/template values, named parts, and Trigger/MultiTrigger rules against WPF. It does not compare the Blend interface or every WPF property. Browser tests send real pointer, keyboard and file input and observe read-only diagnostics; they also check actual artboard pixels. All older regressions remain enabled.

Inspect Actions artifacts for the exact commit before calling a run verified. Software-backed Chromium screenshots are not physical-GPU/native desktop interaction qualification. No matched Blend version/theme/DPI screenshot corpus is provided, so full or pixel-exact Blend equivalence is not asserted.
