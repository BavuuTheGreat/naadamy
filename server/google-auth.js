'use strict';
/* Sign in with Google: a player links a Google account to their player id, then logs in with it on any other device, which
   gets its own ticket for the same id (same coins, XP, clothes, cloud save), as with an arena username and password.

   One flow for every platform (WebGL, Android, Windows), with no deep links: the game asks POST /api/google { act: 'start' }
   and gets Google's sign-in address, a secret `poll` key and a short `code`. It opens the address in a browser and asks
   { act: 'poll', poll } every few seconds. Google sends the browser back to GET /api/google/callback; the server trades the
   code for the account (with its client secret and PKCE), then shows a page with the same short code and a Confirm button.
   Only after Confirm (POST /api/google/confirm) is the account linked or the device logged in, so a sign-in link someone
   else started can't quietly hand them your account: the page asks you to check the code your own game shows.

   The server keeps only Google's account number (`sub`) and the email, next to the player id: naadamy_google in PostgreSQL
   (gone with the identity), else google.json in DATA_DIR. Sign-ins in progress live in memory for 10 minutes, so this
   assumes one server instance (as on Render). Off unless GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET are set.
   The address Google sends the browser back to is PUBLIC_URL + /api/google/callback; it must be listed in the Google
   Cloud Console as an authorized redirect URI. */

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const clientIp = require('./client-ip.js');

const PLAYER_ID = /^[\w-]{4,24}$/;
const PLAYER_TICKET = /^[a-f0-9]{64}$/i;
const KEY = /^[a-f0-9]{64}$/;
const PENDING_MS = 10 * 60 * 1000, PENDING_MAX = 2000, PER_MINUTE = 20;
const CODE_LETTERS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const RENDER_URL = 'https://naadamy-server.onrender.com';

const b64url = (buffer) => buffer.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const shortCode = () => Array.from(crypto.randomBytes(4), (b) => CODE_LETTERS[b % CODE_LETTERS.length]).join('');
const escapeHtml = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

/** The claims of an ID token that came straight from Google's token endpoint over TLS (OpenID Connect Core 3.1.3.7 lets
 *  the client skip the signature then), checked for issuer, audience, expiry and a verified email. Null when not right. */
