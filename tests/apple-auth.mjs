// Sign in with Apple: nonce, a token signed with a key served as Apple's, link, login on another device, and refusals.
// Apple's key endpoint is faked with a real RSA key; everything else runs over HTTP with the JSON store.
import assert from 'node:assert/strict';
import http from 'node:http';
import { createHash, generateKeyPairSync, randomBytes, sign } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import roomStoreModule from '../server/room-store.js';
import createAppleAuth from '../server/apple-auth.js';

const { createRoomStore } = roomStoreModule;
const dataDir = mkdtempSync(join(tmpdir(), 'naadamy-apple-'));
const BUNDLE = 'com.pineconeacademy.naadamy', KID = 'apple-test-1';
const { publicKey, privateKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
const b64 = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
const hashed = (n) => createHash('sha256').update(n).digest('hex');
const token = (claims, key = privateKey, kid = KID) => {
  const head = b64({ alg: 'RS256', kid }), body = b64({ iss: 'https://appleid.apple.com', aud: BUNDLE, exp: Math.floor(Date.now() / 1000) + 600, ...claims });
  return `${head}.${body}.${sign('RSA-SHA256', Buffer.from(`${head}.${body}`), key).toString('base64url')}`;
};
let keysFetched = 0;
async function fakeFetch(url) {
  assert.equal(url, 'https://appleid.apple.com/auth/keys');
  keysFetched++;
  return { ok: true, status: 200, headers: { get: () => 'max-age=3600' }, json: async () => ({ keys: [{ ...publicKey.export({ format: 'jwk' }), kid: KID, alg: 'RS256', use: 'sig' }] }) };
}

const store = createRoomStore({ dataDir, databaseUrl: '' });
const apple = createAppleAuth(store, null, { dataDir, env: {}, fetch: fakeFetch, perMinute: 1000, cloudOf: async (id) => ({ eth: 'khalkh', for: id }) });
await apple.init();
const server = http.createServer((req, res) => apple.handle(req, res));
await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const api = async (body) => (await fetch(`http://127.0.0.1:${server.address().port}/api/apple`, { method: 'POST', body: JSON.stringify(body) })).json();
const freshNonce = async () => (await api({ act: 'nonce' })).nonce;

try {
  const kid = { id: 'kid-0001', ticket: randomBytes(32).toString('hex') }, other = { id: 'kid-0002', ticket: randomBytes(32).toString('hex') };
  assert.ok(await store.authenticatePlayer(kid.id, kid.ticket));
  assert.ok(await store.authenticatePlayer(other.id, other.ticket));
  assert.deepEqual(await api({ act: 'me', ...kid }), { ok: true, linked: false, email: null });

  // link: the token carries the SHA-256 of a nonce this server gave out
  const n1 = await freshNonce();
  const linked = await api({ act: 'native', mode: 'link', ...kid, idToken: token({ sub: '001.abc', email: 'x1@privaterelay.appleid.com', email_verified: 'true', nonce: hashed(n1) }) });
  assert.deepEqual(linked, { ok: true, status: 'done', mode: 'link', email: 'x1@privaterelay.appleid.com' });
  assert.deepEqual(await api({ act: 'me', ...kid }), { ok: true, linked: true, email: 'x1@privaterelay.appleid.com' });
  assert.equal((await api({ act: 'native', mode: 'login', idToken: token({ sub: '001.abc', nonce: hashed(n1) }) })).reason, 'expired', 'a nonce works once');

  // log in on another device (Apple gives no email after the first time): a fresh ticket for the same player
  const login = await api({ act: 'native', mode: 'login', name: 'Сараа<b>', idToken: token({ sub: '001.abc', nonce: hashed(await freshNonce()) }) });
  assert.equal(login.status, 'done');
  assert.equal(login.id, kid.id);
  assert.equal(login.name, 'Сарааb');
  assert.deepEqual(login.cloud, { eth: 'khalkh', for: kid.id });
  assert.ok(await store.authenticatePlayer(kid.id, login.ticket), 'the new device ticket works');
  assert.equal(keysFetched, 1, "Apple's keys are kept");

  // one Apple ID per player, one player per Apple ID
  assert.equal((await api({ act: 'native', mode: 'link', ...other, idToken: token({ sub: '001.abc', nonce: hashed(await freshNonce()) }) })).reason, 'taken');
  assert.equal((await api({ act: 'native', mode: 'link', ...kid, idToken: token({ sub: '002.def', nonce: hashed(await freshNonce()) }) })).reason, 'has-apple');

  // refused: a raw (unhashed) nonce, a made-up one, another key, another app, an expired token, a bad token, no ticket
  const n2 = await freshNonce();
  assert.equal((await api({ act: 'native', mode: 'login', idToken: token({ sub: '001.abc', nonce: n2 }) })).reason, 'expired');
  assert.equal((await api({ act: 'native', mode: 'login', idToken: token({ sub: '001.abc', nonce: hashed('made-up') }) })).reason, 'expired');
  const forger = generateKeyPairSync('rsa', { modulusLength: 2048 }).privateKey;
  assert.equal((await api({ act: 'native', mode: 'login', idToken: token({ sub: '001.abc', nonce: hashed(n2) }, forger) })).reason, 'unverified');
  assert.equal((await api({ act: 'native', mode: 'login', idToken: token({ sub: '001.abc', nonce: hashed(n2), aud: 'com.other.app' }) })).reason, 'unverified');
  assert.equal((await api({ act: 'native', mode: 'login', idToken: token({ sub: '001.abc', nonce: hashed(n2), exp: 1 }) })).reason, 'unverified');
  assert.equal((await api({ act: 'native', mode: 'login', idToken: token({ sub: '001.abc', nonce: hashed(n2), iss: 'https://evil.example' }) })).reason, 'unverified');
  assert.equal((await api({ act: 'native', mode: 'login', idToken: 'nope' })).reason, 'unverified');
  assert.equal((await api({ act: 'native', mode: 'link', id: kid.id, ticket: randomBytes(32).toString('hex'), idToken: token({ sub: '001.abc', nonce: hashed(n2) }) })).reason, 'identity');
  // an unverified email is not kept
  const n3 = await freshNonce();
  assert.deepEqual(await api({ act: 'native', mode: 'link', ...other, idToken: token({ sub: '003.ghi', email: 'z@example.com', email_verified: false, nonce: hashed(n3) }) }),
    { ok: true, status: 'done', mode: 'link', email: '' });

  // an Apple ID linked to nobody, then unlink and erase
  assert.equal((await api({ act: 'native', mode: 'login', idToken: token({ sub: '009.zzz', nonce: hashed(await freshNonce()) }) })).reason, 'not-linked');
  assert.equal((await api({ act: 'unlink', ...kid })).ok, true);
  assert.equal((await api({ act: 'native', mode: 'login', idToken: token({ sub: '001.abc', nonce: hashed(await freshNonce()) }) })).reason, 'not-linked');
  await apple.forget([other.id]);
  assert.equal((await api({ act: 'native', mode: 'login', idToken: token({ sub: '003.ghi', nonce: hashed(await freshNonce()) }) })).reason, 'not-linked');

  console.log('apple-auth: ok');
} finally {
  server.close();
  rmSync(dataDir, { recursive: true, force: true });
}
