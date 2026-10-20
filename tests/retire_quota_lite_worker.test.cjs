const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

test('retire only the /quota/ lightweight cache and reload only its windows', async () => {
  const handlers = {}, deleted = [], navigated = [];
  let unregistered = false, claimed = false;
  const entries = new Map([
    ['quota-web-lite-/quota/-old', [originPlaceholder('/quota/')]],
    ['quota-web-lite-2026-10-20-b', [originPlaceholder('/quota/index.html'), originPlaceholder('/quota2/')]],
    ['quota-web-lite-/quota2/-current', [originPlaceholder('/quota2/')]],
    ['unity-offline', [originPlaceholder('/quota/')]]
  ]);
  function originPlaceholder(route) { return {url: 'https://quota.example' + route}; }
  const origin = 'https://quota.example';
  const clients = ['/quota/', '/quota/settings', '/quota2/', '/fragile/'].map(route => ({
    url: origin + route,
    navigate: async url => { assert.equal(claimed, true); navigated.push(url); }
  }));
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../portal/retire-quota-lite-sw.js'), 'utf8'), {
    URL,
    self: {
      location: { origin },
      addEventListener: (name, callback) => { handlers[name] = callback; },
      skipWaiting: async () => {},
      registration: { unregister: async () => { unregistered = true; } },
      clients: { claim: async () => { claimed = true; }, matchAll: async options => {
        assert.equal(options.includeUncontrolled, true);
        return clients;
      } }
    },
    caches: {
      keys: async () => [...entries.keys()],
      open: async key => ({
        keys: async () => entries.get(key),
        delete: async request => { entries.set(key, entries.get(key).filter(item => item !== request)); }
      }),
      delete: async key => { deleted.push(key); entries.delete(key); }
    }
  });
  let finished;
  handlers.activate({ waitUntil: promise => { finished = promise; } });
  await finished;
  assert.equal(unregistered, true);
  assert.deepEqual(deleted, ['quota-web-lite-/quota/-old']);
  assert.deepEqual(navigated, ['/quota/?client=webgl', '/quota/?client=webgl']);
  assert.equal(entries.get('quota-web-lite-2026-10-20-b').length, 1);
  assert.equal(entries.get('quota-web-lite-2026-10-20-b')[0].url, origin + '/quota2/');
  assert.equal(entries.get('unity-offline').length, 1);
  assert.equal(handlers.fetch, undefined);
});
