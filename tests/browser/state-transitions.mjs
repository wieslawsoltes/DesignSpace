import assert from 'node:assert/strict';
import {PNG} from 'pngjs';
import {readFile} from 'node:fs/promises';

// Diagnostics are read-only. Source, authoring, selection and preview use real UI input.
export async function stateTransitions({page,snapshot,click,check,directory}) {
  const wait=async()=>page.waitForTimeout(400);
  async function edit(name,text){await click(name);await page.keyboard.press('Control+A');await page.keyboard.insertText(text);await page.keyboard.press('Tab');await wait();}
  async function changed(revision){await page.waitForFunction(n=>globalThis.designSpaceDiagnostics.revision===n,revision+1);return snapshot();}
  async function settled(){await page.waitForFunction(()=>!globalThis.designSpaceDiagnostics.states.transitioning,null,{timeout:15000});await wait();return snapshot();}
  async function states(){await click('States');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name==='Preview base state'));}
  async function editor(){await click('Open Transitions panel');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name==='Transition duration'));}
  const preview=(s,id,key)=>s.states.preview.find(p=>p.target===id&&p.property===key)?.value;
  const rule=(s,group,from=null,to=null)=>s.states.groups.find(g=>g.name===group)?.rules.find(t=>t.from===from&&t.to===to);
  async function pixel(x,y){const s=await snapshot(),v=s.surface,png=PNG.sync.read(await page.screenshot());const px=Math.round(v.x+v.panX+x*v.zoom),py=Math.round(v.y+v.panY+y*v.zoom),i=(py*png.width+px)*4;return [...png.data.subarray(i,i+3)];}
  const fixture=`<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="StateArtboard" Width="960" Height="640" Background="White">
  <VisualStateManager.VisualStateGroups>
    <VisualStateGroup x:Name="Common">
      <VisualState x:Name="Calm"><VisualState.Setters><Setter Target="TransitionBox.Opacity" Value="1"/><Setter Target="TransitionBox.(Canvas.Left)" Value="180"/><Setter Target="ColorBox.Fill" Value="#FF000000"/></VisualState.Setters></VisualState>
      <VisualState x:Name="Emphasis"><VisualState.Setters><Setter Target="TransitionBox.Opacity" Value="0.2"/><Setter Target="TransitionBox.(Canvas.Left)" Value="380"/><Setter Target="ColorBox.Fill" Value="#FFFFFFFF"/></VisualState.Setters></VisualState>
    </VisualStateGroup>
    <VisualStateGroup x:Name="Focus">
      <VisualState x:Name="Compact"><VisualState.Setters><Setter Target="TransitionBox.Width" Value="120"/></VisualState.Setters></VisualState>
      <VisualState x:Name="Wide"><VisualState.Setters><Setter Target="TransitionBox.Width" Value="220"/></VisualState.Setters></VisualState>
    </VisualStateGroup>
  </VisualStateManager.VisualStateGroups>
  <TextBlock x:Name="Heading" Text="State groups and transitions" Canvas.Left="72" Canvas.Top="64" Width="800" Height="50" FontSize="30" Foreground="#FF18243A"/>
  <TextBlock x:Name="Caption" Text="Independent groups. Editable transitions. One shared engine." Canvas.Left="72" Canvas.Top="124" Width="800" Height="35" FontSize="16" Foreground="#FF63738A"/>
  <Rectangle x:Name="TransitionBox" Canvas.Left="180" Canvas.Top="250" Width="120" Height="100" Opacity="1" Fill="#FF0078D4"/>
  <Rectangle x:Name="ColorBox" Canvas.Left="650" Canvas.Top="250" Width="150" Height="100" Fill="#FF000000"/>
  <TextBlock x:Name="PositionLabel" Text="Position, opacity, and width" Canvas.Left="180" Canvas.Top="390" Width="430" Height="35" FontSize="16"/>
  <TextBlock x:Name="ColorLabel" Text="Solid-color transition" Canvas.Left="630" Canvas.Top="390" Width="300" Height="35" FontSize="16"/>
</Canvas>`;
  let box,color;
  await check('grouped state XAML imports through the real source editor',async()=>{
    await click('Return to base values');await click('XAML view');await click('XAML source editor');await page.keyboard.press('Control+A');await page.keyboard.insertText(fixture);await wait();await click('Apply XAML');
    await page.waitForFunction(()=>!globalThis.designSpaceDiagnostics.sourceDirty&&globalThis.designSpaceDiagnostics.states.groups.some(g=>g.name==='Common'));
    await click('Design view');await click('Fit artboard');await states();const s=await snapshot();assert.equal(s.states.groups.length,2);assert.equal(s.states.items.length,4);box=s.nodes.find(n=>n.name==='TransitionBox').id;color=s.nodes.find(n=>n.name==='ColorBox').id;
    // Give a multi-group list more space using the actual dock splitter, not test mutation.
    await page.mouse.move(175,336);await page.mouse.down();await page.mouse.move(175,556,{steps:12});await page.mouse.up();await wait();
    assert.ok((await pixel(725,300)).every(c=>c<12));
  });
  await check('transition editor authors default rules for two groups',async()=>{
    for(const [name,duration] of [['Common','3'],['Focus','2']]){
      await click('Select state group '+name);await editor();await click('Reload visual transition draft');await edit('Transition duration',duration);const before=await snapshot();await click('Save visual transition');const s=await changed(before.revision);
      assert.equal(rule(s,name).duration,Number(duration));assert.equal(rule(s,name).easing,'Linear');assert.equal(s.sourceDirty,false);
    }
    await click('Select state group Common');await click('Preview state Calm');
  });
  await check('generated numeric and color transitions render intermediate pixels without history',async()=>{
    const before=await snapshot();await click('Toggle state transition preview');await click('Preview state Emphasis');
    await page.waitForFunction(()=>{const s=globalThis.designSpaceDiagnostics.states;return s.transitioning&&s.progress>.2&&s.progress<.6;});const mid=await snapshot();
    assert.ok(Number(preview(mid,box,'Opacity'))>.2&&Number(preview(mid,box,'Opacity'))<1);assert.ok(Number(preview(mid,box,'Canvas.Left'))>180&&Number(preview(mid,box,'Canvas.Left'))<380);
    const rgb=await pixel(725,300);assert.ok(rgb.every(c=>c>10&&c<245),`intermediate grayscale pixels ${rgb}`);assert.ok(Math.max(...rgb)-Math.min(...rgb)<3);
    await page.screenshot({path:directory+'/transition-interpolation.png'});const end=await settled();assert.equal(end.revision,before.revision);assert.equal(preview(end,box,'Opacity'),'0.2');assert.equal(preview(end,color,'Fill'),'#FFFFFFFF');assert.equal(end.nodes.find(n=>n.id===box).properties.Opacity,'1');assert.equal(end.sourceDirty,false);
  });
  await check('an interrupted transition resumes from the displayed values',async()=>{
    const revision=(await snapshot()).revision;await click('Preview state Calm');await page.waitForFunction(()=>{const s=globalThis.designSpaceDiagnostics.states;return s.transitioning&&s.progress>.25&&s.progress<.5;});const before=await snapshot();
    await click('Preview state Emphasis');const next=await snapshot();const a=Number(preview(before,box,'Opacity')),b=Number(preview(next,box,'Opacity'));assert.ok(b>.2&&b<1);assert.ok(Math.abs(a-b)<.25,`${a} -> ${b}`);const end=await settled();assert.equal(end.revision,revision);assert.equal(preview(end,box,'Opacity'),'0.2');
  });
  await check('concurrent groups animate disjoint properties and group Base is isolated',async()=>{
    const revision=(await snapshot()).revision;await click('Preview state Calm');await click('Preview state Wide');
    await page.waitForFunction(({box})=>{const s=globalThis.designSpaceDiagnostics.states,w=Number(s.preview.find(p=>p.target===box&&p.property==='Width')?.value);return s.transitioning&&w>120&&w<220;},{box});
    const mid=await snapshot();assert.equal(mid.states.active.Common,'Calm');assert.equal(mid.states.active.Focus,'Wide');assert.ok(Number(preview(mid,box,'Opacity'))<1);await settled();
    await click('Preview group base Common');const end=await settled();assert.equal(end.states.active.Common,undefined);assert.equal(end.states.active.Focus,'Wide');assert.equal(preview(end,box,'Width'),'220');assert.equal(end.revision,revision);
    await page.screenshot({path:directory+'/concurrent-state-groups.png'});
  });
  await check('disabling transition preview completes the destination without editing the model',async()=>{
    const revision=(await snapshot()).revision;await click('Preview state Compact');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.states.transitioning);await click('Toggle state transition preview');const s=await settled();assert.equal(s.states.transitionsEnabled,false);assert.equal(preview(s,box,'Width'),'120');assert.equal(s.revision,revision);
  });
  await check('group and state creation, rename and deletion are undoable',async()=>{
    await editor();await click('Reload visual transition draft');await edit('Visual state group name','ValidationStates');const before=await snapshot();await click('Add visual state group');await changed(before.revision);
    await edit('New visual state name','Invalid');await click('Add visual state');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.states.items.some(s=>s.name==='Invalid'&&s.group==='ValidationStates'));
    await editor();await click('Reload visual transition draft');await edit('Visual state group name','Validation');await click('Rename visual state group');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.states.items.some(s=>s.name==='Invalid'&&s.group==='Validation'));
    const revision=(await snapshot()).revision;await click('Delete visual state group');const deleted=await changed(revision);assert.ok(!deleted.states.items.some(s=>s.name==='Invalid'));await click('Undo');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.states.items.some(s=>s.name==='Invalid'&&s.group==='Validation'));
  });
  await check('specific transition selectors and easing are editable and rename-safe',async()=>{
    await click('Select state group Common');await editor();await click('Reload visual transition draft');await edit('Transition from state','Calm');await edit('Transition to state','Emphasis');await edit('Transition duration','1.5');
    await click('Transition easing');await page.keyboard.press('End');await page.keyboard.press('Enter');await wait();let before=await snapshot();await click('Save visual transition');let s=await changed(before.revision);assert.equal(rule(s,'Common','Calm','Emphasis').easing,'EaseInOut');assert.ok(s.xaml.includes('GeneratedEasingFunction'));
    await click('Preview state Emphasis');await edit('New visual state name','Highlighted');before=await snapshot();await click('Rename visual state');s=await changed(before.revision);assert.ok(rule(s,'Common','Calm','Highlighted'));assert.ok(!s.states.items.some(s=>s.name==='Emphasis'));
  });
  await check('invalid and stale transition drafts do not mutate the design',async()=>{
    await editor();await click('Reload visual transition draft');await edit('Transition duration','-1');const before=await snapshot();await click('Save visual transition');await wait();assert.equal((await snapshot()).revision,before.revision);assert.equal(rule(await snapshot(),'Common').duration,3);
    await edit('Transition duration','0.75');await click('Undo');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.states.items.some(s=>s.name==='Emphasis'));const stale=await snapshot();await click('Save visual transition');await wait();assert.equal((await snapshot()).revision,stale.revision);assert.equal(rule(await snapshot(),'Common').duration,3);await click('Reload visual transition draft');
  });
  await check('generated transitions and groups persist through native save and browser recovery',async()=>{
    const pending=page.waitForEvent('download');await click('Save design');const download=await pending;await download.saveAs(directory+'/state-groups.designspace');const doc=JSON.parse(await readFile(directory+'/state-groups.designspace','utf8'));
    assert.ok(doc.stateGroups.some(g=>g.name==='Common'&&g.transitions.some(t=>t.from==='Calm'&&t.to==='Emphasis')));assert.ok(doc.states.some(s=>s.group==='Validation'));await page.waitForTimeout(1600);await page.reload({waitUntil:'domcontentloaded'});
    await page.waitForFunction(()=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.states.groups.some(g=>g.name==='Validation'),null,{timeout:180000});const s=await snapshot();assert.equal(rule(s,'Common','Calm','Emphasis').duration,1.5);assert.deepEqual(s.states.active,{});assert.equal(s.sourceDirty,false);await states();await editor();await click('Select state group Common');await click('Preview state Calm');await page.screenshot({path:directory+'/state-transition-workspace.png'});
  });
}
