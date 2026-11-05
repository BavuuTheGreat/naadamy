// Sign in with Google: link, log in on another device, the confirm page and its code, refusals, and off without a client id.
// Google's token endpoint is faked; everything else runs for real over HTTP with the JSON store.
import assert from 'node:assert/strict';
import http from 'node:http';
import { randomBytes } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import roomStoreModule from '../server/room-store.js';
import createGoogleAuth from '../server/google-auth.js';

const { createRoomStore } = roomStoreModule;
const dataDir = mkdtempSync(join(tmpdir(), 'naadamy-google-'));
const CLIENT_ID = 'test-client.apps.googleusercontent.com';
const b64 = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
const idToken = (claims) => `${b64({ alg: 'RS256' })}.${b64({ iss: 'https://accounts.google.com', aud: CLIENT_ID, exp: Math.floor(Date.now() / 1000) + 600, email_verified: true, ...claims })}.sig`;

// the Google account each authorization code stands for, as the fake token endpoint answers
const accounts = new Map();
let lastTokenRequest = null;
async function fakeFetch(url, options) {
  assert.equal(url, 'https://oauth2.googleapis.com/token');
  const form = new URLSearchParams(options.body);
  lastTokenRequest = form;
  const claims = accounts.get(form.get('code'));
  if (!claims) return { ok: false, status: 400, json: async () => ({ error: 'invalid_grant' }) };
  return { ok: true, status: 200, json: async () => ({ id_token: idToken(claims) }) };
}

const store = createRoomStore({ dataDir, databaseUrl: '' });
const env = { GOOGLE_CLIENT_ID: CLIENT_ID, GOOGLE_CLIENT_SECRET: 'shh', PUBLIC_URL: 'https://naadamy.example' };
const google = createGoogleAuth(store, null, { dataDir, env, fetch: fakeFetch, perMinute: 1000, cloudOf: async (id) => ({ eth: 'khalkh', for: id }) });
await google.init();
const server = http.createServer((req, res) => { if (!google.handle(req, res, new URL(req.url, 'http://x'))) { res.writeHead(404); res.end(); } });
await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const base = `http://127.0.0.1:${server.address().port}`;
const api = async (body) => (await fetch(`${base}/api/google`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) })).json();

/** Runs one sign-in through Google's page: start, callback with `code`, then Confirm (or `answer`). Returns what each step gave. */
async function signIn(start, googleCode, answer = 'yes') {
  const begun = await api({ act: 'start', ...start });
  if (!begun.ok) return { begun };
  const auth = new URL(begun.url);
  assert.equal(auth.origin + auth.pathname, 'https://accounts.google.com/o/oauth2/v2/auth');
  assert.equal(auth.searchParams.get('redirect_uri'), 'https://naadamy.example/api/google/callback');
  assert.equal(auth.searchParams.get('code_challenge_method'), 'S256');
  assert.equal((await api({ act: 'poll', poll: begun.poll })).status, 'waiting');
  const state = auth.searchParams.get('state');
  const callback = await fetch(`${base}/api/google/callback?state=${state}&code=${googleCode}`);
  const html = await callback.text();
  if (callback.status !== 200) return { begun, callback: callback.status, html, polled: await api({ act: 'poll', poll: begun.poll }) };
  assert.ok(html.includes(begun.code), 'the page shows the code the game shows');
  assert.ok(callback.headers.get('content-security-policy'));
  const nonce = /name="nonce" value="([a-f0-9]+)"/.exec(html)[1];
  const confirmed = await fetch(`${base}/api/google/confirm`, { method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams({ state, nonce, answer }).toString() });
  return { begun, html, confirmed: confirmed.status, page: await confirmed.text(), polled: await api({ act: 'poll', poll: begun.poll }) };
}

