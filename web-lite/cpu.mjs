import { achievementBonus, finalScore, sequenceAt } from './engine.mjs';

function visible(game) {
  return [...game.market.filter(Boolean), ...game.discard, ...game.players.flatMap(player => [
    ...(player.quota ? [player.quota] : []), ...player.collection, ...player.achieved,
  ])];
}
function expected(game, suit, strength) {
  const total = suit === 'J' ? 4 : 26;
  if (strength === 0) return game.deck.length * total / 108;
  const left = Math.max(0, total - visible(game).filter(card => card.suit === suit).length);
  return Math.min(game.deck.length, left * game.deck.length / (game.deck.length + 8));
}
function completion(game, suit, need, strength, excludedId = -1) {
  if (need <= 0) return 1;
  let market = game.market.filter(card => card && card.id !== excludedId && (card.suit === suit || card.suit === 'J')).length;
  let future = expected(game, suit, strength) + expected(game, 'J', strength);
  if (strength > 0) {
    const rivals = game.players.filter((player, seat) => seat !== game.current && player.quota?.suit === suit);
    const pressure = rivals.reduce((sum, player) => sum + Math.max(0, player.quota.rank - 1 - player.collection.length), 0);
    if (excludedId >= 0) market = Math.max(0, market - pressure);
    future /= 1 + Math.min(2, pressure / Math.max(1, need));
  }
  return Math.pow(Math.min(1, (market + future * (game.playerCount === 3 ? .32 : .27)) / need), 1.7);
}

function quotaValue(game, card, profile, strength) {
  const player = game.players[game.current];
  if (card.rank === 1) return 1.6 + (profile === 0 ? .6 : 0);
  const chance = completion(game, card.suit, card.rank - 1, strength, card.id);
  let value = (card.rank + achievementBonus(card.rank)) * chance;
  if (profile === 0) value *= card.rank >= 8 ? .75 : 1.18;
  if (profile === 2) value *= card.rank <= 7 ? 1.2 : 1;
  if (profile === 3) value *= card.rank >= 10 ? 1.25 : .9;
  if (profile === 5 && player.bundles.some(bundle => bundle.kind === card.suit)) value += 1.8 * chance;
  if (profile === 5 && !player.achieved.some(item => item.suit === card.suit)) value += .7 * chance;
  return value;
}

function orderedCards(game, eligible, profile) {
  const player = game.players[game.current];
  const line = [...player.achieved, ...(player.quota ? [player.quota] : []), ...player.collection];
  const remaining = [...eligible], ordered = [];
  while (remaining.length) {
    remaining.sort((a, b) => {
      const score = card => {
        const sequence = sequenceAt([...line, card], line.length);
        const denial = game.players.some((rival, seat) => seat !== game.current && rival.quota?.suit === card.suit &&
          rival.quota.rank - 1 - rival.collection.length === 1) ? (profile === 4 ? 3 : .3) : 0;
        return sequence + denial + (card.suit === 'J' ? -.1 : .05);
      };
      return score(b) - score(a) || a.id - b.id;
    });
    const card = remaining.shift(); ordered.push(card); line.push(card);
  }
  return ordered;
}

export function chooseCpuAction(game, strength = 1, profile = 1) {
  const player = game.players[game.current];
  if (game.canDeclare()) {
    const helpful = player.quota ? game.eligible() : game.market.filter(card => card && card.suit !== 'J');
    if (!helpful.length && player.reshuffleLeft && game.deck.length > 0) { game.declareReshuffle(); return null; }
    if (player.doubleLeft && helpful.length > 0 && (
      player.quota ? player.quota.rank - 1 - player.collection.length <= helpful.length + 1 :
        helpful.some(card => card.rank === 1 || (card.rank <= 5 && card.rank >= 3))
    ) && (profile === 2 || profile === 3 || game.deck.length < 25)) { game.declareDouble(); return null; }
  }
  if (!player.quota) {
    const choices = game.market.filter(card => card && card.suit !== 'J');
    if (!choices.length) return { type: 'pass' };
    choices.sort((a, b) => quotaValue(game, b, profile, strength) - quotaValue(game, a, profile, strength) || a.id - b.id);
    return { type: 'take', id: choices[0].id };
  }
  const need = player.quota.rank - 1 - player.collection.length;
  const eligible = game.eligible();
  if (strength === 2 && eligible.length < need && player.quota.rank >= 8 &&
    completion(game, player.quota.suit, need, strength) < .27 && game.deck.length < 50 &&
    game.market.some(card => card && card.suit !== player.quota.suit && card.suit !== 'J' && quotaValue(game, card, profile, strength) > 2.3))
    return { type: 'abandon' };
  if (eligible.length) return { type: 'collect', ids: orderedCards(game, eligible, profile).slice(0, need).map(card => card.id) };
  const left = expected(game, player.quota.suit, strength) + expected(game, 'J', strength);
  if (need > left + 2 && game.deck.length < 30) return { type: 'abandon' };
  if (strength === 2 && profile === 3 && finalScore(player) + 12 < Math.max(...game.players.map(finalScore)) && need >= 7)
    return { type: 'abandon' };
  return { type: 'pass' };
}

export function randomCpuIds(count, random = Math.random) {
  const ids = [0, 1, 2, 3, 4, 5];
  for (let i = ids.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1)); [ids[i], ids[j]] = [ids[j], ids[i]];
  }
  return ids.slice(0, count);
}
