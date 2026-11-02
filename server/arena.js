'use strict';
/* The online arena over the room WebSocket: friends (`fr-*`), ranked matchmaking, challenges between friends and sparring
   with the CPU (`ar-*`). Bouts run here, server-side, 30 ticks a second (arena-sim.js): each client sends only what it
   holds and presses, and every tick both get the same snapshot, so nobody can move faster or hit harder than the rules
   allow. Results go to the arena store (rating, wins, losses, the record of the bout) and pay XP and coins into the
   wallet. A player needs an account (accounts.js) to use any of it. Matchmaking, challenges and bouts live on one server
   instance; friends, ratings and records are shared through the store. See docs/PROTOCOL.md, "Online arena". */

const S = require('./arena-sim.js');
const { createBot } = require('./arena-bot.js');
const { rankBy } = require('./arena-store.js');
const { nameKey } = require('./accounts.js');

const TICK_MS = 1000 / S.TICK_HZ;
const INVITE_MS = 30_000, AWAY_MS = 10_000, BOARD_GAP_MS = 2000, QUEUE_SCAN_MS = 500;
const STAGES = ['naadam', 'steppe', 'winter'];
const TIERS = [['avarga', 1650], ['arslan', 1500], ['garid', 1350], ['zaan', 1250], ['khartsaga', 1150], ['nachin', 1050], ['novice', -Infinity]];
const tierOf = (rating) => TIERS.find(([, min]) => rating >= min)[0];
/* What a bout pays: XP (and coins for a win) into the wallet, under the daily earn cap. */
const PAY = {
  ranked: { win: { coins: 20, xp: 40 }, loss: { coins: 0, xp: 15 }, draw: { coins: 5, xp: 20 }, k: 32 },
  friend: { win: { coins: 10, xp: 25 }, loss: { coins: 0, xp: 10 }, draw: { coins: 5, xp: 15 }, k: 24 },
  cpu: { win: { coins: 5, xp: 10 }, loss: { coins: 0, xp: 5 }, draw: { coins: 0, xp: 5 }, k: 0 },
};

module.exports = function createArena(store, { level = () => 1, reward = async () => {}, now = () => Date.now(), tickMs = TICK_MS } = {}) {
  const online = new Map();   // player id → conn (the newest connection with an account)
  const queue = [];           // { id, rating, at }
  const invites = new Map();  // "from>to" → expires at
  const matches = new Map();  // match id → bout
  const inMatch = new Map();  // player id → match id
  let loop = null, scan = null, seq = 0;

  const accountOf = (conn) => conn && conn.arena && conn.arena.account;
  const send = (id, msg) => { const c = online.get(id); if (c) c.send(msg); };
  const publicOf = async (account, stats) => ({ u: account.username, look: account.look || null, rating: stats.rating, tier: tierOf(stats.rating), w: stats.wins, l: stats.losses });

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
    for (const [key, until] of invites) if (until > now() && key.endsWith('>' + id)) {
      const from = key.slice(0, key.indexOf('>')), fa = await store.accountById(from);
      if (fa) conn.send({ t: 'ar-invite', u: fa.username, ...(await publicOf(fa, await store.stats(from))) });
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
    else if (act === 'remove') { ok = await store.friendRemove(me.id, other.id); reason = ok ? 'removed' : 'none'; invites.delete(`${me.id}>${other.id}`); invites.delete(`${other.id}>${me.id}`); }
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
  async function begin(mode, ids, botLevel = 0) {
    const id = 'a' + now().toString(36) + (++seq).toString(36);
    const info = [];
    for (const pid of ids) {
      if (!pid) { info.push({ u: 'CPU', cpu: botLevel, rating: 800 + 250 * botLevel, tier: tierOf(800 + 250 * botLevel), look: null }); continue; }
      const acc = await store.accountById(pid), st = await store.stats(pid);
      info.push({ u: acc ? acc.username : '?', look: acc && acc.look || null, rating: st.rating, tier: tierOf(st.rating), w: st.wins, l: st.losses });
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
      case 'ar-in': {
        if (!match) return;
        const side = match.ids.indexOf(id);
        if (Number.isInteger(m.m)) match.held[side] = m.m & S.INPUT_MASK;
        if (Number.isInteger(m.p)) match.pressed[side] |= m.p & S.INPUT_MASK;
        return;
      }
      case 'ar-quit': if (match) finish(match, 1 - match.ids.indexOf(id), 'quit'); return;
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
        queue.push({ id, rating: (await store.stats(id)).rating, at: now() });
        if (!scan) scan = setInterval(scanQueue, QUEUE_SCAN_MS);
        conn.send({ t: 'ar-queued', n: queue.length });
        return;
      }
      case 'ar-cancel': dropFromQueue(id); return conn.send({ t: 'ar-cancelled' });
      case 'ar-cpu': {
        const lv = [1, 2, 3].includes(m.lv) ? m.lv : 2;
        dropFromQueue(id);
        await begin('cpu', [id, null], lv);
        return;
      }
      case 'ar-challenge': {
        const other = await byName(m.u);
        if (!other || other.id === id) return conn.send({ t: 'ar-err', reason: 'no-account', of: m.t });
        if (!(await store.areFriends(id, other.id))) return conn.send({ t: 'ar-err', reason: 'not-friends', of: m.t, u: other.username });
        if (!online.has(other.id)) return conn.send({ t: 'ar-err', reason: 'offline', of: m.t, u: other.username });
        if (inMatch.has(other.id)) return conn.send({ t: 'ar-err', reason: 'busy', of: m.t, u: other.username });
        invites.set(`${id}>${other.id}`, now() + INVITE_MS);
        if (invites.size > 2000) for (const [k, until] of invites) if (until <= now()) invites.delete(k);
        send(other.id, { t: 'ar-invite', ...(await publicOf(me, await store.stats(id))) });
        return conn.send({ t: 'ar-sent', u: other.username });
      }
      case 'ar-accept': case 'ar-decline': {
        const other = await byName(m.u);
        const key = other && `${other.id}>${id}`, until = key && invites.get(key);
        if (!other || !until || until <= now()) { if (key) invites.delete(key); return conn.send({ t: 'ar-err', reason: 'no-invite', of: m.t }); }
        invites.delete(key);
        if (m.t === 'ar-decline') return send(other.id, { t: 'ar-declined', u: me.username });
        if (!online.has(other.id) || inMatch.has(other.id)) return conn.send({ t: 'ar-err', reason: 'busy', of: m.t, u: other.username });
        await begin('friend', [other.id, id]);
        return;
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
    if (match) { const side = match.ids.indexOf(account.id); match.away[side] = now(); match.held[side] = 0; }
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
      for (const key of [...invites.keys()]) if (key.startsWith(id + '>') || key.endsWith('>' + id)) invites.delete(key);
    }
  }
  function close() { if (loop) clearTimeout(loop); if (scan) clearInterval(scan); loop = scan = null; }

  return { handle, leave, changed, gone, close, tierOf, stats: () => ({ matches: matches.size, queue: queue.length, online: online.size }) };
};
module.exports.tierOf = tierOf;
module.exports.TIERS = TIERS;
