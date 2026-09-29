import { chromium } from 'playwright';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const base=process.argv[2]||'http://127.0.0.1:4173/DesignSpace/';
const directory='artifacts/verification/workspace';await mkdir(directory,{recursive:true});
const browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-webgl','--enable-unsafe-swiftshader','--no-sandbox']});
const context=await browser.newContext({viewport:{width:1600,height:1000},deviceScaleFactor:1,acceptDownloads:true});
let page=await context.newPage();const results=[],errors=[],log=[];
function observe(p){p.on('pageerror',e=>errors.push(String(e)));p.on('console',m=>log.push(m.type()+': '+m.text()));}
observe(page);
const snapshot=()=>page.evaluate(()=>globalThis.designSpaceDiagnostics);
const wait=(fn,arg=null)=>page.waitForFunction(fn,arg,{timeout:20000});
const settle=()=>page.waitForTimeout(400);
async function start(){await page.goto(base+'?diagnostics=1',{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.drawCount>0,null,{timeout:180000});await settle();}
async function bounds(name){
  // Capture the rendered control in the same observation that satisfies the wait.
  // A model update can precede its replacement tab's first measure/arrange pass.
  const observed=await wait(name=>{
    const c=globalThis.designSpaceDiagnostics.controls.find(c=>c.Name===name);
    return c&&c.Width>0&&c.Height>0?c:null;
  },name);
  try{return await observed.jsonValue();}finally{await observed.dispose();}
}
async function click(name){
  const c=await bounds(name);
  await page.mouse.click(c.X+c.Width/2,c.Y+c.Height/2);await settle();
}
async function select(id){const tab=(await snapshot()).workspace.documents.find(d=>d.id===id);assert.ok(tab);await click('Document tab '+tab.title);await wait(id=>globalThis.designSpaceDiagnostics.workspace.active===id,id);}
async function newDocument(){const count=(await snapshot()).workspace.documents.length;await click('New design');await wait(count=>globalThis.designSpaceDiagnostics.workspace.documents.length===count+1,count);return (await snapshot()).workspace.active;}
async function edit(name,text){await click(name);await page.keyboard.press('Control+A');await page.keyboard.insertText(text);await page.keyboard.press('Tab');await settle();}
async function source(text){await click('XAML view');await edit('XAML source editor',text);await wait(text=>globalThis.designSpaceDiagnostics.xaml.replace(/\r\n?/g,'\n')===text.replace(/\r\n?/g,'\n'),text);}
async function changed(revision){await wait(revision=>globalThis.designSpaceDiagnostics.revision===revision+1,revision);await settle();assert.equal((await snapshot()).revision,revision+1);}
async function addButton(){const before=await snapshot();await click('Assets');await click('Asset category Controls');await click('Add Button');await wait(n=>globalThis.designSpaceDiagnostics.nodes.length===n+1,before.nodes.length);const s=await snapshot();return s.nodes.find(n=>n.name===s.selection[0]);}
async function download(command,file){const pending=page.waitForEvent('download',{timeout:20000});await click(command);const output=await pending;await output.saveAs(directory+'/'+file);return JSON.parse(await readFile(directory+'/'+file,'utf8'));}
async function close(id,choice){const tab=(await snapshot()).workspace.documents.find(d=>d.id===id);await click('Close document '+tab.title);if(choice)await click(choice+' document close');await wait(()=>!globalThis.designSpaceDiagnostics.workspace.busy);}
async function check(name,run){await run();results.push(name);console.log('PASS workspace:',name);}
let main,second,firstButton,secondButton,validSecond,savedWorkspace;
try{
  await start();main=(await snapshot()).workspace.active;
  await check('one initial document with tools next to the artboard',async()=>{
    const s=await snapshot();assert.equal(s.workspace.documents.length,1);assert.equal(s.nodes.length,19);
    const tool=s.controls.find(c=>c.Name==='Tool Selection (V)');assert.ok(tool.X>180&&tool.X<s.surface.x);assert.ok(s.surface.x-tool.X<42);
    assert.ok(!s.controls.some(c=>c.Name==='Scroll document tabs right'));await page.screenshot({path:directory+'/initial-workspace.png'});
  });
  await check('new document preserves the original design and independent selection',async()=>{
    firstButton=await addButton();second=await newDocument();let s=await snapshot();assert.equal(s.nodes.length,1);assert.equal(s.selection.length,0);assert.ok(!s.canUndo);assert.equal(s.workspace.documents.find(d=>d.id===main).nodes,20);
    secondButton=await addButton();await select(main);s=await snapshot();assert.ok(s.nodes.some(n=>n.id===firstButton.id));assert.ok(s.selection.includes(firstButton.name));assert.ok(!s.nodes.some(n=>n.id===secondButton.id));
  });
  await check('undo and redo are isolated across tab switches',async()=>{
    await select(second);let s=await snapshot();await click('Undo');await changed(s.revision);assert.equal((await snapshot()).nodes.length,1);
    await select(main);s=await snapshot();assert.equal(s.nodes.length,20);assert.equal(s.canRedo,false);await click('Undo');await changed(s.revision);assert.equal((await snapshot()).nodes.length,19);
    await select(second);assert.equal((await snapshot()).canRedo,true);s=await snapshot();await click('Redo');await changed(s.revision);assert.ok((await snapshot()).nodes.some(n=>n.id===secondButton.id));
    await select(main);s=await snapshot();await click('Redo');await changed(s.revision);assert.ok((await snapshot()).nodes.some(n=>n.id===firstButton.id));
  });
  await check('per-document viewport survives switching without fitting it again',async()=>{
    await click('Zoom in');await settle();const view=(await snapshot()).surface;await select(second);await click('Fit artboard');await select(main);const actual=(await snapshot()).surface;
    for(const key of ['zoom','panX','panY'])assert.ok(Math.abs(actual[key]-view[key])<0.001,key);
  });
  await check('invalid source drafts survive switches without contaminating other documents',async()=>{
    await select(second);validSecond=(await snapshot()).xaml;const count=(await snapshot()).nodes.length;await source('<Canvas>');const rev=(await snapshot()).revision;await click('Apply XAML');assert.equal((await snapshot()).revision,rev);
    await select(main);assert.equal((await snapshot()).sourceDirty,false);assert.ok((await snapshot()).xaml.includes('Ideas, brought to life.'));
    await select(second);const s=await snapshot();assert.equal(s.mode,'XAML');assert.equal(s.xaml,'<Canvas>');assert.equal(s.sourceDirty,true);assert.equal(s.nodes.length,count);
  });
  await check('valid source can apply after returning to its own document revision',async()=>{
    await source(validSecond.replace('Content="Button"','Content="Second document"'));const rev=(await snapshot()).revision;await click('Apply XAML');await changed(rev);const s=await snapshot();assert.equal(s.sourceDirty,false);assert.equal(s.nodes.find(n=>n.id===secondButton.id).properties.Content,'Second document');await click('Design view');
  });
  await check('horizontal and vertical splits retain document-specific modes',async()=>{
    await click('Split view');let s=await snapshot();let editor=s.controls.find(c=>c.Name==='XAML source editor');assert.ok(editor.X>s.surface.x+s.surface.width-2);
    await click('Change split orientation');await wait(()=>globalThis.designSpaceDiagnostics.workspace.orientation==='Horizontal');s=await snapshot();editor=s.controls.find(c=>c.Name==='XAML source editor');assert.ok(editor.Y>=s.surface.y+s.surface.height);
    await page.screenshot({path:directory+'/horizontal-split.png'});await select(main);assert.equal((await snapshot()).mode,'Design');await select(second);assert.equal((await snapshot()).mode,'Split');assert.equal((await snapshot()).workspace.orientation,'Horizontal');await click('Design view');
  });
  await check('Design and Animation profiles switch without document changes',async()=>{
    await click('Tool Selection (V)');const s=await snapshot();await page.keyboard.press('F6');await wait(()=>globalThis.designSpaceDiagnostics.workspace.profile==='Animation');assert.ok((await snapshot()).workspace.timelineHeight>s.workspace.timelineHeight);assert.equal((await snapshot()).revision,s.revision);
    await page.screenshot({path:directory+'/animation-workspace.png'});await page.keyboard.press('F6');await wait(()=>globalThis.designSpaceDiagnostics.workspace.profile==='Design');assert.equal((await snapshot()).workspace.timelineHeight,s.workspace.timelineHeight);
  });
  await check('sample-data drafts remain attached to their document',async()=>{
    await select(second);await click('Data');await edit('Sample data JSON','{"Title":"Second draft"}');
    await wait(()=>{const raw=localStorage.getItem('designspace.v1.recovery.json');if(!raw)return false;const envelope=JSON.parse(raw);if(!envelope.Workspace)return false;const w=JSON.parse(envelope.Workspace);return w.documents.find(d=>d.id===w.activeDocumentId)?.editor.panels.Data?.values.JSON==='{"Title":"Second draft"}';});
    await select(main);assert.notEqual((await snapshot()).controls.find(c=>c.Name==='Sample data JSON')?.Text,'{"Title":"Second draft"}');
    await select(second);assert.equal((await snapshot()).controls.find(c=>c.Name==='Sample data JSON').Text,'{"Title":"Second draft"}');const rev=(await snapshot()).revision;await click('Apply sample data');await changed(rev);await click('Assets');
  });
  await check('stroke panel drafts survive tab changes without editing the base',async()=>{
    await click('Asset category Shapes');await click('Add Rectangle');await click('Open Stroke panel');const s=await snapshot();const selected=s.nodes.find(n=>n.name===s.selection[0]);await edit('Stroke Dash array','0 0');await select(main);await select(second);
    assert.equal((await snapshot()).controls.find(c=>c.Name==='Stroke Dash array').Text,'0 0');assert.equal((await snapshot()).nodes.find(n=>n.id===selected.id).properties.StrokeDashArray,undefined);await click('Reload stroke settings');await click('Properties');
  });
  await check('Save All writes each changed document and restores the active tab',async()=>{
    const saved=[];const listener=d=>saved.push(d);page.on('download',listener);const before=await snapshot();await click('Save all documents');await wait(()=>!globalThis.designSpaceDiagnostics.workspace.busy&&globalThis.designSpaceDiagnostics.workspace.documents.every(d=>!d.dirty));
    assert.equal(saved.length,2);assert.equal((await snapshot()).workspace.active,before.workspace.active);
    for(const d of saved){await d.saveAs(directory+'/'+d.suggestedFilename());const doc=JSON.parse(await readFile(directory+'/'+d.suggestedFilename(),'utf8'));assert.ok(doc.root.id);}
    page.off('download',listener);
  });
  await check('document tabs reorder with real drag input',async()=>{
    const a=await bounds('Document tab MainPage.xaml'),b=await bounds('Document tab Page1.xaml');
    await page.mouse.move(a.X+a.Width/2,a.Y+a.Height/2);await page.mouse.down();await page.mouse.move(b.X+b.Width+15,b.Y+b.Height/2,{steps:12});await page.mouse.up();
    await wait(id=>{
      const s=globalThis.designSpaceDiagnostics,docs=s.workspace.documents;
      if(docs.at(-1).id!==id)return false;
      const tabs=docs.map(d=>s.controls.find(c=>c.Name==='Document tab '+d.title));
      return tabs.every(t=>t&&t.Width>0&&t.Height>0)&&tabs.every((t,i)=>i===0||t.X>=tabs[i-1].X+tabs[i-1].Width);
    },main);
    assert.equal((await snapshot()).workspace.active,second);
  });
  await check('pin and close-other commands protect pinned documents',async()=>{
    async function tabMenu(id,command){
      const tab=(await snapshot()).workspace.documents.find(d=>d.id===id);assert.ok(tab,'Document still exists');
      const c=await bounds('Document tab '+tab.title);
      await page.mouse.click(c.X+c.Width/2,c.Y+c.Height/2,{button:'right'});
      const name=command+' '+tab.title;
      await wait(name=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name===name),name);
      // Popup layout can be observable before its opening animation accepts input.
      await settle();await click(name);
    }
    await tabMenu(main,'Pin document');await wait(id=>globalThis.designSpaceDiagnostics.workspace.documents.find(d=>d.id===id).pinned,main);
    await newDocument();await newDocument();await select(second);await tabMenu(second,'Close other unpinned documents');
    await click('Discard document close');await click('Discard document close');await wait(()=>!globalThis.designSpaceDiagnostics.workspace.busy&&globalThis.designSpaceDiagnostics.workspace.documents.length===2);
    assert.ok((await snapshot()).workspace.documents.some(d=>d.id===main&&d.pinned));
  });
  await check('document close offers Cancel and explicit discard without losing siblings',async()=>{
    const temp=await newDocument();await close(temp,'Cancel');assert.ok((await snapshot()).workspace.documents.some(d=>d.id===temp));await close(temp,'Discard');assert.equal((await snapshot()).workspace.documents.length,2);assert.ok((await snapshot()).workspace.documents.some(d=>d.id===main));
  });
  await check('Save-and-close emits a valid document before removing its tab',async()=>{
    const temp=await newDocument();await addButton();const pending=page.waitForEvent('download');await close(temp,'Save');const d=await pending;await d.saveAs(directory+'/closed-document.designspace');const doc=JSON.parse(await readFile(directory+'/closed-document.designspace','utf8'));assert.equal(doc.root.children.length,1);assert.ok(!(await snapshot()).workspace.documents.some(d=>d.id===temp));
  });
  await check('opening XAML adds a document rather than replacing existing tabs',async()=>{
    const before=await snapshot();const chosen=page.waitForEvent('filechooser');await click('Open design');await(await chosen).setFiles({name:'Imported.xaml',mimeType:'text/plain',buffer:Buffer.from('<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="480" Height="360"><TextBlock Text="Imported document"/></Canvas>')});
    await wait(n=>globalThis.designSpaceDiagnostics.workspace.documents.length===n+1,before.workspace.documents.length);assert.ok((await snapshot()).nodes.some(n=>n.properties.Text==='Imported document'));const id=(await snapshot()).workspace.active;await close(id);assert.equal((await snapshot()).workspace.documents.length,2);
  });
  await check('orphaned stroke drafts retain their text until explicitly discarded',async()=>{
    await select(second);await click('Asset category Shapes');await click('Add Rectangle');await click('Open Stroke panel');const before=await snapshot();const target=before.nodes.find(n=>n.name===before.selection[0]);
    await edit('Stroke Dash array','0 0');await click('Tool Selection (V)');const rev=(await snapshot()).revision;await page.keyboard.press('Delete');await changed(rev);
    await select(main);await select(second);const saved=await download('Save workspace','orphaned-stroke.designspace-workspace');const draft=saved.documents.find(d=>d.id===second).editor.panels.Stroke;
    assert.equal(draft.values.StrokeDashArray,'0 0');assert.equal(draft.hasChanges,true);assert.ok(draft.targets.includes(target.id));assert.ok(!saved.documents.find(d=>d.id===second).document.root.children.some(n=>n.id===target.id));
    await click('Reload stroke settings');await click('Properties');
  });
  await check('timing and template drafts remain isolated between documents',async()=>{
    await select(main);await click('Edit storyboard timing');await edit('Storyboard duration','invalid duration');await select(second);await select(main);assert.equal((await snapshot()).controls.find(c=>c.Name==='Storyboard duration').Text,'invalid duration');await click('Reload storyboard timing');
    await click('Open Templates panel');await edit('ControlTemplate source','<ControlTemplate incomplete');await select(second);await select(main);assert.equal((await snapshot()).controls.find(c=>c.Name==='ControlTemplate source').Text,'<ControlTemplate incomplete');
    const snapshotFile=await download('Save workspace','template-draft.designspace-workspace');assert.equal(snapshotFile.documents.find(d=>d.id===main).editor.panels.Templates.hasChanges,true);
    // Retain this deliberate draft through later workspace export/recovery, not a native-document save.
    await click('Properties');await select(second);
  });
  await check('workspace download preserves invalid drafts and all document identities',async()=>{
    await select(second);await source('<Retained invalid source');savedWorkspace=await download('Save workspace','all-documents.designspace-workspace');assert.equal(savedWorkspace.documents.length,2);assert.equal(savedWorkspace.activeDocumentId,second);assert.equal(savedWorkspace.documents.find(d=>d.id===second).editor.sourceDraft,'<Retained invalid source');assert.equal((await snapshot()).sourceDirty,true);
  });
  await check('local recovery restores every tab and keeps invalid source unapplied',async()=>{
    await page.waitForTimeout(1800);await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(()=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.workspace.documents.length===2,null,{timeout:180000});
    let s=await snapshot();assert.equal(s.workspace.active,second);assert.equal(s.xaml,'<Retained invalid source');assert.equal(s.sourceDirty,true);await select(main);assert.ok((await snapshot()).nodes.some(n=>n.id===firstButton.id));assert.equal((await snapshot()).canUndo,false);await select(second);assert.equal((await snapshot()).xaml,'<Retained invalid source');
  });
  await check('workspace import is a real upload with preserved drafts',async()=>{
    const fresh=await browser.newContext({viewport:{width:1600,height:1000},acceptDownloads:true});page=await fresh.newPage();observe(page);await start();
    const clean=await download('Save workspace','clean-workspace.designspace-workspace');assert.equal(clean.documents[0].hasUnsavedChanges,false);assert.ok(Object.values(clean.documents[0].editor.panels).every(p=>!p.hasChanges),'Unchanged panel initialization is not a user draft');
    const pending=page.waitForEvent('filechooser');await click('Open design');await(await pending).setFiles(directory+'/all-documents.designspace-workspace');
    await wait(()=>globalThis.designSpaceDiagnostics.workspace.documents.length===2);assert.equal((await snapshot()).workspace.active,second);assert.equal((await snapshot()).xaml,'<Retained invalid source');await page.screenshot({path:directory+'/recovered-documents.png'});
  });
  await check('opening an invalid workspace does not replace live documents',async()=>{
    const before=await snapshot();const chosen=page.waitForEvent('filechooser');await click('Open design');await(await chosen).setFiles({name:'Invalid.designspace-workspace',mimeType:'application/json',buffer:Buffer.from('{"formatVersion":99,"documents":[]}')});await wait(()=>!globalThis.designSpaceDiagnostics.workspace.busy);const after=await snapshot();assert.equal(after.workspace.active,before.workspace.active);assert.equal(after.revision,before.revision);assert.equal(after.xaml,before.xaml);assert.equal(after.workspace.documents.length,2);
  });
  await check('no unhandled application errors during document operations',async()=>{assert.deepEqual(errors,[]);assert.ok(!log.some(line=>line.startsWith('error: [DesignSpace')));});
}catch(error){await writeFile(directory+'/failure.txt',String(error.stack??error));await page.screenshot({path:directory+'/failure.png'}).catch(()=>{});throw error;}
finally{await writeFile(directory+'/results.json',JSON.stringify({passed:results.length,tests:results,errors},null,2));await writeFile(directory+'/browser.log',log.join('\n'));await writeFile(directory+'/diagnostics.json',JSON.stringify(await snapshot().catch(()=>null),null,2));await browser.close();}
