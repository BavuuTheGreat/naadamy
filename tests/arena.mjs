// The online arena end to end, on a real server (JSON files, or the database in DATABASE_URL): wrestler accounts over
// /api/account (unique usernames, passwords, login on another device, rename, delete), then over the WebSocket friends,
// a challenge between friends, ranked matchmaking, a full sparring bout with the CPU, a dropped connection that comes
// back, the boards and the record. Bouts run fast here (ARENA_TICK_MS). Run with DATABASE_URL='' to use JSON files.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import crypto from 'node:crypto';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const dataDir = mkdtempSync(join(tmpdir(), 'naadamy-arena-ws-'));
const server = spawn(process.execPath, [join(root, 'server', 'server.js')], { env: { ...process.env, PORT: '0', DATA_DIR: dataDir, RENDER: 'true', ARENA_TICK_MS: '3' }, stdio: ['ignore', 'pipe', 'inherit', 'ipc'] });
const port = await new Promise((res, rej) => { server.on('message', (m) => m.port && res(m.port)); server.on('exit', (c) => rej(new Error('server exited ' + c))); setTimeout(() => rej(new Error('server did not start')), 8000); });
const results = [];
const ok = (label) => results.push('✓ ' + label);
let ip = 0;
const api = async (body) => (await fetch(`http://localhost:${port}/api/account`, { method: 'POST', body: JSON.stringify(body), headers: { 'x-forwarded-for': `10.1.0.${++ip % 250}` } })).json();
const player = (tag) => ({ id: `${tag}-${crypto.randomBytes(4).toString('hex')}`, ticket: crypto.randomBytes(32).toString('hex') });
const room = 'AR' + crypto.randomBytes(2).toString('hex').toUpperCase();

/** A socket in the room: `next(test)` waits for a message that passes `test` (skipping and keeping the others). */
function connect(p) {
  const ws = new WebSocket(`ws://localhost:${port}/ws`), seen = [], waiting = [];
  const closed = new Promise((res) => { ws.onclose = res; });
  ws.onmessage = (e) => { const m = JSON.parse(e.data); if (m.t === 'ar-st') { last.st = m; return; } seen.push(m); for (const w of [...waiting]) if (w.test(m)) { waiting.splice(waiting.indexOf(w), 1); seen.splice(seen.indexOf(m), 1); w.res(m); } };
  const last = { st: null };
  const next = (test, ms = 8000) => {
    const hit = seen.find(test); if (hit) { seen.splice(seen.indexOf(hit), 1); return Promise.resolve(hit); }
    return new Promise((res, rej) => { const w = { test, res }; waiting.push(w); setTimeout(() => { if (waiting.includes(w)) rej(new Error('no such message: ' + test)); }, ms); });
  };
  const opened = new Promise((res) => { ws.onopen = res; });
  const send = (m) => ws.send(JSON.stringify(m));
  return { ws, next, closed, send, last, ready: opened.then(() => { send({ t: 'join', room, id: p.id, ticket: p.ticket, name: 'W', look: {}, lvl: 1, x: 10, y: 10, dir: 0 }); return next((m) => m.t === 'welcome'); }) };
}
const t = (type) => (m) => m.t === type;

