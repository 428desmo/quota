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
