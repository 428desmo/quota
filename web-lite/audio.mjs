// Decoded Web Audio playback keeps the 16..92 second loop sample-continuous.
export class Soundscape {
  constructor(levels) {
    this.levels = levels; this.context = null; this.buffers = new Map(); this.channels = new Map(); this.tickets = new Map(); this.wanted = null;
    const resume = () => { if (!document.hidden && (this.wanted && this.levels().bgm || this.channels.size)) this.unlock(); };
    document.addEventListener('pointerdown', resume, { passive: true });
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) this.context?.suspend().catch(() => {});
      else if (this.context && (this.wanted || this.channels.size)) this.context.suspend().catch(() => {}).then(resume);
    });
    window.addEventListener('pageshow', resume);
    window.addEventListener('focus', resume);
  }
  unlock() {
    const Type = window.AudioContext || window.webkitAudioContext;
    if (!Type) return;
    if (!this.context) this.context = new Type();
    if (this.context.state !== 'running') this.context.resume().catch(() => {});
  }
  async buffer(name) {
    if (!this.buffers.has(name)) this.buffers.set(name, fetch(`./assets/sound/${name}.mp3`)
      .then(response => { if (!response.ok) throw Error('音声を読み込めません'); return response.arrayBuffer(); })
      .then(data => this.context.decodeAudioData(data)).catch(error => { this.buffers.delete(name); throw error; }));
    return this.buffers.get(name);
  }
  ticket(channel) { const next = (this.tickets.get(channel) || 0) + 1; this.tickets.set(channel, next); return next; }
  async start(channel, name, { loop = false, duration, music = false } = {}) {
    this.stop(channel); const ticket = this.ticket(channel);
    if (music) this.wanted = { name, loop };
    const level = this.levels()[music ? 'bgm' : 'se'];
    if (!level) return;
    this.unlock(); if (!this.context) return;
    try {
      const buffer = await this.buffer(name);
      if (this.tickets.get(channel) !== ticket) return;
      const source = this.context.createBufferSource(), gain = this.context.createGain();
      source.buffer = buffer; source.loop = loop;
      if (name === 'play_bgm_02') { source.loopStart = 16; source.loopEnd = 92; }
      gain.gain.value = this.levels()[music ? 'bgm' : 'se'] / 5;
      source.connect(gain); gain.connect(this.context.destination);
      const handle = { source, gain, music };
      this.channels.set(channel, handle);
      source.onended = () => {
        source.disconnect(); gain.disconnect();
        if (this.channels.get(channel) === handle) { this.channels.delete(channel); if (music) this.wanted = null; }
        if (!this.channels.size && !(this.wanted && this.levels().bgm)) this.context.suspend().catch(() => {});
      };
      if (duration) source.start(0, 0, Math.min(duration, buffer.duration)); else source.start();
    } catch (error) { console.warn('Quota audio:', error.message); }
  }
  stop(channel, fade = 0) {
    this.ticket(channel);
    const handle = this.channels.get(channel);
    if (!handle) return;
    this.channels.delete(channel);
    const now = this.context.currentTime;
    handle.gain.gain.cancelScheduledValues(now);
    handle.gain.gain.setValueAtTime(handle.gain.gain.value, now);
    if (fade) handle.gain.gain.linearRampToValueAtTime(0, now + fade);
    try { handle.source.stop(now + fade); } catch { /* source already ended */ }
  }
  music(name) {
    this.stop('music'); this.wanted = { name, loop: name === 'play_bgm_02' };
    return this.start('music', name, { music: true, loop: this.wanted.loop });
  }
  stopMusic(fade = 0) { this.wanted = null; this.stop('music', fade); }
  se(name) { return this.start('se:' + name, name, { duration: .3 }); }
  count() { return this.start('count', 'chip_counting_02', { loop: true }); }
  stopCount() { this.stop('count', .1); }
  update() {
    for (const handle of this.channels.values()) {
      handle.gain.gain.cancelScheduledValues(this.context.currentTime);
      handle.gain.gain.setValueAtTime(this.levels()[handle.music ? 'bgm' : 'se'] / 5, this.context.currentTime);
    }
    if (!this.levels().bgm) this.stop('music');
    else if (!this.channels.has('music') && this.wanted) this.start('music', this.wanted.name, { music: true, loop: this.wanted.loop });
  }
  stopAll() {
    this.stopMusic();
    for (const key of this.tickets.keys()) this.stop(key);
    this.context?.suspend().catch(() => {});
  }
}
