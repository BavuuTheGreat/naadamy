/* Бөхийн Барилдаан — 2D fighting game with Mongolian wrestlers.
 * The simulation (top half) is deterministic: no Math.random, no time, fixed 60 Hz steps.
 * Online play is delay-based lockstep over PeerJS: only inputs are sent. */
'use strict';

// ===================================================================== SIM
const W = 960, H = 540, GROUND = 470, GRAV = 0.85, STAGE_L = 60, STAGE_R = 900;
const BTN = { U: 1, D: 2, L: 4, R: 8, LP: 16, HP: 32, KK: 64, GR: 128 };
const ATK = BTN.LP | BTN.HP | BTN.KK | BTN.GR;

const CHARS = [
  { name: 'НАЧИН', title: 'Хурдан шонхор', spd: 1.16, pow: 0.9, hp: 95, size: 0.95, sup: 'ШОНХОРЫН ШИДЭЛТ',
    col: { z: '#1f55d0', z2: '#163b91', trim: '#f2c14e', skin: '#c98a5a', skin2: '#a26a40', boot: '#5a2d14', boot2: '#e0b040', hat: '#1f55d0' } },
  { name: 'ЗААН', title: 'Хүчит заан', spd: 0.86, pow: 1.18, hp: 112, size: 1.1, sup: 'ЗААНЫ ДАРАЛТ',
    col: { z: '#c8202e', z2: '#8c1520', trim: '#f2c14e', skin: '#b97a4c', skin2: '#935a33', boot: '#3a2012', boot2: '#c98f2e', hat: '#c8202e' } },
  { name: 'АРСЛАН', title: 'Тэнгэрийн арслан', spd: 1, pow: 1, hp: 100, size: 1.02, sup: 'АРСЛАНГИЙН БАРИЛТ',
    col: { z: '#6c2ba0', z2: '#4a1c70', trim: '#efe6cc', skin: '#d39a6a', skin2: '#a9744a', boot: '#262626', boot2: '#c0392b', hat: '#6c2ba0' } },
];
const ALT = { z: '#23894b', z2: '#185f33', hat: '#23894b' };

// box: [forward offset, bottom height, width, height]
const MV = {
  lp:   { su: 4, ac: 3, re: 8,  dmg: 4,  hs: 13, bs: 8,  push: 5,  box: [18, 104, 60, 22], cancel: ['lp', 'hp', 'kk', 'sp'], an: 'jab' },
  hp:   { su: 8, ac: 4, re: 15, dmg: 8,  hs: 19, bs: 12, push: 8,  box: [18, 96, 72, 28],  cancel: ['sp'], an: 'cross' },
  kk:   { su: 7, ac: 4, re: 14, dmg: 7,  hs: 18, bs: 11, push: 9,  box: [18, 58, 84, 28],  cancel: ['hp', 'sp'], an: 'kick' },
  clp:  { su: 4, ac: 3, re: 8,  dmg: 3,  hs: 12, bs: 7,  push: 4,  box: [18, 44, 60, 22],  low: true, crouch: true, cancel: ['clp', 'chp', 'ckk', 'sp'], an: 'cjab' },
  chp:  { su: 6, ac: 5, re: 20, dmg: 9,  hs: 22, bs: 12, push: 2,  box: [4, 60, 52, 115],  launch: [2.5, 15], crouch: true, cancel: ['sp'], an: 'upper' },
  ckk:  { su: 8, ac: 4, re: 18, dmg: 8,  hs: 20, bs: 12, push: 6,  box: [18, 0, 98, 24],   low: true, trip: true, crouch: true, cancel: ['sp'], an: 'sweep' },
  jlp:  { su: 4, ac: 8, re: 0,  dmg: 5,  hs: 14, bs: 8,  push: 3,  box: [14, 58, 56, 30],  high: true, air: true, an: 'jlp' },
  jhp:  { su: 6, ac: 6, re: 0,  dmg: 9,  hs: 18, bs: 10, push: 5,  box: [10, 34, 64, 46],  high: true, air: true, an: 'jhp' },
  jkk:  { su: 5, ac: 8, re: 0,  dmg: 8,  hs: 16, bs: 10, push: 5,  box: [14, 10, 72, 34],  high: true, air: true, an: 'jkk' },
  dive: { su: 9, ac: 26, re: 14, dmg: 12, hs: 24, bs: 14, push: 8, box: [0, 20, 76, 92],   knock: true, sp: true, an: 'dive', leap: [8, 11] },
  rush: { su: 8, ac: 14, re: 18, dmg: 10, hs: 22, bs: 14, push: 10, box: [4, 30, 62, 100], knock: true, sp: true, an: 'rush', lunge: 11 },
  sup:  { su: 6, ac: 16, re: 26, dmg: 6,  hs: 0,  bs: 16, push: 6, box: [4, 10, 72, 140],  sp: true, an: 'rush', lunge: 15, superGrab: true },
};
const SPN = { dive: 'БҮРГЭДИЙН ДАЙРАЛТ', rush: 'АРСЛАНГИЙН ДАВШИЛТ' };
const COMBOS = [
  ['clp>clp>chp>dive', 'ХАНГАРЬДЫН ДЭВЭЛТ'],
  ['lp>lp>hp>dive', 'ТЭНГЭРИЙН БҮРГЭД'],
  ['lp>lp>hp>rush', 'АРСЛАНГИЙН ХҮЧ'],
  ['kk>hp>dive', 'САЛХИН ХҮЛЭГ'],
  ['lp>kk>rush', 'ХӨХ ЧОНО'],
  ['lp>lp>hp', 'ТӨМӨР НУДРАГА'],
  ['chp>dive', 'ДЭВЭЛТ'],
  ['clp>ckk', 'ГАДНА ТАХ'],
];

function newFighter(idx, ci, alt) {
  return {
    idx, ci, alt, x: idx ? 640 : 320, y: 0, vx: 0, vy: 0, face: idx ? -1 : 1,
    hp: CHARS[ci].hp, meter: 0, wins: 0,
    st: 'intro', t: 0, mv: null, mt: 0, hit: false, chain: 0, stun: 0, cb: false, inv: 0, thr: 0,
    hist: [], dir: 5, inp: 0, prev: 0, buf: 0, bufT: 0, comboN: 0, jug: 0, taken: [],
  };
}

function newMatch(c1, c2) {
  return {
    p: [newFighter(0, c1, false), newFighter(1, c2, c1 === c2)],
    phase: 'intro', pt: 0, round: 1, timer: 99 * 60, hitstop: 0, freeze: 0, frame: 0,
    ev: [], ready: [false, false], winner: -1, champ: -1,
  };
}

function resetRound(s) {
  s.round++;
  for (const f of s.p) {
    Object.assign(f, {
      x: f.idx ? 640 : 320, y: 0, vx: 0, vy: 0, face: f.idx ? -1 : 1, hp: CHARS[f.ci].hp,
      st: 'intro', t: 0, mv: null, mt: 0, hit: false, chain: 0, stun: 0, inv: 0,
      buf: 0, bufT: 0, hist: [], comboN: 0, jug: 0, taken: [],
    });
  }
  s.phase = 'intro'; s.pt = 0; s.timer = 99 * 60; s.winner = -1;
}

function setIdle(f) {
  f.st = 'idle'; f.mv = null; f.comboN = 0; f.jug = 0; f.taken = []; f.chain = 0;
}

function readInput(f, inp) {
  const fw = f.face === 1 ? BTN.R : BTN.L, bk = f.face === 1 ? BTN.L : BTN.R;
  const u = inp & BTN.U, d = inp & BTN.D, F = inp & fw, B = inp & bk;
  f.dir = d ? (F ? 3 : B ? 1 : 2) : u ? (F ? 9 : B ? 7 : 8) : (F ? 6 : B ? 4 : 5);
  f.hist.push(f.dir);
  if (f.hist.length > 20) f.hist.shift();
  f.inp = inp;
  const e = inp & ~f.prev & ATK;
  f.prev = inp;
  if (e) { f.buf |= e; f.bufT = 7; }
}

function motion(h, seq, win) {
  let k = seq.length - 1;
  for (let i = h.length - 1; i >= 0 && i >= h.length - win; i--) {
    if (seq[k].includes(h[i]) && --k < 0) return true;
  }
  return false;
}
const M_DOWN = [1, 2, 3], M_NOTDOWN = [4, 5, 6, 7, 8, 9];

function chooseMove(f, air) {
  const b = f.buf, h = f.hist;
  if (!b) return null;
  if (air) {
    if (b & BTN.HP) return 'jhp';
    if (b & BTN.KK) return 'jkk';
    if (b & BTN.LP) return 'jlp';
    return null;
  }
  if (b & BTN.HP) {
    if (f.meter >= 100 && motion(h, [M_DOWN, M_NOTDOWN, M_DOWN], 18)) return 'sup';
    if (motion(h, [[1, 2], [3, 6]], 14)) return 'dive';
  }
  if ((b & BTN.LP) && motion(h, [[4, 1, 7], [6, 3, 9]], 14)) return 'rush';
  if (b & BTN.GR) return 'grab';
  const crouch = f.dir <= 3;
  if (b & BTN.HP) return crouch ? 'chp' : 'hp';
  if (b & BTN.KK) return crouch ? 'ckk' : 'kk';
  if (b & BTN.LP) return crouch ? 'clp' : 'lp';
  return null;
}

function canCancel(f, nx) {
  const m = MV[f.mv];
  if (!m.cancel || nx === 'grab') return false;
  if (MV[nx].sp) return m.cancel.includes('sp');
  if (!m.cancel.includes(nx)) return false;
  return !(nx === f.mv && f.chain >= 2);
}

function startMove(s, f, o, id) {
  f.buf = 0; f.bufT = 0;
  if (id === 'grab') { tryGrab(s, f, o); return; }
  f.chain = f.st === 'atk' ? f.chain + 1 : 0;
  f.st = 'atk'; f.mv = id; f.mt = 0; f.hit = false; f.t = 0;
  const m = MV[id];
  if (id === 'sup') { f.meter = 0; s.freeze = 40; s.ev.push({ k: 'super', p: f.idx }); }
  else if (SPN[id]) s.ev.push({ k: 'special', p: f.idx, name: SPN[id] });
  s.ev.push({ k: 'whoosh', big: m.dmg >= 8 });
}

