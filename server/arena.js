'use strict';
/* The online arena over the room WebSocket: friends (`fr-*`), ranked matchmaking, sparring with the CPU and 5-second duels
   in a room with a 5-letter code (`ar-*`). Bouts run here, server-side, 30 ticks a second (arena-sim.js): each client sends
   only what it holds and presses, and every tick both get the same snapshot, so nobody can move faster or hit harder than
   the rules allow. Results go to the arena store (rating, wins, losses, the record of the bout) and pay XP and coins into
   the wallet. A duel's skill game runs on the device, which reports how well it went, so duels never change a rating. A
   player needs an account (accounts.js) to use any of it. Matchmaking, rooms and bouts live on one server instance;
   friends, ratings and records are shared through the store. See docs/PROTOCOL.md, "Online arena". */

const crypto = require('node:crypto');
const S = require('./arena-sim.js');
const { createBot } = require('./arena-bot.js');
const { rankBy } = require('./arena-store.js');
const { nameKey } = require('./accounts.js');

const TICK_MS = 1000 / S.TICK_HZ;
const AWAY_MS = 10_000, BOARD_GAP_MS = 2000, QUEUE_SCAN_MS = 500;
const ROOM_LETTERS = 'ABCDEFGHJKMNPQRSTUVWXYZ', ROOM_CODE = /^[A-Z]{5}$/; // no I, L or O: they are easy to misread aloud
const ROOM_MS = 10 * 60_000, AGAIN_MS = 60_000, WRONG_MAX = 10, WRONG_LOCK_MS = 5 * 60_000;
const DUEL_SKILLS = ['timing', 'hold', 'combo', 'mash', 'balance'], DUEL_ARROWS = 5;
const DUEL_LEAD_MS = 3000, DUEL_MS = 5000, DUEL_GRACE_MS = 1500;
const STAGES = ['naadam', 'steppe', 'winter'];
const TIERS = [['avarga', 1650], ['arslan', 1500], ['garid', 1350], ['zaan', 1250], ['khartsaga', 1150], ['nachin', 1050], ['novice', -Infinity]];
const tierOf = (rating) => TIERS.find(([, min]) => rating >= min)[0];
/* What a bout pays: XP (and coins for a win) into the wallet, under the daily earn cap. */
const PAY = {
  ranked: { win: { coins: 20, xp: 40 }, loss: { coins: 0, xp: 15 }, draw: { coins: 5, xp: 20 }, k: 32 },
  friend: { win: { coins: 5, xp: 10 }, loss: { coins: 0, xp: 5 }, draw: { coins: 0, xp: 5 }, k: 0 },
  cpu: { win: { coins: 5, xp: 10 }, loss: { coins: 0, xp: 5 }, draw: { coins: 0, xp: 5 }, k: 0 },
};

