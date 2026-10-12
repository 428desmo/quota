const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const workerFile = path.join(__dirname, '../unity/Assets/WebGLTemplates/Quota/offline-sw.js');

test('cached game assets and start page still load without a network', async () => {
  const handlers = {};
  const entries = new Map();
  let online = true;
  const scope = 'https://quota.example/';
  const key = request => new URL(typeof request === 'string' ? request : request.url, scope).href;
  const cache = {
    async match(request) { const response = entries.get(key(request)); return response && response.clone(); },
    async put(request, response) { entries.set(key(request), response.clone()); }
  };
  const worker = {
    registration: { scope },
    addEventListener(name, handler) { handlers[name] = handler; },
    skipWaiting: async () => {},
    clients: { claim: async () => {} }
  };
  vm.runInNewContext(fs.readFileSync(workerFile, 'utf8'), {
    self: worker, URL, Response, AbortController, setTimeout, clearTimeout,
    caches: { open: async () => cache },
    fetch: async request => {
      if (!online) throw new Error('offline');
      return new Response(key(request));
    }
  });

  let extended;
  handlers.install({ waitUntil(promise) { extended = promise; } });
  await extended;

  async function request(url, mode) {
    let response;
    const waits = [];
    handlers.fetch({
      request: { method: 'GET', url: scope + url, mode, headers: new Headers() },
      respondWith(promise) { response = promise; },
      waitUntil(promise) { waits.push(promise); }
    });
    const result = await response;
    await Promise.all(waits);
    return result.text();
  }

  assert.equal(await request('Build/game.data', 'cors'), scope + 'Build/game.data');
  assert.equal(await request('StreamingAssets/title1.png', 'cors'), scope + 'StreamingAssets/title1.png');
  online = false;
  assert.equal(await request('Build/game.data', 'cors'), scope + 'Build/game.data');
  assert.equal(await request('StreamingAssets/title1.png', 'cors'), scope + 'StreamingAssets/title1.png');
  assert.equal(await request('', 'navigate'), scope);
});
