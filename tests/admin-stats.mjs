// Admin stats: counts from the JSON store, the ADMIN_KEY check, the per-address limit on wrong keys, and off without a key.
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import roomStoreModule from '../server/room-store.js';
import createAdminStats from '../server/admin-stats.js';

const { createRoomStore } = roomStoreModule;
const dataDir = mkdtempSync(join(tmpdir(), 'naadamy-admin-'));

/** A fake request/response pair; resolves with { status, headers, body } once the handler answers. */
function call(admin, pathname, { key, method = 'GET', ip = '203.0.113.7' } = {}) {
  return new Promise((resolve) => {
    const req = { method, headers: key === undefined ? {} : { 'x-admin-key': key }, socket: { remoteAddress: ip } };
    let status = 0, headers = {};
    const res = { writeHead(s, h) { status = s; headers = h || {}; }, end(body = '') { resolve({ status, headers, body: String(body) }); } };
    const handled = admin.handle(req, res, pathname);
    if (!handled) resolve(null);
  });
}

try {
  const store = createRoomStore({ dataDir, databaseUrl: '' });
  const ids = ['kid-01', 'kid-02', 'kid-03'];
  for (const id of ids) assert.ok(await store.authenticatePlayer(id, randomBytes(32).toString('hex')));
  await store.noteRoomPlayer('NUTAG', { id: 'kid-01', name: 'A', look: {} });
  await store.noteRoomPlayer('ABCD', { id: 'kid-01', name: 'A', look: {} }); // two rooms, still one player
  await store.noteRoomPlayer('NUTAG', { id: 'kid-02', name: 'B', look: {} });

  const live = () => ({ total: 2, nutag: 1, rooms: 2 });
  const admin = createAdminStats({ roomStore: store, dataDir, live, adminKey: 'secret-key' });

  // other paths are not the admin's
  assert.equal(await call(admin, '/api/hello'), null);

  // the right key: counts only, no ids
  const ok = await call(admin, '/api/admin/stats', { key: 'secret-key' });
  assert.equal(ok.status, 200);
  const s = JSON.parse(ok.body);
  assert.equal(s.ok, true);
  assert.equal(s.storage, 'json');
  assert.deepEqual(s.online, { total: 2, nutag: 1, rooms: 2 });
  assert.deepEqual(s.players, { total: 3, new24h: null, new7d: null });
  assert.deepEqual(s.active, { last24h: 2, last7d: 2, last30d: 2 });
  assert.equal(s.accounts, 0);
  assert.equal(s.paid, null);
  assert.ok(!ok.body.includes('kid-'), 'no player ids');

  // the page is served, with its own content policy
  const page = await call(admin, '/admin');
  assert.equal(page.status, 200);
  assert.match(page.headers['content-type'], /text\/html/);
  assert.ok(page.headers['content-security-policy']);

  // wrong or missing key: 401, and after 10 misses the address waits
  assert.equal((await call(admin, '/api/admin/stats')).status, 401);
  for (let i = 0; i < 9; i++) assert.equal((await call(admin, '/api/admin/stats', { key: 'nope' })).status, 401);
  assert.equal((await call(admin, '/api/admin/stats', { key: 'secret-key' })).status, 429, 'blocked even with the right key');
  assert.equal((await call(admin, '/api/admin/stats', { key: 'secret-key', ip: '198.51.100.9' })).status, 200, 'other addresses unaffected');

  // only GET
  assert.equal((await call(admin, '/api/admin/stats', { key: 'secret-key', method: 'POST', ip: '198.51.100.10' })).status, 405);

  // without ADMIN_KEY both paths are 404
  const off = createAdminStats({ roomStore: store, dataDir, live, adminKey: '' });
  assert.equal((await call(off, '/api/admin/stats', { key: '' })).status, 404);
  assert.equal((await call(off, '/admin')).status, 404);

  console.log('admin-stats: ok');
} finally {
  rmSync(dataDir, { recursive: true, force: true });
}
