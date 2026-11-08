'use strict';
/* Email accounts: POST /api/auth. `register` takes an address and a password and makes the account and its player (an id
   and this device's ticket) at once; no mail is sent, so the address is not proved. The game then asks for the name and
   mascot, and `profile` keeps them (with the save's default age), so `login` on any device brings the same player back.
   Until `profile`, `ready` is false: a login then goes on with those steps. An older save with no email sends its `id` and
   `ticket` with `register`: the account then belongs to that player, so nothing is lost. An account holds at most 2 players:
   `add` makes the second, and `login` answers with both. Passwords are kept only as salted scrypt hashes; 5 wrong passwords
   lock the account for 15 minutes, doubling, at most a day. See docs/PROTOCOL.md, "Email accounts". */

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const clientIp = require('./client-ip.js');
const { hashPassword, passwordRecord } = require('./accounts.js');
const { emailText } = require('./parent-email.js');

const PLAYER_ID = /^[\w-]{4,24}$/;
const PLAYER_TICKET = /^[a-f0-9]{64}$/i;
const PASSWORD_MIN = 8, PASSWORD_MAX = 64;
const TRIES = 5, LOCK_MS = 15 * 60 * 1000, LOCK_MAX = 24 * 60 * 60 * 1000;
const PER_MINUTE = 30;

const passwordOk = (p) => typeof p === 'string' && p.length >= PASSWORD_MIN && p.length <= PASSWORD_MAX;
const sameHex = (a, b) => a.length === b.length && crypto.timingSafeEqual(Buffer.from(a, 'hex'), Buffer.from(b, 'hex'));

// ---------------------------------------------------------------- where accounts are kept
function jsonAccounts(dataDir) {
  const file = path.join(dataDir, 'email-accounts.json'); // { [email]: { id, hash, salt, name, age, avatar, ready, fails, locks, until } }
  const read = () => { try { const v = JSON.parse(fs.readFileSync(file, 'utf8')); return v && typeof v === 'object' && !Array.isArray(v) ? v : {}; } catch { return {}; } };
  const write = (all) => { fs.mkdirSync(dataDir, { recursive: true }); fs.writeFileSync(`${file}.tmp`, JSON.stringify(all), { mode: 0o600 }); fs.renameSync(`${file}.tmp`, file); };
  const put = (all, email, value) => Object.defineProperty(all, email, { value, writable: true, configurable: true, enumerable: true });
  return {
    async init() {},
    async byEmail(email) { const all = read(); return Object.hasOwn(all, email) ? { email, ...all[email] } : null; },
    async create({ email, id, hash, salt }) {
      const all = read();
      if (Object.hasOwn(all, email) || Object.values(all).some((account) => account.id === id)) return false;
      put(all, email, { id, hash, salt, name: '', age: 0, avatar: 0, ready: false, fails: 0, locks: 0, until: 0 });
      write(all); return true;
    },
    async update(email, fields) { const all = read(); if (Object.hasOwn(all, email)) { put(all, email, { ...all[email], ...fields }); write(all); } },
    async byId(id) { const all = read(), email = Object.keys(all).find((e) => all[e].id === id); return email ? { email, ...all[email] } : null; },
    async second(email) { const all = read(); return (Object.hasOwn(all, email) && all[email].second) || null; },
    async addSecond(email, player) {
      const all = read();
      if (!Object.hasOwn(all, email) || all[email].second) return false;
      put(all, email, { ...all[email], second: player }); write(all); return true;
    },
    async setProfile(id, fields) {
      const all = read(), email = Object.keys(all).find((e) => all[e].id === id);
      if (!email) return false;
      put(all, email, { ...all[email], ...fields, ready: true }); write(all); return true;
    },
    async forget(ids) {
      const gone = new Set(ids), all = read(); let changed = false;
      for (const e of Object.keys(all)) {
        if (gone.has(all[e].id)) { delete all[e]; changed = true; } else if (all[e].second && gone.has(all[e].second.id)) { all[e].second = null; changed = true; }
      }
      if (changed) write(all);
    },
  };
}

