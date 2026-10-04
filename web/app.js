const app = document.querySelector("#app");
let state = null;
let seenEvent = 0;
const scoreAnim = new Map();
let scoreTimer = 0;
let marketSlots = [];
let pendingMarket = null;
const marksTaken = new Set();
const tallyScores = new Map();
let tally = null;
let watched = false;
const recordSeen = new Set();
let recordPrimed = false;
let gathering = false;
let bonusCashed = false;
const lockedScore = new Map();

let hiding = new Set();
let inFlight = new Set();
let parked = new Set();
let pendingBonus = 0;
let pendingBonusSeat = null;
let bonusNote = null;
let titleCheer = null;
let guide = null;
let settingsOpen = false;
let settingsDraft = null;
let ask = null;
let coverSeen = 0;
let coverUntil = 0;
let coverText = "";
let rosterNote = "";
let standardOffer = false;
const splash = document.querySelector("#splash");
if (splash) {
  document.body.classList.add("booting");
  setTimeout(() => {
    splash.classList.add("out");
    setTimeout(() => {
      splash.remove();
      document.body.classList.remove("booting");
      document.body.classList.add("dim");
    }, 600);
  }, 3000);
}
let turnLeft = null;
let turnLeftAt = 0;

function cardHtml(card, z = 1, coins = null, compact = false) {
  const id = String(card.id);
  const hidden = hiding.has(id) || inFlight.has(id) ? "incoming" : "";
  const fresh = compact && recordPrimed && !recordSeen.has(id) ? " just-in" : "";
  if (compact && recordPrimed) recordSeen.add(id);
  const wide = card.face && [...card.face].length > 2 ? " wide" : "";
  const rank = card.face ? `<div class="rank${wide}" style="color:${card.color}">${card.face}</div>` : "";
  const pile = !compact && coins && coins.length
    ? `<div class="coins">${coins.map((coin) => `<i class="coin ${coin.kind}" data-id="${coin.id}" data-kind="${coin.kind}"></i>`).join("")}</div>`
    : "";
  return `<div class="card ${card.joker ? "joker" : ""} ${hidden}${fresh}" data-id="${card.id}" style="z-index:${z}">
    ${rank}
    ${iconHtml(card)}
    ${goodsHtml(card, compact)}
    ${pile}
  </div>`;
}

function iconHtml(card) {
  if (card.image) return `<img class="goods-icon" src="${card.image}" alt="">`;
  return `<div class="emoji">${card.emoji || ""}</div>`;
}

function goodsHtml(card, compact = false) {
  const n = [...card.goods].length;
  const scale = n >= 5 ? 0.168 : n === 4 ? 0.2 : 0.24;
  const width = compact ? "var(--card-w) * 0.68" : "var(--card-w)";
  return `<div class="goods" style="font-size:calc((${width}) * ${scale})">${card.goods}</div>`;
}

function baseScore(player) {
  let total = 0;
  let index = 0;
  const cards = player && player.achieved ? player.achieved : [];
  while (index < cards.length) {
    const rank = cards[index].rank;
    if (!rank) break;
    total += rank;
    index += rank;
  }
  return total;
}

const titleReady = new Set();
const coinHold = new Set();

function scatter(id, index) {
  let hash = 2166136261;
  const text = `${id}:${index}`;
  for (let i = 0; i < text.length; i += 1) hash = Math.imul(hash ^ text.charCodeAt(i), 16777619);
  const next = () => {
    hash = Math.imul(hash ^ (hash >>> 16), 2246822507);
    hash = Math.imul(hash ^ (hash >>> 13), 3266489909);
    return ((hash >>> 0) % 1000) / 1000;
  };
  // Average of two uniforms: still scattered, denser toward the middle of the tray.
  const centered = () => (next() + next()) / 2;
  return { x: 6 + centered() * 78, y: 8 + centered() * 62 };
}

function coinPlan(player, index) {
  const recorded = (player.achieved || []).filter((card) => !parked.has(String(card.id)));
  const open = [];
  if (player.quota) open.push(player.quota, ...(player.collection || []));
  const parkedCards = (player.achieved || []).filter((card) => parked.has(String(card.id)));
  const onCard = new Map();
  const bank = [];
  const add = (card, kind, count, banked) => {
    if (!card || count <= 0) return;
    for (let n = 0; n < count; n += 1) {
      const id = `${card.id}-${kind}-${n}`;
      if (banked) bank.push({ kind, id });
      else {
        const key = String(card.id);
        const pile = onCard.get(key) || [];
        pile.push({ kind, id });
        onCard.set(key, pile);
      }
    }
  };
  const greens = (cards, banked) => {
    let cursor = 0;
    while (cursor < cards.length) {
      const rank = cards[cursor] && cards[cursor].rank;
      if (!rank) break;
      add(cards[cursor], "green", achievementCoins(rank), banked);
      cursor += rank;
    }
  };
  greens(recorded, true);
  greens(open, false);
  greens(parkedCards, false);
  if (state && state.sequence_rule) {
    const line = recorded.concat(parkedCards, open);
    const waiting = new Set(parkedCards.concat(open).map((card) => card.id));
    for (let i = 1; i < line.length; i += 1) {
      const prev = line[i - 1];
      const card = line[i];
      if (!prev || !card || prev.joker || card.joker || prev.rank == null || card.rank == null) continue;
      const purple = prev.rank === card.rank ? 2 : Math.abs(prev.rank - card.rank) === 1 ? 1 : 0;
      if (purple) add(card, "purple", purple, !waiting.has(card.id));
    }
  }
  if (state && state.finished && titleCoinsVisible(index)) {
    let points = 0;
    for (const title of player.titles || []) points += title.points || 0;
    for (let n = 0; n < points; n += 1) bank.push({ kind: "blue", id: `title-${index}-${n}` });
  }
  return { onCard, bank };
}

function titleCoinsVisible(index) {
  if (!state || !state.finished) return false;
  if (titleReady.has(index)) return true;
  if (!watched) return true;
  if (bonusCashed) return true;
  return !!(tally && tally.phase === "done");
}

function achievementCoins(rank) {
  if (rank >= 13) return 6;
  if (rank >= 10) return 3;
  if (rank >= 7) return 1;
  return 0;
}

function trayTitles(player) {
  if (!state || !state.title_rule) return "";
  const voids = player.title_void || {};
  const mark = (key, name) => `<span class="tray-title${voids[key] ? " void" : ""}" data-title="${key}">${name}</span>`;
  return `<div class="tray-titles">${mark("mono", "単色達成")}${mark("purist", "生粋の買い付け")}</div>`;
}

function trayPile(index, coins) {
  if (ceremony && ceremony.review) return [];
  if (ceremony && ceremony.coins.has(index)) return ceremony.coins.get(index);
  return coins.bank;
}

function trayHtml(bank) {
  const coins = bank.map((coin, index) => {
    const spot = scatter(coin.id, index);
    const hidden = coinHold.has(coin.id) ? "opacity:0;" : "";
    return `<i class="coin ${coin.kind}" data-id="${coin.id}" data-kind="${coin.kind}" style="left:${spot.x}%;top:${spot.y}%;z-index:${index + 1};${hidden}"></i>`;
  }).join("");
  return `<div class="tray-coins">${coins}</div>`;
}

function guideRich(text) {
  return escapeText(text).replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");
}

const GUIDES = {
  quick: {
    title: "QuickStartガイド",
    blocks: [
      { list: [
        "場札から商品のカードを1枚選んで、**ノルマ札**にする。",
        "次の自分の手番から、場札にある**同じ種類**の札を集める。**ワイルド**（万能札。標準カードセットでは「金貨」のカード）はどの種類の代わりにも使える。",
        "ノルマ札に書かれた数字の枚数がそろったら**達成**！",
        "**山札**がなくなったら、そのラウンドは終了。シンプルモードは1ラウンド、標準ルールは人数と同じ回数のラウンド。得点の合計が一番多い人の勝ち。",
      ] },
      { note: "まずはこれだけ覚えれば遊べる。細かいルールは「ルール」を見る。" },
    ],
  },
  rules: {
    title: "ルール",
    blocks: [
      { note: "「進め方」「得点計算」の中で「（シンプルルールでは無し）」と記した項目は、設定で**シンプルモード**を選ぶと使わない。それ以外はモードによらず共通のルールである。" },
      { heading: "進め方" },
      { list: [
        "**場札**から1枚選び、**ノルマ札**にする。",
        "ノルマ札と同じ種類の札を、書かれた数字の枚数だけ集めたら**達成**。",
        "集める札は、場札から何枚取ってもよい。",
        "**ワイルド**はどの種類の代わりにもなる。ただし、ワイルド（万能札。標準カードセットでは「金貨」のカード）はノルマ札にすることはできない。",
        "達成が難しければ、ノルマ札を**放棄**して選び直せる。放棄しても手番は終わらず、同じ手番で新しいノルマ札を選べる。",
        "取れる札も取りたい札もなければ**パス**。",
        "全員が続けてパスしたら**配り直し**。配り直した後も誰も取れなければ、そこで終了。",
        "数を減らした**場札**は、次のプレイヤーの手番になるタイミングで**山札**（裏向きの残りカードの束）から自動で補充される。数えて減っていても気にしなくてよい。",
        "**配り直し**では、**場札**をすべて**山札**に戻してシャッフルし、同じ枚数を並べ直す。中身は総入れ替えになる。",
        "**山札**がなくなったら、そのラウンドは終了。シンプルモードはそこでゲーム終了。標準ルールは人数と同じ回数だけラウンドを行い、得点は累計する。",
        "カードは(4種類×13＋**ワイルド**2)×2セット＝108枚。うち8枚をランダムに除外して遊ぶ。",
        "各プレイヤーは、ラウンドごとに1回だけ、手番の最初に**配り直し**をしてから行動できる（ノルマの有無は問わない）。**（シンプルルールでは無し）**",
        "各プレイヤーは、ラウンドごとに1回だけ、手番の最初に宣言して「行動→補充→もう1行動」の**ダブルアクション**ができる。この2つは同じ手番では併用不可。**（シンプルルールでは無し）**",
      ] },
      { heading: "得点計算" },
      { list: [
        "**達成**すると、集めた枚数がそのまま得点になる。",
        "7枚以上の達成には、枚数に応じて達成ボーナスがつく。",
      ] },
      { table: [
        ["達成した枚数", "達成ボーナス"],
        ["7枚", "＋1点"],
        ["8枚", "＋1点"],
        ["9枚", "＋1点"],
        ["10枚", "＋3点"],
        ["11枚", "＋3点"],
        ["12枚", "＋3点"],
        ["13枚", "＋6点"],
      ] },
      { note: "（6枚以下はボーナスなし）" },
      { list: [
        "達成の記録の並びで、前後する数字なら＋1点、同じ数字なら＋2点（組をまたいでもよい）。ラウンド終了時に加算する。**（シンプルルールでは無し）**",
        "**ワイルド**不使用で3組以上達成したら、ラウンド終了時に＋5点。**（シンプルルールでは無し）**",
        "同じ種類だけ（ワイルドは可）で3組以上達成したら、ラウンド終了時に＋15点。**（シンプルルールでは無し）**",
      ] },
      { heading: "こんなときは？" },
      { list: [
        "**狙っている種類が場になかなか出ない。どうする？** → パスして待ってもよいし、ノルマ札を放棄して選び直してもよい。焦る必要はない。",
        "**ワイルドを使ってノルマを達成した。何か損する？** → 損はしないが、シンプルルール以外では「ワイルド不使用ボーナス」（3組以上達成で＋5点）の対象から外れる。",
        "**違う種類のノルマを達成してしまった。単色ボーナスはもうもらえない？** → もらえない。単色達成ボーナスは、3組以上のノルマ達成すべてが同じ種類でないと成立しない。",
        "**配り直し・ダブルアクションは、同じ手番で両方使える？** → 使えない。どちらか一方のみ。ただし1ラウンドの中でなら、別々の手番でそれぞれ1回ずつ使える。次のラウンドでは、また1回ずつ使える。",
        "**山札がちょうど尽きたタイミングで手番が回ってきた。何もできない？** → その手番は行動せずに、そのラウンドは終了となる。それまでに達成した分の得点はそのまま有効。標準ルールでは、次のラウンドが残っていれば続ける。",
      ] },
    ],
  },
  hint: {
    title: "勝つためのヒント",
    blocks: [
      { list: [
        "**小さすぎず大きすぎないノルマ（4〜8あたり）を軸にする。** 数字が大きいほど1回の得点は魅力的だが、そろわないまま終盤を迎えるリスクも上がる。",
        "**達成ボーナス狙いの大きなノルマ（10以上）は、賭けと割り切る。** 決まれば一気に差がつくが、頼りすぎると得点源が偏る。",
        "**ワイルドは、自分のためだけとは限らない。** 誰かの大きなノルマの決め手になりそうなら、急いで自分に使わず、あえて場に残しておく判断もある。",
        "**他のプレイヤーが選んでいる種類を把握しておく。** 同じ種類を避けて選べば、奪い合いを避けやすい。",
        "**単色達成ボーナスを狙うなら、早めに1つの種類に決め打ちする。** 序盤から種類を絞る覚悟が要る。途中で他の種類に手を出すと条件が崩れる。",
        "**ワイルド不使用ボーナスは、小さなノルマを積み重ねる戦い方と相性が良い。** 大きなノルマほどワイルドに頼りやすくなるため、両立は難しい。",
        "**並び順ボーナスを意識するなら、達成する順番も考える。** 同じ数字や前後の数字が続けて達成できるよう、ノルマ札を選ぶ順番を工夫する。",
        "**特殊アクションは、ここぞという場面まで温存する。** 配り直し＆取得は市場に恵まれないときの立て直しに、ダブルアクションは大きなノルマの残り数枚がそろった瞬間に使うと効果的。",
        "**見切りも実力のうち。** 必要な種類がもう出回っていないと感じたら、放棄して同じ手番で次のノルマに切り替える。",
      ] },
    ],
  },
};