function tryGrab(s, f, o) {
  const ok = Math.abs(o.x - f.x) < 105 && o.y <= 0 && o.inv <= 0 &&
    ['idle', 'walk', 'crouch', 'block', 'atk'].includes(o.st) && o.mv !== 'sup';
  if (ok) setThrow(s, f, o, 0);
  else { f.st = 'grabmiss'; f.t = 0; f.vx = 0; s.ev.push({ k: 'whoosh', big: false }); }
}

function setThrow(s, a, d, type) {
  a.st = 'throw'; a.thr = type; a.t = 0; a.mv = null; a.vx = 0;
  d.st = 'thrown'; d.t = 0; d.mv = null; d.vx = 0; d.vy = 0; d.face = -a.face;
  s.ev.push({ k: 'grab' });
}

function clampX(f) { f.x = Math.max(STAGE_L, Math.min(STAGE_R, f.x)); }
function atWall(f) { return f.x <= STAGE_L + 1 || f.x >= STAGE_R - 1; }

function dealRaw(s, a, d, dmg) {
  d.hp = Math.max(0, d.hp - dmg);
  a.meter = Math.min(100, a.meter + dmg * 0.6);
  d.meter = Math.min(100, d.meter + dmg * 0.4);
  d.comboN++;
  if (d.comboN >= 2) s.ev.push({ k: 'combo', p: a.idx, n: d.comboN, name: '' });
}

function throwStep(s, f, o) {
  const T = f.t, pow = CHARS[f.ci].pow;
  const held = o.st === 'thrown';
  if (f.thr === 0) {
    if (held && T <= 8 && (o.buf & BTN.GR)) { // throw tech
      f.st = 'hit'; f.stun = 14; f.t = 0; o.st = 'hit'; o.stun = 14; o.t = 0; o.y = 0; o.buf = 0;
      f.vx = -f.face * 7; o.vx = f.face * 7;
      s.ev.push({ k: 'tech', x: (f.x + o.x) / 2, y: 110 });
      return;
    }
    if (held) {
      o.t = T;
      if (T < 16) { o.x = f.x + f.face * 52; o.y = 0; }
      else if (T < 30) { const p = (T - 16) / 14; o.x = f.x + f.face * (52 - p * 30); o.y = p * 95; }
      else if (T < 36) { const p = (T - 30) / 6; o.x = f.x + f.face * (22 + p * 60); o.y = 95 * (1 - p); }
      else {
        o.x = f.x + f.face * 82; o.y = 0;
        dealRaw(s, f, o, 14 * pow);
        o.st = 'down'; o.t = 0;
        s.hitstop = 10;
        s.ev.push({ k: 'slam', x: o.x });
      }
      clampX(o);
    }
    if (T >= 50) setIdle(f);
  } else {
    if (held) {
      o.t = T;
      const ph = T % 28;
      o.x = f.x + f.face * 58;
      o.y = ph === 0 ? 0 : 115 * 4 * (ph / 28) * (1 - ph / 28);
      clampX(o);
      if (T === 28 || T === 56 || T === 84) {
        dealRaw(s, f, o, (T === 84 ? 14 : 9) * pow);
        s.hitstop = T === 84 ? 14 : 8;
        s.ev.push({ k: 'slam', x: o.x });
        if (T === 84) { o.st = 'down'; o.t = 0; o.y = 0; }
      }
    }
    if (T >= 100) setIdle(f);
  }
}

function onLand(s, f) {
  if (f.st === 'jump') { f.st = 'idle'; f.vx = 0; s.ev.push({ k: 'dust', x: f.x }); }
  else if (f.st === 'atk') {
    const m = MV[f.mv];
    if (m.air) { setIdle(f); f.vx = 0; }
    else if (m.leap) { if (f.mt < m.su + m.ac) f.mt = m.su + m.ac; f.vx = 0; }
    s.ev.push({ k: 'dust', x: f.x });
  } else if (f.st === 'air') {
    f.st = 'down'; f.t = 0; f.vx *= 0.3;
    s.ev.push({ k: 'thud', x: f.x });
  } else if (f.st === 'idle' || f.st === 'walk') { f.vx = 0; }
}

function stepFighter(s, f, o, act) {
  const c = CHARS[f.ci];
  f.t++;
  if (f.inv > 0) f.inv--;
  const dir = act ? f.dir : 5;
  const buf = act ? f.buf : 0;
  switch (f.st) {
    case 'idle': case 'walk': case 'crouch': {
      if (f.y > 0) { f.st = 'jump'; break; }
      if (o.x !== f.x && o.st !== 'thrown') f.face = o.x > f.x ? 1 : -1;
      const id = buf ? chooseMove(f, false) : null;
      if (id) { startMove(s, f, o, id); break; }
      if (dir >= 7) {
        f.st = 'jump'; f.t = 0; f.vy = 15.5;
        f.vx = (dir === 9 ? 1 : dir === 7 ? -1 : 0) * f.face * 4.4 * c.spd;
      } else if (dir <= 3) { f.st = 'crouch'; f.vx = 0; }
      else if (dir === 6) { f.st = 'walk'; f.vx = f.face * 3.7 * c.spd; }
      else if (dir === 4) { f.st = 'walk'; f.vx = -f.face * 2.9 * c.spd; }
      else { f.st = 'idle'; f.vx *= 0.6; }
      break;
    }
    case 'jump':
      if (buf) { const id = chooseMove(f, true); if (id) startMove(s, f, o, id); }
      break;
    case 'atk': {
      const m = MV[f.mv];
      f.mt++;
      const t = f.mt;
      if (m.lunge) { if (t > m.su && t <= m.su + m.ac) f.vx = f.face * m.lunge * c.spd; else if (f.y <= 0) f.vx *= 0.7; }
      else if (m.leap) { if (t === m.su + 1) { f.vy = m.leap[1]; f.vx = f.face * m.leap[0] * c.spd; } }
      else if (f.y <= 0) f.vx *= 0.7;
      if (f.hit && buf && t > m.su && f.y <= 0 && !m.air) {
        const id = chooseMove(f, false);
        if (id && canCancel(f, id)) { startMove(s, f, o, id); break; }
      }
      if (!m.air && t >= m.su + m.ac + m.re) setIdle(f);
      break;
    }
    case 'hit': case 'block':
      f.vx *= 0.82;
      if (--f.stun <= 0) setIdle(f);
      break;
    case 'down':
      f.vx *= 0.8; f.inv = 2;
      if (f.hp <= 0) f.st = 'ko';
      else if (f.t >= 34) { f.st = 'getup'; f.t = 0; }
      break;
    case 'getup':
      f.inv = 2;
      if (f.t >= 16) { setIdle(f); f.inv = 6; }
      break;
    case 'grabmiss':
      f.vx *= 0.7;
      if (f.t >= 22) setIdle(f);
      break;
    case 'throw':
      throwStep(s, f, o);
      break;
    default:
      f.vx *= 0.8;
  }
  if (f.bufT > 0 && --f.bufT === 0) f.buf = 0;

  if (f.st !== 'thrown') {
    const wasAir = f.y > 0;
    f.x += f.vx; f.y += f.vy;
    if (f.y > 0) f.vy -= GRAV;
    if (f.y <= 0) { f.y = 0; if (f.vy <= 0) { f.vy = 0; if (wasAir) onLand(s, f); } }
    clampX(f);
  }
}

function rect(f, b) {
  const sz = CHARS[f.ci].size, fx = b[0] * sz, w = b[2] * sz;
  const x0 = f.face === 1 ? f.x + fx : f.x - fx - w;
  return { x0, x1: x0 + w, y0: f.y + b[1] * sz, y1: f.y + (b[1] + b[3]) * sz };
}
function hurt(f) {
  if (f.st === 'crouch' || (f.st === 'atk' && MV[f.mv].crouch) || (f.st === 'block' && f.cb)) return rect(f, [-30, 0, 60, 95]);
  if (f.y > 0 || f.st === 'air') return rect(f, [-28, 10, 56, 115]);
  return rect(f, [-28, 0, 56, 148]);
}
function hittable(f) {
  return f.inv <= 0 && !['down', 'getup', 'ko', 'thrown', 'intro', 'win'].includes(f.st);
}
function activeMove(f) {
  if (f.st !== 'atk' || f.hit) return null;
  const m = MV[f.mv];
  return f.mt > m.su && f.mt <= m.su + m.ac ? m : null;
}

function resolveHits(s) {
  const [a, b] = s.p, hits = [];
  for (const [x, y] of [[a, b], [b, a]]) {
    const m = activeMove(x);
    if (!m || !hittable(y)) continue;
    const hb = rect(x, m.box), hu = hurt(y);
    if (hb.x0 < hu.x1 && hb.x1 > hu.x0 && hb.y0 < hu.y1 && hb.y1 > hu.y0) hits.push([x, y, m, hb, hu]);
  }
  for (const h of hits) applyHit(s, ...h);
}

function emitCombo(s, a, d) {
  if (d.comboN < 2) return;
  const str = d.taken.join('>');
  let name = '';
  for (const [k, n] of COMBOS) if (str === k || str.endsWith('>' + k)) { name = n; break; }
  s.ev.push({ k: 'combo', p: a.idx, n: d.comboN, name });
}

