'use strict';
/* The online arena over the room WebSocket: friends (`fr-*`), ranked matchmaking, sparring with the CPU and bouts with a
   friend in a room with a 5-letter code (`ar-*`). Every bout is Naadam wrestling, as in the village (GameWrestle.cs): each
   round both wrestlers pick a move and do its skill game on the device, which sends the move and its power (`ar-mv`); when
   both are in, the server weighs them with the game's rules (rules.js `exchange`) and counts the fall. First to two falls
   wins. Results go to the arena store (rating, wins, losses, the record of the bout) and pay XP and coins into the wallet.
   A player needs an account (accounts.js) to use any of it. Matchmaking, rooms and bouts live on one server instance;
   friends, ratings and records are shared through the store. See docs/PROTOCOL.md, "Online arena". */

const crypto = require('node:crypto');
const R = require('../web/js/village/rules.js');
const { rankBy } = require('./arena-store.js');
const { nameKey } = require('./accounts.js');

const AWAY_MS = 10_000, BOARD_GAP_MS = 2000, QUEUE_SCAN_MS = 500;
const ROOM_LETTERS = 'ABCDEFGHJKMNPQRSTUVWXYZ', ROOM_CODE = /^[A-Z]{5}$/; // no I, L or O: they are easy to misread aloud
const ROOM_MS = 10 * 60_000, AGAIN_MS = 60_000, WRONG_MAX = 10, WRONG_LOCK_MS = 5 * 60_000;
/* A bout: first to NEED falls, at most MAX_ROUNDS rounds (then the falls decide, or it is a draw). Each round has ROUND_MS
   to choose (the first a little more, for the eagle dance). Who lets the time run out falls that round (shown as a random,
   weak move), and who lets IDLE_MAX rounds in a row run out loses the bout. */
const NEED = 2, MAX_ROUNDS = 9, ROUND_MS = 25_000, FIRST_EXTRA_MS = 10_000, IDLE_MAX = 2, LATE_POWER = 0.15;
const MOVE_IDS = R.MOVES.map((m) => m.id);
/* The CPU by level: how strong its moves are and how often it reads the move you use most. */
const CPU = { 1: { power: 0.35, read: 0.05 }, 2: { power: 0.55, read: 0.2 }, 3: { power: 0.72, read: 0.4 } };
const TIERS = [['avarga', 1650], ['arslan', 1500], ['garid', 1350], ['zaan', 1250], ['khartsaga', 1150], ['nachin', 1050], ['novice', -Infinity]];
const tierOf = (rating) => TIERS.find(([, min]) => rating >= min)[0];
/* What a bout pays: XP (and coins for a win) into the wallet, under the daily earn cap. */
const PAY = {
  ranked: { win: { coins: 20, xp: 40 }, loss: { coins: 0, xp: 15 }, draw: { coins: 5, xp: 20 }, k: 32 },
  friend: { win: { coins: 5, xp: 10 }, loss: { coins: 0, xp: 5 }, draw: { coins: 0, xp: 5 }, k: 0 },
  cpu: { win: { coins: 5, xp: 10 }, loss: { coins: 0, xp: 5 }, draw: { coins: 0, xp: 5 }, k: 0 },
};

