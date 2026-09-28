import assert from 'node:assert/strict';
import { PNG } from 'pngjs';
import { readFile } from 'node:fs/promises';

// Diagnostics only locate controls and inspect results; all edits use real input.
export async function brushes({page,snapshot,click,check,directory}) {
  const settle=()=>page.waitForTimeout(400);
  const fixture=`<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="BrushRoot" Width="960" Height="560" Background="White">
    <TextBlock x:Name="Title" Canvas.Left="80" Canvas.Top="24" Width="800" Height="45" Text="Gradient brush authoring" FontSize="28" Foreground="#FF202838"/>
    <Rectangle x:Name="GradientTarget" Canvas.Left="80" Canvas.Top="80" Width="640" Height="220"><Rectangle.Fill><LinearGradientBrush StartPoint="0,0" EndPoint="1,0"><GradientStop Offset="0" Color="Red"/><GradientStop Offset="1" Color="Blue"/></LinearGradientBrush></Rectangle.Fill></Rectangle>
    <TextBlock Canvas.Left="80" Canvas.Top="325" Width="720" Height="32" Text="Editable stops / elliptical focus / scoped resources / opacity masks" FontSize="16" Foreground="#FF63738A"/>
    <Rectangle Canvas.Left="80" Canvas.Top="382" Width="180" Height="100"><Rectangle.Fill><RadialGradientBrush RadiusX="0.5" RadiusY="0.35" GradientOrigin="0.3,0.45"><GradientStop Offset="0" Color="#FF56D9F0"/><GradientStop Offset="1" Color="#FF1738AD"/></RadialGradientBrush></Rectangle.Fill></Rectangle>
    <Rectangle Canvas.Left="295" Canvas.Top="382" Width="180" Height="100"><Rectangle.Fill><LinearGradientBrush StartPoint="0,0" EndPoint="0.2,0" SpreadMethod="Reflect"><GradientStop Offset="0" Color="#FF684CC5"/><GradientStop Offset="1" Color="#FFF09BAC"/></LinearGradientBrush></Rectangle.Fill></Rectangle>
    <Canvas Canvas.Left="510" Canvas.Top="382" Width="210" Height="100"><Canvas.OpacityMask><LinearGradientBrush StartPoint="0,0" EndPoint="1,0"><GradientStop Offset="0" Color="#00FFFFFF"/><GradientStop Offset="1" Color="White"/></LinearGradientBrush></Canvas.OpacityMask><Rectangle Width="210" Height="100" Fill="#FF078773"/><TextBlock Canvas.Left="30" Canvas.Top="35" Width="160" Height="30" Text="Subtree mask" Foreground="White" FontSize="18"/></Canvas>
  </Canvas>`;
  let documentId,targetId;
  async function changed(revision){await page.waitForFunction(r=>globalThis.designSpaceDiagnostics.revision===r+1,revision,{timeout:15000});await settle();const s=await snapshot();assert.equal(s.revision,revision+1);return s;}
  async function reveal(name){
    await page.waitForFunction(n=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name===n),name,{timeout:15000});
    for(let i=0;i<12;i++){
      const s=await snapshot(),c=s.controls.find(c=>c.Name===name);assert.ok(c,`Control ${name}`);
      if(c.Y>=130&&c.Y+c.Height<=s.height-65)return c;
      await page.mouse.move(s.width-120,470);await page.mouse.wheel(0,c.Y<130?-230:230);await settle();
    }
    throw new Error(`Cannot reveal ${name}`);
  }
  async function press(name){await reveal(name);await click(name);}
  async function edit(name,value){await press(name);await page.keyboard.press('Control+A');await page.keyboard.insertText(value);await page.keyboard.press('Tab');await page.waitForFunction(({name,value})=>globalThis.designSpaceDiagnostics.controls.find(c=>c.Name===name)?.Text===value,{name,value});}
  async function choose(name,index){await press(name);await page.keyboard.press('Home');for(let i=0;i<index;i++)await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');await settle();}
  async function apply(){const s=await snapshot();await click('Apply gradient brush');return changed(s.revision);}
  async function selected(){const v=(await snapshot()).surface;await page.mouse.click(v.x+v.panX+250*v.zoom,v.y+v.panY+170*v.zoom);await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.selection.includes('GradientTarget'));}
  async function editor(){await click('Open Brush panel');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name==='Gradient Kind'));}
  async function xmlBrush(property='Fill'){
    return page.evaluate(property=>{
      const doc=new DOMParser().parseFromString(globalThis.designSpaceDiagnostics.xaml,'application/xml');
      const node=Array.from(doc.getElementsByTagNameNS('*','Rectangle')).find(n=>n.getAttributeNS('http://schemas.microsoft.com/winfx/2006/xaml','Name')==='GradientTarget');
      const brush=Array.from(node.children).find(n=>n.localName==='Rectangle.'+property)?.firstElementChild;
      return brush?{kind:brush.localName,attributes:Object.fromEntries(Array.from(brush.attributes).map(a=>[a.localName,a.value])),stops:Array.from(brush.getElementsByTagNameNS('*','GradientStop')).map(s=>({offset:Number(s.getAttribute('Offset')),color:s.getAttribute('Color')})),xml:new XMLSerializer().serializeToString(brush)}:null;
    },property);
  }
  async function pixels(){await settle();const png=PNG.sync.read(await page.screenshot()),v=(await snapshot()).surface;return(x,y)=>{const px=Math.round(v.x+v.panX+x*v.zoom),py=Math.round(v.y+v.panY+y*v.zoom),i=(py*png.width+px)*4;return{r:png.data[i],g:png.data[i+1],b:png.data[i+2]};};}
  async function selectDocument(id){const s=await snapshot(),d=s.workspace.documents.find(d=>d.id===id);assert.ok(d);await click('Document tab '+d.title);await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active===id,id);}
  await check('gradient fixture opens as a separate editable document',async()=>{
    const count=(await snapshot()).workspace.documents.length;const pending=page.waitForEvent('filechooser');await click('Open design');await(await pending).setFiles({name:'BrushStudy.xaml',mimeType:'text/plain',buffer:Buffer.from(fixture)});
    await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.workspace.documents.length===count+1&&globalThis.designSpaceDiagnostics.nodes.some(n=>n.name==='GradientTarget'),count);
    const s=await snapshot();documentId=s.workspace.active;targetId=s.nodes.find(n=>n.name==='GradientTarget').id;await click('Design view');await click('Fit artboard');await click('Tool Selection (V)');await selected();await editor();assert.equal((await snapshot()).sourceDirty,false);
  });
  await check('gradient stops and spread apply in one document transaction',async()=>{
    const before=await snapshot();await press('Add gradient stop');await edit('Gradient stop 2 color','#FF00FF00');await edit('Gradient Opacity','0.8');await edit('Gradient End','0.5,0');await choose('Gradient Spread',1);
    assert.equal((await snapshot()).revision,before.revision);assert.equal((await xmlBrush()).stops.length,2);await apply();const b=await xmlBrush();assert.equal(b.stops.length,3);assert.equal(b.attributes.SpreadMethod,'Reflect');assert.equal(b.attributes.Opacity,'0.8');assert.equal(b.stops[2].color,'#FF00FF00');
  });
  await check('reflected gradient and brush alpha have expected artboard pixels',async()=>{
    const pixel=await pixels();for(const x of [240,560]){const c=pixel(x,180);assert.ok(c.g>235&&c.r>35&&c.r<75&&c.b>35&&c.b<75,JSON.stringify(c));}
    await page.screenshot({path:directory+'/gradient-stops.png'});
  });
  await check('gradient stop dragging edits a draft before explicit Apply',async()=>{
    const strip=await reveal('Gradient stop strip'),before=await snapshot();const x=strip.X+8+.5*(strip.Width-16),y=strip.Y+37;
    await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(strip.X+8+.625*(strip.Width-16),y,{steps:10});await page.mouse.up();
    await page.waitForFunction(()=>{const c=globalThis.designSpaceDiagnostics.controls.find(c=>c.Name==='Gradient stop 2 offset');return Number(c?.Text)>.6;});assert.equal((await snapshot()).revision,before.revision);assert.equal((await xmlBrush()).stops[2].offset,.5);await apply();assert.ok(Math.abs((await xmlBrush()).stops[2].offset-.625)<.015);
  });
  await check('gradient reversal preserves offsets and exact undo',async()=>{
    const original=(await snapshot()).xaml,before=await xmlBrush();await press('Reverse gradient stops');await apply();const after=await xmlBrush();assert.ok(Math.abs(after.stops[0].offset-(1-before.stops[2].offset))<1e-6);assert.equal(after.stops[0].color,before.stops[2].color);
    const revision=(await snapshot()).revision;await click('Undo');await changed(revision);assert.equal((await snapshot()).xaml,original);
  });
  await check('radial brush exposes independent radii and focal origin',async()=>{
    await press('Gradient stop 2 color');await press('Remove gradient stop');await edit('Gradient stop 0 color','Black');await edit('Gradient stop 1 color','White');await edit('Gradient Opacity','1');await choose('Gradient Spread',0);await choose('Gradient Kind',3);
    await edit('Gradient Radius X','0.5');await edit('Gradient Radius Y','0.25');await edit('Gradient Origin','0.5,0.5');await apply();const b=await xmlBrush();assert.equal(b.kind,'RadialGradientBrush');assert.equal(b.attributes.RadiusY,'0.25');
    let pixel=await pixels();assert.ok(Math.abs(pixel(560,190).r-128)<14);assert.ok(Math.abs(pixel(400,217).r-128)<14);assert.ok(pixel(400,278).r>240);
    await edit('Gradient Origin','0.25,0.5');await apply();pixel=await pixels();assert.ok(pixel(240,190).r<15&&pixel(400,190).r>70);await page.screenshot({path:directory+'/radial-brush.png'});
  });
  await check('invalid gradient radius is retained without changing the document',async()=>{
    const before=await snapshot();await edit('Gradient Radius Y','-1');await click('Apply gradient brush');await settle();assert.equal((await snapshot()).revision,before.revision);assert.equal((await snapshot()).xaml,before.xaml);assert.equal((await snapshot()).controls.find(c=>c.Name==='Gradient Radius Y').Text,'-1');await click('Reload gradient brush');
  });
  await check('absolute and relative brush transforms roundtrip through source',async()=>{
    await choose('Gradient Kind',2);await choose('Gradient Mapping',1);await edit('Gradient Start','0,0');await edit('Gradient End','160,0');await edit('Gradient Transform','1,0,0,1,32,0');await edit('Gradient Relative','1,0,0,1,0.1,0');await apply();const b=await xmlBrush();assert.equal(b.attributes.MappingMode,'Absolute');assert.ok(b.xml.includes('32,0')&&b.xml.includes('0.1,0'));
    const pixel=await pixels();assert.ok(Math.abs(pixel(256,190).r-128)<15);
  });
  await check('warm brush shaders survive zoom and redraw without rebuilding',async()=>{
    const before=(await snapshot()).rendering;await click('Zoom in');await click('Zoom out');await settle();const after=(await snapshot()).rendering;assert.equal(after.brushBuilds,before.brushBuilds);assert.ok(after.brushHits>before.brushHits);assert.ok(after.brushCacheBytes<=6*1024*1024);await click('Fit artboard');
  });
  await check('opacity-mask authoring uses alpha and not brush luminance',async()=>{
    await choose('Gradient Kind',1);await edit('Gradient Color','#FF078773');await apply();await choose('Gradient Property',5);await choose('Gradient Kind',2);await edit('Gradient stop 0 color','#00FFFFFF');await edit('Gradient stop 1 color','#FFFFFFFF');await press('Gradient direction Horizontal');await apply();
    assert.equal((await xmlBrush('OpacityMask')).kind,'LinearGradientBrush');const pixel=await pixels();assert.ok(pixel(110,190).r>225&&pixel(680,190).r<40);await page.screenshot({path:directory+'/opacity-mask.png'});
  });
  await check('brush drafts cannot overwrite a newer document revision',async()=>{
    await edit('Gradient Opacity','0.3');const revision=(await snapshot()).revision;await click('Undo');await changed(revision);const stale=await snapshot();await click('Apply gradient brush');await settle();assert.equal((await snapshot()).revision,stale.revision);assert.ok((await snapshot()).status.includes('stale'));await click('Reload gradient brush');await click('Redo');await changed(stale.revision);
  });
  await check('invalid brush drafts remain isolated across document tabs',async()=>{
    await edit('Gradient Opacity','invalid opacity');const before=await snapshot();await click('New design');await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.workspace.active!==id,documentId);const other=(await snapshot()).workspace.active;await selectDocument(documentId);await editor();assert.equal((await snapshot()).controls.find(c=>c.Name==='Gradient Opacity').Text,'invalid opacity');assert.equal((await snapshot()).xaml,before.xaml);
    const pending=page.waitForEvent('download');await click('Save workspace');const download=await pending;await download.saveAs(directory+'/gradient-workspace.designspace-workspace');const saved=JSON.parse(await readFile(directory+'/gradient-workspace.designspace-workspace','utf8'));const draft=saved.documents.find(d=>d.id===documentId).editor.panels.Brush;assert.equal(draft.values.Opacity,'invalid opacity');assert.equal(draft.hasChanges,true);assert.ok(draft.targets.includes(targetId));assert.equal(saved.documents.find(d=>d.id===other).editor.panels.Brush?.hasChanges??false,false);
  });
  await check('gradient draft and authored mask survive full workspace recovery',async()=>{
    await page.waitForTimeout(1800);await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(id=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.workspace.active===id,documentId,{timeout:180000});await editor();assert.equal((await snapshot()).controls.find(c=>c.Name==='Gradient Opacity').Text,'invalid opacity');assert.equal((await xmlBrush('OpacityMask')).kind,'LinearGradientBrush');const revision=(await snapshot()).revision;await click('Apply gradient brush');await settle();assert.equal((await snapshot()).revision,revision);await click('Reload gradient brush');assert.equal((await snapshot()).sourceDirty,false);
  });
  await check('native brush save includes gradient metadata without serializing draft errors',async()=>{
    const pending=page.waitForEvent('download');await click('Save design');const download=await pending;await download.saveAs(directory+'/gradient-study.designspace');const d=JSON.parse(await readFile(directory+'/gradient-study.designspace','utf8')),n=d.root.children.find(n=>n.id===targetId);assert.ok(n.propertyElements.some(s=>s.includes('OpacityMask')&&s.includes('GradientStop')));assert.ok(!JSON.stringify(d).includes('invalid opacity'));await page.screenshot({path:directory+'/brush-workspace.png'});
  });
}
