const CACHE = 'quota-web-lite-2026-10-20-b';
const SHELL = ['./', './index.html', './style.css', './app.mjs', './engine.mjs', './cpu.mjs', './assets/goods.json'];
const IMAGES = ['./assets/horizontal_base.jpg'];
const SOUNDS = ['play_bgm_02', 'end_bgm_01', 'end_bgm_03', 'card_03'].map(name => `./assets/sound/${name}.mp3`);
self.addEventListener('install', event => event.waitUntil((async () => {
  const cache = await caches.open(CACHE);
  const response = await fetch('./assets/goods.json', { cache: 'reload' });
  if (!response.ok) throw Error('goods manifest unavailable');
  const goods = await response.json();
  const pictures = [...goods.goods, goods.wild].map(item => `./assets/goods/${item.file}.png`);
  await cache.addAll([...SHELL, ...IMAGES, ...SOUNDS, ...pictures]);
  await self.skipWaiting();
})()));
self.addEventListener('activate', event => event.waitUntil((async () => {
  for (const key of await caches.keys()) if (key.startsWith('quota-web-lite-') && key !== CACHE) await caches.delete(key);
  await self.clients.claim();
})()));
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== location.origin || !url.pathname.startsWith(new URL(self.registration.scope).pathname)) return;
  event.respondWith((async () => {
    const cache = await caches.open(CACHE);
    const cached = await cache.match(event.request);
    if (cached) return cached;
    try {
      const response = await fetch(event.request);
      if (response.ok) cache.put(event.request, response.clone());
      return response;
    } catch (error) {
      if (event.request.mode === 'navigate') return cache.match('./index.html');
      throw error;
    }
  })());
});
