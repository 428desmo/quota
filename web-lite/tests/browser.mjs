// Browser acceptance checks. Run against a static server; no game API required.
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
  await send('Page.enable');await send('Runtime.enable');await send('WebAudio.enable');
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  await send('Page.addScriptToEvaluateOnNewDocument',{source:`window.__quotaTimerScale=1;const originalTimeout=window.setTimeout;window.setTimeout=(fn,delay,...args)=>originalTimeout(fn,delay*window.__quotaTimerScale,...args);`});
  await send('Page.navigate',{url:base});await until('!!document.querySelector("[data-action=lobby]")','Menu missing');
  const soundLevel=process.env.QUOTA_TEST_AUDIO==='1'?1:0;
  await js(`localStorage.setItem('quota.webLite.settings.v1',JSON.stringify({name:'検証プレイヤー',players:3,roundMode:0,strength:2,bgm:${soundLevel},se:${soundLevel},confirm:false}));`);
  await send('Page.reload',{ignoreCache:true});await until('!!document.querySelector("[data-action=lobby]")','Menu reload missing');
  await click('[data-action=how-to]');await until('document.querySelectorAll(".guide-panel").length>4','Guide not rendered');
  await check('document.querySelectorAll(".guide-panel table").length===1','Nested title table missing');
  await click('[data-action=slide-next]');await check('document.querySelector(".slideshow img").src.includes("slide02.jpg")','Slide next failed');
  await click('[data-action=slide-pause]');await click('[data-action=slide-prev]');await check('document.querySelector(".slideshow img").src.includes("slide01.jpg")','Slide previous failed');
  await js('document.querySelector(".shade").click()');await check('!document.querySelector(".dialog")','Outside dismissal failed');
  await click('[data-action=rules]');await until('document.querySelectorAll(".guide-panel table").length===2','Rule tables missing');await click('[data-action=close-modal]');
  await click('[data-action=lobby]');await check('document.body.scrollWidth===innerWidth','Lobby horizontal overflow');
  await click('[data-action=nominate]');for(const id of [5,0,4])await click(`[data-nominee="${id}"]`);await click('[data-action=accept-cast]');
  await check('document.querySelectorAll(".participant")[1].textContent.includes("ソフィア")','Nomination order wrong');
  await click('[data-action=settings]');await check('document.querySelectorAll(".dialog .field").length===3','Personal settings missing');await click('[data-action=close-modal]');
  await js('window.__quotaTimerScale=.05');await click('[data-action=start]');
  await until('!!document.querySelector(".market button[data-card]")','Human turn missing');
  await check('document.body.scrollWidth===innerWidth','Board horizontal overflow');
  let screenshot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});writeFileSync('/tmp/quota2-human-play.png',Buffer.from(screenshot.data,'base64'));
  await click('.market button[data-card]:not(.muted)');await until('document.querySelector(".player.human .fan [data-card-id]")','Card take failed');
  await click('[data-action=settings]');await check('document.querySelectorAll(".dialog .field .segmented").length===3','Playing settings differ');await click('[data-action=close-modal]');
  await click('[data-action=exit]');await until('!!document.querySelector("[data-action=lobby]")','Exit failed');
  await click('[data-action=lobby]');await click('[data-option=players][data-value="4"]');
  await check(`document.querySelector('[data-option="roundMode"][data-value="2"]').textContent==='8'`,'Dynamic round labels wrong');
  await click('[data-option=roundMode][data-value="1"]');
  await send('Emulation.setEmulatedMedia',{features:[{name:'prefers-reduced-motion',value:'reduce'}]});
  await js(`window.__quotaTimerScale=.02;window.__phases=[];new MutationObserver(()=>{const panel=document.querySelector('.ceremony-panel');if(panel){const phase=panel.querySelector('.winner')?'winner':panel.querySelector('.sum')?'equation':panel.querySelector('.previous')?'previous':panel.querySelector('.score-list')?'scores':panel.querySelector('.award-lines')?'titles':'reason';if(!window.__phases.includes(phase))window.__phases.push(phase);}}).observe(document.querySelector('#app'),{childList:true,subtree:true});`);
  await click('[data-action=simulate]');await check('!document.querySelector("[data-action=settings]")','Simulation must not show settings');
  await until('!!document.querySelector(".ceremony-panel .winner")','Simulation did not finish',60000);
  await check('document.querySelectorAll(".tray .chip").length===0','Final trays contain counted chips');
  await check('document.querySelectorAll(".score-row").length===4','Four-player final standings missing');
  await check('window.__phases.includes("reason")&&window.__phases.includes("scores")','Score ceremony skipped');
  await check('window.__phases.includes("previous")&&window.__phases.includes("equation")','Cumulative score ceremony skipped');
  screenshot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});writeFileSync('/tmp/quota2-final-scores.png',Buffer.from(screenshot.data,'base64'));
  await click('[data-action=home]');await until('!!document.querySelector("[data-action=lobby]")','Final return failed');
  await until('!!navigator.serviceWorker.controller','Offline worker missing');
  await js('navigator.serviceWorker.ready');
  await send('Network.enable');await send('Network.emulateNetworkConditions',{offline:true,latency:0,downloadThroughput:0,uploadThroughput:0});
  await send('Page.reload',{ignoreCache:false});await until('!!document.querySelector("[data-action=lobby]")','Offline reload failed');
  await click('[data-action=how-to]');await until('document.querySelectorAll(".guide-panel").length>4','Offline guide missing');
  if(exceptions.length)throw Error(exceptions.join('\n'));
  if(soundLevel&&(!audioContexts||!audioRan))throw Error('Web Audio context did not run');
  console.log('Browser acceptance passed: guides, nomination, human play, settings, 4-player/4-round simulation, cumulative score ceremony, empty trays, offline reload and guides.');
}finally{ws?.close();chrome.kill('SIGTERM');await sleep(300);rmSync(profile,{recursive:true,force:true,maxRetries:5,retryDelay:100});}
