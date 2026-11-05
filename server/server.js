#!/usr/bin/env node
/* Naadamy server: serves the app and links players into a shared village (Нутаг).
   Default JSON storage uses Node built-ins; PostgreSQL storage is enabled with DATABASE_URL.

   Run:  node server/server.js            (port 8000)
         PORT=3000 node server/server.js
   Everyone on the same Wi-Fi opens http://<this-computer-ip>:8000 and plays together.
   For PostgreSQL storage, install dependencies with npm ci and set DATABASE_URL. */
'use strict';
const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const os = require('os');
const R = require('../web/js/village/rules.js');
const WD = require('../web/js/village/world.js');
const QR = require('../web/js/qr.js');

const REPO = path.join(__dirname, '..'); // .env and data/ stay in the repo root
const UNITY_WEB = path.join(REPO, 'unity', 'Builds', 'WebGL'); // Unity's browser build (Naadamy → Build WebGL)
// Only Unity's built game is ever served as files. There is no fallback to web/: it holds the server's own game rules
// (web/js/village/rules.js…), which must never be downloadable. Without a build (on Render) only the API and /ws answer.
const ROOT = fs.existsSync(path.join(UNITY_WEB, 'index.html')) ? UNITY_WEB : null;
const PRODUCTION = !!(process.env.RENDER || process.env.NODE_ENV === 'production');
// headers on every answer: no MIME sniffing, no framing (clickjacking), no referrer leaks, only the device features the
// game uses, HTTPS only when served over HTTPS, and a content policy that lets the game load and nothing else
const SECURITY = {
  'x-content-type-options': 'nosniff', 'x-frame-options': 'DENY', 'referrer-policy': 'no-referrer', 'cross-origin-opener-policy': 'same-origin',
  'permissions-policy': 'camera=(), geolocation=(), payment=(), usb=(), microphone=(self)',
};
const CSP = "default-src 'self'; script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval' blob:; worker-src 'self' blob:; connect-src 'self' https: wss: ws: blob: data:; " +
  "img-src 'self' data: blob:; media-src 'self' data: blob:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; object-src 'none'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'";
function secure(req, res) {
  for (const [k, v] of Object.entries(SECURITY)) res.setHeader(k, v);
  if (req.headers['x-forwarded-proto'] === 'https' || req.socket.encrypted) res.setHeader('strict-transport-security', 'max-age=31536000; includeSubDomains');
}
// Which web pages may call the API: the game on Vercel (and its preview deployments), a computer on this network while
// developing, and any extra origins in CORS_ORIGINS (comma separated). Every other site's page is refused by the browser.
const EXTRA_ORIGINS = (process.env.CORS_ORIGINS || '').split(',').map((o) => o.trim()).filter(Boolean);
function originAllowed(o) {
  return /^https:\/\/naadamy-unity(-[a-z0-9-]+)?\.vercel\.app$/.test(o) || /^http:\/\/(localhost|127\.0\.0\.1|192\.168\.\d+\.\d+|10\.\d+\.\d+\.\d+)(:\d+)?$/.test(o) || EXTRA_ORIGINS.includes(o);
}
const clientIp = require('./client-ip.js');
// the AI helper costs money per question: besides its daily cap for the whole server, each address gets at most 30 a quarter hour
const aiHits = new Map();
function aiAllowed(ip) {
  const now = Date.now(), w = 15 * 60 * 1000, h = (aiHits.get(ip) || []).filter((t) => now - t < w);
  if (h.length >= 30) { aiHits.set(ip, h); return false; }
  h.push(now); aiHits.set(ip, h);
  if (aiHits.size > 5000) for (const [key, times] of aiHits) if (!times.some((t) => now - t < w)) aiHits.delete(key);
  return true;
}
const basar = require('./basar-ai.js')(REPO);
const { createRoomStore, scrubRoom } = require('./room-store');

const INSTANCE_ID = crypto.randomUUID();
const PORT = Number(process.env.PORT || process.argv[2] || 8000);
const lanIPs = () => Object.values(os.networkInterfaces()).flat().filter((i) => i && i.family === 'IPv4' && !i.internal).map((i) => i.address);
let actualPort = PORT;
const DATA_DIR = process.env.DATA_DIR || path.join(REPO, 'data');
const roomStore = createRoomStore({ dataDir: DATA_DIR });
const { resyncRealtime } = require('./realtime-resync.js');
const board = require('./leaderboard.js')(roomStore);
const parentEmail = require('./parent-email.js')(roomStore); // weekly report to a grown-up's email; off without RESEND_API_KEY
const family = require('./family.js')(roomStore, { cloudOf: (id) => getCloudSave(id), onErase: (ids) => eraseLive(ids), onChange: (ids) => board.forget(ids), onGone: (code) => voice.purge(code), email: parentEmail });
const voice = require('./voice.js')(roomStore, { dataDir: DATA_DIR, checkPin: (message) => family.checkPin(message) }); // memory book voice clips
// the online arena: wrestler accounts with unique usernames, friends, rated bouts (arena.js, accounts.js, arena-store.js)
const arenaStore = require('./arena-store.js').createArenaStore({ dataDir: DATA_DIR, pool: roomStore.pool || null });
const arena = require('./arena.js')(arenaStore, { level: (xp) => walletLevel({ xp }), reward: (id, operation, coins, xp) => payPlayer(id, operation, coins, xp),
  tickMs: Number(process.env.ARENA_TICK_MS) || undefined }); // ARENA_TICK_MS: only tests run bouts faster than 30 ticks a second
const accounts = require('./accounts.js')(roomStore, arenaStore, { cloudOf: (id) => getCloudSave(id), onChange: (id) => arena.changed(id), onDelete: (id) => arena.gone([id]) });
// player numbers for the owners at /admin (admin-stats.js); off unless ADMIN_KEY is set
// sign in with Google on any device (google-auth.js); off unless GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET are set
const googleAuth = require('./google-auth.js')(roomStore, arenaStore, { dataDir: DATA_DIR, cloudOf: (id) => getCloudSave(id) });
const adminStats = require('./admin-stats.js')({ roomStore, dataDir: DATA_DIR, live: () => ({ ...counts(), rooms: rooms.size }) });
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml', '.woff2': 'font/woff2', '.ttf': 'font/ttf', '.webmanifest': 'application/manifest+json', '.json': 'application/json', '.txt': 'text/plain; charset=utf-8', '.png': 'image/png',
  '.m4a': 'audio/mp4', '.mp3': 'audio/mpeg', '.ogg': 'audio/ogg', '.wav': 'audio/wav', '.wasm': 'application/wasm', '.data': 'application/octet-stream' };

// ---------------------------------------------------------------- static files
function serveStatic(req, res) {
  secure(req, res);
  let url;
  try { url = new URL(req.url, 'http://x'); } catch { res.writeHead(400); return res.end(); }
  if (url.pathname.startsWith('/api/')) {
    const origin = req.headers.origin;
    if (origin && originAllowed(origin)) { res.setHeader('access-control-allow-origin', origin); res.setHeader('vary', 'Origin'); }
    if (req.method === 'OPTIONS') {
      if (!origin || !originAllowed(origin)) { res.writeHead(403); return res.end(); }
      res.writeHead(204, { 'access-control-allow-methods': 'GET, POST, PUT, DELETE', 'access-control-allow-headers': 'content-type, x-family, x-player, x-ticket, x-pin', 'access-control-max-age': '600' });
      return res.end();
    }
  }
  if (url.pathname === '/api/hello') {
    res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' });
    const c = counts(), info = { naadamy: true, rooms: rooms.size, players: c.total, nutag: c.nutag };
    if (!PRODUCTION) info.urls = lanIPs().map((ip) => `http://${ip}:${actualPort}/`); // the LAN addresses are for a home network only, never published
    return res.end(JSON.stringify(info));
  }
  if (url.pathname === '/api/basar') {
    if (req.method === 'POST' && !aiAllowed(clientIp(req))) { res.writeHead(429, { 'content-type': 'application/json', 'retry-after': '900' }); return res.end('{"ok":false,"reason":"busy"}'); }
    return basar.handle(req, res);
  }
  if (url.pathname === '/api/family') return family.handle(req, res);
  if (url.pathname === '/api/account') return accounts.handle(req, res);
  if (url.pathname === '/api/voice' || url.pathname.startsWith('/api/voice/')) return voice.handle(req, res, url.pathname);
  if (url.pathname === '/api/email/confirm' || url.pathname === '/api/email/stop') return parentEmail.handle(req, res, url);
  if (adminStats.handle(req, res, url.pathname)) return;
  if (googleAuth.handle(req, res, url)) return;
  if (!ROOT || (req.method !== 'GET' && req.method !== 'HEAD')) { res.writeHead(ROOT ? 405 : 404, { 'content-type': 'text/plain; charset=utf-8' }); return res.end(ROOT ? 'Method not allowed' : 'Not found'); }
  let p;
  try { p = decodeURIComponent(url.pathname); } catch { res.writeHead(400); return res.end(); } // a broken %-escape used to throw and stop the server
  if (p.includes('\0')) { res.writeHead(400); return res.end(); }
  if (p.endsWith('/')) p += 'index.html';
  if (p.endsWith('/index.html') || p === '/index.html') res.setHeader('content-security-policy', CSP);
  const file = path.normalize(path.join(ROOT, p));
  if (!file.startsWith(ROOT + path.sep)) { res.writeHead(403); return res.end(); }
  fs.stat(file, (err, st) => {
    if (err || !st.isFile()) { res.writeHead(404); return res.end('Not found'); }
    // Unity's gzipped build files (game.wasm.gz…): the browser unpacks them itself
    const gz = file.endsWith('.gz');
    const type = TYPES[path.extname(gz ? file.slice(0, -3) : file)] || 'application/octet-stream';
    const cache = /\.(woff2|ttf|m4a|mp3|ogg|wav)$/.test(file) ? 'public, max-age=604800' : 'no-cache';
    if (gz) {
      res.writeHead(200, { 'content-type': type, 'content-encoding': 'gzip', 'content-length': st.size, 'cache-control': cache });
      return fs.createReadStream(file).pipe(res);
    }
    // byte ranges: Safari and iPhones refuse to play audio from servers without them
    const m = /^bytes=(\d*)-(\d*)$/.exec(req.headers.range || '');
    if (m && (m[1] || m[2])) {
      let start = m[1] ? Number(m[1]) : Math.max(0, st.size - Number(m[2])), end = m[1] && m[2] ? Math.min(Number(m[2]), st.size - 1) : st.size - 1;
      if (start >= st.size || start > end) { res.writeHead(416, { 'content-range': `bytes */${st.size}` }); return res.end(); }
      res.writeHead(206, { 'content-type': type, 'content-length': end - start + 1, 'content-range': `bytes ${start}-${end}/${st.size}`, 'accept-ranges': 'bytes', 'cache-control': cache });
      return fs.createReadStream(file, { start, end }).pipe(res);
    }
    res.writeHead(200, { 'content-type': type, 'content-length': st.size, 'accept-ranges': 'bytes', 'cache-control': cache });
    fs.createReadStream(file).pipe(res);
  });
}

