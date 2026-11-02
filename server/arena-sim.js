'use strict';
/* Бөхийн өргөө (the online wrestling arena): one bout between two wrestlers, simulated 30 times a second on the server.
   Nothing here is random: the same inputs always give the same bout, so the better wrestler wins. Each move has frame
   data (startup, active and recovery ticks), and every option has an answer:
     - shove / push / sweep / charge (strikes) are stopped by bracing (guard), and a brace pressed just before the hit
       parries it and leaves the attacker staggered;
     - grabs go through a brace, but a strike interrupts a grab while it is reaching;
     - in the clinch (барьцаа) the one who holds the grip throws (hook, hip toss or lift, each with its own tell), and the
       other reads the throw and answers with the same direction + grip to turn it over; guessing early is punished by
       the jolt, and the shove-off escapes the clinch.
   Balance is the life bar (whose knee or back touches the ground first loses the round), stamina pays for every move and
   the eagle meter fills toward the champion's throw. See docs/PROTOCOL.md, "Online arena". */

const TICK_HZ = 30;
const STAGE_W = 1200, WALL_L = 60, WALL_R = 1140, START = [470, 730], BODY = 64;
const BAL = 1000, STA = 1000, MET = 1000;
const ROUND_TICKS = 60 * TICK_HZ, INTRO_TICKS = 75, KO_TICKS = 105, ROUNDS_TO_WIN = 2, ROUNDS_MAX = 5;
const BUFFER = 6, PARRY_WINDOW = 5, PARRY_LOCK = 20, CLINCH_GAP = 70, CLINCH_MAX = 180;

/** Input bits: held directions and buttons as the client sends them (absolute left/right; the sim turns them into
 *  toward/away). DF/DB are a double-tap dash, detected by the client and sent as presses. */
const B = { LEFT: 1, RIGHT: 2, UP: 4, DOWN: 8, L: 16, H: 32, G: 64, BR: 128, SP: 256, DF: 512, DB: 1024 };
const INPUT_MASK = 2047;
const PRESS_ORDER = [B.SP, B.G, B.H, B.L, B.DF, B.DB]; // which buffered press wins when several came in one tick

/* Strikes and grabs from neutral. su = startup ticks, ac = active ticks, rc = recovery ticks, reach = centre distance it
   hits at, adv = steps forward per startup tick, hs/bs = hitstun/blockstun, chip = stamina a brace pays, kd = knocks down. */
const MOVES = {
  shove: { su: 5, ac: 3, rc: 9, reach: 118, dmg: 50, hs: 15, bs: 11, push: 46, bpush: 34, cost: 60, chip: 40, cancel: true },
  push: { su: 8, ac: 4, rc: 14, reach: 128, adv: 6, dmg: 80, hs: 18, bs: 12, push: 64, bpush: 48, cost: 90, chip: 70 },
  sweep: { su: 12, ac: 4, rc: 20, reach: 150, dmg: 130, kd: true, bs: 14, push: 0, bpush: 40, cost: 140, chip: 120 },
  charge: { su: 14, ac: 5, rc: 18, reach: 118, adv: 10, dmg: 110, hs: 22, bs: 16, push: 120, bpush: 90, cost: 150, chip: 230 },
  grab: { su: 6, ac: 3, rc: 22, reach: 82, grab: true, cost: 80 },
  lunge: { su: 12, ac: 4, rc: 26, reach: 90, adv: 9, grab: true, cost: 140 },
  super: { su: 8, ac: 5, rc: 40, reach: 112, adv: 15, grab: true, sup: true, inv: 9, cost: 0 },
};
/* The clinch. Throws land at the end of their startup (the tell is the wrestler's pose); counter stances are active for
   `on` ticks and then can't be used again until `rc`; the jolt catches a guess; the shove-off escapes. */
