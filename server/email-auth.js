'use strict';
/* Email accounts: POST /api/auth. `signup` takes an address and a password and mails a 6-digit code; `verify` with that code
   makes the account and its player (an id and this device's ticket). The game then asks for the name, mascot and secret
   pictures, and `profile` keeps the name and mascot (with the save's default age), so `login` on any device brings the same player back. Until `profile`,
   `ready` is false: a login then goes on with those steps. Passwords are kept only as salted scrypt hashes; a code only as a
   hash, in memory, for 15 minutes and 5 tries; 5 wrong passwords lock the account for 15 minutes, doubling, at most a day.
   A guest (a player made on the device, with no email) sends its `id` and `ticket` with `verify`: the account then belongs to
   that player, so nothing is lost.
   Mail goes through Resend as in parent-email.js; without it the codes are printed to the log, except in production, where
   signing up is off. See docs/PROTOCOL.md, "Email accounts". */

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const clientIp = require('./client-ip.js');
const { hashPassword, passwordRecord } = require('./accounts.js');
const { resend, emailText } = require('./parent-email.js');

const PLAYER_ID = /^[\w-]{4,24}$/;
const PLAYER_TICKET = /^[a-f0-9]{64}$/i;
const CODE = /^\d{6}$/;
const PASSWORD_MIN = 8, PASSWORD_MAX = 64;
const CODE_MS = 15 * 60 * 1000, CODE_TRIES = 5, PENDING_MAX = 2000;
const HOUR = 60 * 60 * 1000, DAY = 24 * HOUR;
const SENDS_PER_HOUR = 3, SENDS_PER_DAY = 500; // mails to one address, and in all
const TRIES = 5, LOCK_MS = 15 * 60 * 1000, LOCK_MAX = DAY;
const PER_MINUTE = 30;

