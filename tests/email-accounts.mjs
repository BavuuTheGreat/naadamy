// Email and password accounts: sign-up, login on another device, lockout, the mailed reset code, erasing, and mail off.
// Mail is caught by a fake sender; everything else runs over HTTP with the JSON store.
import assert from 'node:assert/strict';
import http from 'node:http';
import { randomBytes } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import roomStoreModule from '../server/room-store.js';
import createEmailAccounts from '../server/email-accounts.js';

const { createRoomStore } = roomStoreModule;
const dataDir = mkdtempSync(join(tmpdir(), 'naadamy-email-'));
const mails = [];
let clock = Date.now();
const store = createRoomStore({ dataDir, databaseUrl: '' });
const accounts = createEmailAccounts(store, { dataDir, send: async (mail) => { mails.push(mail); }, now: () => clock, perMinute: 1000, cloudOf: async (id) => ({ eth: 'khalkh', for: id }) });
await accounts.init();
const servers = [];
async function serve(handler) {
  const server = http.createServer((req, res) => handler.handle(req, res));
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  servers.push(server);
  return async (body) => (await fetch(`http://127.0.0.1:${server.address().port}/api/email-account`, { method: 'POST', body: JSON.stringify(body) })).json();
}
const api = await serve(accounts);
const codeOf = (mail) => /(\d{6})/.exec(mail.text)[1];

