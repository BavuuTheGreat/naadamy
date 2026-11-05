'use strict';
/* Sign in with Apple on iPhone and iPad: a player links an Apple ID to their player id, then logs in with it on any other
   Apple device, which gets its own ticket for the same id (same coins, XP, clothes, cloud save), as Google logins do.

   The game asks POST /api/apple { act: 'nonce' } for a one-time nonce, gives Apple its SHA-256 (hex) with the request,
   and sends the identity token Apple returns to { act: 'native', idToken }. The server checks the token's RS256
   signature against Apple's published keys (https://appleid.apple.com/auth/keys, cached for their max-age), the issuer,
   that it was made for this app (APPLE_BUNDLE_ID, default com.pineconeacademy.naadamy), its expiry, and that its nonce
   is the SHA-256 of one this server gave out in the last 10 minutes and not used yet.

   Apple's account number (`sub`) and the email Apple shares (often a private relay address, sometimes none) are kept
   next to the player id: naadamy_apple in PostgreSQL (gone with the identity), else apple.json in DATA_DIR; an erased
   player's link goes too. Apple gives the person's name only to the app, on the first sign-in: the game passes it on
   for a new device's profile and the server does not keep it. */

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const PLAYER_ID = /^[\w-]{4,24}$/;
const PLAYER_TICKET = /^[a-f0-9]{64}$/i;
const NONCE_MS = 10 * 60 * 1000, NONCE_MAX = 2000, PER_MINUTE = 20;
const ISSUER = 'https://appleid.apple.com';
const KEYS_URL = 'https://appleid.apple.com/auth/keys';
const clientIp = require('./client-ip.js');
const sha256hex = (text) => crypto.createHash('sha256').update(text).digest('hex');

/** An Apple identity token's claims after checking its signature with Apple's keys ({ kid: KeyObject }), issuer,
 *  audience and expiry: { sub, email, nonce }, or null. */
function verifyAppleToken(idToken, keys, audience, now) {
  const parts = String(idToken || '').split('.');
  if (parts.length !== 3) return null;
  let header, claims;
  try {
    header = JSON.parse(Buffer.from(parts[0], 'base64url').toString('utf8'));
    claims = JSON.parse(Buffer.from(parts[1], 'base64url').toString('utf8'));
  } catch { return null; }
  if (!header || header.alg !== 'RS256' || typeof header.kid !== 'string' || !Object.hasOwn(keys, header.kid)) return null;
  if (!crypto.verify('RSA-SHA256', Buffer.from(`${parts[0]}.${parts[1]}`), keys[header.kid], Buffer.from(parts[2], 'base64url'))) return null;
  if (!claims || typeof claims !== 'object' || claims.iss !== ISSUER) return null;
  const aud = Array.isArray(claims.aud) ? claims.aud : [claims.aud];
  if (!aud.includes(audience)) return null;
  if (!(Number(claims.exp) * 1000 > now)) return null;
  if (typeof claims.sub !== 'string' || !/^[\w.-]{1,255}$/.test(claims.sub)) return null;
  const verified = claims.email_verified === true || claims.email_verified === 'true';
  const email = verified && typeof claims.email === 'string' && claims.email.length <= 254 ? claims.email : '';
  return { sub: claims.sub, email, nonce: typeof claims.nonce === 'string' ? claims.nonce : '' };
}

// ---------------------------------------------------------------- where links are kept
function jsonLinks(dataDir) {
  const file = path.join(dataDir, 'apple.json'); // { [sub]: { id, email, at } }
  const read = () => { try { const v = JSON.parse(fs.readFileSync(file, 'utf8')); return v && typeof v === 'object' && !Array.isArray(v) ? v : {}; } catch { return {}; } };
  const write = (all) => { fs.mkdirSync(dataDir, { recursive: true }); fs.writeFileSync(`${file}.tmp`, JSON.stringify(all), { mode: 0o600 }); fs.renameSync(`${file}.tmp`, file); };
  const subOf = (all, id) => Object.keys(all).find((sub) => all[sub].id === id) || null;
  return {
    async init() {},
    async bySub(sub) { const all = read(); return Object.hasOwn(all, sub) ? { id: all[sub].id, email: all[sub].email } : null; },
    async byPlayer(id) { const all = read(), sub = subOf(all, id); return sub ? { sub, email: all[sub].email } : null; },
    async link(sub, id, email) {
      const all = read();
      if (Object.hasOwn(all, sub)) return all[sub].id === id ? 'ok' : 'taken';
      if (subOf(all, id)) return 'has-apple';
      Object.defineProperty(all, sub, { value: { id, email, at: Date.now() }, writable: true, configurable: true, enumerable: true });
      write(all); return 'ok';
    },
    async unlink(id) { const all = read(), sub = subOf(all, id); if (sub) { delete all[sub]; write(all); } },
    async forget(ids) { const gone = new Set(ids), all = read(); let changed = false; for (const sub of Object.keys(all)) if (gone.has(all[sub].id)) { delete all[sub]; changed = true; } if (changed) write(all); },
  };
}