module.exports = function createArena(store, { level = () => 1, reward = async () => {}, now = () => Date.now(), tickMs = TICK_MS } = {}) {
  const online = new Map();   // player id → conn (the newest connection with an account)
  const queue = [];           // { id, rating, at }
  const matches = new Map();  // match id → bout
  const duels = new Map();    // match id → 5-second duel
  const inMatch = new Map();  // player id → match id (a bout or a duel)
  const rooms = new Map();    // code → { code, ids, again, timer }
  const roomOf = new Map();   // player id → room
  const wrong = new Map();    // player id → { n, until }: wrong room codes
  let loop = null, scan = null, seq = 0;

  const accountOf = (conn) => conn && conn.arena && conn.arena.account;
  const send = (id, msg) => { const c = online.get(id); if (c) c.send(msg); };

  async function hello(conn) {
    const id = conn.player.id, account = await store.accountById(id);
    conn.arena = { account, boardAt: 0 };
    if (!account) { if (online.get(id) === conn) online.delete(id); return conn.send({ t: 'ar-me', u: null }); }
    if (conn.player.look) { account.look = conn.player.look; await store.updateAccount(id, { look: conn.player.look }); }
    online.set(id, conn);
    const stats = await store.stats(id), xp = await store.xpOf(id);
    conn.send({ t: 'ar-me', u: account.username, rating: stats.rating, tier: tierOf(stats.rating), w: stats.wins, l: stats.losses, d: stats.draws,
      streak: stats.streak, best: stats.best, n: stats.matches, xp, lvl: level(xp) });
    const mid = inMatch.get(id), match = mid && matches.get(mid);
    if (match) { // back after a dropped connection: the bout goes on
      const side = match.ids.indexOf(id);
      match.away[side] = 0;
      conn.send(startMessage(match, side, true));
    }
  }

  // ---------------------------------------------------------------- friends
  async function friendList(conn) {
    const me = accountOf(conn).id, rows = await store.friendsOf(me);
    const h2h = await store.headToHead(me, rows.filter((r) => r.status === 'friends').map((r) => r.id));
    const shown = (r) => ({ u: r.username, look: r.look, rating: r.rating, tier: tierOf(r.rating), w: r.wins, l: r.losses, lvl: level(r.xp), xp: r.xp,
      on: online.has(r.id), busy: inMatch.has(r.id), ...(h2h[r.id] ? { hw: h2h[r.id].w, hl: h2h[r.id].l } : {}) });
    conn.send({ t: 'fr-list', friends: rows.filter((r) => r.status === 'friends').map(shown),
      inc: rows.filter((r) => r.status === 'pending' && r.incoming).map(shown), out: rows.filter((r) => r.status === 'pending' && !r.incoming).map(shown) });
  }
  async function byName(u) {
    if (typeof u !== 'string' || u.length > 32) return null;
    return store.accountByKey(nameKey(u.trim()));
  }
  async function friendAct(conn, m) {
    const me = accountOf(conn), other = await byName(m.u), act = m.t.slice(3);
    if (!other) return conn.send({ t: 'fr-res', act, ok: false, reason: 'no-account', u: String(m.u || '').slice(0, 16) });
    if (other.id === me.id) return conn.send({ t: 'fr-res', act, ok: false, reason: 'self', u: other.username });
    let ok = false, reason = '';
    if (act === 'add') { const r = await store.friendRequest(me.id, other.id); ok = r === 'sent' || r === 'accepted'; reason = r; }
    else if (act === 'accept') { ok = await store.friendAccept(me.id, other.id); reason = ok ? 'accepted' : 'no-request'; }
    else if (act === 'remove') { ok = await store.friendRemove(me.id, other.id); reason = ok ? 'removed' : 'none'; }
    conn.send({ t: 'fr-res', act, ok, reason, u: other.username });
    if (ok) send(other.id, { t: 'fr-ping', act: reason, u: me.username });
    if (ok) await friendList(conn);
  }

  // ---------------------------------------------------------------- boards
  async function boardRows(conn, k) {
    const me = accountOf(conn).id;
    let rows;
    if (k === 'friends') {
      const friends = (await store.friendsOf(me)).filter((r) => r.status === 'friends');
      const mine = { id: me, username: accountOf(conn).username, look: accountOf(conn).look, xp: await store.xpOf(me), ...(await store.stats(me)) };
      rows = rankBy([...friends, mine]);
    } else rows = await store.globalBoard(me, 50);
    return rows.map((r) => ({ rank: r.rank, u: r.username, look: r.look, rating: r.rating, tier: tierOf(r.rating), w: r.wins, l: r.losses, d: r.draws,
      streak: r.streak, lvl: level(r.xp), xp: r.xp, on: online.has(r.id), ...(r.id === me ? { me: true } : {}) }));
  }

  // ---------------------------------------------------------------- bouts
  function startMessage(match, side, resume = false) {
    const opp = match.info[1 - side], you = match.info[side];
    return { t: 'ar-start', id: match.id, side, mode: match.mode, stage: match.stage, you, opp, ...(resume ? { resume: true } : {}) };
  }
  const newMatchId = () => 'a' + now().toString(36) + (++seq).toString(36);
  async function playerInfo(pid) {
    const acc = await store.accountById(pid), st = await store.stats(pid);
    return { u: acc ? acc.username : '?', look: acc && acc.look || null, rating: st.rating, tier: tierOf(st.rating), w: st.wins, l: st.losses };
  }
  async function begin(mode, ids, botLevel = 0) {
    const id = newMatchId();
    const info = [];
    for (const pid of ids) {
      if (!pid) { info.push({ u: 'CPU', cpu: botLevel, rating: 800 + 250 * botLevel, tier: tierOf(800 + 250 * botLevel), look: null }); continue; }
      info.push(await playerInfo(pid));
    }
    const match = { id, mode, ids, info, stage: STAGES[(seq + ids.length) % STAGES.length], sim: S.createMatch(), held: [0, 0], pressed: [0, 0],
      away: [0, 0], bot: botLevel ? createBot(1, botLevel, (now() ^ seq) >>> 0) : null, done: false };
    for (const pid of ids) if (pid) { inMatch.set(pid, id); dropFromQueue(pid); }
    matches.set(id, match);
    ids.forEach((pid, side) => { if (pid) send(pid, startMessage(match, side)); });
    ensureLoop();
    return match;
  }
  function tickMatch(match) {
    const m = match.sim;
    for (let side = 0; side < 2; side++) {
      if (match.ids[side]) { S.input(m, side, match.held[side], match.pressed[side]); match.pressed[side] = 0; }
      else if (match.bot) { const b = match.bot.think(m); S.input(m, side, b.held, b.pressed); }
    }
    S.step(m);
    const snap = { t: 'ar-st', id: match.id, ...S.snapshot(m) };
    for (const pid of match.ids) if (pid) send(pid, snap);
    if (m.phase === 'over') finish(match, m.result.winner, 'done');
    else for (let side = 0; side < 2; side++) if (match.away[side] && now() - match.away[side] > AWAY_MS) { finish(match, 1 - side, 'left'); break; }
  }
  async function finish(match, winner, reason) {
    if (match.done) return;
    match.done = true; matches.delete(match.id);
    for (const pid of match.ids) if (pid && inMatch.get(pid) === match.id) inMatch.delete(pid);
    const pay = PAY[match.mode], [a, b] = match.ids;
    let result = null;
    try {
      result = await store.recordMatch({ id: match.id, mode: match.mode, a, b, winner, rounds: match.sim.rounds, k: pay.k });
    } catch (error) { console.error('Could not record arena bout:', error.message); }
    for (let side = 0; side < 2; side++) {
      const pid = match.ids[side]; if (!pid) continue;
      const outcome = winner === -1 ? 'draw' : winner === side ? 'win' : 'loss', money = pay[outcome];
      const msg = { t: 'ar-end', id: match.id, side, winner, reason, wins: match.sim.wins, coins: money.coins, xp: money.xp };
      if (result && result.before[side]) msg.rating = { before: result.before[side].rating, after: result.after[side].rating };
      send(pid, msg);
      reward(pid, `arena:${match.id}`, money.coins, money.xp).catch((error) => console.error('Could not pay arena bout:', error.message));
    }
    if (!matches.size) stopLoop();
  }
  function ensureLoop() {
    if (loop) return;
    let next = now() + tickMs;
    const run = () => {
      let n = 0;
      while (now() >= next && n++ < 5) { for (const match of [...matches.values()]) if (!match.done) tickMatch(match); next += tickMs; }
      if (now() >= next) next = now() + tickMs; // too far behind: skip ahead rather than run in bursts
      if (matches.size) loop = setTimeout(run, Math.max(1, next - now())); else loop = null;
    };
    loop = setTimeout(run, tickMs);
  }
  function stopLoop() { if (loop && !matches.size) { clearTimeout(loop); loop = null; } }

  // ---------------------------------------------------------------- rooms with a code, and the 5-second duel
  function newRoomCode() {
    for (let i = 0; i < 50; i++) {
      const code = Array.from({ length: 5 }, () => ROOM_LETTERS[crypto.randomInt(ROOM_LETTERS.length)]).join('');
      if (!rooms.has(code)) return code;
    }
    return null;
  }
  function closeRoom(room) {
    if (rooms.get(room.code) !== room) return;
    clearTimeout(room.timer); rooms.delete(room.code);
    for (const pid of room.ids) if (roomOf.get(pid) === room) { roomOf.delete(pid); send(pid, { t: 'ar-room-closed' }); }
  }
  function leaveRoomOf(id) { const room = roomOf.get(id); if (room) closeRoom(room); }
  function armRoom(room, ms) {
    clearTimeout(room.timer);
    room.timer = setTimeout(() => closeRoom(room), ms);
    room.timer.unref();
  }
  /** Seconds this player must still wait before trying a room code; 0 when they may. */
  function codeLockedFor(id) {
    const w = wrong.get(id), t = now();
    if (!w || !w.until) return 0;
    if (w.until > t) return Math.ceil((w.until - t) / 1000);
    wrong.delete(id);
    return 0;
  }
  function wrongCode(id) {
    const w = wrong.get(id) || { n: 0, until: 0 };
    if (++w.n >= WRONG_MAX) w.until = now() + WRONG_LOCK_MS;
    wrong.delete(id); wrong.set(id, w); // the oldest stay first, for the cap below
    if (wrong.size > 5000) wrong.delete(wrong.keys().next().value);
  }
  async function startDuel(room) {
    clearTimeout(room.timer); room.again.clear();
    const id = newMatchId(), ids = [...room.ids], info = [await playerInfo(ids[0]), await playerInfo(ids[1])];
    if (rooms.get(room.code) !== room) return; // someone left while the names were being read
    const skill = DUEL_SKILLS[crypto.randomInt(DUEL_SKILLS.length)], seed = crypto.randomInt(1_000_000);
    const arrows = Array.from({ length: DUEL_ARROWS }, () => crypto.randomInt(4));
    const duel = { id, room, ids, goAt: now() + DUEL_LEAD_MS, got: [null, null], done: false, timer: null };
    duels.set(id, duel);
    duel.timer = setTimeout(() => finishDuel(duel, 'done').catch((e) => console.error('Could not finish duel:', e.message)), DUEL_LEAD_MS + DUEL_MS + DUEL_GRACE_MS);
    duel.timer.unref();
    ids.forEach((pid, side) => {
      inMatch.set(pid, id); dropFromQueue(pid);
      send(pid, { t: 'ar-duel', id, side, you: info[side], opp: info[1 - side], skill, arrows, seed, lead: DUEL_LEAD_MS, ms: DUEL_MS });
    });
  }
  /** How well it went (0–1, from the device) counts most; the server's own clock adds up to 15% for being quick. */
  function duelScore(duel, q) {
    const elapsed = Math.min(DUEL_MS, Math.max(0, now() - duel.goAt));
    return Math.max(0, Math.min(1, q)) * (0.85 + 0.15 * (1 - elapsed / DUEL_MS));
  }
  /** `loser` is the side that gave up or left; the room closes then. Otherwise whoever scored more wins. */
  async function finishDuel(duel, reason, loser = -1) {
    if (duel.done) return;
    duel.done = true; clearTimeout(duel.timer); duels.delete(duel.id);
    for (const pid of duel.ids) if (inMatch.get(pid) === duel.id) inMatch.delete(pid);
    const scores = duel.got.map((s) => s || 0);
    const winner = loser >= 0 ? 1 - loser : Math.abs(scores[0] - scores[1]) < 0.005 ? -1 : scores[0] > scores[1] ? 0 : 1;
    const pay = PAY.friend, [a, b] = duel.ids;
    try {
      await store.recordMatch({ id: duel.id, mode: 'friend', a, b, winner, rounds: [], k: pay.k });
    } catch (error) { console.error('Could not record arena duel:', error.message); }
    duel.ids.forEach((pid, side) => {
      const money = pay[winner === -1 ? 'draw' : winner === side ? 'win' : 'loss'];
      send(pid, { t: 'ar-duel-end', id: duel.id, side, winner, reason, scores: scores.map((s) => Math.round(s * 100)), coins: money.coins, xp: money.xp });
      reward(pid, `arena:${duel.id}`, money.coins, money.xp).catch((error) => console.error('Could not pay arena duel:', error.message));
    });
    if (loser >= 0) closeRoom(duel.room); else if (rooms.get(duel.room.code) === duel.room) armRoom(duel.room, AGAIN_MS);
  }

  // ---------------------------------------------------------------- matchmaking
  function dropFromQueue(id) { const i = queue.findIndex((q) => q.id === id); if (i >= 0) queue.splice(i, 1); }
  function scanQueue() {
    const t = now();
    for (const q of [...queue]) if (!online.has(q.id)) dropFromQueue(q.id);
    queue.sort((x, y) => x.rating - y.rating);
    for (let i = 0; i + 1 < queue.length; i++) {
      const x = queue[i], y = queue[i + 1];
      const reach = 100 + 25 * Math.floor(Math.max(t - x.at, t - y.at) / 1000); // the longer they wait, the wider the search
      if (y.rating - x.rating <= reach) { queue.splice(i, 2); i--; begin('ranked', [x.id, y.id]).catch((e) => console.error('Could not start arena bout:', e.message)); }
    }
    for (const q of queue) send(q.id, { t: 'ar-queued', n: queue.length });
    if (!queue.length && scan) { clearInterval(scan); scan = null; }
  }

  // ---------------------------------------------------------------- messages
  async function handle(conn, m) {
    if (m.t === 'ar-hello') return hello(conn);
    const me = accountOf(conn);
    if (!me) return conn.send({ t: 'ar-err', reason: 'no-account', of: m.t });
    const id = me.id, mid = inMatch.get(id), match = mid && matches.get(mid), duel = mid && duels.get(mid);
    switch (m.t) {
      case 'ar-duel-score': {
        if (!duel) return;
        const side = duel.ids.indexOf(id);
        if (duel.got[side] !== null || now() < duel.goAt || !Number.isFinite(m.q)) return;
        duel.got[side] = duelScore(duel, m.q);
        if (duel.got.every((s) => s !== null)) await finishDuel(duel, 'done');
        return;
      }
      case 'ar-in': {
        if (!match) return;
        const side = match.ids.indexOf(id);
        if (Number.isInteger(m.m)) match.held[side] = m.m & S.INPUT_MASK;
        if (Number.isInteger(m.p)) match.pressed[side] |= m.p & S.INPUT_MASK;
        return;
      }
      case 'ar-quit':
        if (match) finish(match, 1 - match.ids.indexOf(id), 'quit');
        else if (duel) await finishDuel(duel, 'quit', duel.ids.indexOf(id));
        return;
      case 'fr-list': return friendList(conn);
      case 'fr-add': case 'fr-accept': case 'fr-remove': return friendAct(conn, m);
      case 'ar-board': {
        const k = m.k === 'friends' ? 'friends' : 'global', t = now();
        if (t - conn.arena.boardAt < BOARD_GAP_MS) return;
        conn.arena.boardAt = t;
        return conn.send({ t: 'ar-board', k, rows: await boardRows(conn, k) });
      }
      case 'ar-history': {
        const list = await store.recentMatches(id, 10);
        return conn.send({ t: 'ar-history', list: list.map((x) => {
          const side = x.a === id ? 0 : 1;
          return { mode: x.mode, opp: side === 0 ? (x.bn || (x.b ? '?' : 'CPU')) : (x.an || '?'), res: x.winner === -1 ? 'draw' : x.winner === side ? 'win' : 'loss',
            delta: side === 0 ? x.da : x.db, at: x.at };
        }) });
      }
    }
    if (match || duel) return conn.send({ t: 'ar-err', reason: 'in-match', of: m.t });
    switch (m.t) {
      case 'ar-queue': {
        if (queue.some((q) => q.id === id)) return;
        leaveRoomOf(id);
        queue.push({ id, rating: (await store.stats(id)).rating, at: now() });
        if (!scan) scan = setInterval(scanQueue, QUEUE_SCAN_MS);
        conn.send({ t: 'ar-queued', n: queue.length });
        return;
      }
      case 'ar-cancel': dropFromQueue(id); return conn.send({ t: 'ar-cancelled' });
      case 'ar-cpu': {
        const lv = [1, 2, 3].includes(m.lv) ? m.lv : 2;
        dropFromQueue(id);
        leaveRoomOf(id);
        await begin('cpu', [id, null], lv);
        return;
      }
      case 'ar-room-open': {
        leaveRoomOf(id);
        dropFromQueue(id);
        const code = newRoomCode();
        if (!code) return conn.send({ t: 'ar-err', reason: 'storage', of: m.t });
        const room = { code, ids: [id], again: new Set(), timer: null };
        rooms.set(code, room); roomOf.set(id, room);
        armRoom(room, ROOM_MS);
        return conn.send({ t: 'ar-room', code });
      }
      case 'ar-room-close': {
        if (roomOf.has(id)) leaveRoomOf(id); else conn.send({ t: 'ar-room-closed' });
        return;
      }
      case 'ar-room-join': {
        const wait = codeLockedFor(id);
        if (wait) return conn.send({ t: 'ar-err', reason: 'locked', wait, of: m.t });
        const code = typeof m.code === 'string' ? m.code.trim().toUpperCase() : '';
        const room = ROOM_CODE.test(code) ? rooms.get(code) : null;
        if (room && room.ids[0] === id) return conn.send({ t: 'ar-err', reason: 'own-room', of: m.t });
        if (!room || room.ids.length !== 1) { wrongCode(id); return conn.send({ t: 'ar-err', reason: 'no-room', of: m.t }); }
        leaveRoomOf(id);
        dropFromQueue(id);
        room.ids.push(id); roomOf.set(id, room);
        return startDuel(room);
      }
      case 'ar-duel-again': {
        const room = roomOf.get(id);
        if (!room || room.ids.length !== 2) return conn.send({ t: 'ar-room-closed' });
        room.again.add(id);
        if (room.again.size === 2) return startDuel(room);
        return send(room.ids.find((pid) => pid !== id), { t: 'ar-duel-again', u: me.username });
      }
    }
  }

  /** The connection closed: out of the queue; a bout goes on for AWAY_MS in case the player comes back. */
  function leave(conn) {
    const account = accountOf(conn);
    if (!account || online.get(account.id) !== conn) return;
    online.delete(account.id);
    dropFromQueue(account.id);
    const mid = inMatch.get(account.id), match = mid && matches.get(mid), duel = mid && duels.get(mid);
    if (match) { const side = match.ids.indexOf(account.id); match.away[side] = now(); match.held[side] = 0; }
    if (duel) finishDuel(duel, 'left', duel.ids.indexOf(account.id)).catch((error) => console.error('Could not finish duel:', error.message));
    else leaveRoomOf(account.id);
  }
  /** An account was renamed or deleted (accounts.js), or players were erased with their family: reload or drop them. */
  async function changed(id) {
    const conn = online.get(id);
    if (conn) await hello(conn).catch((error) => console.error('Could not reload arena account:', error.message));
  }
  function gone(ids) {
    for (const id of ids) {
      const conn = online.get(id);
      if (conn) { conn.arena = { account: null, boardAt: 0 }; online.delete(id); conn.send({ t: 'ar-me', u: null }); }
      dropFromQueue(id);
      const mid = inMatch.get(id), match = mid && matches.get(mid);
      if (match) finish(match, 1 - match.ids.indexOf(id), 'left');
      const duel = mid && duels.get(mid);
      if (duel) finishDuel(duel, 'left', duel.ids.indexOf(id)).catch((error) => console.error('Could not finish duel:', error.message));
      else leaveRoomOf(id);
    }
  }
  function close() {
    if (loop) clearTimeout(loop); if (scan) clearInterval(scan); loop = scan = null;
    for (const duel of duels.values()) clearTimeout(duel.timer);
    for (const room of rooms.values()) clearTimeout(room.timer);
  }

  return { handle, leave, changed, gone, close, tierOf, stats: () => ({ matches: matches.size, queue: queue.length, online: online.size }) };
};
module.exports.tierOf = tierOf;
module.exports.TIERS = TIERS;