function pgAccounts(pool) {
  const row = (r) => r && { email: r.email, id: r.player_id, hash: r.pass_hash, salt: r.pass_salt, name: r.name, age: r.age, avatar: r.avatar, ready: r.ready, fails: r.fails, locks: r.locks, until: Number(r.until_ms) };
  const COLUMNS = { fails: 'fails', locks: 'locks', until: 'until_ms' };
  return {
    async init() {
      await pool.query(`
        CREATE TABLE IF NOT EXISTS naadamy_email_accounts (
          email text PRIMARY KEY,
          player_id text NOT NULL UNIQUE REFERENCES naadamy_identities (id) ON DELETE CASCADE,
          pass_hash text NOT NULL,
          pass_salt text NOT NULL,
          name text NOT NULL DEFAULT '',
          age integer NOT NULL DEFAULT 0,
          avatar integer NOT NULL DEFAULT 0,
          ready boolean NOT NULL DEFAULT false,
          fails integer NOT NULL DEFAULT 0,
          locks integer NOT NULL DEFAULT 0,
          until_ms bigint NOT NULL DEFAULT 0,
          created_at timestamptz NOT NULL DEFAULT now()
        )`);
      await pool.query(`
        CREATE TABLE IF NOT EXISTS naadamy_email_second (
          email text PRIMARY KEY REFERENCES naadamy_email_accounts (email) ON DELETE CASCADE,
          player_id text NOT NULL UNIQUE REFERENCES naadamy_identities (id) ON DELETE CASCADE,
          name text NOT NULL,
          age integer NOT NULL,
          avatar integer NOT NULL,
          created_at timestamptz NOT NULL DEFAULT now()
        )`); // one row per email at most: the database itself keeps an account to 2 players
      await pool.query('ALTER TABLE naadamy_email_accounts ENABLE ROW LEVEL SECURITY'); // closed to Supabase's REST API, like the other tables
      await pool.query('ALTER TABLE naadamy_email_second ENABLE ROW LEVEL SECURITY');
    },
    async byEmail(email) { const r = await pool.query('SELECT * FROM naadamy_email_accounts WHERE email = $1', [email]); return row(r.rows[0]) || null; },
    async byId(id) { const r = await pool.query('SELECT * FROM naadamy_email_accounts WHERE player_id = $1', [id]); return row(r.rows[0]) || null; },
    async second(email) {
      const r = await pool.query('SELECT player_id, name, age, avatar FROM naadamy_email_second WHERE email = $1', [email]);
      return r.rows[0] ? { id: r.rows[0].player_id, name: r.rows[0].name, age: r.rows[0].age, avatar: r.rows[0].avatar } : null;
    },
    async addSecond(email, { id, name, age, avatar }) {
      const r = await pool.query('INSERT INTO naadamy_email_second (email, player_id, name, age, avatar) VALUES ($1, $2, $3, $4, $5) ON CONFLICT DO NOTHING RETURNING email', [email, id, name, age, avatar]);
      return r.rows.length > 0;
    },
    async create({ email, id, hash, salt }) {
      const r = await pool.query('INSERT INTO naadamy_email_accounts (email, player_id, pass_hash, pass_salt) VALUES ($1, $2, $3, $4) ON CONFLICT DO NOTHING RETURNING email', [email, id, hash, salt]);
      return r.rows.length > 0;
    },
    async update(email, fields) {
      const keys = Object.keys(fields).filter((k) => Object.hasOwn(COLUMNS, k));
      if (keys.length) await pool.query(`UPDATE naadamy_email_accounts SET ${keys.map((k, i) => `${COLUMNS[k]} = $${i + 2}`).join(', ')} WHERE email = $1`, [email, ...keys.map((k) => fields[k])]);
    },
    async setProfile(id, { name, age, avatar }) {
      const r = await pool.query('UPDATE naadamy_email_accounts SET name = $2, age = $3, avatar = $4, ready = true WHERE player_id = $1 RETURNING email', [id, name, age, avatar]);
      return r.rows.length > 0;
    },
    async forget() {}, // gone with the identities (ON DELETE CASCADE)
  };
}

