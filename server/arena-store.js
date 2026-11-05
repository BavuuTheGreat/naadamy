'use strict';
/* Storage for the online arena: wrestler accounts (a unique username and a password for a player id), friends, arena
   stats (rating, wins, losses…) and the record of every bout. PostgreSQL when room-store has a pool (tables made at
   start), else JSON files in DATA_DIR like the rest of the server. Usernames are unique by their key (lower case, with
   Cyrillic letters that look like Latin ones read as Latin), enforced by a UNIQUE index, so two players can never hold
   the same name; renaming or deleting an account frees its old name at once. See docs/PROTOCOL.md, "Online arena". */

const fs = require('node:fs');
const path = require('node:path');

const PLAYER_ID = /^[\w-]{4,24}$/;
const RATING_START = 1000, RATING_MIN = 100;
const FRIENDS_MAX = 100, PENDING_MAX = 50;
const MATCHES_KEEP = 5000; // JSON: the newest bouts kept

const STAT_FIELDS = ['rating', 'wins', 'losses', 'draws', 'streak', 'best', 'matches'];
const freshStats = () => ({ rating: RATING_START, wins: 0, losses: 0, draws: 0, streak: 0, best: 0, matches: 0 });

/** Elo with factor k: the change for a player rated `mine` against `theirs` with score 1 (win), 0.5 (draw) or 0. */
function eloDelta(mine, theirs, score, k) {
  const expected = 1 / (1 + 10 ** ((theirs - mine) / 400));
  return Math.round(k * (score - expected));
}
/** Both players' stats after a bout: winner 0 (a), 1 (b) or -1 (a draw); k 0 leaves the ratings alone. */
function applyResult(sa, sb, winner, k) {
  const out = [{ ...sa }, { ...sb }];
  const score = winner === 0 ? 1 : winner === 1 ? 0 : 0.5;
  const da = k ? eloDelta(sa.rating, sb.rating, score, k) : 0, db = k ? eloDelta(sb.rating, sa.rating, 1 - score, k) : 0;
  out[0].rating = Math.max(RATING_MIN, sa.rating + da); out[1].rating = Math.max(RATING_MIN, sb.rating + db);
  for (const [i, s] of out.entries()) {
    s.matches++;
    if (winner === -1) { s.draws++; s.streak = 0; } else if (winner === i) { s.wins++; s.streak = Math.max(0, s.streak) + 1; s.best = Math.max(s.best, s.streak); } else { s.losses++; s.streak = Math.min(0, s.streak) - 1; }
  }
  return out;
}