function applyHit(s, a, d, m, hb, hu) {
  a.hit = true;
  const sx = (Math.max(hb.x0, hu.x0) + Math.min(hb.x1, hu.x1)) / 2;
  const sy = (Math.max(hb.y0, hu.y0) + Math.min(hb.y1, hu.y1)) / 2;
  const pow = CHARS[a.ci].pow;
  const canBlock = ['idle', 'walk', 'crouch', 'block'].includes(d.st) && d.y <= 0;
  const dl = d.dir;
  let blocked = false;
  if (canBlock) blocked = m.low ? dl === 1 : m.high ? dl === 4 : (dl === 1 || dl === 4);

  if (blocked) {
    d.st = 'block'; d.cb = dl === 1; d.stun = m.bs; d.t = 0; d.vx = a.face * m.push * 0.75;
    if (m.sp) { const chip = m.dmg * 0.15 * pow; if (d.hp > chip) d.hp -= chip; }
    a.meter = Math.min(100, a.meter + 3); d.meter = Math.min(100, d.meter + 2);
    if (atWall(d)) a.vx = -a.face * m.push * 0.6;
    if (m.leap) { a.vx = -a.face * 4; a.vy = Math.max(a.vy, 6); }
    s.hitstop = Math.max(s.hitstop, 5);
    s.ev.push({ k: 'block', x: sx, y: sy });
    return;
  }

  const scale = Math.max(0.35, 1 - 0.1 * d.comboN);
  d.hp = Math.max(0, d.hp - m.dmg * pow * scale);
  a.meter = Math.min(100, a.meter + m.dmg * 1.1);
  d.meter = Math.min(100, d.meter + m.dmg * 0.6);
  d.comboN++; d.taken.push(a.mv);
  const big = m.dmg >= 8;
  s.ev.push({ k: 'hit', x: sx, y: sy, big, p: a.idx });

  if (m.superGrab) { setThrow(s, a, d, 1); s.hitstop = 10; emitCombo(s, a, d); return; }

  const air = d.y > 0 || d.st === 'air';
  if (m.launch || m.knock || m.trip || air || d.hp <= 0) {
    let vy = m.launch ? m.launch[1] : m.trip ? 5 : m.knock ? 9 : 7;
    vy *= Math.max(0.4, 1 - 0.14 * d.jug);
    d.jug++;
    let vx = m.launch ? m.launch[0] : m.knock ? 6 : m.trip ? 1.5 : 3.2;
    if (d.hp <= 0) { vy = 12; vx = 5; }
    d.st = 'air'; d.t = 0; d.mv = null; d.vy = vy; d.vx = a.face * vx;
  } else {
    d.st = 'hit'; d.t = 0; d.mv = null; d.stun = m.hs; d.vx = a.face * m.push * 0.8;
  }
  if (atWall(d)) a.vx = -a.face * m.push * 0.6;
  s.hitstop = Math.max(s.hitstop, d.hp <= 0 ? 18 : big ? 9 : 6);
  emitCombo(s, a, d);
}

function separate(s) {
  const [a, b] = s.p;
  if (a.st === 'thrown' || b.st === 'thrown') return;
  if (Math.abs(a.y - b.y) > 100) return;
  const md = 68 * (CHARS[a.ci].size + CHARS[b.ci].size) / 2;
  const dx = b.x - a.x, ad = Math.abs(dx);
  if (ad >= md) return;
  const sg = dx > 0 ? 1 : dx < 0 ? -1 : a.face;
  const push = (md - ad) / 2;
  a.x -= sg * push; b.x += sg * push;
  clampX(a); clampX(b);
  if (Math.abs(b.x - a.x) < md - 0.01) {
    if (atWall(a)) b.x = a.x + sg * md; else a.x = b.x - sg * md;
    clampX(a); clampX(b);
  }
}

function stepBoth(s, act) {
  const [a, b] = s.p;
  stepFighter(s, a, b, act);
  stepFighter(s, b, a, act);
  resolveHits(s);
  separate(s);
}

function checkEnd(s) {
  const [a, b] = s.p;
  const k0 = a.hp <= 0, k1 = b.hp <= 0;
  if (!k0 && !k1 && s.timer > 0) return;
  const w = k0 && k1 ? -1 : k1 ? 0 : k0 ? 1 : a.hp > b.hp ? 0 : b.hp > a.hp ? 1 : -1;
  s.phase = 'ko'; s.pt = 0; s.winner = w;
  if (w >= 0) s.p[w].wins++;
  s.ev.push({ k: 'ann', text: k0 || k1 ? 'УНАЛАА!' : 'ЦАГ ДУУСЛАА', big: true });
  s.ev.push({ k: 'ko' });
}

function step(s, i0, i1) {
  s.frame++;
  const [a, b] = s.p;
  readInput(a, i0); readInput(b, i1);
  if (s.freeze > 0) { s.freeze--; return; }
  if (s.hitstop > 0) { s.hitstop--; return; }
  s.pt++;
  switch (s.phase) {
    case 'intro':
      if (s.pt === 1) s.ev.push({ k: 'ann', text: 'БАРИЛДААН ' + s.round });
      if (s.pt === 70) s.ev.push({ k: 'ann', text: 'БАРИЛД!', big: true });
      if (s.pt >= 90) { s.phase = 'fight'; s.pt = 0; setIdle(a); setIdle(b); }
      stepBoth(s, false);
      break;
    case 'fight':
      if (s.timer > 0) s.timer--;
      stepBoth(s, true);
      checkEnd(s);
      break;
    case 'ko':
      if (s.pt >= 40 || (s.pt & 1) === 0) stepBoth(s, false); // slow motion
      if (s.pt === 90) {
        if (s.winner >= 0) {
          const w = s.p[s.winner];
          w.mv = null; w.st = 'win'; w.t = 0;
          s.ev.push({ k: 'ann', text: CHARS[w.ci].name + ' ЯЛЛАА!' });
        } else s.ev.push({ k: 'ann', text: 'ТЭНЦЛЭЭ!' });
      }
      if (s.pt >= 240) {
        if (a.wins >= 2 || b.wins >= 2) {
          s.phase = 'over'; s.pt = 0; s.ready = [false, false];
          s.champ = a.wins >= 2 ? 0 : 1;
          s.ev.push({ k: 'ann', text: 'АВАРГА', sub: CHARS[s.p[s.champ].ci].name, hold: true });
        } else resetRound(s);
      }
      break;
    case 'over':
      stepBoth(s, false);
      for (const f of s.p) if (f.buf & BTN.LP) { s.ready[f.idx] = true; f.buf = 0; }
      if (s.ready[0] && s.ready[1]) {
        for (const f of s.p) { f.wins = 0; f.meter = 0; }
        s.round = 0; s.champ = -1;
        s.ev.push({ k: 'ann', text: '', clear: true });
        resetRound(s);
      }
      break;
  }
}

function hashState(s) {
  let h = s.frame | 0;
  for (const f of s.p) {
    h = (h * 31 + Math.round(f.x * 100)) | 0;
    h = (h * 31 + Math.round(f.y * 100)) | 0;
    h = (h * 31 + Math.round(f.hp * 100)) | 0;
    h = (h * 31 + Math.round(f.meter * 100)) | 0;
  }
  return h;
}

if (typeof module !== 'undefined') module.exports = { newMatch, step, hashState, BTN, MV, CHARS };