function guideBlock(block) {
  if (block.heading) return `<h3>${escapeText(block.heading)}</h3>`;
  if (block.note) return `<p class="lead">${guideRich(block.note)}</p>`;
  if (block.list) return `<ul>${block.list.map((line) => `<li>${guideRich(line)}</li>`).join("")}</ul>`;
  if (block.table) {
    const [head, ...rows] = block.table;
    const cells = (tag, items) => items.map((item) => `<${tag}>${escapeText(item)}</${tag}>`).join("");
    return `<table><thead><tr>${cells("th", head)}</tr></thead><tbody>${rows.map((row) => `<tr>${cells("td", row)}</tr>`).join("")}</tbody></table>`;
  }
  return "";
}

function playerNameFrom(data) {
  const name = String(data.get("player_name") || "").trim();
  return name || "あなた";
}

function startOptions(players, leftHanded) {
  const saved = savedOptions() || {};
  const simple = simpleOn(saved);
  return {
    players,
    simple,
    sequence: !simple,
    title: !simple,
    special: !simple,
    ok_timeout: savedOkTimeout(saved),
    ok_timeout_set: true,
    turn_timeout: Number(saved.turn_timeout ?? 120),
    left_handed: leftHanded,
  };
}

function applySettings() {
  if (!settingsDraft) return;
  const simpleBox = app.querySelector("#simple-mode");
  const okInput = app.querySelector("#ok-timeout");
  const turnInput = app.querySelector("#turn-timeout");
  const simple = simpleBox ? simpleBox.checked : settingsDraft.simple;
  const ok = Number(okInput ? okInput.value : settingsDraft.ok);
  const turn = Number(turnInput ? turnInput.value : settingsDraft.turn);
  if (!Number.isFinite(ok) || ok < 0 || !Number.isFinite(turn) || turn < 1) return;
  const form = app.querySelector("#start");
  const data = form ? new FormData(form) : null;
  const players = data ? Number(data.get("players")) : Number((savedOptions() || {}).players || 3);
  const leftHanded = data ? data.get("left_handed") === "on" : !!(savedOptions() || {}).left_handed;
  const options = {
    players,
    simple,
    sequence: !simple,
    title: !simple,
    special: !simple,
    ok_timeout: ok,
    ok_timeout_set: true,
    turn_timeout: turn,
    left_handed: leftHanded,
  };
  rememberSimple(simple);
  rememberOptions(options);
  settingsOpen = false;
  settingsDraft = null;
  render();
}

function settingsHtml() {
  if (!settingsOpen || !settingsDraft) return "";
  return `<div class="rollover settings"><div class="panel">
    <h2>設定</h2>
    <label class="check">
      <input id="simple-mode" type="checkbox" ${settingsDraft.simple ? "checked" : ""}>
      シンプルモード
      <span id="simple-state">${settingsDraft.simple ? "オン" : "オフ"}</span>
    </label>
    <div class="row">
      <label>OKタイムアウト（秒）
        <input id="ok-timeout" class="short" type="number" min="0" step="0.5" value="${settingsDraft.ok}">
      </label>
      <label>手番タイムアウト（秒）
        <input id="turn-timeout" class="short" type="number" min="1" step="1" value="${settingsDraft.turn}">
      </label>
    </div>
    <p class="ask-buttons">
      <button type="button" class="primary" id="settings-save">決定</button>
      <button type="button" id="settings-cancel">キャンセル</button>
    </p>
  </div></div>`;
}

function guideHtml() {
  const page = GUIDES[guide];
  if (!page) return "";
  const body = page.blocks.map(guideBlock).join("");
  return `<div class="rollover guide"><div class="panel"><h2>${escapeText(page.title)}</h2><div class="guide-body">${body}</div><p><button type="button" class="primary" id="guide-ok">OK</button></p></div></div>`;
}

