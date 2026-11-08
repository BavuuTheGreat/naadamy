// Email accounts: register, the profile steps, log in on another device, wrong passwords, an older save linking an email, and
// 2 players per account. Everything runs for real over HTTP with the JSON store.
import assert from 'node:assert/strict';
import http from 'node:http';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import roomStoreModule from '../server/room-store.js';
import createEmailAuth from '../server/email-auth.js';

const { createRoomStore } = roomStoreModule;
const dataDir = mkdtempSync(join(tmpdir(), 'naadamy-email-auth-'));
const store = createRoomStore({ dataDir, databaseUrl: '' });
let t = 1_000_000;
const auth = createEmailAuth(store, { dataDir, now: () => t, perMinute: 1000, cloudOf: async (id) => ({ for: id }) });
await auth.init();
const server = http.createServer((req, res) => auth.handle(req, res));
await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const api = async (body) => (await fetch(`http://127.0.0.1:${server.address().port}/`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) })).json();
const email = 'Bat@Example.com', password = 'long-enough';

try {
  assert.deepEqual(await api({ act: 'register', email: 'nope', password }), { ok: false, reason: 'email-invalid' });
  assert.deepEqual(await api({ act: 'register', email, password: 'short' }), { ok: false, reason: 'weak-password' });
  assert.deepEqual(await api({ act: 'login', email, password }), { ok: false, reason: 'wrong' });
  assert.deepEqual(await api({ act: 'signup', email, password }), { ok: false, reason: 'invalid' }, 'the mailed-code sign-up is gone');

  const made = await api({ act: 'register', email, password });
  assert.equal(made.ok, true); assert.equal(made.ready, false);
  assert.equal(await store.authenticatePlayer(made.id, made.ticket), true);
  assert.deepEqual(await api({ act: 'register', email: 'bat@example.com', password }), { ok: false, reason: 'taken' });

  // left before the steps: a login says so and the steps are asked again
  const early = await api({ act: 'login', email, password });
  assert.equal(early.id, made.id); assert.equal(early.ready, false);

  const me = { id: made.id, ticket: made.ticket };
  assert.deepEqual(await api({ act: 'profile', ...me, name: 'Бат', age: 2, avatar: 3 }), { ok: false, reason: 'invalid' });
  assert.deepEqual(await api({ act: 'profile', id: made.id, ticket: 'a'.repeat(64), name: 'Бат', age: 9, avatar: 3 }), { ok: false, reason: 'identity' });
  assert.deepEqual(await api({ act: 'profile', ...me, name: ' <Бат> ', age: 9, avatar: 3 }), { ok: true });

  const other = await api({ act: 'login', email: 'bat@example.com', password });
  assert.deepEqual([other.ok, other.id, other.ready, other.name, other.age, other.avatar], [true, made.id, true, 'Бат', 9, 3]);
  assert.notEqual(other.ticket, made.ticket);
  assert.equal(await store.authenticatePlayer(other.id, other.ticket), true);
  assert.deepEqual(other.cloud, { for: made.id });

  // wrong passwords lock the account, and time opens it
  for (let left = 4; left > 0; left--) assert.deepEqual(await api({ act: 'login', email, password: 'wrong-password' }), { ok: false, reason: 'wrong', left });
  assert.deepEqual(await api({ act: 'login', email, password: 'wrong-password' }), { ok: false, reason: 'locked', wait: 900 });
  assert.equal((await api({ act: 'login', email, password })).reason, 'locked');
  t += 15 * 60 * 1000;
  assert.equal((await api({ act: 'login', email, password })).ok, true);

  // an older save with no email links one and stays the same player
  const guest = { id: 'guest-player', ticket: 'a'.repeat(64) }, guestMail = 'guest@example.com';
  assert.equal(await store.authenticatePlayer(guest.id, guest.ticket), true);
  assert.deepEqual(await api({ act: 'register', email: guestMail, password, id: guest.id, ticket: 'b'.repeat(64) }), { ok: false, reason: 'identity' });
  assert.deepEqual(await api({ act: 'register', email: guestMail, password, ...guest }), { ok: true, ...guest, ready: false });
  assert.deepEqual(await api({ act: 'profile', ...guest, name: 'Guest', age: 8, avatar: 1 }), { ok: true });
  const elsewhere = await api({ act: 'login', email: guestMail, password });
  assert.equal(elsewhere.id, guest.id, 'the other device gets the guest player');
  assert.equal(elsewhere.name, 'Guest');
  assert.deepEqual(await api({ act: 'register', email: 'second@example.com', password, ...guest }), { ok: false, reason: 'taken' }, 'a player has one email');

  // an account holds 2 players: the first adds the second, and login brings both
  const twin = { name: 'Twin', age: 8, avatar: 2 };
  assert.deepEqual(await api({ act: 'add', id: guest.id, ticket: 'b'.repeat(64), ...twin }), { ok: false, reason: 'identity' });
  assert.deepEqual(await api({ act: 'add', ...guest, name: '', age: 8, avatar: 2 }), { ok: false, reason: 'invalid' });
  const added = await api({ act: 'add', ...guest, ...twin });
  assert.equal(added.ok, true);
  assert.notEqual(added.id, guest.id);
  assert.deepEqual(await api({ act: 'add', ...guest, ...twin }), { ok: false, reason: 'full' }, 'a third player is refused');
  assert.deepEqual(await api({ act: 'add', id: added.id, ticket: added.ticket, ...twin }), { ok: false, reason: 'identity' }, 'only the first player adds');
  const both = await api({ act: 'login', email: guestMail, password });
  assert.equal(both.id, guest.id);
  assert.equal(both.second.id, added.id);
  assert.equal(both.second.name, 'Twin');
  assert.deepEqual(both.second.cloud, { for: added.id });
  assert.equal(await store.authenticatePlayer(added.id, both.second.ticket), true, 'the new device has its own ticket for the second player');
  await auth.forget([added.id]);
  assert.equal((await api({ act: 'login', email: guestMail, password })).second, undefined);

  await auth.forget([made.id]);
  assert.deepEqual(await api({ act: 'login', email, password }), { ok: false, reason: 'wrong' });
  console.log('email-auth: all passed');
} finally {
  server.close();
  await store.close?.();
  rmSync(dataDir, { recursive: true, force: true });
}