// ===================================================================== CLIENT
if (typeof window !== 'undefined') (function client() {

const cv = document.getElementById('cv');
const ctx = cv.getContext('2d');
const FONT = '"Russo One", Impact, sans-serif';
const D2R = Math.PI / 180;

// ---------------------------------------------------------------- poses
const mk = p => Object.assign({ hy: 74, tr: 0, hd: 0, rot: 0, aF: [0, 0], aB: [0, 0], lF: [0, 0], lB: [0, 0] }, p);
const PS = {
  idle:   mk({ hy: 74, tr: 22, hd: -12, aF: [62, 105], aB: [48, 92], lF: [25, -5], lB: [-25, -15] }),
  crouch: mk({ hy: 50, tr: 38, hd: -28, aF: [72, 112], aB: [58, 98], lF: [75, -20], lB: [60, -45] }),
  jump:   mk({ hy: 74, tr: 12, hd: -8, aF: [115, 150], aB: [95, 130], lF: [85, -15], lB: [30, -50] }),
  block:  mk({ hy: 73, tr: 4, hd: 8, aF: [30, 168], aB: [22, 155], lF: [22, -5], lB: [-25, -15] }),
  cblock: mk({ hy: 50, tr: 22, hd: 0, aF: [30, 168], aB: [22, 155], lF: [75, -20], lB: [60, -45] }),
  jabW:   mk({ hy: 74, tr: 22, hd: -12, aF: [40, 125], aB: [48, 92], lF: [25, -5], lB: [-25, -15] }),
  jab:    mk({ hy: 74, tr: 26, hd: -14, aF: [90, 90], aB: [38, 100], lF: [30, -5], lB: [-30, -15] }),
  crossW: mk({ hy: 72, tr: 6, hd: -4, aF: [28, 75], aB: [75, 115], lF: [20, -5], lB: [-30, -15] }),
  cross:  mk({ hy: 71, tr: 42, hd: -34, aF: [97, 94], aB: [18, 50], lF: [42, 0], lB: [-42, -22] }),
  kickW:  mk({ hy: 76, tr: 2, hd: -5, aF: [60, 120], aB: [30, 90], lF: [95, 5], lB: [-5, -5] }),
  kick:   mk({ hy: 79, tr: -20, hd: 12, aF: [55, 115], aB: [-25, 35], lF: [100, 97], lB: [-6, -3] }),
  cjab:   mk({ hy: 50, tr: 42, hd: -30, aF: [96, 92], aB: [50, 95], lF: [75, -20], lB: [60, -45] }),
  upperW: mk({ hy: 44, tr: 48, hd: -30, aF: [18, -5], aB: [60, 100], lF: [78, -25], lB: [62, -50] }),
  upper:  mk({ hy: 84, tr: -2, hd: 18, aF: [168, 176], aB: [22, 45], lF: [14, 0], lB: [-14, -8] }),
  sweepW: mk({ hy: 42, tr: 46, hd: -28, aF: [65, 100], aB: [35, 60], lF: [72, -30], lB: [60, -50] }),
  sweep:  mk({ hy: 32, tr: 58, hd: -35, aF: [95, 120], aB: [15, 5], lF: [93, 92], lB: [60, -72] }),
  jlp:    mk({ hy: 74, tr: 16, hd: -10, aF: [96, 94], aB: [60, 112], lF: [85, -15], lB: [30, -50] }),
  jhpW:   mk({ hy: 74, tr: -5, hd: 5, aF: [175, 190], aB: [150, 170], lF: [70, -20], lB: [20, -50] }),
  jhp:    mk({ hy: 74, tr: 35, hd: -25, aF: [110, 60], aB: [80, 40], lF: [80, -10], lB: [20, -50] }),
  jkk:    mk({ hy: 74, tr: -12, hd: 8, aF: [95, 140], aB: [60, 120], lF: [105, 100], lB: [25, -60] }),
  dive:   mk({ hy: 74, tr: 22, hd: -20, rot: 40, aF: [128, 110], aB: [230, 245], lF: [-12, -30], lB: [-32, -55] }),
  rush:   mk({ hy: 62, tr: 62, hd: -45, aF: [100, 102], aB: [93, 95], lF: [48, 6], lB: [-55, -40] }),
  grab:   mk({ hy: 70, tr: 36, hd: -25, aF: [92, 96], aB: [88, 92], lF: [36, 0], lB: [-36, -20] }),
  lift:   mk({ hy: 72, tr: -8, hd: 22, aF: [172, 178], aB: [166, 174], lF: [22, -5], lB: [-25, -10] }),
  slam:   mk({ hy: 52, tr: 66, hd: -35, aF: [112, 140], aB: [106, 132], lF: [72, -20], lB: [-22, -42] }),
  hit:    mk({ hy: 72, tr: -24, hd: -22, aF: [15, -15], aB: [-25, -45], lF: [22, 0], lB: [-30, -10] }),
  airhit: mk({ hy: 74, tr: -15, hd: -20, rot: -35, aF: [150, 170], aB: [205, 215], lF: [30, 20], lB: [-10, -20] }),
  down:   mk({ hy: 13, tr: 0, hd: 0, rot: -90, aF: [25, 35], aB: [-15, -5], lF: [4, 0], lB: [-4, 8] }),
  intro:  mk({ hy: 79, tr: 0, hd: 0, aF: [8, 4], aB: [-8, -4], lF: [6, 0], lB: [-6, 0] }),
};
const ANIM = {
  jab: { w: PS.jabW, h: PS.jab }, cross: { w: PS.crossW, h: PS.cross }, kick: { w: PS.kickW, h: PS.kick },
  cjab: { w: PS.crouch, h: PS.cjab }, upper: { w: PS.upperW, h: PS.upper }, sweep: { w: PS.sweepW, h: PS.sweep },
  jlp: { w: PS.jump, h: PS.jlp }, jhp: { w: PS.jhpW, h: PS.jhp }, jkk: { w: PS.jump, h: PS.jkk },
  dive: { w: PS.crouch, h: PS.dive }, rush: { w: PS.crossW, h: PS.rush },
};
const ease = t => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);
function lp(a, b, t) {
  t = Math.max(0, Math.min(1, t));
  const o = {};
  for (const k in a) {
    const va = a[k], vb = b[k];
    o[k] = Array.isArray(va) ? [va[0] + (vb[0] - va[0]) * t, va[1] + (vb[1] - va[1]) * t] : va + (vb - va) * t;
  }
  return o;
}
const mod = (p, o) => Object.assign({}, p, o);

function getPose(s, f, o) {
  const fr = s.frame;
  switch (f.st) {
    case 'intro': return mod(PS.intro, { hy: 79 + Math.sin(fr * 0.07 + f.idx) * 0.8 });
    case 'idle': {
      const b = Math.sin(fr * 0.1 + f.idx * 2);
      return mod(PS.idle, { hy: 74 + b * 1.4, aF: [62 + b * 5, 105 + b * 5], aB: [48 - b * 4, 92 - b * 3] });
    }
    case 'walk': {
      const ph = f.t * 0.24, sw = Math.sin(ph);
      return mod(PS.idle, {
        hy: 73 + Math.abs(Math.cos(ph)) * 2,
        lF: [25 + sw * 20, -5 - Math.max(0, sw) * 18], lB: [-25 - sw * 20, -15 - Math.max(0, -sw) * 18],
        aF: [62 - sw * 6, 105], aB: [48 + sw * 6, 92],
      });
    }
    case 'crouch': return PS.crouch;
    case 'jump': return f.vy < -5 ? mod(PS.jump, { lF: [40, -5], lB: [-10, -25] }) : PS.jump;
    case 'block': return f.cb ? PS.cblock : PS.block;
    case 'hit': return lp(PS.hit, PS.idle, (f.t - 6) / 14);
    case 'air': return PS.airhit;
    case 'down': case 'ko': return PS.down;
    case 'getup': return lp(PS.down, PS.crouch, ease(f.t / 16));
    case 'grabmiss': return lp(PS.grab, PS.idle, f.t / 22);
    case 'win': {
      const w = Math.sin(f.t * 0.13), k = Math.sin(f.t * 0.065);
      return mk({
        hy: 77, tr: 8, hd: -12,
        aF: [118 + w * 22, 138 + w * 30], aB: [240 - w * 22, 222 - w * 30],
        lF: [8 + Math.max(0, k) * 60, -Math.max(0, k) * 30],
        lB: [-8 + Math.max(0, -k) * 60, -10 - Math.max(0, -k) * 30],
      });
    }
    case 'atk': {
      const m = MV[f.mv], A = ANIM[m.an], t = f.mt;
      const base = m.air ? PS.jump : m.crouch ? PS.crouch : PS.idle;
      if (t <= m.su) return lp(base, A.w, ease(t / m.su));
      if (m.air || t <= m.su + m.ac) return (m.leap || m.lunge) ? A.h : lp(A.w, A.h, (t - m.su) / 2);
      return lp(A.h, m.leap ? PS.crouch : base, ease((t - m.su - m.ac) / m.re));
    }
    case 'throw': {
      const T = f.t;
      if (f.thr === 0) {
        if (T < 16) return PS.grab;
        if (T < 30) return lp(PS.grab, PS.lift, ease((T - 16) / 8));
        if (T < 36) return lp(PS.lift, PS.slam, ease((T - 30) / 6));
        return lp(PS.slam, PS.idle, ease((T - 36) / 14));
      }
      if (T <= 84) {
        const p = (T % 28) / 28;
        return p < 0.5 ? lp(PS.slam, PS.lift, ease(p * 2)) : lp(PS.lift, PS.slam, ease((p - 0.5) * 2));
      }
      return lp(PS.slam, PS.idle, ease((T - 84) / 16));
    }
    case 'thrown': {
      const T = o.t;
      if (o.thr === 0 && T < 16) return PS.hit;
      if (o.thr === 0 && T < 20) return lp(PS.hit, PS.down, (T - 16) / 4);
      return PS.down;
    }
  }
  return PS.idle;
}

// ---------------------------------------------------------------- body drawing
function seg(g, a, b, w, c) { g.strokeStyle = c; g.lineWidth = w; g.beginPath(); g.moveTo(a.x, a.y); g.lineTo(b.x, b.y); g.stroke(); }
function dot(g, p, r, c) { g.fillStyle = c; g.beginPath(); g.arc(p.x, p.y, r, 0, Math.PI * 2); g.fill(); }
const add = (p, v, k) => ({ x: p.x + v.x * k, y: p.y + v.y * k });
const mix = (a, b, t) => ({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t });

function drawBody(g, P, col, hat) {
  g.lineCap = 'round'; g.lineJoin = 'round';
  const hip = { x: 0, y: -P.hy };
  const rot = P.rot * D2R, cr = Math.cos(rot), sr = Math.sin(rot);
  const Rt = p => ({ x: hip.x + (p.x - hip.x) * cr - (p.y - hip.y) * sr, y: hip.y + (p.x - hip.x) * sr + (p.y - hip.y) * cr });
  const L = (p, a, l) => ({ x: p.x + Math.sin(a * D2R) * l, y: p.y + Math.cos(a * D2R) * l });
  const tr = P.tr * D2R;
  const up = { x: Math.sin(tr), y: -Math.cos(tr) }, back = { x: -Math.cos(tr), y: -Math.sin(tr) };
  const at = k => add(hip, up, k);
  const neck = at(56), sh = at(48);
  const ha = (P.tr + P.hd) * D2R;
  const head = { x: neck.x + Math.sin(ha) * 15, y: neck.y - Math.cos(ha) * 15 };
  const shB = { x: sh.x - 4, y: sh.y + 1 }, shF = { x: sh.x + 2, y: sh.y };
  const eF = L(shF, P.aF[0], 31), hF = L(eF, P.aF[1], 28);
  const eB = L(shB, P.aB[0], 31), hB = L(eB, P.aB[1], 28);
  const hipF = { x: hip.x + 5, y: hip.y + 2 }, hipB = { x: hip.x - 5, y: hip.y + 2 };
  const kF = L(hipF, P.lF[0], 40), fF = L(kF, P.lF[1], 39);
  const kB = L(hipB, P.lB[0], 40), fB = L(kB, P.lB[1], 39);
  const tF = L(hipF, P.lF[0], 9), tB = L(hipB, P.lB[0], 9);
  const pts = {
    hip, neck, sh, head, shB, shF, eF, hF, eB, hB, hipF, hipB, kF, fF, kB, fB, tF, tB,
    zA: add(at(52), back, 6), zB: add(at(24), back, 9), cA: add(at(22), back, 16), cB: add(at(22), back, -16), sT: at(7),
  };
  for (const k in pts) pts[k] = Rt(pts[k]);
  const p = pts;
  const fwd = { x: cr, y: sr }, upv = { x: sr, y: -cr };

  const foot = (a, c) => {
    g.fillStyle = c; g.beginPath();
    const q = [add(add(a, fwd, -7), upv, 9), add(add(a, fwd, -7), upv, -1), add(add(a, fwd, 13), upv, -1),
      add(add(a, fwd, 20), upv, 8), add(add(a, fwd, 8), upv, 7)];
    g.moveTo(q[0].x, q[0].y); for (let i = 1; i < q.length; i++) g.lineTo(q[i].x, q[i].y);
    g.closePath(); g.fill();
  };

  // far arm
  seg(g, p.shB, p.eB, 14, col.z2); seg(g, p.eB, p.hB, 12, col.z2); dot(g, p.hB, 6, col.skin2);
  // far leg
  seg(g, p.hipB, p.kB, 18, col.skin2); seg(g, p.kB, p.fB, 15, col.boot); foot(p.fB, col.boot);
  seg(g, p.kB, mix(p.kB, p.fB, 0.12), 16, col.boot2);
  // torso + zodog
  seg(g, p.hip, p.neck, 34, col.skin);
  seg(g, mix(p.hip, p.neck, 0.45), mix(p.hip, p.neck, 0.75), 3, 'rgba(0,0,0,.12)');
  seg(g, p.zB, p.zA, 22, col.z);
  dot(g, p.sh, 12.5, col.z);
  seg(g, p.cA, p.cB, 3, col.trim);
  // shuudag
  seg(g, p.sT, p.tF, 20, col.z); seg(g, p.sT, p.tB, 20, col.z);
  // near leg
  seg(g, p.hipF, p.kF, 19, col.skin); seg(g, p.kF, p.fF, 16, col.boot); foot(p.fF, col.boot);
  seg(g, p.kF, mix(p.kF, p.fF, 0.14), 17, col.boot2);
  // head
  seg(g, p.neck, p.head, 12, col.skin);
  const A = rot + ha, hu = { x: Math.sin(A), y: -Math.cos(A) }, hv = { x: Math.cos(A), y: Math.sin(A) };
  dot(g, add(add(p.head, hv, -3), hu, 2), 13.5, '#16100c');
  dot(g, add(add(p.head, hv, 1.5), hu, -0.5), 12.5, col.skin);
  dot(g, add(p.head, hv, -2), 3.5, col.skin2);
  dot(g, add(add(p.head, hv, 7.5), hu, 2), 1.8, '#1a0f0a');
  seg(g, add(add(p.head, hv, 5), hu, 5.5), add(add(p.head, hv, 10), hu, 4.5), 2, '#16100c');
  if (hat) {
    const base = add(p.head, hu, 8), tip = add(add(p.head, hu, 31), hv, -3);
    const b1 = add(base, hv, -15), b2 = add(base, hv, 15);
    g.fillStyle = col.hat; g.beginPath(); g.moveTo(b1.x, b1.y); g.lineTo(tip.x, tip.y); g.lineTo(b2.x, b2.y); g.closePath(); g.fill();
    seg(g, b1, b2, 7, '#2a1a10');
    seg(g, b2, add(add(b2, hu, 9), hv, 2), 5, '#2a1a10');
    dot(g, tip, 4, col.trim);
    seg(g, tip, add(add(tip, hu, -10), hv, -10), 2, '#c4202c');
  }
  // near arm
  seg(g, p.shF, p.eF, 15, col.z); seg(g, p.eF, p.hF, 13, col.z);
  seg(g, mix(p.eF, p.hF, 0.78), mix(p.eF, p.hF, 0.92), 14, col.trim);
  dot(g, p.hF, 7, col.skin);
}

function colorsOf(f) { const c = CHARS[f.ci].col; return f.alt ? Object.assign({}, c, ALT) : c; }

function drawFighter(g, s, f, glow) {
  const o = s.p[1 - f.idx], c = CHARS[f.ci];
  const P = getPose(s, f, o);
  g.save();
  g.translate(f.x, GROUND - f.y);
  g.scale(f.face * c.size, c.size);
  if (glow) { g.shadowColor = '#ffd34d'; g.shadowBlur = 24; }
  drawBody(g, P, colorsOf(f), f.st === 'intro');
  g.restore();
}

// ---------------------------------------------------------------- background
function rng(seed) { let x = seed >>> 0 || 1; return () => { x ^= x << 13; x >>>= 0; x ^= x >>> 17; x ^= x << 5; x >>>= 0; return x / 4294967296; }; }
function makeBG() {
  const c = document.createElement('canvas'); c.width = W; c.height = H;
  const g = c.getContext('2d'), r = rng(7);
  let gr = g.createLinearGradient(0, 0, 0, 380);
  gr.addColorStop(0, '#141c45'); gr.addColorStop(0.45, '#4b3a78'); gr.addColorStop(0.78, '#d8684a'); gr.addColorStop(1, '#f7c56a');
  g.fillStyle = gr; g.fillRect(0, 0, W, 380);
  for (let i = 0; i < 80; i++) { g.fillStyle = `rgba(255,255,255,${(r() * 0.7).toFixed(2)})`; g.fillRect(r() * W, r() * 150, 1.5, 1.5); }
  const sg = g.createRadialGradient(690, 335, 10, 690, 335, 170);
  sg.addColorStop(0, 'rgba(255,240,190,1)'); sg.addColorStop(0.25, 'rgba(255,200,120,.75)'); sg.addColorStop(1, 'rgba(255,150,80,0)');
  g.fillStyle = sg; g.fillRect(480, 150, 420, 240);
  dot(g, { x: 690, y: 335 }, 46, '#fff1c4');
  const mount = (base, amp, col, fq) => {
    const p1 = r() * 6, p2 = r() * 6;
    g.fillStyle = col; g.beginPath(); g.moveTo(0, 380);
    for (let x = 0; x <= W; x += 8) g.lineTo(x, base - amp * (0.5 + 0.3 * Math.sin(x * fq * 2 + p1) + 0.2 * Math.sin(x * fq * 5.3 + p2)));
    g.lineTo(W, 380); g.closePath(); g.fill();
  };
  mount(300, 90, '#5b3f6b', 0.004); mount(340, 55, '#3d2f4f', 0.008);
  gr = g.createLinearGradient(0, 350, 0, H);
  gr.addColorStop(0, '#8a8a45'); gr.addColorStop(0.3, '#6b7a35'); gr.addColorStop(1, '#3e4a20');
  g.fillStyle = gr; g.fillRect(0, 352, W, H - 352);
  const ger = (x, y, s) => {
    g.fillStyle = '#f3efe6'; g.fillRect(x - s / 2, y - s * 0.45, s, s * 0.45);
    g.beginPath(); g.moveTo(x - s / 2 - 2, y - s * 0.45); g.quadraticCurveTo(x, y - s * 0.95, x + s / 2 + 2, y - s * 0.45); g.fill();
    g.fillStyle = '#b23a2a'; g.fillRect(x - s * 0.1, y - s * 0.3, s * 0.2, s * 0.3);
    g.strokeStyle = 'rgba(120,90,60,.55)'; g.lineWidth = 1.5;
    g.beginPath(); g.moveTo(x - s / 2, y - s * 0.3); g.lineTo(x + s / 2, y - s * 0.3); g.stroke();
  };
  [[120, 374, 26], [175, 378, 34], [820, 372, 24], [868, 377, 32], [460, 366, 16]].forEach(a => ger(...a));
  // prayer flags
  const fc = ['#2f6fd6', '#f4f1e8', '#c4202c', '#2f9e4f', '#f2c14e'];
  for (const [x0, y0, x1, y1] of [[-10, 60, W / 2, 105], [W / 2, 105, W + 10, 50]]) {
    const cx = (x0 + x1) / 2, cy = Math.max(y0, y1) + 40;
    g.strokeStyle = 'rgba(30,20,20,.7)'; g.lineWidth = 1.5;
    g.beginPath(); g.moveTo(x0, y0); g.quadraticCurveTo(cx, cy, x1, y1); g.stroke();
    for (let i = 1; i < 24; i++) {
      const t = i / 24, u = 1 - t;
      const x = u * u * x0 + 2 * u * t * cx + t * t * x1, y = u * u * y0 + 2 * u * t * cy + t * t * y1;
      g.fillStyle = fc[i % 5]; g.fillRect(x - 5, y, 10, 13);
    }
  }
  // crowd
  const cc = ['#2a1d2e', '#3a2a3e', '#1d2633', '#4a2a22', '#28324a'];
  for (let row = 0; row < 2; row++) {
    for (let x = -10; x < W + 10; x += 13 + r() * 7) {
      const y = 402 + row * 14 + r() * 4;
      g.fillStyle = cc[(r() * cc.length) | 0];
      g.beginPath(); g.arc(x, y, 6, 0, Math.PI * 2); g.fill();
      g.fillRect(x - 8, y + 4, 16, 24);
      if (r() < 0.3) { g.beginPath(); g.moveTo(x - 7, y - 3); g.lineTo(x, y - 14); g.lineTo(x + 7, y - 3); g.fill(); }
    }
  }
  gr = g.createLinearGradient(0, 432, 0, H);
  gr.addColorStop(0, '#9c7a4c'); gr.addColorStop(1, '#6e5232');
  g.fillStyle = gr; g.fillRect(0, 432, W, H - 432);
  g.fillStyle = 'rgba(255,220,150,.13)'; g.beginPath(); g.ellipse(W / 2, 492, 430, 46, 0, 0, Math.PI * 2); g.fill();
  for (let i = 0; i < 120; i++) { g.fillStyle = `rgba(60,40,20,${(0.2 + r() * 0.3).toFixed(2)})`; g.fillRect(r() * W, 436 + r() * 100, 2, 2); }
  return c;
}
const BG = makeBG();

// ---------------------------------------------------------------- sound
let AC = null;
function ac() {
  if (!AC) { try { AC = new (window.AudioContext || window.webkitAudioContext)(); } catch (e) { return null; } }
  if (AC.state === 'suspended') AC.resume();
  return AC;
}
function sNoise(dur, gain, fq, type) {
  const a = ac(); if (!a) return;
  const n = (a.sampleRate * dur) | 0, buf = a.createBuffer(1, n, a.sampleRate), d = buf.getChannelData(0);
  for (let i = 0; i < n; i++) d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / n, 2);
  const src = a.createBufferSource(); src.buffer = buf;
  const f = a.createBiquadFilter(); f.type = type || 'lowpass'; f.frequency.value = fq;
  const g = a.createGain(); g.gain.value = gain;
  src.connect(f).connect(g).connect(a.destination); src.start();
}
function sTone(f0, f1, dur, gain, type) {
  const a = ac(); if (!a) return;
  const o = a.createOscillator(), g = a.createGain(), t = a.currentTime;
  o.type = type || 'sine';
  o.frequency.setValueAtTime(f0, t); o.frequency.exponentialRampToValueAtTime(Math.max(20, f1), t + dur);
  g.gain.setValueAtTime(gain, t); g.gain.exponentialRampToValueAtTime(0.001, t + dur);
  o.connect(g).connect(a.destination); o.start(t); o.stop(t + dur + 0.02);
}
const SFX = {
  hit: b => { sNoise(b ? 0.18 : 0.1, b ? 0.55 : 0.35, b ? 1800 : 2600); sTone(b ? 140 : 210, 50, b ? 0.25 : 0.12, b ? 0.7 : 0.4); },
  block: () => { sNoise(0.06, 0.3, 3500, 'highpass'); sTone(900, 600, 0.05, 0.1, 'square'); },
  whoosh: b => sNoise(b ? 0.18 : 0.1, 0.14, b ? 900 : 1400, 'bandpass'),
  slam: () => { sTone(95, 32, 0.45, 0.9); sNoise(0.35, 0.55, 600); },
  land: () => sNoise(0.07, 0.15, 500),
  superS: () => { sTone(180, 900, 0.5, 0.2, 'sawtooth'); sTone(90, 360, 0.6, 0.3); },
  ko: () => { sTone(120, 30, 1.2, 0.8); sNoise(0.8, 0.45, 400); },
  gong: () => { sTone(220, 200, 1.2, 0.3, 'triangle'); sTone(331, 300, 0.9, 0.12, 'sine'); },
};

