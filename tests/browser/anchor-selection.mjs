import assert from 'node:assert/strict';

// Read-only diagnostics locate the real handles. Every edit uses pointer/keyboard input.
export async function anchorSelection({page,snapshot,click,check,directory}) {
  const wait=async()=>page.waitForTimeout(300);
  async function world(x,y){const v=(await snapshot()).surface;return{x:v.x+v.panX+x*v.zoom,y:v.y+v.panY+y*v.zoom};}
  const anchors=s=>s.paths.handles.filter(h=>h.kind==='Anchor');
  const absolute=(s,h)=>({x:s.surface.x+h.x,y:s.surface.y+h.y});
  async function tapHandle(segment,shift=false){
    const s=await snapshot(),h=anchors(s).find(h=>h.segment===segment);assert.ok(h,`anchor ${segment}`);const p=absolute(s,h);
    if(shift)await page.keyboard.down('Shift');await page.mouse.click(p.x,p.y);if(shift)await page.keyboard.up('Shift');await wait();
  }
  async function command(name){
    await click('Open Paths panel');
    for(let i=0;i<8;i++){
      const s=await snapshot(),c=s.controls.find(c=>c.Name==='Path '+name);assert.ok(c,`Path command ${name}`);
      if(c.Y>115&&c.Y+c.Height<s.height-55){await click('Path '+name);return;}
      await page.mouse.move(s.width-120,450);await page.mouse.wheel(0,c.Y<115?-280:280);await wait();
    }
    throw new Error(`Could not bring Path ${name} into view`);
  }
  async function undo(data,id){await click('Undo');await page.waitForFunction(({id,data})=>globalThis.designSpaceDiagnostics.nodes.find(n=>n.id===id)?.properties.Data===data,{id,data});}
  let id,original;
  await check('multi-point fixture is authored through Pen',async()=>{
    await click('Tool Pen (P)');
    for(const [x,y] of [[300,350],[320,290],[400,300],[440,360],[360,440],[300,350]]){const p=await world(x,y);await page.mouse.click(p.x,p.y);await page.waitForTimeout(80);}
    await wait();let s=await snapshot();const n=s.nodes.find(n=>n.name===s.selection[0]);assert.equal(n.type,'Path');id=n.id;original=n.properties.Data;
    await click('Tool Direct Selection (A)');s=await snapshot();assert.equal(anchors(s).length,5);
  });
  await check('Shift selection toggles anchors without history or snapping',async()=>{
    const revision=(await snapshot()).revision;await tapHandle(-1);await tapHandle(0,true);let s=await snapshot();assert.equal(s.paths.anchorCount,2);
    await tapHandle(0,true);assert.equal((await snapshot()).paths.anchorCount,1);await tapHandle(0,true);
    s=await snapshot();assert.equal(s.paths.anchorCount,2);assert.equal(s.revision,revision);assert.equal(s.nodes.find(n=>n.id===id).properties.Data,original);
    await page.screenshot({path:directory+'/multi-point-selection.png'});
  });
  await check('batch drag previews all selected anchors and commits once',async()=>{
    const before=await snapshot();const h=anchors(before).find(h=>h.segment===-1),p=absolute(before,h);
    await page.keyboard.down('Alt');await page.mouse.move(p.x,p.y);await page.mouse.down();await page.mouse.move(p.x+24,p.y+16,{steps:12});await wait();
    const preview=await snapshot();assert.equal(preview.revision,before.revision);
    for(const segment of [-1,0,1]){
      const a=anchors(before).find(h=>h.segment===segment),b=anchors(preview).find(h=>h.segment===segment);const selected=segment!==1;
      assert.ok(Math.abs((b.x-a.x)-(selected?24:0))<1);assert.ok(Math.abs((b.y-a.y)-(selected?16:0))<1);
    }
    await page.mouse.up();await page.keyboard.up('Alt');await wait();const s=await snapshot();assert.equal(s.revision,before.revision+1);assert.equal(s.paths.anchorCount,2);
    const edited=s.nodes.find(n=>n.id===id).properties.Data;assert.notEqual(edited,original);await undo(original,id);
    await click('Redo');await page.waitForFunction(({id,data})=>globalThis.designSpaceDiagnostics.nodes.find(n=>n.id===id).properties.Data===data,{id,data:edited});await undo(original,id);
  });
  await check('point marquee selects anchors rather than whole objects',async()=>{
    const before=await snapshot(),subset=anchors(before).filter(h=>[-1,0].includes(h.segment));
    const x=Math.min(...subset.map(h=>h.x))+before.surface.x-14,y=Math.min(...subset.map(h=>h.y))+before.surface.y-14;
    const right=Math.max(...subset.map(h=>h.x))+before.surface.x+14,bottom=Math.max(...subset.map(h=>h.y))+before.surface.y+14;
    await page.keyboard.down('Shift');await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(right,bottom,{steps:10});await page.mouse.up();await page.keyboard.up('Shift');await wait();
    const s=await snapshot();assert.equal(s.paths.anchorCount,2);assert.equal(s.selection.length,1);assert.equal(s.revision,before.revision);assert.equal(s.nodes.find(n=>n.id===id).properties.Data,original);
  });
  await check('Escape cancels a live marquee and restores point selection',async()=>{
    const before=await snapshot(),all=anchors(before);const x=Math.min(...all.map(h=>h.x))+before.surface.x-20,y=Math.min(...all.map(h=>h.y))+before.surface.y-20;
    const right=Math.max(...all.map(h=>h.x))+before.surface.x+20,bottom=Math.max(...all.map(h=>h.y))+before.surface.y+20;
    await page.keyboard.down('Shift');await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(right,bottom,{steps:10});await wait();assert.equal((await snapshot()).paths.anchorCount,5);
    await page.keyboard.press('Escape');await page.mouse.up();await page.keyboard.up('Shift');await wait();const s=await snapshot();assert.equal(s.paths.anchorCount,before.paths.anchorCount);assert.equal(s.revision,before.revision);
  });
  await check('select-all and clear are scoped to path points',async()=>{
    const revision=(await snapshot()).revision;await page.keyboard.press('Control+A');await wait();let s=await snapshot();assert.equal(s.paths.anchorCount,5);assert.equal(s.selection.length,1);
    await page.keyboard.press('Control+Shift+A');await wait();s=await snapshot();assert.equal(s.paths.anchorCount,0);assert.equal(s.selection.length,1);assert.equal(s.revision,revision);
  });
  await check('keyboard batch nudge moves each point by one artboard unit',async()=>{
    await page.keyboard.press('Control+A');await wait();const before=await snapshot();await page.keyboard.press('ArrowRight');await wait();const after=await snapshot();assert.equal(after.revision,before.revision+1);
    for(const a of anchors(before)){const b=anchors(after).find(h=>h.segment===a.segment);assert.ok(Math.abs(b.x-a.x-before.surface.zoom)<0.01);assert.ok(Math.abs(b.y-a.y)<0.01);}
    assert.equal(after.paths.anchorCount,5);await undo(original,id);
  });
  await check('point alignment is a single undoable geometry operation',async()=>{
    await tapHandle(-1);await tapHandle(0,true);const before=await snapshot();await command('Align Top');const s=await snapshot();const a=anchors(s).find(h=>h.segment===-1),b=anchors(s).find(h=>h.segment===0);
    assert.ok(Math.abs(a.y-b.y)<0.01);assert.equal(s.revision,before.revision+1);assert.equal(s.sourceDirty,false);await undo(original,id);
  });
  await check('point distribution equalizes intervals without moving endpoints',async()=>{
    await command('Select points');const before=await snapshot(),x=anchors(before).map(h=>h.x).sort((a,b)=>a-b);await command('Distribute X');const s=await snapshot(),next=anchors(s).map(h=>h.x).sort((a,b)=>a-b);
    assert.ok(Math.abs(next[0]-x[0])<0.01&&Math.abs(next.at(-1)-x.at(-1))<0.01);for(let i=1;i<next.length;i++)assert.ok(Math.abs(next[i]-next[i-1]-(x.at(-1)-x[0])/4)<0.01);
    assert.equal(s.revision,before.revision+1);await page.screenshot({path:directory+'/point-distribution.png'});await undo(original,id);
  });
  await check('batch point deletion retains the object and undoes exactly',async()=>{
    await tapHandle(-1);await tapHandle(0,true);const before=await snapshot();await page.keyboard.press('Delete');await wait();let s=await snapshot();assert.equal(anchors(s).length,3);assert.equal(s.nodes.length,before.nodes.length);assert.equal(s.revision,before.revision+1);assert.equal(s.paths.anchorCount,0);await undo(original,id);
  });
  await check('Escape cancels multi-anchor dragging without committing',async()=>{
    await command('Select points');const before=await snapshot(),p=absolute(before,anchors(before)[0]);await page.mouse.move(p.x,p.y);await page.mouse.down();await page.mouse.move(p.x+18,p.y-16,{steps:8});await page.keyboard.press('Escape');await page.mouse.up();await wait();
    const s=await snapshot();assert.equal(s.revision,before.revision);assert.equal(s.nodes.find(n=>n.id===id).properties.Data,original);assert.equal(s.paths.anchorCount,5);
  });
  await check('batch-authored geometry survives save and recovery',async()=>{
    await page.keyboard.press('ArrowDown');await wait();const data=(await snapshot()).nodes.find(n=>n.id===id).properties.Data;assert.notEqual(data,original);
    await click('Save design');await page.waitForTimeout(1500);await page.reload({waitUntil:'domcontentloaded'});
    await page.waitForFunction(({id,data})=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.nodes.find(n=>n.id===id)?.properties.Data===data,{id,data},{timeout:180000});assert.equal((await snapshot()).sourceDirty,false);
  });
}