const THROWS = { hook: { su: 14, dmg: 170, cost: 120 }, hip: { su: 14, dmg: 190, cost: 120 }, lift: { su: 18, dmg: 240, cost: 180 } };
const CLINCH = { counterOn: 9, counterRc: 24, counterCost: 60, joltSu: 6, joltRc: 10, joltDmg: 30, joltCatch: 45, joltCost: 50,
  escapeSu: 10, escapeCost: 150, reversalDmg: 150, guessBonus: 1.25, leadDrain: 3, defRegen: 4, superSu: 10, superDmg: 320 };
const SUPER_DMG = 300;
const DOWN = 38, GETUP = 12, THROWING = 20, THROWN = 20, TRIP = 10;

const ACTIONABLE = new Set(['idle', 'walk_f', 'walk_b', 'guard']);
const STRIKE_INVULN = new Set(['down', 'getup', 'trip', 'thrown', 'fall', 'win']);

function fighter(i) {
  return { x: START[i], face: i === 0 ? 1 : -1, st: 'idle', sf: 0, dur: 0, next: null, mv: null, done: false,
    bal: BAL, sta: STA, met: 0, held: 0, prev: 0, pend: 0, buf: [], parryAt: -99, parryLock: -99, combo: 0, dealt: 0, kind: null };
}

/** A new bout. `seed` only names it (nothing random happens in the sim). */
function createMatch() {
  return { tick: 0, phase: 'intro', pt: 0, round: 1, wins: [0, 0], timer: ROUND_TICKS, freeze: 0, f: [fighter(0), fighter(1)],
    clinch: null, events: [], rounds: [], result: null };
}

function enter(f, st, dur = 0, next = null) {
  f.st = st; f.sf = 0; f.dur = dur; f.next = next; f.mv = null; f.done = false; f.kind = null;
}
const toward = (f) => (f.face > 0 ? B.RIGHT : B.LEFT);
const away = (f) => (f.face > 0 ? B.LEFT : B.RIGHT);
function dirOf(f) { // 'f' toward, 'b' away, '' neither (or both)
  const t = (f.held & toward(f)) !== 0, a = (f.held & away(f)) !== 0;
  return t && !a ? 'f' : a && !t ? 'b' : '';
}
function ev(m, k, by, extra) { m.events.push({ k, by, ...extra }); }

/** Feeds a fighter's inputs for this tick: held is what is held now, pressed the buttons pressed since the last tick
 *  (so a tap shorter than a tick is not lost). */
function input(m, i, held, pressed = 0) {
  const f = m.f[i];
  f.held = held & INPUT_MASK;
  f.pend |= (pressed & INPUT_MASK);
}

function takeInputs(m, f) {
  const edges = (f.pend | (f.held & ~f.prev)) & INPUT_MASK;
  f.prev = f.held; f.pend = 0;
  if (edges & B.BR) {
    if (m.tick - f.parryLock >= PARRY_LOCK) { f.parryAt = m.tick; f.parryLock = m.tick; }
  }
  for (const b of PRESS_ORDER) if (edges & b) f.buf.push({ b, k: m.tick });
  f.buf = f.buf.filter((p) => m.tick - p.k < BUFFER).slice(-6);
}
/** The newest buffered press of these buttons (removed), or 0. */
function press(m, f, mask) {
  for (let i = f.buf.length - 1; i >= 0; i--) if (f.buf[i].b & mask) { const b = f.buf[i].b; f.buf.splice(i, 1); return b; }
  return 0;
}

function startMove(m, f, name) {
  const mv = MOVES[name];
  if (mv.sup) { if (f.met < MET) return false; f.met = 0; }
  else if (f.sta < mv.cost) { ev(m, 'tired', m.f.indexOf(f)); return false; }
  else f.sta -= mv.cost;
  enter(f, name, mv.su + mv.ac + mv.rc); f.mv = mv;
  if (mv.sup) ev(m, 'super', m.f.indexOf(f));
  return true;
}

