import {chromium} from 'playwright';
import {mkdir,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import {animationTracks} from './animation-tracks.mjs';

const base=process.argv[2]||'http://127.0.0.1:4173/DesignSpace/';
const directory='artifacts/verification/animation';await mkdir(directory,{recursive:true});
const browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-webgl','--enable-unsafe-swiftshader','--no-sandbox']});
const context=await browser.newContext({viewport:{width:1600,height:1000},deviceScaleFactor:1,acceptDownloads:true});
const page=await context.newPage(),errors=[],log=[],results=[];
page.on('console',message=>{const line=message.type()+': '+message.text();log.push(line);if(message.type()==='error')console.error(line);});
page.on('pageerror',error=>{errors.push(error.message);console.error(error);});
const snapshot=()=>page.evaluate(()=>globalThis.designSpaceDiagnostics);
async function click(name){
  await page.waitForFunction(name=>globalThis.designSpaceDiagnostics?.controls.some(c=>c.Name===name),name,{timeout:15000});
  const c=(await snapshot()).controls.find(c=>c.Name===name);assert.ok(c,`Visible control ${name}`);
  await page.mouse.click(c.X+c.Width/2,c.Y+c.Height/2);await page.waitForTimeout(300);
}
async function check(name,run){await run();results.push(name);console.log('PASS',name);}
try {
  await page.goto(base+'?diagnostics=1',{waitUntil:'domcontentloaded'});
  await page.waitForFunction(()=>globalThis.designSpaceDiagnostics?.ready&&globalThis.designSpaceDiagnostics.drawCount>0,null,{timeout:180000});
  await animationTracks({page,snapshot,click,check,directory});
  assert.deepEqual(errors,[]);const state=await snapshot();assert.deepEqual(state.rendering.warnings,[]);assert.deepEqual(state.rendering.previewWarnings,[]);
  assert.ok(!log.some(line=>line.startsWith('error: [DesignSpace')));
} catch(error){
  await writeFile(directory+'/failure.txt',String(error.stack??error));await page.screenshot({path:directory+'/failure.png'}).catch(()=>{});throw error;
} finally {
  await writeFile(directory+'/results.json',JSON.stringify({passed:results.length,tests:results,errors},null,2));
  await writeFile(directory+'/diagnostics.json',JSON.stringify(await snapshot().catch(()=>null)??null,null,2));
  await writeFile(directory+'/browser.log',log.join('\n'));await browser.close();
}
