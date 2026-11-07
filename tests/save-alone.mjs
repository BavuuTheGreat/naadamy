// Saving while playing alone: POST /api/save takes the same upload as `sync` and `cloud-save`, with the device ticket and no
// socket. A replayed op is applied once, a wrong ticket is refused, and what was saved is there on the next join.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const dataDir = mkdtempSync(join(tmpdir(), 'naadamy-save-alone-'));
const server = spawn(process.execPath, [process.env.NAADAMY_SERVER || join(root, 'server', 'server.js')], {
  env: { ...process.env, PORT: '0', DATA_DIR: dataDir, DATABASE_URL: '' }, stdio: ['ignore', 'ignore', 'inherit', 'ipc'],
});
try {
  const port = await new Promise((done, reject) => {
    const timer = setTimeout(() => reject(new Error('server did not start')), 8000);
    server.on('message', (message) => { if (message.port) { clearTimeout(timer); done(message.port); } });
    server.once('exit', (code) => reject(new Error(`server exited (${code})`)));
  });
  const save = async (body) => {
    const res = await fetch(`http://127.0.0.1:${port}/api/save`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });
    return { status: res.status, body: await res.json() };
  };
  const me = { id: 'alone-player', ticket: randomBytes(32).toString('hex') };
  const op = randomBytes(16).toString('hex');

  const first = await save({ ...me, op, coins: 25, xp: 30, owned: ['deel-blue'], rod: 1, cloud: { eth: 'khalkh', aimag: 'tov' } });
  assert.equal(first.status, 200);
  assert.equal(first.body.ok, true);
  assert.match(first.body.day, /^\d{4}-\d{2}-\d{2}$/);
  const result = first.body.out.find((m) => m.t === 'sync-result');
  assert.equal(result.accepted, true);
  assert.equal(result.op, op);
  assert.deepEqual(result.applied, { coins: 25, xp: 30 });
  assert.equal(result.wallet.coins, 65, 'the starter 40 and the 25 earned');
  assert.equal(first.body.out.find((m) => m.t === 'cloud').cloud.eth, 'khalkh');

  const again = await save({ ...me, op, coins: 25, xp: 30, owned: ['deel-blue'], rod: 1 });
  assert.equal(again.body.out.find((m) => m.t === 'sync-result').wallet.coins, 65, 'the same op is not paid twice');

  assert.deepEqual((await save({ id: me.id, ticket: randomBytes(32).toString('hex'), op: randomBytes(16).toString('hex'), coins: 5, xp: 0 })),
    { status: 403, body: { ok: false, reason: 'identity' } });
  assert.equal((await save({ ...me, op: 'nope', coins: 5, xp: 0 })).body.out[0].reason, 'invalid');

  // the next join sees what was saved alone
  const welcome = await new Promise((done, reject) => {
    const socket = new WebSocket(`ws://127.0.0.1:${port}/ws`);
    const timer = setTimeout(() => reject(new Error('no welcome')), 5000);
    socket.onopen = () => socket.send(JSON.stringify({ t: 'join', room: 'NUTAG', ...me, name: 'Alone', look: {} }));
    socket.onmessage = (event) => { const m = JSON.parse(event.data); if (m.t === 'welcome') { clearTimeout(timer); socket.close(); done(m); } };
  });
  assert.equal(welcome.wallet.coins, 65);
  assert.equal(welcome.wallet.xp, 30);
  console.log('save-alone: all passed');
} finally {
  server.kill();
  rmSync(dataDir, { recursive: true, force: true });
}