/** What an actionable fighter does this tick. `cancel` is true inside a shove's on-hit cancel window: only a stronger strike. */
function decide(m, f, cancel = false) {
  const d = dirOf(f);
  const b = cancel ? press(m, f, B.H | B.L) : press(m, f, B.SP | B.G | B.H | B.L | B.DF | B.DB);
  if (cancel) {
    if (b === B.H) return startMove(m, f, d === 'f' ? 'charge' : 'sweep');
    if (b === B.L && d === 'f') return startMove(m, f, 'push');
    return false;
  }
  if (b === B.SP) { if (f.met >= MET) return startMove(m, f, 'super'); }
  else if (b === B.G) return startMove(m, f, d === 'f' ? 'lunge' : 'grab');
  else if (b === B.H) return startMove(m, f, d === 'f' ? 'charge' : 'sweep');
  else if (b === B.L) return startMove(m, f, d === 'f' ? 'push' : 'shove');
  else if (b === B.DF || b === B.DB) {
    if (f.sta < 80) { ev(m, 'tired', m.f.indexOf(f)); return false; }
    f.sta -= 80; enter(f, b === B.DF ? 'dash_f' : 'dash_b', b === B.DF ? 10 : 12); return true;
  }
  if (f.held & B.BR) { if (f.st !== 'guard') enter(f, 'guard'); return true; }
  const tired = f.sta < 150;
  if (d === 'f') { if (f.st !== 'walk_f') enter(f, 'walk_f'); f.x += f.face * (tired ? 5 : 7); return true; }
  if (d === 'b') { if (f.st !== 'walk_b') enter(f, 'walk_b'); f.x -= f.face * (tired ? 4 : 5); return true; }
  if (f.st !== 'idle') enter(f, 'idle');
  return true;
}

function advance(f) {
  f.sf++;
  if (f.st === 'dash_f' && f.sf <= 8) f.x += f.face * 16;
  if (f.st === 'dash_b' && f.sf <= 8) f.x -= f.face * 14;
  if (f.mv && f.mv.adv && f.sf <= f.mv.su) f.x += f.face * f.mv.adv;
  if (f.dur && f.sf >= f.dur) {
    const nx = f.next; enter(f, nx ? nx.st : 'idle', nx ? nx.dur : 0, nx ? nx.next : null);
  }
}

function invulnerable(f, grab) {
  if (STRIKE_INVULN.has(f.st)) return true;
  if (f.st === 'dash_b' && f.sf < 6) return true;
  if (f.st === 'super' && f.sf < f.mv.inv && !grab) return true;
  if (grab && (f.st === 'hit' || f.st === 'block')) return true; // nobody is grabbed out of hitstun or blockstun
  return false;
}
const active = (f) => f.mv && !f.done && f.sf >= f.mv.su && f.sf < f.mv.su + f.mv.ac;
const canParry = (m, f) => (ACTIONABLE.has(f.st) || f.st === 'parry') && m.tick - f.parryAt <= PARRY_WINDOW;

function damage(m, victim, amount, attacker) {
  const scaled = Math.max(Math.floor(amount / 2), Math.floor(amount * (100 - 15 * victim.combo) / 100));
  const dmg = Math.min(victim.bal, scaled);
  victim.bal -= dmg; attacker.dealt += dmg;
  attacker.met = Math.min(MET, attacker.met + Math.floor(dmg * 4 / 10));
  victim.met = Math.min(MET, victim.met + Math.floor(dmg * 2 / 10));
  return dmg;
}
/** Moves the victim back; at a wall the attacker is moved back instead. */
function pushBack(victim, attacker, amount) {
  const dir = Math.sign(victim.x - attacker.x) || victim.face * -1;
  const room = dir > 0 ? WALL_R - victim.x : victim.x - WALL_L, moved = Math.min(amount, Math.max(0, room));
  victim.x += dir * moved; attacker.x -= dir * (amount - moved);
}

