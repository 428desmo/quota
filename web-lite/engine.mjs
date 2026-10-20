// A browser-native port of Quota's offline rules. No Unity or server dependency.
import { PythonRandom } from './random.mjs';
export const SUITS = ['S', 'H', 'D', 'C'];
export const CPU_NAMES = ['慎重なピエトロ', '柔軟なマルコ', '手際のよいルチア', '大胆なロレンツォ', '抜け目ないイザベラ', '好奇心旺盛なソフィア'];

export function rng(seed = Math.floor(Math.random() * 0xffffffff)) {
  const source = new PythonRandom(seed);
  const random = () => source.next() / 0x100000000;
  random.randBelow = n => source.randBelow(n);
  return random;
}
export function shuffle(items, random) {
  for (let i = items.length - 1; i > 0; i--) {
    const j = random.randBelow ? random.randBelow(i + 1) : Math.floor(random() * (i + 1));
    [items[i], items[j]] = [items[j], items[i]];
  }
  return items;
}
export function makeDeck() {
  const cards = [];
  for (let deck = 0; deck < 2; deck++) {
    for (const suit of SUITS) for (let rank = 1; rank <= 13; rank++) cards.push({ id: cards.length, suit, rank });
    for (let i = 0; i < 2; i++) cards.push({ id: cards.length, suit: 'J', rank: null });
  }
  return cards;
}
export function achievementBonus(rank) { return rank <= 6 ? 0 : rank <= 9 ? 1 : rank <= 12 ? 3 : 6; }
export function sequenceAt(cards, index) {
  if (index < 1) return 0;
  const left = cards[index - 1], right = cards[index];
  if (right.suit === 'J') {
    let count = 0;
    for (let i = index - 1; i >= 0 && cards[i].suit === 'J'; i--) count++;
    return count;
  }
  if (left.suit === 'J') return 0;
  if (left.rank === right.rank) return 2;
  return Math.abs(left.rank - right.rank) === 1 ? 1 : 0;
}
export function sequencePoints(cards) { return cards.reduce((sum, _, index) => sum + sequenceAt(cards, index), 0); }
export function titleAwards(player) {
  const awards = [];
  if (SUITS.every(suit => player.achieved.some(card => card.suit === suit)) && player.achieved.some(card => card.suit === 'J'))
    awards.push({ name: '五種の品揃え', points: 5 });
  if (player.bundles.length >= 3) {
    if (new Set(player.bundles.map(bundle => bundle.kind)).size === 1) awards.push({ name: '単色達成', points: 15 });
    if (player.bundles.every(bundle => !bundle.hasWild)) awards.push({ name: '生粋の買い付け', points: 5 });
  }
  return awards;
}
export const titlePoints = player => titleAwards(player).reduce((sum, award) => sum + award.points, 0);
export const finalScore = player => player.score + sequencePoints(player.achieved) + titlePoints(player);

export class Game {
  constructor({ playerCount = 3, rounds = playerCount, name = 'あなた', cpuIds = [0, 1, 2, 3], spectator = false, seed } = {}) {
    if (![3, 4].includes(playerCount) || ![1, playerCount, playerCount * 2].includes(rounds)) throw Error('設定値が不正です');
    this.random = rng(seed);
    this.playerCount = playerCount;
    this.roundCount = rounds;
    this.roundIndex = 1;
    this.observers = new Set();
    this.players = Array.from({ length: playerCount }, (_, seat) => ({
      name: seat === 0 && !spectator ? name : CPU_NAMES[cpuIds[spectator ? seat : seat - 1] ?? seat], human: seat === 0 && !spectator,
      quota: null, collection: [], achieved: [], bundles: [], score: 0,
      achieveCount: 0, maxSingleScore: 0, reshuffleLeft: 1, doubleLeft: 1,
    }));
    this.log = [];
    this.finished = false;
    this.awaitingNextRound = false;
    this.roundEndReason = null;
    this.dealRound();
    this.random.randBelow(playerCount); // Keep the opening stream identical to Unity.
    this.chooseGoods();
    this.turnOrder = shuffle(Array.from({ length: playerCount }, (_, i) => i), this.random);
    this.openRound(false, false);
  }

