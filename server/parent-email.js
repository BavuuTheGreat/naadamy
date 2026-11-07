'use strict';
/* Weekly parent email: a grown-up adds an email for the family (family.js `set-email`, behind the family PIN). It first gets
   a link to confirm it (double opt-in); once confirmed, a short report of what each child did that week, every 7 days, each
   with a one-click link to stop. Sent with Resend (https://resend.com); without RESEND_API_KEY and EMAIL_FROM it is off.
   Never in a mail: the family code, the PIN or the recovery code. See docs/PROTOCOL.md. */

const crypto = require('node:crypto');
const R = require('../web/js/village/rules.js');

const HOUR = 60 * 60 * 1000, DAY = 24 * HOUR, WEEK = 7 * DAY;
const CONFIRM_MS = 2 * DAY; // a confirm link works for 48 h
const STOP_MS = 90 * DAY; // a report's stop link works for 90 days
const RETRY_MS = 6 * HOUR; // a report Resend refused for a passing reason is tried again after this…
const TRIES = 3; // …at most this many times in a row; then it waits for next week
// confirm mails a day: per family (on its record), and in the store's counts per address, per sender address and in all
const SENDS_PER_DAY = 3, PER_IP = 5, ALL = 200;
const TOKEN = /^[a-f0-9]{64}$/;
const EMAIL = /^[^\s@<>()[\]\\,;:"]{1,64}@[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$/;
const RENDER_URL = 'https://pinequest-s5-episode-2-team-8.onrender.com';
// lesson ids of the cloud save (cleanCloud `learn`) the report can name; any other id is shown as it is
const LESSONS = { ger: ['Монгол гэр', 'The Mongol ger'], idee: ['Цагаан идээ', 'Dairy foods'], customs: ['Ёс заншил', 'Customs'],
  nuuts: ['Нууц товчоо', 'The Secret History'], history: ['Түүх', 'History'], naadam: ['Наадам', 'Naadam'], nomad: ['Нүүдэлчин ахуй', 'Nomad life'],
  arts: ['Урлаг', 'Arts'] };
// the GET pages: no scripts, nothing loaded, a form that posts only back here, never in a frame
const PAGE_CSP = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";

const hash = (token) => crypto.createHash('sha256').update(token).digest('hex');
const newToken = () => crypto.randomBytes(32).toString('hex');
/** " Kid@Gmail.COM " → "kid@gmail.com"; anything else → "". */
function emailText(value) {
  const email = typeof value === 'string' ? value.trim().toLowerCase() : '';
  return email.length <= 254 && EMAIL.test(email) ? email : '';
}
/** "kid@gmail.com" → "k***@gmail.com": enough for a grown-up to know which address it is. */
const masked = (email) => email[0] + '***' + email.slice(email.indexOf('@'));
const esc = (text) => String(text).replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
const shown = (r) => ({ to: masked(r.email), confirmed: r.confirmed });

/** The stored record (room-store's getFamilyEmail) cleaned, or null. `snapshot` is { at, kids: { id: { xp, lessons } } }:
 *  what each child had when the last report (or the confirm) was made, so a report counts only that week. `pending` is
 *  a report Resend refused, { token, key, rows, snapshot }: its retry is the same mail (same stop link, rows and
 *  Idempotency-Key, so Resend sends it at most once); `fails` counts the refusals in a row. */
function clean(r) {
  if (!r || !emailText(r.email)) return null;
  const int = (v) => (Number.isSafeInteger(v) && v > 0 ? v : 0), snap = r.snapshot && typeof r.snapshot === 'object' ? r.snapshot : {};
  const p = r.pending, pending = p && TOKEN.test(p.token || '') && TOKEN.test(p.key || '') && Array.isArray(p.rows) && p.snapshot && typeof p.snapshot === 'object'
    ? { token: p.token, key: p.key, rows: p.rows.slice(0, 8), snapshot: p.snapshot } : null;
  return { email: emailText(r.email), confirmed: r.confirmed === true, confirmHash: TOKEN.test(r.confirmHash || '') ? r.confirmHash : '',
    confirmUntil: int(r.confirmUntil), sends: Array.isArray(r.sends) ? r.sends.filter((t) => int(t)).slice(-SENDS_PER_DAY) : [],
    reportAt: int(r.reportAt), dueAt: int(r.dueAt), fails: int(r.fails), pending,
    snapshot: { at: int(snap.at), kids: snap.kids && typeof snap.kids === 'object' && !Array.isArray(snap.kids) ? snap.kids : {} } };
}

/** Sends one mail with Resend's HTTP API; throws an error with `status` when it is not accepted. */
function resend(apiKey, from) {
  return async ({ to, subject, html, text, headers, key }) => {
    const answer = await fetch('https://api.resend.com/emails', {
      method: 'POST', signal: AbortSignal.timeout(15000),
      headers: { authorization: `Bearer ${apiKey}`, 'content-type': 'application/json', ...(key ? { 'idempotency-key': key } : {}) },
      body: JSON.stringify({ from, to: [to], subject, html, text, ...(headers ? { headers } : {}) }),
    });
    await answer.body?.cancel().catch(() => {}); // the answer's body is not needed: free the socket
    if (!answer.ok) throw Object.assign(new Error(`Resend answered ${answer.status}`), { status: answer.status });
  };
}

module.exports = function createParentEmail(roomStore, { env = process.env, now = () => Date.now(), send } = {}) {
  const apiKey = env.RESEND_API_KEY || '', from = env.EMAIL_FROM || '';
  const enabled = Boolean(send || (apiKey && from));
  if (apiKey && !from && !send) console.warn('Parent email: off, RESEND_API_KEY is set but EMAIL_FROM is not');
  const deliver = send || resend(apiKey, from);
  const base = (env.PUBLIC_URL || RENDER_URL).replace(/\/+$/, '');
  let timer = null, busy = false;

  /** What each child of the family has now: { id: { name, xp, cloud } }, or null when there is no family. */
  async function kidsOf(code) {
    const family = await roomStore.getFamily(code);
    if (!family) return null;
    const kids = {};
    for (const m of family.members) {
      kids[m.id] = { name: m.name, xp: (await roomStore.getWallet(m.id)).xp, cloud: R.cleanCloud((await roomStore.getProgress(m.id, {})).state.cloud) };
    }
    return kids;
  }
  const doneLessons = (cloud) => Object.keys(cloud.learn).filter((id) => cloud.learn[id].done);
  const snapshotOf = (kids, at) => ({ at, kids: Object.fromEntries(Object.entries(kids).map(([id, k]) => [id, { xp: k.xp, lessons: doneLessons(k.cloud) }])) });

  function confirmMail(to, token) {
    const link = `${base}/api/email/confirm?t=${token}`;
    const mn = ['Сайн байна уу! Naadamy-гийн долоо хоног бүрийн тайланг энэ хаягаар авахын тулд доорх холбоос дээр дарна уу (48 цагийн дотор).',
      'Та үүнийг хүсээгүй бол энэ захидлыг үл тоомсорлоорой: өөр юу ч ирэхгүй.'];
    const en = ['Hello! To get the weekly Naadamy report at this address, please open the link below (within 48 hours).',
      'If you did not ask for this, just ignore this email: nothing else will be sent.'];
    return {
      to, subject: 'Naadamy: имэйлээ баталгаажуулна уу / Please confirm your email', key: hash('confirm:' + token),
      text: [mn[0], link, mn[1], '', en[0], link, en[1]].join('\n'),
      html: `<p>${esc(mn[0])}</p><p><a href="${esc(link)}">Баталгаажуулах / Confirm</a></p><p>${esc(mn[1])}</p><hr><p>${esc(en[0])}</p><p>${esc(en[1])}</p>`,
    };
  }

  /** The week of each child: XP gained, lessons finished, badges (`ach`) and stamps earned since the snapshot (a stamp
   *  dated after `t` is not believed). A child new to the report starts from what they have now. */
  function weekOf(kids, snapshot, t) {
    return Object.entries(kids).map(([id, k]) => {
      const before = Object.hasOwn(snapshot.kids, id) ? snapshot.kids[id] : { xp: k.xp, lessons: doneLessons(k.cloud) };
      const had = new Set(Array.isArray(before.lessons) ? before.lessons : []);
      const count = (list) => Object.values(list).filter((at) => at >= snapshot.at && at <= t).length;
      return { name: k.name, xp: Math.max(0, k.xp - (Number(before.xp) || 0)), lessons: doneLessons(k.cloud).filter((l) => !had.has(l)),
        badges: count(k.cloud.ach), stamps: count(k.cloud.stamps) };
    });
  }

  function reportMail(to, rows, token) {
    const link = `${base}/api/email/stop?t=${token}`;
    const named = (lessons, i) => lessons.map((l) => (LESSONS[l] ? LESSONS[l][i] : l)).join(', ');
    const lineMn = (r) => `${r.name}: +${r.xp} XP, ${r.lessons.length} хичээл дуусгасан${r.lessons.length ? ` (${named(r.lessons, 0)})` : ''}, ${r.badges} тэмдэг, ${r.stamps} тамга`;
    const lineEn = (r) => `${r.name}: +${r.xp} XP, ${r.lessons.length} lesson${r.lessons.length === 1 ? '' : 's'} finished${r.lessons.length ? ` (${named(r.lessons, 1)})` : ''}, ${r.badges} badge${r.badges === 1 ? '' : 's'}, ${r.stamps} stamp${r.stamps === 1 ? '' : 's'}`;
    const mn = ['Сайн байна уу! Энэ долоо хоногт хүүхдүүд тань Naadamy-д:', 'Энэ захидлыг авахаа болих бол:'];
    const en = ['Hello! This week in Naadamy your children did:', 'To stop these emails:'];
    const list = (f) => `<ul>${rows.map((r) => `<li>${esc(f(r))}</li>`).join('')}</ul>`;
    return {
      to, subject: 'Naadamy: долоо хоногийн тайлан / Weekly report',
      headers: { 'List-Unsubscribe': `<${link}>`, 'List-Unsubscribe-Post': 'List-Unsubscribe=One-Click' },
      text: [mn[0], ...rows.map((r) => '- ' + lineMn(r)), '', en[0], ...rows.map((r) => '- ' + lineEn(r)), '', `${mn[1]} / ${en[1]}`, link].join('\n'),
      html: `<p>${esc(mn[0])}</p>${list(lineMn)}<hr><p>${esc(en[0])}</p>${list(lineEn)}<hr><p style="font-size:small">${esc(mn[1])} / ${esc(en[1])} <a href="${esc(link)}">Болих / Stop</a></p>`,
    };
  }

  /** For the grown-ups' screen: the masked address and whether it is confirmed (with `full`), else only `{ confirmed }`;
   *  null when there is none. */
  async function shownFor(code, full) {
    const r = clean(await roomStore.getFamilyEmail(code));
    return r ? (full ? shown(r) : { confirmed: r.confirmed }) : null;
  }

  /** set-email after the PIN was checked: stored unconfirmed, and a confirm link sent. At most 3 a day per family and per
   *  address, 5 per sender address (`ip`) and 200 in all, counted in the store (so on every server, and after a restart). */
  async function set(code, address, ip = '') {
    const email = emailText(address), t = now();
    if (!email) return { ok: false, reason: 'invalid' };
    if (!enabled) return { ok: false, reason: 'email-off' };
    const current = clean(await roomStore.getFamilyEmail(code));
    if (current && current.email === email && current.confirmed) return { ok: true, email: shown(current) }; // nothing to confirm again
    const sends = current ? current.sends.filter((at) => t - at < DAY) : [];
    if (sends.length >= SENDS_PER_DAY) return { ok: false, reason: 'email-busy', wait: Math.ceil((sends[0] + DAY - t) / 1000) };
    const quota = await roomStore.takeEmailSend([{ key: 'to:' + hash(email), max: SENDS_PER_DAY }, { key: 'ip:' + hash(String(ip)), max: PER_IP }, { key: 'all', max: ALL }], t, DAY);
    if (!quota.ok) return { ok: false, reason: 'email-busy', wait: Math.ceil(quota.wait / 1000) };
    const token = newToken();
    const result = await roomStore.mutateFamilyEmail(code, (stored, hasFamily) => {
      if (!hasFamily) return { result: { ok: false, reason: 'no-family' } };
      const r = clean(stored), recent = (r ? r.sends : []).filter((at) => t - at < DAY);
      if (recent.length >= SENDS_PER_DAY) return { result: { ok: false, reason: 'email-busy', wait: Math.ceil((recent[0] + DAY - t) / 1000) } }; // raced another
      const next = { email, confirmed: false, confirmHash: hash(token), confirmUntil: t + CONFIRM_MS, sends: [...recent, t], reportAt: 0, dueAt: 0, fails: 0, pending: null, snapshot: {} };
      return { record: next, clearTokens: true, token: { hash: next.confirmHash, kind: 'confirm', until: next.confirmUntil }, result: { ok: true, email: shown(next) } };
    });
    if (!result.ok) return result;
    try { await deliver(confirmMail(email, token)); } catch (error) { console.error('Parent email: confirm mail not sent:', error.message); return { ok: false, reason: 'email-failed' }; }
    return result;
  }

  /** remove-email after the PIN was checked: the address and its links are forgotten. */
  function remove(code) {
    return roomStore.mutateFamilyEmail(code, (stored, hasFamily) => (hasFamily ? { record: stored ? null : undefined, result: { ok: true } } : { result: { ok: false, reason: 'no-family' } }));
  }

  /** The confirm link, posted: the reports start, and the first one counts the week from now. 'ok' or 'bad'. */
  async function confirm(token) {
    const h = TOKEN.test(token) ? hash(token) : '', link = h && (await roomStore.familyEmailToken(h, now()));
    if (!link || link.kind !== 'confirm') return 'bad';
    const kids = await kidsOf(link.code);
    if (!kids) return 'bad';
    return roomStore.mutateFamilyEmail(link.code, (stored) => {
      const r = clean(stored), t = now();
      if (!r || r.confirmed || r.confirmHash !== h || r.confirmUntil <= t) return { result: 'bad' }; // the address changed meanwhile
      return { record: { ...r, confirmed: true, confirmHash: '', confirmUntil: 0, reportAt: t, dueAt: t + WEEK, snapshot: snapshotOf(kids, t) }, clearTokens: true, result: 'ok' };
    });
  }

  /** A report's stop link, posted: the address is forgotten. 'ok' or 'bad'. */
  async function stop(token) {
    const link = TOKEN.test(token) ? await roomStore.familyEmailToken(hash(token), now()) : null;
    if (!link || link.kind !== 'stop') return 'bad';
    return roomStore.mutateFamilyEmail(link.code, (stored) => ({ record: stored ? null : undefined, result: 'ok' }));
  }

  /** One claimed report (the claim already moved the next one a week on). A week where no child did anything sends
   *  nothing. Else its stop link is stored, the mail sent, and the week's snapshot kept. Resend refusing it for a passing
   *  reason (5xx, 429, no answer) tries the same mail again in 6 hours, at most 3 times; any other refusal waits a week. */
  async function report(code, claimed, t) {
    const r = clean(claimed), kids = r && r.confirmed ? await kidsOf(code) : null;
    if (!kids) return false;
    const keep = (change) => roomStore.mutateFamilyEmail(code, (stored) => {
      const x = clean(stored);
      return x && x.confirmed && x.email === r.email ? { record: { ...x, ...change }, result: true } : { result: false };
    });
    const rows = r.pending ? r.pending.rows : weekOf(kids, r.snapshot, t);
    if (!rows.some((w) => w.xp || w.lessons.length || w.badges || w.stamps)) { await keep({ reportAt: t, snapshot: snapshotOf(kids, t) }); return false; }
    const pending = r.pending || { token: newToken(), key: newToken(), rows, snapshot: snapshotOf(kids, t) };
    const still = await roomStore.mutateFamilyEmail(code, (stored) => {
      const x = clean(stored);
      if (!x || !x.confirmed || x.email !== r.email) return { result: false };
      return { record: { ...x, pending }, token: r.pending ? undefined : { hash: hash(pending.token), kind: 'stop', until: t + STOP_MS }, result: true };
    });
    if (!still) return false;
    try { await deliver({ ...reportMail(r.email, rows, pending.token), key: pending.key }); } catch (error) {
      const fails = r.fails + 1, passing = !(error.status >= 400 && error.status < 500) || error.status === 429;
      console.error(`Parent email: report not sent (${fails}):`, error.message);
      await keep(passing && fails < TRIES ? { fails, pending, dueAt: t + RETRY_MS } : { fails: 0, pending: null });
      return false;
    }
    await keep({ reportAt: t, snapshot: pending.snapshot, fails: 0, pending: null }); // the next week counts from the first try
    return true;
  }

  /** Sends every report that is due (at most `limit`). Each one is claimed in the store first (claimFamilyReport), so two
   *  servers never send the same one. Returns how many were sent. */
  async function sendDue(limit = 100) {
    if (!enabled || busy) return 0;
    busy = true;
    let sent = 0;
    try {
      for (let i = 0; i < limit; i++) {
        const t = now(), claimed = await roomStore.claimFamilyReport(t, t + WEEK);
        if (!claimed) break;
        try { if (await report(claimed.code, claimed.record, t)) sent++; } catch (error) { console.error('Parent email: report failed:', error.message); }
      }
    } finally { busy = false; }
    return sent;
  }

  /** Checks for due reports every hour (and a minute after start); the timer never keeps the process alive. */
  function start(every = HOUR) {
    if (!enabled || timer) return;
    const tick = () => { sendDue().catch((error) => console.error('Parent email: could not check reports:', error.message)); };
    timer = setInterval(tick, every); timer.unref();
    setTimeout(tick, 60_000).unref();
  }

  /** /api/email/confirm?t= and /api/email/stop?t=: GET (or HEAD) changes nothing, as mail scanners open links: it shows a
   *  button that POSTs back. POST confirms or stops (also the one-click List-Unsubscribe-Post). A tiny page in both languages. */
  async function handle(req, res, url) {
    const page = (status, mn, en, form = '') => {
      res.writeHead(status, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store', 'referrer-policy': 'no-referrer', 'content-security-policy': PAGE_CSP });
      res.end(`<!doctype html><html lang="mn"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Naadamy</title>`
        + `<body style="font-family:sans-serif;max-width:32em;margin:3em auto;padding:0 1em;line-height:1.5"><h1>Naadamy</h1><p>${mn}</p><p>${en}</p>${form}</body></html>`);
    };
    const stopping = url.pathname === '/api/email/stop', token = String(url.searchParams.get('t') || '');
    const gone = () => (stopping
      ? page(404, 'Энэ холбоос ажиллахгүй байна (аль хэдийн зогсоосон байж магадгүй).', 'This link does not work (the emails may already be stopped).')
      : page(404, 'Энэ холбоос хуучирсан эсвэл ашиглагдсан байна. Хаягаа тоглоомоос дахин нэмж болно.', 'This link is old or already used. You can add the address again in the game.'));
    if (req.method === 'GET' || req.method === 'HEAD') {
      if (!TOKEN.test(token)) return gone();
      const form = `<form method="post" action="${esc(`${url.pathname}?t=${token}`)}"><button type="submit" style="font-size:1.2em;padding:.5em 1.5em">${stopping ? 'Болих / Stop' : 'Баталгаажуулах / Confirm'}</button></form>`;
      return stopping
        ? page(200, 'Энэ хаяг руу тайлан ирэхээ болих бол товчийг дарна уу.', 'Press the button to stop the reports to this address.', form)
        : page(200, 'Долоо хоног бүрийн тайланг авах бол товчийг дарна уу.', 'Press the button to get the weekly report.', form);
    }
    if (req.method !== 'POST') return page(405, 'Болохгүй.', 'Not allowed.');
    if (req.resume) req.resume(); // the body (List-Unsubscribe=One-Click) is not needed
    try {
      if (stopping) return (await stop(token)) === 'ok' ? page(200, 'Болсон: энэ хаяг руу тайлан дахиж ирэхгүй.', 'Done: no more reports will be sent to this address.') : gone();
      return (await confirm(token)) === 'ok' ? page(200, 'Баярлалаа! Долоо хоног бүр тайлан ирнэ.', 'Thank you! The report will come every week.') : gone();
    } catch (error) {
      console.error('Parent email:', error.message);
      return page(500, 'Алдаа гарлаа, дараа дахин оролдоно уу.', 'Something went wrong, please try again later.');
    }
  }

  return { enabled, address: emailText, shownFor, set, remove, confirm, stop, sendDue, start, handle };
};
module.exports.resend = resend;
module.exports.emailText = emailText;