/** One impact of attacker a (index ia) on victim v with move mv (taken before either impact of a trade was applied). */
function impact(m, ia, mv = m.f[ia].mv) {
  const a = m.f[ia], v = m.f[1 - ia];
  a.done = true;
  if (mv.grab) {
    if (mv.sup) {
      enter(a, 'super_throw', 50); enter(v, 'thrown', 50, { st: 'down', dur: DOWN, next: { st: 'getup', dur: GETUP } }); v.kind = 'super';
      const dmg = damage(m, v, SUPER_DMG, a); v.combo = 0;
      m.freeze = 10; ev(m, 'super-throw', ia, { d: dmg, x: v.x });
      return;
    }
    startClinch(m, ia);
    ev(m, 'grab', ia, { x: (a.x + v.x) >> 1 });
    return;
  }
  if (canParry(m, v)) {
    enter(a, 'staggered', 26); enter(v, 'parry', 8);
    v.met = Math.min(MET, v.met + 150); v.sta = Math.min(STA, v.sta + 80);
    m.freeze = 8; ev(m, 'parry', 1 - ia, { x: (a.x + v.x) >> 1 });
    return;
  }
  if (v.st === 'guard' || v.st === 'block') {
    v.sta -= mv.chip; a.met = Math.min(MET, a.met + 20);
    pushBack(v, a, mv.bpush);
    if (v.sta <= 0) {
      v.sta = 300; enter(v, 'dizzy', 50); m.freeze = 8; ev(m, 'guard-break', ia, { x: v.x });
    } else { enter(v, 'block', mv.bs, { st: 'guard' }); m.freeze = 2; ev(m, 'block', ia, { x: (a.x + v.x) >> 1 }); }
    return;
  }
  const wasHit = v.st === 'hit' || v.st === 'trip';
  const counter = !wasHit && v.mv && !v.done && v.sf < v.mv.su; // caught starting a move: a counter hit, 20% more
  v.combo = wasHit ? v.combo + 1 : 0;
  const dmg = damage(m, v, counter ? Math.floor(mv.dmg * 6 / 5) : mv.dmg, a);
  pushBack(v, a, mv.push);
  if (mv.kd) enter(v, 'trip', TRIP, { st: 'down', dur: DOWN, next: { st: 'getup', dur: GETUP } });
  else enter(v, 'hit', mv.hs);
  if (mv.cancel) a.cancel = m.tick + mv.ac + 3;
  m.freeze = mv.kd ? 6 : 4;
  ev(m, mv.kd ? 'sweep' : 'hit', ia, { d: dmg, x: v.x, c: v.combo + 1, mv: a.st, ...(counter ? { ch: 1 } : {}) });
}

// ---------------------------------------------------------------- the clinch
function startClinch(m, lead) {
  const a = m.f[lead], v = m.f[1 - lead];
  let mid = (a.x + v.x) >> 1;
  mid = Math.max(WALL_L + CLINCH_GAP / 2, Math.min(WALL_R - CLINCH_GAP / 2, mid));
  const side = Math.sign(a.x - v.x) || -a.face;
  a.x = mid + side * (CLINCH_GAP / 2); v.x = mid - side * (CLINCH_GAP / 2);
  a.face = -side; v.face = side;
  enter(a, 'clinch_lead'); enter(v, 'clinch_def'); a.buf = []; v.buf = [];
  m.clinch = { lead, t: 0 };
}
function endClinch(m) { m.clinch = null; }
function separate(m, ia, ib, gap) {
  const a = m.f[ia], b = m.f[ib], dir = Math.sign(b.x - a.x) || 1;
  a.x -= dir * (gap >> 1); b.x += dir * (gap >> 1);
}

