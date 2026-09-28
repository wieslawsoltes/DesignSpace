import { chromium } from 'playwright';
import { PNG } from 'pngjs';
import { mkdir, writeFile, readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const base=process.argv[2]||'http://127.0.0.1:4173/DesignSpace/';
const directory='artifacts/verification';await mkdir(directory,{recursive:true});
const browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-webgl','--enable-unsafe-swiftshader','--no-sandbox']});
const context=await browser.newContext({viewport:{width:1600,height:1000},deviceScaleFactor:1,acceptDownloads:true});
const page=await context.newPage();const log=[],errors=[],results=[];
page.on('console',message=>{const line=`${message.type()}: ${message.text()}`;log.push(line);if(message.type()==='error')console.error(line);});
page.on('pageerror',error=>{errors.push(error.message);console.error(error);});
const snapshot=()=>page.evaluate(()=>globalThis.designSpaceDiagnostics);
async function click(name){
  await page.waitForFunction(name=>globalThis.designSpaceDiagnostics?.controls.some(c=>c.Name===name),name,{timeout:15000});
  const control=(await snapshot()).controls.find(c=>c.Name===name);assert.ok(control,`Visible control: ${name}`);
  await page.mouse.click(control.X+control.Width/2,control.Y+control.Height/2);await page.waitForTimeout(300);
}
async function replaceSource(text){await click('XAML source editor');await page.keyboard.press('Control+A');await page.keyboard.insertText(text);await page.waitForTimeout(350);}
async function check(name,run){await run();results.push(name);console.log('PASS',name);}
try{
  await page.goto(base+'?diagnostics=1',{waitUntil:'domcontentloaded'});
  await page.waitForFunction(()=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.drawCount>0,null,{timeout:180000});
  await page.waitForTimeout(400);await page.screenshot({path:directory+'/workspace.png'});
  await check('real Uno workbench and sample document',async()=>{const s=await snapshot();assert.equal(s.nodes.length,19);assert.ok(s.nodes.some(n=>n.name==='ExploreButton'));assert.ok(s.surface.width>500);assert.equal(s.mode,'Design');});
  await check('rendered artboard contains pixels and color',async()=>{
    const png=PNG.sync.read(await page.screenshot());let white=0,blue=0;
    for(let i=0;i<png.data.length;i+=4){const[r,g,b]=png.data.subarray(i,i+3);if(r>240&&g>240&&b>240)white++;if(b>140&&g>60&&r<60)blue++;}
    assert.ok(white>12000,`white pixels: ${white}`);assert.ok(blue>5000,`blue pixels: ${blue}`);
  });
  const before=await snapshot();
  await check('pointer creates a rectangle',async()=>{
    await click('Tool Rectangle (R)');const v=(await snapshot()).surface;
    const a={x:v.x+v.panX+350*v.zoom,y:v.y+v.panY+350*v.zoom},b={x:v.x+v.panX+450*v.zoom,y:v.y+v.panY+410*v.zoom};
    await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:8});await page.mouse.up();
    await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.nodes.length===count+1,before.nodes.length,{timeout:20000});const s=await snapshot();assert.equal(s.selection.length,1);assert.ok(s.selection[0].startsWith('Rectangle'));
  });
  await check('undo and redo restore visual edits',async()=>{
    await click('Undo');await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.nodes.length===count,before.nodes.length);
    await click('Redo');await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.nodes.length===count+1,before.nodes.length);
  });
  await check('property editing commits exactly once',async()=>{
    const revision=(await snapshot()).revision;await click('Property Width');await page.keyboard.press('Control+A');await page.keyboard.insertText('144');await page.keyboard.press('Enter');
    await page.waitForFunction(()=>{const s=globalThis.designSpaceDiagnostics;return s.nodes.find(n=>n.name===s.selection[0])?.properties.Width==='144';});await page.waitForTimeout(700);assert.equal((await snapshot()).revision,revision+1);
  });
  await check('normalized source is not a false draft',async()=>assert.equal((await snapshot()).sourceDirty,false));
  await check('native download is valid complete JSON',async()=>{
    const pending=page.waitForEvent('download',{timeout:15000});await click('Save design');const download=await pending;await download.saveAs(directory+'/saved.designspace');
    const doc=JSON.parse(await readFile(directory+'/saved.designspace','utf8'));const count=n=>1+n.children.reduce((sum,child)=>sum+count(child),0);
    assert.equal(doc.formatVersion,1);assert.equal(count(doc.root),before.nodes.length+1);assert.equal(doc.storyboards[0].tracks.length,2);
  });
  const sourceState=await snapshot();const validSource=sourceState.xaml;const headline=sourceState.nodes.find(n=>n.properties.Text==='Ideas, brought to life.');assert.ok(headline);
  await check('invalid source stays a draft without model mutation',async()=>{
    await click('XAML view');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.mode==='XAML');await replaceSource('<Canvas>');await click('Apply XAML');
    const s=await snapshot();assert.equal(s.revision,sourceState.revision);assert.equal(s.sourceDirty,true);assert.equal(s.xaml,'<Canvas>');await page.screenshot({path:directory+'/invalid-source.png'});
  });
  await check('valid source retains named identities and animation',async()=>{
    await replaceSource(validSource.replace('Ideas, brought to life.','Ideas, designed in DesignSpace.'));await click('Apply XAML');await page.waitForFunction(()=>!globalThis.designSpaceDiagnostics.sourceDirty);
    const s=await snapshot();assert.equal(s.nodes.find(n=>n.id===headline.id).properties.Text,'Ideas, designed in DesignSpace.');assert.equal(s.timeline.tracks,2);await page.screenshot({path:directory+'/source-editor.png'});await click('Design view');
  });
  await check('animation plays and stops',async()=>{await click('Play or pause storyboard');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.timeline.time>0.1);await click('Stop storyboard');await page.waitForFunction(()=>!globalThis.designSpaceDiagnostics.timeline.playing&&globalThis.designSpaceDiagnostics.timeline.time===0);});
  await check('local recovery persists the document',async()=>{
    await page.waitForFunction(()=>!!localStorage.getItem('designspace.v1.recovery.json'),null,{timeout:20000});await page.waitForTimeout(1200);const prior=(await snapshot()).nodes.length;
    await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(count=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.nodes.length===count,prior,{timeout:180000});const s=await snapshot();assert.ok(s.nodes.some(n=>n.properties.Text==='Ideas, designed in DesignSpace.'));assert.equal(s.sourceDirty,false);
  });
  await check('storyboard timing authoring scales keys and preserves source',async()=>{
    await click('Edit storyboard timing');
    for(const [name,value] of [['Storyboard duration','1'],['Storyboard start delay','0.1'],['Storyboard speed','2'],['Storyboard repeat amount','2']]){
      await click(name);await page.keyboard.press('Control+A');await page.keyboard.insertText(value);await page.keyboard.press('Tab');
    }
    await click('Storyboard auto reverse');await click('Scale storyboard keyframes');await click('Apply storyboard timing');
    await page.waitForFunction(()=>{const t=globalThis.designSpaceDiagnostics.timeline;return t.duration===1&&t.begin===0.1&&t.speed===2&&t.repeatCount===2&&t.autoReverse;});
    const s=await snapshot();assert.equal(s.sourceDirty,false);assert.ok(s.xaml.includes('AutoReverse="True"'));assert.ok(s.xaml.includes('RepeatBehavior="2x"'));
    await page.screenshot({path:directory+'/storyboard-timing.png'});
  });
  await check('timed playback reverses and completes without document edits',async()=>{
    const revision=(await snapshot()).revision;await click('Play or pause storyboard');
    await page.waitForFunction(()=>{const t=globalThis.designSpaceDiagnostics.timeline;return t.playing&&t.previewTime>0.7&&t.time<0.85;},null,{timeout:15000});
    await page.waitForFunction(()=>{const t=globalThis.designSpaceDiagnostics.timeline;return !t.playing&&t.clockPreview&&t.previewTime>=2.1;},null,{timeout:15000});
    const s=await snapshot();assert.ok(s.timeline.time<0.001);assert.equal(s.revision,revision);await click('Stop storyboard');
  });
  await check('image import embeds and decodes real pixels',async()=>{
    const png=new PNG({width:32,height:24});for(let i=0;i<png.data.length;i+=4){png.data[i]=20;png.data[i+1]=220;png.data[i+2]=40;png.data[i+3]=255;}
    const pending=page.waitForEvent('filechooser',{timeout:20000});await click('Import image');const chooser=await pending;
    await chooser.setFiles({name:'test-asset.png',mimeType:'image/png',buffer:PNG.sync.write(png)});
    await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.nodes.some(n=>n.type==='Image'&&n.properties.Source.startsWith('data:image/png;base64,')),null,{timeout:20000});
    await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.rendering.imageDecodes>0);const s=await snapshot();assert.deepEqual(s.rendering.warnings,[]);
    const image=s.nodes.find(n=>n.type==='Image');const v=s.surface;const x=Math.round(v.x+v.panX+(Number(image.properties['Canvas.Left'])+16)*v.zoom),y=Math.round(v.y+v.panY+(Number(image.properties['Canvas.Top'])+12)*v.zoom);
    const screenshot=PNG.sync.read(await page.screenshot());const offset=(y*screenshot.width+x)*4;assert.ok(screenshot.data[offset+1]>160&&screenshot.data[offset]<80,'Embedded raster appears on artboard');
  });
  await check('image repaint uses the decode cache',async()=>{
    const count=(await snapshot()).rendering.imageDecodes;await click('Zoom in');await click('Zoom out');await page.waitForTimeout(500);assert.equal((await snapshot()).rendering.imageDecodes,count);await click('Fit artboard');
  });
  await check('template resource authoring previews a real visual tree',async()=>{
    await click('Add Button');const selected=(await snapshot()).selection[0];await click('Open Templates panel');await click('Save control template');await click('Apply control template');
    await page.waitForFunction(name=>{const s=globalThis.designSpaceDiagnostics;return s.nodes.find(n=>n.name===name)?.properties.Template?.includes('ButtonTemplate')&&s.rendering.previewNodes>s.nodes.length;},selected,{timeout:20000});
    const s=await snapshot();assert.deepEqual(s.rendering.previewWarnings,[]);assert.deepEqual(s.rendering.warnings,[]);await page.screenshot({path:directory+'/template-authoring.png'});
    const node=s.nodes.find(n=>n.name===selected),v=s.surface;
    await page.mouse.click(v.x+v.panX+(Number(node.properties['Canvas.Left'])+60)*v.zoom,v.y+v.panY+(Number(node.properties['Canvas.Top'])+20)*v.zoom);
    await page.waitForFunction(name=>globalThis.designSpaceDiagnostics.selection.includes(name),selected);
  });
  await check('visual-state recording edits overrides not base values',async()=>{
    const initial=await snapshot();const name=initial.selection[0];const node=initial.nodes.find(n=>n.name===name);assert.ok(node);
    await click('States');await click('Preview state Pressed');await click('Record visual state properties');await click('Properties');
    await click('Property Opacity');await page.keyboard.press('Control+A');await page.keyboard.insertText('0.35');await page.keyboard.press('Enter');
    await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.states.items.find(s=>s.name==='Pressed').setters.some(s=>s.target===id&&s.property==='Opacity'&&s.value==='0.35'),node.id);
    let s=await snapshot();assert.equal(s.nodes.find(n=>n.id===node.id).properties.Opacity,node.properties.Opacity);assert.equal(s.sourceDirty,false);
    await click('Reset selected state property');
    await page.waitForFunction(id=>!globalThis.designSpaceDiagnostics.states.items.find(s=>s.name==='Pressed').setters.some(s=>s.target===id&&s.property==='Opacity'),node.id);
    await click('Undo');
    await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.states.items.find(s=>s.name==='Pressed').setters.some(s=>s.target===id&&s.property==='Opacity'),node.id);
    await page.screenshot({path:directory+'/state-authoring.png'});await click('Preview base state');await click('Assets');
  });
  await check('template and embedded image survive reload',async()=>{
    await click('Save design');await page.waitForTimeout(1500);await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(()=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.nodes.some(n=>n.properties.Template),null,{timeout:180000});
    const s=await snapshot();assert.ok(s.nodes.some(n=>n.type==='Image'));assert.ok(s.rendering.previewNodes>s.nodes.length);assert.equal(s.sourceDirty,false);assert.equal(s.timeline.autoReverse,true);assert.equal(s.timeline.duration,1);assert.ok(s.states.items.find(state=>state.name==='Pressed').setters.some(setter=>setter.property==='Opacity'&&setter.value==='0.35'));
  });
  await check('no application exceptions or renderer diagnostics',async()=>{
    assert.deepEqual(errors,[]);assert.ok(!log.some(s=>s.startsWith('error: [DesignSpace')));const s=await snapshot();assert.deepEqual(s.rendering.warnings,[]);assert.deepEqual(s.rendering.previewWarnings,[]);
  });
  await page.screenshot({path:directory+'/workspace-edited.png'});
}catch(error){await page.screenshot({path:directory+'/failure.png'}).catch(()=>{});await writeFile(directory+'/failure-dom.html',await page.content().catch(()=>''));throw error;}
finally{
  await writeFile(directory+'/browser.log',log.join('\n'));await writeFile(directory+'/results.json',JSON.stringify({passed:results.length,tests:results,errors},null,2));
  await writeFile(directory+'/diagnostics.json',JSON.stringify(await snapshot().catch(()=>null)??null,null,2));await browser.close();
}