// ---------------------------------------------------------------- rooms + saved gers
const rooms = new Map(); // code -> { code, players: Map(id -> player), lots: [] }
let saved = {};
let dirty = false;
let dirtyVersion = 0;
let persistPromise = null;
function markDirty() { dirty = true; dirtyVersion++; }
function normalizeLots(lots) {
  const normalized = Array.isArray(lots) ? lots.slice(0, R.LOTS) : [];
  while (normalized.length < R.LOTS) normalized.push(null);
  return normalized;
}
function room(code) {
  if (!rooms.has(code)) {
    const lots = normalizeLots(saved[code] && saved[code].lots);
    rooms.set(code, { code, players: new Map(), remotePlayers: new Map(), lots, matches: new Map(), wrestlingTimers: new Map(), chat: restoreChat(saved[code] && saved[code].chat) });
  }
  return rooms.get(code);
}
async function persist() {
  if (!dirty) return true;
  if (persistPromise) {
    const previousSaveSucceeded = await persistPromise;
    return previousSaveSucceeded && dirty ? persist() : previousSaveSucceeded;
  }
  const version = dirtyVersion;
  const snapshot = { ...saved };
  for (const r of rooms.values()) snapshot[r.code] = { lots: r.lots, chat: r.chat, t: Date.now() };
  persistPromise = (async () => {
    try {
      await roomStore.save(snapshot);
      saved = snapshot;
      if (dirtyVersion === version) dirty = false;
      return true;
    } catch (e) {
      console.error('Could not save the rooms:', e.message);
      return false;
    } finally {
      persistPromise = null;
    }
  })();
  return persistPromise;
}
setInterval(() => { void persist(); }, 5000).unref();
let stopping = false;
async function shutdown() {
  if (stopping) return;
  stopping = true;
  arena.close();
  await Promise.all([...conns].map((connection) => leave(connection)));
  const savedOnShutdown = await persist();
  try {
    await roomStore.close(INSTANCE_ID);
  } catch (e) {
    console.error('Could not close room storage:', e.message);
    process.exit(1);
  }
  process.exit(savedOnShutdown ? 0 : 1);
}
process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);

// ---------------------------------------------------------------- minimal WebSocket (RFC 6455)
const GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';
const MAX_PAYLOAD = 16 * 1024;
function frame(op, payload) {
  const data = Buffer.isBuffer(payload) ? payload : Buffer.from(payload);
  const len = data.length;
  const head = len < 126 ? Buffer.from([0x80 | op, len]) : len < 65536 ? Buffer.from([0x80 | op, 126, len >> 8, len & 255]) : null;
  if (!head) return null;
  return Buffer.concat([head, data]);
}
// one address (a whole classroom can share a school's) may hold only so many game connections, and open only so many a minute;
// the whole server holds only so many. All per server instance. A phone whose Wi-Fi vanished keeps its old slot until the missed ping.
const posInt = (v, d) => (/^[1-9]\d*$/.test(String(v || '').trim()) ? Number(v) : d);
const WS_MAX_PER_IP = posInt(process.env.WS_MAX_PER_IP, 200), WS_NEW_PER_MIN = posInt(process.env.WS_NEW_PER_MIN, 300), WS_MAX_TOTAL = posInt(process.env.WS_MAX_TOTAL, 2000);
const wsOpen = new Map(), wsNew = new Map(), wsLogged = new Set();
function wsAllowed(ip) {
  const now = Date.now(), h = (wsNew.get(ip) || []).filter((t) => now - t < 60000), open = wsOpen.get(ip) || 0;
  if (open >= WS_MAX_PER_IP || h.length >= WS_NEW_PER_MIN) {
    wsNew.set(ip, h);
    if (!wsLogged.has(ip) && wsLogged.size < 1000) { wsLogged.add(ip); console.warn('ws cap hit', ip, open); } // once per address a minute
    return false;
  }
  h.push(now); wsNew.set(ip, h); wsOpen.set(ip, open + 1);
  return true;
}
setInterval(() => { // off the hot path: forget addresses with nothing open and nothing in the last minute
  const now = Date.now();
  for (const [key, times] of wsNew) if (!wsOpen.has(key) && !times.some((t) => now - t < 60000)) wsNew.delete(key);
  wsLogged.clear();
}, 60000).unref();
function upgrade(req, socket) {
  const key = req.headers['sec-websocket-key'];
  let pathname; try { pathname = new URL(req.url, 'http://x').pathname; } catch { socket.destroy(); return; } // e.g. `GET //[` throws
  if (pathname !== '/ws' || !key) { socket.destroy(); return; }
  if (conns.size >= WS_MAX_TOTAL) { socket.on('error', () => {}); socket.end('HTTP/1.1 503 Service Unavailable\r\nRetry-After: 30\r\nConnection: close\r\nContent-Length: 0\r\n\r\n'); return; }
  const ip = clientIp(req);
  if (!wsAllowed(ip)) { socket.on('error', () => {}); socket.end('HTTP/1.1 429 Too Many Requests\r\nRetry-After: 60\r\nConnection: close\r\nContent-Length: 0\r\n\r\n'); return; }
  const accept = crypto.createHash('sha1').update(key + GUID).digest('base64');
  socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ' + accept + '\r\n\r\n');
  socket.setNoDelay(true);
  const conn = { socket, buf: Buffer.alloc(0), frag: [], fragBytes: 0, fragOpen: false, alive: true, tokens: 30, last: Date.now(), player: null, room: null };
  // a client that never reads (or a very slow one) would make Node queue every reply in memory: drop it instead
  const write = (f) => { if (socket.destroyed) return; if (socket.writableLength > 256 * 1024) return socket.destroy(); socket.write(f); };
  conn.send = (obj) => { const f = frame(1, JSON.stringify(obj)); if (f) write(f); };
  socket.on('data', (chunk) => {
    conn.buf = Buffer.concat([conn.buf, chunk]);
    if (conn.buf.length > MAX_PAYLOAD * 4) return socket.destroy();
    while (conn.buf.length >= 2) {
      const b0 = conn.buf[0], b1 = conn.buf[1], op = b0 & 0x0f, fin = !!(b0 & 0x80), masked = !!(b1 & 0x80);
      let len = b1 & 0x7f, off = 2;
      if (len === 126) { if (conn.buf.length < 4) return; len = conn.buf.readUInt16BE(2); off = 4; }
      else if (len === 127) return socket.destroy(); // far too big for this game
      if (len > MAX_PAYLOAD || !masked) return socket.destroy();
      if (op >= 8 && (len > 125 || !fin)) return socket.destroy(); // control frames are small and never split (RFC 6455 5.5)
      if (conn.buf.length < off + 4 + len) return;
      const mask = conn.buf.subarray(off, off + 4), data = Buffer.from(conn.buf.subarray(off + 4, off + 4 + len));
      for (let i = 0; i < data.length; i++) data[i] ^= mask[i & 3];
      conn.buf = conn.buf.subarray(off + 4 + len);
      if (op === 8) { try { socket.end(frame(8, Buffer.alloc(0))); } catch { /* closing */ } return; }
      if (op === 9) { write(frame(10, data)); continue; }
      if (op === 10) { conn.alive = true; continue; }
      if (op !== 0 && op !== 1) return socket.destroy(); // binary and reserved opcodes are not used
      if ((op === 0) !== conn.fragOpen) return socket.destroy(); // a continuation with nothing to continue, or a new message inside one
      conn.fragBytes += data.length;
      if (conn.fragBytes > MAX_PAYLOAD) return socket.destroy(); // the whole message, not just one frame
      conn.frag.push(data);
      conn.fragOpen = !fin;
      if (fin) { const text = Buffer.concat(conn.frag).toString('utf8'); conn.frag = []; conn.fragBytes = 0; onMessage(conn, text); }
    }
  });
  // close and error can both fire: give the address its slot back only once
  // (the slot goes first, so a throw in leave cannot leak it)
  const end = () => {
    if (conn.counted) { conn.counted = false; const n = (wsOpen.get(ip) || 1) - 1; if (n > 0) wsOpen.set(ip, n); else wsOpen.delete(ip); }
    conns.delete(conn); leave(conn);
  };
  conn.counted = true;
  socket.on('end', () => socket.end()); // a client that just drops the connection (no close frame) frees its slot now, not at the next ping
  socket.on('close', end);
  socket.on('error', end);
  conns.add(conn);
}
const conns = new Set();
setInterval(() => {
  for (const c of conns) {
    if (!c.alive) { c.socket.destroy(); continue; }
    c.alive = false;
    try { c.socket.write(frame(9, Buffer.alloc(0))); } catch { c.socket.destroy(); }
  }
  if (!stopping) refreshPresence().catch((error) => console.error('Could not refresh multiplayer presence:', error.message));
}, Number(process.env.HEARTBEAT_MS) || 20000).unref(); // a phone that drops off Wi-Fi without saying goodbye is removed after one or two missed pings