try {
  const kid = { id: 'kid-0001', ticket: randomBytes(32).toString('hex') }, other = { id: 'kid-0002', ticket: randomBytes(32).toString('hex') };
  assert.ok(await store.authenticatePlayer(kid.id, kid.ticket));
  assert.ok(await store.authenticatePlayer(other.id, other.ticket));
  accounts.set('code-bat', { sub: '1001', email: 'bat@gmail.com', given_name: 'Бат' });
  accounts.set('code-sara', { sub: '1002', email: 'sara@gmail.com', given_name: 'Сараа' });

  // nothing linked yet
  assert.deepEqual(await api({ act: 'me', ...kid }), { ok: true, enabled: true, email: null });
  // linking needs the player's own ticket
  assert.equal((await api({ act: 'start', mode: 'link', id: kid.id, ticket: randomBytes(32).toString('hex') })).reason, 'identity');

  // link: Confirm on the page, then the game's poll hears it once
  const linked = await signIn({ mode: 'link', ...kid }, 'code-bat');
  assert.equal(linked.confirmed, 200);
  assert.deepEqual(linked.polled, { ok: true, status: 'done', mode: 'link', email: 'bat@gmail.com' });
  assert.equal((await api({ act: 'poll', poll: linked.begun.poll })).reason, 'expired', 'a result is handed out once');
  assert.equal(lastTokenRequest.get('client_secret'), 'shh');
  assert.ok(lastTokenRequest.get('code_verifier').length >= 43, 'PKCE verifier sent');
  assert.equal((await api({ act: 'me', ...kid })).email, 'bat@gmail.com');

  // the same Google account can't be linked to a second player, and a player keeps one Google account
  assert.equal((await signIn({ mode: 'link', ...other }, 'code-bat')).polled.reason, 'taken');
  assert.equal((await signIn({ mode: 'link', ...kid }, 'code-sara')).polled.reason, 'has-google');

  // log in on a new device: a fresh ticket for the same player, which the server accepts
  const login = await signIn({ mode: 'login' }, 'code-bat');
  assert.equal(login.polled.status, 'done');
  assert.equal(login.polled.id, kid.id);
  assert.match(login.polled.ticket, /^[a-f0-9]{64}$/);
  assert.notEqual(login.polled.ticket, kid.ticket);
  assert.equal(login.polled.name, 'Бат');
  assert.deepEqual(login.polled.cloud, { eth: 'khalkh', for: kid.id });
  assert.ok(await store.authenticatePlayer(kid.id, login.polled.ticket), 'the new device ticket works');

  // "No" on the page: nothing happens, the game hears it was cancelled
  const refused = await signIn({ mode: 'login' }, 'code-bat', 'no');
  assert.equal(refused.polled.reason, 'cancelled');

  // a Google account linked to nobody can't log in
  assert.equal((await signIn({ mode: 'login' }, 'code-sara')).polled.reason, 'not-linked');

  // Google refusing the code, and a token for another app: the page says no, the poll gives the reason
  const badCode = await signIn({ mode: 'login' }, 'code-nobody');
  assert.equal(badCode.callback, 400);
  assert.equal(badCode.polled.reason, 'google');
  accounts.set('code-wrong-aud', { sub: '1003', email: 'x@gmail.com', aud: 'someone-else' });
  assert.equal((await signIn({ mode: 'login' }, 'code-wrong-aud')).polled.reason, 'unverified');
  accounts.set('code-unverified', { sub: '1004', email: 'y@gmail.com', email_verified: false });
  assert.equal((await signIn({ mode: 'login' }, 'code-unverified')).polled.reason, 'unverified');

  // an unknown state, a forged nonce
  assert.equal((await fetch(`${base}/api/google/callback?state=nope&code=code-bat`)).status, 400);
  const forged = await api({ act: 'start', mode: 'login' });
  const forgedState = new URL(forged.url).searchParams.get('state');
  await (await fetch(`${base}/api/google/callback?state=${forgedState}&code=code-bat`)).text();
  const bad = await fetch(`${base}/api/google/confirm`, { method: 'POST', body: new URLSearchParams({ state: forgedState, nonce: 'a'.repeat(32), answer: 'yes' }).toString() });
  assert.equal(bad.status, 400);
  assert.equal((await api({ act: 'poll', poll: forged.poll })).status, 'waiting', 'a forged confirm changes nothing');

  // unlink, and an erased player's link goes with them
  assert.equal((await api({ act: 'unlink', ...kid })).ok, true);
  assert.equal((await api({ act: 'me', ...kid })).email, null);
  assert.equal((await signIn({ mode: 'link', ...other }, 'code-sara')).polled.status, 'done');
  await google.forget([other.id]);
  assert.equal((await signIn({ mode: 'login' }, 'code-sara')).polled.reason, 'not-linked');

  // the per-address limit on starting sign-ins
  const limited = createGoogleAuth(store, null, { dataDir, env, fetch: fakeFetch, perMinute: 2 });
  const limitedServer = http.createServer((req, res) => limited.handle(req, res, new URL(req.url, 'http://x')));
  await new Promise((resolve) => limitedServer.listen(0, '127.0.0.1', resolve));
  const tryStart = async () => (await fetch(`http://127.0.0.1:${limitedServer.address().port}/api/google`, { method: 'POST', body: JSON.stringify({ act: 'start', mode: 'login' }) })).status;
  assert.deepEqual([await tryStart(), await tryStart(), await tryStart()], [200, 200, 429]);
  limitedServer.close();

  // without a client id and secret it is off
  const off = createGoogleAuth(store, null, { dataDir, env: {}, fetch: fakeFetch });
  const offServer = http.createServer((req, res) => off.handle(req, res, new URL(req.url, 'http://x')));
  await new Promise((resolve) => offServer.listen(0, '127.0.0.1', resolve));
  const offAnswer = await (await fetch(`http://127.0.0.1:${offServer.address().port}/api/google`, { method: 'POST', body: JSON.stringify({ act: 'start', mode: 'login' }) })).json();
  assert.equal(offAnswer.reason, 'off');
  offServer.close();

  console.log('google-auth: ok');
} finally {
  server.close();
  rmSync(dataDir, { recursive: true, force: true });
}