module.exports = function createArena(store, { level = () => 1, reward = async () => {}, now = () => Date.now(), roundMs = ROUND_MS, cpuMs = [1200, 4000] } = {}) {
  const online = new Map();   // player id → conn (the newest connection with an account)
  const queue = [];           // { id, rating, at }
  const matches = new Map();  // match id → bout
  const inMatch = new Map();  // player id → match id
  const rooms = new Map();    // code → { code, ids, again, timer }
  const roomOf = new Map();   // player id → room
  const wrong = new Map();    // player id → { n, until }: wrong room codes
  let scan = null, seq = 0;

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
      clearTimeout(match.away[side]); match.away[side] = null;
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
    return { t: 'ar-start', id: match.id, side, mode: match.mode, you, opp, need: NEED, rd: match.rounds.length + 1, ms: Math.max(0, match.deadline - now()),
      ...(resume ? { resume: true, w: match.wins.slice(), chose: match.moves[side] !== null } : {}) };
  }
  const newMatchId = () => 'a' + now().toString(36) + (++seq).toString(36);
  async function playerInfo(pid) {
    const acc = await store.accountById(pid), st = await store.stats(pid);
    return { u: acc ? acc.username : '?', look: acc && acc.look || null, rating: st.rating, tier: tierOf(st.rating), w: st.wins, l: st.losses };
  }
  async function begin(mode, ids, botLevel = 0, room = null) {
    const id = newMatchId();
    const info = [];
    for (const pid of ids) {
      if (!pid) { info.push({ u: 'CPU', cpu: botLevel, rating: 800 + 250 * botLevel, tier: tierOf(800 + 250 * botLevel), look: null }); continue; }
      info.push(await playerInfo(pid));
    }
    if (room && rooms.get(room.code) !== room) return null; // someone left while the names were being read
    if (ids.some((pid) => pid && inMatch.has(pid))) return null; // a double start: the first one counts
    const match = { id, mode, ids, info, room, wins: [0, 0], rounds: [], moves: [null, null], idle: [0, 0], used: {}, deadline: 0, timer: null,
      cpuTimer: null, away: [null, null], bot: botLevel ? CPU[botLevel] || CPU[2] : null, done: false };
    for (const pid of ids) if (pid) { inMatch.set(pid, id); dropFromQueue(pid); }
    matches.set(id, match);
    armRound(match, roundMs + FIRST_EXTRA_MS);
    ids.forEach((pid, side) => { if (pid) send(pid, startMessage(match, side)); });
    return match;
  }
  /** A new round: the clock for choosing, and the CPU thinking. */
  function armRound(match, ms) {
    clearTimeout(match.timer); clearTimeout(match.cpuTimer);
    match.deadline = now() + ms;
    match.timer = setTimeout(() => resolveRound(match, true), ms);
    if (match.timer.unref) match.timer.unref();
    if (match.bot) {
      const think = cpuMs[0] + Math.random() * (cpuMs[1] - cpuMs[0]);
      match.cpuTimer = setTimeout(() => choose(match, 1, cpuMove(match), cpuPower(match)), Math.max(0, Math.min(think, ms - 50)));
      if (match.cpuTimer.unref) match.cpuTimer.unref();
    }
  }
  /** The CPU picks a move: sometimes one that beats the move you use most, else any. */
  function cpuMove(match) {
    const fav = Object.entries(match.used).sort((x, y) => y[1] - x[1])[0];
    if (fav && Math.random() < match.bot.read) {
      const counters = R.MOVES.filter((m) => m.beats.includes(fav[0]));
      if (counters.length) return counters[Math.floor(Math.random() * counters.length)].id;
    }
    return MOVE_IDS[Math.floor(Math.random() * MOVE_IDS.length)];
  }
  const cpuPower = (match) => Math.max(0.05, Math.min(1, match.bot.power + (Math.random() - 0.5) * 0.3));
  const round2 = (x) => Math.round(x * 100) / 100;
  function choose(match, side, move, power) {
    if (match.done || match.moves[side] !== null) return;
    match.moves[side] = { m: move, p: power };
    if (match.moves[1 - side] === null) { const other = match.ids[1 - side]; if (other) send(other, { t: 'ar-wait', id: match.id }); return; }
    resolveRound(match, false);
  }
  /** Both moves are in, or the time ran out (a missing move is a random, weak one). */
  function resolveRound(match, timedOut) {
    if (match.done) return;
    if (!timedOut && (match.moves[0] === null || match.moves[1] === null)) return;
    for (let side = 0; side < 2; side++) {
      if (match.moves[side] !== null) { match.idle[side] = 0; continue; }
      match.idle[side]++;
      match.moves[side] = { m: MOVE_IDS[Math.floor(Math.random() * MOVE_IDS.length)], p: LATE_POWER, late: true };
    }
    // one who let the time run out falls that round; when both did, it is even
    const [a, b] = match.moves, r = a.late || b.late ? (a.late && b.late ? 0 : a.late ? -1 : 1) : R.exchange(a.m, a.p, b.m, b.p);
    if (r > 0) match.wins[0]++; else if (r < 0) match.wins[1]++;
    match.rounds.push({ w: r > 0 ? 0 : r < 0 ? 1 : -1, m: [a.m, b.m], p: [round2(a.p), round2(b.p)] });
    if (!a.late) match.used[a.m] = (match.used[a.m] || 0) + 1;   // what the CPU learns of the person (always side 0)
    match.moves = [null, null];
    const over = match.wins[0] >= NEED || match.wins[1] >= NEED || match.rounds.length >= MAX_ROUNDS;
    const idler = over ? -1 : match.idle.findIndex((n, side) => n >= IDLE_MAX && match.ids[side]);
    const more = !over && idler < 0;
    if (more) armRound(match, roundMs);
    const res = { t: 'ar-res', id: match.id, rd: match.rounds.length, m: [a.m, b.m], p: [round2(a.p), round2(b.p)], late: [!!a.late, !!b.late], r, w: match.wins.slice(),
      ...(more ? { ms: roundMs } : {}) };
    match.ids.forEach((pid) => { if (pid) send(pid, res); });
    if (over) finish(match, match.wins[0] === match.wins[1] ? -1 : match.wins[0] > match.wins[1] ? 0 : 1, 'done');
    else if (idler >= 0) finish(match, 1 - idler, 'left');
  }
  async function finish(match, winner, reason) {
    if (match.done) return;
    match.done = true; matches.delete(match.id);
    clearTimeout(match.timer); clearTimeout(match.cpuTimer); match.away.forEach((t) => clearTimeout(t));
    for (const pid of match.ids) if (pid && inMatch.get(pid) === match.id) inMatch.delete(pid);
    const room = match.room;
    if (room) { if (reason === 'done' && rooms.get(room.code) === room) { room.again.clear(); armRoom(room, AGAIN_MS); } else closeRoom(room); }
    const pay = PAY[match.mode], [a, b] = match.ids;
    let result = null;
    try {
      result = await store.recordMatch({ id: match.id, mode: match.mode, a, b, winner, rounds: match.rounds, k: pay.k });
    } catch (error) { console.error('Could not record arena bout:', error.message); }
    for (let side = 0; side < 2; side++) {
      const pid = match.ids[side]; if (!pid) continue;
      const outcome = winner === -1 ? 'draw' : winner === side ? 'win' : 'loss', money = pay[outcome];
      const msg = { t: 'ar-end', id: match.id, side, winner, reason, wins: match.wins.slice(), coins: money.coins, xp: money.xp };
      if (result && result.before[side] && match.mode === 'ranked') msg.rating = { before: result.before[side].rating, after: result.after[side].rating };
      send(pid, msg);
      reward(pid, `arena:${match.id}`, money.coins, money.xp).catch((error) => console.error('Could not pay arena bout:', error.message));
    }
  }

  // ---------------------------------------------------------------- rooms with a code: bouts with a friend
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
  function startRoomBout(room) {
    clearTimeout(room.timer); room.again.clear();
    return begin('friend', [...room.ids], 0, room);
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
    const id = me.id, mid = inMatch.get(id), match = mid && matches.get(mid);
    switch (m.t) {
      case 'ar-mv': {
        if (!match || !MOVE_IDS.includes(m.m)) return;
        const power = Number(m.p);
        choose(match, match.ids.indexOf(id), m.m, Math.max(0, Math.min(1, Number.isFinite(power) ? power : 0)));
        return;
      }
      case 'ar-quit':
        if (match) finish(match, 1 - match.ids.indexOf(id), 'quit');
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
    if (match) return conn.send({ t: 'ar-err', reason: 'in-match', of: m.t });
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
        return startRoomBout(room);
      }
      case 'ar-again': {
        const room = roomOf.get(id);
        if (!room || room.ids.length !== 2) return conn.send({ t: 'ar-room-closed' });
        room.again.add(id);
        if (room.again.size === 2) return startRoomBout(room);
        return send(room.ids.find((pid) => pid !== id), { t: 'ar-again', u: me.username });
      }
    }
  }

  /** The connection closed: out of the queue; a bout goes on for AWAY_MS in case the player comes back. */
  function leave(conn) {
    const account = accountOf(conn);
    if (!account || online.get(account.id) !== conn) return;
    online.delete(account.id);
    dropFromQueue(account.id);
    const mid = inMatch.get(account.id), match = mid && matches.get(mid);
    if (match) {
      const side = match.ids.indexOf(account.id);
      clearTimeout(match.away[side]);
      match.away[side] = setTimeout(() => finish(match, 1 - side, 'left'), AWAY_MS);
      if (match.away[side].unref) match.away[side].unref();
    } else leaveRoomOf(account.id);
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
      else leaveRoomOf(id);
    }
  }
  function close() {
    if (scan) clearInterval(scan); scan = null;
    for (const match of matches.values()) { clearTimeout(match.timer); clearTimeout(match.cpuTimer); match.away.forEach((t) => clearTimeout(t)); }
    for (const room of rooms.values()) clearTimeout(room.timer);
  }

  return { handle, leave, changed, gone, close, tierOf, stats: () => ({ matches: matches.size, queue: queue.length, online: online.size }) };
};
module.exports.tierOf = tierOf;
module.exports.TIERS = TIERS;
