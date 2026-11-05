// The arena's storage (server/arena-store.js), the same checks on JSON files and, with ARENA_TEST_DATABASE_URL set (a
// throwaway PostgreSQL database: its tables are dropped first), on PostgreSQL: usernames are unique (case and lookalike
// letters included) until renamed or deleted, friends, Elo ratings and records, head-to-head and the ranked board.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { mkdtempSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const require = createRequire(import.meta.url);
const { createRoomStore } = require('../server/room-store.js');
const { createArenaStore, applyResult, eloDelta } = require('../server/arena-store.js');
const { checkName, nameKey } = require('../server/accounts.js');
const results = [];
const ok = (label) => results.push('✓ ' + label);

// ---------------------------------------------------------------- names and ratings
assert.equal(checkName('  Bat_99 ').name, 'Bat_99');
assert.equal(checkName('ab').why, 'invalid');
assert.equal(checkName('a b c').why, 'invalid');
assert.equal(checkName('Батболд').name, 'Батболд');
assert.equal(checkName('Bатbold').why, 'mixed');
assert.equal(checkName('Admin').why, 'reserved');
assert.equal(nameKey('BAT'), nameKey('bat'));
assert.equal(nameKey('ВАТ'), nameKey('bat'), 'Cyrillic В-А-Т looks like Latin BAT');
assert.notEqual(nameKey('Бат'), nameKey('bat'));
ok('usernames: 3–16 letters, digits or _, Latin or Cyrillic (not mixed), not reserved; case and lookalikes are the same name');
assert.equal(eloDelta(1000, 1000, 1, 32), 16);
assert.equal(eloDelta(1000, 1400, 1, 32), 29);
const [wa, wb] = applyResult({ rating: 1000, wins: 0, losses: 0, draws: 0, streak: -2, best: 0, matches: 3 }, { rating: 1000, wins: 0, losses: 0, draws: 0, streak: 3, best: 3, matches: 3 }, 0, 32);
assert.deepEqual([wa.rating, wa.wins, wa.streak, wa.best, wa.matches], [1016, 1, 1, 1, 4]);
assert.deepEqual([wb.rating, wb.losses, wb.streak, wb.best], [984, 1, -1, 3]);
ok('Elo: an upset pays more; streaks and best streaks count');

async function suite(label, roomStore, store) {
  const ids = Array.from({ length: 4 }, (_, i) => `t${i}-${crypto.randomBytes(4).toString('hex')}`);
  for (const id of ids) await roomStore.authenticatePlayer(id, crypto.randomBytes(32).toString('hex'));
  const [a, b, c, d] = ids;
  const acc = (id, username) => store.createAccount({ id, username, key: nameKey(username), hash: 'aa', salt: 'bb' });
  assert.equal(await acc(a, 'Bat'), 'ok');
  assert.equal(await acc(b, 'bat'), 'taken');
  assert.equal(await acc(b, 'ВАТ'), 'taken');
  assert.equal(await acc(a, 'Other'), 'has-account');
  assert.equal(await acc(b, 'Dorj'), 'ok');
  assert.equal(await acc(c, 'Sukh'), 'ok');
  assert.equal(await acc(d, 'Tsetseg'), 'ok');
  assert.equal((await store.accountByKey(nameKey('BAT'))).id, a);
  ok(`${label}: a username can be taken once (any case, any lookalike)`);
  assert.equal(await store.renameAccount(b, 'Bat', nameKey('Bat')), 'taken');
  assert.equal(await store.renameAccount(a, 'Batka', nameKey('Batka')), 'ok');
  assert.equal(await store.accountByKey(nameKey('Bat')), null);
  assert.equal(await store.renameAccount(b, 'Bat', nameKey('Bat')), 'ok', 'the old name is free once renamed');
  assert.equal(await store.renameAccount(b, 'Dorj', nameKey('Dorj')), 'ok');
  ok(`${label}: renaming frees the old name for anyone`);

  assert.equal(await store.friendRequest(a, b), 'sent');
  assert.equal(await store.friendRequest(a, b), 'already');
  assert.equal(await store.friendAccept(a, b), false, 'only the asked one accepts');
  assert.equal(await store.friendAccept(b, a), true);
  assert.equal(await store.areFriends(a, b), true);
  assert.equal(await store.friendRequest(c, a), 'sent');
  assert.equal(await store.friendRequest(a, c), 'accepted', 'asking back accepts');
  assert.equal(await store.friendRequest(d, a), 'sent');
  const fa = await store.friendsOf(a);
  assert.deepEqual(fa.map((f) => [f.username, f.status, f.incoming]).sort(), [['Dorj', 'friends', false], ['Sukh', 'friends', false], ['Tsetseg', 'pending', true]]);
  assert.equal(await store.friendRemove(a, d), true, 'declining a request removes it');
  assert.equal(await store.friendRemove(a, d), false);
  ok(`${label}: friend requests, accepting, asking back and declining`);

  let r = await store.recordMatch({ id: 'm1-' + a, mode: 'ranked', a, b, winner: 0, rounds: [{ w: 0 }], k: 32 });
  assert.deepEqual([r.before[0].rating, r.after[0].rating, r.after[1].rating], [1000, 1016, 984]);
  r = await store.recordMatch({ id: 'm2-' + a, mode: 'friend', a: b, b: a, winner: 1, rounds: [], k: 24 });
  r = await store.recordMatch({ id: 'm3-' + a, mode: 'friend', a, b: c, winner: -1, rounds: [], k: 24 });
  r = await store.recordMatch({ id: 'm4-' + a, mode: 'cpu', a: c, b: null, winner: 0, rounds: [], k: 0 });
  const sa = await store.stats(a);
  assert.deepEqual([sa.wins, sa.losses, sa.draws, sa.matches, sa.streak, sa.best], [2, 0, 1, 3, 0, 2]);
  assert.equal((await store.stats(c)).matches, 1, 'a CPU spar does not count in the stats');
  assert.deepEqual(await store.headToHead(a, [b, c]), { [b]: { w: 2, l: 0 }, [c]: { w: 0, l: 0 } });
  ok(`${label}: bouts update both players' rating, record and streak once; a draw counts; CPU spars don't`);

  const board = await store.globalBoard(d, 50);
  const mine = board.filter((row) => ids.includes(row.id));
  assert.deepEqual(mine.map((row) => row.username), ['Batka', 'Sukh', 'Dorj'], 'by rating; Tsetseg has no bouts');
  assert.ok(mine[0].rank < mine[1].rank && mine[1].rank < mine[2].rank);
  ok(`${label}: the board ranks players with bouts by rating`);

  assert.equal(await store.deleteAccounts([a]), 1);
  assert.equal(await store.accountById(a), null);
  assert.equal(await store.renameAccount(d, 'Batka', nameKey('Batka')), 'ok', 'a deleted name is free');
  assert.equal(await store.areFriends(a, b), false);
  assert.deepEqual((await store.friendsOf(b)).length, 0, 'friendships go with the account');
  assert.equal((await store.stats(a)).matches, 0, 'and its stats');
  const hist = await store.recentMatches(b, 10);
  assert.ok(hist.length >= 2 && hist.every((m) => m.a !== a && m.b !== a), 'its bouts stay in the record without its id');
  ok(`${label}: deleting an account frees its name and drops its friends and stats`);
}

