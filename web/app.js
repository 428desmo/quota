const app = document.querySelector("#app");
let state = null;
let seenEvent = 0;
const scoreAnim = new Map();
let scoreTimer = 0;
let marketSlots = [];
let pendingMarket = null;
const banks = new Map();
const marksTaken = new Set();
const recordSeen = new Set();
let recordPrimed = false;
let gathering = false;
let bonusCashed = false;
const lockedScore = new Map();

let hiding = new Set();
let inFlight = new Set();
let parked = new Set();

function cardHtml(card, z = 1, marks = null, compact = false) {
  const id = String(card.id);
  const hidden = hiding.has(id) || inFlight.has(id) ? "incoming" : "";
  const fresh = compact && recordPrimed && !recordSeen.has(id) ? " just-in" : "";
  if (compact && recordPrimed) recordSeen.add(id);
  const wide = card.face && [...card.face].length > 2 ? " wide" : "";
  const rank = card.face ? `<div class="rank${wide}" style="color:${card.color}">${card.face}</div>` : "";
  const show = marks && !marksTaken.has(id) ? marks : null;
  const dots = show
    ? [...Array(show.yellow || 0).fill("🟡"), ...Array(show.purple || 0).fill("🟣")]
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

function bankHtml(index) {
  const bank = banks.get(index) || { yellow: 0, purple: 0 };
  const dots = [...Array(bank.yellow).fill("🟡"), ...Array(bank.purple).fill("🟣")];
  if (!dots.length) return "";
  return `<span class="marker-bank">${dots.join("")}</span>`;
}

function deliveryMarks(cards, sequence) {
  const marks = new Map();
  let index = 0;
  while (index < cards.length) {
    const quota = cards[index];
    const rank = quota.rank;
    if (!rank) break;
    const yellow = rank <= 6 ? 0 : rank <= 9 ? 1 : rank <= 12 ? 3 : rank === 13 ? 6 : 0;
    if (yellow) marks.set(String(quota.id), { yellow, purple: 0 });
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
    const current = marks.get(key) || { yellow: 0, purple: 0 };
    current.purple += purple;
    marks.set(key, current);
  }
  return marks;
}

function render() {
  if (!state || state.phase === "lobby") {
    app.innerHTML = `
      <h1>Quota</h1>
      <p>場札からノルマ札を取り、同じ種類を集めてノルマを達成する。</p>
      <form class="panel" id="start">
        <div class="row">
          <label>アイテムセット
            <select name="item_set">
              ${(state.item_sets || []).map((item) => `<option value="${item.id}" ${item.default ? "selected" : ""}>${item.name}</option>`).join("")}
            </select>
          </label>
          <label>人数
            <select name="players"><option value="3">3</option><option value="4">4</option></select>
          </label>
          <label>人間の席
            <select name="humans"><option>4</option><option selected>3</option><option>2</option><option>1</option><option>0</option></select>
          </label>
          <label>シード（空ならランダム）
            <input name="seed" inputmode="numeric">
          </label>
          <label><span>上級</span>
            <input name="sequence" type="checkbox"> 並び順ボーナス
          </label>
        </div>
        <p><button class="primary" type="submit">スタート</button></p>
        <p class="note">この画面を開いた端末が同じ盤面を共有します。同じネットワークの他の端末からも操作できます。</p>
      </form>`;
    app.querySelector("#start").onsubmit = async (event) => {
      event.preventDefault();
      const data = new FormData(event.target);
      const players = Number(data.get("players"));
      let humans = Number(data.get("humans"));
      if (humans > players) humans = players;
      await post("/api/start", {
        players,
        humans,
        seed: data.get("seed"),
        sequence: data.get("sequence") === "on",
        item_set: data.get("item_set"),
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
    return `<section class="seat${alt}${turn}" data-seat="${index}">
      <div class="bar"><strong>${index === focus && !state.finished ? "▶ " : ""}${player.name}</strong>
        <span>${score.plus}<span class="points">${score.points}</span>点${bankHtml(index)}</span></div>
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
  let controls = "";
  if (state.settling) {
    controls = "";
  } else if (state.current_human) {
    if (!me.quota) {
      controls = `<div class="controls">
        <div class="control-buttons"><button type="button" id="pass">パス</button></div>
        <p>場札からノルマ札を選びましょう。</p>
      </div>`;
    } else {
      const done = me.collection.length > 0;
      controls = `<div class="controls">
        <div class="control-buttons">
          <button type="button" id="abandon">放棄</button>
          <button type="button" id="pass">${done ? "次へ" : "パス"}</button>
        </div>
        <p>ノルマ達成まであと${me.need}枚。</p>
      </div>`;
    }
  } else if (!state.finished) {
    controls = `<p class="note">${me.name} が考えています。</p>`;
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
      <h1>Quota</h1>
      ${state.finished ? "" : `<button type="button" id="restart">途中でやめて最初からやり直す</button>`}
    </div>
    <p class="note">手番 ${state.turn_number} / 山札 ${state.deck_count}
      / 膠着状態 ${state.stall_count} / 連続パス ${state.no_gain_streak}/${state.player_count}
      ${state.sequence_rule ? " / 上級" : ""}</p>
    <section class="panel">
      <div class="market-label">場札</div>
      <div class="market" id="market">${market}</div>
      ${controls}
    </section>
    ${seats}
    ${state.finished && bonusCashed && !scoreAnim.size ? finishHtml() : ""}`;

  const restart = app.querySelector("#restart");
  if (restart) restart.onclick = () => post("/api/reset", {});
  app.querySelectorAll(".pick").forEach((button) => {
    button.onclick = () => onPick(Number(button.parentElement.dataset.id));
  });
  const pass = app.querySelector("#pass");
  if (pass) pass.onclick = () => post("/api/action", { kind: "pass" });
  const abandon = app.querySelector("#abandon");
  if (abandon) abandon.onclick = () => post("/api/action", { kind: "abandon" });
  const ok = app.querySelector("#ok");
  if (ok) ok.onclick = () => post("/api/reset", {});
  if (!recordPrimed && state.phase === "playing") {
    document.querySelectorAll(".line.record .card").forEach((card) => recordSeen.add(card.dataset.id));
    recordPrimed = true;
  }
  queueGather();
  maybeCash();
}

function finishHtml() {
  const reason = state.end_reason === "DECK" ? "山札切れ" : "膠着の連続";
  let place = 1;
  const lines = state.ranking.map((group) => {
    const text = group.map((seat) => {
      const player = state.players[seat];
      return `${place}位 ${player.name} ${player.score}点（達成${player.achieve_count} / 最高${player.max_single_score}）`;
    }).join("<br>");
    place += group.length;
    return text;
  }).join("<br>");
  return `<div class="overlay"><div class="panel"><h2>${reason}</h2><p>${lines}</p><button class="primary" id="ok">OK</button></div></div>`;
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
  const step = cardW * 0.68 / 2;
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
    flushMarket();
    queueGather();
    maybeCash();
  });
}

function applyState(next) {
  const event = next.event;
  const fresh = event && event.n !== seenEvent && event.cards && event.cards.length;
  const lifted = fresh ? liftMarketCards(event.cards) : [];
  if (event) seenEvent = event.n;
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
    banks.clear();
    marksTaken.clear();
    recordSeen.clear();
    recordPrimed = false;
    gathering = false;
    bonusCashed = false;
    lockedScore.clear();
    inFlight = new Set();
    hiding = new Set();
    parked = new Set();
  }
  render();
  hiding = new Set();
  const afterFlight = () => {
    if (pause) {
      setTimeout(() => hopParked(pause.ids.filter((id) => parked.has(id))), pause.ms);
      return;
    }
    flushMarket();
    queueGather();
    maybeCash();
  };
  if (lifted.length) flyLifted(lifted, afterFlight);
  else afterFlight();
  if (!next.settling && !inFlight.size && !parked.size) flushMarket();
}

function queueGather() {
  if (gathering) return;
  const dots = [...document.querySelectorAll(".line.record .marks span")];
  if (!dots.length) return;
  gathering = true;
  let left = dots.length;
  const gained = new Map();
  for (const dot of dots) {
    const card = dot.closest(".card");
    const seat = dot.closest("[data-seat]");
    const seatIndex = seat ? Number(seat.dataset.seat) : 0;
    const kind = dot.textContent === "🟣" ? "purple" : "yellow";
    const box = gained.get(seatIndex) || { yellow: 0, purple: 0, ids: [] };
    box[kind] += 1;
    if (card) box.ids.push(card.dataset.id);
    gained.set(seatIndex, box);
    const from = dot.getBoundingClientRect();
    const park = seat && (seat.querySelector(".marker-bank") || seat.querySelector(".points"));
    const ghost = document.createElement("span");
    ghost.className = "gain-dot";
    ghost.textContent = dot.textContent;
    ghost.style.left = `${from.left}px`;
    ghost.style.top = `${from.top}px`;
    document.body.appendChild(ghost);
    const finish = () => {
      ghost.remove();
      left -= 1;
      if (left > 0) return;
      for (const [index, box] of gained) {
        const banked = banks.get(index) || { yellow: 0, purple: 0 };
        banked.yellow += box.yellow;
        banked.purple += box.purple;
        banks.set(index, banked);
        box.ids.forEach((id) => marksTaken.add(id));
      }
      gathering = false;
      render();
      flushMarket();
      maybeCash();
    };
    requestAnimationFrame(() => {
      const target = park ? park.getBoundingClientRect() : from;
      ghost.style.left = `${target.left}px`;
      ghost.style.top = `${target.top}px`;
      setTimeout(finish, 450);
    });
  }
}

function maybeCash() {
  if (!state || !state.finished || bonusCashed || gathering) return;
  if (state.settling || inFlight.size || parked.size) return;
  if (document.querySelector(".line.record .marks span")) {
    queueGather();
    return;
  }
  bonusCashed = true;
  state.players.forEach((player, index) => {
    const bank = banks.get(index) || { yellow: 0, purple: 0 };
    const extra = bank.yellow + bank.purple;
    banks.set(index, { yellow: 0, purple: 0 });
    if (!extra) return;
    const from = baseScore(player);
    scoreAnim.set(index, { from, to: from + extra, at: Date.now() });
  });
  if (scoreAnim.size && !scoreTimer) scoreTimer = setInterval(tickScores, 80);
  render();
}

function layoutMarket(next) {
  const incoming = next.market || [];
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
  if ((state && state.settling) || inFlight.size || parked.size || gathering) return;
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

async function post(url, body) {
  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  applyState(await response.json());
}

async function poll() {
  const response = await fetch("/api/state");
  const next = await response.json();
  const changed = !state || next.event_n !== state.event_n || next.phase !== state.phase || next.settling !== state.settling;
  if (changed) applyState(next);
}

poll();
setInterval(poll, 700);