// ---------------------------------------------------------------- fx state
const R = { parts: [], shake: 0, flash: 0, ann: null, combo: [null, null], spec: [null, null], hpLag: [1, 1], superT: 0, superP: 0, superName: '' };
function resetFx() { R.parts = []; R.shake = 0; R.flash = 0; R.ann = null; R.combo = [null, null]; R.spec = [null, null]; R.hpLag = [1, 1]; R.superT = 0; }

function addSpark(x, y, big, col) {
  const n = big ? 16 : 9;
  for (let i = 0; i < n; i++) {
    const a = Math.random() * Math.PI * 2, sp = (big ? 9 : 5.5) * (0.4 + Math.random());
    R.parts.push({ k: 'spark', x, y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp, life: big ? 18 : 12, max: big ? 18 : 12, col });
  }
  R.parts.push({ k: 'ring', x, y, r: 6, life: 12, max: 12, big, col });
}
function addDust(x, big) {
  const n = big ? 16 : 6;
  for (let i = 0; i < n; i++) {
    R.parts.push({ k: 'dust', x: x + (Math.random() - 0.5) * (big ? 70 : 40), y: 4, vx: (Math.random() - 0.5) * (big ? 5 : 3),
      vy: Math.random() * 2, r: 4 + Math.random() * (big ? 9 : 5), life: 30, max: 30 });
  }
}

