import { CPU_NAMES, Game, SUITS, finalScore, sequenceAt, sequencePoints, titleAwards, achievementBonus } from './engine.mjs';
import { bindCpuProfiles, chooseCpuAction, randomCpuIds, CPU_DESCRIPTIONS } from './cpu.mjs';
import { Soundscape } from './audio.mjs';
import { displayOrder, titleStates, roundBreakdown, scoreRanks, winnerText, guideHtml, escapeHtml as esc } from './presentation.mjs';

const root = document.querySelector('#app');
const goods = await fetch('./assets/goods.json').then(response => { if (!response.ok) throw Error('商品データを読み込めません'); return response.json(); });
const store = 'quota.webLite.settings.v1';
const saved = (() => { try { return JSON.parse(localStorage.getItem(store)) || {}; } catch { return {}; } })();
const settings = { name: 'あなた', players: 3, roundMode: 1, strength: 1, bgm: 5, se: 5, confirm: true, ...saved };
for (const [key, valid, fallback] of [['players',[3,4],3],['roundMode',[0,1,2],1],['strength',[0,1,2],1],['bgm',[0,1,2,3,4,5],5],['se',[0,1,2,3,4,5],5]]) if (!valid.includes(settings[key])) settings[key] = fallback;
settings.name = String(settings.name).slice(0,24); settings.confirm = settings.confirm !== false;
let cast = Array.isArray(saved.cast) && saved.cast.length >= 3 && new Set(saved.cast).size === saved.cast.length && saved.cast.every(id => Number.isInteger(id) && id >= 0 && id < 6) ? [...saved.cast] : randomCpuIds(4);
if(cast.length<settings.players)cast.push(...randomCpuIds(6).filter(id=>!cast.includes(id)).slice(0,settings.players-cast.length));
let page = 'menu', game = null, shown = null, busy = false, modal = '', pending = null, notice = '', draft = [];
let cpuTimer = null, runId = 0, ceremony = null, roundBase = [], hidden = new Set(), titleVerdict = false;
let slide = 0, slidePaused = false, slideTimer = null;
let renderedModal = '';
const guideTexts = new Map();
const sound = new Soundscape(() => settings);
const reducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const alive = token => token === runId && !!game;
function save() { try { localStorage.setItem(store, JSON.stringify({ ...settings, cast })); } catch { /* private mode */ } }
function cloneState(g) {
  return { goods:[...g.goods], market:[...g.market], deckCount:g.deck.length, players:g.players.map(p => ({ ...p, collection:[...p.collection], achieved:[...p.achieved], bundles:[...p.bundles] })),
    playerCount:g.playerCount, turnOrder:[...g.turnOrder], current:g.current, roundIndex:g.roundIndex, roundCount:g.roundCount,
    turnNumber:g.turnNumber, noGainStreak:g.noGainStreak, stallFlag:g.stallFlag, reshuffleCount:g.reshuffleCount, plan:g.plan, doubleStage:g.doubleStage };
}
function button(label, action, className = 'primary', extra = '') { return `<button type="button" class="${className}" data-action="${action}" ${extra}>${label}</button>`; }
function header(tagline = false) { return `<header class="mast"><img class="title-logo" src="./assets/title1.png" alt="Quota 揃えて、達成。">${tagline ? '<img class="tagline" src="./assets/title2.png" alt="ノルマは、自分で決めろ。">' : ''}</header>`; }
function options(key, list) { const label=({players:'プレイヤーの数',roundMode:'ラウンド数',strength:'CPUプレイヤーの強さ',confirm:'放棄などに確認を求める',bgm:'BGM音量',se:'SE音量'})[key];return `<div class="segmented" role="group" aria-label="${label}">${list.map(([value,label]) => `<button type="button" data-option="${key}" data-value="${value}" aria-pressed="${String(settings[key]) === String(value)}" class="${String(settings[key]) === String(value) ? 'chosen' : ''}">${esc(label)}</button>`).join('')}</div>`; }
function field(label, key, list) { return `<div class="field"><span>${label}：</span>${options(key,list)}</div>`; }
function menuHtml() {
  return `<main class="shell menu">${header(true)}<section class="menu-fields">
    <div class="menu-guides">${button('遊び方','how-to','paper')}${button('詳細ルール','rules','paper')}</div>
    <div class="menu-mode"><span>ゲームモード：</span><strong>オフライン</strong></div>
    <label class="name-field"><span>あなたの名前：</span><input id="player-name" autocomplete="nickname" maxlength="24" value="${esc(settings.name)}"></label>
    ${button('新しいゲームの準備','lobby','primary wide')}</section><span class="version">Quota 2 · OFFLINE</span>${modalHtml()}</main>`;
}
function lobbyHtml() {
  const order = cast.slice(0,settings.players - 1);
  return `<main class="shell lobby">${header(true)}<section class="lobby-fields"><h1>ゲーム設定</h1>
    ${field('プレイヤーの数','players',[[3,'3人'],[4,'4人']])}${field('ラウンド数','roundMode',[[0,'1'],[1,String(settings.players)],[2,String(settings.players*2)]])}
    <h2 class="participants-label">参加プレイヤー：</h2><div class="participants"><div class="participant"><span>1. ${esc(settings.name)}</span><small>あなた</small></div>${order.map((id,i) => `<div class="participant"><span>${i+2}. ${esc(CPU_NAMES[id])}</span><small>CPU</small></div>`).join('')}
      <div class="cpu-buttons">${button('CPUプレイヤー入れ替え','shuffle-cpu')}${button('CPUプレイヤーの指名','nominate')}</div></div>
    ${field('CPUプレイヤーの強さ','strength',[[0,'弱'],[1,'中'],[2,'強']])}<div class="lobby-secondary">${button('個人設定','settings')}</div>
    <div class="lobby-start">${button('ゲーム開始','start','primary wide')}${button('CPU模擬戦を見る','simulate','primary wide')}</div>${button('戻る','menu','paper wide')}
    </section><span class="version">Quota 2 · OFFLINE</span>${modalHtml()}</main>`;
}
const kindIndex = card => card.suit === 'J' ? 0 : ({S:1,H:2,C:3,D:4})[card.suit];
const markerColors = ['#000000','#D55E00','#009E73','#0072B2','#A5378D'];
const rankColors = ['#C4A035','#A0522D','#7B3FA0','#2E7D32','#1E5AA8'];
function face(card) { return card.suit === 'J' ? goods.wild : goods.goods[(shown || game).goods[kindIndex(card)-1]]; }
function cardHtml(card, { small = false, selectable = false, muted = false, coins = [] } = {}) {
  if (!card) return '<div class="card empty" aria-label="空き枠"></div>';
  const item = face(card), kind = kindIndex(card), tag = selectable ? 'button' : 'div';
  return `<${tag} class="card ${small ? 'small' : ''} ${muted ? 'muted' : ''} ${hidden.has(card.id) ? 'concealed' : ''}" data-card-id="${card.id}" ${selectable ? `type="button" data-card="${card.id}" aria-label="${esc(item.name)} ${card.rank ?? '金貨'}"` : ''} style="--marker:${markerColors[kind]};--mark-y:${(50+25*kind)/2}%;--rank:${rankColors[kind]}">
    <span class="kind-mark"></span><span class="rank">${card.rank ?? '＊'}</span><img src="./assets/goods/${encodeURIComponent(item.file)}.png" alt="" draggable="false"><span class="goods-name">${esc(item.name)}</span>${coins.length ? `<span class="card-coins">${coins.map((color,i)=>`<i class="chip ${color}" style="--coin-x:${coins.length>4?(i%2)*30:0};--coin-y:${40+Math.floor(i/(coins.length>4?2:1))*Math.min(22,70/Math.max(1,Math.ceil(coins.length/(coins.length>4?2:1))-1))}"></i>`).join('')}</span>` : ''}</${tag}>`;
}
function fanHtml(cards, label, coins = new Map()) {
  const achieved=label==='実績札';
  return `<div class="fan ${cards.length?'':'vacant'}" data-zone="${achieved?'achieved':'quota'}" aria-label="${label}">${cards.map((card,i)=>`<div class="fan-slot" style="--card-x:${6.5+(achieved?i*3:i===0?0:70+(i-1)*40)}">${cardHtml(card,{small:true,coins:coins.get(card.id)||[]})}</div>`).join('')}</div>`;
}
function quotaCoins(p) {
  if(!p.quota)return new Map();
  const cards=[p.quota,...p.collection],line=[...p.achieved,...cards],coins=new Map();
  cards.forEach((card,i)=>coins.set(card.id,[...Array(i===0?achievementBonus(p.quota.rank):0).fill('green'),...Array(sequenceAt(line,p.achieved.length+i)).fill('purple')]));
  return coins;
}
function makeTokens(seat, green, purple, blue = 0) {
  return ['green','purple','blue'].flatMap((color,i) => Array.from({length:[green,purple,blue][i]},(_,n) => ({ color, key:`${seat}-${color}-${n}` })));
}
function tokenHtml(token, index) { return `<span class="chip ${token.color}" data-chip="${token.key}" style="left:${10+(index*37)%76}%;top:${12+(index*23)%57}%;--tilt:${index*47%90-45}deg"></span>`; }
function trayHtml(p,seat) {
  const breakdown = roundBreakdown(p, roundBase[seat] || 0);
  const tokens = ceremony ? ceremony.trays[seat] : makeTokens(seat,breakdown.green,breakdown.purple);
  return `<div class="tray" data-tray="${seat}" aria-label="ボーナスチップ ${tokens.length}点">${tokens.map(tokenHtml).join('')}</div>`;
}
function actionHtml(p, seat) {
  if (!game.active() || !p.human || game.current !== seat || busy) return '';
  const buttons = [];
  if (game.plan === 'double' && game.doubleStage === 1 && !game.turnGain) buttons.push(['キャンセル','cancel-double']);
  else if (game.canDeclare() && p.doubleLeft) buttons.push(['ダブル','double']);
  if (game.canDeclare() && p.reshuffleLeft) buttons.push(['配り直し','reshuffle']);
  if (p.quota) buttons.push(['放棄','abandon']);
  buttons.push(['パス','pass']);
  return `<div class="action-stack">${buttons.map(([label,action],i) => button(label,action,'action-button',`style="--slot:${i};--caption-length:${label.length}"`)).join('')}</div>`;
}
function playerHtml(p,seat) {
  const g=shown||game,turn=game.active()&&g.current===seat;
  const ordinal=(g.turnOrder.indexOf(seat)-(g.roundIndex-1)%g.playerCount+g.playerCount)%g.playerCount+1;
  const caption=['','①','②','③','④'][ordinal]+p.name;
  const fitted=width=>Math.max(18,Math.min(36,Math.floor(width*.92/caption.length)));
  return `<section class="player ${turn?'turn':''} ${p.human?'human':''}" data-player="${seat}" aria-label="${esc(p.name)}"><h2 class="player-name" style="--portrait-name:${fitted(516)};--wide-name:${Math.min(30,fitted(648))}">${esc(caption)}</h2>
    <div class="player-zones"><div class="stat-labels"><h3 class="quota-label">ノルマ</h3><strong class="quota-count">${p.quota?p.collection.length+1:''}<small>${p.quota?'/'+p.quota.rank:''}</small></strong><h3 class="achieved-label">実績</h3>${g.roundIndex>=2?`<small class="previous-score">${roundBase[seat]||0}+</small>`:''}<strong class="achieved-count ${g.roundIndex>=2?'with-previous':''}">${p.achieved.length}</strong></div>
    <div class="card-zones">${fanHtml(p.quota?[p.quota,...p.collection]:[],'ノルマ札と収集札',quotaCoins(p))}${fanHtml(p.achieved,'実績札')}</div>
    <div class="bonus-zone"><div class="special-remaining"><span class="${p.doubleLeft?'':'used'}">${p.doubleLeft?'ダブル':'USED'}</span><span class="${p.reshuffleLeft?'':'used'}">${p.reshuffleLeft?'配り直し':'USED'}</span></div>
    <div class="titles">${titleStates(p,titleVerdict).map(t=>`<div class="title ${t.earned?'earned':''} ${t.impossible?'invalid':''}"><span aria-hidden="true">${t.earned?'○':' '}</span>${t.name}</div>`).join('')}</div><div class="tray-label">ボーナス</div>${trayHtml(p,seat)}</div></div>${actionHtml(p,seat)}</section>`;
}
function gameHtml() {
  const g = shown || game, human = g.players.find(p=>p.human);
  const canClick = game.active() && game.players[game.current].human && !busy && !modal;
  const turnText = canClick ? `${game.plan==='double'?'ダブル：':''}${human.quota?'同じ商品か金貨をタップして収集。取らずにパスもできます。':'場札をタップしてノルマ札を選んでください。'}` : notice || (game.active() ? g.players[g.current].name+'の手番' : 'ラウンド終了');
  return `<main class="shell board" style="--players:${g.playerCount}"><div class="board-top">${header()}<div class="board-controls">${human ? button('設定','settings','toolbar') : ''}${button('EXIT','exit','toolbar exit')}</div><div class="round-state"><span>ラウンド ${g.roundIndex}/${g.roundCount}</span><span>膠着：${Array.from({length:g.playerCount*2},(_,i)=>`<i class="${i<g.noGainStreak+(g.stallFlag?g.playerCount:0)?'lit':''} ${i>=g.playerCount?'late':''}"></i>`).join('')}</span></div><div class="deck" data-deck><small>山札</small><strong>${g.deckCount ?? g.deck.length}</strong></div></div>
    <section class="market" aria-label="場札"><div class="market-row">${g.market.map(card => cardHtml(card,{selectable:!!card&&canClick,muted:!!card&&!!human?.quota&&card.suit!==human.quota.suit&&card.suit!=='J'})).join('')}</div></section>
    <div class="turn-notice" role="status">${esc(turnText)}</div>
    <div class="player-stack">${displayOrder(g).map(seat=>playerHtml(g.players[seat],seat)).join('')}</div>
    ${ceremonyHtml()}${modalHtml()}</main>`;
}
function ceremonyHtml() {
  if (!ceremony || ceremony.phase === 'verdict') return '';
  const c = ceremony;
  if(c.phase!=='reason')c.maxHeight=Math.max(c.maxHeight||340,c.phase==='titles'?c.awards.length*56+136:c.order.length*64+232);
  return `<section class="ceremony-panel" style="--panel-height:${c.phase==='reason'?340:c.maxHeight}" role="dialog" aria-modal="false" aria-label="ラウンド終了"><h2>第${game.roundIndex}ラウンド終了</h2>
    ${c.phase==='reason' ? `<p>${game.roundEndReason==='DECK'?'山札切れでラウンド終了。':'膠着の連続でラウンド終了。'}</p>` : c.phase==='titles' ? `<div class="award-lines">${c.awards.map((award,i)=>`<div data-award="${i}"><span>${esc(game.players[award.seat].name)}</span><b>「${award.name}」</b><small>+${award.points}</small></div>`).join('')}</div>` : `<div class="score-list">${c.order.map(seat=>`<div class="score-row" data-score-row="${seat}"><span class="place">${c.ranks?.find(r=>r.seat===seat)?.rank ?? ''}${c.ranks?'位':''}</span><span class="score-name">${esc(game.players[seat].name)}</span><span class="score-figure" data-score="${seat}">${figureHtml(seat)}</span></div>`).join('')}</div>`}
    ${c.label?`<p class="rank-caption">${c.label}</p>`:''}${c.phase==='ready' && game.finished ? `<p class="winner">${esc(winnerText(game.players,c.ranks))}</p>`:''}
    ${c.phase==='ready'?button(game.finished?'ゲームを終了':'OK',game.finished?'home':'next-round','paper wide',`style="--caption-length:${game.finished?7:2}"`):''}</section>`;
}
function figureHtml(seat) {
  const c=ceremony,b=c.breakdowns[seat];
  if (c.showPrevious) return `<span class="previous">${b.previous} + </span><span>${b.total}</span>${c.showSum?`<span class="sum"> = ${b.previous+b.total}</span>`:''}`;
  return String(c.values[seat]);
}
function personalHtml() { return field('放棄などに確認を求める','confirm',[[true,'YES'],[false,'NO']])+field('BGM音量','bgm',Array.from({length:6},(_,n)=>[n,n?String(n):'OFF']))+field('SE音量','se',Array.from({length:6},(_,n)=>[n,n?String(n):'OFF'])); }
function modalHtml() {
  if (!modal) return '';
  let content='';
  if (modal==='settings') content=`<h2>個人設定</h2>${personalHtml()}${button('OK','close-modal','primary wide')}`;
  else if (modal==='confirm') content=`<h2>確認</h2><p>${esc(pending?.label||'')}</p><div class="dialog-actions">${button('キャンセル','close-modal','paper')}${button('続ける','accept-confirm')}</div>`;
  else if (modal==='nominate') content=`<h2>CPUプレイヤーの指名 ${draft.length}/${settings.players}</h2><p class="dialog-note">タップした順に登録。同じ人は1枠まで選べます。</p><div class="nominees">${CPU_NAMES.map((name,id)=>`<button type="button" data-nominee="${id}" class="${draft.includes(id)?'chosen':''}" aria-pressed="${draft.includes(id)}"><b>${draft.includes(id)?draft.indexOf(id)+1+'. ':''}${name}</b><small>${CPU_DESCRIPTIONS[id]}</small></button>`).join('')}</div>${button('決定','accept-cast','primary wide',draft.length===settings.players?'':'disabled')}${button('キャンセル','close-modal','paper wide')}`;
  else if (modal==='how-to'||modal==='rules') {
    const text=guideTexts.get(modal);
    content=`<h2>${modal==='how-to'?'遊び方':'詳細ルール'}</h2><div class="guide-scroll">${modal==='how-to'?`<div class="slideshow"><img src="./assets/slides/slide${String(slide+1).padStart(2,'0')}.jpg" alt="遊び方 ${slide+1}ページ"><div class="slide-bottom"><span>${slide+1} / 9</span><div class="slide-controls">${button('⏪','slide-prev','paper','aria-label="前のページ"')}${button(slidePaused?'▶':'Ⅱ','slide-pause','paper',`aria-label="${slidePaused?'再生':'一時停止'}"`)}${button('⏩','slide-next','paper','aria-label="次のページ"')}</div></div></div>`:''}${text?guideHtml(text):'<p>読み込み中…</p>'}</div>${button('OK','close-modal','primary wide')}`;
  }
  return `<div class="shade" data-dismiss="true"><section class="dialog ${modal==='how-to'||modal==='rules'?'guide-dialog':''}" role="dialog" aria-modal="true" aria-label="${modal==='settings'?'個人設定':modal==='how-to'?'遊び方':modal==='rules'?'詳細ルール':'確認'}">${content}</section></div>`;
}
function render() {
  document.body.dataset.scene=page==='game'?'game':'setup';
  const scroll=root.querySelector('.guide-scroll')?.scrollTop || 0;
  const focused=document.activeElement?.dataset;
  root.innerHTML=page==='game'?gameHtml():page==='lobby'?lobbyHtml():menuHtml();
  const pane=root.querySelector('.guide-scroll'); if(pane) pane.scrollTop=scroll;
  if(modal&&modal!==renderedModal)root.querySelector('.dialog button:not(:disabled)')?.focus({preventScroll:true});
  else if(focused?.option)root.querySelector(`[data-option="${focused.option}"][data-value="${focused.value}"]`)?.focus({preventScroll:true});
  renderedModal=modal;
}
function resetToMenu() {
  runId++; clearTimeout(cpuTimer); clearInterval(slideTimer); sound.stopAll();
  document.querySelectorAll('.card-flyer,.chip-flyer').forEach(node=>node.remove());
  game=null; shown=null; ceremony=null; hidden.clear(); busy=false; modal=''; pending=null; notice=''; page='menu'; render();
}
async function start(spectator=false) {
  if(settings.bgm||settings.se)sound.unlock();
  settings.name=settings.name.trim()||'あなた';save();
  clearTimeout(cpuTimer); runId++; const token=runId;
  const rounds=settings.roundMode===0?1:settings.players*(settings.roundMode===2?2:1);
  game=new Game({playerCount:settings.players,rounds,name:settings.name,cpuIds:cast,spectator});
  bindCpuProfiles(game,cast,settings.strength); page='game'; modal=''; ceremony=null; titleVerdict=false; notice=''; busy=true; roundBase=game.players.map(p=>p.score);
  sound.music('play_bgm_02');
  const after=cloneState(game),before=cloneState(game); before.market=before.market.map(()=>null);before.deckCount+=after.market.length;
  await presentChange(before,after,token);
  if(!alive(token))return;
  shown=null;busy=false;render();queueCpu();
}
function queueCpu() {
  clearTimeout(cpuTimer);
  if(!game?.active()||busy||game.players[game.current].human)return;
  cpuTimer=setTimeout(()=>act(()=>{const seat=game.current, profile=cast[game.players.some(p=>p.human)?seat-1:seat];const action=chooseCpuAction(game,settings.strength,profile);notice=`${game.players[seat].name}：${({take:'ノルマを選択',collect:'カードを収集',abandon:'放棄',pass:'パス'})[action.type]}`;game.step(action);}),450);
}
async function act(mutate) {
  if(busy||!game?.active())return;
  busy=true;const token=runId;const frames=[cloneState(game)];
  const observe=()=>frames.push(cloneState(game));game.observers.add(observe);
  try { mutate(); }
  catch(error){notice=error.message;}
  finally {game.observers.delete(observe);}
  if(frames.length===1)frames.push(cloneState(game));
  for(let i=1;i<frames.length&&alive(token);i++)await presentChange(frames[i-1],frames[i],token);
  if(!alive(token))return;
  shown=null;hidden.clear();busy=false;render();
  if(!game.active())await runCeremony(token);else queueCpu();
}
async function fly(html,from,to,duration=330,back=false) {
  if(!from||!to||reducedMotion())return;
  const node=document.createElement('div');node.className='card-flyer';node.innerHTML=back?'<div class="card back"></div>':html;
  Object.assign(node.style,{left:from.left+'px',top:from.top+'px',width:from.width+'px',height:from.height+'px'});document.body.append(node);
  const animation=node.animate([{transform:'translate(0,0) scale(1)'},{transform:`translate(${to.left-from.left}px,${to.top-from.top}px) scale(${to.width/from.width},${to.height/from.height})`}],{duration,easing:'cubic-bezier(.2,.65,.25,1)',fill:'forwards'});
  try{await animation.finished;}catch{}node.remove();
}
function rect(selector){return root.querySelector(selector)?.getBoundingClientRect();}
async function presentChange(before,after,token) {
  if(!alive(token))return;
  shown=before;hidden.clear();render();
  const sources=new Map(before.market.filter(Boolean).map(card=>[card.id,{
    rect:rect(`.market [data-card-id="${card.id}"]`),
    html:root.querySelector(`.market [data-card-id="${card.id}"]`)?.outerHTML,
  }]));
  const oldOwned=new Set(before.players.flatMap(p=>[...(p.quota?[p.quota]:[]),...p.collection,...p.achieved]).map(c=>c.id));
  const taken=after.players.flatMap((p,seat)=>[...(p.quota?[p.quota]:[]),...p.collection,...p.achieved]
    .filter(c=>!oldOwned.has(c.id)).map(card=>({card,seat})));
  const replacing=after.market.map((card,i)=>({card,i})).filter(({card,i})=>card&&card.id!==before.market[i]?.id);
  const completed=after.players.flatMap((p,seat)=>p.achieved.length>before.players[seat].achieved.length
    ? [{seat,cards:p.achieved.slice(before.players[seat].achieved.length)}] : []);
  const redeal=after.plan==='reshuffle'&&before.plan!=='reshuffle'||after.reshuffleCount>before.reshuffleCount;
  if(redeal){
    const deck=rect('[data-deck]');
    await Promise.all([...sources.values()].map(src=>fly(src.html,src.rect,deck,220,true)));
    if(!alive(token))return;
  }
  const arrivals=structuredClone(after);
  for(const {seat,cards}of completed){
    // The final collected card lands in the quota area before the whole bundle
    // moves to achievements. Keep its score and chips pending until that move.
    Object.assign(arrivals.players[seat],{quota:cards[0],collection:cards.slice(1),
      achieved:before.players[seat].achieved,bundles:before.players[seat].bundles,score:before.players[seat].score});
  }
  const deckCount=before.deckCount+(redeal?before.market.filter(Boolean).length:0);
  arrivals.deckCount=deckCount;
  hidden=new Set([...taken.map(t=>t.card.id),...replacing.map(t=>t.card.id)]);shown=arrivals;render();
  for(const {card,seat}of taken){
    if(!alive(token))return;
    const src=sources.get(card.id);
    await fly(src?.html||cardHtml(card),src?.rect||rect('[data-deck]'),rect(`[data-player="${seat}"] [data-card-id="${card.id}"]`),300);
    if(!alive(token))return;
    hidden.delete(card.id);sound.se('card_03');
    root.querySelector(`[data-player="${seat}"] [data-card-id="${card.id}"]`)?.classList.remove('concealed');
  }
  if(completed.length){
    const moves=completed.flatMap(({seat,cards})=>cards.map(card=>({seat,card,
      from:rect(`[data-player="${seat}"] [data-card-id="${card.id}"]`),
      html:root.querySelector(`[data-player="${seat}"] [data-card-id="${card.id}"]`)?.outerHTML,
    })));
    hidden=new Set([...replacing.map(t=>t.card.id),...moves.map(t=>t.card.id)]);
    shown={...after,deckCount};render();
    await Promise.all(moves.map(move=>fly(move.html,move.from,rect(`[data-player="${move.seat}"] [data-card-id="${move.card.id}"]`),240)));
    if(!alive(token))return;
    for(const move of moves)hidden.delete(move.card.id);
    render();
  }
  let remaining=deckCount;
  for(const {card}of replacing){
    if(!alive(token))return;
    const deckLabel=root.querySelector('[data-deck] strong');if(deckLabel)deckLabel.textContent=String(--remaining);
    sound.se('card_01');
    await fly(cardHtml(card),rect('[data-deck]'),rect(`.market [data-card-id="${card.id}"]`),300,true);
    if(!alive(token))return;
    hidden.delete(card.id);root.querySelector(`.market [data-card-id="${card.id}"]`)?.classList.remove('concealed');sound.se('card_02');
  }
  hidden.clear();
}
function updateTray(seat){const tray=root.querySelector(`[data-tray="${seat}"]`);if(tray)tray.innerHTML=ceremony.trays[seat].map(tokenHtml).join('');}
async function flyChip(color,from,to){if(!from||!to||reducedMotion())return;const node=document.createElement('span');node.className=`chip chip-flyer ${color}`;Object.assign(node.style,{left:from.left+from.width/2+'px',top:from.top+from.height/2+'px'});document.body.append(node);const a=node.animate([{transform:'translate(0,0) rotate(0deg)'},{transform:`translate(${to.left+to.width/2-from.left-from.width/2}px,${to.top+to.height/2-from.top-from.height/2}px) rotate(180deg)`}],{duration:360,easing:'ease-in-out',fill:'forwards'});try{await a.finished;}catch{}node.remove();}
async function moveTitles(token){
  const c=ceremony,jobs=c.awards.flatMap((a,i)=>Array.from({length:a.points},(_,n)=>({award:a,index:i,n})));
  if(!jobs.length)return;
  sound.count();await Promise.all(jobs.map(async(job,i)=>{await sleep(i*65);if(!alive(token))return;const a=job.award;await flyChip('blue',rect(`[data-award="${job.index}"]`),rect(`[data-tray="${a.seat}"]`));if(!alive(token))return;c.trays[a.seat].push({color:'blue',key:`${a.seat}-title-${job.index}-${job.n}`});updateTray(a.seat);if(job.n===a.points-1)root.querySelector(`[data-award="${job.index}"]`)?.classList.add('gone');}));sound.stopCount();
}
async function moveScores(token){
  const c=ceremony,jobs=c.trays.flatMap((tokens,seat)=>tokens.map((chip,i)=>({chip,seat,wave:i})));
  if(!jobs.length)return;
  sound.count();await Promise.all(jobs.map(async job=>{await sleep(job.wave*80);if(!alive(token))return;const source=root.querySelector(`[data-chip="${job.chip.key}"]`);const from=source?.getBoundingClientRect();if(source)source.style.visibility='hidden';await flyChip(job.chip.color,from,rect(`[data-score="${job.seat}"]`));if(!alive(token))return;c.values[job.seat]++;c.trays[job.seat]=c.trays[job.seat].filter(t=>t.key!==job.chip.key);source?.remove();const target=root.querySelector(`[data-score="${job.seat}"]`);if(target)target.textContent=String(c.values[job.seat]);}));sound.stopCount();
}
async function sortScores(scores,token){
  const positions=new Map([...root.querySelectorAll('[data-score-row]')].map(el=>[Number(el.dataset.scoreRow),el.getBoundingClientRect()]));
  ceremony.ranks=scoreRanks(scores,ceremony.order);ceremony.order=ceremony.ranks.map(r=>r.seat);render();
  const motions=[];
  for(const el of root.querySelectorAll('[data-score-row]')){const before=positions.get(Number(el.dataset.scoreRow));if(before&&!reducedMotion())motions.push(el.animate([{transform:`translateY(${before.top-el.getBoundingClientRect().top}px)`},{transform:'translateY(0)'}],{duration:550,easing:'ease-in-out'}).finished.catch(()=>{}));}
  await Promise.all(motions);return alive(token);
}
async function runCeremony(token){
  clearTimeout(cpuTimer);modal='';pending=null;titleVerdict=true;
  const breakdowns=game.players.map((p,i)=>roundBreakdown(p,roundBase[i]));
  ceremony={phase:'verdict',order:displayOrder(game),breakdowns,values:breakdowns.map(b=>b.base),trays:breakdowns.map((b,i)=>makeTokens(i,b.green,b.purple)),awards:displayOrder(game).flatMap(seat=>titleAwards(game.players[seat]).map(a=>({...a,seat})))};
  sound.stopMusic(.5);render();await sleep(1000);if(!alive(token))return;
  ceremony.phase='reason';sound.music('end_bgm_01');render();await sleep(1000);if(!alive(token))return;
  if(ceremony.awards.length){ceremony.phase='titles';render();await sleep(350);if(!alive(token))return;await moveTitles(token);await sleep(600);if(!alive(token))return;}
  ceremony.phase='scores';render();await sleep(500);if(!alive(token))return;await moveScores(token);if(!alive(token))return;
  await sortScores(breakdowns.map(b=>b.total),token);await sleep(1200);if(!alive(token))return;
  if(game.roundIndex>=2){ceremony.showPrevious=true;render();await sleep(1000);if(!alive(token))return;ceremony.showSum=true;render();await sleep(1000);if(!alive(token))return;await sortScores(breakdowns.map(b=>b.previous+b.total),token);ceremony.label=game.finished?'最終順位':'暫定順位';}
  if(game.finished){await sleep(1000);if(!alive(token))return;sound.music('end_bgm_03');}
  ceremony.phase='ready';render();
  if(!game.finished&&!game.players.some(p=>p.human)){await sleep(2000);if(alive(token)&&ceremony?.phase==='ready')nextRound();}
}
async function nextRound(){
  if(busy||!game?.awaitingNextRound||ceremony?.phase!=='ready')return;
  busy=true;const token=runId;sound.stopMusic(.5);await sleep(500);if(!alive(token))return;
  game.beginNextRound();roundBase=game.players.map(p=>p.score);ceremony=null;titleVerdict=false;notice='';sound.music('play_bgm_02');
  const after=cloneState(game),before=cloneState(game);before.market=before.market.map(()=>null);before.deckCount+=after.market.length;
  await presentChange(before,after,token);if(!alive(token))return;shown=null;busy=false;render();queueCpu();
}
function confirmAction(label,run,always=false){if(!settings.confirm&&!always){run();return;}pending={label,run};modal='confirm';render();}
function closeModal(){modal='';pending=null;clearInterval(slideTimer);render();queueCpu();}
function scheduleSlides(){clearInterval(slideTimer);if(modal==='how-to'&&!slidePaused)slideTimer=setInterval(()=>{slide=(slide+1)%9;updateSlide();},5000);}
function updateSlide(){const img=root.querySelector('.slideshow img');if(img){img.src=`./assets/slides/slide${String(slide+1).padStart(2,'0')}.jpg`;img.alt=`遊び方 ${slide+1}ページ`;root.querySelector('.slide-bottom > span').textContent=`${slide+1} / 9`;}}
async function openGuide(which){modal=which;slide=0;slidePaused=false;render();scheduleSlides();if(!guideTexts.has(which)){try{const r=await fetch(`./assets/copy/${which==='how-to'?'how-to-play':'detailed-rules'}.txt`);if(!r.ok)throw Error();guideTexts.set(which,await r.text());}catch{guideTexts.set(which,'【読み込み】\n- 説明を読み込めませんでした。通信を確認してもう一度開いてください。');}if(modal===which)render();}}
root.addEventListener('click',event=>{
  if(event.target.matches('[data-dismiss]')){closeModal();return;}
  const option=event.target.closest('[data-option]');
  if(option){const key=option.dataset.option;if(!['players','roundMode','strength','confirm','bgm','se'].includes(key))return;settings[key]=key==='confirm'?option.dataset.value==='true':Number(option.dataset.value);if(key==='players'&&cast.length<settings.players)cast.push(...randomCpuIds(6).filter(id=>!cast.includes(id)).slice(0,settings.players-cast.length));save();sound.update();render();return;}
  const nominee=event.target.closest('[data-nominee]');if(nominee){const id=Number(nominee.dataset.nominee);if(draft.includes(id))draft=draft.filter(x=>x!==id);else if(draft.length<settings.players)draft.push(id);render();return;}
  const card=event.target.closest('[data-card]');
  if(card&&game?.active()&&!busy&&!modal&&game.players[game.current].human){const id=Number(card.dataset.card),p=game.players[game.current];const action=p.quota?{type:'collect',ids:[id]}:{type:'take',id};if(game.legal(action))act(()=>{notice='';game.step(action);});return;}
  const target=event.target.closest('[data-action]');if(!target||target.disabled)return;const action=target.dataset.action;
  if(action==='lobby'){page='lobby';render();}
  else if(action==='menu'){page='menu';render();}
  else if(action==='how-to'||action==='rules')openGuide(action);
  else if(action==='settings'){modal='settings';render();}
  else if(action==='close-modal')closeModal();
  else if(action==='shuffle-cpu'){cast=randomCpuIds(settings.players);save();render();}
  else if(action==='nominate'){draft=[];modal='nominate';render();}
  else if(action==='accept-cast'&&draft.length===settings.players){cast=[...draft];save();closeModal();}
  else if(action==='start'||action==='simulate')start(action==='simulate');
  else if(action==='accept-confirm'){const run=pending?.run;pending=null;modal='';if(run)run();}
  else if(action==='exit')confirmAction('対局を終了してスタートメニューに戻りますか？',()=>resetToMenu());
  else if(action==='pass')confirmAction('パスしますか？',()=>act(()=>game.step({type:'pass'})));
  else if(action==='abandon')confirmAction('ノルマ札と収集札を放棄しますか？',()=>act(()=>game.step({type:'abandon'})));
  else if(action==='double')act(()=>{game.declareDouble();notice='ダブル：2回続けて行動できます';});
  else if(action==='cancel-double')act(()=>{game.cancelDouble();notice='ダブルの宣言を取り消しました';});
  else if(action==='reshuffle')act(()=>{game.declareReshuffle();notice='場札を配り直しました';});
  else if(action==='next-round')nextRound();
  else if(action==='home'){sound.stopMusic(.5);setTimeout(()=>resetToMenu(),500);}
  else if(action==='slide-prev'||action==='slide-next'){slide=(slide+(action==='slide-prev'?8:1))%9;updateSlide();scheduleSlides();}
  else if(action==='slide-pause'){slidePaused=!slidePaused;render();scheduleSlides();}
});
root.addEventListener('input',event=>{if(event.target.id==='player-name'){settings.name=event.target.value.slice(0,24);save();}});
root.addEventListener('change',event=>{if(event.target.id==='player-name'){settings.name=settings.name.trim()||'あなた';save();}});
document.addEventListener('keydown',event=>{
  if(event.key==='Escape'&&modal){closeModal();return;}
  if(event.key==='Tab'&&modal){const items=[...root.querySelectorAll('.dialog button:not(:disabled),.dialog input')];const first=items[0],last=items.at(-1);if(event.shiftKey&&document.activeElement===first){event.preventDefault();last?.focus();}else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first?.focus();}}
});
render();
if('serviceWorker'in navigator){let hadController=!!navigator.serviceWorker.controller;navigator.serviceWorker.addEventListener('controllerchange',()=>{if(hadController&&!game)location.reload();hadController=true;});navigator.serviceWorker.register('./sw.js',{updateViaCache:'none'}).catch(()=>{});}
