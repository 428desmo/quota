// /quota/ now serves Unity. Retire only the old lightweight worker and its cache.
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil((async () => {
  // Replacing the registration alone does not take control away from an old
  // worker in an already open tab. Claim it before navigating that tab.
  await self.clients.claim();
  for (const key of await caches.keys()) {
    if (!key.startsWith('quota-web-lite-')) continue;
    const cache = await caches.open(key);
    // The first prototype used an unscoped cache name. Remove only /quota/
    // entries, preserving anything belonging to /quota2/ or other games.
    for (const request of await cache.keys()) {
      const url = new URL(request.url);
      if (url.origin === self.location.origin && url.pathname.startsWith('/quota/')) {
        await cache.delete(request);
      }
    }
    if (!(await cache.keys()).length) await caches.delete(key);
  }
  await self.registration.unregister();
  for (const client of await self.clients.matchAll({type: 'window', includeUncontrolled: true})) {
    const url = new URL(client.url);
    if (url.origin === self.location.origin && url.pathname.startsWith('/quota/')) {
      // Bypass both cached navigation responses and an old HTML HTTP cache.
      await client.navigate('/quota/?client=webgl');
    }
  }
})()));