function drainEvents(s) {
  for (const e of s.ev) {
    switch (e.k) {
      case 'hit': addSpark(e.x, e.y, e.big, '#fff3a0'); R.shake = Math.max(R.shake, e.big ? 9 : 4); SFX.hit(e.big); break;
      case 'block': addSpark(e.x, e.y, false, '#9fd4ff'); SFX.block(); break;
      case 'whoosh': SFX.whoosh(e.big); break;
      case 'dust': addDust(e.x, false); SFX.land(); break;
      case 'thud': addDust(e.x, true); R.shake = Math.max(R.shake, 6); SFX.slam(); break;
      case 'slam': addDust(e.x, true); R.parts.push({ k: 'ring', x: e.x, y: 4, r: 10, life: 16, max: 16, big: true, col: '#ffd9a0' });
        R.shake = Math.max(R.shake, 14); SFX.slam(); break;
      case 'grab': SFX.block(); break;
      case 'tech': addSpark(e.x, e.y, true, '#ffffff'); R.spec[0] = R.spec[1] = null; R.ann = { text: 'МУЛТАРЛАА!', t: 0, small: true }; SFX.block(); break;
      case 'super': R.superT = 40; R.superP = e.p; R.superName = CHARS[s.p[e.p].ci].sup; SFX.superS(); break;
      case 'special': R.spec[e.p] = { name: e.name, t: 0 }; break;
      case 'combo': R.combo[e.p] = { n: e.n, name: e.name || (R.combo[e.p] && R.combo[e.p].n < e.n ? R.combo[e.p].name : ''), t: 0 }; break;
      case 'ann': R.ann = e.clear ? null : { text: e.text, sub: e.sub, big: e.big, hold: e.hold, t: 0 }; if (!e.clear) SFX.gong(); break;
      case 'ko': R.shake = 18; R.flash = 10; SFX.ko(); break;
    }
  }
  s.ev.length = 0;
}

function fxUpdate() {
  for (const p of R.parts) {
    p.life--;
    if (p.k === 'spark') { p.x += p.vx; p.y -= p.vy; p.vx *= 0.88; p.vy *= 0.88; }
    else if (p.k === 'dust') { p.x += p.vx; p.y += p.vy; p.r += 0.5; p.vx *= 0.95; }
    else if (p.k === 'ring') p.r += p.big ? 6 : 4;
  }
  R.parts = R.parts.filter(p => p.life > 0);
  R.shake *= 0.86; if (R.shake < 0.3) R.shake = 0;
  if (R.flash > 0) R.flash--;
  if (R.superT > 0) R.superT--;
  if (R.ann) R.ann.t++;
  for (let i = 0; i < 2; i++) {
    if (R.combo[i]) { R.combo[i].t++; if (R.combo[i].t > 100) R.combo[i] = null; }
    if (R.spec[i]) { R.spec[i].t++; if (R.spec[i].t > 60) R.spec[i] = null; }
    if (S) {
      const f = S.p[i], r = Math.max(0, f.hp / CHARS[f.ci].hp);
      if (R.hpLag[i] > r) R.hpLag[i] = Math.max(r, R.hpLag[i] - 0.006); else R.hpLag[i] = r;
    }
  }
}