function clinchStep(m) {
  const c = m.clinch, L = m.f[c.lead], D = m.f[1 - c.lead], li = c.lead, di = 1 - c.lead;
  c.t++;
  // the one holding the grip
  if (L.st === 'clinch_lead') {
    const b = press(m, L, B.SP | B.H | B.L);
    if (b === B.SP && L.met >= MET) { L.met = 0; enter(L, 'c_super', CLINCH.superSu); ev(m, 'super', li); }
    else if (b === B.H) {
      const d = dirOf(L), kind = d === 'f' ? 'hook' : d === 'b' ? 'hip' : 'lift', th = THROWS[kind];
      if (L.sta >= th.cost) { L.sta -= th.cost; enter(L, 't_' + kind, th.su); L.kind = kind; } else ev(m, 'tired', li);
    } else if (b === B.L) {
      if (L.sta >= CLINCH.joltCost) { L.sta -= CLINCH.joltCost; enter(L, 'jolt', CLINCH.joltSu + CLINCH.joltRc); } else ev(m, 'tired', li);
    } else { L.sta = Math.max(0, L.sta - CLINCH.leadDrain); }
  }
  // the one being held
  if (D.st === 'clinch_def') {
    const b = press(m, D, B.G | B.L);
    if (b === B.G) {
      const d = dirOf(D), kind = d === 'f' ? 'hook' : d === 'b' ? 'hip' : 'lift';
      if (D.sta >= CLINCH.counterCost) { D.sta -= CLINCH.counterCost; enter(D, 'counter', CLINCH.counterRc, { st: 'clinch_def' }); D.kind = kind; } else ev(m, 'tired', di);
    } else if (b === B.L) {
      if (D.sta >= CLINCH.escapeCost) { D.sta -= CLINCH.escapeCost; enter(D, 'escape', CLINCH.escapeSu); } else ev(m, 'tired', di);
    } else D.sta = Math.min(STA, D.sta + CLINCH.defRegen);
  }
  L.sf++; D.sf++;
  // the lead's move lands
  if (L.st.startsWith('t_') && L.sf >= THROWS[L.kind].su) return throwLands(m, li);
  if (L.st === 'c_super' && L.sf >= CLINCH.superSu) {
    enter(L, 'super_throw', 50); enter(D, 'thrown', 50, { st: 'down', dur: DOWN, next: { st: 'getup', dur: GETUP } }); D.kind = 'super';
    const dmg = damage(m, D, CLINCH.superDmg, L); m.freeze = 10; endClinch(m);
    ev(m, 'super-throw', li, { d: dmg, x: D.x }); return;
  }
  if (L.st === 'jolt' && L.sf === CLINCH.joltSu) {
    const guessing = (D.st === 'counter' && D.sf < CLINCH.counterOn) || D.st === 'escape';
    const dmg = damage(m, D, guessing ? CLINCH.joltCatch : CLINCH.joltDmg, L);
    if (guessing) { enter(D, 'offbal', 24, { st: 'clinch_def' }); ev(m, 'jolt-catch', li, { d: dmg, x: D.x }); m.freeze = 4; }
    else ev(m, 'jolt', li, { d: dmg, x: D.x });
  }
  if (L.st === 'jolt' && L.sf >= L.dur) enter(L, 'clinch_lead');
  if (D.st === 'escape' && D.sf >= CLINCH.escapeSu) {
    endClinch(m); separate(m, li, di, 60);
    enter(D, 'idle'); enter(L, 'pushed', 8); ev(m, 'escape', di, { x: D.x }); return;
  }
  if (D.dur && D.sf >= D.dur) { const nx = D.next; enter(D, nx ? nx.st : 'clinch_def'); }
  // a lead out of breath loses the grip; a clinch that goes on too long is broken up
  if (L.st === 'clinch_lead' && L.sta <= 0) { c.lead = di; c.t = 0; enter(L, 'clinch_def'); enter(D, 'clinch_lead'); ev(m, 'grip-swap', di); return; }
  if (c.t >= CLINCH_MAX) { endClinch(m); separate(m, li, di, 80); enter(L, 'idle'); enter(D, 'idle'); ev(m, 'break', li); }
}
function throwLands(m, li) {
  const L = m.f[li], D = m.f[1 - li], kind = L.kind, th = THROWS[kind];
  const countering = D.st === 'counter' && D.sf < CLINCH.counterOn;
  if (countering && D.kind === kind) { // read it: the throw is turned over and the grip changes hands
    const dmg = damage(m, L, CLINCH.reversalDmg, D);
    D.met = Math.min(MET, D.met + 150);
    m.clinch = { lead: 1 - li, t: 0 }; enter(D, 'clinch_lead'); enter(L, 'clinch_def');
    m.freeze = 8; ev(m, 'reversal', 1 - li, { d: dmg, x: L.x, kind });
    return;
  }
  const dmg = damage(m, D, Math.floor(th.dmg * (countering ? CLINCH.guessBonus : 1)), L);
  L.met = Math.min(MET, L.met + 100);
  endClinch(m);
  enter(L, 'throwing', THROWING); L.kind = kind;
  enter(D, 'thrown', THROWN, { st: 'down', dur: DOWN, next: { st: 'getup', dur: GETUP } }); D.kind = kind;
  m.freeze = 6; ev(m, 'throw', li, { d: dmg, x: D.x, kind, guess: countering });
}

