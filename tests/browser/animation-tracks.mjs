import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {PNG} from 'pngjs';

// Inputs are real pointer/keyboard/file events; diagnostics provide observations only.
export async function animationTracks({page,snapshot,click,check,directory}) {
  const fixture=`<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="MotionRoot" Width="960" Height="560" Background="White">
  <Canvas.Resources><Storyboard x:Key="MotionStudy" Duration="0:0:8">
  <DoubleAnimationUsingKeyFrames Storyboard.TargetName="OpacityTarget" Storyboard.TargetProperty="Opacity" Duration="0:0:2" BeginTime="0:0:1"><LinearDoubleKeyFrame KeyTime="0:0:0" Value="0.2"/><SplineDoubleKeyFrame KeyTime="0:0:2" Value="1" KeySpline="0.42,0 0.58,1"/></DoubleAnimationUsingKeyFrames>
  <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SlideTarget" Storyboard.TargetProperty="(Canvas.Left)" Duration="0:0:2" BeginTime="0:0:0.5" SpeedRatio="2" RepeatBehavior="2x"><LinearDoubleKeyFrame KeyTime="0:0:0" Value="80"/><LinearDoubleKeyFrame KeyTime="0:0:2" Value="580"/></DoubleAnimationUsingKeyFrames>
  <DoubleAnimationUsingKeyFrames Storyboard.TargetName="SingleKeyTarget" Storyboard.TargetProperty="Width" Duration="0:0:2" BeginTime="0:0:1" SpeedRatio="2"><SplineDoubleKeyFrame KeyTime="0:0:1" Value="120" KeySpline="0.3,0.1 0.7,0.9"/></DoubleAnimationUsingKeyFrames>
  </Storyboard></Canvas.Resources>
  <TextBlock x:Name="Title" Canvas.Left="80" Canvas.Top="40" Width="750" Height="45" Text="Animation / independent clocks and spline easing" FontSize="24"/>
  <Rectangle x:Name="OpacityTarget" Canvas.Left="80" Canvas.Top="120" Width="300" Height="75" Fill="#FFFF0000" Opacity="0.4"/>
  <Rectangle x:Name="SlideTarget" Canvas.Left="80" Canvas.Top="240" Width="90" Height="60" Fill="#FF0078D4"/>
  <Rectangle x:Name="SingleKeyTarget" Canvas.Left="80" Canvas.Top="360" Width="40" Height="60" Fill="#FF078773"/>
  </Canvas>`;
  let documentId,opacityId,singleId;
  const settle=()=>page.waitForTimeout(400);
  const tracks=s=>s.timeline.details.tracks;
  const track=(s,id)=>tracks(s).find(t=>t.target===id);
  const preview=(s,id)=>s.timeline.preview.find(n=>n.id===id);
  async function changed(revision){await page.waitForFunction(r=>globalThis.designSpaceDiagnostics.revision===r+1,revision,{timeout:15000});await settle();const s=await snapshot();assert.equal(s.revision,revision+1);return s;}
  async function reveal(name){
    await page.waitForFunction(name=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name===name),name,{timeout:15000});
    for(let i=0;i<12;i++){
      const s=await snapshot(),c=s.controls.find(c=>c.Name===name);assert.ok(c,name);
      if(c.Y>125&&c.Y+c.Height<s.height-55)return c;
      await page.mouse.move(s.width-110,470);await page.mouse.wheel(0,c.Y<125?-260:260);await settle();
    }
    throw new Error(`Cannot reveal ${name}`);
  }
  async function press(name){const c=await reveal(name);await page.mouse.click(c.X+c.Width/2,c.Y+c.Height/2);await settle();}
  async function edit(name,value){await press(name);await page.keyboard.press('Control+A');await page.keyboard.insertText(value);await page.keyboard.press('Tab');await settle();}
  async function choose(name,index){await press(name);await page.keyboard.press('Home');for(let i=0;i<index;i++)await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');await settle();}
  async function editor(){await click('Open Animation panel');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name==='Animation Duration'));}
  async function row(index){const c=(await snapshot()).controls.find(c=>c.Name==='Animation timeline');await page.mouse.click(c.X+45,c.Y+40+index*25);await settle();await editor();}
  async function apply(){const s=await snapshot();await click('Apply animation track');return changed(s.revision);}
  async function scrub(time){await edit('Animation preview time',String(time));await press('Scrub animation preview');await page.waitForFunction(t=>Math.abs(globalThis.designSpaceDiagnostics.timeline.time-t)<1e-6,time);await settle();return snapshot();}
  async function document(id){const s=await snapshot(),d=s.workspace.documents.find(d=>d.id===id);await click('Document tab '+d.title);await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active===id,id);}
  await check('cancelled browser upload cleans up without document changes',async()=>{
    const before=await snapshot(),pending=page.waitForEvent('filechooser');await click('Open design');await(await pending).setFiles([]);
    await page.waitForFunction(()=>!globalThis.designSpaceDiagnostics.workspace.busy);await settle();const after=await snapshot();assert.equal(after.revision,before.revision);assert.equal(after.workspace.documents.length,before.workspace.documents.length);assert.equal(await page.locator('input[data-designspace-upload]').count(),0);
  });
  await check('oversized browser upload is rejected before decoding',async()=>{
    const before=await snapshot(),pending=page.waitForEvent('filechooser');await click('Open design');await(await pending).setFiles({name:'TooLarge.xaml',mimeType:'text/plain',buffer:Buffer.alloc(8*1024*1024+1,32)});
    await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.status.includes('file size limit'));const s=await snapshot();assert.equal(s.revision,before.revision);assert.equal(s.workspace.documents.length,before.workspace.documents.length);assert.equal(await page.locator('input[data-designspace-upload]').count(),0);
  });
  await check('timed spline animation imports into an isolated document',async()=>{
    const count=(await snapshot()).workspace.documents.length,pending=page.waitForEvent('filechooser');await click('Open design');await(await pending).setFiles({name:'MotionStudy.xaml',mimeType:'text/plain',buffer:Buffer.from(fixture)});
    await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.workspace.documents.length===count+1&&globalThis.designSpaceDiagnostics.nodes.some(n=>n.name==='OpacityTarget'),count);
    const s=await snapshot();documentId=s.workspace.active;opacityId=s.nodes.find(n=>n.name==='OpacityTarget').id;singleId=s.nodes.find(n=>n.name==='SingleKeyTarget').id;assert.equal(tracks(s).length,3);assert.equal(track(s,opacityId).timing.begin,1);assert.equal(track(s,opacityId).keys[1].easing,'Spline');await click('Design view');await click('Fit artboard');await row(0);
  });
  await check('parent scrubbing respects child delay speed and spline midpoint',async()=>{
    const before=await snapshot();let s=await scrub(.5);assert.ok(Math.abs(preview(s,opacityId).opacity-.4)<.001);
    s=await scrub(2);assert.ok(Math.abs(preview(s,opacityId).opacity-.6)<.001);const slide=s.nodes.find(n=>n.name==='SlideTarget');assert.ok(Math.abs(preview(s,slide.id).x-330)<.001);assert.equal(s.revision,before.revision);
    const png=PNG.sync.read(await page.screenshot()),v=s.surface,x=Math.round(v.x+v.panX+131*v.zoom),y=Math.round(v.y+v.panY+151*v.zoom),i=(y*png.width+x)*4;assert.ok(png.data[i]>245&&Math.abs(png.data[i+1]-102)<12,'The animated opacity is painted');
  });
  await check('track settings and spline preset commit as one transaction',async()=>{
    const before=await snapshot();await edit('Animation Begin','2');await edit('Animation Speed','2');await edit('Animation Repeat amount','2');await press('Animation auto reverse');await press('Spline preset Ease both');assert.equal((await snapshot()).revision,before.revision);const s=await apply(),t=track(s,opacityId);assert.equal(t.timing.begin,2);assert.equal(t.timing.speed,2);assert.equal(t.timing.count,2);assert.equal(t.timing.reverse,true);assert.equal(t.keys[1].spline,'0.42,0 0.58,1');assert.equal(s.sourceDirty,false);
  });
  await check('child reverse and repeated phases are sampled in parent coordinates',async()=>{
    const before=await snapshot();let s=await scrub(3);assert.ok(Math.abs(preview(s,opacityId).opacity-1)<.001);s=await scrub(3.5);assert.ok(Math.abs(preview(s,opacityId).opacity-.6)<.001);s=await scrub(4);assert.ok(Math.abs(preview(s,opacityId).opacity-.2)<.001);assert.equal(s.revision,before.revision);
  });
  await check('spline handle dragging edits only the draft until Apply',async()=>{
    const before=await snapshot(),c=await reveal('Key spline curve');const x=c.X+20+.42*(c.Width-40),y=c.Y+c.Height-20;
    await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(c.X+20+.25*(c.Width-40),c.Y+20+.9*(c.Height-40),{steps:12});await page.mouse.up();await settle();
    let s=await snapshot();assert.equal(s.revision,before.revision);const field=s.controls.find(c=>c.Name==='Animation Key spline').Text;assert.notEqual(field,'0.42,0 0.58,1');s=await apply();const points=track(s,opacityId).keys[1].spline.split(/[ ,]+/).map(Number);assert.ok(Math.abs(points[0]-.25)<.02&&Math.abs(points[1]-.1)<.02);await reveal('Key spline curve');await page.screenshot({path:directory+'/animation-spline-editor.png'});
  });
  await check('invalid spline draft cannot modify the design',async()=>{
    const before=await snapshot();await edit('Animation Key spline','0,0 2,1');await click('Apply animation track');await settle();assert.equal((await snapshot()).revision,before.revision);assert.equal((await snapshot()).xaml,before.xaml);assert.equal((await snapshot()).controls.find(c=>c.Name==='Animation Key spline').Text,'0,0 2,1');await click('Reload animation track');
  });
  await check('stale animation draft cannot overwrite undo or redo',async()=>{
    await edit('Animation Begin','3');const before=await snapshot();await click('Undo');await changed(before.revision);const stale=await snapshot();await click('Apply animation track');await settle();assert.equal((await snapshot()).revision,stale.revision);assert.ok((await snapshot()).status.includes('stale'));await click('Reload animation track');await click('Redo');await changed(stale.revision);
  });
  await check('dragging a sole key preserves its child clock and spline',async()=>{
    const before=await snapshot(),t=track(before,singleId),key=t.keys[0],c=before.controls.find(c=>c.Name==='Animation timeline');
    await page.mouse.move(c.X+key.x,c.Y+key.y);await page.mouse.down();await page.mouse.move(c.X+key.x+40,c.Y+key.y,{steps:12});await page.mouse.up();const s=await changed(before.revision),moved=track(s,singleId);assert.equal(moved.keys.length,1);assert.deepEqual(moved.timing,t.timing);assert.equal(moved.keys[0].spline,key.spline);assert.ok(moved.keys[0].time>key.time);await editor();
  });
  await check('track-duration scaling retimes its keys without changing parent duration',async()=>{
    const before=await snapshot(),old=track(before,singleId);await edit('Animation Duration','4');await press('Scale track keys');const s=await apply();assert.equal(s.timeline.duration,before.timeline.duration);assert.equal(track(s,singleId).timing.duration,4);assert.ok(Math.abs(track(s,singleId).keys[0].time-old.keys[0].time*2)<1e-6);const r=s.revision;await click('Undo');await changed(r);await row(0);
  });
  await check('child Stop restores the base value without editing source',async()=>{
    await choose('Animation Fill behavior',1);await apply();const before=await snapshot();let s=await scrub(3);assert.ok(preview(s,opacityId).opacity>.99);s=await scrub(6);assert.ok(Math.abs(preview(s,opacityId).opacity-.4)<.001);assert.equal(s.revision,before.revision);assert.equal(s.xaml,before.xaml);await reveal('Key spline curve');await page.screenshot({path:directory+'/animation-child-clocks.png'});
  });
  await check('native save preserves child timing and spline controls',async()=>{
    const pending=page.waitForEvent('download');await click('Save design');const file=await pending;await file.saveAs(directory+'/animation-study.designspace');const d=JSON.parse(await readFile(directory+'/animation-study.designspace','utf8')),t=d.storyboards[0].tracks.find(t=>t.targetId===opacityId);assert.equal(t.timing.beginTime,2);assert.equal(t.timing.fillBehavior,'Stop');assert.equal(t.keys[1].easing,'Spline');assert.ok(Math.abs(t.keys[1].spline.x1-.25)<.02);
  });
  await check('animation drafts remain isolated across document tabs',async()=>{
    await edit('Animation Duration','invalid duration');const before=await snapshot();await click('New design');await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active!==id,documentId);const other=(await snapshot()).workspace.active;await document(documentId);await editor();assert.equal((await snapshot()).controls.find(c=>c.Name==='Animation Duration').Text,'invalid duration');assert.equal((await snapshot()).xaml,before.xaml);
    const pending=page.waitForEvent('download');await click('Save workspace');const file=await pending;await file.saveAs(directory+'/animation-workspace.designspace-workspace');const w=JSON.parse(await readFile(directory+'/animation-workspace.designspace-workspace','utf8'));assert.equal(w.documents.find(d=>d.id===documentId).editor.panels.Animation.values.Duration,'invalid duration');assert.equal(w.documents.find(d=>d.id===other).editor.panels.Animation?.hasChanges??false,false);
  });
  await check('animation draft and native child clock survive workspace recovery',async()=>{
    await page.waitForTimeout(1800);await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(id=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.workspace.active===id,documentId,{timeout:180000});await editor();assert.equal((await snapshot()).controls.find(c=>c.Name==='Animation Duration').Text,'invalid duration');assert.equal(track(await snapshot(),opacityId).timing.fill,'Stop');const before=await snapshot();await click('Apply animation track');await settle();assert.equal((await snapshot()).revision,before.revision);await click('Reload animation track');await reveal('Key spline curve');await page.screenshot({path:directory+'/animation-workspace.png'});
  });
}
