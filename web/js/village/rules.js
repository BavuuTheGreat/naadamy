/* Нутаг (village) rules shared by the browser and server.js.
   Same file runs in both so the shared data always agrees. */
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else { root.TG = root.TG || {}; root.TG.vrules = api; }
})(typeof self !== 'undefined' ? self : this, function () {
  const W = 56, H = 40, LOTS = 8;
  const VERSION = 14; // bump when the game and server must change together (moves, messages), so an old running server is noticed
  const T = (mn, en) => ({ mn, en });

  const DAILY_QUEST_REWARDS = {
    milk: { coins: 20, xp: 25 }, dairy: { coins: 25, xp: 35 }, fish: { coins: 25, xp: 30 },
    basar: { coins: 15, xp: 25 }, quiz: { coins: 25, xp: 35 }, lesson: { coins: 30, xp: 40 },
    together: { coins: 40, xp: 50 }, pet: { coins: 10, xp: 20 }, sell: { coins: 15, xp: 20 },
    race: { coins: 25, xp: 35 }, archery: { coins: 25, xp: 35 }, wrestle: { coins: 25, xp: 35 },
    etiquette: { coins: 30, xp: 40 }, defend: { coins: 40, xp: 60 }, eat: { coins: 15, xp: 20 },
  };
  const DAILY_ACTIVITY_REWARDS = {
    rest: { coins: 5, xp: 10 }, snack: { coins: 0, xp: 5 }, churn: { coins: 0, xp: 5 },
  };
  // The most coins and XP one player's device can add to the server account in one Mongolia day (offline play and online play together).
  // What goes over stays on the device and is uploaded on a later day, so offline progress is never thrown away.
  const DAILY_EARN_CAP = { coins: 1500, xp: 2000 };
  const DAILY_QUEST_POOL = ['milk', 'dairy', 'milk', 'fish', 'basar', 'quiz', 'pet', 'sell', 'basar', 'quiz', 'eat'];
  const SEASON_TASKS = {
    spring: ['pet', 'milk'], summer: ['race', 'archery', 'fish'],
    autumn: ['dairy', 'sell'], winter: ['quiz', 'wrestle'],
  };
  function dailyQuests(playerId, day) {
    if (typeof playerId !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(day || '')) return [];
    const dayTime = Date.parse(`${day}T00:00:00Z`);
    if (!Number.isFinite(dayTime) || new Date(dayTime).toISOString().slice(0, 10) !== day) return [];
    let hash = 0;
    for (const ch of day + playerId) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
    const pool = [...DAILY_QUEST_POOL], selected = [];
    while (selected.length < 3 && pool.length) {
      hash = (hash * 1103515245 + 12345) >>> 0;
      const key = pool.splice(hash % pool.length, 1)[0];
      if (!selected.includes(key)) selected.push(key);
    }
    const [year, month, date] = day.split('-').map(Number);
    const season = ['spring', 'summer', 'autumn', 'winter'][Math.floor(Date.UTC(year, month - 1, date) / 864e5) % 4];
    const options = SEASON_TASKS[season].filter((key) => !selected.includes(key));
    if (options.length) selected.unshift({ k: options[hash % options.length], season });
    else {
      const seasonal = selected.find((key) => SEASON_TASKS[season].includes(key));
      if (seasonal) selected[selected.indexOf(seasonal)] = { k: seasonal, season };
    }
    return selected.map((entry) => typeof entry === 'string' ? { k: entry } : entry);
  }

  // Preset, kid-safe phrases only. No free-text chat.
  const PHRASES = [
    T('Сайн уу! 👋', 'Hi! 👋'), T('Хамт тоглоё!', "Let's play!"), T('Баярлалаа! 💛', 'Thank you! 💛'), T('Энд ир!', 'Come here!'),
    T('Саалиа хийе! 🥛', "Let's milk! 🥛"), T('Уралдъя! 🐎', "Let's race! 🐎"), T('Загасчилъя! 🎣', "Let's fish! 🎣"), T('Баяртай! 👋', 'Bye! 👋'),
  ];
  const EMOTES = ['👋', '❤️', '😂', '🎉', '👍', '🐎', '😮', '⭐'];

  const SKINS = ['#F6D3B3', '#EDBB93', '#D9A06E', '#B97A4A'];
  const DEELS = { blue: '#2F6BFF', red: '#E8453C', green: '#12A05F', purple: '#7A5AF8', pink: '#F06CA8', gold: '#E0A21A', teal: '#1FA3A3', brown: '#8A5A33', white: '#F2EEE4', black: '#2D2A33' };
  const TITLES = [
    { id: 'nachin', name: T('Начин', 'Falcon'), need: 5, fact: T('Начин (шонхор) цол Улсын наадамд 5 давж авдаг.', 'The Nachin (falcon) title is earned by winning 5 rounds at the State Naadam.') },
    { id: 'khartsaga', name: T('Харцага', 'Hawk'), need: 6, fact: T('Харцага (хартсага шувуу) цол 6 давсан бөхөд олгогддог.', 'The Khartsaga (hawk) title goes to a wrestler who wins 6 rounds.') },
    { id: 'zaan', name: T('Заан', 'Elephant'), need: 7, fact: T('Заан цол 7 давсан, хүчирхэг бөхийн цол.', 'The Zaan (elephant) title is for winning 7 rounds.') },
    { id: 'garid', name: T('Гарьд', 'Garuda'), need: 8, fact: T('Гарьд бол домгийн хүчит шувуу. 8 давсан бөхөд олгодог.', 'The Garid is a mighty mythical bird, for winning 8 rounds.') },
    { id: 'arslan', name: T('Арслан', 'Lion'), need: 9, fact: T('Наадамд түрүүлсэн бөх Арслан цол хүртэнэ.', 'The wrestler who wins the whole Naadam becomes an Arslan (lion).') },
    { id: 'avarga', name: T('Аварга', 'Titan'), need: 10, fact: T('Хоёр удаа түрүүлсэн Арслан Аварга цол хүртдэг. Монголын хамгийн дээд бөхийн цол!', 'An Arslan who wins twice becomes an Avarga (titan), the highest wrestling title in Mongolia!') },
  ];
  // Wrestling techniques (мэх): real бөх techniques in a circle. Each beats the next half of the circle and loses to the
  // half before it, so there is always a counter. How well you perform the move (its power) can still turn a bad match-up.
  const M = (id, mn, en, fmn, fen) => ({ id, name: T(mn, en), fact: T(fmn, fen) });
  const MOVES = [
    M('push', 'Түлхэх', 'Push', 'Өрсөлдөгчөө мөрөөрөө түлхэж тэнцвэрийг нь алдагдуулна.', 'Drive into your opponent with your shoulder to break their balance.'),
    M('pull', 'Татах', 'Pull', 'Зодгоос нь татаж урагш унагана.', 'Pull them by the zodog so they fall forward.'),
    M('hook', 'Гадуур дэгээ', 'Outside hook', 'Хөлөө өрсөлдөгчийн хөлийн гадуур дэгээдэж унагадаг.', 'Hook your leg around the outside of theirs and trip them.'),
    M('inhook', 'Дотуур дэгээ', 'Inside hook', 'Хөлийн дотуур дэгээдэж хойш нь унагадаг мэх.', 'Hook the inside of their leg to topple them backwards.'),
    M('hip', 'Ташаадах', 'Hip throw', 'Өрсөлдөгчөө ташаан дээгүүрээ эргүүлэн шиддэг.', 'Turn and throw them over your hip.'),
    M('leg', 'Хөл шүүрэх', 'Leg grab', 'Бөхийн хөлийг шүүрэн авч өргөөд унагадаг.', 'Grab their leg, lift it and bring them down.'),
    M('lift', 'Өргөж хаях', 'Lift and throw', 'Хамгийн хүчтэй мэх: өрсөлдөгчөө газраас өргөөд хаядаг.', 'The strongest move: lift them off the ground and throw.'),
    M('havirah', 'Хавирах', 'Khavirakh (leg sweep)', 'Хөлөөрөө өрсөлдөгчийн хөлийг хавирч, тэнцвэрийг нь алдагдуулан унагадаг мэх.', 'Sweep their leg away with yours so they lose balance and fall.'),
    M('humruh', 'Хөмрөх', 'Khömrökh (overturn)', 'Өрсөлдөгчөө урагш нь хөмрүүлж, эргүүлэн унагадаг хүчтэй мэх.', 'A powerful move that tips your opponent forward and over.'),
    M('toigdoh', 'Тойгдох', 'Toigdokh', 'Хав дөрвөлжин барьцнаас хийдэг онцгой сайхан мэх. Тэнцвэр, байрлал чухал.', 'A fine technique done from the square grip. Balance and position matter most.'),
    M('hongodoh', 'Хонгодох', 'Khongodokh (leg take)', 'Өрсөлдөгчийн хөлийг авч унагадаг, жүдогийн мэхтэй төстэй хөлийн мэх.', 'A leg technique that takes their leg away, a little like a judo throw.'),
    M('suga', 'Сугадах', 'Sugadakh (armpit throw)', 'Өрсөлдөгчийн сугаар орж, мөрөн дээгүүрээ эргүүлэн хаядаг мэх.', 'Get in under their arm and turn them over your shoulder.'),
    M('tahim', 'Тахимдах', 'Takhimdakh (knee grab)', 'Өрсөлдөгчийн өвдөгний ар талаас шүүрэн татаж унагадаг мэх.', 'Grab behind their knee and pull them down.'),
  ];
  // the same moves told simply, for the wrestling school (as you'd explain it to a five-year-old)
  const KID = {
    push: T('Мөрөөрөө хүнд хаалга түлхэж байгаа юм шиг түлхэнэ. Найз чинь ганхаад хойшоо унана!', 'Lean in and push with your shoulder, like pushing a big heavy door. Your friend wobbles and falls back!'),
    pull: T('Зодгоос нь бариад тэрэг чирж байгаа юм шиг өөр лүүгээ татна. Тэр урагшаа өвдөг сөхөрнө!', 'Grab their jacket and pull them to you, like pulling a wagon. They tip forward onto their knees!'),
    hook: T('Хөлөө түүний хөлийн гадуур дэгээ шиг ороогоод татна. Тэр бүдэрч унана!', 'Wrap your leg around the outside of theirs, like a hook. Pull, and they trip!'),
    inhook: T('Хөлөө түүний хөлийн дотуур оруулж дэгээднэ. Тэр хөлөө алдаад хойшоо суучихна!', 'Slip your leg inside theirs and hook it. Their feet slide and they sit down backwards!'),
    hip: T('Эргээд ташаагаа урд нь тавина. Толгод давж өнхрөх бөмбөг шиг ташаан дээгүүрээ өнхрүүлнэ.', 'Turn around and put your hip in front. Roll them over it, like a ball rolling over a little hill.'),
    leg: T('Хурдан бөхийж нэг хөлийг нь шүүрнэ. Нэг хөл дээр зогсоход ганхдаг, тэгээд л унана!', 'Bend down fast and grab one leg. Standing on one leg is wobbly, so down they go!'),
    lift: T('Баавгай шиг чанга тэврээд газраас өргөнө. Тэгээд нуруугаар нь зөөлөн газар тавина.', 'Give a big bear hug and lift them off the ground. Then set them down on their back.'),
    havirah: T('Хөлөөрөө шүүр шиг шүүрдэж түүний хөлийг холдуулна. Хөлгүй бол зогсож чадахгүй!', 'Swish your foot like a broom and sweep their foot away. No foot, no standing!'),
    humruh: T('Түүнийг урагш доош нь дарж, хувин хөмрөх шиг хөмрүүлнэ.', 'Push them down and forward so they tip right over, like tipping a bucket.'),
    toigdoh: T('Хоёр гараараа сайн барьж, салхинд зогсох мод шиг хөдлөхгүй бай. Тэгээд эргүүлж унагана.', 'Hold on tight with both hands and stand still like a tree in the wind. Then turn them over.'),
    hongodoh: T('Тэнцвэрээ хадгалж, түүний хөлийг доороос нь шүүрч авна. Жаахан жүдо шиг!', 'Keep your balance and scoop their leg out from under them. It’s a bit like judo!'),
    suga: T('Гүүрэн доогуур орох шиг суган доогуур нь шургаад, мөрөн дээгүүрээ эргүүлнэ.', 'Duck under their arm, like going under a bridge, and roll them over your shoulder.'),
    tahim: T('Өвдөгний ар талаас нь бариад татна. Өвдөг нугардаг болохоор сандал шиг эвхэгдээд унана!', 'Reach behind their knee and pull. Knees bend, so they fold down like a chair!'),
  };
  MOVES.forEach((m) => { m.kid = KID[m.id]; });
  const HALF = Math.floor((MOVES.length - 1) / 2); // each move beats the next half of the circle and loses to the half before it
  MOVES.forEach((m, i) => { m.beats = Array.from({ length: HALF }, (_, k) => MOVES[(i + k + 1) % MOVES.length].id); });
  const beats = (x, y) => { const m = MOVES.find((q) => q.id === x); return !!m && m.beats.includes(y); };
  function exchange(ma, pa, mb, pb) {
    const base = (x, y) => (x === y ? 0.3 : beats(x, y) ? 0.6 : 0);
    const A = base(ma, mb) + 0.8 * Math.max(0, Math.min(1, pa || 0)), B = base(mb, ma) + 0.8 * Math.max(0, Math.min(1, pb || 0));
    return Math.abs(A - B) < 0.04 ? 0 : A > B ? 1 : -1;
  }
  const ROOF = { blue: '#3f76db', red: '#e14b3d', green: '#2ea35f', purple: '#9466e0', orange: '#f39b2c', teal: '#1fa3a3' };
  const DOOR = { red: '#e14b3d', blue: '#3f76db', green: '#2ea35f', gold: '#e9a91f' };
  const FURNITURE = [
    { id: 'tulga', price: 60, lvl: 1, name: T('Тулга (зуух)', 'Stove (tulga)'), fact: T('Гэрийн голд тулга буюу зуух байрладаг. Энэ бол гэр бүлийн галын голомт.', 'The stove sits in the center of the ger. It is the heart of the family hearth.') },
    { id: 'shirdeg', price: 80, lvl: 1, name: T('Ширдэг', 'Felt rug (shirdeg)'), fact: T('Ширдэг бол хээ угалзаар оёсон эсгий хивс.', 'A shirdeg is a felt rug stitched with traditional patterns.') },
    { id: 'or', price: 90, lvl: 2, name: T('Ор', 'Bed'), fact: T('Гэрийн ор ихэвчлэн баруун, зүүн хананы дагуу байдаг.', 'Ger beds usually stand along the side walls.') },
    { id: 'avdar', price: 110, lvl: 2, name: T('Авдар', 'Chest (avdar)'), fact: T('Авдарт гэр бүлийн үнэт зүйлсийг хадгалдаг. Хойморт тавьдаг.', 'The avdar chest keeps family treasures. It stands at the back of the ger (khoimor).') },
    { id: 'morinkhuur', price: 150, lvl: 3, name: T('Морин хуур', 'Horsehead fiddle'), fact: T('Морин хуурыг гэрийн хойморт хүндэтгэлтэй байршуулдаг.', 'The morin khuur is placed with honor at the back of the ger.') },
    { id: 'shagai', price: 40, lvl: 1, name: T('Шагайн уут', 'Bag of shagai'), fact: T('Урт өвлийн оройгоор гэр бүлээрээ шагай тоглодог.', 'Families play shagai on long winter evenings.') },
  ];
  const LOOKS = {
    title: TITLES.map((t) => t.id),
    hat: ['mongol', 'flower', 'cap', 'loovuuz', 'crown', 'kazakh', 'buriad', 'oirat', 'fur'],
    face: ['glasses', 'sun'],
    neck: ['scarf', 'khadag', 'medal'],
    pet: ['lamb', 'puppy', 'kitten', 'camel', 'foal'],
    ride: ['horse'],
    coat: ['bay', 'black', 'white', 'chestnut', 'gray'],
  };

  // Shop catalogue.
  const I = (id, cat, slot, val, price, lvl, e, mn, en) => ({ id, cat, slot, val, price, lvl, e, name: T(mn, en) });
  const ITEMS = [
    I('deel-blue', 'wear', 'deel', 'blue', 0, 1, '🔵', 'Цэнхэр дээл', 'Blue deel'),
    I('deel-red', 'wear', 'deel', 'red', 50, 1, '🔴', 'Улаан дээл', 'Red deel'),
    I('deel-green', 'wear', 'deel', 'green', 50, 1, '🟢', 'Ногоон дээл', 'Green deel'),
    I('deel-pink', 'wear', 'deel', 'pink', 60, 2, '🩷', 'Ягаан дээл', 'Pink deel'),
    I('deel-purple', 'wear', 'deel', 'purple', 90, 2, '🟣', 'Нил ягаан дээл', 'Purple deel'),
    I('deel-gold', 'wear', 'deel', 'gold', 140, 3, '🟡', 'Алтан шар дээл', 'Golden deel'),
    I('hat-cap', 'wear', 'hat', 'cap', 40, 1, '🧢', 'Малгай', 'Cap'),
    I('hat-flower', 'wear', 'hat', 'flower', 45, 1, '🌼', 'Цэцгэн титэм', 'Flower crown'),
    I('hat-mongol', 'wear', 'hat', 'mongol', 70, 2, '🎩', 'Монгол малгай', 'Mongolian hat'),
    I('hat-loovuuz', 'wear', 'hat', 'loovuuz', 120, 3, '🧶', 'Лоовууз', 'Loovuuz fur hat'),
    I('hat-crown', 'wear', 'hat', 'crown', 300, 6, '👑', 'Алтан титэм', 'Golden crown'),
    I('face-glasses', 'wear', 'face', 'glasses', 50, 1, '👓', 'Нүдний шил', 'Glasses'),
    I('face-sun', 'wear', 'face', 'sun', 80, 2, '🕶️', 'Нарны шил', 'Sunglasses'),
    I('neck-scarf', 'wear', 'neck', 'scarf', 40, 1, '🧣', 'Ороолт', 'Scarf'),
    I('neck-khadag', 'wear', 'neck', 'khadag', 70, 2, '💙', 'Хадаг', 'Khadag'),
    I('neck-medal', 'wear', 'neck', 'medal', 200, 5, '🏅', 'Наадмын медаль', 'Naadam medal'),
    I('pet-lamb', 'pet', 'pet', 'lamb', 150, 2, '🐑', 'Хурга', 'Lamb'),
    I('pet-kitten', 'pet', 'pet', 'kitten', 180, 2, '🐈', 'Муур', 'Kitten'),
    I('pet-puppy', 'pet', 'pet', 'puppy', 220, 3, '🐕', 'Банхар гөлөг', 'Bankhar puppy'),
    I('pet-camel', 'pet', 'pet', 'camel', 260, 4, '🐫', 'Ботго', 'Baby camel'),
    I('pet-foal', 'pet', 'pet', 'foal', 300, 5, '🐴', 'Унага', 'Foal'),
    I('rod-2', 'tool', 'rod', 2, 150, 3, '🎣', 'Сайн загасны саваа', 'Better fishing rod'),
  ];

  const now = () => Date.now();

  /** Level from total XP: level 2 at 60 XP, each next level needs 40 more than the last. */
  function level(xp) {
    let l = 1, need = 60, rest = Math.max(0, xp | 0);
    while (rest >= need) { rest -= need; l++; need = 60 + (l - 1) * 40; }
    return { l, cur: rest, need };
  }

  const ROOM_RE = /^[A-Z0-9]{3,8}$/;
  function cleanLook(look) {
    const l = look || {}, out = {};
    out.deel = DEELS[l.deel] ? l.deel : 'blue';
    out.skin = SKINS.includes(l.skin) ? l.skin : SKINS[0];
    out.gender = l.gender === 'girl' ? 'girl' : null;
    for (const k of Object.keys(LOOKS)) out[k] = LOOKS[k].includes(l[k]) ? l[k] : null;
    return out;
  }

  // ---- building a ger by hand: the order, the parts each step uses, and where the parts come from
  const GER_STEPS = ['shal', 'khana', 'khaalga', 'toono', 'uni', 'esgii', 'buurees', 'buslvvr', 'tulga']; // floor, walls, door, crown, roof poles, felt, cover, ropes, stove
  const GER_DONE = GER_STEPS.length;
  const GER_NEEDS = { shal: {}, khana: { khana: 5 }, khaalga: { khaalga: 1 }, toono: { toono: 1, bagana: 2 }, uni: { uni: 9 }, esgii: { esgii: 2 }, buurees: { buurees: 1 }, buslvvr: { buslvvr: 3 }, tulga: {} };
  /** Can this step be done now? stage = how many steps are already done. */
  function gerTry(stage, step, parts) {
    if (stage >= GER_DONE) return { ok: false, why: 'done' };
    if (GER_STEPS[stage] !== step) return { ok: false, why: 'order', next: GER_STEPS[stage] };
    const missing = {};
    for (const [k, n] of Object.entries(GER_NEEDS[step])) if ((parts[k] || 0) < n) missing[k] = n - (parts[k] || 0);
    return Object.keys(missing).length ? { ok: false, why: 'parts', missing } : { ok: true };
  }
  function gerUse(parts, step) { const out = { ...parts }; for (const [k, n] of Object.entries(GER_NEEDS[step])) out[k] = (out[k] || 0) - n; return out; }
  // Grandpa Craftsman makes the wooden parts; he takes wool and dairy foods, or coins
  const TRADES = [
    { id: 'khana', gives: { khana: 5 }, goods: { wool: 2, aaruul: 2 }, coins: 30 },
    { id: 'khaalga', gives: { khaalga: 1 }, goods: { urum: 1 }, coins: 10 },
    { id: 'toono', gives: { toono: 1, bagana: 2, buslvvr: 3 }, goods: { wool: 2, aaruul: 1 }, coins: 25 },
    { id: 'uni', gives: { uni: 9 }, goods: { wool: 2 }, coins: 15 },
  ];
  function tradeCost(id, pay) { const t = TRADES.find((x) => x.id === id); return !t ? null : pay === 'coins' ? { coins: t.coins } : { ...t.goods }; }
  /** How far a lot's ger is built; gers saved before hands-on building have no stage and count as finished. */
  // ---------------------------------------------------------------- cloud save
  // A child's progress outside the wallet, in one shape for the web and Unity, kept by the server so it follows the child to
  // another device: character, lessons, badges, passport stamps, Mongol script, the historian's quest and story mode. Merging only ever
  // adds (done stays done, best scores and steps go up, the earliest date of a badge or stamp stays), so no device can erase
  // what another one earned. The character is the exception: the most recent choice (charAt) wins.
  const CLOUD_ID = /^[\w:.-]{1,48}$/, CLOUD_KEYS = 200, CLOUD_TIME = 4102444800000; // ids, keys per list, dates before 2100
  function cleanCloud(x) {
    const o = x && typeof x === 'object' && !Array.isArray(x) ? x : {};
    const int = (v, max) => (Number.isSafeInteger(v) && v > 0 ? Math.min(v, max) : 0);
    const str = (v) => (typeof v === 'string' && CLOUD_ID.test(v) ? v : '');
    const map = (m, f) => {
      const r = {};
      if (m && typeof m === 'object' && !Array.isArray(m)) for (const k of Object.keys(m).slice(0, CLOUD_KEYS)) { if (!CLOUD_ID.test(k)) continue; const v = f(m[k]); if (v) r[k] = v; }
      return r;
    };
    return {
      eth: str(o.eth), aimag: str(o.aimag), charAt: int(o.charAt, CLOUD_TIME),
      learn: map(o.learn, (l) => {
        if (!l || typeof l !== 'object') return null;
        const r = { done: l.done === true, best: int(l.best, 1000), family: int(l.family, CLOUD_TIME) };
        return r.done || r.best || r.family ? r : null;
      }),
      ach: map(o.ach, (t) => int(t, CLOUD_TIME)), stamps: map(o.stamps, (t) => int(t, CLOUD_TIME)),
      bichig: map(o.bichig, (n) => int(n, 1000)), bichigMem: map(o.bichigMem, (v) => (v ? 1 : 0)),
      hist: int(o.hist, 1000), story: map(o.story, (n) => int(n, 1000)),
    };
  }
  function mergeCloud(a, b) {
    a = cleanCloud(a); b = cleanCloud(b);
    const own = (o, k) => (Object.hasOwn(o, k) ? o[k] : undefined); // not Object.prototype's: a key may be "constructor"
    const each = (x, y, f) => Object.fromEntries([...new Set([...Object.keys(x), ...Object.keys(y)])].map((k) => [k, f(own(x, k), own(y, k))]));
    const earliest = (x, y) => (x && y ? Math.min(x, y) : x || y);
    const max = (x, y) => Math.max(x || 0, y || 0);
    const who = b.charAt > a.charAt || (!a.eth && !a.aimag) ? b : a; // the most recent character, else the one that has one
    const story = each(a.story, b.story, max); // chapter id → part reached, "<id>:done" once finished
    for (const k of Object.keys(story)) if (k.endsWith(':done')) delete story[k.slice(0, -5)]; // a finished chapter replays from the start
    return {
      eth: who.eth || a.eth, aimag: who.aimag || a.aimag, charAt: max(a.charAt, b.charAt),
      learn: each(a.learn, b.learn, (x = {}, y = {}) => ({ done: !!(x.done || y.done), best: max(x.best, y.best), family: max(x.family, y.family) })),
      ach: each(a.ach, b.ach, earliest), stamps: each(a.stamps, b.stamps, earliest),
      bichig: each(a.bichig, b.bichig, max), bichigMem: each(a.bichigMem, b.bichigMem, () => 1),
      hist: max(a.hist, b.hist), story,
    };
  }

  function lotStage(lot) { if (!lot) return 0; const s = lot.stage; return Number.isInteger(s) && s >= 1 && s <= GER_DONE ? s : GER_DONE; }
  /** Roof poles go in all around the crown to keep it level: three in a row on one side tilts it. sides: 0 east, 1 south, 2 west, 3 north. */
  function unisTilt(sides) { const n = sides.length; return n >= 3 && sides[n - 1] === sides[n - 2] && sides[n - 2] === sides[n - 3]; }

  // ---- learned by hand in Нутаг: shown to parents next to the school lessons. lesson = the lesson whose real-life family
  // activity goes with it; home = this is the one to try at home right after doing it in the game.
  const HANDS_ON = [
    { id: 'ger', icon: '⛺', lesson: 'ger', home: true, name: T('Монгол гэр гараараа барьсан', 'Built a Mongol ger by hand'),
      learned: [T('Гэр барих 9 алхам, дарааллаар нь: шал, хана, хаалга, тооно, унь, эсгий, бүрээс, бүслүүр, тулга', 'The 9 steps of building a ger, in order: floor, walls, door, toono, roof poles, felt, cover, ropes, stove'),
        T('Хаалгаа урагш харуулдаг: нар урдаас тусч, хүйтэн салхи хойноос үлээдэг', 'The door faces south: the sun comes from the south and cold winds from the north'),
        T('Хонины ноосыг цохиж, норгож, өнхрүүлж эсгий болгодог', 'Sheep’s wool becomes felt by beating, wetting and rolling it')] },
    { id: 'aaruul', icon: '🧀', lesson: 'idee', name: T('Ааруул гараараа хийсэн', 'Made aaruul by hand'),
      learned: [T('Ааруул хийх: исгэх, буцалгаж ээдэм гаргах, даавуунд шахах, хэрчих, наранд хатаах', 'Making aaruul: sour the milk, boil out the curds, press them in a cloth, cut, dry in the sun'),
        T('Хатсан ааруул бүтэн жил хадгалагддаг тул өвөл, аянд авч явдаг', 'Dried aaruul keeps for a whole year, so herders take it through winter and on journeys')] },
    { id: 'urum', icon: '🧈', lesson: 'idee', name: T('Өрөм гараараа хийсэн', 'Made öröm by hand'),
      learned: [T('Өрөм хийх: бага галд халааж, шанагаар самраад, шөнөжин хөргөх', 'Making öröm: warm the milk gently, ladle it up high, cool it overnight'),
        T('Сүүг хурдан буцалгавал түлэгддэг', 'Milk boiled too fast burns')] },
    { id: 'tarag', icon: '🥣', lesson: 'idee', home: true, name: T('Тараг гараараа исгэсэн', 'Made tarag by hand'),
      learned: [T('Тараг исгэх: буцалгах, бүлээн болтол хөргөх, хөрөнгө хийх, дулаан хонуулах', 'Making tarag: boil, cool until warm, add the starter, keep it warm overnight'),
        T('Хэт халуун сүүнд хөрөнгийн ашигтай нян үхдэг, хүйтэн сүүнд ажилладаггүй', 'In milk that is too hot the starter’s good bacteria die; in cold milk they can’t work')] },
    { id: 'airag', icon: '🍶', lesson: 'idee', name: T('Айраг гараараа бүлсэн', 'Churned airag by hand'),
      learned: [T('Гүүний сүүг хөхүүрт хийж мянга гаруй удаа бүлдэг', 'Mare’s milk is churned in the khukhuur over a thousand times'),
        T('Айлд ирсэн хүн бүр хэдэн удаа бүлж өгдөг заншилтай', 'Everyone who visits gives it a few stirs')] },
    { id: 'emee', icon: '🫖', lesson: 'customs', home: true, name: T('Эмээгийн гэрт ёсоор зочилсон', 'Visited Grandma with good manners'),
      learned: [T('Босгыг гишгэлгүй алхаж давна, орохдоо ахмадтайгаа мэндэлнэ', 'Step over the threshold, never on it, and greet the elders first'),
        T('Гэрт нар зөв явна, зочин баруун талд сууна', 'Walk sunwise inside the ger; guests sit on the west side'),
        T('Цайг баруун гараараа авч, зүүн гараараа тохойгоо түшинэ', 'Take tea with the right hand, the left hand under the right elbow')] },
  ];
  /** The hands-on ids a child has done, read from their village save (so progress from before this list counts too). */
  function handsOn(v) {
    if (!v) return [];
    const did = { ger: !!v.gerBuilt || ((v.ger || {}).stage || 0) >= GER_DONE, emee: ((v.emee || {}).visits || 0) > 0 };
    return HANDS_ON.filter((a) => (a.id in did ? did[a.id] : !!(v.seen || {})['made_' + a.id])).map((a) => a.id);
  }

  return { GER_STEPS, GER_DONE, gerTry, gerUse, tradeCost, lotStage, unisTilt, HANDS_ON, handsOn, cleanCloud, mergeCloud, VERSION, W, H, LOTS,
    MOVES, exchange, ROOF, DOOR, FURNITURE, DAILY_QUEST_REWARDS, DAILY_ACTIVITY_REWARDS, DAILY_EARN_CAP, dailyQuests, PHRASES, EMOTES, ITEMS, ROOM_RE,
    level, cleanLook };
});