// ---------------------------------------------------------------- the flow
module.exports = function createEmailAuth(roomStore, { dataDir, cloudOf = async () => null, now = () => Date.now(), perMinute = PER_MINUTE } = {}) {
  const accounts = roomStore.kind === 'postgres' && roomStore.pool ? pgAccounts(roomStore.pool) : jsonAccounts(dataDir);
  const hits = new Map();

  function allow(ip) {
    const t = now(), h = hits.get(ip);
    if (!h || t - h.at > 60_000) { hits.set(ip, { at: t, n: 1 }); if (hits.size > 5000) for (const [k, v] of hits) if (t - v.at > 60_000) hits.delete(k); return true; }
    return ++h.n <= perMinute;
  }
  /** The account at once, with no mail: the address is not proved to be this person's. An older save sends its `id` and `ticket`. */
  async function register(message) {
    const email = emailText(message.email);
    if (!email) return { ok: false, reason: 'email-invalid' };
    if (!passwordOk(message.password)) return { ok: false, reason: 'weak-password' };
    const guest = message.id !== undefined || message.ticket !== undefined;
    const guestId = String(message.id || ''), guestTicket = String(message.ticket || '');
    if (guest && (!PLAYER_ID.test(guestId) || !PLAYER_TICKET.test(guestTicket) || !(await roomStore.authenticatePlayer(guestId, guestTicket)))) return { ok: false, reason: 'identity' };
    if (await accounts.byEmail(email)) return { ok: false, reason: 'taken' };
    const { hash, salt } = await passwordRecord(message.password);
    const id = guest ? guestId : 'p' + crypto.randomBytes(8).toString('hex'), ticket = guest ? guestTicket : crypto.randomBytes(32).toString('hex');
    if (!guest && !(await roomStore.authenticatePlayer(id, ticket))) return { ok: false, reason: 'storage' }; // the first ticket of an id makes its identity
    if (!(await accounts.create({ email, id, hash, salt }))) return { ok: false, reason: 'taken' };
    return { ok: true, id, ticket, ready: false };
  }

  async function login(message) {
    const email = emailText(message.email);
    if (!email || !passwordOk(message.password)) return { ok: false, reason: 'wrong' };
    const account = await accounts.byEmail(email);
    if (!account) return { ok: false, reason: 'wrong' };
    const t = now();
    if (account.until > t) return { ok: false, reason: 'locked', wait: Math.ceil((account.until - t) / 1000) };
    if (!sameHex(await hashPassword(message.password, account.salt), account.hash)) {
      const fails = account.fails + 1;
      if (fails < TRIES) { await accounts.update(email, { fails }); return { ok: false, reason: 'wrong', left: TRIES - fails }; }
      const locks = account.locks + 1, until = t + Math.min(LOCK_MAX, LOCK_MS * 2 ** Math.min(20, locks - 1));
      await accounts.update(email, { fails: 0, locks, until });
      return { ok: false, reason: 'locked', wait: Math.ceil((until - t) / 1000) };
    }
    if (account.fails || account.locks) await accounts.update(email, { fails: 0, locks: Math.max(0, account.locks - 1) });
    if (await roomStore.isErased(account.id)) return { ok: false, reason: 'erased' };
    const ticket = crypto.randomBytes(32).toString('hex'); // this device's own ticket for the player
    await roomStore.addDeviceTicket(account.id, ticket);
    const answer = { ok: true, id: account.id, ticket, ready: account.ready, name: account.name, age: account.age, avatar: account.avatar, cloud: await cloudOf(account.id) };
    const second = await accounts.second(email);
    if (second && !(await roomStore.isErased(second.id))) {
      const secondTicket = crypto.randomBytes(32).toString('hex');
      await roomStore.addDeviceTicket(second.id, secondTicket);
      answer.second = { ...second, ticket: secondTicket, cloud: await cloudOf(second.id) };
    }
    return answer;
  }

  /** The name, age and mascot in a message, or null when one of them is not valid. */
  function profileFields(message) {
    const name = typeof message.name === 'string' ? message.name.replace(/[<>]/g, '').trim().slice(0, 20) : '', age = message.age, avatar = message.avatar;
    if (!name || !Number.isInteger(age) || age < 3 || age > 120 || !Number.isInteger(avatar) || avatar < 0 || avatar > 11) return null;
    return { name, age, avatar };
  }

  async function profile(message) {
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (!PLAYER_ID.test(id) || !PLAYER_TICKET.test(ticket) || !(await roomStore.authenticatePlayer(id, ticket))) return { ok: false, reason: 'identity' };
    const fields = profileFields(message);
    if (!fields) return { ok: false, reason: 'invalid' };
    return (await accounts.setProfile(id, fields)) ? { ok: true } : { ok: false, reason: 'identity' };
  }

  /** The account's first player (id and ticket) makes the second one. */
  async function add(message) {
    const owner = String(message.id || ''), ownerTicket = String(message.ticket || '');
    if (!PLAYER_ID.test(owner) || !PLAYER_TICKET.test(ownerTicket) || !(await roomStore.authenticatePlayer(owner, ownerTicket))) return { ok: false, reason: 'identity' };
    const fields = profileFields(message);
    if (!fields) return { ok: false, reason: 'invalid' };
    const account = await accounts.byId(owner);
    if (!account) return { ok: false, reason: 'identity' };
    if (await accounts.second(account.email)) return { ok: false, reason: 'full' };
    const id = 'p' + crypto.randomBytes(8).toString('hex'), ticket = crypto.randomBytes(32).toString('hex');
    if (!(await roomStore.authenticatePlayer(id, ticket))) return { ok: false, reason: 'storage' };
    if (!(await accounts.addSecond(account.email, { id, ...fields }))) return { ok: false, reason: 'full' };
    return { ok: true, id, ticket };
  }

  const ACTS = { register, login, profile, add };
  function handle(req, res) {
    const reply = (status, body) => { res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }); res.end(JSON.stringify(body)); };
    if (req.method !== 'POST') return reply(405, { ok: false, reason: 'method' });
    let raw = '';
    req.on('data', (chunk) => { raw += chunk; if (raw.length > 4000) req.destroy(); });
    req.on('end', async () => {
      let message; try { message = JSON.parse(raw); } catch { return reply(400, { ok: false, reason: 'invalid' }); }
      if (!message || typeof message !== 'object') return reply(400, { ok: false, reason: 'invalid' });
      if (!allow(clientIp(req))) return reply(429, { ok: false, reason: 'busy' });
      if (typeof message.act !== 'string' || !Object.hasOwn(ACTS, message.act)) return reply(400, { ok: false, reason: 'invalid' });
      try {
        reply(200, await ACTS[message.act](message));
      } catch (error) {
        console.error('Email auth:', error.message);
        reply(500, { ok: false, reason: 'storage' });
      }
    });
  }

  return { handle, init: () => accounts.init(), forget: (ids) => accounts.forget(ids) };
};
