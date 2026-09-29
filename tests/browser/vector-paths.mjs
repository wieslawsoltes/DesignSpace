import assert from 'node:assert/strict';

// Runs against the same published Uno app; only reads diagnostics, all edits use actual input.
export async function vectorPaths({page,snapshot,click,check,directory}) {
  const point=async(x,y)=>{const v=(await snapshot()).surface;return{x:v.x+v.panX+x*v.zoom,y:v.y+v.panY+y*v.zoom};};
  async function tap(x,y){const p=await point(x,y);await page.mouse.click(p.x,p.y);await page.waitForTimeout(120);}
  async function drag(x,y,dx,dy){const a=await point(x,y),b=await point(dx,dy);await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:8});await page.mouse.up();await page.waitForTimeout(300);}
  async function drawRectangle(x,y,right,bottom){
    const before=await snapshot(),ids=before.nodes.map(n=>n.id);
    await drag(x,y,right,bottom);
    // Diagnostics are sampled, not a synchronous response to mouse.up(). Wait for
    // creation before retaining its identity for the following Boolean operation.
    await page.waitForFunction(({count,revision})=>{
      const s=globalThis.designSpaceDiagnostics;
      return s.nodes.length===count+1&&s.revision===revision+1;
    },{count:before.nodes.length,revision:before.revision},{timeout:15000});
    const state=await snapshot(),created=state.nodes.filter(n=>!ids.includes(n.id));
    assert.equal(created.length,1);assert.equal(created[0].type,'Rectangle');
    assert.deepEqual(state.selection,[created[0].name]);
    return created[0];
  }
  let pathId,originalData;
  await check('pen draft cancellation leaves history and model unchanged',async()=>{
    const before=await snapshot();await click('Tool Pen (P)');await tap(400,384);await drag(496,336,528,304);
    await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.paths.draftPoints===2);
    let s=await snapshot();assert.equal(s.revision,before.revision);assert.equal(s.nodes.length,before.nodes.length);
    await page.keyboard.press('Escape');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.paths.draftPoints===0);
    s=await snapshot();assert.equal(s.revision,before.revision);assert.equal(s.nodes.length,before.nodes.length);
  });
  await check('pen creates a closed cubic path in one transaction',async()=>{
    const before=await snapshot();await tap(400,384);await drag(496,336,528,304);await tap(560,432);await tap(400,384);
    await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.nodes.length===count+1,before.nodes.length);
    const s=await snapshot(),node=s.nodes.find(n=>n.name===s.selection[0]);pathId=node.id;originalData=node.properties.Data;
    assert.equal(node.type,'Path');assert.match(originalData,/C /);assert.match(originalData,/ Z$/);assert.equal(s.revision,before.revision+1);assert.equal(s.sourceDirty,false);
    await page.screenshot({path:directory+'/pen-path.png'});
  });
  await check('direct anchor drag previews then commits once and undoes exactly',async()=>{
    await click('Tool Direct Selection (A)');const before=await snapshot();const h=before.paths.handles.find(h=>h.kind==='Anchor'&&h.segment===0);assert.ok(h);
    const x=h.x+before.surface.x,y=h.y+before.surface.y;
    await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x+24,y+16,{steps:12});await page.waitForTimeout(300);
    assert.equal((await snapshot()).revision,before.revision);await page.mouse.up();
    await page.waitForFunction(revision=>globalThis.designSpaceDiagnostics.revision===revision+1,before.revision);
    assert.notEqual((await snapshot()).nodes.find(n=>n.id===pathId).properties.Data,originalData);
    await click('Undo');await page.waitForFunction(({id,data})=>globalThis.designSpaceDiagnostics.nodes.find(n=>n.id===id)?.properties.Data===data,{id:pathId,data:originalData});
  });
  await check('direct tangent manipulation changes cubic geometry',async()=>{
    const before=await snapshot();const h=before.paths.handles.find(h=>h.kind==='Control2'&&h.segment===0);assert.ok(h);
    const x=h.x+before.surface.x,y=h.y+before.surface.y;
    await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x-20,y+10,{steps:10});await page.mouse.up();
    await page.waitForFunction(revision=>globalThis.designSpaceDiagnostics.revision===revision+1,before.revision);
    assert.notEqual((await snapshot()).nodes.find(n=>n.id===pathId).properties.Data,originalData);
    await page.screenshot({path:directory+'/direct-path-editing.png'});await click('Undo');
  });
  await check('pen inserts a point on the closing segment',async()=>{
    await click('Tool Pen (P)');const before=await snapshot();await tap(480,408);
    await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.paths.segments===count+1,before.paths.segments);
    assert.equal((await snapshot()).revision,before.revision+1);await click('Undo');
  });
  await check('shape conversion preserves object identity',async()=>{
    await click('Asset category Shapes');await click('Add Rectangle');let s=await snapshot();const node=s.nodes.find(n=>n.name===s.selection[0]);assert.ok(node);
    await click('Open Paths panel');await click('Path Convert');await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.nodes.find(n=>n.id===id)?.type==='Path',node.id);
    assert.equal((await snapshot()).paths.handles.filter(h=>h.kind==='Anchor').length,4);
    await click('Undo');await page.waitForFunction(id=>globalThis.designSpaceDiagnostics.nodes.find(n=>n.id===id)?.type==='Rectangle',node.id);
  });
  await check('boolean unite is editable and undo restores both operands',async()=>{
    await click('Tool Rectangle (R)');const a=await drawRectangle(592,352,672,416),b=await drawRectangle(640,384,720,448);
    assert.notEqual(a.id,b.id);
    await click('Tool Selection (V)');await page.keyboard.down('Control');await tap(608,368);await page.keyboard.up('Control');
    await page.waitForFunction(names=>{const s=globalThis.designSpaceDiagnostics;return s.selection.length===2&&names.every(n=>s.selection.includes(n));},[a.name,b.name]);
    const before=await snapshot();await click('Path Unite');
    await page.waitForFunction(({count,revision})=>{const s=globalThis.designSpaceDiagnostics;return s.nodes.length===count-1&&s.revision===revision+1;},{count:before.nodes.length,revision:before.revision});
    const s=await snapshot();assert.equal(s.nodes.find(n=>n.id===a.id).type,'Path');assert.ok(!s.nodes.some(n=>n.id===b.id));assert.equal(s.sourceDirty,false);
    await page.screenshot({path:directory+'/path-combination.png'});await click('Undo');await page.waitForFunction(ids=>ids.every(id=>globalThis.designSpaceDiagnostics.nodes.some(n=>n.id===id&&n.type==='Rectangle')),[a.id,b.id]);
  });
  await check('clipping path and release are real undoable edits',async()=>{
    const before=await snapshot();await click('Path Make clip');await page.waitForFunction(()=>globalThis.designSpaceDiagnostics.nodes.some(n=>n.properties.Clip));
    let s=await snapshot();assert.equal(s.nodes.length,before.nodes.length-1);const clipped=s.nodes.find(n=>n.properties.Clip);assert.ok(clipped.properties.Clip.includes('M '));
    await click('Path Release clip');await page.waitForFunction(id=>!globalThis.designSpaceDiagnostics.nodes.find(n=>n.id===id).properties.Clip,clipped.id);
    await click('Undo');await page.waitForFunction(id=>!!globalThis.designSpaceDiagnostics.nodes.find(n=>n.id===id).properties.Clip,clipped.id);
    await click('Undo');await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.nodes.length===count,before.nodes.length);
  });
  await check('pencil makes a simplified editable freehand path',async()=>{
    await click('Tool Pencil (Y)');const before=await snapshot();let p=await point(600,480);await page.mouse.move(p.x,p.y);await page.mouse.down();
    for(let i=1;i<=24;i++){p=await point(600+i*4,480+Math.sin(i/6)*20);await page.mouse.move(p.x,p.y);}
    await page.mouse.up();await page.waitForFunction(count=>globalThis.designSpaceDiagnostics.nodes.length===count+1,before.nodes.length);
    const s=await snapshot(),node=s.nodes.find(n=>n.name===s.selection[0]);assert.equal(node.type,'Path');assert.match(node.properties.Data,/L /);assert.ok(s.paths.segments<24&&s.paths.segments>1);assert.equal(s.revision,before.revision+1);
  });
  await check('geometry cache and recovery retain authored paths',async()=>{
    await click('Tool Selection (V)');const builds=(await snapshot()).pathBuilds;await click('Zoom in');await click('Zoom out');await page.waitForTimeout(400);assert.equal((await snapshot()).pathBuilds,builds);
    const before=await snapshot();await click('Save design');await page.waitForTimeout(1400);await page.reload({waitUntil:'domcontentloaded'});
    await page.waitForFunction(count=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.nodes.length===count,before.nodes.length,{timeout:180000});
    const s=await snapshot();assert.equal(s.nodes.find(n=>n.id===pathId).properties.Data,originalData);assert.equal(s.sourceDirty,false);assert.deepEqual(s.rendering.warnings,[]);
    await page.screenshot({path:directory+'/vector-workspace.png'});
  });
  const {anchorSelection}=await import('./anchor-selection.mjs');
  await anchorSelection({page,snapshot,click,check,directory});
  const {stateTransitions}=await import('./state-transitions.mjs');
  await stateTransitions({page,snapshot,click,check,directory});
  const {strokes}=await import('./strokes.mjs');
  await strokes({page,snapshot,click,check,directory});
  const {brushes}=await import('./brushes.mjs');
  await brushes({page,snapshot,click,check,directory});
  const {animationTracks}=await import('./animation-tracks.mjs');
  await animationTracks({page,snapshot,click,check,directory});
}
