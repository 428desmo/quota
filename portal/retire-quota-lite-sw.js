// /quota/ now serves Unity. Retire only the old lightweight worker and its cache.
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil((async () => {
  for (const key of await caches.keys()) {
    if (key.startsWith('quota-web-lite-/quota/-')) await caches.delete(key);
  }
  await self.registration.unregister();
  for (const client of await self.clients.matchAll({type: 'window', includeUncontrolled: true})) {
    const url = new URL(client.url);
    if (url.origin === self.location.origin && url.pathname.startsWith('/quota/')) {
      await client.navigate(client.url);
    }
  }
})()));
