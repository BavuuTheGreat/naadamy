'use strict';
/* Wrestler accounts for the online arena: POST /api/account. A player (an id and its device ticket) takes a unique
   username and a password; with them the player logs in on any other device, which gets its own ticket for the same id
   (same coins, XP, clothes, cloud save, friends and rating). A username belongs to one account until its owner renames or
   deletes it. Passwords are kept only as salted scrypt hashes; 5 wrong tries lock the account for 15 minutes, doubling
   each time, at most a day. See docs/PROTOCOL.md, "Online arena". */

const crypto = require('node:crypto');
const clientIp = require('./client-ip.js');

const PLAYER_ID = /^[\w-]{4,24}$/;
const PLAYER_TICKET = /^[a-f0-9]{64}$/i;
const NAME = /^[A-Za-z0-9_Ѐ-ӿ]{3,16}$/;
const LATIN = /[A-Za-z]/, CYRILLIC = /[Ѐ-ӿ]/;
const RESERVED = new Set(['admin', 'administrator', 'naadamy', 'basar', 'moderator', 'mod', 'system', 'server', 'support', 'root', 'null', 'undefined', 'cpu', 'bot']);
// Cyrillic letters that look like Latin ones count as those for uniqueness, so "Бат" and a lookalike can't both exist
const LOOKALIKE = { а: 'a', в: 'b', е: 'e', ё: 'e', к: 'k', м: 'm', н: 'h', о: 'o', р: 'p', с: 'c', т: 't', у: 'y', ү: 'y', х: 'x', і: 'i', ј: 'j', ѕ: 's', һ: 'h', ԁ: 'd', ӏ: 'l' };
const PASSWORD_MIN = 6, PASSWORD_MAX = 64;
const TRIES = 5, LOCK_MS = 15 * 60 * 1000, LOCK_MAX = 24 * 60 * 60 * 1000;
const PER_MINUTE = 30;

/** The name as typed, trimmed, or '' when it isn't a valid username; with `why` the reason. */
function checkName(value) {
  const name = typeof value === 'string' ? value.trim() : '';
  if (!NAME.test(name)) return { name: '', why: 'invalid' };
  if (LATIN.test(name) && CYRILLIC.test(name)) return { name: '', why: 'mixed' };
  const key = nameKey(name);
  if (RESERVED.has(key)) return { name: '', why: 'reserved' };
  return { name, key };
}
/** What makes two usernames the same: lower case, lookalike Cyrillic letters read as Latin. */
function nameKey(name) {
  return [...name.toLowerCase()].map((c) => LOOKALIKE[c] || c).join('');
}
const passwordOk = (p) => typeof p === 'string' && p.length >= PASSWORD_MIN && p.length <= PASSWORD_MAX;

function hashPassword(password, salt) {
  return new Promise((resolve, reject) => {
    crypto.scrypt(password.normalize('NFC'), Buffer.from(salt, 'hex'), 32, (error, key) => (error ? reject(error) : resolve(key.toString('hex'))));
  });
}
async function passwordRecord(password) {
  const salt = crypto.randomBytes(16).toString('hex');
  return { hash: await hashPassword(password, salt), salt };
}

