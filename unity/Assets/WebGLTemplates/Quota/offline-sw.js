const CACHE_NAME = "quota-offline-v1";
const ROOT = new URL(self.registration.scope);
const HOME = new URL("./", ROOT).href;

self.addEventListener("install", event => {
  event.waitUntil((async () => {
    const cache = await caches.open(CACHE_NAME);
    try {
      const response = await fetch(HOME, { cache: "no-store" });
      if (response.ok) await cache.put(HOME, response);
    } catch (_) {
      // An already installed worker can keep serving its cached page.
    }
    await self.skipWaiting();
  })());
});

self.addEventListener("activate", event => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("fetch", event => {
  const request = event.request;
  if (request.method !== "GET" || request.headers.has("Range")) return;
  const url = new URL(request.url);
  if (url.origin !== ROOT.origin || url.pathname.startsWith("/api/")) return;

  const path = url.pathname.slice(ROOT.pathname.length);
  const navigation = request.mode === "navigate" && (path === "" || path === "index.html");
  const build = path.startsWith("Build/");
  const staticFile = build || path.startsWith("StreamingAssets/") ||
    path.startsWith("TemplateData/") || path === "offline-manifest.json";
  if (!navigation && !staticFile) return;

  event.respondWith((async () => {
    const cache = await caches.open(CACHE_NAME);
    const key = navigation ? HOME : request;
    const cached = await cache.match(key);
    if (build && cached) return cached;
    if (cached && path !== "offline-manifest.json" && !navigation) {
      event.waitUntil(fetch(request).then(response => {
        if (response.ok && response.status === 200) return cache.put(key, response);
      }).catch(() => {}));
      return cached;
    }
    try {
      let response;
      if (cached) {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 4000);
        try { response = await fetch(request, { signal: controller.signal }); }
        finally { clearTimeout(timeout); }
      } else {
        response = await fetch(request);
      }
      if (response.ok && response.status === 200) {
        event.waitUntil(cache.put(key, response.clone()).catch(() => {}));
      }
      return response;
    } catch (error) {
      if (cached) return cached;
      throw error;
    }
  })());
});
