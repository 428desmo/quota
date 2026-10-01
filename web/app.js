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
let ask = null;
let coverSeen = 0;
let coverUntil = 0;
let coverText = "";
let rosterNote = "";
let standardOffer = false;
const splash = document.querySelector("#splash");
if (splash) {
  setTimeout(() => {
    splash.classList.add("out");
    setTimeout(() => splash.remove(), 600);
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

function trayHtml(bank) {
  return bank.map((coin, index) => {
    const spot = scatter(coin.id, index);
    const hidden = coinHold.has(coin.id) ? "opacity:0;" : "";
    return `<i class="coin ${coin.kind}" data-id="${coin.id}" data-kind="${coin.kind}" style="left:${spot.x}%;top:${spot.y}%;z-index:${index + 1};${hidden}"></i>`;
  }).join("");
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
  if (!match) return "";
  try {
    return decodeURIComponent(match[1]);
  } catch {
    return "";
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
    if (count >= 3) standardOffer = true;
  } catch {
    standardOffer = false;
  }
}

function standardOfferHtml() {
  if (!standardOffer) return "";
  return `<div class="rollover standard-offer"><div class="panel">
    <p>標準ルールを試してみますか？（設定からいつでも切り替えられます）</p>
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

function savedOptions() {
  const match = document.cookie.match(/(?:^|; )quota_options=([^;]*)/);
  if (!match) return null;
  try {
    return JSON.parse(decodeURIComponent(match[1]));
  } catch {
    return null;
  }
}

function render() {
  if (ask && ask.kind !== "leave" && !state.your_turn) ask = null;
  if (!state || state.phase === "hall") {
    const saved = savedOptions() || {};
    const players = String(saved.players || 3);
    app.innerHTML = `
      <header class="hero">
        <h1>
          <span class="title-main"><span class="ruby">ク ォ ー タ</span><span class="word">QUOTA</span></span>
          <span class="sub">揃えて、達成。</span>
        </h1>
        <p class="catch">ノルマは、自分で決めろ。</p>
      </header>
      <p class="guide-buttons">
        <button type="button" data-guide="quick">QuickStartガイド</button>
        <button type="button" data-guide="rules">ルール</button>
        <button type="button" data-guide="hint">勝つためのヒント</button>
      </p>
      ${guideHtml()}
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
            <input name="player_name" maxlength="24" placeholder="あなた" autocomplete="nickname" value="${escapeAttr(savedName())}">
          </label>
        </div>
        <div class="row tight">
          <label>シード（空ならランダム）
            <input class="short" name="seed" inputmode="numeric">
          </label>
          <label>OKタイムアウト（秒）
            <input class="short" name="ok_timeout" type="number" min="0" step="0.5" value="${saved.ok_timeout ?? 3}">
          </label>
          <label>手番タイムアウト（秒）
            <input class="short" name="turn_timeout" type="number" min="1" step="1" value="${saved.turn_timeout ?? 30}">
          </label>
        </div>
        <div class="row option-gap">
          <label class="check">
            <input name="simple" type="checkbox" ${simpleOn(saved) ? "checked" : ""}> シンプルモード
          </label>
        </div>
        <div class="row option-gap">
          <label class="check"><span>左利き</span>
            <input name="left_handed" type="checkbox" ${saved.left_handed ? "checked" : ""}> ボタンを左に置く
          </label>
        </div>
        <p class="submit"><button class="primary" type="submit">卓を新設</button></p>
      </form>
      <section class="panel">
        <h2>参加できる卓</h2>
        ${(state.tables || []).length ? (state.tables || []).map((table) => `<div class="row table-line">
          <span>${escapeText(table.leader)}の卓　${table.status}　${table.seated}/${table.players}人${table.observers ? `　観戦${table.observers}` : ""}</span>
          <button type="button" data-join="${escapeAttr(table.id)}">${table.status === "募集中" && table.seated < table.players ? "参加" : "観戦"}</button>
        </div>`).join("") : `<p class="note">いま開ける卓はありません。</p>`}
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
    const form = app.querySelector("#start");
    form.onsubmit = async (event) => {
      event.preventDefault();
      const data = new FormData(event.target);
      const players = Number(data.get("players"));
      const name = String(data.get("player_name") || "");
      const simple = data.get("simple") === "on";
      rememberName(name);
      rememberSimple(simple);
      await post("/api/table", {
        players,
        name,
        seed: data.get("seed"),
        simple,
        sequence: !simple,
        title: !simple,
        special: !simple,
        ok_timeout: Number(data.get("ok_timeout")),
        turn_timeout: Number(data.get("turn_timeout")),
        left_handed: data.get("left_handed") === "on",
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
  const seats = seatOrder().map((index, row) => {
    const player = state.players[index];
    const focus = state.settling ? state.settling_seat : state.current;
    const turn = index === focus && !state.finished ? " turn" : "";
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
      <div class="bar"><span class="who"><strong>${escapeText(player.name)}</strong>${youTag}${clock}${uses}</span>
        <span class="score">${score.plus}<span class="points">${score.points}</span>点</span></div>
      <div class="band">
        <div class="vlabel">ノルマ</div>
        <div class="band-main"><div class="line order">${order}</div></div>
      </div>
      <div class="band">
        <div class="vlabel">実績</div>
        <div class="record-row">
          <div class="band-main"><div class="records${recordRows.length > 1 ? " multi" : ""}" style="--rows:${recordRows.length}">${done}</div></div>
          <div class="coin-tray" aria-label="ボーナス">${trayHtml(coins.bank)}</div>
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

  app.innerHTML = `
    <div class="bar">
      <h1 class="brand"><span class="word">QUOTA</span><span class="sub">揃えて、達成。</span></h1>
    </div>
    ${joinHtml()}
    <p class="note">${state.round_count > 1 ? `第${state.round_index}ラウンド / ${state.round_count}　` : ""}手番 ${state.turn_number} / 山札 ${state.deck_count}
      / 膠着状態 ${state.stall_count} / 連続パス ${state.no_gain_streak}/${state.player_count}
      ${state.sequence_rule ? " / 並び順" : ""}${state.title_rule ? " / 称号" : ""}${state.special_actions_rule ? " / 特殊" : ""}</p>
    ${watcherHtml()}
    ${gateHtml()}
    ${roundBreakHtml()}
    <section class="panel market-panel">
      <div class="market-label">場札${hint ? `<span class="thinking">${hint}</span>` : ""}</div>
      <div class="market" id="market">${market}</div>
      ${controls}
    </section>
    ${seats}
    ${state.finished && tally && tally.phase === "done" && !scoreAnim.size && !titleCheer ? finishHtml() : ""}
    ${standardOfferHtml()}
    ${titleCheer ? `<div class="rollover title-cheer"><div class="panel"><p>${titleCheer.text}</p><p class="title-plus">+${titleCheer.plus}</p></div></div>` : ""}
    ${coverHtml()}
    ${askHtml()}
    <button type="button" id="leave">${leaveLabel()}</button>
    `;

  const leave = app.querySelector("#leave");
  if (leave) leave.onclick = () => confirmLeave();
  const nextRound = app.querySelector("#next-round");
  if (nextRound) nextRound.onclick = () => post("/api/next-round", {});
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
  const open = Math.max(0, state.players - seats.length);
  app.innerHTML = `
    <header class="hero">
      <h1>
        <span class="title-main"><span class="word">QUOTA</span></span>
        <span class="sub">揃えて、達成。</span>
      </h1>
    </header>
    <section class="panel">
      <p>${state.players}人卓　参加 ${seats.length}人${open ? `　空き ${open}` : ""}</p>
      <ul class="roster">
        ${seats.map((seat) => `<li>${escapeText(seat.name)}${seat.leader ? "（リーダー）" : ""}</li>`).join("")}
        ${open ? `<li class="note">参加待ち</li>` : ""}
      </ul>
      ${rosterNote ? `<p class="note">${escapeText(rosterNote)}</p>` : ""}
      ${watcherHtml()}
      ${you.leader ? `<p class="submit"><button class="primary" type="button" id="begin">ゲーム開始</button></p>` : `<p class="note">リーダーの開始を待っています。</p>`}
    </section>
    ${askHtml()}
    <button type="button" id="leave">${leaveLabel()}</button>`;
  const begin = app.querySelector("#begin");
  if (begin) begin.onclick = () => post("/api/start", {});
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

function roundBreakHtml() {
  if (!state.awaiting_next_round) return "";
  const reason = state.round_end_reason === "DECK" ? "山札切れ" : "膠着の連続";
  const lines = seatOrder().map((index) => {
    const player = state.players[index];
    return `${escapeText(player.name)} ${player.score}点`;
  }).join("<br>");
  return `<div class="rollover"><div class="panel"><p>第${state.round_index}ラウンド終了（${reason}）</p><p>${lines}</p><p class="submit"><button type="button" class="primary" id="next-round">次のラウンド</button></p></div></div>`;
}

function gateHtml() {
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
