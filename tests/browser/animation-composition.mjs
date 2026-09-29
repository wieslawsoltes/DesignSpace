import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {PNG} from 'pngjs';

// Only actual file-picker, pointer and keyboard input can edit the workbench.
export async function animationComposition({page,snapshot,click,check,directory}) {
  const fixture=`<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="CompositionRoot" Width="960" Height="560" Background="White">
    <Canvas.Resources><Storyboard x:Key="CompositionStudy" Duration="0:0:8"><DoubleAnimationUsingKeyFrames Storyboard.TargetName="Block" Storyboard.TargetProperty="(Canvas.Left)" Duration="0:0:2" RepeatBehavior="3x"><LinearDoubleKeyFrame KeyTime="0:0:0" Value="10"/><LinearDoubleKeyFrame KeyTime="0:0:2" Value="110"/></DoubleAnimationUsingKeyFrames></Storyboard></Canvas.Resources>
    <TextBlock x:Name="Title" Canvas.Left="64" Canvas.Top="50" Width="840" Height="50" Text="Animation / additive and cumulative keyframes" FontSize="27" Foreground="#FF202838"/>
    <Rectangle x:Name="Lane" Canvas.Left="64" Canvas.Top="240" Width="820" Height="2" Fill="#FFD5DAE3"/>
    <Rectangle x:Name="Block" Canvas.Left="160" Canvas.Top="165" Width="70" Height="70" Fill="#FF0078D4" RadiusX="10" RadiusY="10"/>
    <TextBlock x:Name="Description" Canvas.Left="64" Canvas.Top="320" Width="820" Height="100" Text="Base position: 160. Key values: 10 to 110. Three child cycles.&#10;Additive uses the base; cumulative adds the final key each cycle.&#10;Draft changes become one undoable edit when applied." FontSize="18" Foreground="#FF63738A"/>
  </Canvas>`;
  let documentId,targetId;
  const settle=()=>page.waitForTimeout(400);
  const track=s=>s.timeline.details.tracks.find(t=>t.target===targetId);
  async function changed(revision){await page.waitForFunction(r=>globalThis.designSpaceDiagnostics.revision===r+1,revision,{timeout:15000});await settle();const s=await snapshot();assert.equal(s.revision,revision+1);return s;}
  async function reveal(name){
    await page.waitForFunction(n=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name===n),name,{timeout:15000});
    for(let i=0;i<14;i++){
      const s=await snapshot(),c=s.controls.find(c=>c.Name===name);assert.ok(c,name);
      if(c.Y>125&&c.Y+c.Height<s.height-55)return c;
      await page.mouse.move(s.width-110,470);await page.mouse.wheel(0,c.Y<125?-250:250);await settle();
    }
    throw new Error(`Cannot reveal ${name}`);
  }
  async function press(name){
    let c=await reveal(name);
    for(let i=0;i<8;i++){await settle();const next=await reveal(name);if(next.X===c.X&&next.Y===c.Y&&next.Width===c.Width&&next.Height===c.Height){c=next;break;}c=next;}
    await page.mouse.click(c.X+c.Width/2,c.Y+c.Height/2);await settle();
  }
  async function edit(name,value){
    await press(name);await page.waitForFunction(n=>globalThis.designSpaceDiagnostics.focus===n,name,{timeout:10000});
    await page.keyboard.press('Control+A');await page.keyboard.insertText(value);
    await page.waitForFunction(({name,value})=>globalThis.designSpaceDiagnostics.controls.find(c=>c.Name===name)?.Text===value,{name,value},{timeout:10000});await page.keyboard.press('Tab');await settle();
  }
  async function editor(){await click('Open Animation panel');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name==='Animation cumulative'));}
  async function apply(){const before=await snapshot();await click('Apply animation track');return changed(before.revision);}
  async function scrub(time){await edit('Animation preview time',String(time));await press('Scrub animation preview');await page.waitForFunction(t=>Math.abs(globalThis.designSpaceDiagnostics.timeline.time-t)<1e-6,time);await settle();return snapshot();}
  async function activate(id){const d=(await snapshot()).workspace.documents.find(d=>d.id===id);assert.ok(d);await click('Document tab '+d.title);await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active===id,id);}
  await check('composition study opens through XAML with backward-compatible false flags',async()=>{
    const count=(await snapshot()).workspace.documents.length,pending=page.waitForEvent('filechooser');await click('Open design');await(await pending).setFiles({name:'CompositionStudy.xaml',mimeType:'text/plain',buffer:Buffer.from(fixture)});
    await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.workspace.documents.length===count+1&&globalThis.designSpaceDiagnostics.nodes.some(n=>n.name==='Block'),count);
    let s=await snapshot();documentId=s.workspace.active;targetId=s.nodes.find(n=>n.name==='Block').id;await click('Design view');await click('Fit artboard');
    const c=(await snapshot()).controls.find(c=>c.Name==='Animation timeline');await page.mouse.click(c.X+45,c.Y+40);await settle();await editor();s=await snapshot();assert.equal(track(s).additive,false);assert.equal(track(s).cumulative,false);
  });
  await check('composition flags stay draft-only until one atomic Apply',async()=>{
    const before=await snapshot();await press('Animation additive');await press('Animation cumulative');let s=await snapshot();assert.equal(s.revision,before.revision);assert.equal(track(s).additive,false);assert.equal(track(s).cumulative,false);
    s=await apply();assert.equal(track(s).additive,true);assert.equal(track(s).cumulative,true);assert.equal(s.sourceDirty,false);assert.ok(s.xaml.includes('IsAdditive="True"')&&s.xaml.includes('IsCumulative="True"'));
  });
  await check('repeat-aware scrubbing samples additive base and cumulative final keys',async()=>{
    const before=await snapshot();
    for(const [time,expected] of [[0,170],[1,220],[2,280],[3,330],[5,440],[6,490],[7,490]]){
      const s=await scrub(time);assert.ok(Math.abs(s.timeline.preview.find(n=>n.id===targetId).x-expected)<1e-8,`sample ${time}`);assert.equal(s.revision,before.revision);assert.equal(s.xaml,before.xaml);assert.equal(s.nodes.find(n=>n.id===targetId).properties['Canvas.Left'],'160');
    }
  });
  await check('cumulative position is painted at the evaluated coordinate',async()=>{
    const s=await scrub(3),v=s.surface,png=PNG.sync.read(await page.screenshot());const x=Math.round(v.x+v.panX+365*v.zoom),y=Math.round(v.y+v.panY+200*v.zoom),i=(y*png.width+x)*4;
    assert.ok(png.data[i]<50&&png.data[i+2]>150);await reveal('Animation additive');await page.screenshot({path:directory+'/animation-composition.png'});
  });
  await check('composition flag changes undo without changing timing or keys',async()=>{
    const before=await snapshot();await press('Animation additive');let s=await apply();assert.equal(track(s).additive,false);assert.equal(track(s).cumulative,true);assert.deepEqual(track(s).keys,track(before).keys);assert.deepEqual(track(s).timing,track(before).timing);
    s=await scrub(3);assert.equal(s.timeline.preview.find(n=>n.id===targetId).x,170);await click('Undo');s=await changed(s.revision);assert.equal(track(s).additive,true);assert.equal(track(s).cumulative,true);
  });
  await check('key value editing retains composition and exact undo',async()=>{
    const before=await snapshot();await edit('Animation Value','120');let s=await apply();assert.equal(track(s).keys.at(-1).value,120);assert.equal(track(s).additive,true);assert.equal(track(s).cumulative,true);await click('Undo');s=await changed(s.revision);assert.deepEqual(track(s).keys,track(before).keys);
  });
  await check('invalid composition draft cannot partially apply flags',async()=>{
    const before=await snapshot();await press('Animation cumulative');await edit('Animation Value','not a number');await click('Apply animation track');await settle();const s=await snapshot();assert.equal(s.revision,before.revision);assert.deepEqual(track(s),track(before));assert.ok(s.status.includes('finite'));await click('Reload animation track');
  });
  await check('native export retains composition and original local keys',async()=>{
    const pending=page.waitForEvent('download');await click('Save design');const file=await pending;await file.saveAs(directory+'/composition-study.designspace');const d=JSON.parse(await readFile(directory+'/composition-study.designspace','utf8')),t=d.storyboards[0].tracks[0];assert.equal(t.isAdditive,true);assert.equal(t.isCumulative,true);assert.equal(t.keys.at(-1).value,110);
    await page.waitForFunction(()=>{const s=globalThis.designSpaceDiagnostics;return !s.dirty&&s.status==='Saved CompositionStudy.designspace';});await settle();
  });
  await check('composition drafts follow document tabs and remain separate from model flags',async()=>{
    await press('Animation additive');await edit('Animation Value','retained invalid');const before=await snapshot();await click('New design');await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active!==id,documentId);const other=(await snapshot()).workspace.active;
    await activate(documentId);await editor();await reveal('Animation Value');let s=await snapshot();assert.equal(s.controls.find(c=>c.Name==='Animation Value').Text,'retained invalid');assert.equal(track(s).additive,true);assert.equal(s.xaml,before.xaml);
    const pending=page.waitForEvent('download');await click('Save workspace');const file=await pending;await file.saveAs(directory+'/composition-workspace.designspace-workspace');const w=JSON.parse(await readFile(directory+'/composition-workspace.designspace-workspace','utf8'));const draft=w.documents.find(d=>d.id===documentId).editor.panels.Animation;assert.equal(draft.values.Additive,'False');assert.equal(draft.values.Cumulative,'True');assert.equal(w.documents.find(d=>d.id===other).editor.panels.Animation?.hasChanges??false,false);
  });
  await check('recovery preserves draft composition without changing committed animation',async()=>{
    await page.waitForTimeout(1800);await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(id=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.workspace.active===id,documentId,{timeout:180000});await editor();await reveal('Animation Value');const before=await snapshot();assert.equal(before.controls.find(c=>c.Name==='Animation Value').Text,'retained invalid');assert.equal(track(before).additive,true);assert.equal(track(before).cumulative,true);await click('Apply animation track');await settle();assert.equal((await snapshot()).revision,before.revision);await click('Reload animation track');const s=await scrub(3);assert.equal(s.timeline.preview.find(n=>n.id===targetId).x,330);await reveal('Animation additive');await page.screenshot({path:directory+'/animation-composition-recovery.png'});
  });
}
