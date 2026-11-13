/* Нутаг world: the map layout and what blocks walking, as the server checks positions (Unity has its own copy in World.cs). */
(function (root, factory) {
  const TG = root.TG || {};
  if (!TG.vrules && typeof module === 'object' && module.exports) TG.vrules = require('./rules.js');
  const api = factory(TG);
  if (typeof module === 'object' && module.exports) module.exports = api;
  else { root.TG = root.TG || {}; root.TG.vworld = api; }
})(typeof window !== 'undefined' ? window : globalThis, function (TG) {
  const R = TG.vrules, W = R.W, H = R.H;
  let seed = 20261021;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);

  // ---------------------------------------------------------------- map
  // g grass · p path · s plaza · w water · d dock · k trampled earth (dairy yard) · f fence · T tree · B building · r rock
  const g = Array.from({ length: H }, () => Array(W).fill('g'));
  const set = (x, y, c) => { if (x >= 0 && y >= 0 && x < W && y < H) g[y][x] = c; };
  const rect = (x0, y0, x1, y1, c) => { for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) set(x, y, c); };
  for (let x = 0; x < W; x++) { set(x, 0, 'T'); set(x, 1, 'T'); set(x, H - 1, 'T'); }
  for (let y = 0; y < H; y++) { set(0, y, 'T'); set(W - 1, y, 'T'); }
  for (let y = 2; y < H - 1; y++) for (let x = 1; x < W - 1; x++) if (((x + 0.5 - 30) / 4.6) ** 2 + ((y + 0.5 - 5.5) / 3.3) ** 2 <= 1) set(x, y, 'w');
  rect(1, 12, W - 2, 13, 'p'); rect(17, 2, 18, H - 2, 'p');
  rect(15, 10, 20, 15, 's');
  rect(19, 5, 24, 5, 'p'); rect(25, 5, 26, 5, 'd');
  rect(4, 6, 4, 11, 'p'); rect(11, 6, 11, 11, 'p'); rect(22, 11, 23, 11, 'p');
  // хот айл: the fenced herding and dairy yard, its ground trampled bare by the animals
  for (let x = 3; x <= 14; x++) { set(x, 15, 'f'); set(x, 23, 'f'); }
  for (let y = 15; y <= 23; y++) { set(3, y, 'f'); set(14, y, 'f'); }
  rect(4, 16, 13, 22, 'k');
  rect(9, 14, 10, 15, 'p');
  // sheep pasture
  for (let x = 22; x <= 33; x++) { set(x, 18, 'f'); set(x, 24, 'f'); }
  for (let y = 18; y <= 24; y++) { set(22, y, 'f'); set(33, y, 'f'); }
  rect(27, 14, 27, 18, 'p');
  // ger neighborhood (south): 8 lots along a lane
  const LOT_X = [3, 7, 11, 21, 25, 29, 33, 37];
  rect(2, 31, 42, 32, 'p');
  rect(41, 14, 42, 32, 'p'); // east road: closes the loop around town (used by the horse race)
  // decorative pond in the south meadow
  for (let y = 33; y < H - 1; y++) for (let x = 1; x < W - 1; x++) if (((x + 0.5 - 30.5) / 4.2) ** 2 + ((y + 0.5 - 35.8) / 2.2) ** 2 <= 1) set(x, y, 'w');
  // Naadam wrestling arena (east): sand ring, reached from the main road
  const ARENA = { cx: 47.5, cy: 12.5, rx: 6, ry: 4.6 };
  for (let y = 2; y < H - 1; y++) for (let x = 36; x < W - 1; x++) if (((x + 0.5 - ARENA.cx) / ARENA.rx) ** 2 + ((y + 0.5 - ARENA.cy) / ARENA.ry) ** 2 <= 1) set(x, y, 'a');
  rect(46, 18, 47, 24, 'p');
  rect(48, 20, 50, 20, 'p'); // lane to the Mongol script academy

  const objects = [
    { id: 'home', kind: 'ger', x: 3, y: 3, w: 3, h: 3, door: { x: 4, y: 6 }, accent: '#2F6BFF', sign: '🏠' },
    { id: 'arcade', kind: 'ger', x: 10, y: 3, w: 3, h: 3, door: { x: 11, y: 6 }, accent: '#E9A91F', sign: '📚' },
    { id: 'shop', kind: 'shop', x: 21, y: 8, w: 4, h: 3, door: { x: 22, y: 11 } },
    { id: 'board', kind: 'board', x: 19, y: 10, w: 2, h: 1, door: { x: 19, y: 11 } },
    { id: 'well', kind: 'well', x: 12, y: 17, w: 1, h: 1, door: { x: 12, y: 18 } },
    // the dairy yard: mares and foals on the зэл line, cows, goats, the outdoor stove, the aaruul rack and the airag churn
    { id: 'zel', kind: 'zel', x: 4, y: 16, w: 5, h: 1, door: { x: 6, y: 17 } },
    { id: 'cow1', kind: 'cow', x: 9, y: 17, w: 2, h: 1, door: { x: 9, y: 18 } },
    { id: 'cow2', kind: 'cow', x: 10, y: 19, w: 2, h: 1, door: { x: 10, y: 20 } },
    { id: 'goat1', kind: 'goat', x: 5, y: 19, w: 1, h: 1, door: { x: 5, y: 20 } },
    { id: 'goat2', kind: 'goat', x: 7, y: 19, w: 1, h: 1, door: { x: 7, y: 20 } },
    { id: 'churn', kind: 'churn', x: 5, y: 21, w: 1, h: 1, door: { x: 6, y: 22 } },
    { id: 'tulga', kind: 'tulga', x: 8, y: 21, w: 1, h: 1, door: { x: 8, y: 22 } },
    { id: 'rack', kind: 'rack', x: 11, y: 21, w: 2, h: 1, door: { x: 11, y: 22 } },
    { id: 'lantern1', kind: 'lantern', x: 15, y: 10, w: 1, h: 1 },
    { id: 'lantern2', kind: 'lantern', x: 15, y: 15, w: 1, h: 1 },
    { id: 'lantern3', kind: 'lantern', x: 20, y: 15, w: 1, h: 1 },
    { id: 'bleachers', kind: 'bleachers', x: 42, y: 5, w: 12, h: 2 },
    { id: 'zasuul', kind: 'npc', x: 47, y: 8, w: 1, h: 1, door: { x: 47, y: 9 } },
    { id: 'ovoo', kind: 'ovoo', x: 46, y: 25, w: 2, h: 2, door: { x: 46, y: 24 } },
    { id: 'uya', kind: 'uya', x: 34, y: 14, w: 3, h: 1, door: { x: 35, y: 13 } },
    { id: 'archery', kind: 'archery', x: 45, y: 33, w: 8, h: 3, door: { x: 44, y: 34 } },
    { id: 'general', kind: 'general', x: 36, y: 10, w: 1, h: 1, door: { x: 36, y: 11 } },   // the general: the defend-the-homeland campaign
    { id: 'historian', kind: 'historian', x: 29, y: 10, w: 1, h: 1, door: { x: 29, y: 11 } }, // the historian: history hunts across the aimags
    { id: 'academy', kind: 'academy', x: 49, y: 17, w: 4, h: 3, door: { x: 50, y: 20 } }, // Mongol script academy
    { id: 'emee', kind: 'emee', x: 6, y: 6, w: 1, h: 1, door: { x: 6, y: 7 } },
    { id: 'felt', kind: 'felt', x: 19, y: 20, w: 2, h: 1, door: { x: 19, y: 21 } },       // felt making: beat, wet and roll the wool
    { id: 'craftsman', kind: 'npcq', x: 15, y: 29, w: 1, h: 1, door: { x: 15, y: 30 } }, // Grandpa Craftsman: the ger's wooden parts
    // townspeople with quests (see NPCS in village.js)
    { id: 'herder', kind: 'npcq', x: 25, y: 16, w: 1, h: 1, door: { x: 25, y: 17 } },
    { id: 'fisher', kind: 'npcq', x: 23, y: 6, w: 1, h: 1, door: { x: 23, y: 7 } },
    { id: 'farmer', kind: 'npcq', x: 12, y: 14, w: 1, h: 1, door: { x: 12, y: 13 } },
    { id: 'teacher', kind: 'npcq', x: 13, y: 6, w: 1, h: 1, door: { x: 13, y: 7 } },
    { id: 'trainer', kind: 'npcq', x: 38, y: 15, w: 1, h: 1, door: { x: 38, y: 16 } },
    { id: 'archer', kind: 'npcq', x: 44, y: 32, w: 1, h: 1, door: { x: 44, y: 31 } },
    { id: 'lantern4', kind: 'lantern', x: 40, y: 11, w: 1, h: 1 },
    { id: 'lantern5', kind: 'lantern', x: 40, y: 14, w: 1, h: 1 },
    { id: 'setsen', kind: 'statue', x: 40, y: 6, w: 1, h: 1, door: { x: 40, y: 7 } }, // the stone statue of Setsen, in the open space left of the stands
    ...LOT_X.map((x, i) => ({ id: 'lot' + i, kind: 'lot', i, x, y: 28, w: 3, h: 3, door: { x: x + 1, y: 31 } })),
  ];
  for (const o of objects) for (let y = o.y; y < o.y + o.h; y++) for (let x = o.x; x < o.x + o.w; x++) set(x, y, 'B');
  // trees and rocks
  const clearAround = (x, y) => { for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) { const c = (g[y + dy] || [])[x + dx]; if (c && c !== 'g' && c !== 'T') return false; } return true; };
  const reserved = (x, y) => (x >= 13 && x <= 22 && y >= 9 && y <= 16) || (y >= 2 && y <= 7 && x >= 2 && x <= 14) || (x >= 22 && x <= 33 && y >= 18 && y <= 25) || (x >= 3 && x <= 14 && y >= 14 && y <= 24) || (x >= 38 && y >= 3 && y <= 19) || (x >= 1 && x <= 41 && y >= 26 && y <= 33) || (x >= 43 && x <= 50 && y >= 22 && y <= 28) || (x >= 43 && x <= 54 && y >= 30 && y <= 37);
  for (let y = 2; y < H - 1; y++) for (let x = 1; x < W - 1; x++) {
    if (g[y][x] !== 'g' || reserved(x, y) || !clearAround(x, y)) continue;
    const r = rnd();
    if (r < 0.1) { set(x, y, 'T'); rnd(); } // the second draw (a pine or not, once) keeps the seeded map the same
    else if (r < 0.13) set(x, y, 'r');
  }

  const blocked = (x, y) => x < 0 || y < 0 || x >= W || y >= H || 'TwfBr'.includes(g[y][x]);

  const api = { W, H, g, blocked };
  return api;
});