function readJson(file) {
  try {
    const value = JSON.parse(fs.readFileSync(file, 'utf8'));
    return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
  } catch (error) {
    if (error.code !== 'ENOENT') console.error(`Could not read ${path.basename(file)}:`, error.message);
    return {};
  }
}
function writeJson(file, value) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const tmp = `${file}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(value));
  fs.renameSync(tmp, file);
}
const own = (o, k) => Object.hasOwn(o, k);
const put = (o, k, v) => Object.defineProperty(o, k, { value: v, writable: true, configurable: true, enumerable: true });
const pairKey = (a, b) => (a < b ? `${a}|${b}` : `${b}|${a}`);

function rankBy(rows) { // rating, then wins, then the name: equal rating and wins share a rank
  rows.sort((a, b) => b.rating - a.rating || b.wins - a.wins || (a.username < b.username ? -1 : a.username > b.username ? 1 : 0));
  let rank = 0;
  return rows.map((row, i) => ({ ...row, rank: i && rows[i - 1].rating === row.rating && rows[i - 1].wins === row.wins ? rank : (rank = i + 1) }));
}

function createArenaStore({ dataDir, pool = null } = {}) {
  if (!pool) return jsonStore(dataDir);
  return pgStore(pool);
}

// ---------------------------------------------------------------- JSON files (one process, so each change is atomic)
function jsonStore(dataDir) {
  const files = {
    accounts: path.join(dataDir, 'accounts.json'), // { [id]: { username, key, hash, salt, fails, until, locks, look, at } }
    friends: path.join(dataDir, 'friends.json'),   // { ["a|b"]: { from, to, status: 'pending' | 'friends', at } }
    stats: path.join(dataDir, 'arena.json'),       // { [id]: { rating, wins, losses, draws, streak, best, matches } }
    matches: path.join(dataDir, 'arena-matches.json'), // { list: [{ id, mode, a, b, winner, rounds, ra, rb, da, db, at }] }
    wallets: path.join(dataDir, 'wallets.json'),   // room-store's: for XP on the boards
  };
  const accounts = () => readJson(files.accounts);
  const byKey = (all, key) => Object.keys(all).find((id) => all[id].key === key) || null;
  const xpOf = (id) => { const w = readJson(files.wallets); return own(w, id) && Number.isFinite(w[id].xp) ? w[id].xp : 0; };
  const statsOf = (all, id) => (own(all, id) ? { ...freshStats(), ...all[id] } : freshStats());
  function row(id, acc, stats, wallets) {
    const s = statsOf(stats, id);
    return { id, username: acc.username, look: acc.look || null, xp: own(wallets, id) && Number.isFinite(wallets[id].xp) ? wallets[id].xp : 0, ...s };
  }
  return {
    kind: 'json',
    async init() {},
    async accountById(id) { const all = accounts(); return PLAYER_ID.test(id || '') && own(all, id) ? { id, ...all[id] } : null; },
    async accountByKey(key) { const all = accounts(), id = byKey(all, key); return id ? { id, ...all[id] } : null; },
    async createAccount({ id, username, key, hash, salt }) {
      const all = accounts();
      if (own(all, id)) return 'has-account';
      if (byKey(all, key)) return 'taken';
      put(all, id, { username, key, hash, salt, fails: 0, until: 0, locks: 0, look: null, at: Date.now() });
      writeJson(files.accounts, all);
      return 'ok';
    },
    async renameAccount(id, username, key) {
      const all = accounts();
      if (!own(all, id)) return 'none';
      const holder = byKey(all, key);
      if (holder && holder !== id) return 'taken';
      all[id].username = username; all[id].key = key;
      writeJson(files.accounts, all);
      return 'ok';
    },
    /** Sets some of: hash, salt, fails, until, locks, look. */
    async updateAccount(id, fields) {
      const all = accounts();
      if (!own(all, id)) return false;
      for (const k of ['hash', 'salt', 'fails', 'until', 'locks', 'look']) if (own(fields, k)) all[id][k] = fields[k];
      writeJson(files.accounts, all);
      return true;
    },
    /** Removes accounts with their friendships and stats; their bouts stay in the record without the ids. */
    async deleteAccounts(ids) {
      const gone = new Set(ids), all = accounts(), friends = readJson(files.friends), stats = readJson(files.stats), matches = readJson(files.matches);
      let n = 0;
      for (const id of gone) if (own(all, id)) { delete all[id]; n++; }
      for (const [k, f] of Object.entries(friends)) if (gone.has(f.from) || gone.has(f.to)) delete friends[k];
      for (const id of gone) delete stats[id];
      for (const m of matches.list || []) { if (gone.has(m.a)) m.a = null; if (gone.has(m.b)) m.b = null; }
      writeJson(files.accounts, all); writeJson(files.friends, friends); writeJson(files.stats, stats); writeJson(files.matches, matches);
      return n;
    },
    // friends
    async friendsOf(id) {
      const friends = readJson(files.friends), all = accounts(), stats = readJson(files.stats), wallets = readJson(files.wallets);
      const out = [];
      for (const f of Object.values(friends)) {
        if (f.from !== id && f.to !== id) continue;
        const other = f.from === id ? f.to : f.from;
        if (!own(all, other)) continue;
        out.push({ ...row(other, all[other], stats, wallets), status: f.status, incoming: f.status === 'pending' && f.to === id });
      }
      return out;
    },
    /** 'sent' | 'accepted' (they had asked already) | 'already' | 'limit' | 'none' (no such account). */
    async friendRequest(from, to) {
      const all = accounts();
      if (!own(all, from) || !own(all, to)) return 'none';
      const friends = readJson(files.friends), key = pairKey(from, to), f = own(friends, key) ? friends[key] : null;
      if (f && (f.status === 'friends' || f.from === from)) return 'already';
      const mine = Object.values(friends).filter((x) => x.from === from || x.to === from);
      if (mine.filter((x) => x.status === 'friends').length >= FRIENDS_MAX) return 'limit';
      if (f) { f.status = 'friends'; f.at = Date.now(); writeJson(files.friends, friends); return 'accepted'; }
      if (mine.filter((x) => x.status === 'pending' && x.from === from).length >= PENDING_MAX) return 'limit';
      const theirs = Object.values(friends).filter((x) => (x.from === to || x.to === to) && x.status === 'friends');
      if (theirs.length >= FRIENDS_MAX) return 'limit';
      put(friends, key, { from, to, status: 'pending', at: Date.now() });
      writeJson(files.friends, friends);
      return 'sent';
    },
    /** Accepts other's request to me: true when there was one. */
    async friendAccept(me, other) {
      const friends = readJson(files.friends), key = pairKey(me, other), f = own(friends, key) ? friends[key] : null;
      if (!f || f.status !== 'pending' || f.to !== me) return false;
      if (Object.values(friends).filter((x) => (x.from === me || x.to === me) && x.status === 'friends').length >= FRIENDS_MAX) return false;
      f.status = 'friends'; f.at = Date.now();
      writeJson(files.friends, friends);
      return true;
    },
    /** Ends a friendship, declines a request or takes one back: true when there was something. */
    async friendRemove(me, other) {
      const friends = readJson(files.friends), key = pairKey(me, other);
      if (!own(friends, key)) return false;
      delete friends[key]; writeJson(files.friends, friends);
      return true;
    },
    async areFriends(a, b) { const friends = readJson(files.friends), key = pairKey(a, b); return own(friends, key) && friends[key].status === 'friends'; },
    // stats and bouts
    async stats(id) { return statsOf(readJson(files.stats), id); },
    /** Records a bout and, for two accounts, updates both stats (Elo factor k, 0 = unrated). Returns the stats before and after. */
    async recordMatch({ id, mode, a, b, winner, rounds, k }) {
      const stats = readJson(files.stats), matches = readJson(files.matches);
      const sa = statsOf(stats, a), sb = b ? statsOf(stats, b) : null;
      let after = null;
      if (b) {
        after = applyResult(sa, sb, winner, k);
        put(stats, a, after[0]); put(stats, b, after[1]);
        writeJson(files.stats, stats);
      }
      const list = Array.isArray(matches.list) ? matches.list : [];
      list.push({ id, mode, a, b, winner, rounds, ra: sa.rating, rb: sb ? sb.rating : null, da: after ? after[0].rating - sa.rating : 0, db: after ? after[1].rating - sb.rating : 0, at: Date.now() });
      matches.list = list.slice(-MATCHES_KEEP);
      writeJson(files.matches, matches);
      return { before: [sa, sb], after: after || [sa, sb] };
    },
    /** me's wins and losses against each of `others` (rated and friendly bouts). */
    async headToHead(me, others) {
      const out = {}; for (const o of others) out[o] = { w: 0, l: 0 };
      for (const m of readJson(files.matches).list || []) {
        if (!m.a || !m.b || m.winner < 0) continue;
        const other = m.a === me ? m.b : m.b === me ? m.a : null;
        if (!other || !own(out, other)) continue;
        if ((m.winner === 0) === (m.a === me)) out[other].w++; else out[other].l++;
      }
      return out;
    },
    /** The players who played at least one bout, ranked: the first `top` and me. */
    async globalBoard(me, top = 50) {
      const all = accounts(), stats = readJson(files.stats), wallets = readJson(files.wallets);
      const rows = rankBy(Object.keys(stats).filter((id) => own(all, id) && stats[id].matches > 0).map((id) => row(id, all[id], stats, wallets)));
      return rows.filter((r, i) => i < top || r.id === me);
    },
    async xpOf(id) { return xpOf(id); },
    async recentMatches(id, n = 10) {
      const all = accounts();
      return (readJson(files.matches).list || []).filter((m) => m.a === id || m.b === id).slice(-n).reverse()
        .map((m) => ({ ...m, an: m.a && own(all, m.a) ? all[m.a].username : null, bn: m.b && own(all, m.b) ? all[m.b].username : null }));
    },
  };
}

// ---------------------------------------------------------------- PostgreSQL
function pgStore(pool) {
  const accountRow = (r) => r && { id: r.id, username: r.username, key: r.name_key, hash: r.pass_hash, salt: r.pass_salt,
    fails: r.fails, until: Number(r.locked_until), locks: r.locks, look: r.look };
  const statRow = (r) => (r ? { rating: r.rating, wins: r.wins, losses: r.losses, draws: r.draws, streak: r.streak, best: r.best_streak, matches: r.matches } : freshStats());
  const boardRow = (r) => ({ id: r.id, username: r.username, look: r.look, xp: Number(r.xp || 0), ...statRow(r.rating == null ? null : r) });
  const BOARD_SELECT = `a.id, a.username, a.look, COALESCE(w.xp, 0) AS xp, s.rating, s.wins, s.losses, s.draws, s.streak, s.best_streak, s.matches`;
  return {
    kind: 'postgres',
    async init() {
      const client = await pool.connect();
      try {
        await client.query('BEGIN');
        await client.query(`
          CREATE TABLE IF NOT EXISTS naadamy_accounts (
            id text PRIMARY KEY REFERENCES naadamy_identities(id) ON DELETE CASCADE,
            username text NOT NULL,
            name_key text NOT NULL,
            pass_hash text NOT NULL,
            pass_salt text NOT NULL,
            fails integer NOT NULL DEFAULT 0,
            locked_until bigint NOT NULL DEFAULT 0,
            locks integer NOT NULL DEFAULT 0,
            look jsonb,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now()
          )
        `);
        await client.query('CREATE UNIQUE INDEX IF NOT EXISTS naadamy_accounts_name_key ON naadamy_accounts (name_key)');
        await client.query(`
          CREATE TABLE IF NOT EXISTS naadamy_friends (
            lo text NOT NULL REFERENCES naadamy_accounts(id) ON DELETE CASCADE,
            hi text NOT NULL REFERENCES naadamy_accounts(id) ON DELETE CASCADE,
            requester text NOT NULL,
            status text NOT NULL CHECK (status IN ('pending', 'friends')),
            created_at timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (lo, hi),
            CHECK (lo < hi)
          )
        `);
        await client.query('CREATE INDEX IF NOT EXISTS naadamy_friends_hi ON naadamy_friends (hi)');
        await client.query(`
          CREATE TABLE IF NOT EXISTS naadamy_arena_stats (
            id text PRIMARY KEY REFERENCES naadamy_accounts(id) ON DELETE CASCADE,
            rating integer NOT NULL DEFAULT ${RATING_START},
            wins integer NOT NULL DEFAULT 0,
            losses integer NOT NULL DEFAULT 0,
            draws integer NOT NULL DEFAULT 0,
            streak integer NOT NULL DEFAULT 0,
            best_streak integer NOT NULL DEFAULT 0,
            matches integer NOT NULL DEFAULT 0,
            updated_at timestamptz NOT NULL DEFAULT now()
          )
        `);
        await client.query('CREATE INDEX IF NOT EXISTS naadamy_arena_stats_rank ON naadamy_arena_stats (rating DESC, wins DESC)');
        await client.query(`
          CREATE TABLE IF NOT EXISTS naadamy_arena_matches (
            id text PRIMARY KEY,
            mode text NOT NULL,
            a text,
            b text,
            winner smallint NOT NULL,
            rounds jsonb NOT NULL,
            rating_a integer,
            rating_b integer,
            delta_a integer NOT NULL DEFAULT 0,
            delta_b integer NOT NULL DEFAULT 0,
            played_at timestamptz NOT NULL DEFAULT now()
          )
        `);
        await client.query('CREATE INDEX IF NOT EXISTS naadamy_arena_matches_a ON naadamy_arena_matches (a, played_at DESC)');
        await client.query('CREATE INDEX IF NOT EXISTS naadamy_arena_matches_b ON naadamy_arena_matches (b, played_at DESC)');
        // a deleted account's bouts stay in the record without its id
        await client.query(`
          CREATE OR REPLACE FUNCTION naadamy_forget_account_matches() RETURNS trigger LANGUAGE plpgsql AS $$
          BEGIN
            UPDATE naadamy_arena_matches SET a = NULL WHERE a = OLD.id;
            UPDATE naadamy_arena_matches SET b = NULL WHERE b = OLD.id;
            RETURN OLD;
          END $$
        `);
        await client.query('DROP TRIGGER IF EXISTS naadamy_accounts_forget ON naadamy_accounts');
        await client.query('CREATE TRIGGER naadamy_accounts_forget AFTER DELETE ON naadamy_accounts FOR EACH ROW EXECUTE FUNCTION naadamy_forget_account_matches()');
        // Supabase serves every public table over its REST API to anyone with the project's anon key: without row level
        // security that would show usernames, password hashes and friends to the world. RLS with no policies closes it, as
        // room-store.js does for its tables; the server connects as the tables' owner, which RLS does not apply to.
        for (const table of ['naadamy_accounts', 'naadamy_friends', 'naadamy_arena_stats', 'naadamy_arena_matches']) {
          await client.query(`ALTER TABLE ${table} ENABLE ROW LEVEL SECURITY`);
        }
        await client.query('COMMIT');
      } catch (error) {
        await client.query('ROLLBACK').catch(() => {});
        throw new Error(`Could not create arena tables: ${error.message}`, { cause: error });
      } finally {
        client.release();
      }
    },
    async accountById(id) {
      if (!PLAYER_ID.test(id || '')) return null;
      const r = await pool.query('SELECT * FROM naadamy_accounts WHERE id = $1', [id]);
      return accountRow(r.rows[0]) || null;
    },
    async accountByKey(key) {
      const r = await pool.query('SELECT * FROM naadamy_accounts WHERE name_key = $1', [key]);
      return accountRow(r.rows[0]) || null;
    },
    async createAccount({ id, username, key, hash, salt }) {
      try {
        const r = await pool.query(
          `INSERT INTO naadamy_accounts (id, username, name_key, pass_hash, pass_salt) VALUES ($1, $2, $3, $4, $5)
           ON CONFLICT (id) DO NOTHING RETURNING id`, [id, username, key, hash, salt]);
        return r.rows.length ? 'ok' : 'has-account';
      } catch (error) {
        if (error.code === '23505') return 'taken'; // the unique name key: someone has this name
        throw error;
      }
    },
    async renameAccount(id, username, key) {
      try {
        const r = await pool.query('UPDATE naadamy_accounts SET username = $2, name_key = $3, updated_at = now() WHERE id = $1 RETURNING id', [id, username, key]);
        return r.rows.length ? 'ok' : 'none';
      } catch (error) {
        if (error.code === '23505') return 'taken';
        throw error;
      }
    },
    async updateAccount(id, fields) {
      const cols = { hash: 'pass_hash', salt: 'pass_salt', fails: 'fails', until: 'locked_until', locks: 'locks', look: 'look' };
      const sets = [], values = [id];
      for (const [k, col] of Object.entries(cols)) if (Object.hasOwn(fields, k)) { values.push(k === 'look' ? JSON.stringify(fields[k]) : fields[k]); sets.push(`${col} = $${values.length}`); }
      if (!sets.length) return false;
      const r = await pool.query(`UPDATE naadamy_accounts SET ${sets.join(', ')}, updated_at = now() WHERE id = $1 RETURNING id`, values);
      return r.rows.length > 0;
    },
    async deleteAccounts(ids) {
      const r = await pool.query('DELETE FROM naadamy_accounts WHERE id = ANY($1::text[]) RETURNING id', [ids]);
      return r.rows.length; // friendships and stats go with ON DELETE CASCADE, the trigger forgets the ids in the record
    },
    async friendsOf(id) {
      const r = await pool.query(
        `SELECT ${BOARD_SELECT}, f.status, f.requester
         FROM naadamy_friends f
         JOIN naadamy_accounts a ON a.id = CASE WHEN f.lo = $1 THEN f.hi ELSE f.lo END
         LEFT JOIN naadamy_arena_stats s ON s.id = a.id
         LEFT JOIN naadamy_wallets w ON w.id = a.id
         WHERE f.lo = $1 OR f.hi = $1`, [id]);
      return r.rows.map((row) => ({ ...boardRow(row), status: row.status, incoming: row.status === 'pending' && row.requester !== id }));
    },
    async friendRequest(from, to) {
      const [lo, hi] = from < to ? [from, to] : [to, from];
      const client = await pool.connect();
      try {
        await client.query('BEGIN');
        // one request at a time per pair and per player's limits: lock both accounts in id order
        const locked = await client.query('SELECT id FROM naadamy_accounts WHERE id = ANY($1::text[]) ORDER BY id FOR UPDATE', [[lo, hi]]);
        if (locked.rows.length < 2) { await client.query('ROLLBACK'); return 'none'; }
        const existing = (await client.query('SELECT requester, status FROM naadamy_friends WHERE lo = $1 AND hi = $2', [lo, hi])).rows[0];
        if (existing && (existing.status === 'friends' || existing.requester === from)) { await client.query('ROLLBACK'); return 'already'; }
        const count = async (who, cond) => Number((await client.query(`SELECT count(*) AS n FROM naadamy_friends WHERE (lo = $1 OR hi = $1) AND ${cond}`, [who])).rows[0].n);
        if (await count(from, "status = 'friends'") >= FRIENDS_MAX || await count(to, "status = 'friends'") >= FRIENDS_MAX) { await client.query('ROLLBACK'); return 'limit'; }
        if (existing) {
          await client.query("UPDATE naadamy_friends SET status = 'friends', created_at = now() WHERE lo = $1 AND hi = $2", [lo, hi]);
          await client.query('COMMIT');
          return 'accepted';
        }
        if (Number((await client.query("SELECT count(*) AS n FROM naadamy_friends WHERE requester = $1 AND status = 'pending'", [from])).rows[0].n) >= PENDING_MAX) { await client.query('ROLLBACK'); return 'limit'; }
        await client.query("INSERT INTO naadamy_friends (lo, hi, requester, status) VALUES ($1, $2, $3, 'pending')", [lo, hi, from]);
        await client.query('COMMIT');
        return 'sent';
      } catch (error) {
        await client.query('ROLLBACK').catch(() => {});
        throw error;
      } finally {
        client.release();
      }
    },
    async friendAccept(me, other) {
      const [lo, hi] = me < other ? [me, other] : [other, me];
      const client = await pool.connect();
      try {
        await client.query('BEGIN');
        await client.query('SELECT id FROM naadamy_accounts WHERE id = ANY($1::text[]) ORDER BY id FOR UPDATE', [[lo, hi]]);
        const n = Number((await client.query("SELECT count(*) AS n FROM naadamy_friends WHERE (lo = $1 OR hi = $1) AND status = 'friends'", [me])).rows[0].n);
        if (n >= FRIENDS_MAX) { await client.query('ROLLBACK'); return false; }
        const r = await client.query("UPDATE naadamy_friends SET status = 'friends', created_at = now() WHERE lo = $1 AND hi = $2 AND status = 'pending' AND requester = $3 RETURNING lo", [lo, hi, other]);
        await client.query('COMMIT');
        return r.rows.length > 0;
      } catch (error) {
        await client.query('ROLLBACK').catch(() => {});
        throw error;
      } finally {
        client.release();
      }
    },
    async friendRemove(me, other) {
      const [lo, hi] = me < other ? [me, other] : [other, me];
      const r = await pool.query('DELETE FROM naadamy_friends WHERE lo = $1 AND hi = $2 RETURNING lo', [lo, hi]);
      return r.rows.length > 0;
    },
    async areFriends(a, b) {
      const [lo, hi] = a < b ? [a, b] : [b, a];
      const r = await pool.query("SELECT 1 FROM naadamy_friends WHERE lo = $1 AND hi = $2 AND status = 'friends'", [lo, hi]);
      return r.rows.length > 0;
    },
    async stats(id) {
      const r = await pool.query('SELECT * FROM naadamy_arena_stats WHERE id = $1', [id]);
      return statRow(r.rows[0]);
    },
    async recordMatch({ id, mode, a, b, winner, rounds, k }) {
      const client = await pool.connect();
      try {
        await client.query('BEGIN');
        let before = [freshStats(), null], after = null;
        if (b) {
          for (const pid of [a, b].sort()) await client.query('INSERT INTO naadamy_arena_stats (id) SELECT $1 WHERE EXISTS (SELECT 1 FROM naadamy_accounts WHERE id = $1) ON CONFLICT (id) DO NOTHING', [pid]);
          const r = await client.query('SELECT * FROM naadamy_arena_stats WHERE id = ANY($1::text[]) ORDER BY id FOR UPDATE', [[a, b]]);
          const get = (pid) => statRow(r.rows.find((row) => row.id === pid));
          before = [get(a), get(b)];
          if (r.rows.length === 2) {
            after = applyResult(before[0], before[1], winner, k);
            for (const [pid, s] of [[a, after[0]], [b, after[1]]]) {
              await client.query(`UPDATE naadamy_arena_stats SET rating = $2, wins = $3, losses = $4, draws = $5, streak = $6, best_streak = $7, matches = $8, updated_at = now() WHERE id = $1`,
                [pid, s.rating, s.wins, s.losses, s.draws, s.streak, s.best, s.matches]);
            }
          }
        } else {
          const r = await client.query('SELECT * FROM naadamy_arena_stats WHERE id = $1', [a]);
          before = [statRow(r.rows[0]), null];
        }
        await client.query(
          `INSERT INTO naadamy_arena_matches (id, mode, a, b, winner, rounds, rating_a, rating_b, delta_a, delta_b) VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7, $8, $9, $10)
           ON CONFLICT (id) DO NOTHING`,
          [id, mode, a, b, winner, JSON.stringify(rounds), before[0].rating, before[1] ? before[1].rating : null,
            after ? after[0].rating - before[0].rating : 0, after ? after[1].rating - before[1].rating : 0]);
        await client.query('COMMIT');
        return { before, after: after || before };
      } catch (error) {
        await client.query('ROLLBACK').catch(() => {});
        throw error;
      } finally {
        client.release();
      }
    },
    async headToHead(me, others) {
      const out = {}; for (const o of others) out[o] = { w: 0, l: 0 };
      if (!others.length) return out;
      const r = await pool.query(
        `SELECT CASE WHEN a = $1 THEN b ELSE a END AS other,
           count(*) FILTER (WHERE (winner = 0) = (a = $1)) AS w, count(*) FILTER (WHERE (winner = 0) <> (a = $1)) AS l
         FROM naadamy_arena_matches
         WHERE winner >= 0 AND ((a = $1 AND b = ANY($2::text[])) OR (b = $1 AND a = ANY($2::text[])))
         GROUP BY 1`, [me, others]);
      for (const row of r.rows) out[row.other] = { w: Number(row.w), l: Number(row.l) };
      return out;
    },
    async globalBoard(me, top = 50) {
      const r = await pool.query(
        `SELECT * FROM (
           SELECT ${BOARD_SELECT}, rank() OVER (ORDER BY s.rating DESC, s.wins DESC) AS place,
             row_number() OVER (ORDER BY s.rating DESC, s.wins DESC, a.username) AS n
           FROM naadamy_arena_stats s JOIN naadamy_accounts a ON a.id = s.id LEFT JOIN naadamy_wallets w ON w.id = s.id
           WHERE s.matches > 0
         ) ranked WHERE n <= $2 OR id = $1 ORDER BY n`, [me, top]);
      return r.rows.map((row) => ({ ...boardRow(row), rank: Number(row.place) }));
    },
    async xpOf(id) {
      const r = await pool.query('SELECT xp FROM naadamy_wallets WHERE id = $1', [id]);
      return r.rows[0] ? Number(r.rows[0].xp) : 0;
    },
    async recentMatches(id, n = 10) {
      const r = await pool.query(
        `SELECT m.*, an.username AS an, bn.username AS bn FROM naadamy_arena_matches m
         LEFT JOIN naadamy_accounts an ON an.id = m.a LEFT JOIN naadamy_accounts bn ON bn.id = m.b
         WHERE m.a = $1 OR m.b = $1 ORDER BY m.played_at DESC LIMIT $2`, [id, n]);
      return r.rows.map((m) => ({ id: m.id, mode: m.mode, a: m.a, b: m.b, winner: m.winner, rounds: m.rounds, ra: m.rating_a, rb: m.rating_b, da: m.delta_a, db: m.delta_b, at: new Date(m.played_at).getTime(), an: m.an, bn: m.bn }));
    },
  };
}

module.exports = { createArenaStore, applyResult, eloDelta, rankBy, RATING_START, FRIENDS_MAX, PENDING_MAX, STAT_FIELDS };