try {
  const kid = { id: 'p18f00001', ticket: randomBytes(32).toString('hex') }, other = { id: 'p18f00002', ticket: randomBytes(32).toString('hex') };

  // the first step of signing up asks whether the email is free
  assert.deepEqual(await api({ act: 'check', email: 'nope' }), { ok: false, reason: 'bad-email' });
  assert.deepEqual(await api({ act: 'check', email: 'Bat@gmail.com' }), { ok: true, available: true });

  // sign up: needs the device's own ticket, a real email and a password of 6+ characters that isn't the email
  assert.equal((await api({ act: 'register', ...kid, email: 'not-an-email', password: 'secret1' })).reason, 'bad-email');
  assert.equal((await api({ act: 'register', ...kid, email: 'bat@gmail.com', password: '123' })).reason, 'weak-password');
  assert.equal((await api({ act: 'register', ...kid, email: 'bat@gmail.com', password: 'bat@gmail.com' })).reason, 'weak-password');
  assert.deepEqual(await api({ act: 'register', ...kid, email: '  Bat@Gmail.com ', password: 'secret1', name: 'Бат<b>', age: 9, avatar: 3 }), { ok: true, email: 'bat@gmail.com' });
  assert.equal((await api({ act: 'register', ...kid, email: 'bat2@gmail.com', password: 'secret1' })).reason, 'has-email', 'one email per player');
  assert.equal((await api({ act: 'register', ...other, email: 'BAT@gmail.com', password: 'secret1' })).reason, 'taken', 'one player per email, any case');
  assert.equal((await api({ act: 'me', ...kid })).email, 'bat@gmail.com');
  assert.deepEqual(await api({ act: 'check', email: ' BAT@gmail.com' }), { ok: true, available: false });
  assert.equal((await api({ act: 'me', ...other })).email, null);

  // log in on a new device: a fresh ticket for the same player, with the profile to rebuild
  const login = await api({ act: 'login', email: 'BAT@gmail.com', password: 'secret1' });
  assert.equal(login.ok, true);
  assert.equal(login.id, kid.id);
  assert.notEqual(login.ticket, kid.ticket);
  assert.deepEqual([login.name, login.age, login.avatar], ['Батb', 9, 3]);
  assert.deepEqual(login.cloud, { eth: 'khalkh', for: kid.id });
  assert.ok(await store.authenticatePlayer(kid.id, login.ticket), 'the new device ticket works');

  // wrong passwords: the same answer for an unknown email, then the account locks after 5 tries
  assert.equal((await api({ act: 'login', email: 'nobody@gmail.com', password: 'secret1' })).reason, 'wrong');
  for (let left = 4; left >= 1; left--) assert.deepEqual(await api({ act: 'login', email: 'bat@gmail.com', password: 'nope123' }), { ok: false, reason: 'wrong', left });
  const locked = await api({ act: 'login', email: 'bat@gmail.com', password: 'nope123' });
  assert.equal(locked.reason, 'locked');
  assert.equal((await api({ act: 'login', email: 'bat@gmail.com', password: 'secret1' })).reason, 'locked', 'locked even with the right password');
  clock += 16 * 60 * 1000;
  assert.equal((await api({ act: 'login', email: 'bat@gmail.com', password: 'secret1' })).ok, true, 'open again after 15 minutes');

  // forgot: the same answer for an unknown email, and no mail
  assert.deepEqual(await api({ act: 'forgot', email: 'nobody@gmail.com' }), { ok: true });
  assert.equal(mails.length, 0);
  assert.deepEqual(await api({ act: 'forgot', email: 'Bat@gmail.com' }), { ok: true });
  assert.equal(mails.length, 1);
  assert.equal(mails[0].to, 'bat@gmail.com');
  const code = codeOf(mails[0]);
  assert.deepEqual(await api({ act: 'forgot', email: 'bat@gmail.com' }), { ok: true });
  assert.equal(mails.length, 1, 'at most one code a minute');

  // reset: a wrong code counts down, the right one sets the password and logs this device in
  const wrongCode = code === '000000' ? '111111' : '000000';
  assert.deepEqual(await api({ act: 'reset', email: 'bat@gmail.com', code: wrongCode, password: 'newpass1' }), { ok: false, reason: 'bad-code', left: 4 });
  const reset = await api({ act: 'reset', email: 'bat@gmail.com', code, password: 'newpass1' });
  assert.equal(reset.ok, true);
  assert.equal(reset.id, kid.id);
  assert.ok(await store.authenticatePlayer(kid.id, reset.ticket));
  assert.equal((await api({ act: 'reset', email: 'bat@gmail.com', code, password: 'other12' })).reason, 'bad-code', 'a code works once');
  assert.equal((await api({ act: 'login', email: 'bat@gmail.com', password: 'secret1' })).reason, 'wrong', 'the old password is gone');
  assert.equal((await api({ act: 'login', email: 'bat@gmail.com', password: 'newpass1' })).ok, true);

  // a code runs out after 15 minutes, and after 5 wrong tries
  clock += 2 * 60 * 1000;
  await api({ act: 'forgot', email: 'bat@gmail.com' });
  const late = codeOf(mails.at(-1));
  clock += 16 * 60 * 1000;
  assert.equal((await api({ act: 'reset', email: 'bat@gmail.com', code: late, password: 'newpass2' })).reason, 'bad-code');
  clock += 2 * 60 * 1000;
  await api({ act: 'forgot', email: 'bat@gmail.com' });
  const guessed = codeOf(mails.at(-1)), wrong = guessed === '000000' ? '111111' : '000000';
  for (let i = 0; i < 5; i++) await api({ act: 'reset', email: 'bat@gmail.com', code: wrong, password: 'newpass2' });
  assert.equal((await api({ act: 'reset', email: 'bat@gmail.com', code: guessed, password: 'newpass2' })).reason, 'bad-code', 'no more tries');

  // an erased player's account goes with them, and frees the email
  await accounts.forget([kid.id]);
  assert.equal((await api({ act: 'login', email: 'bat@gmail.com', password: 'newpass1' })).reason, 'wrong');
  assert.equal((await api({ act: 'register', ...other, email: 'bat@gmail.com', password: 'secret1' })).ok, true);

  // without Resend, forgot says so
  const noMail = await serve(createEmailAccounts(store, { dataDir, env: {} }));
  assert.equal((await noMail({ act: 'forgot', email: 'bat@gmail.com' })).reason, 'mail-off');

  // the per-address limit
  const limited = await serve(createEmailAccounts(store, { dataDir, env: {}, perMinute: 2 }));
  const tries = [];
  for (let i = 0; i < 3; i++) tries.push((await limited({ act: 'login', email: 'x@gmail.com', password: 'secret1' })).reason);
  assert.deepEqual(tries, ['wrong', 'wrong', 'busy']);

  console.log('email-accounts: ok');
} finally {
  for (const server of servers) server.close();
  rmSync(dataDir, { recursive: true, force: true });
}