// ---------------------------------------------------------------- game protocol
const num = (v, lo, hi) => (Number.isFinite(v) ? Math.min(hi, Math.max(lo, Math.round(v))) : lo);
const numf = (v, lo, hi) => (Number.isFinite(v) ? Math.min(hi, Math.max(lo, Math.round(v * 100) / 100)) : lo); // positions with 2 decimals for smooth movement
const cleanName = (s) => String(s || '').replace(/[<>]/g, '').trim().slice(0, 16) || 'Найз';
const pub = (p) => ({ id: p.id, name: p.name, look: p.look, lvl: p.lvl, x: p.x, y: p.y, dir: p.dir });
const WALLET_MAX = 2_000_000_000;
const walletLevel = (wallet) => num(R.level(wallet.xp).l, 1, 99);
const PLAYER_HW = 0.26, PLAYER_HH = 0.14, MOVE_MAX_SPEED = 12.5, MOVE_SLACK = 0.18, JUMP_MS = 460;
const safeSpawn = { x: 17, y: 13, dir: 0 };
function positionOpen(x, y, jumping) {
  const solid = (tx, ty) => WD.blocked(tx, ty) && !(jumping && (WD.g[ty] || '')[tx] === 'r');
  const fx = x + 0.5, fy = y + 0.82;
  return !solid(Math.floor(fx - PLAYER_HW), Math.floor(fy - PLAYER_HH))
    && !solid(Math.floor(fx + PLAYER_HW), Math.floor(fy - PLAYER_HH))
    && !solid(Math.floor(fx - PLAYER_HW), Math.floor(fy + PLAYER_HH))
    && !solid(Math.floor(fx + PLAYER_HW), Math.floor(fy + PLAYER_HH));
}
function validPosition(position) {
  return position && Number.isFinite(position.x) && Number.isFinite(position.y)
    && position.x >= 0.5 && position.x <= R.W - 2 && position.y >= 0.5 && position.y <= R.H - 2
    && positionOpen(position.x, position.y, false);
}
function clearMovementPath(from, to, jumping) {
  const distance = Math.hypot(to.x - from.x, to.y - from.y);
  const steps = Math.max(1, Math.ceil(distance / 0.1));
  for (let i = 1; i <= steps; i++) {
    const fraction = i / steps;
    if (!positionOpen(from.x + (to.x - from.x) * fraction, from.y + (to.y - from.y) * fraction, jumping)) return false;
  }
  return true;
}
function walletSeed() {
  return { coins: 40, xp: 0 };
}
function inventorySeed() {
  return { owned: ['deel-blue'], rod: 1 };
}
function ownedLook(look, inventory) {
  const clean = R.cleanLook(look);
  for (const slot of ['deel', 'hat', 'face', 'neck', 'pet']) {
    const value = clean[slot];
    if (value === null || value === undefined) continue;
    const item = R.ITEMS.find((candidate) => candidate.slot === slot && candidate.val === value);
    if (!item || !inventory.owned.includes(item.id)) clean[slot] = slot === 'deel' ? 'blue' : null;
  }
  return clean;
}
/** How many people are on the whole server and in the main village, so players in different rooms can find each other. */
function counts() {
  let total = 0, nutag = 0;
  for (const r of rooms.values()) {
    total += r.players.size + r.remotePlayers.size;
    if (r.code === 'NUTAG') nutag = r.players.size + r.remotePlayers.size;
  }
  return { total, nutag };
}
function census() { const c = counts(); for (const r of rooms.values()) if (r.players.size) broadcast(r, { t: 'census', ...c }); }
const REALTIME_TYPES = new Set(['join', 'leave', 'move', 'jump', 'look', 'say', 'lot', 'replaced']);
function broadcastLocal(r, msg, except) {
  for (const p of r.players.values()) if (p.conn !== except) p.conn.send(msg);
}
function broadcast(r, msg, except) {
  broadcastLocal(r, msg, except);
  if (roomStore.sharedRealtime && REALTIME_TYPES.has(msg.t)) {
    roomStore.publishRealtime({ room: r.code, instanceId: INSTANCE_ID, message: msg }).catch((error) => {
      console.error('Could not publish multiplayer event:', error.message);
    });
  }
}
function handleRealtime(event) {
  if (event && event.t === 'family-erased') { // a family was erased on another instance (or this one: it is safe twice)
    if (typeof event.code === 'string') void voice.purge(event.code); // its voice clips (the erasing instance purges them too: twice is harmless)
    if (!Array.isArray(event.ids)) return;
    return eraseLive(event.ids.filter((id) => typeof id === 'string')).catch((error) => console.error('Could not drop erased players:', error.message)); // the resync waits on it
  }
  if (!event || !R.ROOM_RE.test(event.room || '') || !event.message || typeof event.message.t !== 'string') return;
  const { room: code, message } = event;
  if (message.t === 'wrestle') {
    const r = room(code);
    deliverWrestleEvent(r, message);
    return;
  }
  if (message.t === 'replaced') {
    for (const r of rooms.values()) {
      if (r.remotePlayers.delete(message.id)) broadcastLocal(r, { t: 'leave', id: message.id });
    }
    for (const connection of conns) {
      if (connection.player && connection.player.id === message.id) {
        connection.send({ t: 'replaced' });
        setTimeout(() => connection.socket.destroy(), 50);
      }
    }
    return;
  }
  if (!REALTIME_TYPES.has(message.t)) return;
  const r = room(code);
  if (message.t === 'join' || message.t === 'leave') r.presenceRev = (r.presenceRev || 0) + 1;
  if (message.t === 'join' && message.p) {
    r.remotePlayers.set(message.p.id, message.p);
    if (!message.replace) logChat(r, { k: 'join', id: message.p.id, n: cleanName(message.p.name) });
  } else if (message.t === 'leave') {
    const player = r.remotePlayers.get(message.id);
    r.remotePlayers.delete(message.id);
    if (player) logChat(r, { k: 'leave', id: player.id, n: cleanName(player.name) });
  } else if (message.t === 'move') {
    const player = r.remotePlayers.get(message.id);
    if (player) Object.assign(player, { x: message.x, y: message.y, dir: message.dir });
  } else if (message.t === 'look') {
    const player = r.remotePlayers.get(message.id);
    if (player) Object.assign(player, { look: message.look, lvl: message.lvl });
  } else if (message.t === 'say' && Number.isInteger(message.i) && typeof message.id === 'string') {
    const kind = message.k === 'e' ? 'e' : 'p';
    const max = kind === 'e' ? R.EMOTES.length : R.PHRASES.length;
    if (message.i >= 0 && message.i < max) logChat(r, { k: kind, id: message.id, n: cleanName(message.n), i: message.i });
  } else if (message.t === 'lot' && Number.isInteger(message.i) && message.i >= 0 && message.i < R.LOTS
    && ['claim', 'stage', 'style', 'free'].includes(message.act)) {
    r.lots[message.i] = message.lot || null;
    saved[code] = { ...(saved[code] || {}), lots: r.lots };
  }
  broadcastLocal(r, message);
  if (message.t === 'join' || message.t === 'leave') census();
}
function wrestlingEvent(to, message) {
  return { t: 'wrestle', to, message };
}
function deliverWrestleEvent(r, event) {
  const message = event.message;
  const timerKey = message.id && `${r.code}:${message.id}`;
  if (message.t === 'wr-end' && timerKey) {
    clearTimeout(r.wrestlingTimers.get(timerKey));
    r.wrestlingTimers.delete(timerKey);
  } else if ((message.t === 'wr-start' || message.t === 'wr-result') && timerKey && message.deadline) {
    clearTimeout(r.wrestlingTimers.get(timerKey));
    const delay = Math.max(0, message.deadline - Date.now());
    const timer = setTimeout(() => {
      r.wrestlingTimers.delete(timerKey);
      resolveSharedWrestlingTimeout(r, message.id).catch((error) => {
        console.error('Could not resolve shared wrestling timeout:', error.message);
      });
    }, delay);
    if (timer.unref) timer.unref();
    r.wrestlingTimers.set(timerKey, timer);
  }
  for (const id of event.to || []) {
    const player = r.players.get(id);
    if (player) player.conn.send(message);
  }
  if (message.t === 'wr-end') awardWrestlingResult(r, message);
}
function awardWrestlingResult(r, message) {
  if (message.reason === 'left' && !message.rounds) return; // quit before any round was played: nobody is paid
  if (!message.winner || typeof message.id !== 'string' || !/^w[\w-]{1,64}$/.test(message.id)
    || !Array.isArray(message.playerIds) || message.playerIds.length !== 2 || !message.playerIds.includes(message.winner)) return;
  for (const id of message.playerIds) {
    const player = r.players.get(id);
    if (player) grantWrestlingReward(player, message.id, id === message.winner);
  }
}
async function grantWrestlingReward(player, matchId, won) {
  return payPlayer(player.id, `wrestle:${matchId}`, won ? 25 : 0, won ? 30 : 10);
}
/** Pays a server-decided reward (a bout) into player `id`'s wallet once per `operation`, under the daily earn cap, and tells
 *  every connection of that player here. The player may have gone: the wallet is paid all the same. */
