import { CPU_NAMES, Game, SUITS, achievementBonus, finalScore, sequencePoints, titleAwards } from './engine.mjs';
import { chooseCpuAction, randomCpuIds } from './cpu.mjs';

const root = document.querySelector('#app');
const goods = await fetch('./assets/goods.json').then(response => {
  if (!response.ok) throw Error('商品データを読み込めません');
  return response.json();
});
const store = 'quota.webLite.settings.v1';
const saved = (() => { try { return JSON.parse(localStorage.getItem(store)) || {}; } catch { return {}; } })();
let settings = { name: 'あなた', players: 3, roundMode: 1, strength: 1, bgm: 3, se: 3, confirm: true, ...saved };
let game = null, cast = randomCpuIds(3), selected = new Set(), modal = '', notice = '', cpuTimer = null;
let pending = null;
const bgm = new Audio('./assets/sound/play_bgm_02.mp3');
const roundMusic = new Audio('./assets/sound/end_bgm_01.mp3');
const finalMusic = new Audio('./assets/sound/end_bgm_03.mp3');
let music = null;
const escape = value => String(value).replace(/[&<>"']/g, char => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' })[char]);
const putSettings = () => { try { localStorage.setItem(store, JSON.stringify(settings)); } catch { /* private browsing */ } };
const volume = () => settings.bgm / 5 * .5;
function playMusic(audio) {
  if (music && music !== audio) { music.pause(); music.currentTime = 0; }
  music = audio; audio.volume = volume(); audio.currentTime = 0;
  if (settings.bgm) audio.play().catch(() => {});
}
bgm.addEventListener('timeupdate', () => { if (bgm.currentTime >= 92) bgm.currentTime = 16; });
function stopMusic() { if (music) { music.pause(); music.currentTime = 0; music = null; } }
function se(name) {
  if (!settings.se) return;
  const audio = new Audio(`./assets/sound/${name}.mp3`);
  audio.volume = settings.se / 5 * .55;
  audio.play().catch(() => {});
  setTimeout(() => { audio.pause(); audio.src = ''; }, 330);
}
function face(card) {
  if (card.suit === 'J') return goods.wild;
  return goods.goods[game.goods[SUITS.indexOf(card.suit)]];
}
function cardHtml(card, { small = false, selectable = false, selected: active = false, muted = false } = {}) {
  if (!card) return '<div class="card empty" aria-label="空き枠"></div>';
  const item = face(card), color = card.suit === 'J' ? '#ad8b2b' : ['#9b5433','#9a4d92','#397653','#315f9b'][SUITS.indexOf(card.suit)];
  const klass = `card ${small ? 'small' : ''} ${selectable ? 'selectable' : ''} ${active ? 'selected' : ''} ${muted ? 'muted' : ''}`;
  const content = `<span class="rank" style="color:${color}">${card.rank ?? '＊'}</span><img src="./assets/goods/${encodeURIComponent(item.file)}.png" alt="" loading="lazy"><span class="goods-name">${escape(item.name)}</span>`;
  return selectable ? `<button class="${klass}" data-card="${card.id}" aria-pressed="${active}"><span class="kind-mark" style="background:${color}"></span>${content}</button>` :
    `<div class="${klass}"><span class="kind-mark" style="background:${color}"></span>${content}</div>`;
}
function optionGroup(key, options) {
  return `<div class="segmented" role="group">${options.map(([value, label]) => `<button type="button" data-option="${key}" data-value="${value}" class="${String(settings[key]) === String(value) ? 'chosen' : ''}">${escape(label)}</button>`).join('')}</div>`;
}
function header() {
  return `<header class="mast"><div class="wordmark"><span>クォータ</span><strong>Quota</strong><em>揃えて、達成。</em></div><span class="edition">軽量Web版・オフライン</span></header>`;
}
function setupHtml() {
  const rounds = [[0,'1'],[1,String(settings.players)],[2,String(settings.players * 2)]];
  return `<main class="shell setup">${header()}
    <div class="intro"><p>交易品を揃え、ノルマを達成しよう。</p><h1>ひとりで、CPUと対局。</h1></div>
    <section class="setup-card" aria-label="対局設定">
      <h2>ゲーム設定</h2>
      <label class="field"><span>プレイヤー名</span><input id="player-name" maxlength="24" value="${escape(settings.name)}" autocomplete="nickname"></label>
      <div class="field"><span>プレイヤーの数</span>${optionGroup('players', [[3,'3人'],[4,'4人']])}</div>
      <div class="field"><span>ラウンド数</span>${optionGroup('roundMode', rounds)}</div>
      <div class="field"><span>CPUの強さ</span>${optionGroup('strength', [[0,'弱'],[1,'中'],[2,'強']])}</div>
      <div class="cpu-list"><strong>参加プレイヤー</strong><span>あなた ・ ${cast.slice(0, settings.players - 1).map(id => CPU_NAMES[id]).join(' ・ ')}</span></div>
      <button class="quiet" data-action="shuffle-cpu">CPUプレイヤーを入れ替え</button>
      <button class="primary start" data-action="start">ゲーム開始 <span>→</span></button>
    </section>
    <p class="setup-foot">この版はブラウザ内だけで動作します。対戦サーバーには接続しません。</p>
  </main>`;
}
function playerHtml(player, seat) {
  const isTurn = game.active() && game.current === seat;
  const badges = titleAwards(player).map(award => `<span class="title-badge">${escape(award.name)} +${award.points}</span>`).join('');
  return `<section class="player ${isTurn ? 'turn' : ''} ${player.human ? 'human' : ''}" aria-label="${escape(player.name)}">
    <div class="player-head"><h2><span class="seat">${seat + 1}</span>${escape(player.name)}${player.human && player.name !== 'あなた' ? '<span class="you">あなた</span>' : ''}</h2><strong>${finalScore(player)}<small>点</small></strong></div>
    <div class="player-body"><div><h3>ノルマ <span>${player.quota ? `${player.collection.length + 1}/${player.quota.rank}` : 'なし'}</span></h3>
      <div class="card-row mini">${player.quota ? cardHtml(player.quota, { small:true }) + player.collection.map(card => cardHtml(card, { small:true })).join('') : '<span class="empty-note">次のノルマを待っています</span>'}</div></div>
      <div><h3>実績 <span>${player.achieved.length}枚</span></h3><div class="achievement">${player.achieved.length ? player.achieved.slice(-12).map(card => `<span class="ach-chip" style="background:${card.suit === 'J' ? '#d6b255' : ['#b56d49','#a067ad','#55946c','#6689bd'][SUITS.indexOf(card.suit)]}">${card.rank ?? '＊'}</span>`).join('') : '<span class="empty-note">まだありません</span>'}</div></div></div>
    ${badges ? `<div class="badges">${badges}</div>` : ''}
  </section>`;
}
function actionHtml() {
  if (!game.active() || game.current !== 0) return '<div class="waiting">CPUの手番です…</div>';
  const player = game.players[0], cards = game.eligible(), need = player.quota ? player.quota.rank - 1 - player.collection.length : 0;
  return `<div class="actions">
    ${player.quota ? `<button class="primary" data-action="collect" ${selected.size ? '' : 'disabled'}>選んだ札を取る <span>${selected.size}/${need}</span></button>` : '<span class="action-hint">場札からノルマ札を選んでください</span>'}
    <div class="action-row"><button data-action="double" ${game.canDeclare() && player.doubleLeft ? '' : 'disabled'}>ダブル <small>残り${player.doubleLeft}</small></button>
    <button data-action="reshuffle" ${game.canDeclare() && player.reshuffleLeft ? '' : 'disabled'}>配り直し <small>残り${player.reshuffleLeft}</small></button>
    <button data-action="abandon" ${player.quota ? '' : 'disabled'}>放棄</button><button data-action="pass">パス</button></div>
    ${player.quota && cards.length ? '<p>同じ商品または金貨を選択。1手番で複数枚取れます。</p>' : ''}
  </div>`;
}
function gameHtml() {
  const human = game.players[0];
  const eligible = new Set(game.eligible().map(card => card.id));
  return `<main class="shell board">${header()}
    <div class="board-toolbar"><div><span class="round-pill">ラウンド ${game.roundIndex}/${game.roundCount}</span><span class="deck-info">山札 残り${game.deck.length}枚</span></div><div><button class="toolbar" data-action="settings">設定</button><button class="toolbar" data-action="exit">EXIT</button></div></div>
    <section class="market" aria-label="場札"><div class="section-title"><h2>場札</h2><span>${game.active() ? `${escape(game.players[game.current].name)}の手番` : 'ラウンド終了'}</span></div>
      <div class="market-row">${game.market.map(card => cardHtml(card, { selectable: game.active() && game.current === 0 && !!card, selected: selected.has(card?.id), muted: !!human.quota && !!card && !eligible.has(card.id) })).join('')}</div></section>
    <div class="play-grid"><div class="player-stack">${game.players.map(playerHtml).join('')}</div><aside class="right-rail"><div class="turn-box"><span>現在の手番</span><strong>${game.active() ? escape(game.players[game.current].name) : '終了'}</strong><p>${notice ? escape(notice) : 'カードを選んで進めてください。'}</p></div>${actionHtml()}<div class="log"><h3>最近の出来事</h3>${game.log.slice(-4).reverse().map(line => `<p>${escape(line)}</p>`).join('')}</div></aside></div>
    ${modalHtml()}
  </main>`;
}
function modalHtml() {
  if (modal === 'round') {
    const standings = game.standings(), end = game.finished;
    return `<div class="shade" role="dialog" aria-modal="true" aria-label="ラウンド終了"><div class="dialog score-dialog">
      <span class="overline">ROUND ${game.roundIndex} / ${game.roundCount}</span><h2>${end ? '最終ラウンド終了' : `第${game.roundIndex}ラウンド終了`}</h2>
      <p>${game.roundEndReason === 'DECK' ? '山札がなくなりました。' : '膠着により終了しました。'} 得点を確認しましょう。</p>
      <div class="score-list">${standings.map(row => {
        const player = game.players[row.seat];
        return `<div class="score-row"><b>${row.rank}位</b><span>${escape(player.name)}</span><small>確定 ${player.score} ＋ 連続 ${sequencePoints(player.achieved)} ＋ 称号 ${titleAwards(player).reduce((n, a) => n + a.points, 0)}</small><strong>${row.score}点</strong></div>`;
      }).join('')}</div>
      ${end ? `<p class="winner">${game.standings().filter(row => row.rank === 1).length > 1 ? '今回は、' : ''}${game.standings().filter(row => row.rank === 1).map(row => `${escape(game.players[row.seat].name)}さん`).join('、')}${game.standings().filter(row => row.rank === 1).length > 1 ? 'の同点優勝です。おめでとうございます。' : '、総合優勝おめでとうございます。'}</p>` : ''}
      <button class="primary wide" data-action="${end ? 'home' : 'next-round'}">${end ? 'スタート画面へ' : '次のラウンドへ'}</button>
    </div></div>`;
  }
  if (modal === 'settings') return `<div class="shade" role="dialog" aria-modal="true" aria-label="個人設定"><div class="dialog"><h2>個人設定</h2>
    <div class="field"><span>確認を求める</span>${optionGroup('confirm', [[true,'YES'],[false,'NO']])}</div>
    <div class="field"><span>BGM音量</span>${optionGroup('bgm', Array.from({ length: 6 }, (_, n) => [n, n ? String(n) : 'OFF']))}</div>
    <div class="field"><span>SE音量</span>${optionGroup('se', Array.from({ length: 6 }, (_, n) => [n, n ? String(n) : 'OFF']))}</div>
    <button class="primary wide" data-action="close-modal">OK</button></div></div>`;
  if (modal === 'confirm') return `<div class="shade" role="dialog" aria-modal="true" aria-label="確認"><div class="dialog"><h2>確認</h2><p>${escape(pending?.label || '')}</p><div class="dialog-actions"><button data-action="cancel-confirm">キャンセル</button><button class="primary" data-action="accept-confirm">続ける</button></div></div></div>`;
  return '';
}
function render() { root.innerHTML = game ? gameHtml() : setupHtml(); }
function start() {
  settings.name = (root.querySelector('#player-name')?.value || '').trim().slice(0, 24) || 'あなた';
  putSettings();
  const rounds = settings.roundMode === 0 ? 1 : settings.players * (settings.roundMode === 2 ? 2 : 1);
  game = new Game({ playerCount: settings.players, rounds, name: settings.name, cpuIds: cast });
  selected.clear(); modal = ''; notice = ''; playMusic(bgm); render(); queueCpu();
}
function queueCpu() {
  clearTimeout(cpuTimer);
  if (!game || !game.active() || game.current === 0 || modal) return;
  cpuTimer = setTimeout(() => {
    try {
      const player = game.players[game.current];
      const profile = cast[game.current - 1] ?? 1;
      const action = chooseCpuAction(game, settings.strength, profile);
      if (action) game.step(action);
      notice = `${player.name}：${action ? ({take:'ノルマを選択',collect:'カードを収集',abandon:'放棄',pass:'パス'}[action.type]) : '特殊アクション'}`;
      afterStep();
    } catch (error) { notice = `CPUの行動に失敗しました：${error.message}`; render(); }
  }, 300);
}
function afterStep() {
  selected.clear();
  if (game.finished || game.awaitingNextRound) {
    stopMusic(); playMusic(game.finished ? finalMusic : roundMusic); modal = 'round';
  }
  render(); queueCpu();
}
function runAction(action) {
  try {
    if (action.type === 'take' || action.type === 'collect') se('card_03');
    game.step(action); afterStep();
  } catch (error) { notice = error.message; render(); }
}
function confirmAction(label, run) {
  if (!settings.confirm) { run(); return; }
  pending = { label, run }; modal = 'confirm'; render();
}
root.addEventListener('click', event => {
  const option = event.target.closest('[data-option]');
  if (option) {
    const key = option.dataset.option;
    settings[key] = key === 'confirm' ? option.dataset.value === 'true' : Number(option.dataset.value);
    if (key === 'players') cast = randomCpuIds(settings.players - 1);
    if (key === 'bgm' && music) { music.volume = volume(); if (settings.bgm && music.paused) music.play().catch(() => {}); else if (!settings.bgm) music.pause(); }
    putSettings(); render(); return;
  }
  const cardButton = event.target.closest('[data-card]');
  if (cardButton && game?.active() && game.current === 0 && !modal) {
    const id = Number(cardButton.dataset.card), player = game.players[0];
    if (!player.quota) runAction({ type: 'take', id });
    else if (game.eligible().some(card => card.id === id)) {
      if (selected.has(id)) selected.delete(id); else selected.add(id);
      if (selected.size > player.quota.rank - 1 - player.collection.length) selected.delete(id);
      render();
    }
    return;
  }
  const button = event.target.closest('[data-action]'); if (!button) return;
  const action = button.dataset.action;
  if (action === 'start') start();
  else if (action === 'shuffle-cpu') { cast = randomCpuIds(settings.players - 1); render(); }
  else if (action === 'settings') { modal = 'settings'; render(); }
  else if (action === 'close-modal') { modal = ''; render(); queueCpu(); }
  else if (action === 'cancel-confirm') { pending = null; modal = ''; render(); }
  else if (action === 'accept-confirm') { const run = pending.run; pending = null; modal = ''; run(); }
  else if (action === 'collect') runAction({ type: 'collect', ids: [...selected] });
  else if (action === 'pass') runAction({ type: 'pass' });
  else if (action === 'abandon') confirmAction('このノルマと収集したカードを放棄しますか？', () => runAction({ type: 'abandon' }));
  else if (action === 'double') { try { game.declareDouble(); notice = 'ダブル：2回続けて行動できます'; render(); } catch (error) { notice = error.message; render(); } }
  else if (action === 'reshuffle') { try { game.declareReshuffle(); notice = '場札を配り直しました'; render(); } catch (error) { notice = error.message; render(); } }
  else if (action === 'next-round') { modal = ''; game.beginNextRound(); playMusic(bgm); render(); queueCpu(); }
  else if (action === 'home') { clearTimeout(cpuTimer); stopMusic(); game = null; modal = ''; render(); }
  else if (action === 'exit') confirmAction('対局を終了してスタート画面に戻りますか？', () => { clearTimeout(cpuTimer); stopMusic(); game = null; modal = ''; render(); });
});
root.addEventListener('change', event => {
  if (event.target.id === 'player-name') { settings.name = event.target.value.trim().slice(0, 24) || 'あなた'; putSettings(); }
});
render();
if ('serviceWorker' in navigator) navigator.serviceWorker.register('./sw.js').catch(() => {});
