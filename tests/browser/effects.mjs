import {chromium} from 'playwright';
import {PNG} from 'pngjs';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';

// Diagnostics observe the app; every edit uses actual pointer, keyboard or file input.
const base=process.argv[2]||'http://127.0.0.1:4173/DesignSpace/';
const directory='artifacts/verification/effects';await mkdir(directory,{recursive:true});
const browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-webgl','--enable-unsafe-swiftshader','--no-sandbox']});
const context=await browser.newContext({viewport:{width:1600,height:1000},deviceScaleFactor:1,acceptDownloads:true});
const page=await context.newPage(),errors=[],log=[],results=[];
page.on('console',m=>log.push(m.type()+': '+m.text()));page.on('pageerror',e=>errors.push(e.message));
const snapshot=()=>page.evaluate(()=>globalThis.designSpaceDiagnostics);
const settle=()=>page.waitForTimeout(450);
const node=(s,name='Card')=>s.nodes.find(n=>n.name===name);
async function check(name,run){await run();results.push(name);console.log('PASS',name);}
async function reveal(name){
  await page.waitForFunction(n=>globalThis.designSpaceDiagnostics?.controls.some(c=>c.Name===n),name,{timeout:15000});
  const viewportName=name.startsWith('Effect ')?'Effect settings scroll area':name.startsWith('Artboard ')?'Artboard settings scroll area':null;
  for(let i=0;i<16;i++){
    const s=await snapshot(),c=s.controls.find(c=>c.Name===name);assert.ok(c,name);
    const v=viewportName?s.controls.find(c=>c.Name===viewportName):null;
    if(!v||c.Y>=v.Y+4&&c.Y+c.Height<=v.Y+v.Height-4)return c;
    await page.mouse.move(v.X+v.Width-20,v.Y+v.Height/2);await page.mouse.wheel(0,c.Y<v.Y+4?-230:230);await settle();
  }
  throw new Error('Cannot reveal '+name);
}
async function click(name){let c=await reveal(name);await settle();c=await reveal(name);await page.mouse.click(c.X+c.Width/2,c.Y+c.Height/2);await settle();}
async function edit(name,value){
  await click(name);await page.waitForFunction(n=>globalThis.designSpaceDiagnostics.focus===n,name,{timeout:10000});
  await page.keyboard.press('Control+A');await page.keyboard.insertText(value);
  await page.waitForFunction(({name,value})=>globalThis.designSpaceDiagnostics.controls.find(c=>c.Name===name)?.Text===value,{name,value});await page.keyboard.press('Tab');await settle();
}
async function choose(name,index){await click(name);await page.keyboard.press('Home');for(let i=0;i<index;i++)await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');await settle();}
async function changed(before){await page.waitForFunction(r=>globalThis.designSpaceDiagnostics.revision===r+1,before.revision,{timeout:15000});await settle();const s=await snapshot();assert.equal(s.revision,before.revision+1);return s;}
async function apply(){const s=await snapshot();await click('Apply effect settings');return changed(s);}
async function undo(){const s=await snapshot();await click('Undo');return changed(s);}
async function menu(menu,label){await click(menu);await click('Menu '+menu+' · '+label);}
async function point(x,y){const v=(await snapshot()).surface;return{x:v.x+v.panX+x*v.zoom,y:v.y+v.panY+y*v.zoom};}
async function tap(x,y,control=false){const p=await point(x,y);if(control)await page.keyboard.down('Control');await page.mouse.click(p.x,p.y);if(control)await page.keyboard.up('Control');await settle();}
async function select(name='Card',add=false){const s=await snapshot(),n=node(s,name),b=s.timeline.preview.find(e=>e.id===n.id);await tap(b.x+b.width/2,b.y+b.height/2,add);await page.waitForFunction(name=>globalThis.designSpaceDiagnostics.selection.includes(name),name);}
async function editor(){await click('Open Effects panel');await reveal('Effect Type');}
async function pixels(){await settle();const png=PNG.sync.read(await page.screenshot()),v=(await snapshot()).surface;return(x,y)=>{const px=Math.round(v.x+v.panX+x*v.zoom),py=Math.round(v.y+v.panY+y*v.zoom),i=(py*png.width+px)*4;return{r:png.data[i],g:png.data[i+1],b:png.data[i+2]};};}
const white=p=>p.r>248&&p.g>248&&p.b>248;
const blue=p=>p.r<35&&p.b>170;
const shadow=p=>Math.abs(p.r-127)<5&&Math.abs(p.g-127)<5&&p.b>248;
async function activate(id){const s=await snapshot(),d=s.workspace.documents.find(d=>d.id===id);await click('Document tab '+d.title);await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active===id,id);}
const fixture=`<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="EffectsRoot" Width="960" Height="560" Background="White">
 <Canvas.Resources><DropShadowEffect x:Key="SharedShadow" BlurRadius="0" Direction="0" ShadowDepth="100" Opacity="0.6" Color="Black"/><Style x:Key="ShadowStyle" TargetType="Ellipse"><Setter Property="Effect" Value="{StaticResource SharedShadow}"/></Style></Canvas.Resources>
 <TextBlock x:Name="Title" Canvas.Left="64" Canvas.Top="38" Width="830" Height="50" Text="Effects / blur, shadows and live authoring" FontSize="28" Foreground="#FF202838"/>
 <Rectangle x:Name="Card" Canvas.Left="200" Canvas.Top="160" Width="100" Height="80" Fill="#FF0078D4"/>
 <Ellipse x:Name="Styled" Canvas.Left="620" Canvas.Top="160" Width="80" Height="80" Fill="#FF009B84" Style="{StaticResource ShadowStyle}"/>
 <TextBlock x:Name="Hint" Canvas.Left="64" Canvas.Top="290" Width="830" Height="35" Text="Local drafts, shared resources, independent artboard preview" FontSize="18" Foreground="#FF63738A"/>
 <Canvas x:Name="Group" Canvas.Left="64" Canvas.Top="360" Width="240" Height="100"><Canvas.Effect><DropShadowEffect BlurRadius="0" ShadowDepth="50" Direction="0"/></Canvas.Effect><Rectangle x:Name="ChildA" Canvas.Left="20" Canvas.Top="10" Width="30" Height="30" Fill="#FFE47A31"/><Ellipse x:Name="ChildB" Canvas.Left="120" Canvas.Top="10" Width="30" Height="30" Fill="#FF7353BA"/></Canvas>
</Canvas>`;
let documentId,applied;
try{
  await page.goto(base+'?diagnostics=1',{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.drawCount>0,null,{timeout:180000});
  await check('effect study imports with scoped styles and subtree effects',async()=>{
    const before=await snapshot(),pending=page.waitForEvent('filechooser');await click('Open design');await(await pending).setFiles({name:'EffectsStudy.xaml',mimeType:'text/plain',buffer:Buffer.from(fixture)});
    await page.waitForFunction(c=>globalThis.designSpaceDiagnostics.workspace.documents.length===c+1&&globalThis.designSpaceDiagnostics.nodes.some(n=>n.name==='Card'),before.workspace.documents.length);documentId=(await snapshot()).workspace.active;
    await click('Design view');await click('Fit artboard');await click('Tool Selection (V)');await select();await editor();const s=await snapshot();assert.equal(node(s).effect,null);assert.equal(s.effects.enabled,true);assert.deepEqual(s.rendering.warnings,[]);
  });
  await check('effect fields preview a draft then commit together once',async()=>{
    const before=await snapshot();await click('Effect preset Hard shadow');await edit('Effect Depth','120');await edit('Effect Opacity','0.5');await edit('Effect Color','#000000FF');assert.equal((await snapshot()).revision,before.revision);assert.equal(node(await snapshot()).effect,null);
    const s=await apply();assert.match(node(s).effect,/DropShadowEffect/);assert.match(node(s).effect,/ShadowDepth="120"/);assert.match(node(s).effect,/Opacity="0.5"/);assert.equal(s.sourceDirty,false);
  });
  await check('painted shadow uses direction opacity and source alpha',async()=>{
    const p=await pixels();assert.ok(blue(p(250,200)),'Source pixels remain');assert.ok(shadow(p(370,200)),'Half-opacity blue shadow ignores color alpha');assert.ok(white(p(310,200)),'No shadow in the gap');await page.screenshot({path:directory+'/effect-shadow-editor.png'});
  });
  await check('a parent effect follows child silhouettes rather than container bounds',async()=>{
    const p=await pixels();assert.ok(p(149,385).r<15,'Child shadow');assert.ok(white(p(270,440)),'Empty container region remains transparent');
  });
  await check('shadow-only pixels do not enlarge the editable object hit target',async()=>{await tap(370,200);await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.selection.length===0);await select();assert.deepEqual((await snapshot()).selection,['Card']);});
  await check('effect filters are reused by warm repaint and viewport zoom',async()=>{
    const count=(await snapshot()).effects.builds;await click('Zoom in');await click('Zoom out');await click('Fit artboard');await select();const s=await snapshot();assert.equal(s.effects.builds,count);assert.ok(s.effects.hits>0);
  });
  await check('preview suppression leaves source unchanged and export fully rendered',async()=>{
    const before=await snapshot();await menu('View','Render effects');const s=await snapshot();assert.equal(s.effects.enabled,false);assert.equal(s.revision,before.revision);assert.equal(s.xaml,before.xaml);assert.ok(white((await pixels())(370,200)));
    const pending=page.waitForEvent('download');await menu('File','Export PNG…');const file=await pending;await file.saveAs(directory+'/effects-export.png');const png=PNG.sync.read(await readFile(directory+'/effects-export.png')),i=(400*png.width+740)*4;assert.ok(shadow({r:png.data[i],g:png.data[i+1],b:png.data[i+2]}));await menu('View','Render effects');
  });
  await check('zoom threshold suppresses effects without modifying the document',async()=>{
    const before=await snapshot();await click('Open Artboard settings');await edit('Artboard Effects zoom limit','10');await click('Apply artboard settings');let s=await snapshot();assert.equal(s.effects.zoomLimit,.1);assert.equal(s.effects.active,false);assert.equal(s.revision,before.revision);assert.ok(white((await pixels())(370,200)));
    await edit('Artboard Effects zoom limit','800');await click('Apply artboard settings');await editor();assert.equal((await snapshot()).effects.active,true);assert.ok(shadow((await pixels())(370,200)));
  });
  await check('zero shadow opacity retains the unmodified source and undoes exactly',async()=>{
    const before=node(await snapshot()).effect;await edit('Effect Opacity','0');await apply();const p=await pixels();assert.ok(white(p(370,200))&&blue(p(250,200)));const s=await undo();assert.equal(node(s).effect,before);
  });
  await check('Gaussian blur produces a soft edge and stays editable',async()=>{
    await click('Effect preset Blur');await edit('Effect Radius','12');const s=await apply();assert.match(node(s).effect,/BlurEffect/);assert.match(node(s).effect,/KernelType="Gaussian"/);const p=await pixels();assert.ok(p(195,200).r<248&&p(195,200).r>100);assert.ok(blue(p(250,200)));await page.screenshot({path:directory+'/effect-blur-editor.png'});
  });
  await check('Box kernel preserves its own radius and XML metadata',async()=>{
    await choose('Effect Kernel',1);await edit('Effect Radius','3');const s=await apply();assert.match(node(s).effect,/KernelType="Box"/);assert.match(node(s).effect,/Radius="3"/);assert.ok((await pixels())(198,200).r<250);
  });
  await check('invalid effect draft cannot partially apply or erase valid values',async()=>{
    const before=await snapshot();await edit('Effect Radius','NaN');await click('Apply effect settings');await settle();const s=await snapshot();assert.equal(s.revision,before.revision);assert.equal(node(s).effect,node(before).effect);assert.ok(s.status.includes('finite'));await click('Reload effect settings');
  });
  await check('stale effect draft cannot overwrite undo',async()=>{
    await edit('Effect Radius','7');const s=await undo(),before=node(s).effect;await click('Apply effect settings');await settle();assert.equal((await snapshot()).revision,s.revision);assert.equal(node(await snapshot()).effect,before);assert.ok((await snapshot()).status.includes('stale'));await click('Reload effect settings');await click('Redo');await changed(s);
  });
  await check('None suppresses a styled effect and reset restores inheritance',async()=>{
    await select('Styled');await editor();await reveal('Effect Depth');assert.equal((await snapshot()).controls.find(c=>c.Name==='Effect Depth').Text,'100');await choose('Effect Type',0);const s=await apply();assert.equal(node(s,'Styled').properties.Effect,'{x:Null}');assert.ok(white((await pixels())(760,200)));
    const before=await snapshot();await click('Reset effect to style');await changed(before);assert.equal(node(await snapshot(),'Styled').properties.Effect,undefined);assert.ok((await pixels())(760,200).r<140);
  });
  await check('mixed-selection preset is atomic and undo restores each original effect',async()=>{
    await select();await select('Styled',true);await editor();const before=await snapshot();assert.equal(before.selection.length,2);await click('Effect preset Soft shadow');let s=await apply();assert.equal(node(s).effect,node(s,'Styled').effect);s=await undo();assert.equal(node(s).effect,node(before).effect);assert.equal(node(s,'Styled').effect,node(before,'Styled').effect);await select('Group');await select();await editor();
  });
  await check('native save retains applied effect XML and geometry identities',async()=>{
    const pending=page.waitForEvent('download');await click('Save design');const file=await pending;await file.saveAs(directory+'/effect-study.designspace');const d=JSON.parse(await readFile(directory+'/effect-study.designspace','utf8'));const s=await snapshot(),n=d.root.children.find(n=>n.id===node(s).id);assert.ok(n.propertyElements.some(x=>x.includes('BlurEffect')&&x.includes('KernelType="Box"')));applied=node(s).effect;
    await page.waitForFunction(()=>!globalThis.designSpaceDiagnostics.dirty&&globalThis.designSpaceDiagnostics.status==='Saved EffectsStudy.designspace');await settle();
  });
  await check('effect preview preferences and invalid drafts remain document-owned',async()=>{
    await menu('View','Render effects');await edit('Effect Radius','retained invalid');const before=await snapshot();await click('New design');await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active!==id,documentId);const other=(await snapshot()).workspace.active;assert.equal((await snapshot()).effects.enabled,true);
    await activate(documentId);await editor();await reveal('Effect Radius');const s=await snapshot();assert.equal(s.effects.enabled,false);assert.equal(s.controls.find(c=>c.Name==='Effect Radius').Text,'retained invalid');assert.equal(node(s).effect,applied);assert.equal(s.xaml,before.xaml);
    const pending=page.waitForEvent('download');await click('Save workspace');const file=await pending;await file.saveAs(directory+'/effects-workspace.designspace-workspace');const w=JSON.parse(await readFile(directory+'/effects-workspace.designspace-workspace','utf8')),owner=w.documents.find(d=>d.id===documentId);assert.equal(owner.editor.renderEffects,false);assert.equal(owner.editor.panels.Effects.values.Radius,'retained invalid');assert.equal(w.documents.find(d=>d.id===other).editor.panels.Effects?.hasChanges??false,false);
  });
  await check('recovery keeps invalid text separate from the committed effect',async()=>{
    await page.waitForTimeout(1800);await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(id=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.workspace.active===id,documentId,{timeout:180000});await editor();await reveal('Effect Radius');const before=await snapshot();assert.equal(before.effects.enabled,false);assert.equal(before.controls.find(c=>c.Name==='Effect Radius').Text,'retained invalid');assert.equal(node(before).effect,applied);await click('Apply effect settings');assert.equal((await snapshot()).revision,before.revision);await click('Reload effect settings');await menu('View','Render effects');await page.screenshot({path:directory+'/effects-workspace.png'});
  });
  const s=await snapshot();assert.deepEqual(errors,[]);assert.deepEqual(s.rendering.warnings,[]);assert.deepEqual(s.rendering.previewWarnings,[]);assert.ok(!log.some(l=>l.startsWith('error: [DesignSpace')));
}catch(error){await writeFile(directory+'/failure.txt',String(error.stack??error));await page.screenshot({path:directory+'/failure.png'}).catch(()=>{});throw error;}
finally{await writeFile(directory+'/results.json',JSON.stringify({passed:results.length,tests:results,errors},null,2));await writeFile(directory+'/diagnostics.json',JSON.stringify(await snapshot().catch(()=>null)??null,null,2));await writeFile(directory+'/browser.log',log.join('\n'));await browser.close();}