async function payPlayer(id, operation, coins, xp) {
  const here = [...conns].filter((c) => c.player && c.player.id === id);
  try {
    const outcome = await roomStore.mutateWallet(id, operation, (wallet, inventory, earned) => {
      if (wallet.coins > WALLET_MAX - coins || wallet.xp > WALLET_MAX - xp) {
        return { changed: false, result: { accepted: false, reason: 'limit' } };
      }
      // like an uploaded sync, wrestling pay counts toward the daily earn cap; what goes over is not paid
      const day = villageDay();
      const today = earned.d === day ? earned : { d: day, coins: 0, xp: 0 };
      const addCoins = Math.max(0, Math.min(coins, R.DAILY_EARN_CAP.coins - today.coins));
      const addXp = Math.max(0, Math.min(xp, R.DAILY_EARN_CAP.xp - today.xp));
      if (!addCoins && !addXp) return { changed: false, result: { accepted: false, reason: 'limit' } };
      return {
        changed: true,
        wallet: { coins: wallet.coins + addCoins, xp: wallet.xp + addXp },
        earned: { d: day, coins: today.coins + addCoins, xp: today.xp + addXp },
        result: { accepted: true },
      };
    });
    for (const c of here) {
      c.wallet = outcome.wallet; c.inventory = outcome.inventory;
      syncPlayerLevel(c);
      c.send({ t: 'wallet-result', op: operation, ...outcome.result, wallet: outcome.wallet });
    }
  } catch (error) {
    console.error('Could not grant server-validated reward:', error.message);
    for (const c of here) c.send({ t: 'wallet-result', op: operation, accepted: false, reason: 'storage', wallet: c.wallet });
  }
}
async function mutateSharedWrestling(r, mutate) {
  const { result } = await roomStore.mutateRoom(
    r.code,
    { lots: r.lots, chat: r.chat, wrestling: { invites: [], matches: [] } },
    (state) => {
      if (!state.wrestling || typeof state.wrestling !== 'object') state.wrestling = {};
      if (!Array.isArray(state.wrestling.invites)) state.wrestling.invites = [];
      if (!Array.isArray(state.wrestling.matches)) state.wrestling.matches = [];
      const now = Date.now();
      const oldInviteCount = state.wrestling.invites.length;
      state.wrestling.invites = state.wrestling.invites.filter((invite) => invite.expiresAt > now);
      const changed = state.wrestling.invites.length !== oldInviteCount;
      const outcome = mutate(state.wrestling);
      return {
        changed: changed || Boolean(outcome && outcome.changed),
        events: outcome && outcome.events || [],
        value: outcome && outcome.value,
      };
    },
    INSTANCE_ID,
  );
  for (const event of result.events) deliverWrestleEvent(r, event);
  return result.value;
}
function resolveSharedRound(wrestling, match, timedOut) {
  if (timedOut) {
    for (const side of ['a', 'b']) {
      if (!match.moves[side]) match.moves[side] = { m: R.MOVES[Math.floor(Math.random() * R.MOVES.length)].id, p: 0.15 };
    }
  }
  const a = match.moves.a, b = match.moves.b;
  if (!a || !b) return { changed: false, events: [] };
  const result = R.exchange(a.m, a.p, b.m, b.p);
  if (result > 0) match.score.a++;
  else if (result < 0) match.score.b++;
  match.played = (match.played || 0) + 1; // draws count too: they were played
  match.moves = {};
  const finished = match.score.a >= 2 || match.score.b >= 2;
  match.deadline = finished ? null : Date.now() + 25000;
  const events = [wrestlingEvent([match.a, match.b], {
    t: 'wr-result', id: match.id, ma: a.m, pa: a.p, mb: b.m, pb: b.p,
    r: result, score: match.score, ...(finished ? {} : { deadline: match.deadline }),
  })];
  if (finished) {
    wrestling.matches = wrestling.matches.filter((entry) => entry.id !== match.id);
    events.push(wrestlingEvent([match.a, match.b], {
      t: 'wr-end', id: match.id, winner: match.score.a > match.score.b ? match.a : match.b,
      playerIds: [match.a, match.b], reason: '',
    }));
  }
  return { changed: true, events };
}
async function resolveSharedWrestlingTimeout(r, matchId) {
  if (!roomStore.sharedRealtime) return;
  await mutateSharedWrestling(r, (wrestling) => {
    const match = wrestling.matches.find((entry) => entry.id === matchId);
    if (!match || match.deadline > Date.now()) return { changed: false };
    return resolveSharedRound(wrestling, match, true);
  });
}
async function handleSharedWrestling(conn, type, message) {
  const p = conn.player, r = conn.room;
  if (!p || !r) return;
  try {
    await mutateSharedWrestling(r, (wrestling) => {
      const events = [];
      if (type === 'wr-invite') {
        const toId = String(message.to || '').slice(0, 24);
        const target = r.players.get(toId) || r.remotePlayers.get(toId);
        if (!target || toId === p.id || wrestling.matches.some((match) => match.a === p.id || match.b === p.id || match.a === toId || match.b === toId)) {
          conn.send({ t: 'deny', act: 'wr', why: 'busy' });
          return { changed: false };
        }
        if (wrestling.invites.some((invite) => invite.from === p.id && invite.to === toId)) return { changed: false };
        wrestling.invites.push({ from: p.id, to: toId, fromName: p.name, expiresAt: Date.now() + 60000 });
        events.push(wrestlingEvent([toId], { t: 'wr-invite', from: p.id, name: p.name }));
        return { changed: true, events };
      }
      if (type === 'wr-decline') {
        const fromId = String(message.from || '').slice(0, 24);
        const index = wrestling.invites.findIndex((invite) => invite.from === fromId && invite.to === p.id);
        if (index < 0) return { changed: false };
        wrestling.invites.splice(index, 1);
        events.push(wrestlingEvent([fromId], { t: 'wr-declined', by: p.id, name: p.name }));
        return { changed: true, events };
      }
      if (type === 'wr-accept') {
        const fromId = String(message.from || '').slice(0, 24);
        const invitationIndex = wrestling.invites.findIndex((invite) => invite.from === fromId && invite.to === p.id);
        const opponent = r.players.get(fromId) || r.remotePlayers.get(fromId);
        if (invitationIndex < 0 || !opponent || wrestling.matches.some((match) => match.a === p.id || match.b === p.id || match.a === fromId || match.b === fromId)) {
          conn.send({ t: 'deny', act: 'wr', why: 'busy' });
          return { changed: false };
        }
        const invitation = wrestling.invites[invitationIndex];
        wrestling.invites = wrestling.invites.filter((invite) => invite.from !== p.id && invite.to !== p.id && invite.from !== fromId && invite.to !== fromId);
        const id = `w${crypto.randomUUID()}`;
        const match = {
          id, a: fromId, b: p.id, score: { a: 0, b: 0 }, moves: {}, deadline: Date.now() + 25000,
          aInfo: { id: fromId, name: invitation.fromName, look: opponent.look },
          bInfo: { id: p.id, name: p.name, look: p.look },
        };
        wrestling.matches.push(match);
        events.push(wrestlingEvent([match.a, match.b], {
          t: 'wr-start', id, a: match.aInfo, b: match.bInfo, deadline: match.deadline,
        }));
        return { changed: true, events };
      }
      if (type === 'wr-move') {
        const match = wrestling.matches.find((entry) => entry.a === p.id || entry.b === p.id);
        const side = match && (match.a === p.id ? 'a' : 'b');
        if (!match || !side || match.deadline <= Date.now() || match.moves[side] || !R.MOVES.some((move) => move.id === message.m)) {
          if (match && match.deadline <= Date.now()) return resolveSharedRound(wrestling, match, true);
          return { changed: false };
        }
        const power = Number(message.p);
        match.moves[side] = { m: message.m, p: Math.max(0, Math.min(1, Number.isFinite(power) ? power : 0)) };
        const opponentId = side === 'a' ? match.b : match.a;
        if (!match.moves[side === 'a' ? 'b' : 'a']) {
          events.push(wrestlingEvent([opponentId], { t: 'wr-wait', id: match.id }));
          return { changed: true, events };
        }
        return resolveSharedRound(wrestling, match, false);
      }
      if (type === 'wr-quit') {
        const index = wrestling.matches.findIndex((match) => match.a === p.id || match.b === p.id);
        if (index < 0) return { changed: false };
        const [match] = wrestling.matches.splice(index, 1);
        const winner = match.a === p.id ? match.b : match.a;
        events.push(wrestlingEvent([match.a, match.b], { t: 'wr-end', id: match.id, winner, playerIds: [match.a, match.b], reason: 'left', rounds: match.played || 0 }));
        return { changed: true, events };
      }
      return { changed: false };
    });
  } catch (error) {
    console.error(`Could not process ${type} in shared wrestling:`, error.message);
    conn.send({ t: 'error', why: 'storage' });
  }
}
async function endSharedWrestlingOnLeave(r, playerId) {
  if (!roomStore.sharedRealtime) return;
  try {
    await mutateSharedWrestling(r, (wrestling) => {
      const before = wrestling.invites.length;
      wrestling.invites = wrestling.invites.filter((invite) => invite.from !== playerId && invite.to !== playerId);
      const index = wrestling.matches.findIndex((match) => match.a === playerId || match.b === playerId);
      if (index < 0) return { changed: wrestling.invites.length !== before };
      const [match] = wrestling.matches.splice(index, 1);
      const winner = match.a === playerId ? match.b : match.a;
      return {
        changed: true,
        events: [wrestlingEvent([match.a, match.b], { t: 'wr-end', id: match.id, winner, playerIds: [match.a, match.b], reason: 'left', rounds: match.played || 0 })],
      };
    });
  } catch (error) {
    console.error('Could not finish shared wrestling after player left:', error.message);
  }
}
async function refreshPresence() {
  if (!roomStore.sharedRealtime) return;
  const stale = await roomStore.refreshPresence(INSTANCE_ID);
  for (const player of stale) {
    const r = rooms.get(player.room_code) || room(player.room_code);
    const wasOnline = r.remotePlayers.delete(player.id);
    await endSharedWrestlingOnLeave(r, player.id);
    if (wasOnline) broadcast(r, { t: 'leave', id: player.id });
  }
}
/** The room's bounded history of phrases, emotes, joins and leaves. */
const CHAT_KEEP = 30, SAY_GAP = 1000;
function cleanChatLine(line) {
  if (!line || typeof line !== 'object' || Array.isArray(line)) return null;
  const { k } = line;
  if (!['p', 'e', 'join', 'leave'].includes(k)) return null;
  const id = String(line.id || '').slice(0, 24);
  if (!/^[\w-]{4,24}$/.test(id)) return null;
  const clean = { k, id, n: cleanName(line.n) };
  if (k === 'p' || k === 'e') {
    const max = k === 'e' ? R.EMOTES.length : R.PHRASES.length;
    if (!Number.isInteger(line.i) || line.i < 0 || line.i >= max) return null;
    clean.i = line.i;
  }
  return clean;
}
function restoreChat(lines) {
  return Array.isArray(lines) ? lines.map(cleanChatLine).filter(Boolean).slice(-CHAT_KEEP) : [];
}
function logChat(r, line) {
  r.chat.push(line);
  if (r.chat.length > CHAT_KEEP) r.chat.splice(0, r.chat.length - CHAT_KEEP);
  markDirty();
}
function updatePresence(p) {
  const now = Date.now();
  if (now - (p.presenceUpdatedAt || 0) < 500) return;
  p.presenceUpdatedAt = now;
  roomStore.updatePresence(INSTANCE_ID, pub(p)).catch((error) => console.error('Could not update player presence:', error.message));
}
function syncPlayerLevel(conn) {
  const p = conn.player;
  if (!p || !conn.room || !conn.wallet) return;
  const level = walletLevel(conn.wallet);
  if (p.lvl === level) return;
  p.lvl = level;
  updatePresence(p);
  broadcast(conn.room, { t: 'look', id: p.id, look: p.look, lvl: level }, conn);
}