function pgLinks(pool) {
  return {
    async init() {
      await pool.query(`
        CREATE TABLE IF NOT EXISTS naadamy_apple (
          sub text PRIMARY KEY,
          player_id text NOT NULL UNIQUE REFERENCES naadamy_identities (id) ON DELETE CASCADE,
          email text NOT NULL DEFAULT '',
          created_at timestamptz NOT NULL DEFAULT now()
        )`);
      await pool.query('ALTER TABLE naadamy_apple ENABLE ROW LEVEL SECURITY'); // closed to Supabase's REST API
    },
    async bySub(sub) { const r = await pool.query('SELECT player_id, email FROM naadamy_apple WHERE sub = $1', [sub]); return r.rows[0] ? { id: r.rows[0].player_id, email: r.rows[0].email } : null; },
    async byPlayer(id) { const r = await pool.query('SELECT sub, email FROM naadamy_apple WHERE player_id = $1', [id]); return r.rows[0] ? { sub: r.rows[0].sub, email: r.rows[0].email } : null; },
    async link(sub, id, email) {
      const r = await pool.query('INSERT INTO naadamy_apple (sub, player_id, email) VALUES ($1, $2, $3) ON CONFLICT DO NOTHING RETURNING sub', [sub, id, email]);
      if (r.rows.length) return 'ok';
      const holder = await this.bySub(sub);
      if (holder) return holder.id === id ? 'ok' : 'taken';
      return 'has-apple';
    },
    async unlink(id) { await pool.query('DELETE FROM naadamy_apple WHERE player_id = $1', [id]); },
    async forget() {}, // gone with the identities (ON DELETE CASCADE)
  };
}