function readIdToken(idToken, clientId, now) {
  const parts = String(idToken || '').split('.');
  if (parts.length !== 3) return null;
  let claims;
  try { claims = JSON.parse(Buffer.from(parts[1].replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8')); } catch { return null; }
  if (!claims || typeof claims !== 'object') return null;
  if (claims.iss !== 'https://accounts.google.com' && claims.iss !== 'accounts.google.com') return null;
  const aud = Array.isArray(claims.aud) ? claims.aud : [claims.aud];
  if (!aud.includes(clientId)) return null;
  if (!(Number(claims.exp) * 1000 > now)) return null;
  if (typeof claims.sub !== 'string' || !/^[\w-]{1,255}$/.test(claims.sub)) return null;
  if (claims.email_verified !== true && claims.email_verified !== 'true') return null;
  if (typeof claims.email !== 'string' || claims.email.length > 254) return null;
  return { sub: claims.sub, email: claims.email, name: typeof claims.given_name === 'string' ? claims.given_name.slice(0, 40) : '' };
}

// ---------------------------------------------------------------- where links are kept
function jsonLinks(dataDir) {
  const file = path.join(dataDir, 'google.json'); // { [sub]: { id, email, at } }
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
      if (subOf(all, id)) return 'has-google';
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
        CREATE TABLE IF NOT EXISTS naadamy_google (
          sub text PRIMARY KEY,
          player_id text NOT NULL UNIQUE REFERENCES naadamy_identities (id) ON DELETE CASCADE,
          email text NOT NULL,
          created_at timestamptz NOT NULL DEFAULT now()
        )`);
      await pool.query('ALTER TABLE naadamy_google ENABLE ROW LEVEL SECURITY'); // closed to Supabase's REST API, like the other tables
    },
    async bySub(sub) { const r = await pool.query('SELECT player_id, email FROM naadamy_google WHERE sub = $1', [sub]); return r.rows[0] ? { id: r.rows[0].player_id, email: r.rows[0].email } : null; },
    async byPlayer(id) { const r = await pool.query('SELECT sub, email FROM naadamy_google WHERE player_id = $1', [id]); return r.rows[0] ? { sub: r.rows[0].sub, email: r.rows[0].email } : null; },
    async link(sub, id, email) {
      const r = await pool.query('INSERT INTO naadamy_google (sub, player_id, email) VALUES ($1, $2, $3) ON CONFLICT DO NOTHING RETURNING sub', [sub, id, email]);
      if (r.rows.length) return 'ok';
      const holder = await this.bySub(sub);
      if (holder) return holder.id === id ? 'ok' : 'taken';
      return 'has-google';
    },
    async unlink(id) { await pool.query('DELETE FROM naadamy_google WHERE player_id = $1', [id]); },
    async forget() {}, // gone with the identities (ON DELETE CASCADE)
  };
}

// ---------------------------------------------------------------- the flow
module.exports = function createGoogleAuth(roomStore, arenaStore, { dataDir, cloudOf = async () => null, env = process.env, fetch = globalThis.fetch, now = () => Date.now(), perMinute = PER_MINUTE } = {}) {
  const clientId = env.GOOGLE_CLIENT_ID || '', clientSecret = env.GOOGLE_CLIENT_SECRET || '';
  const enabled = Boolean(clientId && clientSecret);
  const base = (env.PUBLIC_URL || RENDER_URL).replace(/\/+$/, '');
  const redirectUri = `${base}/api/google/callback`;
  const links = roomStore.kind === 'postgres' && roomStore.pool ? pgLinks(roomStore.pool) : jsonLinks(dataDir);
  const byState = new Map(), byPoll = new Map();
  const hits = new Map();

  function allow(ip) {
    const t = now(), h = hits.get(ip);
    if (!h || t - h.at > 60_000) { hits.set(ip, { at: t, n: 1 }); if (hits.size > 5000) for (const [k, v] of hits) if (t - v.at > 60_000) hits.delete(k); return true; }
    return ++h.n <= perMinute;
  }
  function sweep() {
    const t = now();
    for (const [state, p] of byState) if (t - p.at > PENDING_MS) { byState.delete(state); byPoll.delete(p.poll); }
  }
  async function playerOk(id, ticket) {
    return PLAYER_ID.test(id) && PLAYER_TICKET.test(ticket) && roomStore.authenticatePlayer(id, ticket);
  }

  async function start(message) {
    if (!enabled) return { ok: false, reason: 'off' };
    const mode = message.mode === 'link' ? 'link' : message.mode === 'login' ? 'login' : '';
    if (!mode) return { ok: false, reason: 'invalid' };
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (mode === 'link' && !(await playerOk(id, ticket))) return { ok: false, reason: 'identity' };
    sweep();
    if (byState.size >= PENDING_MAX) return { ok: false, reason: 'busy' };
    const state = crypto.randomBytes(32).toString('hex'), poll = crypto.randomBytes(32).toString('hex');
    const verifier = b64url(crypto.randomBytes(48));
    const pending = { mode, id: mode === 'link' ? id : '', state, poll, verifier, code: shortCode(), nonce: crypto.randomBytes(16).toString('hex'), at: now(), status: 'waiting', google: null, result: null };
    byState.set(state, pending); byPoll.set(poll, pending);
    const url = 'https://accounts.google.com/o/oauth2/v2/auth?' + new URLSearchParams({
      client_id: clientId, redirect_uri: redirectUri, response_type: 'code', scope: 'openid email profile', state,
      code_challenge: b64url(crypto.createHash('sha256').update(verifier).digest()), code_challenge_method: 'S256', prompt: 'select_account',
    });
    return { ok: true, url, poll, code: pending.code };
  }

  async function poll(message) {
    const key = String(message.poll || '');
    if (!KEY.test(key)) return { ok: false, reason: 'invalid' };
    sweep();
    const p = byPoll.get(key);
    if (!p) return { ok: false, reason: 'expired' };
    if (p.status === 'waiting') return { ok: true, status: 'waiting' };
    byState.delete(p.state); byPoll.delete(p.poll); // a result is handed out once
    if (p.status === 'failed') return { ok: false, reason: p.reason };
    return { ok: true, status: 'done', ...p.result };
  }

  async function me(message) {
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (!(await playerOk(id, ticket))) return { ok: false, reason: 'identity' };
    const link = await links.byPlayer(id);
    return { ok: true, enabled, email: link ? link.email : null };
  }

  async function unlink(message) {
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (!(await playerOk(id, ticket))) return { ok: false, reason: 'identity' };
    await links.unlink(id);
    return { ok: true };
  }

  /** Google's code for the account: the token endpoint's ID token, read; or a reason. */
  async function exchange(code, verifier) {
    const answer = await fetch('https://oauth2.googleapis.com/token', {
      method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({ code, client_id: clientId, client_secret: clientSecret, redirect_uri: redirectUri, grant_type: 'authorization_code', code_verifier: verifier }).toString(),
      signal: AbortSignal.timeout(10_000),
    });
    const body = await answer.json().catch(() => null);
    if (!answer.ok || !body || !body.id_token) { console.error('Google sign-in: the token endpoint answered', answer.status, body && body.error); return { reason: 'google' }; }
    const google = readIdToken(body.id_token, clientId, now());
    return google ? { google } : { reason: 'unverified' };
  }

  /** What Confirm does: link the account to the player, or log this device in as the player it is linked to. */
  async function finish(p) {
    const { sub, email, name } = p.google;
    if (p.mode === 'link') {
      const result = await links.link(sub, p.id, email);
      if (result !== 'ok') return { reason: result };
      return { result: { mode: 'link', email } };
    }
    const link = await links.bySub(sub);
    if (!link) return { reason: 'not-linked' };
    if (await roomStore.isErased(link.id)) return { reason: 'erased' };
    const ticket = crypto.randomBytes(32).toString('hex'); // this device's own ticket for the player
    await roomStore.addDeviceTicket(link.id, ticket);
    const account = arenaStore ? await arenaStore.accountById(link.id).catch(() => null) : null;
    return { result: { mode: 'login', email, id: link.id, ticket, username: account ? account.username : null, name, cloud: await cloudOf(link.id) } };
  }

  // ---------------------------------------------------------------- the pages the browser sees
  const PAGE_CSP = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
  function page(res, status, title, body) {
    res.writeHead(status, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store', 'content-security-policy': PAGE_CSP });
    res.end(`<!DOCTYPE html><html lang="mn"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="robots" content="noindex"><title>Naadamy · ${escapeHtml(title)}</title>
<style>body{font:17px/1.5 system-ui,sans-serif;margin:0;padding:32px 20px;background:#f6f3ee;color:#222;text-align:center}main{max-width:420px;margin:0 auto;background:#fff;border-radius:14px;padding:28px 24px;box-shadow:0 1px 3px #0002}
h1{font-size:22px;margin:0 0 12px}.code{font:700 34px/1 ui-monospace,monospace;letter-spacing:6px;background:#f1ece2;border-radius:10px;padding:14px;margin:16px 0}
button{font:inherit;font-weight:600;border:0;border-radius:10px;padding:12px 18px;margin:6px;cursor:pointer}.yes{background:#2e7d4f;color:#fff}.no{background:#e7e2d8;color:#222}.muted{color:#666;font-size:15px}</style></head>
<body><main>${body}</main></body></html>`);
  }
  const done = (res, text) => page(res, 200, 'Google', `<h1>${escapeHtml(text)}</h1><p class="muted">Энэ хуудсыг хаагаад тоглоом руугаа буцна уу.</p>`);
  const failed = (res, text) => page(res, 400, 'Google', `<h1>Амжилтгүй боллоо</h1><p>${escapeHtml(text)}</p><p class="muted">Тоглоом руугаа буцаад дахин оролдоно уу.</p>`);
  const fail = (p, reason) => { p.status = 'failed'; p.reason = reason; };

  async function callback(req, res, url) {
    sweep();
    const p = byState.get(url.searchParams.get('state') || '');
    if (!p || p.status !== 'waiting' || p.google) return failed(res, 'Энэ холбоос хүчингүй эсвэл хугацаа нь дууссан байна.');
    if (url.searchParams.get('error') || !url.searchParams.get('code')) { fail(p, 'cancelled'); return failed(res, 'Google нэвтрэлт цуцлагдлаа.'); }
    const { google, reason } = await exchange(url.searchParams.get('code'), p.verifier);
    if (!google) { fail(p, reason); return failed(res, 'Google бүртгэлийг шалгаж чадсангүй.'); }
    p.google = google;
    const what = p.mode === 'link' ? 'Энэ Google бүртгэлийг Naadamy-ийн дүртэйгээ холбох уу?' : 'Энэ Google бүртгэлээр Naadamy-д нэвтрэх үү?';
    return page(res, 200, 'Google', `<h1>${what}</h1><p><b>${escapeHtml(google.email)}</b></p>
<p>Тоглоом дээр чинь яг ийм код харагдаж байгаа эсэхийг шалгаарай:</p><div class="code">${p.code}</div>
<p class="muted">Код таарахгүй эсвэл та өөрөө эхлүүлээгүй бол «Үгүй» дарна уу.</p>
<form method="post" action="/api/google/confirm"><input type="hidden" name="state" value="${p.state}"><input type="hidden" name="nonce" value="${p.nonce}">
<button class="yes" name="answer" value="yes">Тийм, зөвшөөрөх</button><button class="no" name="answer" value="no">Үгүй</button></form>`);
  }

  function confirm(req, res) {
    let raw = '';
    req.on('data', (chunk) => { raw += chunk; if (raw.length > 2000) req.destroy(); });
    req.on('end', async () => {
      const form = new URLSearchParams(raw);
      const p = byState.get(form.get('state') || '');
      if (!p || p.status !== 'waiting' || !p.google) return failed(res, 'Энэ холбоос хүчингүй эсвэл хугацаа нь дууссан байна.');
      const nonce = Buffer.from(form.get('nonce') || ''), expected = Buffer.from(p.nonce);
      if (nonce.length !== expected.length || !crypto.timingSafeEqual(nonce, expected)) return failed(res, 'Энэ холбоос хүчингүй байна.');
      if (form.get('answer') !== 'yes') { fail(p, 'cancelled'); return done(res, 'Цуцаллаа.'); }
      try {
        const { result, reason } = await finish(p);
        if (!result) {
          fail(p, reason);
          return failed(res, { taken: 'Энэ Google бүртгэл өөр дүртэй холбогдсон байна.', 'has-google': 'Энэ дүр өөр Google бүртгэлтэй холбогдсон байна.',
            'not-linked': 'Энэ Google бүртгэл ямар ч дүртэй холбогдоогүй байна. Эхлээд тоглоомын Тохиргооноос холбоно уу.', erased: 'Энэ дүрийг устгасан байна.' }[reason] || 'Алдаа гарлаа.');
        }
        p.status = 'done'; p.result = result;
        return done(res, result.mode === 'link' ? 'Google бүртгэл холбогдлоо!' : 'Нэвтэрлээ!');
      } catch (error) {
        console.error('Google sign-in:', error.message);
        fail(p, 'storage');
        return failed(res, 'Серверийн алдаа гарлаа.');
      }
    });
  }

  const ACTS = { start, poll, me, unlink };
  /** Answers /api/google, /api/google/callback and /api/google/confirm; false for any other path. */
  function handle(req, res, url) {
    const json = (status, body) => { res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }); res.end(JSON.stringify(body)); };
    if (url.pathname === '/api/google/callback') {
      if (req.method !== 'GET') { json(405, { ok: false, reason: 'method' }); return true; }
      if (!allow(clientIp(req))) { failed(res, 'Хэт олон оролдлого. Түр хүлээгээд дахин оролдоно уу.'); return true; }
      callback(req, res, url).catch((error) => { console.error('Google sign-in:', error.message); failed(res, 'Серверийн алдаа гарлаа.'); });
      return true;
    }
    if (url.pathname === '/api/google/confirm') {
      if (req.method !== 'POST') { json(405, { ok: false, reason: 'method' }); return true; }
      confirm(req, res);
      return true;
    }
    if (url.pathname !== '/api/google') return false;
    if (req.method !== 'POST') { json(405, { ok: false, reason: 'method' }); return true; }
    let raw = '';
    req.on('data', (chunk) => { raw += chunk; if (raw.length > 4000) req.destroy(); });
    req.on('end', async () => {
      let message; try { message = JSON.parse(raw); } catch { return json(400, { ok: false, reason: 'invalid' }); }
      if (!message || typeof message !== 'object' || typeof message.act !== 'string' || !Object.hasOwn(ACTS, message.act)) return json(400, { ok: false, reason: 'invalid' });
      if (message.act !== 'poll' && !allow(clientIp(req))) return json(429, { ok: false, reason: 'busy' }); // polling every few seconds is expected
      try { json(200, await ACTS[message.act](message)); } catch (error) { console.error('Google sign-in:', error.message); json(500, { ok: false, reason: 'storage' }); }
    });
    return true;
  }

  return { handle, init: () => links.init(), forget: (ids) => links.forget(ids), enabled };
};
module.exports.readIdToken = readIdToken;
