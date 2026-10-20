import { titleAwards, sequencePoints } from './engine.mjs';
export const TITLES = ['単色達成', '生粋の買い付け', '五種の品揃え'];
export function displayOrder(game) {
  const anchor = game.players.findIndex(player => player.human);
  const start = anchor < 0 ? 0 : Math.max(0, game.turnOrder.indexOf(anchor));
  return game.turnOrder.map((_, i) => game.turnOrder[(i + start) % game.playerCount]);
}
export function titleStates(player, verdict = false) {
  const awarded = new Set(titleAwards(player).map(award => award.name));
  return TITLES.map((name, index) => ({ name,
    earned: awarded.has(name) && (verdict || index === 2),
    impossible: verdict ? !awarded.has(name) : index === 0 ? new Set(player.bundles.map(bundle => bundle.kind)).size > 1 : index === 1 ? player.bundles.some(bundle => bundle.hasWild) : false,
  }));
}
export function roundBreakdown(player, previous) {
  return { base: player.achieved.length, green: player.score - previous - player.achieved.length,
    purple: sequencePoints(player.achieved), blue: titleAwards(player).reduce((sum, award) => sum + award.points, 0),
    previous, total: player.score - previous + sequencePoints(player.achieved) + titleAwards(player).reduce((sum, award) => sum + award.points, 0) };
}
export function scoreRanks(scores, order = scores.map((_, seat) => seat)) {
  const sorted = [...order].sort((a, b) => scores[b] - scores[a]);
  let rank = 1;
  return sorted.map((seat, index) => { if (index && scores[seat] !== scores[sorted[index - 1]]) rank = index + 1; return { seat, rank, score: scores[seat] }; });
}
export function winnerText(players, rows) {
  const names = rows.filter(row => row.rank === 1).map(row => players[row.seat].name + 'さん');
  return names.length > 1 ? `今回は、${names.join('、')}の同点優勝です。おめでとうございます。` : `${names[0]}、総合優勝おめでとうございます。`;
}
export const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' })[char]);
function table(rows) { return `<div class="table-wrap"><table><tbody>${rows.map((row, i) => `<tr>${row.map(cell => `<${i ? 'td' : 'th'}>${escapeHtml(cell.trim())}</${i ? 'td' : 'th'}>`).join('')}</tr>`).join('')}</tbody></table></div>`; }
export function guideHtml(text) {
  const sections = []; let section, panel;
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || /^\*\*/.test(line)) continue;
    if (/^【/.test(line)) { section = { heading: line.replace(/[【】]/g, ''), panels: [] }; sections.push(section); panel = null; continue; }
    if (!section) continue;
    if (/^- /.test(raw)) { panel = { text: line.slice(2), notes: [], rows: [], titles: [] }; section.panels.push(panel); }
    else if (panel && /^\s+- /.test(raw)) { const [name, ...rest] = line.slice(2).split(/[:：]/); panel.titles.push([name, rest.join('：')]); }
    else if (panel && line.startsWith('|')) { if (!/^\|[-:| ]+\|$/.test(line)) panel.rows.push(line.slice(1, -1).split('|')); }
    else if (panel) panel.notes.push(line);
  }
  return sections.map(section => `<section class="guide-section"><h3>${escapeHtml(section.heading)}</h3>${section.panels.map(panel => `<div class="guide-panel"><p>${escapeHtml(panel.text)}</p>${panel.notes.map(note => `<p class="guide-note">${escapeHtml(note)}</p>`).join('')}${panel.rows.length ? table(panel.rows) : ''}${panel.titles.length ? table([['称号', '条件・得点'], ...panel.titles]) : ''}</div>`).join('')}</section>`).join('');
}