// ---------------------------------------------------------------- one tick
function regen(f) {
  const rate = f.st === 'idle' || f.st === 'walk_f' || f.st === 'walk_b' ? 5 : f.st === 'guard' ? 2 : f.st === 'down' || f.st === 'getup' ? 6 : 0;
  f.sta = Math.min(STA, f.sta + rate);
}
function bodies(m) {
  const [a, b] = m.f;
  for (const f of m.f) f.x = Math.max(WALL_L, Math.min(WALL_R, f.x));
  if (m.clinch) return;
  const gap = Math.abs(a.x - b.x);
  if (gap < BODY && !STRIKE_INVULN.has(a.st) && !STRIKE_INVULN.has(b.st)) {
    const dir = a.x <= b.x ? 1 : -1, need = BODY - gap;
    let ma = need >> 1, mb = need - ma;
    if (a.x - dir * ma < WALL_L || a.x - dir * ma > WALL_R) { mb += ma; ma = 0; }
    if (b.x + dir * mb < WALL_L || b.x + dir * mb > WALL_R) { ma += mb; mb = 0; }
    a.x -= dir * ma; b.x += dir * mb;
    for (const f of m.f) f.x = Math.max(WALL_L, Math.min(WALL_R, f.x));
  }
  for (const [f, o] of [[a, b], [b, a]]) if (ACTIONABLE.has(f.st) || f.st === 'dash_f' || f.st === 'getup') f.face = o.x >= f.x ? 1 : -1;
}

function resetRound(m) {
  for (let i = 0; i < 2; i++) {
    const f = m.f[i], keep = { met: f.met, dealt: f.dealt, held: f.held, prev: f.prev };
    Object.assign(f, fighter(i), keep);
  }
  m.clinch = null; m.timer = ROUND_TICKS; m.freeze = 0;
}

function endRound(m, winner, how) {
  if (winner >= 0) m.wins[winner]++;
  m.rounds.push({ w: winner, how, bal: [m.f[0].bal, m.f[1].bal] });
  m.phase = 'ko'; m.pt = 0; m.freeze = how === 'ko' ? 12 : 0;
  ev(m, how, winner);
  for (let i = 0; i < 2; i++) {
    const f = m.f[i], end = i === winner ? 'win' : winner >= 0 ? (f.bal <= 0 ? 'fall' : 'lose') : 'idle';
    // a throw or a trip in progress plays out first
    if (f.dur && (f.st === 'thrown' || f.st === 'trip' || f.st === 'throwing' || f.st === 'super_throw')) f.next = { st: end };
    else enter(f, end);
  }
  m.clinch = null;
}

function finishMatch(m) {
  let w = m.wins[0] > m.wins[1] ? 0 : m.wins[1] > m.wins[0] ? 1 : -1;
  if (w < 0 && m.f[0].dealt !== m.f[1].dealt) w = m.f[0].dealt > m.f[1].dealt ? 0 : 1;
  m.phase = 'over'; m.result = { winner: w, wins: m.wins.slice(), rounds: m.rounds.slice() };
  ev(m, 'match', w);
}