async function joinPlayer(conn, message, code, id, now) {
  const account = await roomStore.getAccount(id, walletSeed(), inventorySeed());
  await leave(conn);
  const r = room(code);
  const latestState = await roomStore.getRoomState(code);
  if (latestState) {
    r.lots = normalizeLots(latestState.lots);
    r.chat = restoreChat(latestState.chat);
    saved[code] = latestState;
  }
  const old = r.players.get(id);
  const replacingRemote = r.remotePlayers.has(id);
  const activePlayers = roomStore.sharedRealtime && !old && !replacingRemote ? await roomStore.listPresence(code) : [];
  const previousPosition = old || r.remotePlayers.get(id) || activePlayers.find((player) => player.id === id);
  const position = validPosition(previousPosition) ? previousPosition : safeSpawn;
  if (old) {
    old.conn.player = null; old.conn.room = null;
    old.conn.send({ t: 'replaced' });
    setTimeout(() => old.conn.socket.destroy(), 50);
  }
  const p = {
    id, conn, name: cleanName(message.name), look: ownedLook(message.look, account.inventory), lvl: walletLevel(account.wallet),
    x: position.x, y: position.y, dir: num(position.dir, 0, 3), moveAt: Date.now(), jt: 0,
  };
  const previousInstance = await roomStore.registerPresence(INSTANCE_ID, code, pub(p));
  if (conn.socket.destroyed) {
    await roomStore.removePresence(INSTANCE_ID, p.id);
    return;
  }
  if (roomStore.sharedRealtime && (old || replacingRemote || previousInstance)) {
    await endSharedWrestlingOnLeave(r, p.id);
  }
  if (previousInstance && previousInstance !== INSTANCE_ID) {
    await roomStore.publishRealtime({ room: code, instanceId: INSTANCE_ID, message: { t: 'replaced', id } });
  }
  const online = await roomStore.listPresence(code);
  if (conn.socket.destroyed) {
    await roomStore.removePresence(INSTANCE_ID, p.id);
    return;
  }
  r.remotePlayers.clear();
  for (const player of online) if (player.id !== id && !r.players.has(player.id)) r.remotePlayers.set(player.id, player);
  r.players.set(id, p); conn.player = p; conn.room = r;
  // for the room board; a `board` asked right after the welcome waits for it (conn.noted)
  conn.noted = roomStore.noteRoomPlayer(code, { id, name: p.name, look: p.look }).catch((error) => console.error('Could not note the room player:', error.message));
  if (!old && !replacingRemote && !previousInstance) logChat(r, { k: 'join', id, n: p.name });
  const players = [
    ...[...r.players.values()].filter((player) => player !== p).map(pub),
    ...r.remotePlayers.values(),
  ];
  conn.wallet = account.wallet;
  conn.inventory = account.inventory;
  conn.send({
    t: 'welcome', v: R.VERSION, now, day: villageDay(), room: code, players, lots: r.lots, chat: r.chat,
    look: p.look, wallet: account.wallet, inventory: account.inventory, position: { x: p.x, y: p.y, dir: p.dir },
  });
  const replacing = Boolean(old || replacingRemote || previousInstance);
  broadcast(r, { t: 'join', p: pub(p), ...(replacing ? { replace: true } : {}) }, conn);
  census();
}

