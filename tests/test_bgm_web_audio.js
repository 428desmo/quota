const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const plugin = fs.readFileSync(path.join(__dirname, '../unity/Assets/Plugins/WebGL/QuotaBgm.jslib'), 'utf8');

function harness(fetchImpl) {
  const library = {};
  const sources = [];
  const listeners = { window: {}, document: {} };
  const context = {
    state: 'running',
    destination: {},
    resumeCalls: 0,
    suspendCalls: 0,
    resume() { this.resumeCalls++; this.state = 'running'; return Promise.resolve(); },
    suspend() { this.suspendCalls++; this.state = 'suspended'; return Promise.resolve(); },
    decodeAudioData(bytes) { return Promise.resolve({ bytes }); },
    createGain() { return { gain: { value: 1 }, connect() {} }; },
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
  const window = {
    AudioContext: function () { return context; },
    addEventListener(name, callback) { (listeners.window[name] ||= []).push(callback); },
  };
  const document = {
    hidden: false,
    addEventListener(name, callback) { (listeners.document[name] ||= []).push(callback); },
  };
  vm.runInNewContext(plugin, {
    LibraryManager: { library },
    mergeInto(target, members) { Object.assign(target, members); },
    UTF8ToString(value) { return value; },
    window,
    document,
    fetch: fetchImpl,
    console,
  });
  return {
    control: library.QuotaBgmControl, sources, window, document, context,
    emit(target, name) { for (const callback of listeners[target][name] || []) callback(); },
  };
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
  h.control(0, '/StreamingAssets/play_bgm_02.mp3', 1);
  h.control(1, '/StreamingAssets/play_bgm_02.mp3', 1);
  await settle();
  assert.deepEqual(urls, ['/StreamingAssets/play_bgm_02.mp3']);
  assert.equal(h.sources.length, 1);
  assert.equal(h.sources[0].loop, true);
  assert.equal(h.sources[0].loopStart, 16);
  assert.equal(h.sources[0].loopEnd, 92);
  assert.deepEqual(h.sources[0].started, [0, 0]);
  h.control(2, '', 0);
  assert.equal(h.sources[0].stopped, true);
  assert.equal(h.sources[0].disconnected, true);
});

test('volume updates the playing audio without restarting its loop', async () => {
  const h = harness(async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(4) }));
  h.control(1, '/StreamingAssets/play_bgm_02.mp3', 0.6);
  await settle();
  assert.equal(h.window.quotaBgmState.gainNode.gain.value, 0.6);
  h.control(3, '', 0.2);
  assert.equal(h.window.quotaBgmState.gainNode.gain.value, 0.2);
  assert.equal(h.sources.length, 1);
});

test('stopping before the audio downloads prevents late playback', async () => {
  let finish;
  const h = harness(() => new Promise(resolve => { finish = resolve; }));
  h.control(1, '/StreamingAssets/play_bgm_02.mp3', 1);
  h.control(2, '', 0);
  finish({ ok: true, arrayBuffer: async () => new ArrayBuffer(4) });
  await settle();
  assert.equal(h.sources.length, 0);
});

test('returning from another app resumes an interrupted Safari audio context', async () => {
  const h = harness(async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(4) }));
  h.control(1, '/StreamingAssets/play_bgm_02.mp3', 1);
  await settle();
  h.document.hidden = true;
  h.context.state = 'interrupted';
  h.emit('document', 'visibilitychange');
  assert.equal(h.context.resumeCalls, 1);
  h.document.hidden = false;
  h.emit('document', 'visibilitychange');
  await settle();
  assert.equal(h.context.resumeCalls, 2);
  assert.equal(h.context.state, 'running');
  assert.equal(h.sources.length, 1);
});

test('returning also refreshes a context that reports running but is silent', async () => {
  const h = harness(async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(4) }));
  h.control(1, '/StreamingAssets/play_bgm_02.mp3', 1);
  await settle();
  h.emit('window', 'pageshow');
  await settle();
  assert.equal(h.context.suspendCalls, 1);
  assert.equal(h.context.resumeCalls, 2);
  assert.equal(h.sources.length, 1);
  h.control(2, '', 0);
  h.context.state = 'interrupted';
  h.emit('document', 'pointerdown');
  assert.equal(h.context.resumeCalls, 2);
});
