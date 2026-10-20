// Browser acceptance checks. Run against a static server; no game API required.
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {mkdtempSync,readFileSync,existsSync,rmSync,writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
const base=process.env.QUOTA_WEB_URL||'http://127.0.0.1:8765/';
const profile=mkdtempSync(path.join(tmpdir(),'quota-browser-'));
const chromePath=process.env.CHROME_PATH||'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const chrome=spawn(chromePath,['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{stdio:['ignore','ignore','pipe']});
let errors='';chrome.stderr.on('data',data=>{errors=(errors+data).slice(-4000);});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
let migrated = false;
const oldPage = `<!doctype html><div id="legacy">軽量Web版・初期版</div><script>navigator.serviceWorker.register('./sw.js');</script>`;
// Reproduce the first prototype's cache-first navigation, global cache name,
// and lack of a controllerchange/reload handler in its page.
const oldWorker = `const CACHE='quota-web-lite-2026-10-20-b';
self.addEventListener('install',e=>e.waitUntil((async()=>{const c=await caches.open(CACHE);await c.add('./');await self.skipWaiting();})()));
self.addEventListener('activate',e=>e.waitUntil(self.clients.claim()));
self.addEventListener('fetch',e=>{if(e.request.method!=='GET')return;e.respondWith((async()=>{const c=await caches.open(CACHE);return await c.match(e.request)||fetch(e.request);})());});`;
const migrationWorker = readFileSync(new URL('../portal/retire-quota-lite-sw.js', import.meta.url), 'utf8');
const recoveryPage = readFileSync(new URL('../portal/quota-recover.html', import.meta.url), 'utf8');
const server = createServer((request,response)=>{
  response.setHeader('Cache-Control','no-store');
  if(request.url==='/quota/sw.js') {response.setHeader('Content-Type','text/javascript');response.end(migrated?migrationWorker:oldWorker);}
  else {response.setHeader('Content-Type','text/html');response.end(request.url==='/quota-recover.html'?recoveryPage:migrated?'<canvas id="unity-canvas"></canvas>':oldPage);}
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const origin=`http://127.0.0.1:${server.address().port}`;
let ws;
try{
  const portFile=path.join(profile,'DevToolsActivePort');let port;
  for(let i=0;i<150;i++){
    if(existsSync(portFile)){const value=readFileSync(portFile,'utf8').split('\n')[0];if(/^\d+$/.test(value)&&Number(value)>0){port=value;break;}}
    await sleep(100);
  }
  if(!port)throw Error('Chrome unavailable: '+errors);
  const pages=await fetch(`http://127.0.0.1:${port}/json`).then(r=>r.json());
  ws=new WebSocket(pages.find(p=>p.type==='page').webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{ws.onopen=resolve;ws.onerror=reject;});
  let serial=0,audioContexts=0,audioRan=false;const waiting=new Map(),exceptions=[];
  ws.onmessage=event=>{
    const msg=JSON.parse(event.data);
    if(msg.id&&waiting.has(msg.id)){const [resolve,reject]=waiting.get(msg.id);waiting.delete(msg.id);msg.error?reject(Error(JSON.stringify(msg.error))):resolve(msg.result);}
    else if(msg.method==='Runtime.exceptionThrown')exceptions.push(msg.params.exceptionDetails.exception?.description||msg.params.exceptionDetails.text);
    else if(msg.method==='WebAudio.contextCreated'){audioContexts++;audioRan ||= msg.params.context.contextState==='running';}
    else if(msg.method==='WebAudio.contextChanged')audioRan ||= msg.params.context.contextState==='running';
    else if(msg.method==='Runtime.consoleAPICalled'){
      const text=msg.params.args.map(arg=>arg.value||'').join(' ');if(text.startsWith('Quota audio:'))exceptions.push(text);
    }
  };
  const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++serial;waiting.set(id,[resolve,reject]);ws.send(JSON.stringify({id,method,params}));});
  const js=async expression=>{const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true,userGesture:true});if(r.exceptionDetails)throw Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result.value;};
  const check=async(expression,label)=>{if(!await js(expression))throw Error(label);};
  const until=async(expression,label,timeout=20000)=>{const start=Date.now();while(Date.now()-start<timeout){if(await js(expression))return;await sleep(80);}throw Error(label+': '+await js('document.body.innerText.slice(-600)'));};
  const click=async selector=>{await check(`!!document.querySelector(${JSON.stringify(selector)})`,'Missing '+selector);await js(`document.querySelector(${JSON.stringify(selector)}).click()`);};

  await send('Page.enable');await send('Runtime.enable');
  async function seedOldPage() {
    migrated=false;
    await send('Page.navigate',{url:origin+'/quota/'});
    await until('!!document.querySelector("#legacy")','Initial prototype missing');
    await js('navigator.serviceWorker.ready.then(()=>true)');
    await until('!!navigator.serviceWorker.controller','Old worker not controlling');
    await send('Page.reload');
    await until('!!document.querySelector("#legacy")','Cached prototype missing');
    await check('!!navigator.serviceWorker.controller','Reload did not use old worker');
  }
  await seedOldPage();
  migrated=true;
  await js('navigator.serviceWorker.getRegistration().then(r=>r.update()).then(()=>true)');
  await until('!!document.querySelector("#unity-canvas")','Old prototype did not migrate to Unity');
  await check('location.pathname==="/quota/"','Wrong destination');
  await check('caches.has("quota-web-lite-2026-10-20-b").then(x=>!x)','Legacy cache not removed');
  console.log('Initial cache-first prototype migrated automatically without a page reload handler.');
  // Also verify the dedicated recovery entry point outside the stale scope.
  await seedOldPage();
  migrated=true;
  await send('Page.navigate',{url:origin+'/quota-recover.html'});
  await until('!!document.querySelector("#unity-canvas")','Recovery page did not open Unity');
  console.log('Recovery link also cleared the old prototype and opened /quota/.');
} finally {
  ws?.close();chrome.kill();server.closeAllConnections();server.close();
  await sleep(500);rmSync(profile,{recursive:true,force:true});
}
