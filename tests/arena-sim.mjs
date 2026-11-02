// The arena's fight sim (server/arena-sim.js): the same inputs give the same bout, and each option has its answer:
// strikes beat grabs, braces beat strikes, a well-timed brace parries, grabs beat braces, and in the clinch a read throw
// is turned over while a guess is punished by the jolt. Rounds end by a fall or the clock; two rounds win the bout.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const S = require('../server/arena-sim.js');
const { createBot } = require('../server/arena-bot.js');
const { B } = S;
const results = [];
const ok = (label) => results.push('✓ ' + label);

/** A bout past the intro, the wrestlers `gap` apart in the middle of the ring. */
function bout(gap = 100) {
  const m = S.createMatch();
  while (m.phase !== 'fight') S.step(m);
  m.f[0].x = 600 - Math.floor(gap / 2); m.f[1].x = m.f[0].x + gap;
  return m;
}
/** Runs ticks with these inputs: each entry is [held, pressed] for a, then for b. Collects every event. */
function run(m, n, a = [0, 0], b = [0, 0], events = []) {
  for (let i = 0; i < n; i++) {
    S.input(m, 0, a[0], i === 0 ? a[1] : 0); S.input(m, 1, b[0], i === 0 ? b[1] : 0);
    events.push(...S.step(m));
  }
  return events;
}
const kinds = (events) => events.map((e) => e.k);

