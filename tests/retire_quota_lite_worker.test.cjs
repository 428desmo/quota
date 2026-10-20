const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

test('retire only the /quota/ lightweight cache and reload only its windows', async () => {
  const handlers = {}, deleted = [], navigated = [];
  let unregistered = false;
  const origin = 'https://quota.example';
  const clients = ['/quota/', '/quota/settings', '/quota2/', '/fragile/'].map(route => ({
    url: origin + route,
    navigate: async url => navigated.push(url)
  }));
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../portal/retire-quota-lite-sw.js'), 'utf8'), {
    URL,
    self: {
      location: { origin },
      addEventListener: (name, callback) => { handlers[name] = callback; },
      skipWaiting: async () => {},
      registration: { unregister: async () => { unregistered = true; } },
      clients: { matchAll: async options => {
        assert.equal(options.includeUncontrolled, true);
        return clients;
      } }
    },
    caches: {
      keys: async () => ['quota-web-lite-/quota/-old', 'quota-web-lite-/quota2/-current', 'unity-offline'],
      delete: async key => deleted.push(key)
    }
  });
  let finished;
  handlers.activate({ waitUntil: promise => { finished = promise; } });
  await finished;
  assert.equal(unregistered, true);
  assert.deepEqual(deleted, ['quota-web-lite-/quota/-old']);
  assert.deepEqual(navigated, [origin + '/quota/', origin + '/quota/settings']);
  assert.equal(handlers.fetch, undefined);
});
