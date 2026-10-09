const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const plugin = fs.readFileSync(path.join(__dirname, '../unity/Assets/Plugins/WebGL/QuotaBgm.jslib'), 'utf8');

function harness(fetchImpl) {
  const library = {};
  const sources = [];
  const context = {
    state: 'running',
    destination: {},
    resume() { return Promise.resolve(); },
    decodeAudioData(bytes) { return Promise.resolve({ bytes }); },
    createBufferSource() {
      const source = {
        connect() {},
        disconnect() { this.disconnected = true; },
        start(...args) { this.started = args; },
        stop() { this.stopped = true; },
      };
      sources.push(source);
      return source;
    },
  };
  const window = { AudioContext: function () { return context; } };
  vm.runInNewContext(plugin, {
    LibraryManager: { library },
    mergeInto(target, members) { Object.assign(target, members); },
    UTF8ToString(value) { return value; },
    window,
    document: { addEventListener() {} },
    fetch: fetchImpl,
    console,
  });
  return { control: library.QuotaBgmControl, sources, window };
}

async function settle() {
  for (let i = 0; i < 8; i++) await Promise.resolve();
}

test('the original audio buffer loops directly from 92 seconds back to 16', async () => {
  const urls = [];
  const h = harness(async (url) => {
    urls.push(url);
    return { ok: true, arrayBuffer: async () => new ArrayBuffer(4) };
  });
  h.control(0, '/StreamingAssets/play_bgm_01.mp3');
  h.control(1, '/StreamingAssets/play_bgm_01.mp3');
  await settle();
  assert.deepEqual(urls, ['/StreamingAssets/play_bgm_01.mp3']);
  assert.equal(h.sources.length, 1);
  assert.equal(h.sources[0].loop, true);
  assert.equal(h.sources[0].loopStart, 16);
  assert.equal(h.sources[0].loopEnd, 92);
  assert.deepEqual(h.sources[0].started, [0, 0]);
  h.control(2, '');
  assert.equal(h.sources[0].stopped, true);
  assert.equal(h.sources[0].disconnected, true);
});

test('stopping before the audio downloads prevents late playback', async () => {
  let finish;
  const h = harness(() => new Promise(resolve => { finish = resolve; }));
  h.control(1, '/StreamingAssets/play_bgm_01.mp3');
  h.control(2, '');
  finish({ ok: true, arrayBuffer: async () => new ArrayBuffer(4) });
  await settle();
  assert.equal(h.sources.length, 0);
});
