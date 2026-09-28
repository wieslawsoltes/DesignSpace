import { chromium } from 'playwright';
import { PNG } from 'pngjs';
import { mkdir, writeFile, readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const base=process.argv[2] || 'http://127.0.0.1:4173/DesignSpace/';
const directory='artifacts/verification';
await mkdir(directory,{recursive:true});
const browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-webgl','--enable-unsafe-swiftshader','--no-sandbox']});
const context=await browser.newContext({viewport:{width:1600,height:1000},deviceScaleFactor:1,acceptDownloads:true});
const page=await context.newPage();
const log=[],errors=[],results=[];
page.on('console',message=>{const line=`${message.type()}: ${message.text()}`;log.push(line);if(message.type()==='error') console.error(line);});
page.on('pageerror',error=>{errors.push(error.message);console.error(error);});
const snapshot=()=>page.evaluate(()=>globalThis.designSpaceDiagnostics);
async function click(name) {
  await page.waitForFunction(name=>globalThis.designSpaceDiagnostics?.controls.some(c=>c.Name===name),name,{timeout:15000});
  const control=(await snapshot()).controls.find(c=>c.Name===name);
  assert.ok(control,`Visible control: ${name}`);
  await page.mouse.click(control.X+control.Width/2,control.Y+control.Height/2);
  await page.waitForTimeout(300);
}
async function check(name,run) { await run();results.push(name);console.log('PASS',name); }
try {
  await page.goto(base+'?diagnostics=1',{waitUntil:'domcontentloaded'});
  await page.waitForFunction(()=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.drawCount>0,null,{timeout:180000});
  await page.screenshot({path:directory+'/workspace.png'});
  await check('real Uno workbench and sample document',async()=>{
    const s=await snapshot();assert.equal(s.nodes.length,19);assert.ok(s.nodes.some(n=>n.name==='ExploreButton'));assert.ok(s.surface.width>500);assert.ok(s.drawCount>0);assert.equal(s.mode,'Design');
  });
  await check('rendered artboard contains pixels and color',async()=>{
    const png=PNG.sync.read(await page.screenshot());let white=0,blue=0;
    for(let i=0;i<png.data.length;i+=4) {const [r,g,b]=png.data.subarray(i,i+3);if(r>240&&g>240&&b>240) white++;if(b>140&&g>60&&r<60) blue++;}
    assert.ok(white>12000,`white artboard pixels: ${white}`);assert.ok(blue>5000,`blue UI/art pixels: ${blue}`);
  });
  const before=await snapshot();
  await check('pointer creates a rectangle',async()=>{
    await click('Tool Rectangle (R)');const v=(await snapshot()).surface;
    const a={x:v.x+v.panX+350*v.zoom,y:v.y+v.panY+350*v.zoom},b={x:v.x+v.panX+450*v.zoom,y:v.y+v.panY+410*v.zoom};
    await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:8});await page.mouse.up();
    await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.nodes.length===count+1,before.nodes.length,{timeout:20000});
    const s=await snapshot();assert.equal(s.selection.length,1);assert.ok(s.selection[0].startsWith('Rectangle'));
  });
  await check('undo and redo restore visual edits',async()=>{
    await click('Undo');await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.nodes.length===count,before.nodes.length);
    await click('Redo');await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.nodes.length===count+1,before.nodes.length);
  });
  await check('properties edit the selected shape',async()=>{
    await click('Property Width');await page.keyboard.press('Control+A');await page.keyboard.insertText('144');await page.keyboard.press('Enter');
    await page.waitForFunction(()=>{const s=globalThis.designSpaceDiagnostics;return s.nodes.find(n=>n.name===s.selection[0])?.properties.Width==='144';});
  });
  await check('native download is valid and complete JSON',async()=>{
    const downloadPromise=page.waitForEvent('download');await click('Save design');const download=await downloadPromise;
    await download.saveAs(directory+'/saved.designspace');const doc=JSON.parse(await readFile(directory+'/saved.designspace','utf8'));
    const count=n=>1+n.children.reduce((sum,child)=>sum+count(child),0);
    assert.equal(doc.formatVersion,1);assert.equal(count(doc.root),before.nodes.length+1);assert.equal(doc.storyboards[0].tracks.length,2);
  });
  await check('animation plays and stops',async()=>{
    await click('Play or pause storyboard');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.timeline.time>0.1);
    await click('Stop storyboard');await page.waitForFunction(()=>!globalThis.designSpaceDiagnostics.timeline.playing&&globalThis.designSpaceDiagnostics.timeline.time===0);
  });
  await check('local recovery persists the working document',async()=>{
    await page.waitForFunction(()=>!!localStorage.getItem('designspace.v1.recovery.json'),null,{timeout:20000});
    await page.waitForTimeout(1200);const prior=(await snapshot()).nodes.length;
    await page.reload({waitUntil:'domcontentloaded'});
    await page.waitForFunction(count=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.nodes.length===count,prior,{timeout:180000});
  });
  await check('no application exceptions or serialization errors',async()=>{assert.deepEqual(errors,[]);assert.ok(!log.some(s=>s.startsWith('error: [DesignSpace')));});
  await page.screenshot({path:directory+'/workspace-edited.png'});
} catch(error) {
  await page.screenshot({path:directory+'/failure.png'}).catch(()=>{});await writeFile(directory+'/failure-dom.html',await page.content().catch(()=>''));throw error;
} finally {
  await writeFile(directory+'/browser.log',log.join('\n'));await writeFile(directory+'/results.json',JSON.stringify({passed:results.length,tests:results,errors},null,2));
  await writeFile(directory+'/diagnostics.json',JSON.stringify(await snapshot().catch(()=>null) ?? null,null,2));await browser.close();
}
