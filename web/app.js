const app = document.querySelector("#app");
let state = null;
let seenEvent = 0;
let picked = [];

let hiding = new Set();

function cardHtml(card, z = 1) {
  const hidden = hiding.has(card.id) ? "incoming" : "";
  const rank = card.face ? `<div class="rank">${card.face}</div>` : "";
  return `<div class="card ${card.joker ? "joker" : ""} ${hidden}" data-id="${card.id}" style="z-index:${z}">
    ${rank}
    <div class="emoji">${card.emoji}</div>
    <div>${card.goods}</div>
  </div>`;
}

function render() {
  if (!state || state.phase === "lobby") {
    app.innerHTML = `
      <h1>Quota</h1>
      <p>港の市場で注文を請け負い、同じ商品をそろえて納品する。</p>
      <form class="panel" id="start">
        <div class="row">
          <label>人数
            <select name="players"><option value="3">3</option><option value="4">4</option></select>
          </label>
          <label>人間の席
            <select name="humans"><option>4</option><option selected>3</option><option>2</option><option>1</option><option>0</option></select>
          </label>
          <label>シード（空ならランダム）
            <input name="seed" inputmode="numeric">
          </label>
          <label><span>上級ルール</span>
            <input name="sequence" type="checkbox"> 積み付けボーナス
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
      });
    };
    return;
  }

  const seats = state.players.map((player, index) => {
    const turn = index === state.current && !state.finished ? " turn" : "";
    const order = player.quota
      ? cardHtml(player.quota, 1) + player.collection.map((card, index) => cardHtml(card, index + 2)).join("")
      : "<span class='note'>注文なし</span>";
    const need = player.quota ? `<span class="note">あと ${player.need} 枚</span>` : "";
    const done = player.achieved.length
      ? player.achieved.map((card, index) => cardHtml(card, index + 1)).join("")
      : "<span class='note'>なし</span>";
    const seq = state.sequence_rule ? ` / 積み付け ${player.sequence_bonus}` : "";
    return `<section class="seat${turn}" data-seat="${index}">
      <div class="bar"><strong>${index === state.current && !state.finished ? "▶ " : ""}${player.name}</strong>
        <span>${player.score}点${seq} / 納品 ${player.achieve_count}</span></div>
      <div class="note">注文 ${need}</div>
      <div class="line order">${order}</div>
      <div class="note">出荷記録</div>
      <div class="line record">${done}</div>
    </section>`;
  }).join("");

  const me = state.players[state.current];
  let controls = "";
  if (state.current_human) {
    if (!me.quota) {
      controls = `<p>カードを押すと注文を請け負います。</p>
        <button type="button" id="pass">パス</button>`;
    } else {
      controls = `<p>買い付ける順にカードを押す（残り ${me.need} 枚まで）。選んだ順が積み付けの順です。</p>
        <button type="button" id="collect" class="primary">買い付ける</button>
        <button type="button" id="abandon">注文を取り消す</button>
        <button type="button" id="pass">パス</button>`;
    }
  } else if (!state.finished) {
    controls = `<p class="note">${me.name} が考えています。</p>`;
  }

  const market = state.market.map((card) => {
    const on = picked.includes(card.id) ? "selected" : "";
    const mark = picked.includes(card.id) ? `<div>${picked.indexOf(card.id) + 1}</div>` : "";
    const rank = card.face ? `<div class="rank">${card.face}</div>` : "";
    return `<div class="card ${card.joker ? "joker" : ""} ${on}" data-id="${card.id}">
      <button type="button" class="pick">${rank}<div class="emoji">${card.emoji}</div><div>${card.goods}</div>${mark}</button>
    </div>`;
  }).join("");

  app.innerHTML = `
    <div class="bar">
      <h1>Quota</h1>
      <button type="button" id="restart">途中でやめて最初からやり直す</button>
    </div>
    <p class="note">手番 ${state.turn_number} / 山札 ${state.deck_count} / 連続パス ${state.no_gain_streak}
      / 入港後まだ取引なし ${state.stall_flag ? "あり" : "なし"}
      ${state.sequence_rule ? " / 上級ルール" : ""}</p>
    <section class="panel">
      <div>市場</div>
      <div class="market" id="market">${market}</div>
      ${controls}
    </section>
    ${seats}
    ${state.finished ? finishHtml() : ""}`;

  app.querySelector("#restart").onclick = () => post("/api/reset", {});
  app.querySelectorAll(".pick").forEach((button) => {
    button.onclick = () => onPick(Number(button.parentElement.dataset.id));
  });
  const pass = app.querySelector("#pass");
  if (pass) pass.onclick = () => post("/api/action", { kind: "pass" });
  const abandon = app.querySelector("#abandon");
  if (abandon) abandon.onclick = () => post("/api/action", { kind: "abandon" });
  const collect = app.querySelector("#collect");
  if (collect) {
    collect.onclick = () => {
      if (!picked.length) return;
      const ids = picked.slice();
      picked = [];
      post("/api/action", { kind: "collect", card_ids: ids });
    };
  }
  const ok = app.querySelector("#ok");
  if (ok) ok.onclick = () => post("/api/reset", {});
}

function finishHtml() {
  const reason = state.end_reason === "DECK" ? "季節風の終わり（山札切れ）" : "交易の途絶（膠着の連続）";
  let place = 1;
  const lines = state.ranking.map((group) => {
    const text = group.map((seat) => {
      const player = state.players[seat];
      return `${place}位 ${player.name} ${player.score}点（納品${player.achieve_count} / 最高${player.max_single_score}）`;
    }).join("<br>");
    place += group.length;
    return text;
  }).join("<br>");
  return `<div class="overlay"><div class="panel"><h2>${reason}</h2><p>${lines}</p><button class="primary" id="ok">OK</button></div></div>`;
}

function onPick(id) {
  const me = state.players[state.current];
  if (!state.current_human) return;
  if (!me.quota) {
    const card = state.market.find((item) => item.id === id);
    if (!card || card.joker) return;
    post("/api/action", { kind: "take", card_id: id });
    return;
  }
  if (picked.includes(id)) {
    picked = picked.filter((n) => n !== id);
  } else if (picked.length < me.need) {
    const card = state.market.find((item) => item.id === id);
    if (!card) return;
    const quota = me.quota;
    if (card.joker || card.goods === quota.goods) picked.push(id);
  }
  render();
}

function liftMarketCards(cards) {
  if (!cards || !cards.length || !state || state.event_n === undefined) return [];
  const lifted = [];
  for (const card of cards) {
    const el = document.querySelector(`#market [data-id="${card.id}"]`);
    if (!el) continue;
    const rect = el.getBoundingClientRect();
    el.classList.add("lifting");
    el.style.left = `${rect.left}px`;
    el.style.top = `${rect.top}px`;
    el.style.width = `${rect.width}px`;
    el.style.height = `${rect.height}px`;
    document.body.appendChild(el);
    lifted.push(el);
  }
  return lifted;
}

function flyLifted(lifted) {
  requestAnimationFrame(() => {
    for (const el of lifted) {
      const dest = document.querySelector(`[data-seat] [data-id="${el.dataset.id}"]`);
      if (!dest) {
        el.remove();
        continue;
      }
      const to = dest.getBoundingClientRect();
      const done = () => {
        el.remove();
        dest.classList.remove("incoming");
      };
      el.addEventListener("transitionend", done, { once: true });
      el.style.left = `${to.left}px`;
      el.style.top = `${to.top}px`;
      setTimeout(done, 600);
    }
  });
}

function applyState(next) {
  const event = next.event;
  const fresh = event && event.n !== seenEvent && event.cards && event.cards.length;
  const lifted = fresh ? liftMarketCards(event.cards) : [];
  if (event) seenEvent = event.n;
  hiding = new Set(lifted.map((el) => el.dataset.id));
  state = next;
  if (state.phase === "lobby") picked = [];
  render();
  hiding = new Set();
  if (lifted.length) flyLifted(lifted);
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
  const changed = !state || next.event_n !== state.event_n || next.phase !== state.phase;
  if (changed) applyState(next);
}

render();
setInterval(poll, 700);