try {
  const A = player('a'), B = player('b'), C = player('c');
  // ---------------------------------------------------------------- accounts
  assert.deepEqual(await api({ act: 'check', username: 'Bataa' }), { ok: true, available: true });
  assert.deepEqual(await api({ act: 'register', ...A, username: 'Bataa', password: 'secret1' }), { ok: true, username: 'Bataa' });
  assert.deepEqual(await api({ act: 'check', username: 'BATAA' }), { ok: true, available: false, reason: 'taken' });
  assert.deepEqual(await api({ act: 'register', ...B, username: 'bataa', password: 'secret2' }), { ok: false, reason: 'taken' });
  assert.deepEqual(await api({ act: 'register', ...B, username: 'Dorjoo', password: '123' }), { ok: false, reason: 'weak-password' });
  assert.deepEqual(await api({ act: 'register', ...B, username: 'Dorjoo', password: 'dorjoo' }), { ok: false, reason: 'weak-password' });
  assert.deepEqual(await api({ act: 'register', ...A, username: 'Second', password: 'secret1' }), { ok: false, reason: 'has-account' });
  assert.deepEqual(await api({ act: 'register', id: A.id, ticket: B.ticket, username: 'Thief', password: 'secret1' }), { ok: false, reason: 'identity' });
  assert.deepEqual(await api({ act: 'register', ...B, username: 'Dorjoo', password: 'secret2' }), { ok: true, username: 'Dorjoo' });
  assert.deepEqual(await api({ act: 'register', ...C, username: 'Sukhee', password: 'secret3' }), { ok: true, username: 'Sukhee' });
  assert.deepEqual(await api({ act: 'me', ...A }), { ok: true, account: { username: 'Bataa' } });
  ok('register: a unique username (any case) and a password of 6+ letters for a player id; one account per player');

  assert.deepEqual(await api({ act: 'login', username: 'bataa', password: 'nope-nope' }), { ok: false, reason: 'wrong', left: 4 });
  const login = await api({ act: 'login', username: 'BATAA', password: 'secret1' });
  assert.equal(login.ok, true); assert.equal(login.id, A.id); assert.equal(login.username, 'Bataa'); assert.match(login.ticket, /^[a-f0-9]{64}$/);
  const other = connect({ id: login.id, ticket: login.ticket });
  assert.equal((await other.ready).t, 'welcome', 'the new device ticket joins as the same player');
  other.ws.close();
  for (let left = 4; left >= 1; left--) assert.deepEqual(await api({ act: 'login', username: 'Sukhee', password: 'wrong-pass' }), { ok: false, reason: 'wrong', left });
  assert.deepEqual(await api({ act: 'login', username: 'Sukhee', password: 'wrong-pass' }), { ok: false, reason: 'locked', wait: 900 });
  assert.equal((await api({ act: 'login', username: 'Sukhee', password: 'secret3' })).reason, 'locked', 'even the right password waits');
  ok('login: username and password give another device its own ticket for the same player; 5 wrong tries lock 15 minutes');

  // ---------------------------------------------------------------- friends
  const a = connect(A), b = connect(B);
  await a.ready; await b.ready;
  a.send({ t: 'ar-hello' }); b.send({ t: 'ar-hello' });
  const meA = await a.next(t('ar-me'));
  assert.deepEqual([meA.u, meA.rating, meA.tier, meA.w, meA.l], ['Bataa', 1000, 'novice', 0, 0]);
  await b.next(t('ar-me'));
  a.send({ t: 'fr-add', u: 'DORJOO' });
  assert.deepEqual((({ act, ok: o, reason, u }) => ({ act, o, reason, u }))(await a.next(t('fr-res'))), { act: 'add', o: true, reason: 'sent', u: 'Dorjoo' });
  assert.deepEqual((await a.next(t('fr-list'))).out.map((f) => f.u), ['Dorjoo'], 'a request that worked comes with the new list');
  assert.equal((await b.next(t('fr-ping'))).u, 'Bataa');
  b.send({ t: 'fr-list' });
  const lb = await b.next(t('fr-list'));
  assert.deepEqual(lb.inc.map((f) => f.u), ['Bataa']);
  b.send({ t: 'fr-accept', u: 'bataa' });
  assert.equal((await b.next(t('fr-res'))).ok, true);
  await b.next(t('fr-list'));
  await a.next(t('fr-ping'));
  a.send({ t: 'fr-list' });
  const la = await a.next(t('fr-list'));
  assert.deepEqual(la.friends.map((f) => [f.u, f.on, f.busy]), [['Dorjoo', true, false]]);
  a.send({ t: 'fr-add', u: 'nobody_here' });
  assert.equal((await a.next(t('fr-res'))).reason, 'no-account');
  ok('friends: add by username, see the request, accept, then each other online in the list');

  // ---------------------------------------------------------------- a challenge between friends, quit midway
  a.send({ t: 'ar-challenge', u: 'Sukhee' });
  assert.equal((await a.next(t('ar-err'))).reason, 'not-friends');
  a.send({ t: 'ar-challenge', u: 'Dorjoo' });
  await a.next(t('ar-sent'));
  const inv = await b.next(t('ar-invite'));
  assert.equal(inv.u, 'Bataa');
  b.send({ t: 'ar-accept', u: 'Bataa' });
  const sa = await a.next(t('ar-start')), sb = await b.next(t('ar-start'));
  assert.equal(sa.mode, 'friend'); assert.equal(sa.id, sb.id); assert.notEqual(sa.side, sb.side);
  assert.equal(sa.opp.u, 'Dorjoo'); assert.equal(sb.opp.u, 'Bataa');
  await new Promise((r) => setTimeout(r, 400));
  assert.ok(a.last.st && a.last.st.id === sa.id && Array.isArray(a.last.st.f), 'snapshots arrive');
  a.send({ t: 'ar-in', m: 0, p: 16 });
  a.send({ t: 'ar-quit' });
  const ea = await a.next(t('ar-end')), eb = await b.next(t('ar-end'));
  assert.equal(ea.winner, sb.side); assert.equal(ea.reason, 'quit');
  assert.ok(eb.rating.after > eb.rating.before && ea.rating.after < ea.rating.before);
  ok('a challenge between friends: invite, accept, both get the bout and its snapshots; quitting hands the win over');

  // ---------------------------------------------------------------- ranked matchmaking, and coming back after a dropped connection
  a.send({ t: 'ar-queue' }); await a.next(t('ar-queued'));
  b.send({ t: 'ar-queue' });
  const ra = await a.next(t('ar-start')); await b.next(t('ar-start'));
  assert.equal(ra.mode, 'ranked');
  b.ws.close(); await b.closed;
  const b2 = connect(B); await b2.ready;
  b2.send({ t: 'ar-hello' });
  const back = await b2.next(t('ar-start'));
  assert.equal(back.id, ra.id); assert.equal(back.resume, true);
  b2.send({ t: 'ar-quit' });
  const rEnd = await a.next(t('ar-end'));
  assert.equal(rEnd.winner, ra.side);
  ok('ranked: two players in the queue are matched; a dropped connection gets its bout back on return');

  // ---------------------------------------------------------------- a whole bout with the CPU
  a.send({ t: 'ar-cpu', lv: 3 });
  const cpu = await a.next(t('ar-start'));
  assert.equal(cpu.opp.u, 'CPU');
  const cpuEnd = await a.next(t('ar-end'), 30000);
  assert.equal(cpuEnd.reason, 'done'); assert.equal(cpuEnd.winner, 1, 'an idle wrestler loses to the CPU');
  assert.ok(cpuEnd.wins[1] === 2);
  const paid = await a.next((m) => m.t === 'wallet-result' && m.op === `arena:${cpu.id}`);
  assert.equal(paid.accepted, true);
  ok('sparring: a whole bout against the CPU runs on the server to the end and pays XP');

  // ---------------------------------------------------------------- boards and the record
  await new Promise((r) => setTimeout(r, 2100));
  a.send({ t: 'ar-board', k: 'global' });
  const gb = await a.next(t('ar-board'));
  const names = gb.rows.map((r) => r.u);
  assert.ok(names.indexOf('Bataa') >= 0 && names.indexOf('Bataa') < names.indexOf('Dorjoo'), 'one win each, but the ranked win over the friendly one is worth more');
  const rowA = gb.rows.find((r) => r.me);
  assert.equal(rowA.u, 'Bataa'); assert.deepEqual([rowA.w, rowA.l], [1, 1], 'the CPU spar does not count'); assert.ok(rowA.lvl >= 1 && rowA.xp > 0);
  await new Promise((r) => setTimeout(r, 2100));
  a.send({ t: 'ar-board', k: 'friends' });
  const fb = await a.next(t('ar-board'));
  assert.deepEqual(fb.rows.map((r) => r.u).sort(), ['Bataa', 'Dorjoo']);
  a.send({ t: 'ar-history' });
  const hist = await a.next(t('ar-history'));
  assert.deepEqual(hist.list.map((h) => [h.mode, h.opp, h.res]), [['cpu', 'CPU', 'loss'], ['ranked', 'Dorjoo', 'win'], ['friend', 'Dorjoo', 'loss']]);
  a.send({ t: 'fr-list' });
  const h2h = (await a.next(t('fr-list'))).friends[0];
  assert.deepEqual([h2h.hw, h2h.hl], [1, 1]);
  ok('boards: everyone by rating with level and XP, and friends only; the record and head-to-head with each friend');

  // ---------------------------------------------------------------- rename and delete
  assert.deepEqual(await api({ act: 'rename', ...B, password: 'secret2', username: 'Bataa' }), { ok: false, reason: 'taken' });
  assert.deepEqual(await api({ act: 'rename', ...B, password: 'secret2', username: 'Dorj2' }), { ok: true, username: 'Dorj2' });
  assert.equal((await b2.next((m) => m.t === 'ar-me' && m.u === 'Dorj2')).u, 'Dorj2');
  assert.deepEqual(await api({ act: 'register', ...C, username: 'Dorjoo', password: 'x' }), { ok: false, reason: 'weak-password' });
  assert.deepEqual((await api({ act: 'check', username: 'dorjoo' })).available, true, 'the old name is free');
  assert.deepEqual(await api({ act: 'password', ...B, password: 'secret2', next: 'better-2' }), { ok: true });
  assert.equal((await api({ act: 'login', username: 'Dorj2', password: 'secret2' })).ok, false);
  assert.equal((await api({ act: 'login', username: 'Dorj2', password: 'better-2' })).ok, true);
  assert.deepEqual(await api({ act: 'delete', ...B, password: 'better-2', confirm: 'nope' }), { ok: false, reason: 'confirm' });
  assert.deepEqual(await api({ act: 'delete', ...B, password: 'better-2', confirm: 'dorj2' }), { ok: true });
  assert.equal((await b2.next((m) => m.t === 'ar-me' && m.u === null)).u, null);
  assert.deepEqual((await api({ act: 'check', username: 'Dorj2' })).available, true);
  a.send({ t: 'fr-list' });
  assert.equal((await a.next(t('fr-list'))).friends.length, 0, 'the friendship went with the account');
  ok('rename frees the old name; a new password replaces the old one; delete frees the name and the friendships');

  a.ws.close(); b2.ws.close();
} finally {
  server.kill();
}
console.log(results.join('\n'));
console.log(`\n${results.length} arena checks passed`);
