const PREFIX = `quota-web-lite-${new URL(self.registration.scope).pathname}-`;
const CACHE = PREFIX + '2026-10-20-parity-1';
const SHELL = ['./','./index.html','./style.css','./app.mjs','./engine.mjs','./random.mjs','./cpu.mjs','./audio.mjs','./presentation.mjs','./assets/goods.json'];
const IMAGES = ['horizontal_base.jpg','vertical_base.jpg','title1.png','title2.png','card_back.png'].map(name=>'./assets/'+name);
const SOUNDS = ['play_bgm_02','end_bgm_01','end_bgm_03','card_01','card_02','card_03','chip_counting_02'].map(name=>`./assets/sound/${name}.mp3`);
const GUIDES = ['./assets/copy/how-to-play.txt','./assets/copy/detailed-rules.txt',...Array.from({length:9},(_,i)=>`./assets/slides/slide${String(i+1).padStart(2,'0')}.jpg`)];
self.addEventListener('install',event=>event.waitUntil((async()=>{
  const cache=await caches.open(CACHE);
  const response=await fetch('./assets/goods.json',{cache:'reload'});
  if(!response.ok)throw Error('goods manifest unavailable');
  const goods=await response.json();
  const pictures=[...goods.goods,goods.wild].map(item=>`./assets/goods/${item.file}.png`);
  await cache.addAll([...SHELL,...IMAGES,...SOUNDS,...GUIDES,...pictures]);
  await self.skipWaiting();
})()));
self.addEventListener('activate',event=>event.waitUntil((async()=>{
  for(const key of await caches.keys())if(key.startsWith(PREFIX)&&key!==CACHE)await caches.delete(key);
  await self.clients.claim();
})()));
self.addEventListener('fetch',event=>{
  const url=new URL(event.request.url);
  if(event.request.method!=='GET'||url.origin!==location.origin||!url.pathname.startsWith(new URL(self.registration.scope).pathname))return;
  event.respondWith((async()=>{
    const cache=await caches.open(CACHE),cached=await cache.match(event.request);
    if(cached)return cached;
    try{const response=await fetch(event.request);if(response.ok)await cache.put(event.request,response.clone());return response;}
    catch(error){if(event.request.mode==='navigate')return cache.match('./index.html');throw error;}
  })());
});