function escapeAttr(value) {
  return String(value).replace(/[&"<>]/g, (ch) => ({ "&": "&amp;", '"': "&quot;", "<": "&lt;", ">": "&gt;" }[ch]));
}

function escapeText(value) {
  return String(value).replace(/[&<>]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[ch]));
}

function savedName() {
  const match = document.cookie.match(/(?:^|; )quota_name=([^;]*)/);
  if (!match) return "あなた";
  try {
    const name = decodeURIComponent(match[1]).trim();
    return name || "あなた";
  } catch {
    return "あなた";
  }
}

function rememberName(name) {
  document.cookie = `quota_name=${encodeURIComponent(name)}; Path=/; Max-Age=31536000; SameSite=Lax`;
}

function rememberOptions(options) {
  document.cookie = `quota_options=${encodeURIComponent(JSON.stringify(options))}; Path=/; Max-Age=31536000; SameSite=Lax`;
}

function noteFinishedGame() {
  if (!state || !state.finished || !state.table_id || !state.you || state.you.observer || state.you.seat == null) return;
  try {
    if (localStorage.getItem("quota.advancedPrompted") === "1") return;
    let count = Number(localStorage.getItem("quota.finishedGames") || "0");
    if (localStorage.getItem("quota.countedTable") !== state.table_id) {
      count += 1;
      localStorage.setItem("quota.finishedGames", String(count));
      localStorage.setItem("quota.countedTable", state.table_id);
    }
    if (count >= 3 && simpleOn(savedOptions() || {})) standardOffer = true;
  } catch {
    standardOffer = false;
  }
}

function standardOfferHtml() {
  if (!standardOffer) return "";
  return `<div class="rollover standard-offer"><div class="panel">
    <p>シンプルモードをオフにして標準ルールに戻しますか？</p>
    <p class="ask-buttons"><button type="button" id="standard-yes">はい</button><button type="button" id="standard-no">いいえ</button></p>
  </div></div>`;
}

function dismissStandardOffer(enable) {
  standardOffer = false;
  try {
    localStorage.setItem("quota.advancedPrompted", "1");
  } catch {
    /* the choice still closes the dialog for this view */
  }
  if (enable) {
    const saved = savedOptions() || {};
    rememberSimple(false);
    saved.simple = false;
    saved.sequence = true;
    saved.title = true;
    saved.special = true;
    rememberOptions(saved);
  }
  render();
}

function simpleOn(saved) {
  try {
    const stored = localStorage.getItem("quota.simple");
    if (stored === "0") return false;
    if (stored === "1") return true;
  } catch {
    /* fall through to the saved options */
  }
  if (Object.prototype.hasOwnProperty.call(saved, "simple")) return !!saved.simple;
  return true;
}

function rememberSimple(on) {
  try {
    localStorage.setItem("quota.simple", on ? "1" : "0");
  } catch {
    /* the submitted options still record the choice */
  }
}

function savedOkTimeout(saved) {
  if (!saved || !saved.ok_timeout_set) return 5;
  const value = Number(saved.ok_timeout);
  return Number.isFinite(value) ? value : 5;
}

function savedOptions() {
  const match = document.cookie.match(/(?:^|; )quota_options=([^;]*)/);
  if (!match) return null;
  try {
    return JSON.parse(decodeURIComponent(match[1]));
  } catch {
    return null;
  }
}

function syncDim() {
  const atStart = !state || state.phase === "hall" || state.phase === "recruiting";
  if (!atStart) document.body.classList.remove("dim");
  else if (!document.querySelector("#splash")) document.body.classList.add("dim");
}

function render() {
  syncDim();
  if (ask && ask.kind !== "leave" && !state.your_turn) ask = null;
  if (!state || state.phase === "hall") {
    const saved = savedOptions() || {};
    const players = String(saved.players || 3);
    app.innerHTML = `
      <header class="hero">
        <img class="hero-title" src="/title1.png" alt="QUOTA 揃えて、達成。">
        <img class="hero-catch" src="/title2.png" alt="ノルマは、自分で決めろ。">
      </header>
      <p class="guide-buttons">
        <button type="button" data-guide="quick">QuickStartガイド</button>
        <button type="button" data-guide="rules">ルール</button>
        <button type="button" data-guide="hint">勝つためのヒント</button>
      </p>
      ${guideHtml()}
      ${settingsHtml()}
      <form class="panel" id="start">
        <div class="row">
          <label>人数
            <select name="players">
              ${[3, 4].map((n) => `<option value="${n}" ${String(n) === players ? "selected" : ""}>${n}</option>`).join("")}
            </select>
          </label>
        </div>
        <div class="row">
          <label>あなたの名前
            <input name="player_name" maxlength="24" autocomplete="nickname" value="${escapeAttr(savedName())}">
          </label>
        </div>
        <div class="row option-gap">
          <label class="check"><span>左利き</span>
            <input name="left_handed" type="checkbox" ${saved.left_handed ? "checked" : ""}> ボタンを左に置く
          </label>
        </div>
        <p class="submit"><button type="button" id="open-settings">設定</button></p>
        <p class="submit"><button class="primary" type="submit">卓を新設</button></p>
        <p class="submit"><button type="button" id="watch-cpu">CPU模擬戦を観戦</button></p>
      </form>
      <section class="panel">
        <h2>参加できる卓</h2>
        ${(state.tables || []).length ? (state.tables || []).map((table) => {
          const ended = table.status === "ゲーム終了";
          const openSeat = table.status === "募集中" && table.seated < table.players;
          const label = ended ? "見る" : openSeat ? "参加" : "観戦";
          const who = ended ? `${escapeText(table.leader)}　ゲーム終了` : `${escapeText(table.leader)}の卓　${table.status}　${table.seated}/${table.players}人${table.observers ? `　観戦${table.observers}` : ""}`;
          return `<div class="row table-line">
          <span>${who}</span>
          <button type="button" data-join="${escapeAttr(table.id)}">${label}</button>
        </div>`;
        }).join("") : `<p class="note">いま開ける卓はありません。</p>`}
      </section>`;
    app.querySelectorAll("[data-guide]").forEach((button) => {
      button.onclick = () => {
        guide = button.dataset.guide;
        render();
      };
    });
    const guideOk = app.querySelector("#guide-ok");
    if (guideOk) guideOk.onclick = () => {
      guide = null;
      render();
    };
    const openSettings = app.querySelector("#open-settings");
    if (openSettings) openSettings.onclick = () => {
      const savedOptionsNow = savedOptions() || {};
      settingsDraft = {
        simple: simpleOn(savedOptionsNow),
        ok: savedOkTimeout(savedOptionsNow),
        turn: Number(savedOptionsNow.turn_timeout ?? 120),
      };
      settingsOpen = true;
      guide = null;
      render();
    };
    const saveSettings = app.querySelector("#settings-save");
    if (saveSettings) saveSettings.onclick = () => applySettings();
    const cancelSettings = app.querySelector("#settings-cancel");
    if (cancelSettings) cancelSettings.onclick = () => {
      settingsOpen = false;
      settingsDraft = null;
      render();
    };
    const simpleMode = app.querySelector("#simple-mode");
    if (simpleMode) simpleMode.onchange = () => {
      if (!settingsDraft) return;
      settingsDraft.simple = simpleMode.checked;
      const mark = app.querySelector("#simple-state");
      if (mark) mark.textContent = simpleMode.checked ? "オン" : "オフ";
    };
    const form = app.querySelector("#start");
    form.onsubmit = async (event) => {
      event.preventDefault();
      const data = new FormData(event.target);
      const players = Number(data.get("players"));
      const name = playerNameFrom(data);
      const options = startOptions(players, data.get("left_handed") === "on");
      rememberName(name);
      rememberSimple(options.simple);
      rememberOptions(options);
      await post("/api/table", { ...options, name });
    };
    const watchCpu = app.querySelector("#watch-cpu");
    if (watchCpu) watchCpu.onclick = () => {
      const data = new FormData(form);
      const name = playerNameFrom(data);
      const options = startOptions(Number(data.get("players")), data.get("left_handed") === "on");
      rememberName(name);
      rememberSimple(options.simple);
      rememberOptions(options);
      post("/api/table", {
        cpu_match: true,
        ...options,
        name,
      });
    };
    app.querySelectorAll("[data-join]").forEach((button) => {
      button.onclick = () => {
        const name = String(new FormData(form).get("player_name") || "");
        rememberName(name);
        post("/api/join", { table: button.dataset.join, name });
      };
    });
    return;
  }

  if (state.phase === "recruiting") {
    renderRecruiting();
    return;
  }

  noteFinishedGame();
  syncCeremony();
  const seats = viewOrder().map((index, row) => {
    const player = state.players[index];
    const focus = state.settling ? state.settling_seat : state.current;
    const turn = index === focus && !state.finished && !state.awaiting_next_round ? " turn" : "";
    const parkedHere = player.achieved.filter((card) => parked.has(String(card.id)));
    const recorded = player.achieved.filter((card) => !parked.has(String(card.id)));
    const orderCards = [];
    if (player.quota) orderCards.push(player.quota);
    orderCards.push(...player.collection, ...parkedHere);
    const coins = coinPlan(player, index);
    const order = orderCards.length
      ? orderCards.map((card, cardIndex) => cardHtml(card, cardIndex + 1, coins.onCard.get(String(card.id)))).join("")
      : "<span class='note'>ノルマなし</span>";
    const recordRows = achievedRows(recorded);
    if (!recordRows.length) recordRows.push([]);
    const done = recordRows.map((row, rowIndex) => {
      const cards = row.map((card, cardIndex) => cardHtml(card, cardIndex + 1, null, true)).join("");
      return `<div class="line record" style="z-index:${rowIndex + 1}">${cards}</div>`;
    }).join("");
    const score = scoreBits(index, baseScore(player));
    const roundPlus = "";
    const alt = row % 2 ? " alt" : "";
    const mine = state.you && state.you.seat === index;
    const onClock = index === state.current && state.current_human;
    const youTag = mine ? `<span class="you-tag${state.your_turn && onClock ? " live" : ""}">【あなた】</span>` : "";
    const clock = onClock ? `<span id="turn-clock"></span>` : "";
    const uses = state.special_actions_rule
      ? `<span class="uses">ダブル ${player.double_action_left ? "残1" : "済"}　配り直し ${player.reshuffle_take_left ? "残1" : "済"}</span>`
      : "";
    const toast = bonusNote && bonusNote.seat === index && Date.now() < bonusNote.until
      ? `<div class="bonus-toast">${bonusNote.text}</div>`
      : "";
    return `<section class="seat${alt}${turn}" data-seat="${index}">
      ${toast}
      <div class="bar"><span class="who"><span class="nameplate"><strong>${escapeText(player.name)}</strong></span>${youTag}${clock}${uses}</span>
        <span class="score">${score.plus}<span class="points">${score.points}</span>点${roundPlus}</span></div>
      <div class="band">
        <div class="vlabel">ノルマ</div>
        <div class="band-main"><div class="line order">${order}</div></div>
      </div>
      <div class="band">
        <div class="vlabel">実績</div>
        <div class="record-row">
          <div class="band-main"><div class="records${recordRows.length > 1 ? " multi" : ""}" style="--rows:${recordRows.length}">${done}</div></div>
          <div class="bonus-stack">${trayTitles(player)}<div class="coin-tray" aria-label="ボーナス">${trayHtml(trayPile(index, coins))}</div></div>
        </div>
      </div>
    </section>`;
  }).join("");

  const me = state.players[state.current];
  const hand = state.left_handed ? " left-hand" : "";
  let controls = "";
  let hint = "";
  if (!state.settling && !state.finished && !state.awaiting_next_round) {
    if (state.your_turn && state.plan === "reshuffle") hint = "配り直しました。行動を選んでください。";
    else if (state.your_turn && state.double_stage === 1) hint = "ダブル：1回目の行動です。";
    else if (state.your_turn && state.double_stage === 2) hint = "ダブル：2回目の行動です。";
    else if (state.your_turn && !me.quota) hint = "場札からノルマ札を選びましょう。";
    else if (state.your_turn) hint = `ノルマ達成まであと${me.need}枚。`;
    else hint = `${me.name} が考えています`;
  }
  if (!state.settling && state.your_turn) {
    const specials = `${cancelDoubleButton()}${specialButtons()}`;
    if (!me.quota) {
      controls = `<div class="controls${hand}"><div class="control-buttons">${specials}<button type="button" id="pass">パス</button></div></div>`;
    } else {
      controls = `<div class="controls${hand}"><div class="control-buttons">${specials}
          <button type="button" id="abandon">放棄</button>
          <button type="button" id="pass">${state.turn_gain ? "次へ" : "パス"}</button>
        </div></div>`;
    }
  }

  const market = marketSlots.map((card) => {
    if (!card) return `<div class="card gap"></div>`;
    const wide = card.face && [...card.face].length > 2 ? " wide" : "";
    const rank = card.face ? `<div class="rank${wide}" style="color:${card.color}">${card.face}</div>` : "";
    const idle = state.settling || (state.your_turn && !canPlay(card, me)) ? "idle" : "";
    return `<div class="card ${card.joker ? "joker" : ""} ${idle}" data-id="${card.id}">
      <button type="button" class="pick">${rank}${iconHtml(card)}${goodsHtml(card)}</button>
    </div>`;
  }).join("");

  const ceremonyBoxes = captureCeremonyRows();
  app.innerHTML = `
    <div class="bar">
      <h1 class="brand"><img class="brand-title" src="/title1.png" alt="QUOTA 揃えて、達成。"></h1>
    </div>
    ${joinHtml()}
    <p class="note">${state.round_count > 1 ? `第${state.round_index}ラウンド / ${state.round_count}　` : ""}手番 ${state.turn_number} / 山札 ${state.deck_count}
      / 膠着状態 ${state.stall_count} / 連続パス ${state.no_gain_streak}/${state.player_count}
      ${state.sequence_rule ? " / 並び順" : ""}${state.title_rule ? " / 称号" : ""}${state.special_actions_rule ? " / 特殊" : ""}</p>
    ${watcherHtml()}
    ${gateHtml()}
    ${ceremonyHtml()}
    <section class="panel market-panel">
      <div class="market-label">場札${hint ? `<span class="thinking">${hint}</span>` : ""}</div>
      <div class="market" id="market">${market}</div>
      ${controls}
    </section>
    ${seats}
    ${state.finished && !ceremony && tally && tally.phase === "done" && !scoreAnim.size && !titleCheer ? finishHtml() : ""}
    ${standardOfferHtml()}
    ${titleCheer ? `<div class="rollover title-cheer"><div class="panel"><p>${titleCheer.text}</p><p class="title-plus">+${titleCheer.plus}</p></div></div>` : ""}
    ${coverHtml()}
    ${askHtml()}
    <button type="button" id="leave">${leaveLabel()}</button>
    `;

  const leave = app.querySelector("#leave");
  if (leave) leave.onclick = () => confirmLeave();
  const ceremonyOk = app.querySelector("#ceremony-ok");
  if (ceremonyOk) ceremonyOk.onclick = () => pressCeremony();
  slideCeremonyRows(ceremonyBoxes);
  placeCeremony();
  app.querySelectorAll(".pick").forEach((button) => {
    button.onclick = () => onPick(Number(button.parentElement.dataset.id));
  });
  const pass = app.querySelector("#pass");
  if (pass) pass.onclick = () => {
    const next = pass.textContent === "次へ";
    const send = () => post("/api/action", { kind: "pass" });
    if (!(state.market || []).some((card) => card && canPlay(card, me))) {
      send();
      return;
    }
    openAsk(
      next ? "本当に次へ進みますか？" : "本当にパスしますか？",
      next ? "次へ進む" : "パスする",
      send,
      next ? "next" : "pass",
    );
  };
  const abandon = app.querySelector("#abandon");
  if (abandon) abandon.onclick = () => openAsk(
    "本当に放棄しますか？",
    "放棄する",
    () => post("/api/action", { kind: "abandon" }),
    "abandon",
  );
  const double = app.querySelector("#double");
  if (double) double.onclick = () => post("/api/action", { kind: "double" });
  const cancelDouble = app.querySelector("#cancel-double");
  if (cancelDouble) cancelDouble.onclick = () => post("/api/action", { kind: "cancel" });
  const reshuffle = app.querySelector("#reshuffle");
  if (reshuffle) reshuffle.onclick = () => post("/api/action", { kind: "reshuffle" });
  bindAsk();
  const ack = app.querySelector("#ack");
  if (ack) ack.onclick = () => post("/api/ack", {});
  const ok = app.querySelector("#ok");
  if (ok) ok.onclick = () => post(state.you && state.you.observer ? "/api/leave" : "/api/again", {});
  const standardYes = app.querySelector("#standard-yes");
  if (standardYes) standardYes.onclick = () => dismissStandardOffer(true);
  const standardNo = app.querySelector("#standard-no");
  if (standardNo) standardNo.onclick = () => dismissStandardOffer(false);
  if (!recordPrimed && state.phase === "playing") {
    document.querySelectorAll(".line.record .card").forEach((card) => recordSeen.add(card.dataset.id));
    recordPrimed = true;
  }
  maybeTally();
  paintClock();
}

function renderRecruiting() {
  const you = state.you || {};
  const seats = state.seats || [];
  const cpus = state.cpus || [];
  const open = Math.max(0, state.players - seats.length);
  app.innerHTML = `
    <header class="hero">
      <img class="hero-title" src="/title1.png" alt="QUOTA 揃えて、達成。">
    </header>
    <section class="panel">
      <p>${state.players}人卓　参加 ${seats.length}人${open ? `　空き ${open}` : ""}</p>
      ${you.leader ? `<p class="seat-count">人数${[3, 4].map((count) => `<button type="button" class="chip${count === state.players ? " on" : ""}" data-players="${count}"${count < seats.length ? " disabled" : ""}>${count}人</button>`).join("")}</p>` : ""}
      <ul class="roster">
        ${seats.map((seat) => `<li>${escapeText(seat.name)}${seat.leader ? "（リーダー）" : ""}</li>`).join("")}
        ${cpus.map((name) => `<li class="cpu">${escapeText(name)}<span class="tag">CPU</span></li>`).join("")}
      </ul>
      ${open ? `<p class="note">空いている席はこのCPUが入ります。人が参加すると下から席を譲ります。</p>` : ""}
      ${open && you.leader ? `<p class="submit-cast"><button type="button" id="shuffle">シャッフル</button></p>` : ""}
      ${rosterNote ? `<p class="note">${escapeText(rosterNote)}</p>` : ""}
      ${watcherHtml()}
      ${you.leader ? `<p class="submit"><button class="primary" type="button" id="begin">ゲーム開始</button></p>` : `<p class="note">リーダーの開始を待っています。</p>`}
    </section>
    ${askHtml()}
    <button type="button" id="leave">${leaveLabel()}</button>`;
  const begin = app.querySelector("#begin");
  if (begin) begin.onclick = () => post("/api/start", {});
  const shuffle = app.querySelector("#shuffle");
  if (shuffle) shuffle.onclick = () => post("/api/shuffle", {});
  app.querySelectorAll("[data-players]").forEach((button) => {
    button.onclick = () => {
      if (Number(button.dataset.players) === state.players) return;
      post("/api/players", { players: Number(button.dataset.players) });
    };
  });
  const leave = app.querySelector("#leave");
  if (leave) leave.onclick = () => confirmLeave();
  bindAsk();
}

function leaveLabel() {
  return state.you && state.you.observer ? "離れる" : "ゲームから抜ける";
}

function specialButtons() {
  if (!state.special_actions_rule || state.plan !== "normal" || state.turn_gain || state.double_stage) return "";
  const me = state.players[state.current];
  let html = "";
  if (me.double_action_left > 0) html += `<button type="button" id="double">ダブル</button>`;
  if (me.reshuffle_take_left > 0) html += `<button type="button" id="reshuffle">配り直し</button>`;
  return html;
}

function cancelDoubleButton() {
  if (state.plan !== "double" || state.double_stage !== 1 || state.turn_gain) return "";
  return `<button type="button" id="cancel-double">キャンセル</button>`;
}

function confirmLeave() {
  if (state.you && state.you.observer) {
    post("/api/leave", {});
    return;
  }
  openAsk("本当にゲームから抜けますか？", "抜ける", () => post("/api/leave", {}), "leave");
}

function bindAsk() {
  const askYes = app.querySelector("#ask-yes");
  if (askYes) askYes.onclick = () => {
    const run = ask.run;
    ask = null;
    run();
  };
  const askNo = app.querySelector("#ask-no");
  if (askNo) askNo.onclick = () => {
    ask = null;
    render();
  };
}

function openAsk(message, yesLabel, run, kind) {
  ask = { message, yesLabel, run, kind };
  render();
}

function askHtml() {
  if (!ask) return "";
  return `<div class="rollover pass-ask"><div class="panel"><p>${ask.message}</p><p class="ask-buttons"><button type="button" id="ask-yes">${ask.yesLabel}</button><button type="button" class="primary" id="ask-no">キャンセル</button></p></div></div>`;
}

function coverHtml() {
  if (!coverText || Date.now() >= coverUntil) return "";
  return `<div class="rollover cpu-cover"><div class="panel"><p>${escapeText(coverText)}</p></div></div>`;
}

function watcherHtml() {
  return (state.observers || []).map((name) => `<p class="note">オブザーバー${escapeText(name)}が観戦しています</p>`).join("");
}

function joinHtml() {
  if (state.you && state.you.joined) return "";
  return `<form class="panel join" id="join">
    <div class="row">
      <label>あなたの名前
        <input name="player_name" maxlength="24" placeholder="あなた" autocomplete="nickname" value="${escapeAttr(savedName())}">
      </label>
      <button class="primary" type="submit">参加する</button>
    </div>
  </form>`;
}

function confirmHtml(message, gate, rollover) {
  if (!gate || gate.released) return "";
  const waiting = (gate.waiting || []).join("、");
  const body = `<section class="panel tally-note">
    <p>${message}</p>
    <p><button type="button" id="ack" ${gate.you_can_ack ? "" : "disabled"}>OK</button></p>
    ${waiting ? `<p class="note">${waiting} のOKを待っています。</p>` : ""}
  </section>`;
  return rollover ? `<div class="rollover">${body}</div>` : body;
}

function seatOrder() {
  const count = state.players.length;
  const cycle = Array.isArray(state.turn_order) && state.turn_order.length === count
    ? state.turn_order
    : state.players.map((_, index) => index);
  const mine = state.you && state.you.seat != null ? state.you.seat : cycle[0];
  const start = cycle.indexOf(mine);
  const from = start < 0 ? 0 : start;
  return cycle.slice(from).concat(cycle.slice(0, from));
}

function viewOrder() {
  return seatOrder();
}

function roundParts(player) {
  const base = baseScore(player);
  let green = 0;
  let cursor = 0;
  const cards = player.achieved || [];
  while (cursor < cards.length) {
    const rank = cards[cursor] && cards[cursor].rank;
    if (!rank) break;
    green += achievementCoins(rank);
    cursor += rank;
  }
  const purple = player.sequence_bonus || 0;
  const blue = (player.titles || []).reduce((sum, title) => sum + (title.points || 0), 0);
  const previous = (player.delivery_score || 0) - base - green;
  return { base, green, purple, blue, round: base + green + purple + blue, previous };
}

let ceremony = null;
let ceremonyTimer = null;
let ceremonyClosed = false;

function syncCeremony() {
  const live = state && (state.awaiting_next_round || state.finished);
  if (!live) {
    ceremony = null;
    ceremonyClosed = false;
    return;
  }
  if (ceremonyClosed) return;
  const key = `${state.table_id}:${state.round_index}:${state.finished ? 1 : 0}`;
  if (ceremony && ceremony.key === key) return;
  ceremony = state.finished && !watched ? finalCeremony(key) : startCeremony(key);
  if (!ceremonyTimer) ceremonyTimer = setInterval(pumpCeremony, 80);
}

function startCeremony(key) {
  const parts = state.players.map((player) => roundParts(player));
  const scores = new Map(parts.map((part, index) => [index, part.base]));
  const coins = new Map(parts.map((part, index) => {
    const pile = [];
    for (let n = 0; n < part.green; n += 1) pile.push({ kind: "green", id: `g-${index}-${n}` });
    for (let n = 0; n < part.purple; n += 1) pile.push({ kind: "purple", id: `p-${index}-${n}` });
    return [index, pile];
  }));
  const leader = roundLeader();
  const lines = [];
  orderFrom(leader).forEach((seat) => {
    for (const title of state.players[seat].titles || []) {
      if (!title.points) continue;
      lines.push({
        seat,
        text: `${state.players[seat].name} : ${title.name}ボーナス +${title.points}`,
        points: title.points,
        arrived: 0,
        gone: false,
      });
    }
  });
  const reason = state.round_end_reason === "DECK"
    ? "山札切れでラウンド終了。"
    : state.round_end_reason === "STALL"
      ? "膠着の連続でラウンド終了。"
      : "";
  return {
    key,
    phase: "reason",
    dialog: true,
    at: Date.now(),
    readyAt: null,
    pressed: false,
    lines,
    lineCount: lines.length,
    reason,
    reasonShown: reason.length > 0,
    rankTitle: "",
    scores,
    coins,
    roundScore: new Map(parts.map((part, index) => [index, part.round])),
    previous: new Map(parts.map((part, index) => [index, part.previous])),
    order: orderFrom(leader),
    flyLaunched: 0,
    expandArmed: false,
    cashLaunched: 0,
    cashJobs: null,
    plusCount: null,
    plusCleared: false,
    rewriteCount: 0,
    places: null,
    overall: false,
    winnerShown: false,
  };
}

function finalCeremony(key) {
  const parts = state.players.map((player) => roundParts(player));
  const seats = state.players.map((_, index) => index);
  seats.sort((a, b) => (state.players[b].score || 0) - (state.players[a].score || 0));
  const reason = state.round_end_reason === "DECK"
    ? "山札切れでラウンド終了。"
    : state.round_end_reason === "STALL"
      ? "膠着の連続でラウンド終了。"
      : "";
  const lastRound = state.round_index >= 2;
  const board = {
    key,
    phase: "ready",
    at: Date.now(),
    readyAt: Date.now(),
    pressed: false,
    lines: [],
    lineCount: 0,
    dialog: true,
    reason,
    reasonShown: reason.length > 0,
    rankTitle: lastRound ? "最終順位" : "",
    scores: new Map(state.players.map((player, index) => [index, player.score || 0])),
    coins: new Map(state.players.map((_, index) => [index, []])),
    roundScore: new Map(parts.map((part, index) => [index, part.round])),
    previous: new Map(parts.map((part, index) => [index, part.previous])),
    order: seats,
    flyLaunched: 0,
    cashLaunched: 0,
    cashJobs: [],
    plusCount: null,
    plusCleared: true,
    rewriteCount: 0,
    places: null,
    overall: lastRound,
    winnerShown: true,
    review: true,
  };
  assignPlaces(board, (seat) => board.scores.get(seat) || 0);
  captureRankSlots(board);
  return board;
}

function captureRankSlots(show) {
  show.rankSlots = show.order.map((seat) => (show.places ? show.places.get(seat) : 0) || 0);
}

function assignPlaces(show, scoreOf) {
  const places = new Map();
  let place = 1;
  show.order.forEach((seat, index) => {
    if (index > 0 && scoreOf(show.order[index - 1]) !== scoreOf(seat)) place = index + 1;
    places.set(seat, place);
  });
  show.places = places;
}

function assignPlacesByScore(show, scoreOf) {
  const seats = show.order.slice().sort((a, b) => scoreOf(b) - scoreOf(a));
  const places = new Map();
  let place = 1;
  seats.forEach((seat, index) => {
    if (index > 0 && scoreOf(seats[index - 1]) !== scoreOf(seat)) place = index + 1;
    places.set(seat, place);
  });
  show.places = places;
}

function roundLeader() {
  const cycle = state.turn_order || [];
  if (!cycle.length) return 0;
  return cycle[(state.round_index - 1) % cycle.length];
}

function orderFrom(seat) {
  const count = state.players.length;
  const listed = state.turn_order || [];
  const cycle = listed.length === count ? listed.slice() : state.players.map((_, index) => index);
  const start = cycle.indexOf(seat);
  if (start < 0) return cycle;
  return cycle.slice(start).concat(cycle.slice(0, start));
}

function sortSeats(scoreOf) {
  return state.players.map((_, index) => index).sort((a, b) => {
    const diff = scoreOf(b) - scoreOf(a);
    if (diff) return diff;
    return (state.turn_order || []).indexOf(a) - (state.turn_order || []).indexOf(b);
  });
}

function pumpCeremony() {
  if (!ceremony || !state || (!state.awaiting_next_round && !state.finished)) {
    if (ceremonyTimer && (!state || (!state.awaiting_next_round && !state.finished))) {
      clearInterval(ceremonyTimer);
      ceremonyTimer = null;
    }
    return;
  }
  const dirty = updateCeremony(Date.now());
  if (dirty) render();
  launchDueFlights();
}

function updateCeremony(now) {
  const show = ceremony;
  if (!show || show.review) return false;
  let dirty = false;
    if (show.phase === "reason") {
    if (now - show.at >= 1000) {
      show.phase = "titles";
      show.at = now;
      show.flyLaunched = 0;
      show.pressed = false;
      dirty = true;
    }
  } else if (show.phase === "titles") {
    const jobs = titleJobs();
    const done = jobs.length === 0 || (show.flyLaunched >= jobs.length && show.lines.every((line) => line.gone));
    if (done) {
      show.phase = "expand";
      show.at = now;
      show.expandArmed = false;
      show.pressed = false;
      dirty = true;
    }
  } else if (show.phase === "board") {
    if (now - show.at >= 500) {
      show.order = sortSeats((seat) => show.scores.get(seat) || 0);
      assignPlaces(show, (seat) => show.scores.get(seat) || 0);
      captureRankSlots(show);
      show.phase = "base-sorted";
      show.at = now;
      show.pressed = false;
      dirty = true;
    }
  } else if (show.phase === "base-sorted") {
    if (now - show.at >= 500) {
      show.phase = "cash";
      show.at = now;
      show.cashJobs = cashJobs();
      show.cashLaunched = 0;
      show.pressed = false;
      dirty = true;
    }
  } else if (show.phase === "ranked") {
    if (show.pressed || now - show.at >= 2000) {
      show.pressed = false;
      if (state.round_index >= 2) {
        show.phase = "clear";
        show.at = now;
      } else enterReady(show, now);
      dirty = true;
    }
  } else if (show.phase === "clear") {
    if (show.pressed || now - show.at >= 800) {
      show.pressed = false;
      show.overall = true;
      show.scores = new Map(show.previous);
      show.rankTitle = "暫定順位";
      show.order = sortSeats((seat) => show.previous.get(seat) || 0);
      assignPlaces(show, (seat) => show.previous.get(seat) || 0);
      captureRankSlots(show);
      show.phase = "wait-prev";
      show.at = now;
      dirty = true;
    }
  } else if (show.phase === "wait-prev") {
    if (show.pressed || now - show.at >= 2000) {
      show.pressed = false;
      show.phase = "plus";
      show.at = now;
      show.readyAt = null;
      show.plusCount = 0;
      show.plusCleared = false;
      dirty = true;
    }
  } else if (show.phase === "plus") {
    const count = Math.min(show.order.length, Math.floor((now - show.at) / 200));
    if (count !== show.plusCount) {
      show.plusCount = count;
      dirty = true;
    }
    if (count >= show.order.length && show.readyAt == null) show.readyAt = now;
    if (count >= show.order.length && show.readyAt != null && now - show.readyAt >= 1000) {
      show.phase = "rewrite";
      show.at = now;
      show.readyAt = null;
      show.rewriteCount = 1;
      show.scores.set(show.order[0], (show.previous.get(show.order[0]) || 0) + (show.roundScore.get(show.order[0]) || 0));
      dirty = true;
    }
  } else if (show.phase === "rewrite") {
    const count = Math.min(show.order.length, Math.floor((now - show.at) / 200) + 1);
    if (count !== show.rewriteCount) {
      show.rewriteCount = count;
      for (let i = 0; i < count; i += 1) {
        const seat = show.order[i];
        show.scores.set(seat, (show.previous.get(seat) || 0) + (show.roundScore.get(seat) || 0));
      }
      dirty = true;
    }
    if (count >= show.order.length && show.readyAt == null) show.readyAt = now;
    if (show.readyAt != null && now - show.readyAt >= 1000) {
      show.order = sortSeats((seat) => show.scores.get(seat) || 0);
      assignPlaces(show, (seat) => show.scores.get(seat) || 0);
      captureRankSlots(show);
      show.rankTitle = state.finished || state.round_index >= state.round_count ? "最終順位" : "暫定順位";
      show.plusCleared = true;
      enterReady(show, now);
      dirty = true;
    }
  } else if (show.phase === "ready" && show.winnerAt != null && !show.winnerShown && now >= show.winnerAt) {
    show.winnerShown = true;
    dirty = true;
  }
  return dirty;
}

function enterReady(show, now) {
  const last = state.finished || state.round_index >= state.round_count;
  show.phase = "ready";
  show.at = now;
  show.pressed = false;
  if (last) {
    show.winnerAt = now + 1000;
    show.winnerShown = false;
  } else {
    show.winnerAt = null;
    show.winnerShown = true;
  }
}

function cashJobs() {
  const piles = orderFrom(roundLeader()).map((seat) =>
    (ceremony.coins.get(seat) || []).map((coin) => ({ seat, id: coin.id, kind: coin.kind, done: false, started: false }))
  );
  const max = piles.reduce((n, pile) => Math.max(n, pile.length), 0);
  const jobs = [];
  for (let wave = 0; wave < max; wave += 1) {
    for (const pile of piles) {
      if (wave < pile.length) jobs.push({ ...pile[wave], wave });
    }
  }
  return jobs;
}

function titleJobs() {
  const jobs = [];
  (ceremony.lines || []).forEach((line, index) => {
    for (let n = 0; n < line.points; n += 1) jobs.push({ index, n, seat: line.seat });
  });
  return jobs;
}

function finishCash(now) {
  ceremony.order = sortSeats((seat) => ceremony.scores.get(seat) || 0);
  assignPlaces(ceremony, (seat) => ceremony.scores.get(seat) || 0);
  captureRankSlots(ceremony);
  ceremony.phase = "ranked";
  ceremony.at = now;
  ceremony.pressed = false;
  render();
}

function launchDueFlights() {
  if (!ceremony || ceremony.review) return;
  const now = Date.now();
  if (ceremony.phase === "titles") {
    const jobs = titleJobs();
    while (ceremony.flyLaunched < jobs.length && now >= ceremony.at + ceremony.flyLaunched * 100) {
      const job = jobs[ceremony.flyLaunched];
      ceremony.flyLaunched += 1;
      flyTitleCoin(job);
    }
  } else if (ceremony.phase === "cash") {
    const jobs = ceremony.cashJobs || [];
    if (!jobs.length) {
      finishCash(now);
      return;
    }
    while (ceremony.cashLaunched < jobs.length) {
      const job = jobs[ceremony.cashLaunched];
      if (now < ceremony.at + job.wave * 100) break;
      ceremony.cashLaunched += 1;
      if (!job.started) {
        job.started = true;
        flyScoreCoin(job);
      }
    }
  }
}

function flyTitleCoin(job) {
  const line = ceremony && ceremony.lines[job.index];
  const kind = line && line.text.includes("単色") ? "mono" : "purist";
  const source = document.querySelector(`[data-seat="${job.seat}"] [data-title="${kind}"]`);
  const from = source ? source.getBoundingClientRect() : { left: 40, top: 40, width: 14, height: 14 };
  flyDot(from, () => {
    const live = document.querySelector(`[data-seat="${job.seat}"] .tray-coins`);
    return live ? trayCenter(live.getBoundingClientRect()) : from;
  }, "blue", () => {
    if (!ceremony) return;
    const line = ceremony.lines[job.index];
    if (!line) return;
    const pile = ceremony.coins.get(line.seat) || [];
    pile.push({ kind: "blue", id: `t-${job.index}-${job.n}` });
    ceremony.coins.set(line.seat, pile);
    line.arrived += 1;
    if (line.arrived >= line.points) line.gone = true;
    render();
  });
}

function flyScoreCoin(job) {
  const source = document.querySelector(`[data-seat="${job.seat}"] .coin-tray [data-id="${job.id}"]`);
  const from = source ? source.getBoundingClientRect() : { left: 80, top: 80, width: 14, height: 14 };
  const pile = ceremony.coins.get(job.seat) || [];
  const index = pile.findIndex((coin) => coin.id === job.id);
  const coin = index >= 0 ? pile[index] : { kind: job.kind };
  if (index >= 0) pile.splice(index, 1);
  render();
  flyDot(from, () => {
    const points = document.querySelector(`[data-ceremony-score="${job.seat}"]`);
    return points ? trayCenter(points.getBoundingClientRect()) : from;
  }, coin.kind, () => {
    if (!ceremony) return;
    ceremony.scores.set(job.seat, (ceremony.scores.get(job.seat) || 0) + 1);
    job.done = true;
    if ((ceremony.cashJobs || []).every((item) => item.done)) finishCash(Date.now());
    else render();
  });
}

function trayCenter(rect) {
  return { left: rect.left + rect.width / 2 - 7, top: rect.top + rect.height / 2 - 7, width: 14, height: 14 };
}

function flyDot(from, toRect, kind, onDone) {
  const ghost = document.createElement("i");
  ghost.className = `coin ${kind} flying`;
  ghost.style.transition = "none";
  ghost.style.left = `${from.left}px`;
  ghost.style.top = `${from.top}px`;
  document.body.appendChild(ghost);
  requestAnimationFrame(() => {
    ghost.style.transition = "left .3s linear, top .3s linear";
    const to = toRect();
    ghost.style.left = `${to.left}px`;
    ghost.style.top = `${to.top}px`;
  });
  setTimeout(() => {
    if (ghost.isConnected) ghost.remove();
    onDone();
  }, 320);
}

function pressCeremony() {
  if (!ceremony || ceremonyClosed) return;
  if (ceremony.phase === "ready") {
    if (state.finished || state.round_index >= state.round_count) {
      ceremonyClosed = true;
      post("/api/leave", {});
      return;
    }
    ceremony = null;
    post("/api/next-round", {});
    return;
  }
  ceremony.pressed = true;
  if (updateCeremony(Date.now())) render();
  launchDueFlights();
}

function ceremonyFigure(seat, row) {
  const show = ceremony;
  const score = show.scores.get(seat) || 0;
  const round = show.roundScore.get(seat) || 0;
  const prev = show.previous.get(seat) || 0;
  const mark = (text) => `<span class="ceremony-points" data-ceremony-score="${seat}">${text}</span>`;
  if (show.phase === "plus") {
    if (show.plusCleared || row >= (show.plusCount || 0)) return mark(`${score}点`);
    return `${mark(`${score}点`)}<span class="round-plus">+${round}</span>`;
  }
  if (show.phase === "rewrite") {
    if (row >= (show.rewriteCount || 0)) return `${mark(`${prev}点`)}<span class="round-plus">+${round}</span>`;
    return mark(`${prev}+${round}=${prev + round}`);
  }
  if (show.phase === "ready" && state.round_index >= 2) return mark(`${prev}+${round}=${score}`);
  return mark(`${score}点`);
}

function winnerLine() {
  if (!ceremony || !ceremony.places || !state) return "";
  const last = ceremony.review || state.finished || state.round_index >= state.round_count;
  if (!last || (!ceremony.review && !ceremony.winnerShown)) return "";
  const names = [];
  ceremony.order.forEach((seat) => {
    if (ceremony.places.get(seat) === 1) names.push(state.players[seat].name);
  });
  if (!names.length) return "";
  return `${names.join("さん、")}さん、総合優勝おめでとうございます`;
}

function ceremonyHtml() {
  if (!ceremony || (!ceremony.review && !ceremony.dialog)) return "";
  const heading = `第${state.round_index}ラウンド終了`;
  const compact = !ceremony.review && (ceremony.phase === "reason" || ceremony.phase === "titles" || ceremony.phase === "expand");
  const reason = ceremony.reasonShown && ceremony.reason
    ? `<p class="ceremony-reason">${escapeText(ceremony.reason)}</p>`
    : "";
  if (compact) return `<div class="ceremony compact"><p class="ceremony-heading">${heading}</p>${reason}</div>`;
  const overall = state.round_index >= 2
    ? `<p class="ceremony-overall">${ceremony.rankTitle || ""}</p>`
    : "";
  const blank = ceremony.phase === "clear";
  const rows = ceremony.order.map((seat, row) => {
    const player = state.players[seat];
    const slot = !blank && ceremony.rankSlots ? ceremony.rankSlots[row] : !blank && ceremony.places ? ceremony.places.get(seat) : 0;
    const place = slot ? `${slot}位` : "";
    const name = blank ? "" : escapeText(player.name);
    const figure = blank ? "" : ceremonyFigure(seat, row);
    return `<div class="ceremony-row${blank ? " is-blank" : ""}" data-ceremony-row="${seat}">
      <span class="ceremony-rank">${place}</span>
      <span class="ceremony-mover"><span class="ceremony-name">${name}</span><span class="ceremony-figure">${figure}</span></span>
    </div>`;
  }).join("");
  const last = state.finished || state.round_index >= state.round_count;
  const showOk = ceremony.phase === "ranked" || ceremony.phase === "clear" || ceremony.phase === "wait-prev" || (ceremony.phase === "ready" && (!last || ceremony.winnerShown));
  const label = ceremony.review ? "抜ける" : ceremony.phase === "ready" && last ? "ゲームを終了" : "OK";
  const button = showOk
    ? `<button type="button" class="primary" id="ceremony-ok">${label}</button>`
    : `<button type="button" class="primary" tabindex="-1">${label}</button>`;
  const winner = winnerLine();
  const cheer = winner ? `<p class="ceremony-winner">${escapeText(winner)}</p>` : "";
  return `<div class="ceremony large"><p class="ceremony-heading">${heading}</p>${reason}${overall}<div class="ceremony-board">${rows}</div>${cheer}<p class="submit ceremony-action${showOk ? "" : " pending"}">${button}</p></div>`;
}

function placeCeremony() {
  const box = document.querySelector(".ceremony");
  if (!box || !ceremony) return;
  const market = document.querySelector(".market-panel");
  if (!market) return;
  const marketBox = market.getBoundingClientRect();
  const compactW = Math.min(420, Math.max(220, window.innerWidth - 160));
  const compactH = marketBox.height * 0.9;
  const compactLeft = marketBox.left + (marketBox.width - compactW) / 2;
  const compactTop = marketBox.top + (marketBox.height - compactH) / 2;
  const right = compactLeft + compactW;
  const expandedW = Math.min(680, Math.max(compactW, right - 8));
  const expandedLeft = right - expandedW;
  const expandedTop = window.innerHeight / 3;
  const expandedH = window.innerHeight / 3;
  const apply = (node, left, top, width, height) => {
    node.style.left = `${left}px`;
    node.style.top = `${top}px`;
    node.style.width = `${width}px`;
    node.style.height = `${height}px`;
  };
  if (ceremony.review) {
    box.style.transition = "none";
    apply(box, expandedLeft, expandedTop, expandedW, expandedH);
    return;
  }
  if (ceremony.phase === "expand" && !ceremony.expandArmed) {
    ceremony.expandArmed = true;
    box.style.transition = "none";
    apply(box, compactLeft, compactTop, compactW, compactH);
    requestAnimationFrame(() => {
      const live = document.querySelector(".ceremony");
      if (!live || !ceremony || ceremony.phase !== "expand") return;
      live.style.transition = "left .55s ease, top .55s ease, width .55s ease, height .55s ease";
      apply(live, expandedLeft, expandedTop, expandedW, expandedH);
    });
    setTimeout(() => {
      if (!ceremony || ceremony.phase !== "expand") return;
      ceremony.phase = "board";
      ceremony.at = Date.now();
      ceremony.pressed = false;
      render();
    }, 560);
    return;
  }
  box.style.transition = "none";
  const compactPhase = ceremony.phase === "reason" || ceremony.phase === "titles";
  if (compactPhase) apply(box, compactLeft, compactTop, compactW, compactH);
  else apply(box, expandedLeft, expandedTop, expandedW, expandedH);
}

function captureCeremonyRows() {
  const boxes = new Map();
  document.querySelectorAll("[data-ceremony-row]").forEach((el) => {
    const mover = el.querySelector(".ceremony-mover") || el;
    boxes.set(el.dataset.ceremonyRow, mover.getBoundingClientRect().top);
  });
  return boxes;
}

function slideCeremonyRows(before) {
  if (!before || !before.size) return;
  document.querySelectorAll("[data-ceremony-row]").forEach((el) => {
    const mover = el.querySelector(".ceremony-mover");
    if (!mover) return;
    const prev = before.get(el.dataset.ceremonyRow);
    if (prev == null) return;
    const dy = prev - mover.getBoundingClientRect().top;
    if (Math.abs(dy) < 1) return;
    mover.style.transition = "none";
    mover.style.transform = `translateY(${dy}px)`;
    requestAnimationFrame(() => {
      mover.style.transition = "transform .45s ease";
      mover.style.transform = "";
    });
  });
}

function gateHtml() {
  if (ceremony) return "";
  const refresh = confirmHtml("全員がパスをしたので、場札をリフレッシュします", state.refresh_gate, true);
  if (refresh) return refresh;
  if (!state.finished || state.end_reason !== "DECK" || !watched) return "";
  return confirmHtml("山札がなくなりました。得点計算に映ります", state.score_gate, false);
}

function bonusLine(rank) {
  if (rank >= 13) return "ひと組13枚の達成ボーナス🟢🟢🟢🟢🟢🟢";
  if (rank >= 10) return "ひと組10枚以上の達成ボーナス🟢🟢🟢";
  if (rank >= 7) return "ひと組7枚以上の達成ボーナス🟢";
  return "";
}

function showBonus(rank, seat) {
  const text = bonusLine(rank);
  if (!text) return;
  bonusNote = { text, until: Date.now() + 1000, seat };
  if (state && state.phase !== "hall") render();
  setTimeout(() => {
    if (!bonusNote || Date.now() < bonusNote.until) return;
    bonusNote = null;
    if (state && state.phase !== "hall") render();
  }, 1000);
}

function finishHtml() {
  const reason = state.end_reason === "DECK" ? "ゲーム終了" : "膠着の連続";
  let place = 1;
  const lines = state.ranking.map((group) => {
    const text = group.map((seat) => {
      const player = state.players[seat];
      return `${place}位 ${player.name} ${player.score}点（達成${player.achieve_count} / 最高${player.max_single_score}）`;
    }).join("<br>");
    place += group.length;
    return text;
  }).join("<br>");
  const perks = perkHtml();
  const nextLabel = state.you && state.you.observer ? "離れる" : "次のゲームを始める";
  return `<div class="overlay"><div class="panel"><h2>${reason}</h2><p>${lines}</p>${perks}<button class="primary" id="ok">${nextLabel}</button></div></div>`;
}

function onPick(id) {
  const me = state.players[state.current];
  if (!state.your_turn || state.settling) return;
  if (!me.quota) {
    const card = state.market.find((item) => item && item.id === id);
    if (!card || card.joker) return;
    post("/api/action", { kind: "take", card_id: id });
    return;
  }
  const card = state.market.find((item) => item && item.id === id);
  if (!card || !canPlay(card, me)) return;
  post("/api/action", { kind: "collect", card_ids: [id] });
}

function canPlay(card, player) {
  if (!player.quota) return !card.joker;
  return card.joker || card.goods === player.quota.goods;
}

function liftElement(el) {
  const rect = el.getBoundingClientRect();
  el.classList.add("lifting");
  el.style.left = `${rect.left}px`;
  el.style.top = `${rect.top}px`;
  el.style.width = `${rect.width}px`;
  el.style.height = `${rect.height}px`;
  document.body.appendChild(el);
  return el;
}

function liftMarketCards(cards) {
  if (!cards || !cards.length || !state || state.event_n === undefined) return [];
  const lifted = [];
  for (const card of cards) {
    const el = document.querySelector(`#market [data-id="${card.id}"]`);
    if (el) lifted.push(liftElement(el));
  }
  return lifted;
}

function achievedRows(cards) {
  if (!cards.length) return [];
  const width = app.clientWidth || 900;
  const cardW = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--card-w")) || 76;
  const tray = cardW * 1.8;
  const room = Math.max(80, width - tray - 72);
  const perRow = Math.max(1, Math.floor(room / 4));
  const rows = [];
  for (let i = 0; i < cards.length; i += perRow) rows.push(cards.slice(i, i + perRow));
  return rows;
}

function flyLifted(lifted, onDone) {
  let pending = lifted.length;
  const oneDone = () => {
    pending -= 1;
    if (pending <= 0 && onDone) onDone();
  };
  if (!pending) {
    if (onDone) onDone();
    return;
  }
  requestAnimationFrame(() => {
    for (const el of lifted) {
      const dest = document.querySelector(`[data-seat] [data-id="${el.dataset.id}"]`);
      if (!dest) {
        inFlight.delete(el.dataset.id);
        el.remove();
        oneDone();
        continue;
      }
      const to = dest.getBoundingClientRect();
      let finished = false;
      const finish = () => {
        if (finished) return;
        finished = true;
        inFlight.delete(el.dataset.id);
        el.remove();
        const place = document.querySelector(`[data-seat] [data-id="${el.dataset.id}"]`);
        if (place) place.classList.remove("incoming");
        oneDone();
      };
      el.addEventListener("transitionend", (ev) => {
        if (ev.propertyName === "left") finish();
      });
      el.style.left = `${to.left}px`;
      el.style.top = `${to.top}px`;
      setTimeout(finish, 520);
    }
  });
}

function liveTrayCoin(seatIndex, id) {
  const tray = document.querySelector(`[data-seat="${seatIndex}"] .coin-tray`);
  if (!tray) return null;
  return { tray, coin: tray.querySelector(`[data-id="${id}"]`) };
}

function flyCoins(seatIndex, flights) {
  if (seatIndex == null || !flights.length) return;
  const ordered = flights.slice().sort((a, b) => a.rect.left - b.rect.left || a.rect.top - b.rect.top);
  ordered.forEach((flight, index) => {
    coinHold.add(flight.id);
    const current = liveTrayCoin(seatIndex, flight.id);
    if (current && current.coin) current.coin.style.opacity = "0";
    const ghost = document.createElement("i");
    ghost.className = `coin ${flight.kind} flying`;
    ghost.style.transition = "none";
    ghost.style.width = `${flight.rect.width}px`;
    ghost.style.height = `${flight.rect.height}px`;
    ghost.style.left = `${flight.rect.left}px`;
    ghost.style.top = `${flight.rect.top}px`;
    document.body.appendChild(ghost);
    const launch = () => {
      if (!ghost.isConnected) return;
      const spot = liveTrayCoin(seatIndex, flight.id);
      if (!spot) {
        ghost.remove();
        coinHold.delete(flight.id);
        return;
      }
      if (spot.coin) spot.coin.style.opacity = "0";
      const to = (spot.coin || spot.tray).getBoundingClientRect();
      ghost.style.transition = "left .3s ease, top .3s ease";
      requestAnimationFrame(() => {
        ghost.style.left = `${to.left}px`;
        ghost.style.top = `${to.top}px`;
      });
      setTimeout(() => {
        if (ghost.isConnected) ghost.remove();
        coinHold.delete(flight.id);
        const placed = liveTrayCoin(seatIndex, flight.id);
        if (placed && placed.coin) placed.coin.style.opacity = "";
      }, 300);
    };
    setTimeout(() => requestAnimationFrame(launch), index * 100);
  });
}

function onBoard() {
  return state && (state.phase === "playing" || state.phase === "finished");
}

function hopParked(ids) {
  if (!onBoard()) return;
  const lifted = [];
  const flights = [];
  let seatIndex = null;
  for (const id of ids) {
    const el = document.querySelector(`.line.order [data-id="${id}"]`);
    if (!el) {
      parked.delete(id);
      continue;
    }
    const seat = el.closest("[data-seat]");
    if (seat) seatIndex = Number(seat.dataset.seat);
    el.querySelectorAll(".coin").forEach((coin) => {
      flights.push({
        id: coin.dataset.id,
        kind: coin.dataset.kind,
        rect: coin.getBoundingClientRect(),
      });
    });
    const pile = el.querySelector(".coins");
    if (pile) pile.remove();
    lifted.push(liftElement(el));
    parked.delete(id);
    inFlight.add(id);
  }
  render();
  flyCoins(seatIndex, flights);
  flyLifted(lifted, () => {
    if (pendingBonus >= 7) showBonus(pendingBonus, pendingBonusSeat);
    pendingBonus = 0;
    pendingBonusSeat = null;
    flushMarket();
    maybeTally();
  });
}

function noteTurn(next) {
  if (!next || next.turn_left == null) {
    turnLeft = null;
    return;
  }
  turnLeft = Number(next.turn_left);
  turnLeftAt = Date.now();
}

function paintClock() {
  const el = document.querySelector("#turn-clock");
  if (!el) return;
  if (turnLeft == null) {
    el.textContent = "";
    return;
  }
  const left = Math.max(0, turnLeft - (Date.now() - turnLeftAt) / 1000);
  el.textContent = `残り${Math.ceil(left)}秒`;
}

function noteCover(next) {
  const cover = next && next.cover;
  if (!cover || cover.n === coverSeen) return;
  coverSeen = cover.n;
  coverText = cover.text;
  coverUntil = Date.now() + 1000;
  setTimeout(() => {
    if (Date.now() >= coverUntil && state) render();
  }, 1000);
}

function noteRoster(next) {
  if (!next || next.phase !== "recruiting" || !state || state.phase !== "recruiting") {
    if (!next || next.phase !== "recruiting") rosterNote = "";
    return;
  }
  const before = (state.seats || []).map((seat) => seat.name);
  const after = (next.seats || []).map((seat) => seat.name);
  const arrived = after.filter((name) => !before.includes(name));
  const left = before.filter((name) => !after.includes(name));
  const bits = [];
  if (arrived.length) bits.push(`${arrived.join("、")}が参加しました`);
  if (left.length) bits.push(`${left.join("、")}が抜けました`);
  if (bits.length) rosterNote = bits.join("。");
}

function applyState(next) {
  noteTurn(next);
  noteCover(next);
  noteWatch(next);
  resetScoresForNewRound(next);
  if (!next || next.phase === "hall" || next.phase === "recruiting") {
    noteRoster(next);
    state = next;
    if (next && next.phase !== "playing") {
      scoreAnim.clear();
      marketSlots = [];
      pendingMarket = null;
      marksTaken.clear();
      titleReady.clear();
      coinHold.clear();
      document.querySelectorAll(".coin.flying").forEach((el) => el.remove());
      tallyScores.clear();
      tally = null;
      recordSeen.clear();
      recordPrimed = false;
      gathering = false;
      bonusCashed = false;
      lockedScore.clear();
      inFlight = new Set();
      hiding = new Set();
      parked = new Set();
      pendingBonus = 0;
      pendingBonusSeat = null;
      bonusNote = null;
      titleCheer = null;
    }
    render();
    return;
  }
  const event = next.event;
  const fresh = state && state.phase === "playing" && event && event.n !== seenEvent && event.cards && event.cards.length;
  if (fresh && pendingMarket) {
    const missing = event.cards.some((card) => !document.querySelector(`#market [data-id="${card.id}"]`));
    const held = pendingMarket.some((card) => card && event.cards.some((item) => item.id === card.id));
    if (missing && held) {
      marketSlots = pendingMarket.map((card) => card);
      pendingMarket = null;
      render();
    }
  }
  const lifted = fresh ? liftMarketCards(event.cards) : [];
  if (event) seenEvent = event.n;
  if (fresh) {
    pendingBonus = 0;
    pendingBonusSeat = null;
  }
  let pause = null;
  if (fresh && event.kind === "take") {
    const aceIds = event.cards.filter((card) => card.rank === 1).map((card) => String(card.id));
    aceIds.forEach((id) => parked.add(id));
    if (aceIds.length) pause = { ids: aceIds, ms: 100 };
  }
  if (fresh && event.kind === "collect") {
    const collected = new Set(event.cards.map((card) => String(card.id)));
    const player = next.players[event.seat];
    const bundle = bundleContaining(player.achieved, collected);
    if (bundle.length) {
      const ids = bundle.map((card) => String(card.id));
      ids.forEach((id) => parked.add(id));
      pause = { ids, ms: 400 };
      pendingBonus = bundle[0].rank || 0;
      pendingBonusSeat = event.seat;
    }
  }
  for (const el of lifted) inFlight.add(el.dataset.id);
  hiding = new Set(inFlight);
  noteScores(next);
  layoutMarket(next);
  state = next;
  if (state.phase === "hall" || state.phase === "recruiting") {
    scoreAnim.clear();
    marketSlots = [];
    pendingMarket = null;
    marksTaken.clear();
    titleReady.clear();
    coinHold.clear();
    document.querySelectorAll(".coin.flying").forEach((el) => el.remove());
    tallyScores.clear();
    tally = null;
    recordSeen.clear();
    recordPrimed = false;
    gathering = false;
    bonusCashed = false;
    lockedScore.clear();
    inFlight = new Set();
    hiding = new Set();
    parked = new Set();
    pendingBonus = 0;
    pendingBonusSeat = null;
    bonusNote = null;
    titleCheer = null;
    guide = null;
  }
  settleLate();
  render();
  hiding = new Set();
  const afterFlight = () => {
    if (pause) {
      setTimeout(() => hopParked(pause.ids.filter((id) => parked.has(id))), pause.ms);
      return;
    }
    flushMarket();
    maybeTally();
  };
  if (lifted.length) flyLifted(lifted, afterFlight);
  else afterFlight();
  if (!next.settling && !inFlight.size && !parked.size) flushMarket();
}

function finishSeat(index) {
  const titles = (state.players[index] && state.players[index].titles) || [];
  showTitle(index, titles, 0);
}

function showTitle(index, titles, n) {
  if (!onBoard()) return;
  if (n >= titles.length) {
    titleCheer = null;
    scoreSeat(index + 1);
    return;
  }
  const title = titles[n];
  const player = state.players[index];
  titleReady.add(index);
  titleCheer = {
    text: `${player.name}が『${title.name}』を達成したので+${title.points}のボーナス獲得`,
    plus: title.points,
  };
  const from = tallyScores.has(index) ? tallyScores.get(index) : baseScore(player);
  let step = 0;
  render();
  const tick = () => {
    if (!onBoard()) return;
    step += 1;
    tallyScores.set(index, from + step);
    render();
    if (step < title.points) setTimeout(tick, 80);
    else setTimeout(() => showTitle(index, titles, n + 1), 700);
  };
  setTimeout(tick, 450);
}

function noteWatch(next) {
  if (!next || next.phase === "hall" || next.phase === "recruiting") {
    watched = false;
    return;
  }
  if (next.phase === "playing" && !next.finished) watched = true;
}

function settleLate() {
  if (!state || !state.finished || watched || tally) return;
  state.players.forEach((player, index) => {
    tallyScores.set(index, player.score);
    titleReady.add(index);
  });
  bonusCashed = true;
  gathering = false;
  titleCheer = null;
  tally = { phase: "done" };
}

function perkHtml() {
  if (watched || !state || !state.players) return "";
  const lines = [];
  for (const player of state.players) {
    const delivery = Number(player.delivery_score || 0) - baseScore(player);
    if (delivery > 0) lines.push(`${escapeText(player.name)}の達成ボーナス +${delivery}`);
    if (player.sequence_bonus) lines.push(`${escapeText(player.name)}の並び順ボーナス +${player.sequence_bonus}`);
    for (const title of player.titles || []) {
      lines.push(`${escapeText(player.name)}が『${escapeText(title.name)}』を達成したので+${title.points}のボーナス獲得`);
    }
  }
  return lines.length ? `<p class="perks">${lines.join("<br>")}</p>` : "";
}

function maybeTally() {
  if (ceremony || (state && (state.awaiting_next_round || state.finished))) return;
  if (!state || !state.finished || tally || gathering) return;
  if (state.settling || inFlight.size || parked.size || scoreAnim.size) return;
  if (state.end_reason === "DECK" && !(state.score_gate && state.score_gate.released)) return;
  tally = { phase: "scoring" };
  scoreSeat(0);
}

function scoreSeat(index) {
  if (!onBoard()) return;
  if (index >= state.players.length) {
    tally = { phase: "done" };
    bonusCashed = true;
    gathering = false;
    render();
    return;
  }
  tally = { phase: "seats", seat: index };
  const seat = document.querySelector(`[data-seat="${index}"]`);
  const dots = seat ? [...seat.querySelectorAll(".coin-tray .coin")].filter((coin) => coin.dataset.kind !== "blue") : [];
  if (!dots.length) {
    finishSeat(index);
    return;
  }
  gathering = true;
  let n = 0;
  const points = seat.querySelector(".points");
  const flyOne = () => {
    if (!onBoard()) return;
    if (n >= dots.length) {
      gathering = false;
      finishSeat(index);
      return;
    }
    const dot = dots[n];
    n += 1;
    const from = dot.getBoundingClientRect();
    const ghost = document.createElement("i");
    ghost.className = `coin ${dot.dataset.kind} flying`;
    ghost.style.left = `${from.left}px`;
    ghost.style.top = `${from.top}px`;
    document.body.appendChild(ghost);
    requestAnimationFrame(() => {
      const target = points ? points.getBoundingClientRect() : from;
      ghost.style.left = `${target.left}px`;
      ghost.style.top = `${target.top}px`;
      setTimeout(() => {
        ghost.remove();
        const shown = tallyScores.has(index) ? tallyScores.get(index) : baseScore(state.players[index]);
        const next = shown + 1;
        tallyScores.set(index, next);
        if (points) points.textContent = String(next);
        flyOne();
      }, 180);
    });
  };
  flyOne();
}

function marketById(cards) {
  return new Map(cards.filter(Boolean).map((card) => [card.id, card]));
}

function layoutMarket(next) {
  const incoming = (next.market || []).slice();
  const refreshOpen = (gate) => gate && !gate.released;
  if (refreshOpen(next.refresh_gate) || (state && refreshOpen(state.refresh_gate) && !refreshOpen(next.refresh_gate))) {
    marketSlots = incoming;
    pendingMarket = null;
    return;
  }
  if (next.plan === "reshuffle" && (!state || state.plan !== "reshuffle")) {
    marketSlots = incoming;
    pendingMarket = null;
    return;
  }
  const hold = marketSlots.length && (inFlight.size || parked.size || next.settling);
  if (!hold) {
    marketSlots = incoming;
    pendingMarket = null;
    return;
  }
  const byId = marketById(incoming);
  pendingMarket = incoming;
  const kept = marketSlots.map((slot) => (slot && byId.has(slot.id) ? byId.get(slot.id) : null));
  while (kept.length < incoming.length) kept.push(null);
  marketSlots = kept;
}

function flushMarket() {
  if (!pendingMarket) return;
  if (inFlight.size || parked.size || gathering) return;
  marketSlots = pendingMarket.map((card) => card);
  pendingMarket = null;
  if (state && state.phase !== "hall") render();
}

function resetScoresForNewRound(next) {
  if (!state || !next || !state.players || !next.players) return;
  if (next.phase !== "playing" || next.awaiting_next_round || next.finished) return;
  if (!state.table_id || next.table_id !== state.table_id) return;
  if (!(next.round_index > state.round_index)) return;
  scoreAnim.clear();
  lockedScore.clear();
  tallyScores.clear();
  titleReady.clear();
  titleCheer = null;
}

function noteScores(next) {
  if (!state || !state.players || !next.players) return;
  next.players.forEach((player, index) => {
    const prev = state.players[index];
    const gained = baseScore(player) - baseScore(prev);
    if (!prev || gained <= 0) return;
    const current = scoreAnim.get(index);
    const from = current ? shownPoints(current) : baseScore(prev);
    scoreAnim.set(index, { from, to: from + gained, at: Date.now() });
  });
  if (scoreAnim.size && !scoreTimer) scoreTimer = setInterval(tickScores, 80);
}

function shownPoints(anim) {
  const steps = anim.to - anim.from;
  const n = Math.min(steps, Math.floor((Date.now() - anim.at) / 80));
  return anim.from + n;
}

function scoreBits(index, target) {
  if (tallyScores.has(index)) return { points: tallyScores.get(index), plus: "" };
  const anim = scoreAnim.get(index);
  if (!anim) return { points: lockedScore.has(index) ? lockedScore.get(index) : target, plus: "" };
  const steps = Math.max(anim.to - anim.from, 1);
  const countMs = steps * 80;
  const t = Date.now() - anim.at;
  if (t > countMs + 650) {
    if (anim.to !== target) lockedScore.set(index, anim.to);
    scoreAnim.delete(index);
    return { points: anim.to, plus: "" };
  }
  const opacity = t <= countMs ? 1 : Math.max(0, 1 - (t - countMs) / 650);
  return {
    points: shownPoints(anim),
    plus: `<span class="gain" style="opacity:${opacity}">+${anim.to - anim.from}</span>`,
  };
}

function tickScores() {
  if (!scoreAnim.size) {
    clearInterval(scoreTimer);
    scoreTimer = 0;
    return;
  }
  if (state && state.phase !== "hall") render();
}

function bundleContaining(cards, ids) {
  let index = 0;
  while (index < cards.length) {
    const rank = cards[index].rank;
    if (!rank) break;
    const bundle = cards.slice(index, index + rank);
    if (bundle.some((card) => ids.has(String(card.id)))) return bundle;
    index += rank;
  }
  return [];
}

function clientId() {
  let id = localStorage.getItem("quota_client");
  if (!id) {
    id = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
    localStorage.setItem("quota_client", id);
  }
  return id;
}

async function post(url, body) {
  const response = await fetch(url, {
    method: "POST",
    cache: "no-store",
    headers: { "Content-Type": "application/json", "X-Quota-Client": clientId() },
    body: JSON.stringify(body),
  });
  const next = await response.json();
  if (!response.ok || next.error) return;
  applyState(next);
}

async function poll() {
  const response = await fetch("/api/state", { cache: "no-store", headers: { "X-Quota-Client": clientId() } });
  const next = await response.json();
  noteTurn(next);
  const gate = JSON.stringify(next.score_gate || null);
  const refresh = JSON.stringify(next.refresh_gate || null);
  const namesOf = (snap) => Array.isArray(snap && snap.players) ? snap.players.map((p) => p.name) : [];
  const presence = JSON.stringify({ you: next.you, observers: next.observers, your_turn: next.your_turn, cover: next.cover, tables: next.tables, seats: next.seats, names: namesOf(next) });
  const prevPresence = JSON.stringify({ you: state && state.you, observers: state && state.observers, your_turn: state && state.your_turn, cover: state && state.cover, tables: state && state.tables, seats: state && state.seats, names: namesOf(state) });
  const changed = !state || next.event_n !== state.event_n || next.phase !== state.phase || next.settling !== state.settling || gate !== JSON.stringify(state.score_gate || null) || refresh !== JSON.stringify(state.refresh_gate || null) || presence !== prevPresence;
  if (changed) applyState(next);
  else paintClock();
}

async function pollLoop() {
  try {
    await poll();
  } catch {
    // 次の間隔で取り直す
  }
  const wait = state && state.phase === "recruiting" ? 100 : 700;
  setTimeout(pollLoop, wait);
}

pollLoop();
setInterval(paintClock, 200);
