'use strict';
/* Private rooms: POST /api/room. `open` gives a player (an id and its device ticket) a new room with a random code of 6
   letters, which they pass to their friends. A `join` with a 6-letter code only works for a room that was opened, so a
   mistyped code answers `no-room` instead of making an empty village. 10 wrong codes from one address lock that address out
   of private rooms for 5 minutes, so codes can't be guessed. Other codes (NUTAG, and rooms of released builds) still work as
   before. See docs/PROTOCOL.md, "Private rooms". */

const crypto = require('node:crypto');
const clientIp = require('./client-ip.js');

const LETTERS = 'ABCDEFGHJKMNPQRSTUVWXYZ'; // no I, L or O: they are easy to misread when a code is read aloud
const LENGTH = 6, TRIES = 20, PER_MINUTE = 30;
const WRONG_MAX = 10, LOCK_MS = 5 * 60 * 1000;
const PRIVATE = /^[A-Z]{6}$/;
const PLAYER_ID = /^[\w-]{4,24}$/;
const PLAYER_TICKET = /^[a-f0-9]{64}$/i;

const isPrivate = (code) => PRIVATE.test(code);
const newCode = () => Array.from({ length: LENGTH }, () => LETTERS[crypto.randomInt(LETTERS.length)]).join('');

/** `known(code)`: does this server already hold the room? `opened(code)`: keep a room that was just opened. */
module.exports = function createRoomCodes(roomStore, { known = () => false, opened = () => {}, now = () => Date.now(), perMinute = PER_MINUTE } = {}) {
  const hits = new Map();
  const wrong = new Map(); // address -> { n, at, until }

  /** Seconds this address must still wait before trying a private room; 0 when it may. */
  function lockedFor(ip) {
    const w = wrong.get(ip), t = now();
    if (!w || !w.until) return 0;
    if (w.until > t) return Math.ceil((w.until - t) / 1000);
    wrong.delete(ip);
    return 0;
  }
  /** One more wrong code. A right code does not clear the count: a guesser could type their own room's code in between. */
  function wrongCode(ip) {
    const t = now(); let w = wrong.get(ip);
    if (!w || t - w.at > LOCK_MS) w = { n: 0, at: t, until: 0 };
    w.n++; w.at = t;
    if (w.n >= WRONG_MAX) w.until = t + LOCK_MS;
    wrong.delete(ip); wrong.set(ip, w); // the oldest stay first, for the cap below
    if (wrong.size > 5000) wrong.delete(wrong.keys().next().value);
    return lockedFor(ip);
  }
  function allow(ip) {
    const t = now(), h = hits.get(ip);
    if (!h || t - h.at > 60_000) { hits.set(ip, { at: t, n: 1 }); if (hits.size > 5000) for (const [k, v] of hits) if (t - v.at > 60_000) hits.delete(k); return true; }
    return ++h.n <= perMinute;
  }
  const exists = async (code) => known(code) || Boolean(await roomStore.getRoomState(code));

  async function open(message) {
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (!PLAYER_ID.test(id) || !PLAYER_TICKET.test(ticket) || !(await roomStore.authenticatePlayer(id, ticket))) return { ok: false, reason: 'identity' };
    for (let i = 0; i < TRIES; i++) {
      const code = newCode();
      if (await exists(code)) continue;
      await roomStore.mutateRoom(code, { lots: [], chat: [] }, () => ({ changed: true })); // written now: other servers look for it, and a crash must not lose a code already given out
      opened(code);
      return { ok: true, code };
    }
    return { ok: false, reason: 'busy' };
  }

  const ACTS = { open };
  function handle(req, res) {
    const reply = (status, body) => { res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }); res.end(JSON.stringify(body)); };
    if (req.method !== 'POST') return reply(405, { ok: false, reason: 'method' });
    let raw = '';
    req.on('data', (chunk) => { raw += chunk; if (raw.length > 1000) req.destroy(); });
    req.on('end', async () => {
      let message; try { message = JSON.parse(raw); } catch { return reply(400, { ok: false, reason: 'invalid' }); }
      if (!message || typeof message !== 'object') return reply(400, { ok: false, reason: 'invalid' });
      if (!allow(clientIp(req))) return reply(429, { ok: false, reason: 'busy' });
      if (typeof message.act !== 'string' || !Object.hasOwn(ACTS, message.act)) return reply(400, { ok: false, reason: 'invalid' });
      try {
        reply(200, await ACTS[message.act](message));
      } catch (error) {
        console.error('Rooms:', error.message);
        reply(500, { ok: false, reason: 'storage' });
      }
    });
  }

  return { handle, exists, isPrivate, lockedFor, wrongCode };
};
