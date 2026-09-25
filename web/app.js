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

function cardHtml(card, z = 1, marks = null, compact = false) {
  const id = String(card.id);
  const hidden = hiding.has(id) || inFlight.has(id) ? "incoming" : "";
  const fresh = compact && recordPrimed && !recordSeen.has(id) ? " just-in" : "";
  if (compact && recordPrimed) recordSeen.add(id);
  const wide = card.face && [...card.face].length > 2 ? " wide" : "";
  const rank = card.face ? `<div class="rank${wide}" style="color:${card.color}">${card.face}</div>` : "";
  const show = marks && !marksTaken.has(id) ? marks : null;
  const dots = show
    ? [...Array(show.green || 0).fill("🟢"), ...Array(show.purple || 0).fill("🟣")]
    : [];
  const mark = dots.length ? `<div class="marks">${dots.map((dot) => `<span>${dot}</span>`).join("")}</div>` : "";
  return `<div class="card ${card.joker ? "joker" : ""} ${hidden}${fresh}" data-id="${card.id}" style="z-index:${z}">
    ${rank}
    ${mark}
    <div class="emoji">${card.emoji}</div>
    ${goodsHtml(card, compact)}
  </div>`;
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

function addMark(marks, card, kind, count) {
  if (!card || !count) return;
  const key = String(card.id);
  const current = marks.get(key) || { green: 0, purple: 0 };
  current[kind] = (current[kind] || 0) + count;
  marks.set(key, current);
}

function deliveryMarks(cards, sequence) {
  const marks = new Map();
  let index = 0;
  while (index < cards.length) {
    const quota = cards[index];
    const rank = quota.rank;
    if (!rank) break;
    if (rank >= 7) addMark(marks, cards[index + 6], "green", 1);
    if (rank >= 10) addMark(marks, cards[index + 9], "green", 2);
    if (rank === 13) addMark(marks, cards[index + 12], "green", 3);
    index += rank;
  }
  if (!sequence) return marks;
  for (let i = 1; i < cards.length; i += 1) {
    const prev = cards[i - 1];
    const card = cards[i];
    if (prev.joker || card.joker) continue;
    const left = prev.rank;
    const right = card.rank;
    const purple = left === right ? 2 : Math.abs(left - right) === 1 ? 1 : 0;
    if (!purple) continue;
    const key = String(card.id);
    const current = marks.get(key) || { green: 0, purple: 0 };
    current.purple += purple;
    marks.set(key, current);
  }
  return marks;
}

const GUIDES = {
  basic: {
    title: "基本ルール",
    lines: [
      "<strong>場札</strong>から1枚選び、<strong>ノルマ札</strong>にする。",
      "ノルマ札と同じ種類（トランプで言えば♠♦♣♥）の札を、書かれた数字の枚数だけ集めたら<strong>達成</strong>。",
      "集める札は、場札から何枚取ってもよい。",
      "<strong>ワイルド</strong>はどの種類の代わりにもなる。",
      "達成が難しければ、ノルマ札を<strong>放棄</strong>して選び直せる。",
      "取れる札も取りたい札もなければ<strong>パス</strong>。",
      "全員が続けてパスしたら<strong>配り直し</strong>。配り直した後も誰も取れなければ、そこで終了。",
      "<strong>山札</strong>がなくなったら終了。集めた枚数が多い人の勝ち。",
    ],
  },
  advanced: {
    title: "上級モード",
    lines: [
      "7枚以上の<strong>ノルマ達成</strong>で、枚数に応じて＋1／＋3／＋6点。",
      "<strong>達成</strong>の記録の並びで、前後する数字なら＋1点、同じ数字なら＋2点（組をまたいでもよい）。",
      "<strong>ワイルド</strong>不使用で3組以上<strong>達成</strong>したら、終了時に＋5点。",
      "同じ種類だけ（<strong>ワイルド</strong>は可）で3組以上<strong>達成</strong>したら、終了時に＋15点。",
    ],
  },
  hint: {
    title: "ヒント",
    lines: [
      "カードは(4種類×13＋<strong>ワイルド</strong>2)×2セット＝108枚。うち8枚をランダムに除外して遊ぶ。",
      "<strong>放棄</strong>すると、次の<strong>ノルマ札</strong>を選べるのは次の自分の手番から。",
      "数を減らした<strong>場札</strong>は、次のプレイヤーの手番になるタイミングで<strong>山札</strong>（裏向きの残りカードの束）から自動で補充される。数えて減っていても気にしなくてよい。",
      "<strong>配り直し</strong>では、<strong>場札</strong>をすべて<strong>山札</strong>に戻してシャッフルし、同じ枚数を並べ直す。中身は総入れ替えになる。",
    ],
  },
};

function guideHtml() {
  const page = GUIDES[guide];
  if (!page) return "";
  const lines = page.lines.map((line) => `<li>${line}</li>`).join("");
  return `<div class="rollover guide"><div class="panel"><h2>${page.title}</h2><ul>${lines}</ul><p><button type="button" class="primary" id="guide-ok">OK</button></p></div></div>`;
}

function escapeAttr(value) {
  return String(value).replace(/[&"<>]/g, (ch) => ({ "&": "&amp;", '"': "&quot;", "<": "&lt;", ">": "&gt;" }[ch]));
}

function humanSeatCount(players, humans) {
  const seats = Number(players);
  const people = Number(humans);
  return Math.max(0, Math.min(people, seats));
}

function nameFields(count, names) {
  const saved = Array.isArray(names) ? names : [];
  const shown = count >= 2 ? count : 0;
  const fields = Array.from({ length: shown }, (_, i) => {
    const value = saved[i] ? escapeAttr(saved[i]) : "";
    return `<label>席${i + 1}
      <input name="name" maxlength="24" placeholder="席${i + 1}" autocomplete="nickname" value="${value}">
    </label>`;
  }).join("");
  return `<div class="row names" id="names"${shown ? "" : " hidden"}>${fields}</div>`;
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
  if (!state || state.phase === "lobby") {
    const saved = savedOptions() || {};
    const players = String(saved.players || 3);
    const humans = String(saved.humans ?? 3);
    const itemSet = saved.item_set || "";
    const named = humanSeatCount(players, humans);
    app.innerHTML = `
      <header class="hero">
        <h1>
          <span class="title-main"><span class="ruby">ク ォ ー タ</span><span class="word">QUOTA</span></span>
          <span class="sub">揃えて、達成。</span>
        </h1>
        <p class="catch">ノルマは、自分で決めろ。</p>
      </header>
      <p class="guide-buttons">
        <button type="button" data-guide="basic">基本ルール</button>
        <button type="button" data-guide="advanced">上級モード</button>
        <button type="button" data-guide="hint">ヒント</button>
      </p>
      ${guideHtml()}
      <form class="panel" id="start">
        <div class="row">
          <label>アイテムセット
            <select name="item_set">
              ${(state.item_sets || []).map((item) => `<option value="${item.id}" ${(itemSet ? item.id === itemSet : item.default) ? "selected" : ""}>${item.name}</option>`).join("")}
            </select>
          </label>
          <label>人数
            <select name="players">
              ${[3, 4].map((n) => `<option value="${n}" ${String(n) === players ? "selected" : ""}>${n}</option>`).join("")}
            </select>
          </label>
          <label>人間の席
            <select name="humans">
              ${[4, 3, 2, 1, 0].map((n) => `<option ${String(n) === humans ? "selected" : ""}>${n}</option>`).join("")}
            </select>
          </label>
        </div>
        ${nameFields(named, saved.names)}
        <div class="row tight">
          <label>シード（空ならランダム）
            <input class="short" name="seed" inputmode="numeric">
          </label>
          <label>OKタイムアウト（秒）
            <input class="short" name="ok_timeout" type="number" min="0" step="0.5" value="${saved.ok_timeout ?? 3}">
          </label>
        </div>
        <div class="row tight">
          <label><span>上級</span>
            <input name="sequence" type="checkbox" ${saved.sequence ? "checked" : ""}> 並び順ボーナス
          </label>
          <label><span>称号</span>
            <input name="title" type="checkbox" ${saved.title ? "checked" : ""}> 称号ボーナス
          </label>
        </div>
        <div class="row">
          <label><span>左利き</span>
            <input name="left_handed" type="checkbox" ${saved.left_handed ? "checked" : ""}> ボタンを左に置く
          </label>
        </div>
        <p class="submit"><button class="primary" type="submit">スタート</button></p>
        <p class="note">この画面を開いた端末が同じ盤面を共有します。同じネットワークの他の端末からも操作できます。</p>
      </form>`;
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
    const nameMemory = Array.isArray(saved.names) ? saved.names.slice() : [];
    const refreshNames = () => {
      const count = humanSeatCount(form.players.value, form.humans.value);
      [...form.querySelectorAll('input[name="name"]')].forEach((el, i) => {
        nameMemory[i] = el.value;
      });
      form.querySelector("#names").outerHTML = nameFields(count, nameMemory);
    };
    form.players.onchange = refreshNames;
    form.humans.onchange = refreshNames;
    form.onsubmit = async (event) => {
      event.preventDefault();
      const data = new FormData(event.target);
      const players = Number(data.get("players"));
      let humans = Number(data.get("humans"));
      if (humans > players) humans = players;
      await post("/api/start", {
        players,
        humans,
        names: data.getAll("name").slice(0, humans),
        seed: data.get("seed"),
        sequence: data.get("sequence") === "on",
        title: data.get("title") === "on",
        item_set: data.get("item_set"),
        ok_timeout: Number(data.get("ok_timeout")),
        left_handed: data.get("left_handed") === "on",
      });
    };
    return;
  }

  const seats = state.players.map((player, index) => {
    const focus = state.settling ? state.settling_seat : state.current;
    const turn = index === focus && !state.finished ? " turn" : "";
    const parkedHere = player.achieved.filter((card) => parked.has(String(card.id)));
    const recorded = player.achieved.filter((card) => !parked.has(String(card.id)));
    const orderCards = [];
    if (player.quota) orderCards.push(player.quota);
    orderCards.push(...player.collection, ...parkedHere);
    const order = orderCards.length
      ? orderCards.map((card, index) => cardHtml(card, index + 1)).join("")
      : "<span class='note'>ノルマなし</span>";
    const marks = deliveryMarks(player.achieved, state.sequence_rule);
    const recordRows = achievedRows(recorded);
    if (!recordRows.length) recordRows.push([]);
    const done = recordRows.map((row, rowIndex) => {
      const cards = row.map((card, index) => cardHtml(card, index + 1, marks.get(String(card.id)), true)).join("");
      return `<div class="line record" style="z-index:${rowIndex + 1}">${cards}</div>`;
    }).join("");
    const score = scoreBits(index, baseScore(player));
    const alt = index % 2 ? " alt" : "";
    const toast = bonusNote && bonusNote.seat === index && Date.now() < bonusNote.until
      ? `<div class="bonus-toast">${bonusNote.text}</div>`
      : "";
    return `<section class="seat${alt}${turn}" data-seat="${index}">
      ${toast}
      <div class="bar"><strong>${index === focus && !state.finished ? "▶ " : ""}${player.name}</strong>
        <span>${score.plus}<span class="points">${score.points}</span>点</span></div>
      <div class="band">
        <div class="vlabel">ノルマ</div>
        <div class="band-main"><div class="line order">${order}</div></div>
      </div>
      <div class="band">
        <div class="vlabel">実績</div>
        <div class="band-main"><div class="records${recordRows.length > 1 ? " multi" : ""}" style="--rows:${recordRows.length}">${done}</div></div>
      </div>
    </section>`;
  }).join("");

  const me = state.players[state.current];
  const hand = state.left_handed ? " left-hand" : "";
  let controls = "";
  let hint = "";
  if (!state.settling && !state.finished) {
    if (state.current_human && !me.quota) hint = "場札からノルマ札を選びましょう。";
    else if (state.current_human) hint = `ノルマ達成まであと${me.need}枚。`;
    else hint = `${me.name} が考えています`;
  }
  if (!state.settling && state.current_human) {
    if (!me.quota) {
      controls = `<div class="controls${hand}"><div class="control-buttons"><button type="button" id="pass">パス</button></div></div>`;
    } else {
      controls = `<div class="controls${hand}"><div class="control-buttons">
          <button type="button" id="abandon">放棄</button>
          <button type="button" id="pass">${state.turn_gain ? "次へ" : "パス"}</button>
        </div></div>`;
    }
  }

  const market = marketSlots.map((card) => {
    if (!card) return `<div class="card gap"></div>`;
    const wide = card.face && [...card.face].length > 2 ? " wide" : "";
    const rank = card.face ? `<div class="rank${wide}" style="color:${card.color}">${card.face}</div>` : "";
    const idle = state.settling || (state.current_human && !canPlay(card, me)) ? "idle" : "";
    return `<div class="card ${card.joker ? "joker" : ""} ${idle}" data-id="${card.id}">
      <button type="button" class="pick">${rank}<div class="emoji">${card.emoji}</div>${goodsHtml(card)}</button>
    </div>`;
  }).join("");

  app.innerHTML = `
    <div class="bar">
      <h1 class="brand"><span class="word">QUOTA</span><span class="sub">揃えて、達成。</span></h1>
    </div>
    <p class="note">手番 ${state.turn_number} / 山札 ${state.deck_count}
      / 膠着状態 ${state.stall_count} / 連続パス ${state.no_gain_streak}/${state.player_count}
      ${state.sequence_rule ? " / 並び順" : ""}${state.title_rule ? " / 称号" : ""}</p>
    ${gateHtml()}
    <section class="panel market-panel">
      <div class="market-label">場札${hint ? `<span class="thinking">${hint}</span>` : ""}</div>
      <div class="market" id="market">${market}</div>
      ${controls}
    </section>
    ${seats}
    ${state.finished && tally && tally.phase === "done" && !scoreAnim.size && !titleCheer ? finishHtml() : ""}
    ${titleCheer ? `<div class="rollover title-cheer"><div class="panel"><p>${titleCheer.text}</p><p class="title-plus">+${titleCheer.plus}</p></div></div>` : ""}
    ${state.finished ? "" : `<button type="button" id="restart">途中でやめて最初からやり直す</button>`}
    `;

  const restart = app.querySelector("#restart");
  if (restart) restart.onclick = () => post("/api/reset", {});
  app.querySelectorAll(".pick").forEach((button) => {
    button.onclick = () => onPick(Number(button.parentElement.dataset.id));
  });
  const pass = app.querySelector("#pass");
  if (pass) pass.onclick = () => post("/api/action", { kind: "pass" });
  const abandon = app.querySelector("#abandon");
  if (abandon) abandon.onclick = () => post("/api/action", { kind: "abandon" });
  const ack = app.querySelector("#ack");
  if (ack) ack.onclick = () => post("/api/ack", {});
  const ok = app.querySelector("#ok");
  if (ok) ok.onclick = () => post("/api/reset", {});
  if (!recordPrimed && state.phase === "playing") {
    document.querySelectorAll(".line.record .card").forEach((card) => recordSeen.add(card.dataset.id));
    recordPrimed = true;
  }
  maybeTally();
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

function gateHtml() {
  const refresh = confirmHtml("全員がパスをしたので、場札をリフレッシュします", state.refresh_gate, true);
  if (refresh) return refresh;
  if (!state.finished || state.end_reason !== "DECK") return "";
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
  if (state && state.phase !== "lobby") render();
  setTimeout(() => {
    if (!bonusNote || Date.now() < bonusNote.until) return;
    bonusNote = null;
    if (state && state.phase !== "lobby") render();
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
  return `<div class="overlay"><div class="panel"><h2>${reason}</h2><p>${lines}</p><button class="primary" id="ok">次のゲームを始める</button></div></div>`;
}

function onPick(id) {
  const me = state.players[state.current];
  if (!state.current_human || state.settling) return;
  if (!me.quota) {
    const card = state.market.find((item) => item.id === id);
    if (!card || card.joker) return;
    post("/api/action", { kind: "take", card_id: id });
    return;
  }
  const card = state.market.find((item) => item.id === id);
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
  const step = cardW * 0.68 * 0.3;
  const perRow = Math.max(1, Math.floor((width - 48) / step));
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

function hopParked(ids) {
  if (!state || state.phase === "lobby") return;
  const lifted = [];
  for (const id of ids) {
    const el = document.querySelector(`.line.order [data-id="${id}"]`);
    if (!el) {
      parked.delete(id);
      continue;
    }
    lifted.push(liftElement(el));
    parked.delete(id);
    inFlight.add(id);
  }
  render();
  flyLifted(lifted, () => {
    if (pendingBonus >= 7) showBonus(pendingBonus, pendingBonusSeat);
    pendingBonus = 0;
    pendingBonusSeat = null;
    flushMarket();
    maybeTally();
  });
}

function applyState(next) {
  const event = next.event;
  const fresh = event && event.n !== seenEvent && event.cards && event.cards.length;
  if (fresh && pendingMarket) {
    const missing = event.cards.some((card) => !document.querySelector(`#market [data-id="${card.id}"]`));
    const held = pendingMarket.some((card) => event.cards.some((item) => item.id === card.id));
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
  if (state.phase === "lobby") {
    scoreAnim.clear();
    marketSlots = [];
    pendingMarket = null;
    marksTaken.clear();
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
  if (!state || state.phase === "lobby") return;
  if (n >= titles.length) {
    titleCheer = null;
    scoreSeat(index + 1);
    return;
  }
  const title = titles[n];
  const player = state.players[index];
  titleCheer = {
    text: `${player.name}が『${title.name}』を達成したので+${title.points}のボーナス獲得`,
    plus: title.points,
  };
  const from = tallyScores.has(index) ? tallyScores.get(index) : baseScore(player);
  let step = 0;
  render();
  const tick = () => {
    if (!state || state.phase === "lobby") return;
    step += 1;
    tallyScores.set(index, from + step);
    render();
    if (step < title.points) setTimeout(tick, 80);
    else setTimeout(() => showTitle(index, titles, n + 1), 700);
  };
  setTimeout(tick, 450);
}

function maybeTally() {
  if (!state || !state.finished || tally || gathering) return;
  if (state.settling || inFlight.size || parked.size || scoreAnim.size) return;
  if (state.end_reason === "DECK" && !(state.score_gate && state.score_gate.released)) return;
  tally = { phase: "scoring" };
  scoreSeat(0);
}

function scoreSeat(index) {
  if (!state || state.phase === "lobby") return;
  if (index >= state.players.length) {
    tally = { phase: "done" };
    bonusCashed = true;
    gathering = false;
    render();
    return;
  }
  tally = { phase: "seats", seat: index };
  const seat = document.querySelector(`[data-seat="${index}"]`);
  const dots = seat ? [...seat.querySelectorAll(".line.record .marks span")] : [];
  if (!dots.length) {
    finishSeat(index);
    return;
  }
  gathering = true;
  let n = 0;
  const points = seat.querySelector(".points");
  const flyOne = () => {
    if (!state || state.phase === "lobby") return;
    if (n >= dots.length) {
      gathering = false;
      finishSeat(index);
      return;
    }
    const dot = dots[n];
    n += 1;
    const from = dot.getBoundingClientRect();
    const ghost = document.createElement("span");
    ghost.className = "gain-dot fast";
    ghost.textContent = dot.textContent;
    ghost.style.left = `${from.left}px`;
    ghost.style.top = `${from.top}px`;
    document.body.appendChild(ghost);
    dot.style.visibility = "hidden";
    const card = dot.closest(".card");
    if (card && ![...card.querySelectorAll(".marks span")].some((span) => span.style.visibility !== "hidden")) {
      marksTaken.add(card.dataset.id);
    }
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

function layoutMarket(next) {
  const incoming = next.market || [];
  const refreshOpen = (gate) => gate && !gate.released;
  if (refreshOpen(next.refresh_gate) || (state && refreshOpen(state.refresh_gate) && !refreshOpen(next.refresh_gate))) {
    marketSlots = incoming.map((card) => card);
    pendingMarket = null;
    return;
  }
  const sameTurn = state && state.phase === "playing" && next.phase !== "lobby" && state.turn_number === next.turn_number;
  if (!sameTurn && marketSlots.length && (next.settling || inFlight.size || parked.size)) {
    pendingMarket = incoming;
    const byId = new Map(incoming.map((card) => [card.id, card]));
    marketSlots = marketSlots.map((slot) => (slot && byId.has(slot.id) ? byId.get(slot.id) : slot ? null : null));
    return;
  }
  if (!sameTurn) {
    marketSlots = incoming.map((card) => card);
    pendingMarket = null;
    return;
  }
  const byId = new Map(incoming.map((card) => [card.id, card]));
  const used = new Set();
  marketSlots = marketSlots.map((slot) => {
    if (!slot || !byId.has(slot.id)) return null;
    used.add(slot.id);
    return byId.get(slot.id);
  });
}

function flushMarket() {
  if (!pendingMarket) return;
  if (inFlight.size || parked.size || gathering) return;
  marketSlots = pendingMarket.map((card) => card);
  pendingMarket = null;
  if (state && state.phase !== "lobby") render();
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
  if (state && state.phase !== "lobby") render();
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
    headers: { "Content-Type": "application/json", "X-Quota-Client": clientId() },
    body: JSON.stringify(body),
  });
  applyState(await response.json());
}

async function poll() {
  const response = await fetch("/api/state", { headers: { "X-Quota-Client": clientId() } });
  const next = await response.json();
  const gate = JSON.stringify(next.score_gate || null);
  const refresh = JSON.stringify(next.refresh_gate || null);
  const changed = !state || next.event_n !== state.event_n || next.phase !== state.phase || next.settling !== state.settling || gate !== JSON.stringify(state.score_gate || null) || refresh !== JSON.stringify(state.refresh_gate || null);
  if (changed) applyState(next);
}

poll();
setInterval(poll, 700);