async function handleWalletOperation(conn, message) {
  const p = conn.player;
  if (!p) return;
  const operation = String(message.op || '');
  const action = message.action;
  const coins = message.coins, xp = message.xp;
  if (!/^[a-f0-9]{32}$/i.test(operation)) return conn.send({ t: 'wallet-result', op: operation.slice(0, 40), accepted: false, reason: 'invalid', wallet: conn.wallet });
  if (action === 'reward') {
    return conn.send({ t: 'wallet-result', op: operation, accepted: false, reason: 'server-only', wallet: conn.wallet });
  }
  const valid = action === 'spend' && Number.isInteger(coins) && coins > 0 && coins <= 10_000 && xp === 0;
  if (!valid) return conn.send({ t: 'wallet-result', op: operation, accepted: false, reason: 'invalid', wallet: conn.wallet });

  try {
    const outcome = await roomStore.mutateWallet(p.id, operation, (wallet) => {
      if (wallet.coins < coins) {
        return { changed: false, result: { accepted: false, reason: 'insufficient' } };
      }
      return {
        changed: true,
        wallet: { coins: wallet.coins - coins, xp: wallet.xp },
        result: { accepted: true },
      };
    });
    conn.wallet = outcome.wallet;
    conn.inventory = outcome.inventory;
    syncPlayerLevel(conn);
    conn.send({ t: 'wallet-result', op: operation, ...outcome.result, wallet: outcome.wallet });
  } catch (error) {
    console.error('Could not update player wallet:', error.message);
    conn.send({ t: 'wallet-result', op: operation, accepted: false, reason: 'storage', wallet: conn.wallet });
  }
}

/** Progress a device made since it last saw the server wallet (offline play, or rewards earned while connected), added to the account.
 *  Coins and XP count against the player's daily earn cap; what goes over is left out of `applied` so the device keeps it and tries
 *  again on a later day. Coins spent on the device come off the balance, never below zero. Shop items are combined with the
 *  account's, as long as the player's level allows them. */
const SYNC_MAX = 1_000_000;
async function handleSync(conn, message) {
  const p = conn.player;
  if (!p) return;
  const operation = String(message.op || '');
  const reply = (fields) => conn.send({ t: 'sync-result', op: operation.slice(0, 40), wallet: conn.wallet, inventory: conn.inventory, ...fields });
  const { coins, xp, rod } = message;
  const owned = Array.isArray(message.owned) ? message.owned.slice(0, R.ITEMS.length) : [];
  if (!/^[a-f0-9]{32}$/i.test(operation) || !Number.isSafeInteger(coins) || Math.abs(coins) > SYNC_MAX
    || !Number.isSafeInteger(xp) || xp < 0 || xp > SYNC_MAX || (rod !== undefined && rod !== 1 && rod !== 2)) {
    return reply({ accepted: false, reason: 'invalid' });
  }
  try {
    const outcome = await roomStore.mutateWallet(p.id, operation, (wallet, inventory, earned) => {
      const day = villageDay();
      const today = earned.d === day ? earned : { d: day, coins: 0, xp: 0 };
      const addXp = Math.max(0, Math.min(xp, R.DAILY_EARN_CAP.xp - today.xp, WALLET_MAX - wallet.xp));
      const addCoins = coins >= 0
        ? Math.max(0, Math.min(coins, R.DAILY_EARN_CAP.coins - today.coins, WALLET_MAX - wallet.coins))
        : -Math.min(-coins, wallet.coins);
      const level = R.level(wallet.xp + addXp).l;
      const allowed = (item) => item && item.lvl <= level;
      const nextOwned = [...inventory.owned];
      for (const id of owned) {
        const item = typeof id === 'string' ? R.ITEMS.find((entry) => entry.id === id && entry.cat !== 'tool') : null;
        if (allowed(item) && !nextOwned.includes(item.id)) nextOwned.push(item.id);
      }
      const rodItem = R.ITEMS.find((entry) => entry.cat === 'tool' && entry.val === rod);
      const nextRod = rod > inventory.rod && allowed(rodItem) ? rod : inventory.rod;
      const result = { accepted: true, applied: { coins: addCoins, xp: addXp } };
      if (!addXp && !addCoins && nextOwned.length === inventory.owned.length && nextRod === inventory.rod) return { changed: false, result };
      return {
        changed: true,
        wallet: { coins: wallet.coins + addCoins, xp: wallet.xp + addXp },
        inventory: { owned: nextOwned, rod: nextRod },
        earned: { d: day, coins: today.coins + Math.max(0, addCoins), xp: today.xp + addXp },
        result,
      };
    });
    conn.wallet = outcome.wallet;
    conn.inventory = outcome.inventory;
    syncPlayerLevel(conn);
    reply({ ...outcome.result, wallet: outcome.wallet, inventory: outcome.inventory });
  } catch (error) {
    console.error('Could not sync player progress:', error.message);
    reply({ accepted: false, reason: 'storage' });
  }
}

function villageDay(date = new Date()) {
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en', {
    timeZone: 'Asia/Ulaanbaatar', year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(date).map(({ type, value }) => [type, value]));
  return `${parts.year}-${parts.month}-${parts.day}`;
}
async function handleDailyActivityClaim(conn, message) {
  const p = conn.player;
  if (!p || !conn.room) return;
  const activity = typeof message.activity === 'string' && message.activity.length <= 16 ? message.activity : '';
  const valid = Object.hasOwn(R.DAILY_ACTIVITY_REWARDS, activity);
  const day = villageDay();
  conn.send({
    t: 'daily-activity-result', activity, day, accepted: false,
    reason: valid ? 'server-only' : 'invalid', wallet: conn.wallet,
  });
}
async function handleQuestClaim(conn, message) {
  const p = conn.player;
  if (!p) return;
  const quest = typeof message.quest === 'string' && message.quest.length <= 16 ? message.quest : '';
  const valid = Object.hasOwn(R.DAILY_QUEST_REWARDS, quest);
  conn.send({
    t: 'quest-claim-result', quest, accepted: false,
    reason: valid ? 'server-only' : 'invalid', wallet: conn.wallet,
  });
}

async function handleShopPurchase(conn, message) {
  const p = conn.player;
  if (!p) return;
  const operation = String(message.op || '');
  const itemId = message.item;
  if (!/^[a-f0-9]{32}$/i.test(operation)) {
    return conn.send({ t: 'inventory-result', op: operation.slice(0, 40), accepted: false, reason: 'invalid', wallet: conn.wallet, inventory: conn.inventory });
  }
  const item = typeof itemId === 'string' && itemId.length <= 32 ? R.ITEMS.find((entry) => entry.id === itemId) : null;
  if (!item) return conn.send({ t: 'inventory-result', op: operation, accepted: false, reason: 'invalid', wallet: conn.wallet, inventory: conn.inventory });
  try {
    const outcome = await roomStore.mutateWallet(p.id, operation, (wallet, inventory) => {
      const alreadyOwned = item.cat === 'tool' ? inventory.rod >= item.val : inventory.owned.includes(item.id);
      if (alreadyOwned) return { changed: false, result: { accepted: false, reason: 'owned' } };
      if (R.level(wallet.xp).l < item.lvl) return { changed: false, result: { accepted: false, reason: 'level' } };
      if (wallet.coins < item.price) return { changed: false, result: { accepted: false, reason: 'insufficient' } };
      const nextInventory = item.cat === 'tool'
        ? { ...inventory, rod: item.val }
        : { ...inventory, owned: [...inventory.owned, item.id] };
      return {
        changed: true,
        wallet: { ...wallet, coins: wallet.coins - item.price },
        inventory: nextInventory,
        result: { accepted: true },
      };
    });
    conn.wallet = outcome.wallet;
    conn.inventory = outcome.inventory;
    conn.send({ t: 'inventory-result', op: operation, ...outcome.result, wallet: outcome.wallet, inventory: outcome.inventory });
  } catch (error) {
    console.error('Could not purchase player shop item:', error.message);
    conn.send({ t: 'inventory-result', op: operation, accepted: false, reason: 'storage', wallet: conn.wallet, inventory: conn.inventory });
  }
}

async function handleProgressSave(conn) {
  if (!conn.player) return;
  conn.send({ t: 'progress-result', accepted: false, reason: 'server-only', revision: 0 });
}

/** The child's cloud save (R.cleanCloud) merged with what a device sends. It lives in the player's progress record under
 *  `cloud`, next to any legacy progress; a write that raced another device is merged again. */
async function mergeCloudSave(id, incoming) {
  for (let attempt = 0; attempt < 4; attempt++) {
    const current = await roomStore.getProgress(id, {});
    const stored = R.cleanCloud(current.state.cloud);
    const merged = R.mergeCloud(stored, incoming);
    if (JSON.stringify(merged) === JSON.stringify(stored)) return merged;
    const saved = await roomStore.saveProgress(id, current.revision, { ...current.state, cloud: merged });
    if (saved.accepted) return merged;
  }
  throw new Error('the cloud save kept changing');
}
async function getCloudSave(id) {
  return R.cleanCloud((await roomStore.getProgress(id, {})).state.cloud);
}
async function handleCloudSave(conn, message) {
  const p = conn.player;
  if (!p || !message.cloud || typeof message.cloud !== 'object') return;
  // one save at a time per connection; a newer one waiting replaces the older
  conn.cloudNext = message.cloud;
  if (conn.cloudBusy) return;
  conn.cloudBusy = true;
  try {
    while (conn.cloudNext) {
      const next = conn.cloudNext; conn.cloudNext = null;
      conn.send({ t: 'cloud', cloud: await mergeCloudSave(p.id, next) });
    }
  } catch (error) {
    console.error('Could not save the cloud save:', error.message);
    conn.send({ t: 'error', why: 'storage' });
  } finally {
    conn.cloudBusy = false;
  }
}

