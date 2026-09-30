import {chromium} from 'playwright';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import {PNG} from 'pngjs';

const base=process.argv[2]||'http://127.0.0.1:4173/DesignSpace/';
const directory='artifacts/verification/artboard';await mkdir(directory,{recursive:true});
const browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-webgl','--enable-unsafe-swiftshader','--no-sandbox']});
const context=await browser.newContext({viewport:{width:1600,height:1000},deviceScaleFactor:1,acceptDownloads:true});
const page=await context.newPage(),errors=[],log=[],results=[];
page.on('console',m=>log.push(m.type()+': '+m.text()));page.on('pageerror',e=>errors.push(e.message));
const snapshot=()=>page.evaluate(()=>globalThis.designSpaceDiagnostics);
const settle=()=>page.waitForTimeout(450);
const node=(s,name)=>s.nodes.find(n=>n.name===name);
const preview=(s,name)=>s.timeline.preview.find(n=>n.id===node(s,name).id);
const coordinate=(s,x,y)=>({x:s.surface.x+s.surface.panX+x*s.surface.zoom,y:s.surface.y+s.surface.panY+y*s.surface.zoom});
const near=(expected,actual,tolerance=.002)=>assert.ok(Math.abs(expected-actual)<=tolerance,`Expected ${expected}; got ${actual}`);
async function check(name,run){await run();results.push(name);console.log('PASS',name);}
async function control(name){await page.waitForFunction(n=>globalThis.designSpaceDiagnostics?.controls.some(c=>c.Name===n),name,{timeout:15000});return(await snapshot()).controls.find(c=>c.Name===name);}
async function reveal(name){
  for(let i=0;i<16;i++){
    const c=await control(name),s=await snapshot(),v=s.controls.find(c=>c.Name==='Artboard settings scroll area');
    if(!name.startsWith('Artboard ')||name==='Artboard settings scroll area')return c;
    assert.ok(v,'Settings viewport');const top=v.Y+4,bottom=v.Y+v.Height-4;
    if(c.Y>=top&&c.Y+c.Height<=bottom)return c;
    await page.mouse.move(v.X+v.Width-18,v.Y+v.Height/2);await page.mouse.wheel(0,c.Y<top?-230:230);await settle();
  }
  throw new Error('Cannot reveal '+name);
}
async function click(name){let c=await reveal(name);await settle();c=await reveal(name);await page.mouse.click(c.X+c.Width/2,c.Y+c.Height/2);await settle();}
async function edit(name,value){
  await click(name);await page.waitForFunction(n=>globalThis.designSpaceDiagnostics.focus===n,name);
  await page.keyboard.press('Control+A');await page.keyboard.insertText(value);
  await page.waitForFunction(({name,value})=>globalThis.designSpaceDiagnostics.controls.find(c=>c.Name===name)?.Text===value,{name,value});await page.keyboard.press('Tab');await settle();
}
async function select(name,add=false){
  const s=await snapshot(),b=preview(s,name),p=coordinate(s,b.x+b.width/2,b.y+b.height/2);
  if(add)await page.keyboard.down('Control');await page.mouse.click(p.x,p.y);if(add)await page.keyboard.up('Control');await settle();
}
async function startMove(x,y,{alt=false,name='Moving'}={}){
  const s=await snapshot(),b=preview(s,name),a=coordinate(s,b.x+b.width/2,b.y+b.height/2),z=coordinate(s,x+b.width/2,y+b.height/2);
  await page.mouse.move(a.x,a.y);await page.mouse.down();if(alt)await page.keyboard.down('Alt');
  await page.mouse.move(z.x,z.y,{steps:14});await settle();return s;
}
async function finish(before,{alt=false,changed=true}={}){
  await page.mouse.up();if(alt)await page.keyboard.up('Alt');
  if(changed)await page.waitForFunction(r=>globalThis.designSpaceDiagnostics.revision===r+1,before.revision,{timeout:15000});
  await page.waitForFunction(()=>{const g=globalThis.designSpaceDiagnostics.snapping.guides;return g.XGuide===null&&g.YGuide===null;});
  await settle();const s=await snapshot();
  assert.equal(s.revision,before.revision+(changed?1:0));assert.equal(s.snapping.guides.XGuide,null);assert.equal(s.snapping.guides.YGuide,null);return s;
}
async function undo(){const s=await snapshot();await click('Undo');await page.waitForFunction(r=>globalThis.designSpaceDiagnostics.revision===r+1,s.revision);await settle();}
async function activate(id){const s=await snapshot(),d=s.workspace.documents.find(d=>d.id===id);await click('Document tab '+d.title);await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active===id,id);}
const fixture=`<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="SnapRoot" Width="960" Height="560" Background="White">
  <Rectangle x:Name="Reference" Canvas.Left="300" Canvas.Top="140" Width="100" Height="80" Fill="#FF7353BA"/>
  <Rectangle x:Name="Moving" Canvas.Left="100" Canvas.Top="300" Width="60" Height="50" Fill="#FF0078D4"/>
  <Rectangle x:Name="Companion" Canvas.Left="170" Canvas.Top="300" Width="30" Height="50" Fill="#FF009B84"/>
  <Rectangle x:Name="HiddenReference" Canvas.Left="297" Canvas.Top="430" Width="30" Height="30" Fill="Red" Visibility="Hidden"/>
</Canvas>`;
let documentId;
try{
  await page.goto(base+'?diagnostics=1',{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.drawCount>0,null,{timeout:180000});
  await check('snapline fixture opens without changing legacy defaults',async()=>{
    const count=(await snapshot()).workspace.documents.length,pending=page.waitForEvent('filechooser');await click('Open design');await(await pending).setFiles({name:'SnaplineStudy.xaml',mimeType:'text/plain',buffer:Buffer.from(fixture)});
    await page.waitForFunction(c=>globalThis.designSpaceDiagnostics.workspace.documents.length===c+1&&globalThis.designSpaceDiagnostics.nodes.some(n=>n.name==='Moving'),count);
    documentId=(await snapshot()).workspace.active;await click('Design view');await click('Fit artboard');await click('Open Artboard settings');const s=await snapshot();assert.equal(s.snapping.settings.SnapToSnaplines,false);assert.equal(s.snapping.settings.SnapToGrid,true);
  });
  await check('settings apply atomically without geometry or undo history',async()=>{
    const before=await snapshot();await click('Artboard Snap grid');await click('Artboard Snap lines');await edit('Artboard Padding','12');assert.deepEqual((await snapshot()).snapping.settings,before.snapping.settings);
    await click('Apply artboard settings');const s=await snapshot();assert.equal(s.snapping.settings.SnapToSnaplines,true);assert.equal(s.snapping.settings.SnapToGrid,false);assert.equal(s.snapping.settings.DefaultPadding,12);assert.equal(s.revision,before.revision);assert.equal(s.xaml,before.xaml);
  });
  await check('View menu and settings panel share one preference baseline',async()=>{
    const before=await snapshot();
    async function menu(label){await click('View');await click('Menu View · '+label);}
    await menu('Show / hide grid');assert.equal((await snapshot()).snapping.settings.ShowGrid,true);
    await edit('Artboard Margin','9');await click('Apply artboard settings');let s=await snapshot();assert.equal(s.snapping.settings.DefaultMargin,9);assert.equal(s.snapping.settings.ShowGrid,true);
    await edit('Artboard Margin','8');await click('Apply artboard settings');await menu('Show / hide grid');
    for(const [label,key] of [['Show / hide rulers','ShowRulers'],['Snap to gridlines','SnapToGrid'],['Snap to snaplines','SnapToSnaplines']]){
      const original=(await snapshot()).snapping.settings[key];await menu(label);assert.equal((await snapshot()).snapping.settings[key],!original);await menu(label);
    }
    await menu('Artboard options');s=await snapshot();assert.deepEqual(s.snapping.settings,before.snapping.settings);assert.equal(s.revision,before.revision);assert.equal(s.xaml,before.xaml);
  });
  await check('edge snapping previews real geometry and pixels before one undoable commit',async()=>{
    const before=await startMove(297,300),s=await snapshot();near(300,preview(s,'Moving').x);assert.equal(s.revision,before.revision);assert.equal(node(s,'Moving').properties['Canvas.Left'],'100');assert.equal(s.snapping.targets,2);assert.equal(s.snapping.indexBuilds,before.snapping.indexBuilds+1);
    assert.equal(s.snapping.guides.SnappedX,true);const p=coordinate(s,300,250),png=PNG.sync.read(await page.screenshot());let pixels=0;
    for(let y=Math.round(p.y)-2;y<=Math.round(p.y)+2;y++)for(let x=Math.round(p.x)-2;x<=Math.round(p.x)+2;x++){const i=(y*png.width+x)*4;if(png.data[i]>200&&png.data[i+1]<140&&png.data[i+2]>95)pixels++;}
    assert.ok(pixels>0,'Visible alignment guide');await page.screenshot({path:directory+'/snapline-alignment.png'});const after=await finish(before);assert.equal(node(after,'Moving').properties['Canvas.Left'],'300');await undo();near(100,preview(await snapshot(),'Moving').x);
  });
  await check('center and right-edge alignment use the closest coordinate',async()=>{
    for(const [raw,expected] of [[319,320],[338,340]]){const before=await startMove(raw,300);near(expected,preview(await snapshot(),'Moving').x);await finish(before);await undo();}
  });
  await check('Alt bypasses snaplines during a captured drag',async()=>{
    const before=await startMove(297,300,{alt:true}),s=await snapshot();near(297,preview(s,'Moving').x);assert.equal(s.snapping.guides.SnappedX,false);await finish(before,{alt:true});await undo();
  });
  await check('default margin has a distance guide between facing objects',async()=>{
    const before=await startMove(409,150),s=await snapshot();near(408,preview(s,'Moving').x);near(8,s.snapping.guides.XGuide.Distance);await page.screenshot({path:directory+'/snapline-spacing.png'});await finish(before);await undo();
  });
  await check('default padding snaps inside the parent content area',async()=>{
    const before=await startMove(10,360),s=await snapshot();near(12,preview(s,'Moving').x);near(12,s.snapping.guides.XGuide.Distance);await finish(before);await undo();
  });
  await check('resize snapping retains the fixed opposite edge',async()=>{
    await select('Moving');const before=await snapshot(),a=coordinate(before,160,325),b=coordinate(before,297,325);
    await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:14});await settle();let s=await snapshot();near(100,preview(s,'Moving').x);near(200,preview(s,'Moving').width);assert.equal(s.revision,before.revision);await finish(before);await undo();
  });
  await check('multi-selection snapping preserves relative offsets and builds one index',async()=>{
    await select('Moving');await select('Companion',true);assert.deepEqual((await snapshot()).selection.sort(),['Companion','Moving']);const before=await startMove(297,300),s=await snapshot();near(300,preview(s,'Moving').x);near(370,preview(s,'Companion').x);assert.equal(s.snapping.indexBuilds,before.snapping.indexBuilds+1);assert.equal(s.snapping.targets,1);await finish(before);await undo();await select('Reference');await select('Moving');
  });
  await check('Escape cancels snapped previews without a history entry',async()=>{
    const before=await startMove(297,300);near(300,preview(await snapshot(),'Moving').x);await page.keyboard.press('Escape');await settle();await finish(before,{changed:false});near(100,preview(await snapshot(),'Moving').x);
  });
  await check('returning to the pointer origin cannot commit snapped jitter',async()=>{
    const before=await startMove(297,300),a=coordinate(before,130,325);await page.mouse.move(a.x+1,a.y+1,{steps:8});await settle();await finish(before,{changed:false});near(100,preview(await snapshot(),'Moving').x);
  });
  await check('snap tolerance stays in screen pixels after zoom changes',async()=>{
    for(let i=0;i<2;i++){const s=await snapshot(),raw=300-4/s.surface.zoom,before=await startMove(raw,300);near(300,preview(await snapshot(),'Moving').x);await finish(before);await undo();await click('Zoom in');}
    await click('Fit artboard');
  });
  await check('grid fallback cannot move the matching snapline axis',async()=>{
    await click('Toggle grid snapping');const before=await startMove(297,300),s=await snapshot();near(300,preview(s,'Moving').x);await finish(before);await undo();await click('Toggle grid snapping');
  });
  await check('disabled snaplines leave raw movement unchanged',async()=>{
    await click('Toggle snapline snapping');const before=await startMove(297,360),s=await snapshot();near(297,preview(s,'Moving').x);assert.equal(s.snapping.guides.SnappedX,false);await finish(before);await undo();await click('Toggle snapline snapping');
  });
  await check('invalid settings and stale drafts cannot partially apply',async()=>{
    await click('Open Artboard settings');const before=await snapshot();await edit('Artboard Margin','24');await edit('Artboard Tolerance','NaN');await click('Apply artboard settings');assert.deepEqual((await snapshot()).snapping.settings,before.snapping.settings);assert.equal((await snapshot()).revision,before.revision);
    await click('Reload artboard settings');await edit('Artboard Margin','24');await click('Toggle snapline snapping');const changed=(await snapshot()).snapping.settings;await click('Apply artboard settings');assert.deepEqual((await snapshot()).snapping.settings,changed);assert.ok((await snapshot()).status.includes('Reload'));await click('Reload artboard settings');await click('Toggle snapline snapping');
  });
  await check('artboard preferences and invalid drafts stay with their document',async()=>{
    await edit('Artboard Margin','retained invalid');const before=await snapshot();await click('New design');await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active!==id,documentId);assert.equal((await snapshot()).snapping.settings.SnapToSnaplines,false);
    await activate(documentId);await click('Open Artboard settings');const s=await snapshot();assert.equal(s.snapping.settings.SnapToSnaplines,true);assert.equal(s.controls.find(c=>c.Name==='Artboard Margin').Text,'retained invalid');assert.equal(s.xaml,before.xaml);
    const pending=page.waitForEvent('download');await click('Save workspace');const file=await pending;await file.saveAs(directory+'/snaplines.designspace-workspace');const w=JSON.parse(await readFile(directory+'/snaplines.designspace-workspace','utf8')),d=w.documents.find(d=>d.id===documentId);assert.equal(d.editor.snapToSnaplines,true);assert.equal(d.editor.defaultPadding,12);assert.equal(d.editor.panels.Artboard.values.Margin,'retained invalid');
  });
  await check('workspace recovery preserves preferences separately from unapplied text',async()=>{
    await page.waitForTimeout(1800);await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(id=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.workspace.active===id,documentId,{timeout:180000});await click('Open Artboard settings');await reveal('Artboard Margin');let s=await snapshot();assert.equal(s.snapping.settings.SnapToSnaplines,true);assert.equal(s.controls.find(c=>c.Name==='Artboard Margin').Text,'retained invalid');await click('Apply artboard settings');assert.deepEqual((await snapshot()).snapping.settings,s.snapping.settings);await click('Reload artboard settings');await page.screenshot({path:directory+'/artboard-settings-recovery.png'});
  });
  const s=await snapshot();assert.deepEqual(errors,[]);assert.deepEqual(s.rendering.warnings,[]);assert.deepEqual(s.rendering.previewWarnings,[]);assert.ok(!log.some(l=>l.startsWith('error: [DesignSpace')));
}catch(error){await writeFile(directory+'/failure.txt',String(error.stack??error));await page.screenshot({path:directory+'/failure.png'}).catch(()=>{});throw error;}
finally{await writeFile(directory+'/results.json',JSON.stringify({passed:results.length,tests:results,errors},null,2));await writeFile(directory+'/diagnostics.json',JSON.stringify(await snapshot().catch(()=>null)??null,null,2));await writeFile(directory+'/browser.log',log.join('\n'));await browser.close();}
