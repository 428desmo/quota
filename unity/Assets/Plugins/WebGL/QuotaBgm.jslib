mergeInto(LibraryManager.library, {
  QuotaBgmControl: function (action, urlPtr) {
    var state = window.quotaBgmState || (window.quotaBgmState = {
      generation: 0,
      context: null,
      bufferPromise: null,
      source: null,
      url: null,
      active: false
    });

    if (action === 2) {
      state.generation++;
      state.active = false;
      if (state.source) {
        try { state.source.stop(); } catch (error) { /* already stopped */ }
        state.source.disconnect();
        state.source = null;
      }
      return;
    }

    var url = UTF8ToString(urlPtr);
    if (!state.context) {
      var AudioContextType = window.AudioContext || window.webkitAudioContext;
      if (!AudioContextType) return;
      state.context = new AudioContextType();
      var resume = function () {
        if (state.context.state === "suspended") state.context.resume();
      };
      document.addEventListener("pointerdown", resume);
      document.addEventListener("touchstart", resume);
      document.addEventListener("keydown", resume);
    }
    if (state.url !== url || !state.bufferPromise) {
      state.url = url;
      state.bufferPromise = fetch(url).then(function (response) {
        if (!response.ok) throw new Error("BGM download failed: " + response.status);
        return response.arrayBuffer();
      }).then(function (bytes) {
        return state.context.decodeAudioData(bytes);
      }).catch(function (error) {
        state.bufferPromise = null;
        console.warn("Quota BGM:", error);
        return null;
      });
    }
    if (action === 0) return;

    var generation = ++state.generation;
    state.active = true;
    state.bufferPromise.then(function (buffer) {
      if (!buffer || generation !== state.generation) return;
      return state.context.resume().then(function () {
        if (generation !== state.generation) return;
        var source = state.context.createBufferSource();
        source.buffer = buffer;
        source.loop = true;
        source.loopStart = 16;
        source.loopEnd = 92;
        source.connect(state.context.destination);
        state.source = source;
        source.start(0, 0);
      });
    }).catch(function (error) {
      console.warn("Quota BGM playback:", error);
    });
  }
});
