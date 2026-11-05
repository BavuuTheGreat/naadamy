'use strict';
/* Player numbers for the game's owners: GET /api/admin/stats (JSON) and GET /admin (a small page that shows them).
   Both answer only when ADMIN_KEY is set; the stats need it in the x-admin-key header. Without ADMIN_KEY they are 404,
   so nobody can tell they exist. Wrong keys are limited per address. Only counts leave the server: no ids, no names.

   - online: players connected right now, and the village (НУТАГ) among them
   - players: every player id the server knows (a device that joined once), new ones in the last 24 h / 7 days
     (PostgreSQL only: the JSON files don't keep when an id was made)
   - active: players who joined a room in the last 24 h / 7 / 30 days (room joins are kept 30 days)
   - accounts: arena accounts with a username and password
   - paid: not tracked yet. The travel pass is only recorded on the device (Aimags.cs, Game.Charge), so the server can't
     count who paid until a real payment provider confirms payments to it. */

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const clientIp = require('./client-ip.js');

const DAY = 24 * 60 * 60 * 1000;
const TRIES_PER_15_MIN = 10;

function readJson(file) {
  try {
    const value = JSON.parse(fs.readFileSync(file, 'utf8'));
    return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
  } catch {
    return {};
  }
}

function sameKey(expected, given) {
  const a = crypto.createHash('sha256').update(expected).digest();
  const b = crypto.createHash('sha256').update(String(given || '')).digest();
  return crypto.timingSafeEqual(a, b);
}

module.exports = function createAdminStats({ roomStore, dataDir, live, adminKey = process.env.ADMIN_KEY || '', now = () => Date.now() }) {
  const misses = new Map(); // ip -> [times of wrong keys]
  function blocked(ip) {
    const t = now(), recent = (misses.get(ip) || []).filter((at) => t - at < 15 * 60 * 1000);
    misses.set(ip, recent);
    if (misses.size > 5000) for (const [key, times] of misses) if (!times.some((at) => t - at < 15 * 60 * 1000)) misses.delete(key);
    return recent.length >= TRIES_PER_15_MIN;
  }

  async function fromPostgres(pool) {
    const q = async (sql) => Number((await pool.query(sql)).rows[0].n) || 0;
    const seen = (days) => q(`SELECT count(DISTINCT player_id) AS n FROM naadamy_room_players WHERE seen_at > now() - interval '${days} days'`);
    const made = (days) => q(`SELECT count(*) AS n FROM naadamy_identities WHERE created_at > now() - interval '${days} days'`);
    const accounts = await q('SELECT count(*) AS n FROM naadamy_accounts').catch(() => 0); // the arena tables may not exist yet
    return {
      players: { total: await q('SELECT count(*) AS n FROM naadamy_identities'), new24h: await made(1), new7d: await made(7) },
      active: { last24h: await seen(1), last7d: await seen(7), last30d: await seen(30) },
      accounts,
    };
  }

  function fromJson() {
    const t = now();
    const lastSeen = new Map(); // player id -> latest join in any room
    for (const players of Object.values(readJson(path.join(dataDir, 'room-players.json')))) {
      if (!players || typeof players !== 'object') continue;
      for (const [id, entry] of Object.entries(players)) {
        const at = entry && Number(entry.at);
        if (Number.isFinite(at) && at > (lastSeen.get(id) || 0)) lastSeen.set(id, at);
      }
    }
    const within = (days) => [...lastSeen.values()].filter((at) => t - at < days * DAY).length;
    return {
      players: { total: Object.keys(readJson(path.join(dataDir, 'identities.json'))).length, new24h: null, new7d: null },
      active: { last24h: within(1), last7d: within(7), last30d: within(30) },
      accounts: Object.keys(readJson(path.join(dataDir, 'accounts.json'))).length,
    };
  }

  async function stats() {
    const stored = roomStore.kind === 'postgres' && roomStore.pool ? await fromPostgres(roomStore.pool) : fromJson();
    return { at: new Date(now()).toISOString(), storage: roomStore.kind, online: live(), ...stored, paid: null };
  }

  function send(res, status, body, headers = {}) {
    res.writeHead(status, { 'cache-control': 'no-store', ...headers });
    res.end(body);
  }

  /** Answers /admin and /api/admin/stats; false for any other path. */
  function handle(req, res, pathname) {
    if (pathname !== '/admin' && pathname !== '/api/admin/stats') return false;
    if (!adminKey) { send(res, 404, 'Not found', { 'content-type': 'text/plain; charset=utf-8' }); return true; }
    if (req.method !== 'GET' && req.method !== 'HEAD') { send(res, 405, 'Method not allowed', { 'content-type': 'text/plain; charset=utf-8' }); return true; }
    if (pathname === '/admin') {
      send(res, 200, PAGE, { 'content-type': 'text/html; charset=utf-8',
        'content-security-policy': "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'" });
      return true;
    }
    const ip = clientIp(req), json = { 'content-type': 'application/json' };
    if (blocked(ip)) { send(res, 429, '{"ok":false,"reason":"busy"}', { ...json, 'retry-after': '900' }); return true; }
    if (!sameKey(adminKey, req.headers['x-admin-key'])) {
      misses.get(ip).push(now());
      send(res, 401, '{"ok":false,"reason":"key"}', json);
      return true;
    }
    stats().then((s) => send(res, 200, JSON.stringify({ ok: true, ...s }), json)).catch((error) => {
      console.error('Could not count players for the admin stats:', error.message);
      send(res, 500, '{"ok":false,"reason":"storage"}', json);
    });
    return true;
  }

  return { handle, stats };
};

