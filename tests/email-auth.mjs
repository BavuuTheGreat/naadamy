// Email accounts: sign up with a mailed code, the profile steps, log in on another device, wrong codes and passwords, off in
// production without a mail service. The mail is faked; everything else runs for real over HTTP with the JSON store.
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
const mails = [];
let t = 1_000_000;
const auth = createEmailAuth(store, { dataDir, env: {}, now: () => t, perMinute: 1000, send: async (mail) => { mails.push(mail); }, cloudOf: async (id) => ({ for: id }) });
await auth.init();
const server = http.createServer((req, res) => auth.handle(req, res));
await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const api = async (body) => (await fetch(`http://127.0.0.1:${server.address().port}/`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) })).json();
const email = 'Bat@Example.com', password = 'long-enough';
const lastCode = () => mails.at(-1).code;
const wrongCode = () => (lastCode() === '000000' ? '000001' : '000000');

try {
  assert.deepEqual(await api({ act: 'signup', email: 'nope', password }), { ok: false, reason: 'email-invalid' });
  assert.deepEqual(await api({ act: 'signup', email, password: 'short' }), { ok: false, reason: 'weak-password' });
  assert.deepEqual(await api({ act: 'login', email, password }), { ok: false, reason: 'wrong' });

  assert.deepEqual(await api({ act: 'signup', email, password }), { ok: true });
  assert.equal(mails.at(-1).to, 'bat@example.com');
  assert.match(mails.at(-1).text, new RegExp(lastCode()));
  assert.deepEqual(await api({ act: 'login', email, password }), { ok: false, reason: 'wrong' }, 'no account before the code');
  assert.deepEqual(await api({ act: 'verify', email, code: wrongCode() }), { ok: false, reason: 'code', left: 4 });

  const made = await api({ act: 'verify', email, code: lastCode() });
  assert.equal(made.ok, true); assert.equal(made.ready, false);
  assert.equal(await store.authenticatePlayer(made.id, made.ticket), true);
  assert.deepEqual(await api({ act: 'verify', email, code: lastCode() }), { ok: false, reason: 'expired' }, 'a code works once');
  assert.deepEqual(await api({ act: 'signup', email, password }), { ok: false, reason: 'taken' });

  // left before the 4 steps: a login says so and the steps are asked again
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

  // five wrong codes end a code; a code ends after 15 minutes; 3 mails an hour
  const kid = 'kid@example.com';
  assert.equal((await api({ act: 'signup', email: kid, password })).ok, true);
  for (let i = 0; i < 4; i++) assert.equal((await api({ act: 'verify', email: kid, code: wrongCode() })).reason, 'code');
  assert.equal((await api({ act: 'verify', email: kid, code: wrongCode() })).reason, 'expired');
  assert.equal((await api({ act: 'verify', email: kid, code: lastCode() })).reason, 'expired');
  assert.equal((await api({ act: 'signup', email: kid, password })).ok, true);
  t += 15 * 60 * 1000 + 1;
  assert.equal((await api({ act: 'verify', email: kid, code: lastCode() })).reason, 'expired');
  assert.equal((await api({ act: 'signup', email: kid, password })).ok, true);
  assert.deepEqual(await api({ act: 'signup', email: kid, password }), { ok: false, reason: 'busy' });

  // a mail that fails leaves nothing to verify
  const failing = createEmailAuth(store, { dataDir, env: {}, send: async () => { throw new Error('refused'); } });
  const off = createEmailAuth(store, { dataDir, env: { RENDER: 'true' } });
  assert.equal(failing.enabled, true); assert.equal(off.enabled, false);
  assert.equal(createEmailAuth(store, { dataDir, env: {} }).enabled, true, 'a home server prints the code to its log');

  // a guest made on the device links an email and stays the same player
  const guest = { id: 'guest-player', ticket: 'a'.repeat(64) }, guestMail = 'guest@example.com';
  assert.equal(await store.authenticatePlayer(guest.id, guest.ticket), true);
  assert.deepEqual(await api({ act: 'signup', email: guestMail, password }), { ok: true });
  assert.deepEqual(await api({ act: 'verify', email: guestMail, code: lastCode(), id: guest.id, ticket: 'b'.repeat(64) }), { ok: false, reason: 'identity' });
  assert.deepEqual(await api({ act: 'verify', email: guestMail, code: lastCode(), ...guest }), { ok: true, ...guest, ready: false });
  assert.deepEqual(await api({ act: 'profile', ...guest, name: 'Guest', age: 8, avatar: 1 }), { ok: true });
  const elsewhere = await api({ act: 'login', email: guestMail, password });
  assert.equal(elsewhere.id, guest.id, 'the other device gets the guest player');
  assert.equal(elsewhere.name, 'Guest');
  assert.deepEqual(await api({ act: 'signup', email: 'second@example.com', password }), { ok: true });
  assert.deepEqual(await api({ act: 'verify', email: 'second@example.com', code: lastCode(), ...guest }), { ok: false, reason: 'taken' });

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

  // register: the account at once, with no mail, also where mail is off
  const sent = mails.length;
  assert.deepEqual(await api({ act: 'register', email: 'nope', password }), { ok: false, reason: 'email-invalid' });
  assert.deepEqual(await api({ act: 'register', email: 'quick@example.com', password: 'short' }), { ok: false, reason: 'weak-password' });
  const quick = await api({ act: 'register', email: 'Quick@Example.com', password });
  assert.equal(quick.ok, true);
  assert.equal(quick.ready, false);
  assert.deepEqual(await api({ act: 'register', email: 'quick@example.com', password }), { ok: false, reason: 'taken' });
  assert.equal((await api({ act: 'login', email: 'quick@example.com', password })).id, quick.id);
  const walker = { id: 'guest-walker', ticket: 'c'.repeat(64) };
  assert.equal(await store.authenticatePlayer(walker.id, walker.ticket), true);
  assert.deepEqual(await api({ act: 'register', email: 'walker@example.com', password, id: walker.id, ticket: 'd'.repeat(64) }), { ok: false, reason: 'identity' });
  assert.deepEqual(await api({ act: 'register', email: 'walker@example.com', password, ...walker }), { ok: true, ...walker, ready: false });
  assert.deepEqual(await api({ act: 'register', email: 'walker2@example.com', password, ...walker }), { ok: false, reason: 'taken' }, 'a player has one email');
  assert.equal(mails.length, sent, 'nothing was mailed');

  await auth.forget([made.id]);
  assert.deepEqual(await api({ act: 'login', email, password }), { ok: false, reason: 'wrong' });
  console.log('email-auth: all passed');
} finally {
  server.close();
  await store.close?.();
  rmSync(dataDir, { recursive: true, force: true });
}