// ---------------------------------------------------------------- determinism
{
  const play = () => {
    const m = S.createMatch(), x = createBot(0, 3, 11), y = createBot(1, 2, 22), trace = [];
    for (let i = 0; i < 6000 && m.phase !== 'over'; i++) {
      const p = x.think(m), q = y.think(m);
      S.input(m, 0, p.held, p.pressed); S.input(m, 1, q.held, q.pressed); S.step(m);
      trace.push(JSON.stringify(S.snapshot(m)));
    }
    return { trace, result: m.result };
  };
  const one = play(), two = play();
  assert.deepEqual(one.trace, two.trace);
  assert.ok(one.result, 'a CPU bout ends');
  assert.ok(one.result.wins[0] === 2 || one.result.wins[1] === 2 || one.result.rounds.length === 5);
  ok('the same inputs give the same bout, tick for tick, to the end');
}
// ---------------------------------------------------------------- strikes
{
  const m = bout(100);
  const ev = run(m, 5, [0, B.L]);
  assert.equal(m.f[1].bal, S.BAL, 'nothing lands during the startup');
  ev.push(...run(m, 1));
  const hit = ev.find((e) => e.k === 'hit');
  assert.ok(hit, 'a shove lands on its 6th tick'); assert.equal(hit.d, S.MOVES.shove.dmg);
  assert.equal(m.f[1].st, 'hit');
  ok(`a shove lands after ${S.MOVES.shove.su} ticks of startup for ${S.MOVES.shove.dmg} balance`);
}
{
  const m = bout(100);
  const ev = run(m, 5, [0, B.L]);
  ev.push(...run(m, 3, [0, B.H]));
  ev.push(...run(m, 20));
  assert.deepEqual(kinds(ev).filter((k) => k === 'hit' || k === 'sweep'), ['hit', 'sweep']);
  const sweep = ev.find((e) => e.k === 'sweep');
  assert.equal(sweep.c, 2, 'the sweep is the second hit of a combo');
  assert.equal(sweep.d, Math.floor(S.MOVES.sweep.dmg * 85 / 100), 'the second hit is scaled down 15%');
  assert.ok(['trip', 'down'].includes(m.f[1].st));
  ok('a shove that lands cancels into a sweep: a two-hit combo, scaled, that knocks down');
}
{
  const m = bout(100);
  run(m, 10, [B.BR, 0]); // braced well before the shove comes, so it is a block, not a parry
  const ev = run(m, 12, [B.BR, 0], [0, B.L]);
  assert.ok(kinds(ev).includes('block'));
  assert.equal(m.f[0].bal, S.BAL, 'a brace takes no balance');
  assert.ok(m.f[0].sta < S.STA, 'it costs stamina');
  ok('bracing blocks a strike: no balance lost, some stamina');
}
{
  const m = bout(100);
  run(m, 2, [0, 0], [0, B.L]); // b starts a shove; a braces 3 ticks before it lands
  const ev = run(m, 6, [B.BR, B.BR], [0, 0]);
  assert.ok(kinds(ev).includes('parry'));
  assert.equal(m.f[1].st, 'staggered');
  assert.equal(m.f[0].bal, S.BAL);
  ok('a brace pressed just before the hit parries it and staggers the attacker');
}
{
  const m = bout(100);
  run(m, 1, [B.BR, B.BR]); run(m, 4, [B.BR, 0]); run(m, 1, [B.BR, B.BR]); // pressing again so soon does not count
  const ev = run(m, 12, [B.BR, 0], [0, B.L]);
  assert.ok(!kinds(ev).includes('parry') && kinds(ev).includes('block'));
  ok('mashing the brace does not parry: a second press within 20 ticks is only a block');
}
// ---------------------------------------------------------------- grabs and the clinch
{
  const m = bout(70);
  const ev = run(m, 10, [B.BR, 0], [0, B.G]);
  assert.ok(kinds(ev).includes('grab'));
  assert.ok(m.clinch && m.clinch.lead === 1);
  ok('a grab goes through a brace into the clinch');
}
{
  const m = bout(70);
  const ev = run(m, 10, [0, B.L], [0, B.G]);
  assert.ok(kinds(ev).includes('hit') && !kinds(ev).includes('grab'));
  assert.equal(m.clinch, null);
  ok('a shove interrupts a grab that is still reaching');
}
{
  const m = bout(70);
  run(m, 3, [0, B.DB], [0, 0]);
  const ev = run(m, 10, [0, 0], [0, B.G]);
  assert.ok(!kinds(ev).includes('grab'));
  ok('a back-dash slips a grab');
}
function clinch(lead = 0) {
  const m = bout(70);
  run(m, 10, [0, lead === 0 ? B.G : 0], [0, lead === 1 ? B.G : 0]);
  assert.ok(m.clinch && m.clinch.lead === lead);
  return m;
}
{
  const m = clinch(0);
  const towardA = m.f[0].face > 0 ? B.RIGHT : B.LEFT, towardB = m.f[1].face > 0 ? B.RIGHT : B.LEFT;
  run(m, 8, [towardA, B.H]);                      // a starts the hook; b sees the tell and answers in time
  const ev = run(m, 14, [0, 0], [towardB, B.G]);  // and answers with toward + grip
  assert.ok(kinds(ev).includes('reversal'));
  assert.equal(m.clinch.lead, 1, 'the grip changes hands');
  assert.equal(m.f[0].bal, S.BAL - S.CLINCH.reversalDmg);
  ok('a hook read and answered with the same direction + grip is turned over');
}
{
  const m = clinch(0);
  const towardA = m.f[0].face > 0 ? B.RIGHT : B.LEFT;
  run(m, 6, [towardA, B.H]);
  const ev = run(m, 14, [0, 0], [0, B.G]); // b answers as if it were a lift
  const th = ev.find((e) => e.k === 'throw');
  assert.ok(th && th.kind === 'hook' && th.guess);
  assert.equal(th.d, Math.floor(S.THROWS.hook.dmg * S.CLINCH.guessBonus));
  assert.equal(m.clinch, null);
  ok('the wrong answer: the throw lands harder');
}
{
  const m = clinch(0);
  run(m, 1, [0, 0], [0, B.G]);              // b guesses before a commits
  const ev = run(m, 8, [0, B.L]);           // a jolts
  assert.ok(kinds(ev).includes('jolt-catch'));
  assert.equal(m.f[1].st, 'offbal');
  ok('guessing early is punished: the jolt catches it and leaves the guesser off balance');
}
{
  const m = clinch(0);
  const ev = run(m, 14, [0, 0], [0, B.L]);
  assert.ok(kinds(ev).includes('escape'));
  assert.equal(m.clinch, null);
  ok('the shove-off escapes the clinch');
}
{
  const m = clinch(0);
  run(m, 2, [0, 0], [0, B.L]);                 // b starts to escape
  const ev = run(m, 20, [0, B.H]);             // a lifts: it lands before the escape finishes? (lift is slower: the escape wins)
  assert.ok(kinds(ev).includes('escape') && !kinds(ev).includes('throw'));
  ok('a slow lift loses to an escape already under way');
}
// ---------------------------------------------------------------- meter, stamina, rounds
{
  const m = bout(80);
  const ev = run(m, 12, [0, B.SP]);
  assert.ok(!kinds(ev).includes('super'));
  m.f[0].met = S.MET;
  const ev2 = run(m, 14, [0, B.SP]);
  assert.ok(kinds(ev2).includes('super') && kinds(ev2).includes('super-throw'));
  assert.ok(m.f[0].met < S.MET / 2, 'the meter was spent (what is left came from the throw itself)');
  ok("the champion's throw needs a full eagle meter and spends it");
}
{
  const m = bout(300);
  m.f[0].sta = 50;
  const ev = run(m, 3, [0, B.H]);
  assert.ok(kinds(ev).includes('tired'));
  assert.equal(m.f[0].st === 'sweep', false);
  ok('a move costs stamina: without enough, it does not come out');
}
{
  const m = bout(100);
  m.f[1].bal = 40;
  const ev = run(m, 8, [0, B.L]);
  assert.ok(kinds(ev).includes('ko'));
  assert.equal(m.wins[0], 1);
  run(m, S.KO_TICKS + 2);
  assert.equal(m.phase, 'intro'); assert.equal(m.round, 2);
  assert.equal(m.f[1].bal, S.BAL, 'balance is full again for the next round');
  while (m.phase !== 'fight') S.step(m);
  m.f[1].bal = 40; m.f[0].x = 550; m.f[1].x = 650;
  run(m, 8, [0, B.L]); run(m, S.KO_TICKS + 2);
  assert.equal(m.phase, 'over'); assert.equal(m.result.winner, 0);
  ok('a fall ends the round; two rounds win the bout');
}
{
  const m = bout(400);
  m.f[0].bal = 900;
  m.timer = 3;
  const ev = run(m, 3);
  assert.ok(kinds(ev).includes('time'));
  assert.equal(m.wins[1], 1);
  ok('when time runs out the wrestler with more balance takes the round');
}
{
  const m = bout(100);
  m.f[1].x = S.WALL_R; m.f[0].x = S.WALL_R - 100;
  run(m, 8, [0, B.L]);
  assert.equal(m.f[1].x, S.WALL_R);
  assert.ok(m.f[0].x < S.WALL_R - 100, 'at the edge the attacker is pushed back instead');
  ok('wrestlers stay in the ring; at its edge a hit pushes the attacker back');
}
{
  // a level 3 CPU beats a level 1 one more often than not
  let wins = 0;
  for (let seed = 1; seed <= 6; seed++) {
    const m = S.createMatch(), x = createBot(0, 3, seed), y = createBot(1, 1, seed + 100);
    for (let i = 0; i < 12000 && m.phase !== 'over'; i++) { const p = x.think(m), q = y.think(m); S.input(m, 0, p.held, p.pressed); S.input(m, 1, q.held, q.pressed); S.step(m); }
    if (m.result && m.result.winner === 0) wins++;
  }
  assert.ok(wins >= 4, `level 3 won ${wins} of 6`);
  ok(`skill shows: a sharper CPU (level 3) beat a slower one (level 1) ${wins} times of 6`);
}

console.log(results.join('\n'));
console.log(`\n${results.length} arena sim checks passed`);
