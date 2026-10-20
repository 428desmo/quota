// Port of Unity CpuProfiles.ProfileMind. Only public cards/counts are inspected.
import { SUITS, achievementBonus, finalScore, sequenceAt } from './engine.mjs';
const scoreFor = rank => rank + achievementBonus(rank);
export const CPU_DESCRIPTIONS = [
  '完成の確実さを重視し、大きすぎるノルマを避ける慎重派。',
  '得点・完成見込み・相手との競合を見て柔軟に選ぶバランス派。',
  '短い手数で達成を重ね、効率よく得点する実務派。',
  '大きなノルマの上積みを狙い、劣勢では逆転に賭ける勝負派。',
  '相手が必要とする札にも目を配り、競合や妨害を織り込む競争派。',
  '単色達成や品揃えなどの称号を意識して集める収集派。',
];
const minds = new WeakMap();
export class ProfileMind {
  constructor(id, strength) { this.id = id; this.strength = Math.max(0, Math.min(2, strength)); this.returned = new Map(); }
  known(suit) { return this.returned.get(suit) || 0; }
  observe(game, beforeMarket, beforeDeck, reshuffled, newRound) {
    if (newRound) { this.returned.clear(); return; }
    if (this.strength < 2) return;
    if (reshuffled) {
      for (const card of beforeMarket.filter(Boolean)) this.returned.set(card.suit, this.known(card.suit) + 1);
      const pool = beforeDeck + beforeMarket.filter(Boolean).length;
      const survival = pool > 0 ? Math.max(0, 1 - game.market.filter(Boolean).length / pool) : 0;
      for (const [suit, count] of this.returned) this.returned.set(suit, count * survival);
    } else if (beforeDeck > game.deck.length && beforeDeck > 0) {
      const survival = Math.max(0, 1 - (beforeDeck - game.deck.length) / beforeDeck);
      for (const [suit, count] of this.returned) this.returned.set(suit, count * survival);
    }
  }
  visible(game) {
    return [...game.market.filter(Boolean), ...game.discard, ...game.players.flatMap(player => [
      ...(player.quota ? [player.quota] : []), ...player.collection, ...player.achieved,
    ])];
  }
  expected(game, suit) {
    const deck = game.deck.length, total = suit === 'J' ? 4 : 26;
    if (!deck) return 0;
    if (this.strength === 0) return deck * total / 108;
    const visible = this.visible(game), count = s => visible.filter(card => card.suit === s).length;
    if (this.strength === 1) return Math.min(deck, Math.max(0, total - count(suit)) * deck / (deck + 8));
    const suits = [...SUITS, 'J'];
    const knownTotal = suits.reduce((sum, s) => sum + Math.min(deck, this.known(s)), 0);
    const unknown = Math.max(0, total - count(suit) - this.known(suit));
    const unknownTotal = suits.reduce((sum, s) => sum + Math.max(0, (s === 'J' ? 4 : 26) - count(s) - this.known(s)), 0);
    return Math.min(deck, this.known(suit)) + (unknownTotal > 0 ? Math.max(0, deck - knownTotal) * unknown / unknownTotal : 0);
  }
  completion(game, suit, need, excludedId = -1) {
    if (need <= 0) return 1;
    let market = game.market.filter(card => card && card.id !== excludedId && (card.suit === suit || card.suit === 'J')).length;
    if (this.strength > 0) {
      const visible = this.visible(game);
      if (26 - visible.filter(card => card.suit === suit).length + 4 - visible.filter(card => card.suit === 'J').length + market < need) return 0;
    }
    let future = this.expected(game, suit) + this.expected(game, 'J');
    if (this.strength >= 1) {
      const competing = game.players.filter((rival, seat) => seat !== game.current && rival.quota?.suit === suit &&
        rival.quota.rank - 1 > rival.collection.length).map(rival => rival.quota.rank - 1 - rival.collection.length);
      if (excludedId >= 0) market = Math.max(0, market - competing.reduce((sum, value) => sum + value, 0));
      const unmet = Math.max(1, need - market);
      const pressure = competing.reduce((sum, other) => sum + Math.min(1, other / unmet), 0);
      future /= 1 + pressure;
    }
    return Math.pow(Math.min(1, (market + future * (game.playerCount === 3 ? .32 : .27)) / need), 1.7);
  }
  denial(game, card) {
    const impact = Math.max(0, ...game.players.filter((rival, seat) => seat !== game.current && rival.quota?.suit === card.suit)
      .map(rival => rival.quota.rank - 1 - rival.collection.length === 1 ? 2 : .5));
    return impact * (this.id === 4 ? 2 : this.id === 1 ? .4 : .2);
  }
  titleValue(player, card, chance) {
    let value = 0;
    const kinds = new Set(player.bundles.map(bundle => bundle.kind));
    if (player.bundles.length > 0 && player.bundles.length < 3 && kinds.size === 1 && kinds.has(card.suit)) value += 3 * chance;
    if (!player.achieved.some(item => item.suit === card.suit)) value += .5 * chance;
    return value * (this.id === 5 ? 2.8 : this.id === 0 || this.id === 3 ? .5 : this.id === 1 ? .6 : this.id === 4 ? .4 : .3);
  }
  shouldAbandonContested(game) {
    const player = game.players[game.current], quota = player.quota;
    if (this.strength !== 2 || !quota || game.plan !== 'normal' || game.turnGain) return false;
    if (!game.players.some((rival, seat) => seat !== game.current && rival.quota?.suit === quota.suit && rival.quota.rank - 1 > rival.collection.length)) return false;
    const need = quota.rank - 1 - player.collection.length;
    const available = game.market.filter(card => card && (card.suit === quota.suit || card.suit === 'J')).length;
    if (available >= need) return false;
    const chance = this.completion(game, quota.suit, need);
    if (chance >= (this.id === 3 ? .34 : .40)) return false;
    const alternatives = game.market.filter(card => card && card.suit !== quota.suit && card.suit !== 'J');
    if (!alternatives.length) return false;
    const best = Math.max(...alternatives.map(card => scoreFor(card.rank) * this.completion(game, card.suit, card.rank - 1, card.id)));
    return best >= 2 && best >= scoreFor(quota.rank) * chance * .70;
  }
  choose(game) {
    const player = game.players[game.current], id = this.id;
    if (game.canDeclare()) {
      const eligible = player.quota ? game.eligible() : game.market.filter(card => card && card.suit !== 'J');
      if (player.reshuffleLeft > 0 && !eligible.length && game.deck.length > 0 && !this.shouldAbandonContested(game)) game.declareReshuffle();
      else if (player.doubleLeft > 0 && game.deck.length > 1 && (
        player.quota && id !== 0 && player.quota.rank - 1 - player.collection.length > 0 && player.quota.rank - 1 - player.collection.length <= eligible.length ||
        !player.quota && [1, 2, 3].includes(id) && eligible.some(card => card.rank === 1))) game.declareDouble();
    }
    if (!player.quota) {
      const takes = game.market.filter(card => card && card.suit !== 'J');
      if (!takes.length) return { type: 'pass' };
      const lead = Math.max(...game.players.filter((_, seat) => seat !== game.current).map(finalScore)) - finalScore(player);
      const remaining = game.roundCount - game.roundIndex;
      const risk = [5, 2, 2, 1.2, 2.3, 2.4][id], time = [.58, .48, .90, .40, .54, .50][id];
      let upside = [.70, 1, .85, 1.55, .85, .95][id];
      if (lead >= 8 && remaining <= 1) upside *= 1.4;
      if (lead <= -10 && remaining <= 1) upside *= .8;
      const value = card => {
        const chance = this.completion(game, card.suit, card.rank - 1, card.id);
        return scoreFor(card.rank) * chance * upside - risk * (1 - chance) - time * card.rank * .65 + this.titleValue(player, card, chance) + this.denial(game, card);
      };
      takes.sort((a, b) => value(b) - value(a) || a.id - b.id);
      return { type: 'take', id: takes[0].id };
    }
    const need = player.quota.rank - 1 - player.collection.length;
    if (this.shouldAbandonContested(game)) return { type: 'abandon' };
    let available = game.eligible();
    if (!available.length) return { type: game.plan === 'normal' && !game.turnGain && this.completion(game, player.quota.suit, need) < (id === 3 ? .18 : .32) ? 'abandon' : 'pass' };
    available = [...available.filter(card => card.suit !== 'J'), ...available.filter(card => card.suit === 'J')];
    if (id === 5 && player.bundles.length < 3 && !player.bundles.some(bundle => bundle.hasWild)) {
      const suits = available.filter(card => card.suit !== 'J').length;
      if (suits > 0 && suits < need && this.completion(game, player.quota.suit, need - suits) > .60) available = available.filter(card => card.suit !== 'J');
    }
    const chosen = [], line = [...player.achieved, player.quota, ...player.collection];
    while (available.length && chosen.length < need) {
      const value = card => sequenceAt([...line, card], line.length) + this.denial(game, card);
      available.sort((a, b) => value(b) - value(a) || Number(a.suit === 'J') - Number(b.suit === 'J') || a.id - b.id);
      const card = available.shift(); chosen.push(card); line.push(card);
    }
    return chosen.length ? { type: 'collect', ids: chosen.map(card => card.id) } : { type: 'pass' };
  }
}
export function bindCpuProfiles(game, ids, strength, allSeats = false) {
  const profiles = new Map(); let index = 0;
  game.players.forEach((player, seat) => { if (allSeats || !player.human) profiles.set(seat, new ProfileMind(ids[index++], strength)); });
  const previous = minds.get(game);
  if (previous) game.observers.delete(previous.observe);
  const observe = (...args) => { for (const profile of profiles.values()) profile.observe(...args); };
  game.observers.add(observe); minds.set(game, { profiles, observe });
  return profiles;
}
export function chooseCpuAction(game, strength = 1, profile = 1) {
  let state = minds.get(game);
  if (!state) { bindCpuProfiles(game, game.players.map((_, seat) => seat % 6), strength, true); state = minds.get(game); }
  let mind = state.profiles.get(game.current);
  if (!mind || mind.id !== profile || mind.strength !== strength) {
    mind = new ProfileMind(profile, strength); state.profiles.set(game.current, mind);
  }
  return mind.choose(game);
}
export function randomCpuIds(count, random = Math.random) {
  const ids = [0, 1, 2, 3, 4, 5];
  for (let i = ids.length - 1; i > 0; i--) { const j = Math.floor(random() * (i + 1)); [ids[i], ids[j]] = [ids[j], ids[i]]; }
  return ids.slice(0, count);
}
