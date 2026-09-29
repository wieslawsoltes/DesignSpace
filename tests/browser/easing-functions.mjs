import assert from 'node:assert/strict';
import {PNG} from 'pngjs';
import {readFile} from 'node:fs/promises';

// These tests never mutate through diagnostics: only real file, keyboard and pointer events.
export async function easingFunctions({page,snapshot,click,check,directory}) {
  const fixture=`<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="EasingRoot" Width="960" Height="560" Background="White">
    <Canvas.Resources><Storyboard x:Key="EasingStudy" Duration="0:0:5"><DoubleAnimationUsingKeyFrames Storyboard.TargetName="Ball" Storyboard.TargetProperty="(Canvas.Left)" Duration="0:0:2" BeginTime="0:0:0.25"><LinearDoubleKeyFrame KeyTime="0:0:0" Value="160"/><LinearDoubleKeyFrame KeyTime="0:0:2" Value="600"/></DoubleAnimationUsingKeyFrames></Storyboard></Canvas.Resources>
    <TextBlock x:Name="Title" Canvas.Left="64" Canvas.Top="50" Width="820" Height="50" Text="Animation / bounce, spring and overshoot" FontSize="28" Foreground="#FF202838"/>
    <Rectangle x:Name="Lane" Canvas.Left="80" Canvas.Top="225" Width="800" Height="2" Fill="#FFD5DAE3"/>
    <Rectangle x:Name="Ball" Canvas.Left="160" Canvas.Top="154" Width="70" Height="70" Fill="#FF0078D4" RadiusX="12" RadiusY="12"/>
    <TextBlock x:Name="Description" Canvas.Left="64" Canvas.Top="320" Width="820" Height="70" Text="Edit built-in easing parameters. Preview output includes overshoot.&#10;Drafts stay isolated until Apply and follow their document tabs." FontSize="18" Foreground="#FF63738A"/>
  </Canvas>`;
  let documentId,targetId;
  const settle=()=>page.waitForTimeout(400);
  const track=s=>s.timeline.details.tracks.find(t=>t.target===targetId);
  const last=s=>track(s).keys.at(-1);
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
  async function press(name){const c=await reveal(name);await page.mouse.click(c.X+c.Width/2,c.Y+c.Height/2);await settle();}
  async function edit(name,value){
    await press(name);await page.waitForFunction(n=>globalThis.designSpaceDiagnostics.focus===n,name,{timeout:10000});
    await page.keyboard.press('Control+A');await page.keyboard.insertText(value);
    await page.waitForFunction(({name,value})=>globalThis.designSpaceDiagnostics.controls.find(c=>c.Name===name)?.Text===value,{name,value},{timeout:10000});await page.keyboard.press('Tab');await settle();
  }
  async function choose(name,index){await press(name);await page.keyboard.press('Home');for(let i=0;i<index;i++)await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');await settle();}
  async function editor(){await click('Open Animation panel');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name==='Animation Duration'));}
  async function apply(){const s=await snapshot();await click('Apply animation track');return changed(s.revision);}
  async function scrub(time){await edit('Animation preview time',String(time));await press('Scrub animation preview');await page.waitForFunction(t=>Math.abs(globalThis.designSpaceDiagnostics.timeline.time-t)<1e-6,time);await settle();return snapshot();}
  async function document(id){const d=(await snapshot()).workspace.documents.find(d=>d.id===id);assert.ok(d);await click('Document tab '+d.title);await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active===id,id);}
  function bounceIn(t,count,ratio){const power=Math.pow(ratio,count),units=(1-power)/(1-ratio)+power*.5,bounce=Math.floor(Math.log(1+t*units*(ratio-1))/Math.log(ratio)),a=(1-Math.pow(ratio,bounce))/((1-ratio)*units),b=(1-Math.pow(ratio,bounce+1))/((1-ratio)*units),middle=(a+b)*.5,radius=middle-a,offset=t-middle;return(-Math.pow(1/ratio,count-bounce)/(radius*radius))*(offset-radius)*(offset+radius);}

  await check('easing study opens as a separate editable document',async()=>{
    const count=(await snapshot()).workspace.documents.length,pending=page.waitForEvent('filechooser');await click('Open design');await(await pending).setFiles({name:'EasingStudy.xaml',mimeType:'text/plain',buffer:Buffer.from(fixture)});
    await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.workspace.documents.length===count+1&&globalThis.designSpaceDiagnostics.nodes.some(n=>n.name==='Ball'),count);
    let s=await snapshot();documentId=s.workspace.active;targetId=s.nodes.find(n=>n.name==='Ball').id;await click('Design view');await click('Fit artboard');
    const c=(await snapshot()).controls.find(c=>c.Name==='Animation timeline');await page.mouse.click(c.X+45,c.Y+40);await settle();await editor();assert.equal(last(await snapshot()).easing,'Linear');
  });
  await check('bounce parameters stay a draft then apply in one transaction',async()=>{
    const before=await snapshot();await choose('Animation Easing',6);await choose('Easing Family',1);await choose('Easing Direction',1);await edit('Easing Bounces','5');await edit('Easing Bounciness','2.5');
    assert.equal((await snapshot()).revision,before.revision);assert.equal(last(await snapshot()).easing,'Linear');
    const s=await apply();assert.equal(last(s).function.family,'Bounce');assert.equal(last(s).function.mode,'EaseOut');assert.equal(last(s).function.bounces,5);assert.equal(last(s).function.bounciness,2.5);assert.equal(s.sourceDirty,false);assert.ok(s.xaml.includes('BounceEase'));
  });
  await check('bounce sampling retains child-clock delay and matches its curve',async()=>{
    const before=await snapshot();let s=await scrub(.1);assert.equal(s.timeline.preview.find(n=>n.id===targetId).x,160);
    for(const time of [.75,1.25,1.75]){s=await scrub(time);const t=(time-.25)/2,expected=160+440*(1-bounceIn(1-t,5,2.5));assert.ok(Math.abs(s.timeline.preview.find(n=>n.id===targetId).x-expected)<1e-7);}
    assert.equal(s.revision,before.revision);assert.equal(s.xaml,before.xaml);
  });
  await check('easing preview draws a curve and inspects without document edits',async()=>{
    const c=await reveal('Easing function preview'),before=await snapshot();await page.mouse.move(c.X+30,c.Y+c.Height/2);await page.mouse.down();await page.mouse.move(c.X+c.Width-30,c.Y+c.Height/2,{steps:14});await page.mouse.up();await settle();assert.equal((await snapshot()).revision,before.revision);
    const png=PNG.sync.read(await page.screenshot());let blue=0;
    for(let y=Math.ceil(c.Y+5);y<Math.floor(c.Y+c.Height-5);y++)for(let x=Math.ceil(c.X+5);x<Math.floor(c.X+c.Width-5);x++){const i=(y*png.width+x)*4;if(png.data[i+2]>130&&png.data[i+1]>85&&png.data[i]<130)blue++;}
    assert.ok(blue>40,`Graph curve pixels: ${blue}`);await page.screenshot({path:directory+'/easing-bounce-editor.png'});
  });
  await check('back easing preserves overshoot in values and painted position',async()=>{
    await choose('Easing Family',0);await choose('Easing Direction',1);await edit('Easing Amplitude','1');await apply();const before=await snapshot(),s=await scrub(1.65),t=.7,u=1-t;
    const expected=160+440*(1-(u*u*u-u*Math.sin(Math.PI*u))),actual=s.timeline.preview.find(n=>n.id===targetId).x;assert.ok(actual>600);assert.ok(Math.abs(actual-expected)<1e-7);assert.equal(s.revision,before.revision);
    const png=PNG.sync.read(await page.screenshot()),v=s.surface,x=Math.round(v.x+v.panX+(actual+35)*v.zoom),y=Math.round(v.y+v.panY+189*v.zoom),i=(y*png.width+x)*4;
    assert.ok(png.data[i]<50&&png.data[i+2]>150,'Overshooting shape is rendered at its evaluated position');await reveal('Easing function preview');await page.screenshot({path:directory+'/easing-overshoot.png'});
  });
  await check('elastic family exposes spring controls and preserves its metadata',async()=>{
    await choose('Easing Family',4);await choose('Easing Direction',2);await edit('Easing Oscillations','4');await edit('Easing Springiness','6');const s=await apply();assert.equal(last(s).function.family,'Elastic');assert.equal(last(s).function.mode,'EaseInOut');assert.equal(last(s).function.oscillations,4);assert.equal(last(s).function.springiness,6);assert.ok(s.xaml.includes('ElasticEase'));
  });
  await check('invalid easing parameter remains editable without partial mutation',async()=>{
    const before=await snapshot();await edit('Easing Oscillations','3.5');await click('Apply animation track');await settle();let s=await snapshot();assert.equal(s.revision,before.revision);assert.deepEqual(track(s),track(before));assert.ok(s.status.includes('integer'));
    s=await scrub(1.25);assert.equal(s.revision,before.revision);assert.equal(s.controls.find(c=>c.Name==='Easing Oscillations').Text,'3.5');await click('Reload animation track');
  });
  await check('power-zero keys preserve exact endpoints before jumping',async()=>{
    await choose('Easing Family',6);await choose('Easing Direction',0);await edit('Easing Power','0');await apply();let s=await scrub(.25);assert.equal(s.timeline.preview.find(n=>n.id===targetId).x,160);s=await scrub(.26);assert.equal(s.timeline.preview.find(n=>n.id===targetId).x,600);
  });
  await check('retiming a function key retains parameters and exact undo',async()=>{
    const before=await snapshot();await edit('Animation Key time','1.5');await edit('Animation Value','640');let s=await apply();assert.equal(last(s).time,1.5);assert.equal(last(s).value,640);assert.deepEqual(last(s).function,last(before).function);
    await click('Undo');s=await changed(s.revision);assert.equal(last(s).time,2);assert.equal(last(s).value,600);assert.deepEqual(last(s).function,last(before).function);
  });
  await check('switching interpolation clears function metadata and undo restores it',async()=>{
    const original=last(await snapshot()).function;await choose('Animation Easing',0);let s=await apply();assert.equal(last(s).function,null);assert.equal(last(s).easing,'Linear');await click('Undo');s=await changed(s.revision);assert.deepEqual(last(s).function,original);
  });
  await check('easing function is preserved in native export',async()=>{
    const pending=page.waitForEvent('download');await click('Save design');const file=await pending;await file.saveAs(directory+'/easing-study.designspace');const doc=JSON.parse(await readFile(directory+'/easing-study.designspace','utf8'));const key=doc.storyboards[0].tracks[0].keys.at(-1);assert.equal(key.easing,'Function');assert.equal(key.function.power,0);
  });
  await check('invalid easing drafts follow only their owning document',async()=>{
    await edit('Easing Power','invalid power');const before=await snapshot();await click('New design');await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active!==id,documentId);const other=(await snapshot()).workspace.active;
    await document(documentId);await editor();await reveal('Easing Power');assert.equal((await snapshot()).controls.find(c=>c.Name==='Easing Power').Text,'invalid power');assert.equal((await snapshot()).xaml,before.xaml);
    const pending=page.waitForEvent('download');await click('Save workspace');const file=await pending;await file.saveAs(directory+'/easing-workspace.designspace-workspace');const w=JSON.parse(await readFile(directory+'/easing-workspace.designspace-workspace','utf8'));assert.equal(w.documents.find(d=>d.id===documentId).editor.panels.Animation.values['Function Power'],'invalid power');assert.equal(w.documents.find(d=>d.id===other).editor.panels.Animation?.hasChanges??false,false);
  });
  await check('recovery keeps invalid easing text and valid committed function separate',async()=>{
    await page.waitForTimeout(1800);await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(id=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.workspace.active===id,documentId,{timeout:180000});await editor();await reveal('Easing Power');const before=await snapshot();assert.equal(before.controls.find(c=>c.Name==='Easing Power').Text,'invalid power');assert.equal(last(before).function.power,0);await click('Apply animation track');await settle();assert.equal((await snapshot()).revision,before.revision);await click('Reload animation track');await reveal('Easing function preview');await page.screenshot({path:directory+'/easing-workspace.png'});
  });
}
