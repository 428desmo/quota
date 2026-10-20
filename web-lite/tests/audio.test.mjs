import test from 'node:test';
import assert from 'node:assert/strict';
import {Soundscape} from '../audio.mjs';
class FakeContext {
  constructor(){this.currentTime=10;this.state='running';this.destination={};this.sources=[];}
  resume(){this.state='running';return Promise.resolve();}suspend(){this.state='suspended';return Promise.resolve();}
  decodeAudioData(){return Promise.resolve({duration:120});}
  createGain(){return {gain:{value:1,events:[],cancelScheduledValues(){},setValueAtTime(v,t){this.value=v;this.events.push([v,t]);},linearRampToValueAtTime(v,t){this.events.push([v,t]);}},connect(){},disconnect(){}};}
  createBufferSource(){const source={connect(){},disconnect(){},start(...args){this.started=args;},stop(time){this.stopped=time;}};this.sources.push(source);return source;}
}
globalThis.window={AudioContext:FakeContext,addEventListener(){}};
globalThis.document={hidden:false,addEventListener(){}};
globalThis.fetch=async()=>({ok:true,arrayBuffer:async()=>new ArrayBuffer(0)});
test('BGMは16秒以上92秒未満をWeb Audioでループし、設定音量を反映する',async()=>{
  const levels={bgm:3,se:2},audio=new Soundscape(()=>levels);await audio.music('play_bgm_02');
  const handle=audio.channels.get('music');assert.ok(handle.source.loop);assert.equal(handle.source.loopStart,16);assert.equal(handle.source.loopEnd,92);assert.equal(handle.gain.gain.value,.6);
  levels.bgm=1;audio.update();assert.equal(handle.gain.gain.value,.2);audio.stopMusic(.5);assert.equal(handle.source.stopped,10.5);assert.deepEqual(handle.gain.gain.events.at(-1),[0,10.5]);
});
test('カードSEを各0.3秒鳴らし、異なるSEの発音で直前のSEを切らない',async()=>{
  const audio=new Soundscape(()=>({bgm:0,se:4}));await audio.se('card_03');const a=audio.channels.get('se:card_03');await audio.se('card_01');
  assert.equal(a.source.stopped,undefined);assert.deepEqual(a.source.started,[0,0,.3]);assert.equal(a.gain.gain.value,.8);audio.stopAll();assert.equal(audio.context.state,'suspended');
});
test('チップSEは繰り返し、終了時は0.1秒で止める',async()=>{
  const audio=new Soundscape(()=>({bgm:0,se:5}));await audio.count();const handle=audio.channels.get('count');assert.ok(handle.source.loop);audio.stopCount();assert.equal(handle.source.stopped,10.1);
});
test('無音設定では音声の解凍やAudioContextの作成を開始しない',async()=>{
  const audio=new Soundscape(()=>({bgm:0,se:0}));await audio.music('play_bgm_02');await audio.se('card_01');assert.equal(audio.context,null);
});
