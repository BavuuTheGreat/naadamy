'use strict';
/* Email and password for every player: POST /api/email-account. Signing up starts with them
   (`check`: is the email still free) and, once the profile is made, gives the new player
   (its id and device ticket) an email and a password; with them the player logs in on any other device, which gets its
   own ticket for the same id (same coins, XP, clothes, cloud save), as arena and Google logins do.

   The email is not confirmed when signing up. A forgotten password is reset with a 6-digit code mailed with Resend
   (RESEND_API_KEY, EMAIL_FROM, as parent-email.js); without them `forgot` answers `mail-off`. The answer to `forgot` is
   the same whether or not the email has an account, so nobody can use it to find out who plays.

   Passwords are kept only as salted scrypt hashes; 5 wrong tries lock the account for 15 minutes, doubling each time,
   at most a day (as accounts.js). A reset code is kept hashed, lasts 15 minutes and allows 5 tries. The name, age and
   mascot are kept with it so a login on a new device can rebuild the profile. Stored in naadamy_email_accounts
   (PostgreSQL, gone with the identity) or email-accounts.json in DATA_DIR; an erased player's record goes too. */

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const clientIp = require('./client-ip.js');

const PLAYER_ID = /^[\w-]{4,24}$/;
const PLAYER_TICKET = /^[a-f0-9]{64}$/i;
const EMAIL = /^[^\s@<>()[\]\\,;:"]{1,64}@[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$/;
const PASSWORD_MIN = 6, PASSWORD_MAX = 64;
const TRIES = 5, LOCK_MS = 15 * 60 * 1000, LOCK_MAX = 24 * 60 * 60 * 1000;
const RESET_MS = 15 * 60 * 1000, RESET_TRIES = 5, RESET_GAP_MS = 60 * 1000;
const PER_MINUTE = 30;

/** The email lower-cased and trimmed, or '' when it isn't one. */
function emailOf(value) {
  const email = typeof value === 'string' ? value.trim().toLowerCase() : '';
  return email.length <= 254 && EMAIL.test(email) ? email : '';
}
const passwordOk = (p) => typeof p === 'string' && p.length >= PASSWORD_MIN && p.length <= PASSWORD_MAX;
const cleanName = (v) => (typeof v === 'string' ? v.replace(/[<>\u0000-\u001f]/g, '').trim().slice(0, 20) : '');
const cleanInt = (v, min, max, fallback) => (Number.isSafeInteger(v) && v >= min && v <= max ? v : fallback);
const sha = (text) => crypto.createHash('sha256').update(text).digest('hex');

function hashPassword(password, salt) {
  return new Promise((resolve, reject) => {
    crypto.scrypt(password.normalize('NFC'), Buffer.from(salt, 'hex'), 32, (error, key) => (error ? reject(error) : resolve(key.toString('hex'))));
  });
}
async function passwordRecord(password) {
  const salt = crypto.randomBytes(16).toString('hex');
  return { hash: await hashPassword(password, salt), salt };
}

// ---------------------------------------------------------------- where accounts are kept
// A record: { id, email, hash, salt, name, age, avatar, fails, until, locks, resetHash, resetUntil, resetTries, resetAt }
function jsonAccounts(dataDir) {
  const file = path.join(dataDir, 'email-accounts.json'); // { [player id]: record without id }
  const read = () => { try { const v = JSON.parse(fs.readFileSync(file, 'utf8')); return v && typeof v === 'object' && !Array.isArray(v) ? v : {}; } catch { return {}; } };
  const write = (all) => { fs.mkdirSync(dataDir, { recursive: true }); fs.writeFileSync(`${file}.tmp`, JSON.stringify(all), { mode: 0o600 }); fs.renameSync(`${file}.tmp`, file); };
  const put = (all, id, value) => Object.defineProperty(all, id, { value, writable: true, configurable: true, enumerable: true });
  return {
    async init() {},
    async byEmail(email) { const all = read(), id = Object.keys(all).find((k) => all[k].email === email); return id ? { id, ...all[id] } : null; },
    async byId(id) { const all = read(); return Object.hasOwn(all, id) ? { id, ...all[id] } : null; },
    async create(record) {
      const all = read();
      if (Object.hasOwn(all, record.id)) return 'has-email';
      if (Object.values(all).some((r) => r.email === record.email)) return 'taken';
      const { id, ...rest } = record;
      put(all, id, { fails: 0, until: 0, locks: 0, resetHash: '', resetUntil: 0, resetTries: 0, resetAt: 0, ...rest });
      write(all); return 'ok';
    },
    async update(id, fields) { const all = read(); if (!Object.hasOwn(all, id)) return; put(all, id, { ...all[id], ...fields }); write(all); },
    async forget(ids) { const all = read(); let changed = false; for (const id of ids) if (Object.hasOwn(all, id)) { delete all[id]; changed = true; } if (changed) write(all); },
  };
}

function pgAccounts(pool) {
  const COLUMNS = { email: 'email', hash: 'pass_hash', salt: 'pass_salt', name: 'name', age: 'age', avatar: 'avatar', fails: 'fails', until: 'locked_until',
    locks: 'locks', resetHash: 'reset_hash', resetUntil: 'reset_until', resetTries: 'reset_tries', resetAt: 'reset_at' };
  const row = (r) => r && { id: r.player_id, email: r.email, hash: r.pass_hash, salt: r.pass_salt, name: r.name, age: r.age, avatar: r.avatar, fails: r.fails,
    until: Number(r.locked_until), locks: r.locks, resetHash: r.reset_hash, resetUntil: Number(r.reset_until), resetTries: r.reset_tries, resetAt: Number(r.reset_at) };
  return {
    async init() {
      await pool.query(`
        CREATE TABLE IF NOT EXISTS naadamy_email_accounts (
          player_id text PRIMARY KEY REFERENCES naadamy_identities (id) ON DELETE CASCADE,
          email text NOT NULL UNIQUE,
          pass_hash text NOT NULL,
          pass_salt text NOT NULL,
          name text NOT NULL DEFAULT '',
          age integer NOT NULL DEFAULT 8,
          avatar integer NOT NULL DEFAULT 0,
          fails integer NOT NULL DEFAULT 0,
          locked_until bigint NOT NULL DEFAULT 0,
          locks integer NOT NULL DEFAULT 0,
          reset_hash text NOT NULL DEFAULT '',
          reset_until bigint NOT NULL DEFAULT 0,
          reset_tries integer NOT NULL DEFAULT 0,
          reset_at bigint NOT NULL DEFAULT 0,
          created_at timestamptz NOT NULL DEFAULT now()
        )`);
      await pool.query('ALTER TABLE naadamy_email_accounts ENABLE ROW LEVEL SECURITY'); // closed to Supabase's REST API
    },
    async byEmail(email) { return row((await pool.query('SELECT * FROM naadamy_email_accounts WHERE email = $1', [email])).rows[0]) || null; },
    async byId(id) { return row((await pool.query('SELECT * FROM naadamy_email_accounts WHERE player_id = $1', [id])).rows[0]) || null; },
    async create(r) {
      const inserted = await pool.query(
        `INSERT INTO naadamy_email_accounts (player_id, email, pass_hash, pass_salt, name, age, avatar) VALUES ($1, $2, $3, $4, $5, $6, $7)
         ON CONFLICT DO NOTHING RETURNING player_id`, [r.id, r.email, r.hash, r.salt, r.name, r.age, r.avatar]);
      if (inserted.rows.length) return 'ok';
      return (await this.byId(r.id)) ? 'has-email' : 'taken';
    },
    async update(id, fields) {
      const keys = Object.keys(fields).filter((k) => Object.hasOwn(COLUMNS, k));
      if (!keys.length) return;
      await pool.query(`UPDATE naadamy_email_accounts SET ${keys.map((k, i) => `${COLUMNS[k]} = $${i + 2}`).join(', ')} WHERE player_id = $1`, [id, ...keys.map((k) => fields[k])]);
    },
    async forget() {}, // gone with the identities (ON DELETE CASCADE)
  };
}

/** Sends one mail with Resend's HTTP API; throws when it is not accepted. */
function resend(apiKey, from) {
  return async ({ to, subject, html, text }) => {
    const answer = await fetch('https://api.resend.com/emails', {
      method: 'POST', signal: AbortSignal.timeout(15000),
      headers: { authorization: `Bearer ${apiKey}`, 'content-type': 'application/json' },
      body: JSON.stringify({ from, to: [to], subject, html, text }),
    });
    await answer.body?.cancel().catch(() => {});
    if (!answer.ok) throw new Error(`Resend answered ${answer.status}`);
  };
}

module.exports = function createEmailAccounts(roomStore, { dataDir, cloudOf = async () => null, env = process.env, send, now = () => Date.now(), perMinute = PER_MINUTE } = {}) {
  const store = roomStore.kind === 'postgres' && roomStore.pool ? pgAccounts(roomStore.pool) : jsonAccounts(dataDir);
  const mailOn = Boolean(send || (env.RESEND_API_KEY && env.EMAIL_FROM));
  const deliver = send || resend(env.RESEND_API_KEY, env.EMAIL_FROM);
  const hits = new Map();
  function allow(ip) {
    const t = now(), h = hits.get(ip);
    if (!h || t - h.at > 60_000) { hits.set(ip, { at: t, n: 1 }); if (hits.size > 5000) for (const [k, v] of hits) if (t - v.at > 60_000) hits.delete(k); return true; }
    return ++h.n <= perMinute;
  }

  /** Checks the password and counts a wrong try (as accounts.js): null when right, else the refusal. */
  async function verify(account, password) {
    const t = now();
    if (account.until > t) return { ok: false, reason: 'locked', wait: Math.ceil((account.until - t) / 1000) };
    if (passwordOk(password)) {
      const hash = await hashPassword(password, account.salt);
      if (crypto.timingSafeEqual(Buffer.from(hash, 'hex'), Buffer.from(account.hash, 'hex'))) {
        if (account.fails || account.locks) await store.update(account.id, { fails: 0, locks: Math.max(0, account.locks - 1) });
        return null;
      }
    }
    const fails = account.fails + 1;
    if (fails >= TRIES) {
      const locks = account.locks + 1, until = t + Math.min(LOCK_MAX, LOCK_MS * 2 ** Math.min(20, locks - 1));
      await store.update(account.id, { fails: 0, locks, until });
      return { ok: false, reason: 'locked', wait: Math.ceil((until - t) / 1000) };
    }
    await store.update(account.id, { fails });
    return { ok: false, reason: 'wrong', left: TRIES - fails };
  }
  /** What a device logging in as the account needs: its own new ticket, and the profile and cloud save to rebuild. */
  async function loggedIn(account) {
    const ticket = crypto.randomBytes(32).toString('hex');
    await roomStore.addDeviceTicket(account.id, ticket);
    return { ok: true, id: account.id, ticket, email: account.email, name: account.name, age: account.age, avatar: account.avatar, cloud: await cloudOf(account.id) };
  }

  async function register(m) {
    const id = String(m.id || ''), ticket = String(m.ticket || ''), email = emailOf(m.email);
    if (!PLAYER_ID.test(id) || !PLAYER_TICKET.test(ticket)) return { ok: false, reason: 'invalid' };
    if (!email) return { ok: false, reason: 'bad-email' };
    if (!passwordOk(m.password) || m.password.trim().toLowerCase() === email) return { ok: false, reason: 'weak-password' };
    if (!(await roomStore.authenticatePlayer(id, ticket))) return { ok: false, reason: (await roomStore.isErased(id)) ? 'erased' : 'identity' };
    const record = await passwordRecord(m.password);
    const result = await store.create({ id, email, ...record, name: cleanName(m.name), age: cleanInt(m.age, 3, 120, 8), avatar: cleanInt(m.avatar, 0, 63, 0) });
    return result === 'ok' ? { ok: true, email } : { ok: false, reason: result };
  }
  /** Is this email still free? Asked by the first step of signing up, before the profile exists. */
  async function check(m) {
    const email = emailOf(m.email);
    if (!email) return { ok: false, reason: 'bad-email' };
    return { ok: true, available: !(await store.byEmail(email)) };
  }
  async function login(m) {
    const email = emailOf(m.email);
    if (!email || !passwordOk(m.password)) return { ok: false, reason: 'wrong' };
    const account = await store.byEmail(email);
    if (!account) return { ok: false, reason: 'wrong' };
    const refusal = await verify(account, m.password);
    return refusal || loggedIn(account);
  }
  async function me(m) {
    const id = String(m.id || ''), ticket = String(m.ticket || '');
    if (!PLAYER_ID.test(id) || !PLAYER_TICKET.test(ticket) || !(await roomStore.authenticatePlayer(id, ticket))) return { ok: false, reason: 'identity' };
    const account = await store.byId(id);
    return { ok: true, email: account ? account.email : null };
  }
  async function forgot(m) {
    const email = emailOf(m.email);
    if (!email) return { ok: false, reason: 'bad-email' };
    if (!mailOn) return { ok: false, reason: 'mail-off' };
    const account = await store.byEmail(email), t = now();
    if (!account || t - account.resetAt < RESET_GAP_MS) return { ok: true }; // the same answer either way
    const code = String(crypto.randomInt(0, 1_000_000)).padStart(6, '0');
    await store.update(account.id, { resetHash: sha(`${account.id}:${code}`), resetUntil: t + RESET_MS, resetTries: 0, resetAt: t });
    try {
      await deliver({
        to: email, subject: `Naadamy: нууц үг сэргээх код ${code}`,
        text: `Сайн байна уу!\n\nNaadamy-ийн нууц үгээ сэргээх код: ${code}\n\nКод 15 минутын дотор хүчинтэй. Та өөрөө хүсээгүй бол энэ имэйлийг үл тоомсорлоорой.`,
        html: `<p>Сайн байна уу!</p><p>Naadamy-ийн нууц үгээ сэргээх код:</p><p style="font:700 28px/1 monospace;letter-spacing:6px">${code}</p><p>Код 15 минутын дотор хүчинтэй. Та өөрөө хүсээгүй бол энэ имэйлийг үл тоомсорлоорой.</p>`,
      });
    } catch (error) {
      console.error('Email account: could not send the reset code:', error.message);
      await store.update(account.id, { resetHash: '', resetUntil: 0, resetAt: 0 });
      return { ok: false, reason: 'mail-failed' };
    }
    return { ok: true };
  }
  async function reset(m) {
    const email = emailOf(m.email), code = String(m.code || '').trim();
    if (!email || !/^\d{6}$/.test(code)) return { ok: false, reason: 'bad-code' };
    if (!passwordOk(m.password) || m.password.trim().toLowerCase() === email) return { ok: false, reason: 'weak-password' };
    const account = await store.byEmail(email), t = now();
    if (!account || !account.resetHash || account.resetUntil < t || account.resetTries >= RESET_TRIES) return { ok: false, reason: 'bad-code' };
    const given = Buffer.from(sha(`${account.id}:${code}`)), expected = Buffer.from(account.resetHash);
    if (given.length !== expected.length || !crypto.timingSafeEqual(given, expected)) {
      await store.update(account.id, { resetTries: account.resetTries + 1 });
      return { ok: false, reason: 'bad-code', left: Math.max(0, RESET_TRIES - account.resetTries - 1) };
    }
    const record = await passwordRecord(m.password);
    await store.update(account.id, { ...record, fails: 0, until: 0, resetHash: '', resetUntil: 0, resetTries: 0 });
    return loggedIn({ ...account, ...record });
  }

  const ACTS = { check, register, login, me, forgot, reset };
  function handle(req, res) {
    const json = (status, body) => { res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }); res.end(JSON.stringify(body)); };
    if (req.method !== 'POST') return json(405, { ok: false, reason: 'method' });
    let raw = '';
    req.on('data', (chunk) => { raw += chunk; if (raw.length > 4000) req.destroy(); });
    req.on('end', async () => {
      let message; try { message = JSON.parse(raw); } catch { return json(400, { ok: false, reason: 'invalid' }); }
      if (!message || typeof message !== 'object' || typeof message.act !== 'string' || !Object.hasOwn(ACTS, message.act)) return json(400, { ok: false, reason: 'invalid' });
      if (!allow(clientIp(req))) return json(429, { ok: false, reason: 'busy' });
      try { json(200, await ACTS[message.act](message)); } catch (error) { console.error('Email account:', error.message); json(500, { ok: false, reason: 'storage' }); }
    });
  }

  return { handle, init: () => store.init(), forget: (ids) => store.forget(ids), mailOn };
};
module.exports.emailOf = emailOf;
