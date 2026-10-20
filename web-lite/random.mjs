// Integer-seeded MT19937 and rejection sampling, matching Unity's PythonRandom.
export class PythonRandom {
  constructor(seed) {
    this.mt = new Uint32Array(624);
    this.mt[0] = 19650218;
    for (let i = 1; i < 624; i++) this.mt[i] = (Math.imul(1812433253, this.mt[i - 1] ^ (this.mt[i - 1] >>> 30)) + i) >>> 0;
    let i = 1;
    for (let k = 624; k; k--) {
      this.mt[i] = ((this.mt[i] ^ Math.imul(this.mt[i - 1] ^ (this.mt[i - 1] >>> 30), 1664525)) + (seed >>> 0)) >>> 0;
      if (++i >= 624) { this.mt[0] = this.mt[623]; i = 1; }
    }
    for (let k = 623; k; k--) {
      this.mt[i] = ((this.mt[i] ^ Math.imul(this.mt[i - 1] ^ (this.mt[i - 1] >>> 30), 1566083941)) - i) >>> 0;
      if (++i >= 624) { this.mt[0] = this.mt[623]; i = 1; }
    }
    this.mt[0] = 0x80000000; this.index = 624;
  }
  next() {
    if (this.index >= 624) {
      for (let i = 0; i < 624; i++) {
        const y = (this.mt[i] & 0x80000000) | (this.mt[(i + 1) % 624] & 0x7fffffff);
        this.mt[i] = this.mt[(i + 397) % 624] ^ (y >>> 1) ^ (y & 1 ? 0x9908b0df : 0);
      }
      this.index = 0;
    }
    let y = this.mt[this.index++];
    y ^= y >>> 11; y ^= (y << 7) & 0x9d2c5680; y ^= (y << 15) & 0xefc60000; y ^= y >>> 18;
    return y >>> 0;
  }
  randBelow(n) {
    if (!Number.isInteger(n) || n < 1) throw Error('randBelow requires a positive integer');
    const bits = 32 - Math.clz32(n);
    let value;
    do { value = this.next() >>> (32 - bits); } while (value >= n);
    return value;
  }
}
