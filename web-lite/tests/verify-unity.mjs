import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Game, finalScore } from '../engine.mjs';
import { chooseCpuAction, bindCpuProfiles } from '../cpu.mjs';
const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const monoRoot = process.env.QUOTA_MONO_ROOT || '/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge';
const mono = path.join(monoRoot, 'bin/mono');
if (!existsSync(mono)) throw Error('Unity Mono not found. Set QUOTA_MONO_ROOT to MonoBleedingEdge.');
const temp = mkdtempSync(path.join(tmpdir(), 'quota-parity-'));
function run(command, args) {
  const result = spawnSync(command, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (result.status !== 0) throw Error(result.stderr || result.stdout || result.error?.message);
  return result.stdout;
}
const ids = cards => cards.map(card => card?.id ?? -1).join(',');
function snapshot(g, action) {
  return [action, g.roundIndex, g.current, g.turnNumber, g.plan, g.doubleStage, +g.turnGain, +g.doubleGained,
    g.noGainStreak, +g.stallFlag, g.reshuffleCount, +g.finished, +g.awaitingNextRound, g.roundEndReason || '',
    g.goods.join(','), g.turnOrder.join(','), ids(g.deck), ids(g.removed), ids(g.market), ids(g.discard),
    ...g.players.map(p => [p.quota?.id ?? -1, ids(p.collection), ids(p.achieved), p.score, finalScore(p), p.achieveCount,
      p.maxSingleScore, p.reshuffleLeft, p.doubleLeft, p.bundles.map(b => b.kind + +b.hasWild).join(',')].join(';'))].join('|');
}
try {
  const exe = path.join(temp, 'reference.exe');
  run(mono, [path.join(monoRoot, 'lib/mono/4.5/csc.exe'), '-nologo', '-out:' + exe,
    ...['Cards', 'Engine', 'PythonRandom', 'CpuProfiles'].map(name => path.join(repo, 'unity/Assets/Scripts/Quota', name + '.cs')),
    path.join(here, 'unity-reference.cs')]);
  let matches = 0, transitions = 0;
  const seeds = process.env.QUOTA_PARITY_SEEDS ? process.env.QUOTA_PARITY_SEEDS.split(',').map(Number) : [0, 1, 42, 2026];
  const modes = (process.env.QUOTA_PARITY_ROUND_MODES || 'players').split(',');
  for (const seed of seeds) for (const count of [3, 4]) for (const strength of [0, 1, 2]) for (const offset of [0, 3]) for (const mode of modes) {
    const rounds = mode === 'single' ? 1 : mode === 'double' ? count * 2 : count;
    const reference = run(mono, [exe, seed, count, strength, offset, rounds].map(String)).trim().split(/\r?\n/);
    const profiles = Array.from({ length: count }, (_, i) => (i + offset) % 6);
    const g = new Game({ seed, playerCount: count, rounds, cpuIds: profiles, spectator: true });
    bindCpuProfiles(g, profiles, strength);
    const actual = [snapshot(g, 'start')];
    let guard = 0;
    while (!g.finished && guard++ < 10000) {
      if (g.awaitingNextRound) { g.beginNextRound(); actual.push(snapshot(g, 'next')); continue; }
      const plan = g.plan, seat = g.current;
      const a = chooseCpuAction(g, strength, profiles[seat]);
      const declaration = plan === g.plan ? '' : g.plan + '/';
      const key = a.type === 'take' ? `take:${a.id}` : a.type === 'collect' ? `collect:${a.ids.join(',')}` : a.type;
      g.step(a); actual.push(snapshot(g, declaration + key));
    }
    const mismatch = actual.findIndex((line, i) => line !== reference[i]);
    if (mismatch >= 0 || reference.length !== actual.length) {
      const i = mismatch >= 0 ? mismatch : actual.length;
      throw Error(`Mismatch seed=${seed} players=${count} rounds=${rounds} strength=${strength} offset=${offset} transition=${i}\nUnity: ${reference[i]}\nWeb:   ${actual[i]}`);
    }
    matches++; transitions += actual.length;
  }
  console.log(`Unity parity: ${matches} matches, ${transitions} transitions; all states, actions, decks and scores identical.`);
} finally { rmSync(temp, { recursive: true, force: true }); }
