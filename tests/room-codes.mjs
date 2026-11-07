// Private rooms: a player opens a room and friends join with its 6-letter code; a wrong code is refused, 10 of them lock the
// address for 5 minutes, NUTAG still works for released builds, and an opened room survives a restart. Runs a real server over
// HTTP and WebSocket with the JSON store.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import createRoomCodes from '../server/room-codes.js';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const dataDir = mkdtempSync(join(tmpdir(), 'naadamy-room-codes-'));
const player = (id) => ({ id, ticket: randomBytes(32).toString('hex') });

async function start() {
  const server = spawn(process.execPath, [process.env.NAADAMY_SERVER || join(root, 'server', 'server.js')], {
    env: { ...process.env, PORT: '0', DATA_DIR: dataDir, DATABASE_URL: '' }, stdio: ['ignore', 'ignore', 'inherit', 'ipc'],
  });
  const port = await new Promise((done, reject) => {
    const timer = setTimeout(() => reject(new Error('server did not start')), 8000);
    server.on('message', (message) => { if (message.port) { clearTimeout(timer); done(message.port); } });
    server.once('exit', (code) => reject(new Error(`server exited (${code})`)));
  });
  return { server, port };
}
async function stop(server) {
  const gone = new Promise((done) => server.once('exit', done));
  server.kill('SIGTERM');
  await gone;
}
const api = async (port, body) => (await fetch(`http://127.0.0.1:${port}/api/room`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) })).json();

/** Joins a room and resolves with the first `welcome` or `error`, leaving the socket open. */
function join_(port, room, who) {
  return new Promise((done, reject) => {
    const socket = new WebSocket(`ws://127.0.0.1:${port}/ws`);
    const timer = setTimeout(() => reject(new Error(`no answer to join ${room}`)), 5000);
    socket.onopen = () => socket.send(JSON.stringify({ t: 'join', room, ...who, name: who.id, look: {} }));
    socket.onmessage = (event) => {
      const message = JSON.parse(event.data);
      if (message.t === 'welcome' || message.t === 'error') { clearTimeout(timer); done({ socket, message }); }
    };
  });
}

// the lock's clock, without a server
{
  let t = 1_000_000;
  const codes = createRoomCodes({}, { now: () => t });
  for (let i = 0; i < 9; i++) assert.equal(codes.wrongCode('a'), 0, 'nine wrong codes do not lock');
  assert.equal(codes.wrongCode('a'), 300, 'the tenth locks for 5 minutes');
  assert.equal(codes.lockedFor('b'), 0, 'another address is not locked');
  t += 299_000;
  assert.equal(codes.lockedFor('a'), 1);
  t += 1_000;
  assert.equal(codes.lockedFor('a'), 0, 'free again after 5 minutes');
  assert.equal(codes.wrongCode('a'), 0, 'and the count starts over');
  for (let i = 0; i < 8; i++) codes.wrongCode('c');
  t += 300_001;
  assert.equal(codes.wrongCode('c'), 0, 'old wrong codes are forgotten');
}

let running;
try {
  running = await start();
  const host = player('room-host'), friend = player('room-friend'), stranger = player('room-other');

  assert.deepEqual(await api(running.port, { act: 'open', id: 'room-host', ticket: 'nope' }), { ok: false, reason: 'identity' });
  assert.deepEqual(await api(running.port, { act: 'nothing' }), { ok: false, reason: 'invalid' });

  const opened = await api(running.port, { act: 'open', ...host });
  assert.equal(opened.ok, true);
  assert.match(opened.code, /^[A-HJKMNP-Z]{6}$/);

  const first = await join_(running.port, opened.code, host);
  assert.equal(first.message.t, 'welcome');
  assert.equal(first.message.room, opened.code);
  const second = await join_(running.port, opened.code.toLowerCase(), friend);
  assert.equal(second.message.t, 'welcome');
  assert.deepEqual(second.message.players.map((p) => p.id), ['room-host'], 'the friend sees the host');

  const typo = opened.code === 'AAAAAA' ? 'BBBBBB' : 'AAAAAA';
  const wrong = await join_(running.port, typo, stranger);
  assert.deepEqual(wrong.message, { t: 'error', why: 'no-room' });
  const answers = [];
  const nine = new Promise((done) => {
    wrong.socket.onmessage = (event) => { answers.push(JSON.parse(event.data)); if (answers.length === 9) done(); };
  });
  for (let i = 0; i < 9; i++) wrong.socket.send(JSON.stringify({ t: 'join', room: typo, ...stranger, name: 'x', look: {} }));
  await nine;
  assert.deepEqual(answers.slice(0, 8).map((a) => a.why), Array(8).fill('no-room'));
  assert.equal(answers[8].why, 'room-locked', 'the tenth wrong code locks');
  assert.equal(answers[8].wait, 300);
  const locked = await join_(running.port, opened.code, stranger);
  assert.equal(locked.message.why, 'room-locked', 'even the right code waits');
  locked.socket.close(); wrong.socket.close();

  const main = await join_(running.port, 'NUTAG', stranger);
  assert.equal(main.message.t, 'welcome', 'released builds still reach the main village');
  assert.deepEqual(main.message.players, [], 'and nobody from the private room is there');
  const older = await join_(running.port, 'CLASS1', player('room-older'));
  assert.equal(older.message.t, 'welcome', 'a code with a digit is not a private one');

  for (const open of [first, second, main, older]) open.socket.close();
  await stop(running.server);
  running = await start();
  const back = await join_(running.port, opened.code, friend);
  assert.equal(back.message.t, 'welcome', 'an opened room is still there after a restart');
  back.socket.close();
  console.log('room-codes: all passed');
} finally {
  if (running) await stop(running.server).catch(() => {});
  rmSync(dataDir, { recursive: true, force: true });
}