const passwordOk = (p) => typeof p === 'string' && p.length >= PASSWORD_MIN && p.length <= PASSWORD_MAX;
const codeHash = (email, code) => crypto.createHash('sha256').update(`${email}:${code}`).digest('hex');
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
    async setProfile(id, fields) {
      const all = read(), email = Object.keys(all).find((e) => all[e].id === id);
      if (!email) return false;
      put(all, email, { ...all[email], ...fields, ready: true }); write(all); return true;
    },
    async forget(ids) { const gone = new Set(ids), all = read(); let changed = false; for (const e of Object.keys(all)) if (gone.has(all[e].id)) { delete all[e]; changed = true; } if (changed) write(all); },
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
      await pool.query('ALTER TABLE naadamy_email_accounts ENABLE ROW LEVEL SECURITY'); // closed to Supabase's REST API, like the other tables
    },
    async byEmail(email) { const r = await pool.query('SELECT * FROM naadamy_email_accounts WHERE email = $1', [email]); return row(r.rows[0]) || null; },
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
module.exports = function createEmailAuth(roomStore, { dataDir, cloudOf = async () => null, env = process.env, now = () => Date.now(), send, perMinute = PER_MINUTE } = {}) {
  const apiKey = env.RESEND_API_KEY || '', from = env.EMAIL_FROM || '';
  const production = Boolean(env.RENDER || env.NODE_ENV === 'production');
  const canMail = Boolean(send || (apiKey && from));
  const enabled = canMail || !production;
  const deliver = send || (canMail ? resend(apiKey, from) : async ({ to, code }) => console.log(`Email auth: no mail service, the code for ${to} is ${code}`));
  const accounts = roomStore.kind === 'postgres' && roomStore.pool ? pgAccounts(roomStore.pool) : jsonAccounts(dataDir);
  const pending = new Map(); // email -> { hash, salt, code, until, tries }
  const sends = new Map(); // email -> times of its mails this hour
  let sentToday = [];
  const hits = new Map();

  function allow(ip) {
    const t = now(), h = hits.get(ip);
    if (!h || t - h.at > 60_000) { hits.set(ip, { at: t, n: 1 }); if (hits.size > 5000) for (const [k, v] of hits) if (t - v.at > 60_000) hits.delete(k); return true; }
    return ++h.n <= perMinute;
  }
  function sweep() {
    const t = now();
    for (const [email, p] of pending) if (p.until <= t) pending.delete(email);
    for (const [email, times] of sends) { const left = times.filter((at) => t - at < HOUR); if (left.length) sends.set(email, left); else sends.delete(email); }
    sentToday = sentToday.filter((at) => t - at < DAY);
  }
  function codeMail(to, code) {
    const mn = `Naadamy-д бүртгүүлэх код: ${code}. Энэ код 15 минут хүчинтэй. Та өөрөө бүртгүүлээгүй бол энэ захидлыг үл тоомсорлоорой.`;
    const en = `Your Naadamy sign-up code: ${code}. It works for 15 minutes. If you did not sign up, just ignore this email.`;
    return { to, code, subject: `Naadamy: ${code} бол таны код / is your code`, text: `${mn}\n\n${en}`, html: `<p>${mn}</p><hr><p>${en}</p>` };
  }

  async function signup(message) {
    if (!enabled) return { ok: false, reason: 'email-off' };
    const email = emailText(message.email);
    if (!email) return { ok: false, reason: 'email-invalid' };
    if (!passwordOk(message.password)) return { ok: false, reason: 'weak-password' };
    if (await accounts.byEmail(email)) return { ok: false, reason: 'taken' };
    sweep();
    const t = now(), times = sends.get(email) || [];
    if (times.length >= SENDS_PER_HOUR || sentToday.length >= SENDS_PER_DAY || (pending.size >= PENDING_MAX && !pending.has(email))) return { ok: false, reason: 'busy' };
    const code = String(crypto.randomInt(1_000_000)).padStart(6, '0');
    const record = await passwordRecord(message.password);
    try {
      await deliver(codeMail(email, code));
    } catch (error) {
      console.error('Email auth: the code could not be sent:', error.message);
      return { ok: false, reason: 'email-failed' };
    }
    sends.set(email, [...times, t]); sentToday.push(t);
    pending.set(email, { ...record, code: codeHash(email, code), until: t + CODE_MS, tries: 0 });
    return { ok: true };
  }

  async function verify(message) {
    const email = emailText(message.email), code = String(message.code || '').trim();
    const guest = message.id !== undefined || message.ticket !== undefined;
    const guestId = String(message.id || ''), guestTicket = String(message.ticket || '');
    if (guest && (!PLAYER_ID.test(guestId) || !PLAYER_TICKET.test(guestTicket) || !(await roomStore.authenticatePlayer(guestId, guestTicket)))) return { ok: false, reason: 'identity' };
    sweep();
    const p = pending.get(email);
    if (!p) return { ok: false, reason: 'expired' };
    if (!CODE.test(code) || !sameHex(codeHash(email, code), p.code)) {
      if (++p.tries >= CODE_TRIES) { pending.delete(email); return { ok: false, reason: 'expired' }; }
      return { ok: false, reason: 'code', left: CODE_TRIES - p.tries };
    }
    pending.delete(email); // a code works once
    const id = guest ? guestId : 'p' + crypto.randomBytes(8).toString('hex'), ticket = guest ? guestTicket : crypto.randomBytes(32).toString('hex');
    if (!guest && !(await roomStore.authenticatePlayer(id, ticket))) return { ok: false, reason: 'storage' }; // the first ticket of an id makes its identity
    if (!(await accounts.create({ email, id, hash: p.hash, salt: p.salt }))) return { ok: false, reason: 'taken' };
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
    return { ok: true, id: account.id, ticket, ready: account.ready, name: account.name, age: account.age, avatar: account.avatar, cloud: await cloudOf(account.id) };
  }

  async function profile(message) {
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (!PLAYER_ID.test(id) || !PLAYER_TICKET.test(ticket) || !(await roomStore.authenticatePlayer(id, ticket))) return { ok: false, reason: 'identity' };
    const name = typeof message.name === 'string' ? message.name.replace(/[<>]/g, '').trim().slice(0, 20) : '', age = message.age, avatar = message.avatar;
    if (!name || !Number.isInteger(age) || age < 3 || age > 120 || !Number.isInteger(avatar) || avatar < 0 || avatar > 11) return { ok: false, reason: 'invalid' };
    return (await accounts.setProfile(id, { name, age, avatar })) ? { ok: true } : { ok: false, reason: 'identity' };
  }

  const ACTS = { signup, verify, login, profile };
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

  return { handle, init: () => accounts.init(), forget: (ids) => accounts.forget(ids), enabled };
};
