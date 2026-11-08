// The online arena end to end, on a real server (JSON files, or the database in DATABASE_URL): wrestler accounts over
// /api/account (unique usernames, passwords, login on another device, rename, delete), then over the WebSocket friends,
// a 5-second duel in a room with a code, ranked matchmaking, a full sparring bout with the CPU, a dropped connection that comes
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

  // ---------------------------------------------------------------- a 5-second duel in a room with a code
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  a.send({ t: 'ar-room-join', code: 'ZZZZZ' });
  assert.equal((await a.next(t('ar-err'))).reason, 'no-room');
  a.send({ t: 'ar-room-open' });
  const { code } = await a.next(t('ar-room'));
  assert.match(code, /^[ABCDEFGHJKMNPQRSTUVWXYZ]{5}$/);
  a.send({ t: 'ar-room-join', code });
  assert.equal((await a.next(t('ar-err'))).reason, 'own-room');
  b.send({ t: 'ar-room-join', code: code.toLowerCase() });
  const da = await a.next(t('ar-duel')), db = await b.next(t('ar-duel'));
  assert.equal(da.id, db.id); assert.notEqual(da.side, db.side); assert.equal(da.skill, db.skill); assert.deepEqual(da.arrows, db.arrows); assert.equal(da.seed, db.seed);
  assert.deepEqual([da.lead, da.ms, da.opp.u, db.opp.u], [3000, 5000, 'Dorjoo', 'Bataa']);
  a.send({ t: 'ar-queue' });
  assert.equal((await a.next(t('ar-err'))).reason, 'in-match');
  a.send({ t: 'ar-duel-score', q: 1 }); // before the start: not counted
  await sleep(da.lead + 100);
  a.send({ t: 'ar-duel-score', q: 0.9 });
  b.send({ t: 'ar-duel-score', q: 0.5 });
  const ea = await a.next(t('ar-duel-end')), eb = await b.next(t('ar-duel-end'));
  assert.equal(ea.winner, da.side, 'the better score wins'); assert.equal(ea.reason, 'done');
  assert.ok(ea.scores[da.side] > ea.scores[db.side]); assert.equal(eb.winner, ea.winner);
  assert.deepEqual([ea.coins, ea.xp, eb.coins, eb.xp], [5, 10, 0, 5]);
  assert.equal(ea.rating, undefined);
  a.send({ t: 'ar-hello' });
  const afterDuel = await a.next(t('ar-me'));
  assert.deepEqual([afterDuel.rating, afterDuel.w, afterDuel.l, afterDuel.n], [1000, 0, 0, 0], 'a duel changes no rating or record');
  // the same two play again, and quitting hands the win over and closes the room
  a.send({ t: 'ar-duel-again' });
  await b.next(t('ar-duel-again'));
  b.send({ t: 'ar-duel-again' });
  const again = await a.next(t('ar-duel')); await b.next(t('ar-duel'));
  assert.notEqual(again.id, da.id);
  a.send({ t: 'ar-quit' });
  const qa = await a.next(t('ar-duel-end')), qb = await b.next(t('ar-duel-end'));
  assert.equal(qa.reason, 'quit'); assert.equal(qb.winner, 1 - again.side);
  await a.next(t('ar-room-closed')); await b.next(t('ar-room-closed'));
  b.send({ t: 'ar-room-join', code });
  assert.equal((await b.next(t('ar-err'))).reason, 'no-room', 'a closed room is gone');
  ok('a duel: a 5-letter code, the same game for both, the better score wins, no rating change; play again; quitting closes the room');
  // a host who leaves takes the room with them, and 10 wrong codes lock the player out for a while
  const c3 = connect(C); await c3.ready; c3.send({ t: 'ar-hello' }); await c3.next(t('ar-me'));
  c3.send({ t: 'ar-room-open' });
  const left = (await c3.next(t('ar-room'))).code;
  c3.ws.close(); await c3.closed;
  await sleep(50);
  b.send({ t: 'ar-room-join', code: left });
  assert.equal((await b.next(t('ar-err'))).reason, 'no-room');
  const c4 = connect(C); await c4.ready; c4.send({ t: 'ar-hello' }); await c4.next(t('ar-me'));
  for (let i = 0; i < 10; i++) { c4.send({ t: 'ar-room-join', code: 'ZZZZZ' }); assert.equal((await c4.next(t('ar-err'))).reason, 'no-room'); await sleep(60); }
  c4.send({ t: 'ar-room-join', code: 'ZZZZZ' });
  const locked = await c4.next(t('ar-err'));
  assert.equal(locked.reason, 'locked'); assert.equal(locked.wait, 300);
  c4.ws.close();
  ok('rooms: a waiting room closes with its host; 10 wrong codes lock a player out for 5 minutes');

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
  assert.ok(names.indexOf('Bataa') >= 0 && names.indexOf('Bataa') < names.indexOf('Dorjoo'), 'the ranked win puts Bataa above Dorjoo');
  const rowA = gb.rows.find((r) => r.me);
  assert.equal(rowA.u, 'Bataa'); assert.deepEqual([rowA.w, rowA.l], [1, 0], 'duels and the CPU spar do not count'); assert.ok(rowA.lvl >= 1 && rowA.xp > 0);
  await new Promise((r) => setTimeout(r, 2100));
  a.send({ t: 'ar-board', k: 'friends' });
  const fb = await a.next(t('ar-board'));
  assert.deepEqual(fb.rows.map((r) => r.u).sort(), ['Bataa', 'Dorjoo']);
  a.send({ t: 'ar-history' });
  const hist = await a.next(t('ar-history'));
  assert.deepEqual(hist.list.map((h) => [h.mode, h.opp, h.res]), [['cpu', 'CPU', 'loss'], ['ranked', 'Dorjoo', 'win'], ['friend', 'Dorjoo', 'loss'], ['friend', 'Dorjoo', 'win']]);
  a.send({ t: 'fr-list' });
  const h2h = (await a.next(t('fr-list'))).friends[0];
  assert.deepEqual([h2h.hw, h2h.hl], [2, 1]);
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
