'use strict';
/* The sparring partner of the online arena (`ar-cpu`): a CPU wrestler that plays the same sim through the same inputs as
   a person. It sees the bout `react` ticks late (a person's reaction time), so what it can answer depends on its level:
   1 is a gentle coach, 3 punishes slow moves and reads throws. Its choices come from a seeded generator, so a sparring
   bout with the same seed and the same inputs plays out the same way. */

const S = require('./arena-sim.js');
const { B } = S;

const LEVELS = {
  1: { react: 16, read: 0.25, parry: 0.05, punish: 0.25, aggro: 0.35, cancel: 0 },
  2: { react: 11, read: 0.45, parry: 0.15, punish: 0.6, aggro: 0.5, cancel: 0.4 },
  3: { react: 7, read: 0.65, parry: 0.3, punish: 0.9, aggro: 0.6, cancel: 0.85 },
};

function rng(seed) { // mulberry32
  let a = seed >>> 0;
  return () => { a = (a + 0x6d2b79f5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}

function createBot(side, level = 2, seed = 1) {
  const lv = LEVELS[level] || LEVELS[2], rand = rng(seed), seen = [];
  let plan = [], wait = 0, lastOppSt = '', holdBrace = 0;
  const towardBit = (me) => (me.face > 0 ? B.RIGHT : B.LEFT);
  const awayBit = (me) => (me.face > 0 ? B.LEFT : B.RIGHT);

  /** The inputs for this tick: { held, pressed }. */
  function think(m) {
    const me = m.f[side];
    seen.push(m.f[1 - side] && { st: m.f[1 - side].st, sf: m.f[1 - side].sf, x: m.f[1 - side].x, kind: m.f[1 - side].kind });
    if (seen.length > 40) seen.shift();
    const op = seen[Math.max(0, seen.length - 1 - lv.react)]; // what it has noticed by now
    if (m.phase !== 'fight' || !op) { plan = []; return { held: 0, pressed: 0 }; }
    if (plan.length) { const next = plan.shift(); return next; }
    const T = towardBit(me), A = awayBit(me), dist = Math.abs(me.x - op.x);

    if (m.clinch) {
      const lead = m.clinch.lead === side;
      if (lead && me.st === 'clinch_lead') {
        if (wait > 0) { wait--; return { held: 0, pressed: 0 }; }
        wait = 6 + Math.floor(rand() * 24);
        if (me.met >= S.MET) return { held: 0, pressed: B.SP };
        if (op.st === 'counter' && rand() < lv.read) return { held: 0, pressed: B.L }; // it saw the guess: jolt
        const r = rand();
        if (r < 0.15) return { held: 0, pressed: B.L };
        const dir = r < 0.45 ? T : r < 0.75 ? A : 0;
        return { held: dir, pressed: B.H };
      }
      if (!lead && me.st === 'clinch_def') {
        if (op.st && op.st.startsWith('t_') && op.st !== lastOppSt) {
          lastOppSt = op.st;
          if (rand() < lv.read) { const kind = op.st.slice(2); return { held: kind === 'hook' ? T : kind === 'hip' ? A : 0, pressed: B.G }; }
          return { held: 0, pressed: 0 };
        }
        if (!op.st.startsWith('t_')) lastOppSt = '';
        if (me.sta > 400 && rand() < 0.02) return { held: 0, pressed: B.L };
      }
      return { held: 0, pressed: 0 };
    }

    const actionable = me.st === 'idle' || me.st === 'walk_f' || me.st === 'walk_b' || me.st === 'guard';
    // a shove that landed: cancel into a sweep while the cancel window is open
    if (me.st === 'shove' && me.done && rand() < lv.cancel) return { held: 0, pressed: B.H };
    if (!actionable) return { held: holdBrace-- > 0 ? B.BR : 0, pressed: 0 };
    const mv = S.MOVES[op.st];
    // its strike is coming: brace, maybe parry
    if (mv && !mv.grab && op.sf < mv.su && dist <= mv.reach + 30) {
      if (rand() < lv.parry) { holdBrace = 4; return { held: B.BR, pressed: B.BR }; }
      if (rand() < 0.5 + lv.parry) { holdBrace = 8; return { held: B.BR, pressed: 0 }; }
    }
    // it whiffed: punish the recovery
    if (mv && op.sf >= mv.su + mv.ac && dist <= 150 && rand() < lv.punish) {
      return dist <= 110 ? { held: 0, pressed: B.L } : { held: 0, pressed: B.H };
    }
    if (me.met >= S.MET && dist <= 160 && rand() < 0.3) return { held: T, pressed: B.SP };
    if (dist > 180) {
      if (rand() < 0.05 && me.sta > 300) return { held: T, pressed: B.DF };
      return { held: T, pressed: 0 };
    }
    if (rand() > lv.aggro) { // wait a little, or step back
      if (rand() < 0.3) { plan = Array.from({ length: 6 }, () => ({ held: A, pressed: 0 })); return { held: A, pressed: 0 }; }
      if (rand() < 0.3) { holdBrace = 10; return { held: B.BR, pressed: 0 }; }
      return { held: 0, pressed: 0 };
    }
    if (dist <= 85) {
      const r = rand();
      return r < 0.45 ? { held: 0, pressed: B.G } : r < 0.8 ? { held: 0, pressed: B.L } : { held: B.BR, pressed: 0 };
    }
    if (dist <= 130) { const r = rand(); return r < 0.4 ? { held: T, pressed: B.L } : r < 0.7 ? { held: 0, pressed: B.H } : { held: T, pressed: B.G }; }
    return rand() < 0.5 ? { held: 0, pressed: B.H } : { held: T, pressed: 0 };
  }
  return { think };
}

module.exports = { createBot, LEVELS };
