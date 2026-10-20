import test from 'node:test';
import assert from 'node:assert/strict';
import { Game, makeDeck, sequencePoints, titleAwards } from './engine.mjs';
import { chooseCpuAction } from './cpu.mjs';

test('山札は108枚・金貨4枚', () => {
  const deck = makeDeck();
  assert.equal(deck.length, 108);
  assert.equal(deck.filter(card => card.suit === 'J').length, 4);
  assert.equal(new Set(deck.map(card => card.id)).size, 108);
});

test('連続する金貨の紫ボーナスは2枚目から1、2、3点', () => {
  const cards = [{ suit:'S', rank:4 }, ...Array.from({ length:4 }, () => ({ suit:'J', rank:null }))];
  assert.equal(sequencePoints(cards), 6);
});

test('五種の品揃えと他の称号を計算する', () => {
  const player = { achieved: ['S','H','D','C','J'].map(suit => ({ suit })), bundles: [
    { kind:'S', hasWild:false }, { kind:'S', hasWild:false }, { kind:'S', hasWild:false },
  ] };
  assert.deepEqual(titleAwards(player).map(item => item.points).sort((a,b) => a-b), [5,5,15]);
});

test('20試合を最後まで進め、カードを失わない', () => {
  for (let seed = 1; seed <= 20; seed++) {
    const game = new Game({ playerCount: seed % 2 ? 3 : 4, rounds: 1, seed });
    let steps = 0;
    while (game.active() && steps++ < 3000) {
      const action = chooseCpuAction(game, 2, game.current);
      if (action) game.step(action);
    }
    assert.ok(game.finished, `seed ${seed} did not finish`);
    const cards = [...game.deck, ...game.removed, ...game.market.filter(Boolean), ...game.discard,
      ...game.players.flatMap(player => [...(player.quota ? [player.quota] : []), ...player.collection, ...player.achieved])];
    assert.equal(cards.length, 108, `seed ${seed}`);
    assert.equal(new Set(cards.map(card => card.id)).size, 108, `seed ${seed}`);
  }
});

test('複数ラウンドでは前ラウンドの得点を確定して新しい商品を配る', () => {
  const game = new Game({ playerCount:3, rounds:3, seed:42 });
  let steps = 0;
  while (!game.awaitingNextRound && steps++ < 3000) {
    const action = chooseCpuAction(game, 1, game.current);
    if (action) game.step(action);
  }
  assert.ok(game.awaitingNextRound);
  const priorScores = game.players.map(player => player.score);
  game.beginNextRound();
  assert.equal(game.roundIndex, 2);
  assert.equal(game.market.length, 7);
  assert.ok(game.players.every((player, seat) => player.score >= priorScores[seat] && player.achieved.length === 0));
  assert.equal(new Set(game.goods).size, 4);
});

test('収集の途中では補充せず、指定した順に実績へ積む',()=>{
  const game=new Game({seed:9}),cards=makeDeck(),p=game.players[game.current];
  p.quota=cards[2];p.collection=[];game.market=[cards[4],cards[5],cards[52],cards[13],cards[26],cards[39],cards[14]];
  const seat=game.current,deck=game.deck.length;
  game.step({type:'collect',ids:[4]});assert.equal(game.current,seat);assert.equal(game.deck.length,deck);assert.equal(game.market[0],null);
  game.step({type:'collect',ids:[5]});assert.deepEqual(p.achieved.map(c=>c.id),[2,4,5]);assert.equal(p.quota,null);assert.equal(p.score,3);assert.equal(sequencePoints(p.achieved),1);assert.equal(game.deck.length,deck-2);
});
test('ダブルは宣言取消で回数を戻し、1行動後は補充して同じ人の2行動目になる',()=>{
  const game=new Game({seed:1}),cards=makeDeck(),p=game.players[game.current],seat=game.current;
  game.market=[cards[1],cards[2],cards[3],cards[13],cards[26],cards[39],cards[14]];
  game.declareDouble();game.cancelDouble();assert.equal(p.doubleLeft,1);assert.equal(game.plan,'normal');
  game.declareDouble();game.step({type:'take',id:1});assert.equal(game.current,seat);assert.equal(game.doubleStage,2);assert.ok(game.market[0]);assert.throws(()=>game.cancelDouble());
  game.step({type:'collect',ids:[2]});assert.notEqual(game.current,seat);assert.deepEqual(p.achieved.map(c=>c.id),[1,2]);assert.equal(p.doubleLeft,0);
});
test('放棄は手番を終えず、その手番で新しいノルマを選べる',()=>{
  const game=new Game({seed:7}),cards=makeDeck(),p=game.players[game.current],seat=game.current;
  p.quota=cards[12];p.collection=[cards[5]];game.market=[cards[13],null,null,null,null,null,null];
  game.step({type:'abandon'});assert.equal(game.current,seat);assert.deepEqual(game.discard.map(c=>c.id),[12,5]);assert.ok(game.legal({type:'take',id:13}));
  game.step({type:'take',id:13});assert.equal(p.achieved[0].id,13);assert.equal(p.score,1);
});
test('全員のパスが2巡続いたときは配り直し後に膠着終了する',()=>{
  const game=new Game({seed:2});for(let i=0;i<3;i++)game.step({type:'pass'});
  assert.ok(game.stallFlag);assert.equal(game.reshuffleCount,1);assert.equal(game.noGainStreak,0);
  for(let i=0;i<3;i++)game.step({type:'pass'});assert.equal(game.roundEndReason,'STALL');assert.ok(game.awaitingNextRound);
});
