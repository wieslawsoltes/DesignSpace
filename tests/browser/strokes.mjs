import assert from 'node:assert/strict';
import { PNG } from 'pngjs';
import { readFile } from 'node:fs/promises';

// The diagnostic snapshot is read-only; source, properties and commands use actual UI input.
export async function strokes({page,snapshot,click,check,directory}) {
  const wait=()=>page.waitForTimeout(400);
  const fixture=`<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="StrokeRoot" Width="960" Height="560" Background="White">
    <TextBlock x:Name="Title" Canvas.Left="64" Canvas.Top="34" Width="720" Height="45" Text="Stroke authoring / editable geometry" FontSize="26" Foreground="#FF202838"/>
    <Path x:Name="StrokeFixture" Canvas.Left="64" Canvas.Top="60" Width="500" Height="220" Stretch="None" Data="M40 100L440 100" Stroke="#FFD03030" StrokeThickness="12"/>
    <TextBlock x:Name="Hint" Canvas.Left="64" Canvas.Top="206" Width="700" Height="30" Text="Independent end caps, dashes and true gap picking" FontSize="16" Foreground="#FF63738A"/>
    <Path x:Name="CornerSample" Canvas.Left="64" Canvas.Top="268" Width="320" Height="180" Stretch="None" Data="M40 140L140 40L240 140" Stroke="#FF0078D4" StrokeThickness="20" StrokeLineJoin="Round" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
    <Path x:Name="DotSample" Canvas.Left="470" Canvas.Top="310" Width="350" Height="110" Stretch="None" Data="M20 40C90 0 190 120 300 40" Stroke="#FF6750A4" StrokeThickness="12" StrokeDashArray="0 2" StrokeDashCap="Round"/>
  </Canvas>`;
  let id;
  const node=s=>s.nodes.find(n=>n.id===id);
  async function changed(revision){await page.waitForFunction(r=>globalThis.designSpaceDiagnostics.revision===r+1,revision,{timeout:15000});await wait();const s=await snapshot();assert.equal(s.revision,revision+1);return s;}
  async function edit(name,value){await click(name);await page.keyboard.press('Control+A');await page.keyboard.insertText(value);await page.keyboard.press('Tab');await wait();}
  async function choice(name,index){await click(name);await page.keyboard.press('Home');for(let i=0;i<index;i++)await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');await wait();}
  async function point(x,y){const v=(await snapshot()).surface;return{x:v.x+v.panX+x*v.zoom,y:v.y+v.panY+y*v.zoom};}
  async function tap(x,y){const p=await point(x,y);await page.mouse.click(p.x,p.y);await wait();}
  async function selected(){await tap(116,160);await page.waitForFunction(id=>{const s=globalThis.designSpaceDiagnostics;return s.selection.includes(s.nodes.find(n=>n.id===id)?.name);},id);}
  async function editor(){await click('Open Stroke panel');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name==='Stroke Width'),null,{timeout:15000});}
  async function apply(){const before=await snapshot();await click('Apply stroke settings');return changed(before.revision);}
  async function pixels(){await wait();const png=PNG.sync.read(await page.screenshot());const v=(await snapshot()).surface;
    return(x,y)=>{const sx=Math.round(v.x+v.panX+x*v.zoom),sy=Math.round(v.y+v.panY+y*v.zoom),i=(sy*png.width+sx)*4;return{r:png.data[i],g:png.data[i+1],b:png.data[i+2]};};}
  const red=p=>p.r>140&&p.g<110&&p.b<110;
  async function command(name){
    // Bring commands near the bottom of the real scrollable editor into view.
    for(let i=0;i<8;i++){
      const s=await snapshot(),c=s.controls.find(c=>c.Name===name);assert.ok(c,`Control ${name}`);
      if(c.Y>120&&c.Y+c.Height<s.height-55){await click(name);return;}
      await page.mouse.move(s.width-120,450);await page.mouse.wheel(0,c.Y<120?-250:250);await wait();
    }
    throw new Error(`Unable to reveal ${name}`);
  }
  await check('stroke fixture is imported through validated XAML source',async()=>{
    await click('XAML view');await click('XAML source editor');await page.keyboard.press('Control+A');await page.keyboard.insertText(fixture);await wait();const before=await snapshot();await click('Apply XAML');const s=await changed(before.revision);id=s.nodes.find(n=>n.name==='StrokeFixture')?.id;assert.ok(id);assert.equal(s.sourceDirty,false);await click('Design view');await click('Fit artboard');await click('Tool Selection (V)');await selected();await editor();
  });
  await check('stroke settings remain a draft then commit together once',async()=>{
    const before=await snapshot();await click('Stroke preset Dash');await choice('Stroke Start cap',3);await choice('Stroke End cap',1);await edit('Stroke Miter limit','3');
    assert.equal((await snapshot()).revision,before.revision);assert.equal(node(await snapshot()).properties.StrokeDashArray,undefined);
    const s=await apply();assert.equal(node(s).properties.StrokeDashArray,'2 2');assert.equal(node(s).properties.StrokeStartLineCap,'Triangle');assert.equal(node(s).properties.StrokeEndLineCap,'Square');assert.equal(node(s).properties.StrokeMiterLimit,'3');assert.equal(s.sourceDirty,false);
  });
  await check('stroke pixels include independent caps and exclude dash gaps',async()=>{
    const pixel=await pixels();assert.ok(red(pixel(116,160)),'Painted dash');assert.ok(!red(pixel(140,160)),'Dash gap');assert.ok(red(pixel(100,160)),'Triangle cap tip');assert.ok(!red(pixel(100,155)),'Triangle cap corner remains clear');
    await page.screenshot({path:directory+'/stroke-authoring.png'});
  });
  await check('native picking selects painted strokes but not their gaps',async()=>{
    await tap(140,160);await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.selection.length===0);await selected();const s=await snapshot();assert.equal(s.selection[0],'StrokeFixture');
  });
  await check('stroke outlines are reused across repaint, zoom and picking',async()=>{
    const count=(await snapshot()).rendering.strokeBuilds;await click('Zoom in');await click('Zoom out');await selected();const s=await snapshot();assert.equal(s.rendering.strokeBuilds,count);assert.ok(s.rendering.strokeCacheHits>0);assert.ok(s.rendering.strokeCacheBytes<=16*1024*1024);await click('Fit artboard');
  });
  await check('invalid stroke draft is retained without partial edits',async()=>{
    await editor();const before=await snapshot();await edit('Stroke Dash array','0 0');await edit('Stroke Brush','#FF0000FF');await click('Apply stroke settings');await wait();const s=await snapshot();assert.equal(s.revision,before.revision);assert.equal(node(s).properties.Stroke,node(before).properties.Stroke);assert.equal(node(s).properties.StrokeDashArray,'2 2');assert.ok(s.status.includes('all-zero'));await click('Reload stroke settings');
  });
  await check('zero width hides strokes rather than producing a hairline',async()=>{
    await edit('Stroke Width','0');await apply();const pixel=await pixels();assert.ok(!red(pixel(116,160)));await tap(116,160);await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.selection.length===0);
    const before=await snapshot();await click('Undo');await changed(before.revision);await selected();await editor();assert.equal(node(await snapshot()).properties.StrokeThickness,'12');
  });
  await check('dot preset produces round zero-length dashes',async()=>{
    await click('Stroke preset Dot');const s=await apply();assert.equal(node(s).properties.StrokeDashArray,'0 2');assert.equal(node(s).properties.StrokeDashCap,'Round');const pixel=await pixels();assert.ok(red(pixel(128,160))&&red(pixel(131,160)));assert.ok(!red(pixel(140,160)));
    const before=await snapshot();await click('Undo');await changed(before.revision);await selected();await editor();
  });
  await check('stale stroke drafts cannot overwrite a later document revision',async()=>{
    await edit('Stroke Width','16');const before=await snapshot();await click('Undo');await changed(before.revision);const stale=await snapshot();await click('Apply stroke settings');await wait();assert.equal((await snapshot()).revision,stale.revision);assert.ok((await snapshot()).status.includes('Reload'));
    await click('Reload stroke settings');const undoState=await snapshot();await click('Redo');await changed(undoState.revision);await selected();await editor();
  });
  await check('stroke-to-path conversion preserves identity, pixels and undo',async()=>{
    const before=await snapshot();const original=node(before);assert.equal(original.properties.StrokeDashArray,'2 2');const pixel=await pixels();await command('Convert stroke to path');const after=await changed(before.revision);const outlined=node(after);assert.equal(outlined.type,'Path');assert.equal(outlined.properties.Fill,original.properties.Stroke);assert.equal(outlined.properties.Stroke,undefined);assert.notEqual(outlined.properties.Data,original.properties.Data);
    const output=await pixels();for(const x of [100,110,116,124,140,155,163,178])assert.equal(red(pixel(x,160)),red(output(x,160)),`outline pixel ${x}`);await page.screenshot({path:directory+'/stroke-outline.png'});
    const revision=(await snapshot()).revision;await click('Undo');await changed(revision);const restored=node(await snapshot());assert.deepEqual(restored.properties,original.properties);await editor();
  });
  await check('stroke values survive native export and recovery',async()=>{
    const pending=page.waitForEvent('download');await click('Save design');const download=await pending;await download.saveAs(directory+'/stroke-settings.designspace');const doc=JSON.parse(await readFile(directory+'/stroke-settings.designspace','utf8'));const saved=doc.root.children.find(n=>n.id===id);assert.equal(saved.properties.StrokeStartLineCap,'Triangle');assert.equal(saved.properties.StrokeDashArray,'2 2');
    await page.waitForTimeout(1600);await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(id=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.nodes.some(n=>n.id===id),id,{timeout:180000});const s=await snapshot();assert.equal(node(s).properties.StrokeDashArray,'2 2');assert.equal(s.sourceDirty,false);await selected();await editor();await page.screenshot({path:directory+'/stroke-workspace.png'});
  });
}
