import test from 'node:test';
import assert from 'node:assert/strict';
import { Game, makeDeck, rng, shuffle } from '../engine.mjs';
import { ProfileMind, bindCpuProfiles, chooseCpuAction } from '../cpu.mjs';
import { titleStates, roundBreakdown, scoreRanks, winnerText, guideHtml } from '../presentation.mjs';

test('Python/Unityの乱数列を再現する',()=>{
  const r=rng(42);assert.deepEqual(Array.from({length:8},()=>r.randBelow(1000)),[654,114,25,759,281,250,228,142]);
});
test('得点は同点の人数だけ次の順位を飛ばす',()=>{
  assert.deepEqual(scoreRanks([35,26,26]).map(r=>r.rank),[1,2,2]);
  assert.deepEqual(scoreRanks([35,35,26]).map(r=>r.rank),[1,1,3]);
  const rows=scoreRanks([26,26,26]);assert.deepEqual(rows.map(r=>r.rank),[1,1,1]);
  assert.equal(winnerText([{name:'A'},{name:'B'},{name:'C'}],rows),'今回は、Aさん、Bさん、Cさんの同点優勝です。おめでとうございます。');
  assert.deepEqual(new Game({seed:1}).standings().map(row=>row.rank),[1,1,1]);
});
test('称号は途中の可能性と終了時の確定を区別する',()=>{
  const p={achieved:[],bundles:[]};assert.ok(titleStates(p).every(t=>!t.impossible&&!t.earned));
  assert.ok(titleStates(p,true).every(t=>t.impossible));
  p.achieved=['S','H','D','C','J'].map(suit=>({suit}));
  p.bundles=[{kind:'S',hasWild:false},{kind:'H',hasWild:true}];
  const states=titleStates(p);assert.ok(states[0].impossible&&states[1].impossible&&states[2].earned);
});
test('前ラウンドの得点を当ラウンドの実績やチップに重複計上しない',()=>{
  const p={score:39,achieved:Array.from({length:9},()=>({suit:'S',rank:7})),bundles:[{kind:'S',hasWild:false}]};
  const b=roundBreakdown(p,29);assert.deepEqual(b,{base:9,green:1,purple:16,blue:0,previous:29,total:26});
});
function contested(){
  const g=new Game({seed:0}),cards=makeDeck();const pick=(suit,rank)=>cards.find(c=>c.suit===suit&&c.rank===rank);
  const own=pick('S',13),rival=pick('S',12),matching=pick('S',5),alternative=pick('D',4);
  const used=[own,rival,matching,alternative];const remaining=shuffle(cards.filter(c=>!used.includes(c)),rng(0));
  g.current=0;g.market=[matching,alternative,null,null,null,null,null];g.deck=remaining.slice(61,96);g.discard=remaining.slice(0,61);
  g.players[0].quota=own;g.players[1].quota=rival;
  for(const p of g.players){p.doubleLeft=0;p.reshuffleLeft=0;}
  return g;
}
test('中と強は商品の競合を考慮し、取れる札があっても乗り換えるのは強のみ',()=>{
  for(const strength of [1,2]){const g=contested(),mind=new ProfileMind(3,strength);const contestedChance=mind.completion(g,'S',12);g.players[1].quota=null;assert.ok(contestedChance<mind.completion(g,'S',12));}
  for(const id of [3,4]){assert.equal(new ProfileMind(id,2).choose(contested()).type,'abandon');assert.equal(new ProfileMind(id,1).choose(contested()).type,'collect');}
});
test('CPUは伏せられた山札の並びや抜かれた8枚を読まない',()=>{
  const a=contested(),b=contested();b.deck.reverse();b.removed.reverse();
  assert.deepEqual(new ProfileMind(4,2).choose(a),new ProfileMind(4,2).choose(b));
});
test('強の配り直しの記憶は次のラウンドへ持ち越さない',()=>{
  const g=new Game({seed:42,rounds:3}),profiles=bindCpuProfiles(g,[0,1,2],2,true);
  g.declareReshuffle();assert.ok([...profiles.values()].some(m=>m.returned.size));
  let guard=0;while(g.active()&&guard++<3000)g.step(chooseCpuAction(g,2,g.current));
  assert.ok(g.awaitingNextRound);g.beginNextRound();assert.ok([...profiles.values()].every(m=>m.returned.size===0));
});
test('説明の表を説明パネル内に含め、ハイフンだけを分離しない',()=>{
  const html=guideHtml('**ルールブック**\n【得点】\n- 達成ボーナス。\n（6枚以下はボーナスなし）\n| 枚数 | 得点 |\n|---|---|\n|7枚|+1|\n- 称号ボーナス。\n  - 単色達成: 15点');
  assert.ok(html.indexOf('<table>')<html.indexOf('</div></section>'));assert.ok(html.includes('6枚以下'));assert.ok(!html.includes('ルールブック'));assert.equal((html.match(/<table>/g)||[]).length,2);
});
