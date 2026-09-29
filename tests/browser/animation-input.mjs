import assert from 'node:assert/strict';

// Layout bounds alone do not imply visibility: a scrolled field may be clipped
// behind the fixed preview bar. Use the actual named ScrollViewer viewport.
export function animationInput(page,snapshot) {
  const settle=()=>page.waitForTimeout(400);
  const fixed=new Set(['Animation preview time','Scrub animation preview']);
  async function reveal(name) {
    await page.waitForFunction(name=>globalThis.designSpaceDiagnostics.controls.some(c=>c.Name===name),name,{timeout:15000});
    for(let i=0;i<18;i++) {
      const state=await snapshot(),control=state.controls.find(c=>c.Name===name);
      assert.ok(control,`Animation control ${name}`);
      if(fixed.has(name)) {
        assert.ok(control.Y>0&&control.Y+control.Height<state.height,`Fixed control is visible: ${name}`);
        return control;
      }
      const viewport=state.controls.find(c=>c.Name==='Animation track scroll area');
      assert.ok(viewport,'Animation inspector exposes its actual scroll viewport');
      const top=viewport.Y+4,bottom=viewport.Y+viewport.Height-4;
      if(control.Y>=top&&control.Y+control.Height<=bottom)return control;
      const delta=control.Y<top?control.Y-top-12:control.Y+control.Height-bottom+12;
      await page.mouse.move(viewport.X+viewport.Width-20,viewport.Y+viewport.Height/2);
      await page.mouse.wheel(0,Math.max(-300,Math.min(300,delta)));
      await settle();
    }
    throw new Error(`Cannot reveal animation control ${name}`);
  }
  async function press(name) {
    let bounds=await reveal(name),stable=false;
    for(let i=0;i<8;i++) {
      await settle();const next=await reveal(name);
      if(next.X===bounds.X&&next.Y===bounds.Y&&next.Width===bounds.Width&&next.Height===bounds.Height){bounds=next;stable=true;break;}
      bounds=next;
    }
    assert.ok(stable,`Control layout settled before clicking ${name}`);
    await page.mouse.click(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2);
    await settle();
  }
  async function edit(name,value) {
    await press(name);
    await page.waitForFunction(name=>globalThis.designSpaceDiagnostics.focus===name,name,{timeout:10000});
    await page.keyboard.press('Control+A');await page.keyboard.insertText(value);
    await page.waitForFunction(({name,value})=>globalThis.designSpaceDiagnostics.controls.find(c=>c.Name===name)?.Text===value,{name,value},{timeout:10000});
    await page.keyboard.press('Tab');await settle();
  }
  async function choose(name,index) {
    await press(name);await page.keyboard.press('Home');
    for(let i=0;i<index;i++)await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');await settle();
  }
  return {reveal,press,edit,choose};
}