const PAGE = `<!DOCTYPE html>
<html lang="mn"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex"><title>Naadamy · Admin</title>
<style>
  body { font: 16px/1.4 system-ui, sans-serif; margin: 0; padding: 24px; background: #f6f3ee; color: #222; }
  h1 { font-size: 22px; margin: 0 0 16px; }
  form { display: flex; gap: 8px; margin-bottom: 20px; }
  input { flex: 1; max-width: 320px; padding: 8px 10px; font: inherit; border: 1px solid #bbb; border-radius: 6px; }
  button { padding: 8px 14px; font: inherit; border: 0; border-radius: 6px; background: #8a2b2b; color: #fff; cursor: pointer; }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: 12px; }
  .tile { background: #fff; border-radius: 10px; padding: 14px 16px; box-shadow: 0 1px 2px #0001; }
  .tile b { display: block; font-size: 30px; font-variant-numeric: tabular-nums; }
  .tile span { color: #666; font-size: 14px; }
  .note, .err { margin-top: 16px; color: #666; font-size: 14px; } .err { color: #a00; }
</style></head><body>
<h1>Naadamy · Тоглогчид</h1>
<form id="f"><input id="k" type="password" placeholder="ADMIN_KEY" autocomplete="current-password"><button>Харах</button></form>
<div class="grid" id="g"></div><p class="note" id="n"></p><p class="err" id="e"></p>
<script>
  const $ = (id) => document.getElementById(id);
  const fmt = (v) => (v === null || v === undefined ? '—' : Number(v).toLocaleString());
  let timer = 0;
  async function load() {
    const key = sessionStorage.getItem('naadamyAdminKey') || '';
    if (!key) return;
    try {
      const r = await fetch('/api/admin/stats', { headers: { 'x-admin-key': key }, cache: 'no-store' });
      const s = await r.json();
      if (!s.ok) { $('e').textContent = s.reason === 'key' ? 'Түлхүүр буруу.' : s.reason === 'busy' ? 'Хэт олон буруу оролдлого. 15 минут хүлээнэ үү.' : 'Алдаа: ' + s.reason; if (s.reason === 'key') sessionStorage.removeItem('naadamyAdminKey'); return; }
      $('e').textContent = '';
      const tiles = [
        ['Одоо онлайн', s.online.total], ['Нутагт онлайн', s.online.nutag], ['Нээлттэй өрөө', s.online.rooms],
        ['Идэвхтэй · 24 цаг', s.active.last24h], ['Идэвхтэй · 7 хоног', s.active.last7d], ['Идэвхтэй · 30 хоног', s.active.last30d],
        ['Нийт тоглогч', s.players.total], ['Шинэ · 24 цаг', s.players.new24h], ['Шинэ · 7 хоног', s.players.new7d],
        ['Arena бүртгэл', s.accounts], ['Төлсөн', s.paid],
      ];
      $('g').replaceChildren(...tiles.map(([label, value]) => { const d = document.createElement('div'); d.className = 'tile'; const b = document.createElement('b'); b.textContent = fmt(value); const t = document.createElement('span'); t.textContent = label; d.append(b, t); return d; }));
      $('n').textContent = 'Шинэчлэгдсэн: ' + new Date(s.at).toLocaleString() + ' · storage: ' + s.storage +
        (s.players.new24h === null ? ' · "Шинэ" тоо зөвхөн PostgreSQL дээр харагдана.' : '') + ' · "Төлсөн" тоо жинхэнэ төлбөр холбогдсоны дараа гарна.';
    } catch (error) { $('e').textContent = 'Сервертэй холбогдож чадсангүй.'; }
  }
  $('f').addEventListener('submit', (e) => { e.preventDefault(); sessionStorage.setItem('naadamyAdminKey', $('k').value.trim()); $('k').value = ''; load(); clearInterval(timer); timer = setInterval(load, 15000); });
  if (sessionStorage.getItem('naadamyAdminKey')) { load(); timer = setInterval(load, 15000); }
</script></body></html>`;