/** Advances the bout one tick. Inputs for this tick are set with input() first. Returns the events of this tick. */
function step(m) {
  m.events = [];
  m.tick++;
  for (const f of m.f) takeInputs(m, f);
  if (m.phase === 'over') return m.events;
  if (m.phase === 'intro') {
    if (++m.pt === 1) ev(m, 'round', -1, { n: m.round });
    if (m.pt >= INTRO_TICKS) { m.phase = 'fight'; m.pt = 0; ev(m, 'fight', -1); }
    return m.events;
  }
  if (m.phase === 'ko') {
    m.pt++;
    if (m.freeze > 0) m.freeze--; else for (const f of m.f) if (f.dur) advance(f); else f.sf++;
    if (m.pt >= KO_TICKS) {
      if (m.wins[0] >= ROUNDS_TO_WIN || m.wins[1] >= ROUNDS_TO_WIN || m.round >= ROUNDS_MAX) finishMatch(m);
      else { m.round++; resetRound(m); m.phase = 'intro'; m.pt = 0; }
    }
    return m.events;
  }
  // phase 'fight'
  if (m.freeze > 0) { m.freeze--; return m.events; }
  m.timer--;
  if (m.clinch) clinchStep(m);
  else {
    for (let i = 0; i < 2; i++) {
      const f = m.f[i];
      if (f.st === 'shove' && f.cancel >= m.tick && f.done && decide(m, f, true)) continue;
      if (ACTIONABLE.has(f.st) || (f.st === 'parry' && f.sf >= 4)) { decide(m, f); continue; }
      advance(f);
    }
    // impacts: both are found before either is applied, so two strikes on the same tick trade
    const hits = [0, 1].filter((i) => {
      const a = m.f[i], v = m.f[1 - i];
      return active(a) && Math.abs(a.x - v.x) <= a.mv.reach && !invulnerable(v, a.mv.grab);
    });
    if (hits.length === 2 && m.f[0].mv.grab && m.f[1].mv.grab) { // two grabs at once: they break apart
      for (const f of m.f) { f.done = true; enter(f, 'pushed', 10); }
      separate(m, 0, 1, 60); ev(m, 'clash', -1, { x: (m.f[0].x + m.f[1].x) >> 1 });
    } else if (hits.length === 2 && (m.f[0].mv.grab || m.f[1].mv.grab)) {
      impact(m, m.f[0].mv.grab ? 1 : 0); // a strike beats a grab
    } else {
      const mvs = hits.map((i) => m.f[i].mv);
      hits.forEach((i, n) => impact(m, i, mvs[n])); // two strikes on the same tick trade
    }
    // a strike or grab that found nothing ran its course in advance()
  }
  for (const f of m.f) { if (f.st !== 'hit' && f.st !== 'trip') f.combo = 0; regen(f); }
  bodies(m);
  const [a, b] = m.f;
  if (a.bal <= 0 || b.bal <= 0) {
    endRound(m, a.bal <= 0 && b.bal <= 0 ? -1 : a.bal <= 0 ? 1 : 0, 'ko');
  } else if (m.timer <= 0) {
    endRound(m, a.bal === b.bal ? -1 : a.bal > b.bal ? 0 : 1, 'time');
  }
  return m.events;
}

/** What the clients draw: compact, no buffers or hidden state. */
function snapshot(m) {
  const F = (f) => [f.x, f.face, f.st, f.sf, f.bal, f.sta, f.met, f.combo, f.kind || ''];
  return { k: m.tick, ph: m.phase, rd: m.round, tm: m.timer, w: m.wins, f: [F(m.f[0]), F(m.f[1])], c: m.clinch ? m.clinch.lead : -1, ev: m.events };
}

module.exports = { TICK_HZ, STAGE_W, WALL_L, WALL_R, START, BAL, STA, MET, ROUND_TICKS, INTRO_TICKS, KO_TICKS, B, INPUT_MASK,
  MOVES, THROWS, CLINCH, createMatch, input, step, snapshot, dirOf };