  chooseGoods() { this.goods = shuffle(Array.from({ length: 27 }, (_, i) => i), this.random).slice(0, 4); }
  marketSize() { return this.playerCount === 4 ? 6 : 7; }
  dealRound() {
    this.deck = shuffle(makeDeck(), this.random);
    this.removed = Array.from({ length: 8 }, () => this.deck.pop());
    this.market = Array.from({ length: this.marketSize() }, () => this.deck.pop());
    this.discard = [];
  }
  openRound(resetScores, deal = true) {
    if (resetScores) for (const player of this.players) {
      player.score += sequencePoints(player.achieved) + titlePoints(player);
      player.quota = null; player.collection = []; player.achieved = []; player.bundles = [];
    }
    if (deal) this.dealRound();
    for (const player of this.players) { player.reshuffleLeft = 1; player.doubleLeft = 1; }
    this.noGainStreak = 0; this.stallFlag = false; this.turnGain = false;
    this.plan = 'normal'; this.doubleStage = 0; this.doubleGained = false;
    this.turnNumber = 1; this.reshuffleCount = 0;
    this.orderCursor = (this.roundIndex - 1) % this.turnOrder.length;
    this.current = this.turnOrder[this.orderCursor];
    this.log.push(`第${this.roundIndex}ラウンド開始`);
  }
  beginNextRound() {
    if (!this.awaitingNextRound || this.finished) throw Error('次のラウンドはありません');
    this.roundIndex++; this.awaitingNextRound = false; this.roundEndReason = null;
    this.chooseGoods(); this.openRound(true);
    this.notify([], 0, false, true);
  }
  active() { return !this.finished && !this.awaitingNextRound; }
  eligible() {
    const player = this.players[this.current];
    return player.quota ? this.market.filter(card => card && (card.suit === player.quota.suit || card.suit === 'J')) : [];
  }
  legal(action) {
    if (!this.active()) return false;
    const player = this.players[this.current];
    if (action.type === 'pass') return true;
    if (action.type === 'abandon') return !!player.quota;
    if (action.type === 'take') return !player.quota && this.market.some(card => card && card.id === action.id && card.suit !== 'J');
    if (action.type === 'collect' && player.quota && Array.isArray(action.ids)) {
      const need = player.quota.rank - 1 - player.collection.length;
      const ids = action.ids;
      return ids.length > 0 && ids.length <= need && new Set(ids).size === ids.length && ids.every(id => this.eligible().some(card => card.id === id));
    }
    return false;
  }
  take(id) {
    const index = this.market.findIndex(card => card?.id === id);
    if (index < 0) throw Error('場札にありません');
    const card = this.market[index]; this.market[index] = null; return card;
  }
  achieve(player, cards, rank) {
    player.achieved.push(...cards);
    player.bundles.push({ kind: cards[0].suit, hasWild: cards.some(card => card.suit === 'J') });
    const points = cards.length + achievementBonus(rank);
    player.score += points; player.achieveCount++; player.maxSingleScore = Math.max(player.maxSingleScore, points);
    this.log.push(`${player.name} がノルマ達成（${points}点）`);
  }
  notify(market, deckCount, reshuffled = false, newRound = false) {
    for (const observer of this.observers) observer(this, market, deckCount, reshuffled, newRound);
  }
  step(action) {
    const market = [...this.market], deckCount = this.deck.length;
    this.apply(action);
    this.notify(market, deckCount, action.type === 'pass' && market.some((card, i) => card !== this.market[i]));
  }
  apply(action) {
    if (!this.legal(action)) throw Error('この行動はできません');
    const player = this.players[this.current];
    let gained = false;
    if (action.type === 'take') {
      const card = this.take(action.id); gained = true;
      if (card.rank === 1) this.achieve(player, [card], 1);
      else { player.quota = card; player.collection = []; }
      this.log.push(`${player.name} がノルマ札を選びました`);
    } else if (action.type === 'collect') {
      const cards = action.ids.map(id => this.take(id)); gained = true; this.turnGain = true;
      player.collection.push(...cards); this.log.push(`${player.name} が${cards.length}枚を収集`);
      if (1 + player.collection.length === player.quota.rank) {
        const rank = player.quota.rank; this.achieve(player, [player.quota, ...player.collection], rank);
        player.quota = null; player.collection = [];
      } else if (this.eligible().length > 0) return;
    } else if (action.type === 'abandon') {
      this.discard.push(player.quota, ...player.collection); player.quota = null; player.collection = [];
      this.log.push(`${player.name} がノルマを放棄`); return;
    } else this.log.push(`${player.name} がパス`);
    this.closeAction(gained);
  }
  canDeclare() { return this.active() && this.plan === 'normal' && !this.turnGain; }
  declareReshuffle() {
    if (!this.canDeclare() || !this.players[this.current].reshuffleLeft) throw Error('配り直しは使えません');
    const market = [...this.market], deckCount = this.deck.length;
    this.players[this.current].reshuffleLeft--; this.plan = 'reshuffle'; this.reshuffle();
    this.notify(market, deckCount, true);
    this.log.push(`${this.players[this.current].name} が配り直し`);
  }
  declareDouble() {
    if (!this.canDeclare() || !this.players[this.current].doubleLeft) throw Error('ダブルは使えません');
    this.players[this.current].doubleLeft--; this.plan = 'double'; this.doubleStage = 1;
    this.log.push(`${this.players[this.current].name} がダブル`);
  }
  cancelDouble() {
    if (!this.active() || this.plan !== 'double' || this.doubleStage !== 1 || this.turnGain) throw Error('ダブルは取り消せません');
    this.players[this.current].doubleLeft++; this.plan = 'normal'; this.doubleStage = 0; this.doubleGained = false;
    this.log.push(`${this.players[this.current].name} がダブルを取り消し`);
  }
  reshuffle() {
    this.deck.push(...this.market.filter(Boolean)); shuffle(this.deck, this.random);
    this.market = Array.from({ length: this.marketSize() }, () => this.deck.pop() || null);
  }
  closeAction(gained) {
    gained ||= this.turnGain;
    if (this.plan === 'double' && this.doubleStage === 1) {
      this.doubleGained = gained; this.turnGain = false;
      if (this.refill()) this.doubleStage = 2;
      return;
    }
    this.endTurn(gained || this.doubleGained);
  }
  endTurn(gained) {
    if (gained) { this.noGainStreak = 0; this.stallFlag = false; }
    else if (++this.noGainStreak === this.playerCount) {
      if (this.stallFlag) { this.endRound('STALL'); return; }
      this.reshuffle(); this.noGainStreak = 0; this.stallFlag = true; this.reshuffleCount++;
      this.log.push('全員がパスしたため場札を配り直しました');
    }
    this.turnGain = false; this.plan = 'normal'; this.doubleStage = 0; this.doubleGained = false;
    this.orderCursor = (this.orderCursor + 1) % this.turnOrder.length;
    this.current = this.turnOrder[this.orderCursor]; this.turnNumber++;
    this.refill();
  }
  refill() {
    while (this.market.length < this.marketSize()) this.market.push(null);
    for (let i = 0; i < this.market.length; i++) {
      if (this.market[i]) continue;
      if (!this.deck.length) { this.endRound('DECK'); return false; }
      this.market[i] = this.deck.pop();
    }
    return true;
  }
  endRound(reason) {
    this.roundEndReason = reason; this.awaitingNextRound = this.roundIndex < this.roundCount;
    this.finished = !this.awaitingNextRound;
    this.log.push(reason === 'DECK' ? '山札切れ' : '膠着によりラウンド終了');
  }
  standings() {
    const seats = this.players.map((_, seat) => seat).sort((a, b) => {
      const left = this.players[a], right = this.players[b];
      return finalScore(right) - finalScore(left) || right.achieveCount - left.achieveCount || right.maxSingleScore - left.maxSingleScore || a - b;
    });
    let rank = 1;
    return seats.map((seat, index) => {
      const player = this.players[seat], prev = this.players[seats[index - 1]];
      const tied = prev && finalScore(player) === finalScore(prev) && player.achieveCount === prev.achieveCount && player.maxSingleScore === prev.maxSingleScore;
      if (!tied) rank = index + 1;
      return { seat, rank, score: finalScore(player) };
    });
  }
}