{
  const dir = mkdtempSync(join(tmpdir(), 'naadamy-arena-'));
  const roomStore = createRoomStore({ dataDir: dir, databaseUrl: '' });
  await suite('JSON', roomStore, createArenaStore({ dataDir: dir }));
}
if (process.env.ARENA_TEST_DATABASE_URL) {
  const roomStore = createRoomStore({ dataDir: mkdtempSync(join(tmpdir(), 'naadamy-arena-pg-')), databaseUrl: process.env.ARENA_TEST_DATABASE_URL });
  await roomStore.pool.query('DROP TABLE IF EXISTS naadamy_arena_matches, naadamy_arena_stats, naadamy_friends, naadamy_accounts CASCADE');
  await roomStore.load();
  const store = createArenaStore({ pool: roomStore.pool });
  await store.init();
  await store.init(); // twice: the server restarts
  const open = await roomStore.pool.query(`SELECT relname FROM pg_class WHERE relname = ANY($1::text[]) AND NOT relrowsecurity`,
    [['naadamy_accounts', 'naadamy_friends', 'naadamy_arena_stats', 'naadamy_arena_matches']]);
  assert.deepEqual(open.rows, [], 'row level security on every arena table (closed to the Supabase REST API)');
  ok('PostgreSQL: row level security is on for every arena table');
  await suite('PostgreSQL', roomStore, store);
  // two players taking one name at the same moment: exactly one gets it
  const x = 'race-' + crypto.randomBytes(4).toString('hex'), y = 'race-' + crypto.randomBytes(4).toString('hex');
  for (const id of [x, y]) await roomStore.authenticatePlayer(id, crypto.randomBytes(32).toString('hex'));
  const both = await Promise.all([x, y].map((id) => store.createAccount({ id, username: 'Racer', key: nameKey('Racer'), hash: 'a', salt: 'b' })));
  assert.deepEqual([...both].sort(), ['ok', 'taken']);
  const holder = both[0] === 'ok' ? x : y;
  ok('PostgreSQL: two players taking one name at once: only one gets it (unique index)');
  // an account goes with its player identity (a family erased)
  assert.ok(await store.accountById(holder));
  await roomStore.pool.query('DELETE FROM naadamy_identities WHERE id = $1', [holder]);
  assert.equal(await store.accountById(holder), null);
  ok('PostgreSQL: an account is deleted with its player identity');
  await roomStore.close();
} else results.push('- PostgreSQL skipped (set ARENA_TEST_DATABASE_URL to a throwaway database)');

console.log(results.join('\n'));