/** One player's ger actions run one after another: a stage sent right after a claim must find the claimed lot, which only
 *  exists once the claim is saved (with PostgreSQL that takes a few round trips). */
function queueLotAction(conn, action, message) {
  const run = () => {
    const p = conn.player, r = conn.room;
    if (!p || !r) return;
    if (action === 'claim') return persistLotAction(conn, action, message);
    const i = r.lots.findIndex((lot) => lot && lot.by === p.id);
    if (i >= 0) return persistLotAction(conn, action, { ...message, i });
  };
  conn.lotQueue = (conn.lotQueue || Promise.resolve()).then(run, run);
}

async function persistLotAction(conn, action, message) {
  const p = conn.player, r = conn.room;
  if (!p || !r) return;
  const i = message.i;
  if (action === 'claim' && (!Number.isInteger(i) || i < 0 || i >= R.LOTS)) return;
  if (action !== 'claim' && (!Number.isInteger(i) || i < 0 || i >= R.LOTS)) return;

  try {
    const { state, result } = await roomStore.mutateRoom(
      r.code,
      { lots: r.lots, chat: r.chat },
      (current) => {
        const lots = normalizeLots(current.lots);
        let lot = lots[i];
        if (action === 'claim' && lot && lot.by === p.id) {
          // the claim was sent again before the first one was answered: the client did not know yet that the plot is
          // its own, so the next stage came as a claim. It counts as that stage.
          if (!Number.isInteger(message.stage) || R.lotStage(lot) >= R.GER_DONE || message.stage !== R.lotStage(lot) + 1) return { changed: false };
          lot.stage = message.stage;
          current.lots = lots;
          return { changed: true, event: { t: 'lot', act: 'stage', i, lot, by: p.id } };
        }
        if (action === 'claim') {
          if (lot) return { changed: false, deny: 'taken' };
          if (lots.some((owned) => owned && owned.by === p.id)) return { changed: false, deny: 'own' };
          lot = {
            by: p.id,
            n: p.name,
            roof: Object.hasOwn(R.ROOF, message.roof) ? message.roof : 'blue',
            door: Object.hasOwn(R.DOOR, message.door) ? message.door : 'red',
            furn: [],
            stage: 1, // a new claim always starts at stage 1; the submitted stage is ignored
          };
          lots[i] = lot;
        } else {
          if (!lot || lot.by !== p.id) return { changed: false };
          if (action === 'stage') {
            if (!Number.isInteger(message.stage) || R.lotStage(lot) >= R.GER_DONE || message.stage !== R.lotStage(lot) + 1) {
              return { changed: false, deny: 'stage' };
            }
            lot.stage = message.stage;
          } else {
            if (Object.hasOwn(R.ROOF, message.roof)) lot.roof = message.roof;
            if (Object.hasOwn(R.DOOR, message.door)) lot.door = message.door;
            if (Array.isArray(message.furn)) {
              lot.furn = [...new Set(message.furn.filter((furniture) => R.FURNITURE.some((item) => item.id === furniture)))].slice(0, 12);
            }
            lot.n = p.name;
          }
        }
        current.lots = lots;
        return { changed: true, event: { t: 'lot', act: action, i, lot, by: p.id } };
      },
    );
    r.lots = normalizeLots(state.lots);
    saved[r.code] = { ...(saved[r.code] || {}), lots: r.lots };
    if (result.deny) conn.send({ t: 'deny', act: 'ger', why: result.deny });
    if (result.event) broadcast(r, result.event);
  } catch (error) {
    console.error('Could not save shared ger state:', error.message);
    conn.send({ t: 'error', why: 'storage' });
  }
}

function onMessage(conn, text) {
  const t = Date.now();
  // a bout's inputs (`ar-in`, small) have their own allowance: 60 a second; everything else ~20 a second
  if (text.length <= 64 && text.startsWith('{"t":"ar-in"')) {
    conn.inTokens = Math.min(60, (conn.inTokens ?? 60) + ((t - (conn.inLast || t)) / 1000) * 60); conn.inLast = t;
    if ((conn.inTokens -= 1) < 0) return;
  } else {
    conn.tokens = Math.min(30, conn.tokens + ((t - conn.last) / 1000) * 20); conn.last = t;
    if ((conn.tokens -= 1) < 0) return;
  }
  let m; try { m = JSON.parse(text); } catch { return; }
  if (!m || typeof m.t !== 'string') return;

  if (m.t === 'join') {
    const code = String(m.room || '').toUpperCase();
    const id = String(m.id || '').slice(0, 24);
    if (!R.ROOM_RE.test(code) || !/^[\w-]{4,24}$/.test(id)) return conn.send({ t: 'error', why: 'bad-join' });
    roomStore.authenticatePlayer(id, m.ticket).then(async (authenticated) => {
      if (!authenticated) {
        // 'erased': the family was erased (the device forgets it); 'identity': a wrong or evicted ticket
        conn.send({ t: 'error', why: (await roomStore.isErased(id)) ? 'erased' : 'identity' });
        setTimeout(() => conn.socket.destroy(), 50);
        return;
      }
      return joinPlayer(conn, m, code, id, t);
    }).catch((error) => {
      console.error('Could not join multiplayer room:', error.message);
      conn.send({ t: 'error', why: 'storage' });
    });
    return;
  }
  const p = conn.player, r = conn.room;
  if (!p || !r) return;
  if (m.t.startsWith('ar-') || m.t.startsWith('fr-')) {
    arena.handle(conn, m).catch((error) => {
      console.error('Arena:', error.message);
      conn.send({ t: 'ar-err', reason: 'storage', of: m.t });
    });
    return;
  }
  switch (m.t) {
    case 'move': {
      const t = Date.now(), next = { x: numf(m.x, 0.5, R.W - 2), y: numf(m.y, 0.5, R.H - 2) };
      const elapsed = Math.min(0.5, Math.max(0, (t - p.moveAt) / 1000));
      const maxDistance = MOVE_MAX_SPEED * elapsed + MOVE_SLACK;
      const distance = Math.hypot(next.x - p.x, next.y - p.y);
      const jumping = t - p.jt < JUMP_MS;
      if (distance > maxDistance || !clearMovementPath(p, next, jumping)) {
        p.moveAt = t;
        conn.send({ t: 'move-correct', x: p.x, y: p.y, dir: p.dir });
        return;
      }
      p.x = next.x; p.y = next.y; p.dir = num(m.dir, 0, 3); p.moveAt = t;
      updatePresence(p);
      broadcast(r, { t: 'move', id: p.id, x: p.x, y: p.y, dir: p.dir }, conn);
      break;
    }
    case 'jump': { // show other players your jump, at most a few times a second
      const t = Date.now(); if (t - (p.jt || 0) < 300) return; p.jt = t;
      broadcast(r, { t: 'jump', id: p.id }, conn);
      break;
    }
    case 'look':
      p.look = ownedLook(m.look, conn.inventory); p.lvl = walletLevel(conn.wallet);
      updatePresence(p);
      broadcast(r, { t: 'look', id: p.id, look: p.look, lvl: p.lvl }, conn);
      break;
    case 'say': {
      const kind = m.k === 'e' ? 'e' : 'p', max = kind === 'e' ? R.EMOTES.length : R.PHRASES.length;
      if (!Number.isInteger(m.i) || m.i < 0 || m.i >= max) return;
      const t = Date.now(); if (t - (p.st || 0) < SAY_GAP) return; p.st = t; // one phrase a second, so nobody floods the room
      logChat(r, { k: kind, id: p.id, n: p.name, i: m.i });
      broadcast(r, { t: 'say', id: p.id, n: p.name, k: kind, i: m.i });   // to everyone, including the sender
      break;
    }
    case 'wallet-op':
      handleWalletOperation(conn, m);
      break;
    case 'quest-claim':
      handleQuestClaim(conn, m);
      break;
    case 'daily-activity-claim':
      handleDailyActivityClaim(conn, m);
      break;
    case 'shop-buy':
      handleShopPurchase(conn, m);
      break;
    case 'sync':
      handleSync(conn, m);
      break;
    case 'progress-save':
      handleProgressSave(conn, m);
      break;
    case 'cloud-save':
      handleCloudSave(conn, m);
      break;
    case 'board': // XP leaderboard of this room or of the player's family (leaderboard.js)
      Promise.resolve(conn.noted).then(() => board.reply(p.id, r.code, m.k)).then((answer) => conn.send(answer));
      break;
    // ---- own ger
    case 'ger-claim':
      queueLotAction(conn, 'claim', m);
      break;
    case 'ger-stage':
      queueLotAction(conn, 'stage', m);
      break;
    case 'ger-style':
      queueLotAction(conn, 'style', m);
      break;
    // ---- live wrestling
    case 'wr-invite': {
      if (roomStore.sharedRealtime) {
        handleSharedWrestling(conn, 'wr-invite', m);
        break;
      }
      const to = r.players.get(String(m.to));
      if (!to || to === p || to.match || p.match) return conn.send({ t: 'deny', act: 'wr', why: 'busy' });
      (to.invites = to.invites || new Set()).add(p.id);
      to.conn.send({ t: 'wr-invite', from: p.id, name: p.name });
      break;
    }
    case 'wr-decline': {
      if (roomStore.sharedRealtime) {
        handleSharedWrestling(conn, 'wr-decline', m);
        break;
      }
      const from = r.players.get(String(m.from)); if (p.invites) p.invites.delete(String(m.from));
      if (from) from.conn.send({ t: 'wr-declined', by: p.id, name: p.name });
      break;
    }
    case 'wr-accept': {
      if (roomStore.sharedRealtime) {
        handleSharedWrestling(conn, 'wr-accept', m);
        break;
      }
      const from = r.players.get(String(m.from));
      if (!from || !p.invites || !p.invites.has(from.id) || from.match || p.match) return conn.send({ t: 'deny', act: 'wr', why: 'busy' });
      p.invites.delete(from.id);
      const id = 'w' + Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
      const match = { id, a: from.id, b: p.id, score: { a: 0, b: 0 }, moves: {}, timer: null };
      r.matches.set(id, match); from.match = p.match = id;
      const info = (q) => ({ id: q.id, name: q.name, look: q.look });
      for (const q of [from, p]) q.conn.send({ t: 'wr-start', id, a: info(from), b: info(p) });
      armMatch(r, match);
      break;
    }
    case 'wr-move': {
      if (roomStore.sharedRealtime) {
        handleSharedWrestling(conn, 'wr-move', m);
        break;
      }
      const match = p.match && r.matches.get(p.match); if (!match) return;
      const side = match.a === p.id ? 'a' : 'b';
      if (match.moves[side] || !R.MOVES.some((x) => x.id === m.m)) return;
      match.moves[side] = { m: m.m, p: Math.max(0, Math.min(1, Number(m.p) || 0)) };
      const other = r.players.get(side === 'a' ? match.b : match.a); if (other) other.conn.send({ t: 'wr-wait' });
      if (match.moves.a && match.moves.b) resolveMatch(r, match);
      break;
    }
    case 'wr-quit': {
      if (roomStore.sharedRealtime) {
        handleSharedWrestling(conn, 'wr-quit', m);
        break;
      }
      const match = p.match && r.matches.get(p.match); if (!match) return;
      endMatch(r, match, match.a === p.id ? match.b : match.a, 'left');
      break;
    }
  }
}
function armMatch(r, match) {
  clearTimeout(match.timer);
  match.timer = setTimeout(() => {          // a player who never answers gets a weak random move
    for (const side of ['a', 'b']) if (!match.moves[side]) match.moves[side] = { m: R.MOVES[Math.floor(Math.random() * R.MOVES.length)].id, p: 0.15 };
    resolveMatch(r, match);
  }, 25000);
  match.timer.unref && match.timer.unref();
}
function resolveMatch(r, match) {
  const { a, b } = match.moves, res = R.exchange(a.m, a.p, b.m, b.p);
  if (res > 0) match.score.a++; else if (res < 0) match.score.b++;
  match.played = (match.played || 0) + 1; // draws count too: they were played
  match.moves = {};
  for (const id of [match.a, match.b]) { const q = r.players.get(id); if (q) q.conn.send({ t: 'wr-result', ma: a.m, pa: a.p, mb: b.m, pb: b.p, r: res, score: match.score }); }
  if (match.score.a >= 2 || match.score.b >= 2) endMatch(r, match, match.score.a > match.score.b ? match.a : match.b, '');
  else armMatch(r, match);
}
function endMatch(r, match, winner, reason) {
  clearTimeout(match.timer); r.matches.delete(match.id);
  const message = { t: 'wr-end', id: match.id, winner, playerIds: [match.a, match.b], reason, rounds: match.played || 0 };
  for (const id of [match.a, match.b]) { const q = r.players.get(id); if (q) { q.match = null; q.conn.send(message); } }
  awardWrestlingResult(r, message);
}
function leave(conn) {
  arena.leave(conn);
  const p = conn.player, r = conn.room;
  conn.player = null; conn.room = null;
  if (!p || !r || r.players.get(p.id) !== p) return Promise.resolve();
  if (!roomStore.sharedRealtime && p.match && r.matches.get(p.match)) {
    const match = r.matches.get(p.match);
    endMatch(r, match, match.a === p.id ? match.b : match.a, 'left');
  }
  r.players.delete(p.id);
  const removed = roomStore.removePresence(INSTANCE_ID, p.id).catch((error) => console.error('Could not remove player presence:', error.message));
  const endedWrestling = endSharedWrestlingOnLeave(r, p.id);
  logChat(r, { k: 'leave', id: p.id, n: p.name });
  broadcast(r, { t: 'leave', id: p.id });
  census();
  return Promise.all([removed, endedWrestling]);
}