// ---------------------------------------------------------------- render
function bigText(g, t, x, y, size, fill) {
  g.font = `${size}px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
  g.lineJoin = 'round'; g.lineWidth = Math.max(3, size / 7); g.strokeStyle = '#1a0a05';
  g.strokeText(t, x, y); g.fillStyle = fill; g.fillText(t, x, y);
}

function drawParts(g) {
  for (const p of R.parts) {
    const a = p.life / p.max, sy = GROUND - p.y;
    if (p.k === 'spark') {
      g.strokeStyle = p.col; g.globalAlpha = a; g.lineWidth = 3;
      g.beginPath(); g.moveTo(p.x, sy); g.lineTo(p.x - p.vx * 2.2, sy + p.vy * 2.2); g.stroke();
    } else if (p.k === 'dust') {
      g.globalAlpha = a * 0.5; dot(g, { x: p.x, y: sy }, p.r, '#c9a878');
    } else if (p.k === 'ring') {
      g.globalAlpha = a * 0.8; g.strokeStyle = p.col; g.lineWidth = p.big ? 4 : 2.5;
      g.beginPath();
      if (p.y < 10) g.ellipse(p.x, sy, p.r * 1.6, p.r * 0.35, 0, 0, Math.PI * 2); else g.arc(p.x, sy, p.r, 0, Math.PI * 2);
      g.stroke();
    }
  }
  g.globalAlpha = 1;
}

function drawHUD(g, s) {
  const bw = 370, bh = 24, y = 22;
  for (let i = 0; i < 2; i++) {
    const f = s.p[i], c = CHARS[f.ci], r = Math.max(0, f.hp / c.hp), x = i === 0 ? 30 : W - 30 - bw;
    g.fillStyle = 'rgba(0,0,0,.65)'; g.fillRect(x - 4, y - 4, bw + 8, bh + 8);
    g.fillStyle = '#3a0a0e'; g.fillRect(x, y, bw, bh);
    const lw = bw * R.hpLag[i], hw = bw * r;
    g.fillStyle = '#e8412c'; g.fillRect(i === 0 ? x : x + bw - lw, y, lw, bh);
    const gr = g.createLinearGradient(0, y, 0, y + bh); gr.addColorStop(0, '#fff09a'); gr.addColorStop(1, '#e0a020');
    g.fillStyle = r < 0.25 ? (s.frame % 20 < 10 ? '#ff7a3a' : '#ffb03a') : gr;
    g.fillRect(i === 0 ? x : x + bw - hw, y, hw, bh);
    g.strokeStyle = '#f2c14e'; g.lineWidth = 2; g.strokeRect(x - 1, y - 1, bw + 2, bh + 2);
    g.font = `22px ${FONT}`; g.textBaseline = 'top'; g.textAlign = i === 0 ? 'left' : 'right';
    g.lineWidth = 4; g.strokeStyle = '#000'; g.strokeText(c.name, i === 0 ? x : x + bw, y + bh + 6);
    g.fillStyle = '#fff'; g.fillText(c.name, i === 0 ? x : x + bw, y + bh + 6);
    for (let w = 0; w < 2; w++) {
      const cx = i === 0 ? x + bw - 12 - w * 26 : x + 12 + w * 26;
      dot(g, { x: cx, y: y + bh + 18 }, 9, '#1a1208');
      dot(g, { x: cx, y: y + bh + 18 }, 7, f.wins > w ? '#f2c14e' : '#3a3022');
    }
    // super meter
    const mw = 230, mh = 12, my = H - 30, mx = i === 0 ? 30 : W - 30 - mw, full = f.meter >= 100;
    g.fillStyle = 'rgba(0,0,0,.65)'; g.fillRect(mx - 3, my - 3, mw + 6, mh + 6);
    g.fillStyle = full ? (s.frame % 16 < 8 ? '#ffd34d' : '#ff8a2a') : '#3d8bff';
    const fw = mw * f.meter / 100; g.fillRect(i === 0 ? mx : mx + mw - fw, my, fw, mh);
    g.font = `15px ${FONT}`; g.textAlign = i === 0 ? 'left' : 'right'; g.textBaseline = 'bottom';
    g.fillStyle = full ? '#ffd34d' : '#cfd8ff';
    g.fillText(full ? 'СУПЕР БЭЛЭН! ↓ ↓ + K' : 'ХҮЧ', i === 0 ? mx : mx + mw, my - 5);
  }
  g.fillStyle = 'rgba(0,0,0,.7)'; g.fillRect(W / 2 - 34, 12, 68, 50);
  g.strokeStyle = '#f2c14e'; g.lineWidth = 2; g.strokeRect(W / 2 - 34, 12, 68, 50);
  bigText(g, String(Math.ceil(s.timer / 60)), W / 2, 38, 34, '#fff');
}

function drawTexts(g, s) {
  for (let i = 0; i < 2; i++) {
    const cmb = R.combo[i];
    if (cmb) {
      g.globalAlpha = cmb.t > 80 ? (100 - cmb.t) / 20 : 1;
      const x = i === 0 ? 150 : W - 150, sc = cmb.t < 6 ? 1.4 - cmb.t * 0.066 : 1;
      g.save(); g.translate(x, 170); g.scale(sc, sc);
      bigText(g, `${cmb.n} ЦОХИЛТ`, 0, 0, 40, '#ffd34d');
      const praise = cmb.name || (cmb.n >= 7 ? 'АВАРГЫН!' : cmb.n >= 5 ? 'ГАЙХАЛТАЙ!' : cmb.n >= 3 ? 'САЙН!' : '');
      if (praise) bigText(g, praise, 0, 38, 24, cmb.name ? '#ffffff' : '#9fe0ff');
      g.restore(); g.globalAlpha = 1;
    }
    const sp = R.spec[i];
    if (sp) {
      const f = s.p[i];
      g.globalAlpha = sp.t > 45 ? (60 - sp.t) / 15 : 1;
      bigText(g, sp.name, Math.max(120, Math.min(W - 120, f.x)), GROUND - 205 - f.y, 20, '#ffefc0');
      g.globalAlpha = 1;
    }
  }
  if (R.superT > 0) {
    const t = 40 - R.superT, x = t < 8 ? -400 + t * 60 : t > 32 ? W / 2 + (t - 32) * 120 : W / 2;
    g.fillStyle = 'rgba(200,32,46,.85)'; g.fillRect(0, H * 0.62 - 34, W, 68);
    bigText(g, R.superName, x, H * 0.62, 48, '#ffd34d');
  }
  const a = R.ann;
  if (a) {
    const life = a.hold ? 1e9 : a.small ? 50 : 85;
    if (a.t < life) {
      const sc = a.t < 8 ? 1 + (8 - a.t) / 8 * 1.2 : 1;
      g.save(); g.globalAlpha = a.t > life - 15 ? (life - a.t) / 15 : 1;
      g.translate(W / 2, H * 0.4); g.scale(sc, sc);
      bigText(g, a.text, 0, 0, a.small ? 46 : a.big ? 92 : 66, a.big ? '#ffd34d' : '#ffffff');
      if (a.sub) bigText(g, a.sub, 0, 70, 48, '#ffd34d');
      g.restore(); g.globalAlpha = 1;
    }
  }
  if (s.phase === 'over' && s.pt > 60) {
    const who = MODE === 'online' ? (s.ready[NET.isHost ? 0 : 1] ? 'Найзыгаа хүлээж байна...' : 'Дахин барилдах: J дар')
      : MODE === 'local' ? `Дахин: хоёулаа хөнгөн цохилт дар  (${s.ready[0] ? '✓' : '1-р'} / ${s.ready[1] ? '✓' : '2-р'})`
        : 'Дахин барилдах: J дар';
    bigText(g, who, W / 2, H * 0.4 + 130, 22, '#ffffff');
    bigText(g, 'ESC — цэс', W / 2, H * 0.4 + 160, 16, '#cfd8ff');
  }
}

function render() {
  const g = ctx;
  g.save();
  if (R.shake > 0) g.translate((Math.random() - 0.5) * R.shake, (Math.random() - 0.5) * R.shake);
  g.drawImage(BG, 0, 0);
  const s = S || DEMO;
  if (R.superT > 0 && S) {
    g.fillStyle = `rgba(5,5,18,${0.7 * Math.min(1, R.superT / 8)})`; g.fillRect(-20, -20, W + 40, H + 40);
    const f = s.p[R.superP], cx = f.x, cy = GROUND - 90;
    g.save(); g.translate(cx, cy); g.rotate(s.frame * 0.03);
    for (let i = 0; i < 12; i++) {
      g.rotate(Math.PI / 6); g.fillStyle = i % 2 ? 'rgba(255,211,77,.25)' : 'rgba(200,32,46,.25)';
      g.beginPath(); g.moveTo(0, 0); g.lineTo(600, -50); g.lineTo(600, 50); g.closePath(); g.fill();
    }
    g.restore();
  }
  for (const f of s.p) {
    const sz = CHARS[f.ci].size, k = Math.max(0.35, 1 - f.y / 260);
    g.fillStyle = 'rgba(0,0,0,.3)'; g.beginPath(); g.ellipse(f.x, GROUND + 2, 42 * sz * k, 8 * k, 0, 0, Math.PI * 2); g.fill();
  }
  const pr = f => (f.st === 'atk' || f.st === 'throw') ? 1 : 0;
  const order = [...s.p].sort((a, b) => pr(a) - pr(b));
  for (const f of order) drawFighter(g, s, f, R.superT > 0 && S && f.idx === R.superP);
  drawParts(g);
  g.restore();
  if (R.flash > 0) { g.fillStyle = `rgba(255,255,255,${R.flash / 14})`; g.fillRect(0, 0, W, H); }
  if (S) {
    drawHUD(g, S);
    drawTexts(g, S);
    if (MODE === 'online') {
      g.font = `13px system-ui, sans-serif`; g.textAlign = 'center'; g.textBaseline = 'top'; g.fillStyle = 'rgba(255,255,255,.7)';
      g.fillText(`ping ${NET.rtt == null ? '-' : Math.round(NET.rtt)}ms · delay ${NET.delay}f${NET.desync ? ' · DESYNC!' : ''}`, W / 2, 66);
      if (NET.stall > 20) bigText(g, 'Холболт хүлээж байна...', W / 2, H / 2 + 120, 26, '#ff9a8a');
    }
  }
}

// ---------------------------------------------------------------- input
const KEYS = new Set();
const MAP_SOLO = { KeyW: 'U', ArrowUp: 'U', KeyS: 'D', ArrowDown: 'D', KeyA: 'L', ArrowLeft: 'L', KeyD: 'R', ArrowRight: 'R',
  KeyJ: 'LP', KeyF: 'LP', KeyK: 'HP', KeyG: 'HP', KeyL: 'KK', KeyH: 'KK', KeyI: 'GR', KeyR: 'GR' };
const MAP_P1 = { KeyW: 'U', KeyS: 'D', KeyA: 'L', KeyD: 'R', KeyF: 'LP', KeyG: 'HP', KeyH: 'KK', KeyR: 'GR' };
const MAP_P2 = { ArrowUp: 'U', ArrowDown: 'D', ArrowLeft: 'L', ArrowRight: 'R',
  Comma: 'LP', Numpad1: 'LP', Period: 'HP', Numpad2: 'HP', Slash: 'KK', Numpad3: 'KK', KeyM: 'GR', Numpad0: 'GR' };
function readMap(m) {
  let b = 0;
  for (const k of KEYS) { const v = m[k]; if (v) b |= BTN[v]; }
  if ((b & BTN.L) && (b & BTN.R)) b &= ~(BTN.L | BTN.R);
  if ((b & BTN.U) && (b & BTN.D)) b &= ~BTN.U;
  return b;
}
function readKeys(p) { return MODE === 'local' ? readMap(p === 0 ? MAP_P1 : MAP_P2) : readMap(MAP_SOLO); }

window.addEventListener('keydown', e => {
  if (e.target && e.target.tagName === 'INPUT') return;
  KEYS.add(e.code);
  if (S && (e.code.startsWith('Arrow') || e.code === 'Space' || e.code === 'Slash')) e.preventDefault();
  if (e.code === 'Escape' && S) quitToMenu('');
  ac();
});
window.addEventListener('keyup', e => KEYS.delete(e.code));
window.addEventListener('blur', () => KEYS.clear());

// ---------------------------------------------------------------- CPU
const AI = { q: [], cd: 0 };
function aiPush(seq) { for (const [b, n] of seq) for (let i = 0; i < n; i++) AI.q.push(b); }
function aiInput(s) {
  const me = s.p[1], o = s.p[0];
  if (s.phase === 'over') return s.pt > 90 && s.frame % 40 < 2 ? BTN.LP : 0;
  if (s.phase !== 'fight') { AI.q.length = 0; return 0; }
  if (AI.q.length) return AI.q.shift();
  const F = me.face === 1 ? BTN.R : BTN.L, B = me.face === 1 ? BTN.L : BTN.R;
  const dx = Math.abs(o.x - me.x), r = Math.random();
  if (o.st === 'atk' && dx < 190 && r < 0.5) { aiPush([[B | (MV[o.mv].low ? BTN.D : 0), 12]]); return AI.q.shift(); }
  if (o.y > 30 && o.vy < 0 && dx < 150 && r < 0.45) { aiPush([[BTN.D, 2], [BTN.D | BTN.HP, 1], [0, 10]]); return AI.q.shift(); }
  if (!['idle', 'walk', 'crouch'].includes(me.st)) return 0;
  if (AI.cd > 0) { AI.cd--; return dx > 120 ? F : 0; }
  AI.cd = 6 + ((Math.random() * 16) | 0);
  const q = Math.random();
  if (dx > 300) {
    if (q < 0.15) aiPush([[B, 3], [F, 2], [F | BTN.LP, 1], [0, 4]]);
    else if (q < 0.3) aiPush([[BTN.U | F, 1], [F, 14], [BTN.KK, 1], [0, 14]]);
    else aiPush([[F, 14]]);
  } else if (dx > 130) {
    if (q < 0.22) aiPush([[BTN.D, 2], [BTN.D | F, 2], [F | BTN.HP, 1], [0, 20]]);
    else if (q < 0.42) aiPush([[BTN.U | F, 1], [F, 10], [BTN.HP, 1], [0, 16]]);
    else if (q < 0.55) aiPush([[B, 10]]);
    else aiPush([[F, 10]]);
  } else {
    if (me.meter >= 100 && q < 0.4) aiPush([[BTN.D, 3], [0, 2], [BTN.D, 2], [BTN.D | BTN.HP, 1], [0, 20]]);
    else if (q < 0.3) aiPush([[BTN.LP, 1], [0, 10], [BTN.LP, 1], [0, 10], [BTN.HP, 1], [0, 15], [BTN.D, 2], [BTN.D | F, 2], [F | BTN.HP, 1], [0, 25]]);
    else if (q < 0.42) aiPush([[BTN.GR, 1], [0, 10]]);
    else if (q < 0.56) aiPush([[BTN.D, 2], [BTN.D | BTN.KK, 1], [0, 15]]);
    else if (q < 0.7) aiPush([[BTN.LP, 1], [0, 10], [BTN.KK, 1], [0, 15], [B, 2], [F, 1], [F | BTN.LP, 1], [0, 20]]);
    else aiPush([[B, 12]]);
  }
  return AI.q.length ? AI.q.shift() : 0;
}

// ---------------------------------------------------------------- online (PeerJS lockstep)
const PREFIX = 'bokh-barildaan-v1-';
const NET = { peer: null, conn: null, isHost: false, started: false, delay: 4, frame: 0, local: [], remote: [], remoteMax: -1,
  rtt: null, stall: 0, myChar: 0, remoteChar: null, myHash: {}, theirHash: {}, desync: false, pingTimer: 0 };
const $ = id => document.getElementById(id);
function netStatus(h) { $('netStatus').innerHTML = h; }
function netSend(o) { if (NET.conn && NET.conn.open) NET.conn.send(o); }
function netClose() {
  clearInterval(NET.pingTimer);
  try { if (NET.conn) NET.conn.close(); } catch (e) { /* ignore */ }
  try { if (NET.peer) NET.peer.destroy(); } catch (e) { /* ignore */ }
  Object.assign(NET, { peer: null, conn: null, started: false, remoteChar: null, rtt: null, stall: 0, myHash: {}, theirHash: {}, desync: false });
}
function genCode() { const A = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; let s = ''; for (let i = 0; i < 5; i++) s += A[(Math.random() * A.length) | 0]; return s; }
function needPeer() { if (typeof Peer === 'undefined') { netStatus('PeerJS ачаалагдсангүй — интернэтээ шалга.'); return false; } return true; }

function hostRoom() {
  if (!needPeer()) return;
  netClose();
  const code = genCode();
  NET.isHost = true;
  netStatus('Сервертэй холбогдож байна...');
  NET.peer = new Peer(PREFIX + code);
  NET.peer.on('open', () => netStatus(`Өрөөний код: <b>${code}</b><br>Энэ кодыг найздаа явуул. Хүлээж байна...`));
  NET.peer.on('connection', conn => { if (NET.conn) { conn.close(); return; } setupConn(conn); });
  NET.peer.on('error', e => netStatus(e.type === 'unavailable-id' ? 'Код давхцлаа, дахин үүсгэ.' : 'Алдаа: ' + e.type));
}
function joinRoom(code) {
  if (!needPeer()) return;
  code = code.toUpperCase().trim();
  if (code.length < 5) { netStatus('5 тэмдэгттэй код оруул.'); return; }
  netClose();
  NET.isHost = false;
  netStatus('Холбогдож байна...');
  NET.peer = new Peer();
  NET.peer.on('open', () => setupConn(NET.peer.connect(PREFIX + code, { reliable: true, serialization: 'json' })));
  NET.peer.on('error', e => netStatus(e.type === 'peer-unavailable' ? 'Өрөө олдсонгүй. Кодоо шалга.' : 'Алдаа: ' + e.type));
}
function setupConn(conn) {
  NET.conn = conn;
  conn.on('open', () => {
    netStatus('Холбогдлоо! Эхэлж байна...');
    netSend({ t: 'hello', c: NET.myChar });
    netSend({ t: 'ping', ts: performance.now(), hs: 1 });
    NET.pingTimer = setInterval(() => netSend({ t: 'ping', ts: performance.now() }), 1000);
  });
  conn.on('data', onNet);
  conn.on('close', () => { if (MODE === 'online' || NET.conn === conn) quitToMenu('Холболт тасарлаа.'); });
  conn.on('error', () => quitToMenu('Холболтын алдаа.'));
}
function onNet(d) {
  switch (d.t) {
    case 'hello': NET.remoteChar = d.c; maybeStart(); break;
    case 'ping': netSend({ t: 'pong', ts: d.ts, hs: d.hs }); break;
    case 'pong': NET.rtt = performance.now() - d.ts; if (d.hs) maybeStart(); break;
    case 'start': startOnline(d); break;
    case 'in': {
      const n = d.i.length;
      for (let k = 0; k < n; k++) { const fr = d.f - n + 1 + k; if (fr >= 0) NET.remote[fr] = d.i[k]; }
      if (d.f > NET.remoteMax) NET.remoteMax = d.f;
      break;
    }
    case 'hash':
      NET.theirHash[d.f] = d.h;
      if (NET.myHash[d.f] !== undefined && NET.myHash[d.f] !== d.h) NET.desync = true;
      break;
  }
}
function maybeStart() {
  if (!NET.isHost || NET.started || NET.remoteChar == null || NET.rtt == null) return;
  const delay = Math.max(2, Math.min(8, Math.round(NET.rtt / 2 / 16.67) + 2));
  const msg = { t: 'start', c1: NET.myChar, c2: NET.remoteChar, delay };
  netSend(msg);
  startOnline(msg);
}
function startOnline(d) {
  NET.started = true; NET.delay = d.delay; NET.frame = 0; NET.local = []; NET.remote = []; NET.remoteMax = -1;
  NET.myHash = {}; NET.theirHash = {}; NET.desync = false;
  for (let i = 0; i < d.delay; i++) { NET.local[i] = 0; NET.remote[i] = 0; }
  MODE = 'online';
  S = newMatch(d.c1, d.c2);
  resetFx(); hideAll();
}
function netTick() {
  for (let k = 0; k < 2; k++) {
    const tf = NET.frame + NET.delay;
    if (NET.local[tf] === undefined) {
      NET.local[tf] = readKeys(0);
      netSend({ t: 'in', f: tf, i: [NET.local[tf - 2] || 0, NET.local[tf - 1] || 0, NET.local[tf]] });
    }
    if (NET.remote[NET.frame] === undefined) { NET.stall++; return; }
    const mine = NET.local[NET.frame], theirs = NET.remote[NET.frame];
    if (NET.isHost) step(S, mine, theirs); else step(S, theirs, mine);
    delete NET.local[NET.frame - 10]; delete NET.remote[NET.frame - 10];
    NET.frame++; NET.stall = 0;
    if (S.frame % 120 === 0) {
      const h = hashState(S);
      NET.myHash[S.frame] = h;
      netSend({ t: 'hash', f: S.frame, h });
      if (NET.theirHash[S.frame] !== undefined && NET.theirHash[S.frame] !== h) NET.desync = true;
    }
    if (NET.remoteMax - NET.frame <= NET.delay + 1) return; // only catch up when clearly behind
  }
}

// ---------------------------------------------------------------- menus
let MODE = null, S = null;
const DEMO = newMatch(2, 1);
DEMO.p.forEach(f => { f.st = 'idle'; });
DEMO.p[0].x = 380; DEMO.p[1].x = 580;
let PICK = { mode: null, c: [] };

function hideAll() { document.querySelectorAll('.ov').forEach(e => e.classList.add('hidden')); }
function show(id) { document.querySelectorAll('.ov').forEach(e => e.classList.toggle('hidden', e.id !== id)); }
function quitToMenu(msg) {
  if (MODE === 'online' || NET.peer) netClose();
  MODE = null; S = null; resetFx();
  $('msg').textContent = msg || '';
  show('menu');
}

const cards = $('cards'), previews = [];
CHARS.forEach((c, i) => {
  const d = document.createElement('div');
  d.className = 'card';
  d.innerHTML = `<canvas width="184" height="200"></canvas><div class="nm">${c.name}</div><div class="tt">${c.title}<br>Хурд ${'★'.repeat(Math.round((c.spd - 1) * 6 + 3))} · Хүч ${'★'.repeat(Math.round((c.pow - 1) * 6 + 3))}</div>`;
  d.onclick = () => pickChar(i);
  cards.appendChild(d);
  previews.push(d.querySelector('canvas').getContext('2d'));
});
function selTitle() {
  $('selTitle').textContent = PICK.mode === 'local' ? (PICK.c.length === 0 ? '1-р тоглогч бөхөө сонго' : '2-р тоглогч бөхөө сонго') : 'Бөхөө сонго';
}
function openSelect(mode) { PICK = { mode, c: [] }; selTitle(); show('select'); ac(); }
function pickChar(i) {
  PICK.c.push(i);
  if (PICK.mode === 'local' && PICK.c.length < 2) { selTitle(); return; }
  if (PICK.mode === 'online') { NET.myChar = i; netStatus('Өрөө үүсгэ, эсвэл найзынхаа кодоор нэгд.'); show('online'); return; }
  let c2 = PICK.c[1];
  if (PICK.mode === 'cpu') { c2 = (i + 1 + ((Math.random() * (CHARS.length - 1)) | 0)) % CHARS.length; }
  MODE = PICK.mode; S = newMatch(PICK.c[0], c2); AI.q = []; resetFx(); hideAll();
}
document.querySelectorAll('#menu button[data-mode]').forEach(b => { b.onclick = () => openSelect(b.dataset.mode); });
$('selBack').onclick = () => show('menu');
$('netBack').onclick = () => { netClose(); show('menu'); };
$('btnHost').onclick = hostRoom;
$('btnJoin').onclick = () => joinRoom($('joinCode').value);
$('joinCode').addEventListener('keydown', e => { if (e.key === 'Enter') joinRoom($('joinCode').value); });

function drawPreviews(fr) {
  if ($('select').classList.contains('hidden')) return;
  previews.forEach((g, i) => {
    g.clearRect(0, 0, 184, 200);
    const b = Math.sin(fr * 0.1 + i);
    const P = mod(PS.idle, { hy: 74 + b * 1.4, aF: [62 + b * 5, 105 + b * 5], aB: [48 - b * 4, 92 - b * 3] });
    g.save(); g.translate(92, 186); g.scale(CHARS[i].size * 1.05, CHARS[i].size * 1.05);
    drawBody(g, P, CHARS[i].col, false);
    g.restore();
  });
}

// ---------------------------------------------------------------- main loop
function tick() {
  if (MODE === 'online' && NET.started) netTick();
  else if (S && MODE) step(S, readKeys(0), MODE === 'cpu' ? aiInput(S) : readKeys(1));
  else DEMO.frame++;
  if (S) drainEvents(S);
  fxUpdate();
}
let last = performance.now(), acc = 0, rf = 0;
function loop(now) {
  acc += Math.min(100, now - last); last = now;
  while (acc >= 1000 / 60) { tick(); acc -= 1000 / 60; }
  render();
  drawPreviews(++rf);
  requestAnimationFrame(loop);
}
requestAnimationFrame(loop);
})();