// ---------------------------------------------------------------- the flow
module.exports = function createAppleAuth(roomStore, arenaStore, { dataDir, cloudOf = async () => null, env = process.env, fetch = globalThis.fetch, now = () => Date.now(), perMinute = PER_MINUTE } = {}) {
  const bundleId = env.APPLE_BUNDLE_ID || 'com.pineconeacademy.naadamy';
  const links = roomStore.kind === 'postgres' && roomStore.pool ? pgLinks(roomStore.pool) : jsonLinks(dataDir);
  const nonces = new Map(); // nonce -> when it was given
  let appleKeys = { keys: {}, until: 0 };
  const hits = new Map();
  function allow(ip) {
    const t = now(), h = hits.get(ip);
    if (!h || t - h.at > 60_000) { hits.set(ip, { at: t, n: 1 }); if (hits.size > 5000) for (const [k, v] of hits) if (t - v.at > 60_000) hits.delete(k); return true; }
    return ++h.n <= perMinute;
  }
  async function playerOk(id, ticket) {
    return PLAYER_ID.test(id) && PLAYER_TICKET.test(ticket) && roomStore.authenticatePlayer(id, ticket);
  }
  /** Apple's signing keys, fetched again when they run out or a token names a key not known yet. */
  async function signingKeys(kid) {
    if (appleKeys.until > now() && Object.hasOwn(appleKeys.keys, kid)) return appleKeys.keys;
    const answer = await fetch(KEYS_URL, { signal: AbortSignal.timeout(10_000) });
    const body = await answer.json().catch(() => null);
    if (!answer.ok || !body || !Array.isArray(body.keys)) throw new Error(`Apple's keys answered ${answer.status}`);
    const keys = {};
    for (const jwk of body.keys) if (jwk && jwk.kty === 'RSA' && typeof jwk.kid === 'string') { try { keys[jwk.kid] = crypto.createPublicKey({ key: jwk, format: 'jwk' }); } catch { /* skip a bad key */ } }
    const maxAge = Number((/max-age=(\d+)/.exec(answer.headers && answer.headers.get ? answer.headers.get('cache-control') || '' : '') || [])[1]) || 3600;
    appleKeys = { keys, until: now() + Math.min(maxAge, 86400) * 1000 };
    return keys;
  }

  async function nonce() {
    const t = now();
    for (const [n, at] of nonces) if (t - at > NONCE_MS) nonces.delete(n);
    if (nonces.size >= NONCE_MAX) return { ok: false, reason: 'busy' };
    const value = crypto.randomBytes(24).toString('base64url');
    nonces.set(value, t);
    return { ok: true, nonce: value };
  }

  async function native(message) {
    const mode = message.mode === 'link' ? 'link' : message.mode === 'login' ? 'login' : '';
    const idToken = String(message.idToken || '');
    if (!mode || idToken.length > 8192) return { ok: false, reason: 'invalid' };
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (mode === 'link' && !(await playerOk(id, ticket))) return { ok: false, reason: 'identity' };
    let header = null;
    try { header = JSON.parse(Buffer.from(idToken.split('.')[0], 'base64url').toString('utf8')); } catch { /* checked below */ }
    if (!header || typeof header.kid !== 'string') return { ok: false, reason: 'unverified' };
    const apple = verifyAppleToken(idToken, await signingKeys(header.kid), bundleId, now());
    if (!apple) return { ok: false, reason: 'unverified' };
    // the token carries the SHA-256 of the nonce: find the one given out that hashes to it, and use it up
    let given = null;
    for (const [n, at] of nonces) if (sha256hex(n) === apple.nonce && now() - at <= NONCE_MS) { given = n; break; }
    if (!given) return { ok: false, reason: 'expired' };
    nonces.delete(given);
    if (mode === 'link') {
      const result = await links.link(apple.sub, id, apple.email);
      return result === 'ok' ? { ok: true, status: 'done', mode: 'link', email: apple.email } : { ok: false, reason: result };
    }
    const link = await links.bySub(apple.sub);
    if (!link) return { ok: false, reason: 'not-linked' };
    if (await roomStore.isErased(link.id)) return { ok: false, reason: 'erased' };
    const deviceTicket = crypto.randomBytes(32).toString('hex'); // this device's own ticket for the player
    await roomStore.addDeviceTicket(link.id, deviceTicket);
    const account = arenaStore ? await arenaStore.accountById(link.id).catch(() => null) : null;
    const name = typeof message.name === 'string' ? message.name.replace(/[<>\u0000-\u001f]/g, '').trim().slice(0, 40) : '';
    return { ok: true, status: 'done', mode: 'login', email: link.email, id: link.id, ticket: deviceTicket, username: account ? account.username : null, name, cloud: await cloudOf(link.id) };
  }

  async function me(message) {
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (!(await playerOk(id, ticket))) return { ok: false, reason: 'identity' };
    const link = await links.byPlayer(id);
    return { ok: true, linked: Boolean(link), email: link ? link.email : null };
  }
  async function unlink(message) {
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (!(await playerOk(id, ticket))) return { ok: false, reason: 'identity' };
    await links.unlink(id);
    return { ok: true };
  }

  const ACTS = { nonce, native, me, unlink };
  function handle(req, res) {
    const json = (status, body) => { res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }); res.end(JSON.stringify(body)); };
    if (req.method !== 'POST') return json(405, { ok: false, reason: 'method' });
    let raw = '';
    req.on('data', (chunk) => { raw += chunk; if (raw.length > 12000) req.destroy(); });
    req.on('end', async () => {
      let message; try { message = JSON.parse(raw); } catch { return json(400, { ok: false, reason: 'invalid' }); }
      if (!message || typeof message !== 'object' || typeof message.act !== 'string' || !Object.hasOwn(ACTS, message.act)) return json(400, { ok: false, reason: 'invalid' });
      if (!allow(clientIp(req))) return json(429, { ok: false, reason: 'busy' });
      try { json(200, await ACTS[message.act](message)); } catch (error) { console.error('Apple sign-in:', error.message); json(500, { ok: false, reason: 'storage' }); }
    });
  }

  return { handle, init: () => links.init(), forget: (ids) => links.forget(ids) };
};
module.exports.verifyAppleToken = verifyAppleToken;