/** A family was erased (family.js): its children leave every room here and lose their gers and chat lines in memory too,
 *  so the next save does not write them back. Their sockets get `erased` (the client stops retrying and forgets the family) and close. */
async function eraseLive(ids) {
  const gone = new Set(ids);
  board.clear();
  arena.gone(ids);
  await arenaStore.deleteAccounts(ids).catch((error) => console.error('Could not drop erased arena accounts:', error.message)); // PostgreSQL: already gone with their identities
  await googleAuth.forget(ids).catch((error) => console.error('Could not drop erased Google links:', error.message));
  for (const connection of [...conns]) {
    if (!connection.player || !gone.has(connection.player.id)) continue;
    await leave(connection);
    connection.send({ t: 'error', why: 'erased' });
    setTimeout(() => connection.socket.destroy(), 50);
  }
  for (const r of rooms.values()) {
    for (const id of gone) if (r.remotePlayers.delete(id)) broadcastLocal(r, { t: 'leave', id });
    for (const i of scrubRoom(r, gone).freed) broadcast(r, { t: 'lot', act: 'free', i, lot: null });
  }
  for (const state of Object.values(saved)) if (state && typeof state === 'object') scrubRoom(state, gone);
  markDirty();
  void persist();
}

// ---------------------------------------------------------------- start
const server = http.createServer(serveStatic);
server.on('upgrade', upgrade);
let tries = 0;
server.on('error', (err) => {
  if (err.code === 'EADDRINUSE' && PORT !== 0 && tries < 10) {
    // the port is taken (often another copy of Naadamy): say so and use the next free port
    const busy = actualPort;
    http.get({ host: '127.0.0.1', port: busy, path: '/api/hello', timeout: 800 }, (r) => { let b = ''; r.on('data', (d) => (b += d)); r.on('end', () => { if (/naadamy/.test(b)) console.log(`  Note: Naadamy is already running on port ${busy}. You can just open http://localhost:${busy}`); }); }).on('error', () => {});
    tries++; actualPort++;
    console.log(`  Port ${busy} is busy, trying ${actualPort}…`);
    return setTimeout(() => server.listen(actualPort, '0.0.0.0'), 150);
  }
  console.error('  Could not start the server:', err.message);
  process.exit(1);
});
server.on('listening', () => {
  const port = actualPort = server.address().port, ips = lanIPs();
  console.log(`\n  \x1b[1mNaadamy is running!\x1b[0m\n\n  On this computer:  http://localhost:${port}`);
  ips.forEach((ip) => console.log(`  Same Wi-Fi:        \x1b[1mhttp://${ip}:${port}\x1b[0m`));
  if (ips.length && process.stdout.isTTY) { console.log('\n  Friends on the same Wi-Fi can scan this with their phone camera:\n'); console.log(QR.toTerminal(`http://${ips[0]}:${port}/`).split('\n').map((l) => '  ' + l).join('\n')); }
  console.log(basar.enabled ? `\n  Basar AI: on (${basar.model})` : '\n  Basar AI: offline mode (add ANTHROPIC_API_KEY to a .env file to turn on the AI buddy)');
  console.log('\n  If friends cannot connect: some school/guest Wi-Fi blocks devices from');
  console.log('  seeing each other. Use a phone hotspot or a home router instead.');
  console.log('\n  Press Ctrl+C to stop.\n');
  if (process.send) process.send({ port });
});
roomStore.load().then(async (state) => {
  saved = state;
  await arenaStore.init(); // after room-store's tables: accounts belong to player identities
  await googleAuth.init(); // likewise: a Google link belongs to a player identity
  void voice.init(); // the private Supabase bucket, if missing
  await roomStore.startRealtime(INSTANCE_ID, handleRealtime, () => { // events sent while the listener was down are gone: re-derive erased families and who is online
    resyncRealtime({ roomStore, rooms, saved: () => saved, localIds: () => [...conns].filter((c) => c.player).map((c) => c.player.id), handle: handleRealtime })
      .catch((error) => console.error('Could not resync after the realtime listener returned:', error.message));
  });
  server.listen(PORT, '0.0.0.0');
  parentEmail.start();
}).catch(async (error) => {
  console.error('Could not initialize room storage:', error.message);
  try { await roomStore.close(); } catch (closeError) { console.error('Could not close room storage:', closeError.message); }
  process.exit(1);
});