module.exports = function createAccounts(roomStore, arenaStore, { now = () => Date.now(), cloudOf = async () => null, onChange = () => {}, onDelete = () => {} } = {}) {
  const hits = new Map();
  function allow(ip) {
    const t = now(), h = hits.get(ip);
    if (!h || t - h.at > 60_000) { hits.set(ip, { at: t, n: 1 }); if (hits.size > 5000) for (const [k, v] of hits) if (t - v.at > 60_000) hits.delete(k); return true; }
    return ++h.n <= PER_MINUTE;
  }

  /** Checks the password of an account and counts a wrong try: null when right, else the refusal. */
  async function verify(account, password) {
    const t = now();
    if (account.until > t) return { ok: false, reason: 'locked', wait: Math.ceil((account.until - t) / 1000) };
    if (!passwordOk(password)) return { ok: false, reason: 'wrong', left: TRIES - account.fails };
    const hash = await hashPassword(password, account.salt);
    if (crypto.timingSafeEqual(Buffer.from(hash, 'hex'), Buffer.from(account.hash, 'hex'))) {
      if (account.fails || account.locks) await arenaStore.updateAccount(account.id, { fails: 0, locks: Math.max(0, account.locks - 1) });
      return null;
    }
    const fails = account.fails + 1;
    if (fails >= TRIES) {
      const locks = account.locks + 1, until = t + Math.min(LOCK_MAX, LOCK_MS * 2 ** Math.min(20, locks - 1));
      await arenaStore.updateAccount(account.id, { fails: 0, locks, until });
      return { ok: false, reason: 'locked', wait: Math.ceil((until - t) / 1000) };
    }
    await arenaStore.updateAccount(account.id, { fails });
    return { ok: false, reason: 'wrong', left: TRIES - fails };
  }
  /** The account of a device that plays as `id` (its ticket checked), or a refusal. */
  async function owner(message) {
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (!PLAYER_ID.test(id) || !PLAYER_TICKET.test(ticket)) return { refusal: { ok: false, reason: 'invalid' } };
    if (!(await roomStore.authenticatePlayer(id, ticket))) return { refusal: { ok: false, reason: 'identity' } };
    const account = await arenaStore.accountById(id);
    return account ? { account } : { refusal: { ok: false, reason: 'no-account' } };
  }

  async function check(message) {
    const { name, key, why } = checkName(message.username);
    if (!name) return { ok: true, available: false, reason: why };
    const holder = await arenaStore.accountByKey(key);
    return holder ? { ok: true, available: false, reason: 'taken' } : { ok: true, available: true };
  }
  async function register(message) {
    const id = String(message.id || ''), ticket = String(message.ticket || '');
    if (!PLAYER_ID.test(id) || !PLAYER_TICKET.test(ticket)) return { ok: false, reason: 'invalid' };
    const { name, key, why } = checkName(message.username);
    if (!name) return { ok: false, reason: why };
    if (!passwordOk(message.password)) return { ok: false, reason: 'weak-password' };
    if (nameKey(message.password) === key) return { ok: false, reason: 'weak-password' };
    if (!(await roomStore.authenticatePlayer(id, ticket))) return { ok: false, reason: (await roomStore.isErased(id)) ? 'erased' : 'identity' };
    const record = await passwordRecord(message.password);
    const result = await arenaStore.createAccount({ id, username: name, key, ...record });
    if (result !== 'ok') return { ok: false, reason: result };
    onChange(id);
    return { ok: true, username: name };
  }
  async function login(message) {
    const { name, key } = checkName(message.username);
    if (!name || !passwordOk(message.password)) return { ok: false, reason: 'wrong' };
    const account = await arenaStore.accountByKey(key);
    if (!account) return { ok: false, reason: 'wrong' };
    const refusal = await verify(account, message.password);
    if (refusal) return refusal;
    const ticket = crypto.randomBytes(32).toString('hex'); // this device's own ticket for the player
    await roomStore.addDeviceTicket(account.id, ticket);
    return { ok: true, id: account.id, ticket, username: account.username, cloud: await cloudOf(account.id) };
  }
  async function me(message) {
    const { account, refusal } = await owner(message);
    if (refusal && refusal.reason !== 'no-account') return refusal;
    return { ok: true, account: account ? { username: account.username } : null };
  }
  async function rename(message) {
    const { account, refusal } = await owner(message);
    if (refusal) return refusal;
    const { name, key, why } = checkName(message.username);
    if (!name) return { ok: false, reason: why };
    const wrong = await verify(account, message.password);
    if (wrong) return wrong;
    const result = await arenaStore.renameAccount(account.id, name, key);
    if (result !== 'ok') return { ok: false, reason: result };
    onChange(account.id);
    return { ok: true, username: name };
  }
  async function password(message) {
    const { account, refusal } = await owner(message);
    if (refusal) return refusal;
    if (!passwordOk(message.next) || nameKey(message.next) === account.key) return { ok: false, reason: 'weak-password' };
    const wrong = await verify(account, message.password);
    if (wrong) return wrong;
    await arenaStore.updateAccount(account.id, await passwordRecord(message.next));
    return { ok: true };
  }
  async function remove(message) {
    const { account, refusal } = await owner(message);
    if (refusal) return refusal;
    if (typeof message.confirm !== 'string' || nameKey(message.confirm.trim()) !== account.key) return { ok: false, reason: 'confirm' };
    const wrong = await verify(account, message.password);
    if (wrong) return wrong;
    await arenaStore.deleteAccounts([account.id]);
    onDelete(account.id);
    return { ok: true };
  }

  const ACTS = { check, register, login, me, rename, password, delete: remove };
  function handle(req, res) {
    const send = (status, body) => { res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }); res.end(JSON.stringify(body)); };
    if (req.method !== 'POST') return send(405, { ok: false, reason: 'method' });
    let raw = '';
    req.on('data', (chunk) => { raw += chunk; if (raw.length > 4000) req.destroy(); });
    req.on('end', async () => {
      let message; try { message = JSON.parse(raw); } catch { return send(400, { ok: false, reason: 'invalid' }); }
      if (!message || typeof message !== 'object') return send(400, { ok: false, reason: 'invalid' });
      if (!allow(clientIp(req))) return send(429, { ok: false, reason: 'busy' });
      if (typeof message.act !== 'string' || !Object.hasOwn(ACTS, message.act)) return send(400, { ok: false, reason: 'invalid' });
      try {
        send(200, await ACTS[message.act](message));
      } catch (error) {
        console.error('Account:', error.message);
        send(500, { ok: false, reason: 'storage' });
      }
    });
  }

  return { handle, checkName, nameKey };
};
module.exports.checkName = checkName;
module.exports.nameKey = nameKey;
