// Нутаг — the village: walk, herd and milk in the хот айл, fish, ride and race, wrestle, shoot, visit Grandma, quests.
// Ported from js/village/village.js. Positions are in the web version's tile coordinates (x right, y down);
// a character's feet are at (px + 0.5, py + 0.82). Sprites are placed by their top-left art pixel like the web canvas.
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public sealed class Act { public string type, id; public int i = -1; public Game.Sheep s; public object raider; }

public sealed partial class Game : MonoBehaviour
{
    public static Game I;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (I == null) new GameObject("Naadamy").AddComponent<Game>(); }

    const float SPEED = 4.3f, RUN_SPEED = 7.6f, ACCEL = 14f, HW = 0.26f, HH = 0.14f, JUMP_S = 0.46f; // tiles per second; feet hit-box half size
    const int T = 16;
    static readonly int[,] DIRS = { { 0, 1 }, { -1, 0 }, { 1, 0 }, { 0, -1 } }; // down, left, right, up

    public SaveData V;
    public string MyName => string.IsNullOrEmpty(V.name) ? Loc.L("Би", "Me") : V.name;
    Camera cam;
    Vector2 camPos;

    // you
    float px, py, vx, vy, stuck, jumpT = -99, walkT, saveT, ctxT, walked;
    int mx, my, dir, face = 2; bool moving, running, tiredSaid, riding;
    Vector2 petPos;
    readonly List<Vector2> path = new List<Vector2>();
    Act pending, ctx;

    // Басар trots behind you and sits beside you when you stop
    float dgx, dgy, dgIdle, dgStuck; bool dgMoving, dgFlip, dgFollow;

    public sealed class Sheep { public int i, x, y; public float px, py, next; public bool right; }
    readonly List<Sheep> sheep = new List<Sheep>();

    readonly Dictionary<PxCanvas, Sprite> spriteCache = new Dictionary<PxCanvas, Sprite>();
    readonly List<Action> later = new List<Action>();
    /** Buttons in the windows change things here, at the start of the next frame, so IMGUI's layout never changes mid-event. */
    public void Later(Action a) => later.Add(a);

    // ================================================================ start
    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this; DontDestroyOnLoad(gameObject);
        Application.targetFrameRate = 60;
        Input.multiTouchEnabled = true;
        App = AppState.Load(); Loc.En = App.en;
        Sfx.Init(gameObject); Sfx.On = App.sound;
        Music.Mute(App.muted); Music.Init(gameObject, App.music, App.musicVolume);

        cam = Camera.main;
        if (cam == null) { cam = new GameObject("Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
        cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Px.Col(Px.K.grassD);
        cam.transform.position = new Vector3(0, 0, -10);
        if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>(); // without a listener Unity plays every sound silently

        // the ground, buildings and trees are one big pixel picture
        flatMap = new GameObject("Map").AddComponent<SpriteRenderer>();
        flatMap.sprite = Px.RenderStatic().ToSprite(0, 0); flatMap.sortingOrder = -30000;

        int[,] sp = { { 24, 20 }, { 28, 22 }, { 31, 20 }, { 26, 23 } };
        for (int i = 0; i < 4; i++) sheep.Add(new Sheep { i = i, x = sp[i, 0], y = sp[i, 1], px = sp[i, 0], py = sp[i, 1], next = 1 + i });
        // the child who played last comes straight back; otherwise log in or register
        if (App.loggedIn && App.profiles.Contains(App.activeId)) Enter(SaveData.Load(App.activeId));
        else Open(new AuthPage());
    }

    Sprite SprOf(PxCanvas c)
    {
        if (!spriteCache.TryGetValue(c, out var s)) spriteCache[c] = s = c.ToSprite(c.W / 2f, c.H - 1);
        return s;
    }

    // ================================================================ rewards + quests (also used by the rest of Naadamy)
    public (int coins, int xp, int level) Reward(int coins, int xp)
    {
        int before = Rules.Level(V.xp).l;
        V.coins += coins; V.xp += xp; V.Save(); net.QueueSync(); // uploaded to the server account when online
        int after = Rules.Level(V.xp).l;
        if (after > before) { Sfx.Win(); Confetti(80); Toast(Loc.L($"Түвшин ахилаа! Түвшин {after}", $"Level up! Level {after}")); }
        return (coins, xp, after > before ? after : 0);
    }
    public List<QuestState> Quests()
    {
        string d = SaveData.DayKey();
        if (V.questDay != d || V.quests.Exists(q => !Rules.QUESTS.ContainsKey(q.k))) // new day, or quests from an older version
        {
            uint hs = 0; foreach (char ch in d + V.id) hs = unchecked(hs * 31 + ch);
            var pool = new List<string>(Rules.POOL); var pick = new List<string>();
            while (pick.Count < 3 && pool.Count > 0) { hs = unchecked(hs * 1103515245 + 12345); int at = (int)(hs % (uint)pool.Count); string k = pool[at]; pool.RemoveAt(at); if (!pick.Contains(k)) pick.Add(k); }
            V.questDay = d; V.quests = pick.ConvertAll(k => new QuestState { k = k });
            // one extra quest from today's season, worth +50%
            var sea = Seasons.Current(); var opts = sea.tasks.FindAll(k => !pick.Contains(k) && Rules.QUESTS.ContainsKey(k));
            if (opts.Count > 0) V.quests.Insert(0, new QuestState { k = opts[(int)(hs % (uint)opts.Count)], season = true });
            else { var q = V.quests.Find(x => sea.tasks.Contains(x.k)); if (q != null) q.season = true; }
            V.pets.Clear(); V.Save();
        }
        return V.quests;
    }
    public void Quest(string k, int n = 1)
    {
        NpcProgress(k, n);
        foreach (var q in Quests())
        {
            if (q.k != k || q.done) continue;
            int was = q.p, need = Rules.QUESTS[k].n; q.p = Mathf.Min(need, q.p + n);
            if (q.p == need && was < q.p) Toast(Loc.L("Даалгавар биеллээ! Самбараас шагналаа аваарай.", "Quest complete! Claim it at the board."));
        }
        V.Save();
    }
    /** Progress the townspeople's tasks (called with every daily-quest event). */
    void NpcProgress(string k, int n)
    {
        foreach (var d in Rules.NPCS)
        {
            var st = V.Npc(d.id); if (!st.on || st.step >= d.chain.Length) continue;
            var q = d.chain[st.step]; if (q.k != k || st.p >= q.n) continue;
            st.p = Mathf.Min(q.n, st.p + n);
            if (st.p >= q.n) Toast($"{d.name}: " + Loc.L("даалгавар биеллээ! Очиж шагналаа аваарай.", "task done! Go and collect your reward."));
        }
    }
    public string NpcMark(string id)
    {
        var d = Rules.NpcById(id); var st = V.Npc(id); if (st.step >= d.chain.Length) return "";
        if (!st.on) return "!"; return st.p >= d.chain[st.step].n ? "?" : "";
    }

    /** Eat or drink: costs coins (free for dairy you made, at home or when a family offers it), fills energy, teaches a fact. */
    public bool Eat(string id, bool free = false)
    {
        var f = Rules.FoodById(id); if (f == null) return false;
        bool mine = !free && Rules.DAIRY.ContainsKey(id) && V.Have(id) > 0;
        if (mine) V.Stock(id, -1);
        int cost = mine || free ? 0 : f.price;
        if (cost > V.coins) { Sfx.Bad(); Toast(Loc.L("Зоос хүрэлцэхгүй байна.", "Not enough coins.")); return false; }
        V.coins -= cost; V.energy = Mathf.Min(Rules.ENERGY_MAX, V.energy + f.energy);
        bool first = SaveData.Get(V.ate, id) == 0; SaveData.Add(V.ate, id, 1);
        if (f.kind == "drink") Sfx.Pour(0.5f); else Sfx.Pop();
        Float(px + 0.5f, py - 0.6f, "+" + f.energy, new Color(1f, 0.85f, 0.3f));
        if (first) Toast($"{f.name}: {f.fact}");
        Quest("eat"); V.Save();
        return true;
    }

    // ================================================================ the frame
    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (later.Count > 0) { var run = later.ToArray(); later.Clear(); foreach (var f in run) f(); }
        ReadWheel();
        if (V == null && modal == null) Open(new AuthPage());
        Pointer();
        if (V != null) net.Tick(this, dt);
        if (modal != null) { modal.Tick(this, dt); if (modal != null) KeysInModal(); }
        else KeysInWorld();
        if (!InScene)
        {
            Move(dt);
            MoveP2(dt);
            if (race != null) RaceTick(dt);
            if (battle != null) BattleTick(dt);
            Dog(dt);
            foreach (var q in sheep)
            {
                q.next -= dt;
                if (q.next <= 0)
                {
                    q.next = 2 + Random.value * 4; int k = Random.Range(0, 4), dx = DIRS[k, 0], dy = DIRS[k, 1], nx = q.x + dx, ny = q.y + dy;
                    if (nx >= 23 && nx <= 32 && ny >= 19 && ny <= 23) { q.x = nx; q.y = ny; if (dx != 0) q.right = dx > 0; }
                }
                float a = Mathf.Min(1, dt * 2.5f); q.px += (q.x - q.px) * a; q.py += (q.y - q.py) * a;
            }
            ctxT -= dt; if (ctxT <= 0) { ctxT = 0.2f; ctx = ContextAction(); }
            TutorialTick(dt);
        }
        saveT += dt; if (saveT > 5) { saveT = 0; SavePos(); }
        Draw();
    }
    void OnApplicationPause(bool paused) { if (paused && V != null) SavePos(); }
    void OnApplicationQuit() { if (V != null) SavePos(); net.Close(); }
    void SavePos() { if (race != null || V == null) return; V.hasPos = true; V.posX = px; V.posY = py; V.Save(); }

    void KeysInWorld()
    {
        if (dialog != null && dialog.choices == null && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.E))) { DialogNext(); return; }
        if (Input.GetKeyDown(KeyCode.Space)) Jump();
        if (Input.GetKeyDown(KeyCode.B) && race == null && battle == null) OpenBasar();
        if (race != null) return; // no shops or menus in the middle of a race
        if (battle != null && Input.GetKeyDown(KeyCode.F)) ShootNearest();
        if (Input.GetKeyDown(KeyCode.E) || p2 == null && Input.GetKeyDown(KeyCode.Return)) DoAction(ContextAction());
        if (Input.GetKeyDown(KeyCode.Escape) && riding) Dismount();
    }
    // with two players on one screen, the arrow keys are player 2's
    bool Arrows => p2 == null || InScene || modal != null;
    float KeyX() => (Input.GetKey(KeyCode.D) || Arrows && Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Arrows && Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
    float KeyY() => (Input.GetKey(KeyCode.S) || Arrows && Input.GetKey(KeyCode.DownArrow) ? 1 : 0) - (Input.GetKey(KeyCode.W) || Arrows && Input.GetKey(KeyCode.UpArrow) ? 1 : 0);
    bool ShiftHeld => Input.GetKey(KeyCode.LeftShift) || p2 == null && Input.GetKey(KeyCode.RightShift);

    public void Jump() { if (Time.time - jumpT < JUMP_S) return; jumpT = Time.time; Sfx.Pop(); net.Jumped(); }

    Vector2 lastMove = new Vector2(0, 1); // the way you last went on the grid: your doll faces it (Person)
    float JumpZ() { float q = (Time.time - jumpT) / JUMP_S; return q >= 0 && q < 1 ? Mathf.Round(Mathf.Sin(q * Mathf.PI) * 9) : 0; }

    /** over: the feet are off the ground (or already on a rock), so rocks no longer block the way. */
    static bool Solid(int x, int y, bool over) => World.Blocked(x, y) && !(over && World.At(x, y) == 'r');
    static bool FreeAt(float fx, float fy, bool over = false) =>
        !Solid(Mathf.FloorToInt(fx - HW), Mathf.FloorToInt(fy - HH), over) && !Solid(Mathf.FloorToInt(fx + HW), Mathf.FloorToInt(fy - HH), over) &&
        !Solid(Mathf.FloorToInt(fx - HW), Mathf.FloorToInt(fy + HH), over) && !Solid(Mathf.FloorToInt(fx + HW), Mathf.FloorToInt(fy + HH), over);
    static bool Los(float ax, float ay, float bx, float by)
    {
        int n = Mathf.CeilToInt(Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) / 0.2f);
        for (int i = 1; i <= n; i++) { float q = i / (float)n; if (!FreeAt(ax + (bx - ax) * q, ay + (by - ay) * q)) return false; }
        return true;
    }

    void Move(float dt)
    {
        bool block = modal != null, counting = race != null && race.phase == "count";
        float ix = block || counting ? 0 : KeyX(), iy = block || counting ? 0 : KeyY(), mag = 1;
        bool wantRun = !block && race == null && (ShiftHeld || runLock) && (ix != 0 || iy != 0 || stickOn || path.Count > 0);
        if (wantRun && V.energy <= 0 && !tiredSaid) { tiredSaid = true; Toast(Loc.L("Ядарчихлаа! Хоол идэж, ундаа уугаад хүчээ нөхөөрэй.", "Out of energy! Eat or drink to get it back."), "hungry"); }
        running = wantRun && V.energy > 0;
        float SP = riding ? (race != null ? RaceSpeed() : running ? 11 : 7.2f) : running ? RUN_SPEED : SPEED, tvx = 0, tvy = 0;
        float sm = stickOn ? stick.magnitude : 0;
        if (ix == 0 && iy == 0 && sm > 0.12f && !block && !counting) { ix = stick.x; iy = stick.y; mag = Mathf.Clamp((sm - 0.12f) / 0.5f, 0.4f, 1); } // a gentle push walks slowly, a full push walks at full speed
        if (IsoOn && (ix != 0 || iy != 0)) { var gv = Iso.FromScreen(ix, iy); ix = gv.x; iy = gv.y; } // up on the screen is up-left on the grid
        if (counting) path.Clear();
        if (ix != 0 || iy != 0) { path.Clear(); pending = null; float n = Mathf.Sqrt(ix * ix + iy * iy); tvx = ix / n * SP * mag; tvy = iy / n * SP * mag; }
        else if (path.Count > 0)
        {
            var wp = path[0]; float dx = wp.x - px, dy = wp.y - py, d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d < Mathf.Max(0.06f, SP * dt * 1.1f))
            {
                px = wp.x; py = wp.y; path.RemoveAt(0);
                if (path.Count == 0)
                {
                    vx = vy = 0; var a0 = pending; pending = null;
                    mx = Mathf.FloorToInt(px + 0.5f); my = Mathf.FloorToInt(py + 0.82f);
                    if (a0 != null) DoAction(a0);
                    ctx = ContextAction();
                }
            }
            else
            {
                float sp = path.Count == 1 && d < 0.6f ? SP * Mathf.Max(0.45f, d / 0.6f) : SP;
                tvx = dx / d * sp; tvy = dy / d * sp;
            }
        }
        float acc = Mathf.Min(1, dt * (ix != 0 || iy != 0 || path.Count > 0 ? ACCEL : ACCEL * 1.4f));
        vx += (tvx - vx) * acc; vy += (tvy - vy) * acc;
        if (ix != 0 || iy != 0) lastMove = new Vector2(ix, iy).normalized; else if (vx * vx + vy * vy > 0.5f) lastMove = new Vector2(vx, vy).normalized;
        if (Mathf.Abs(vx) < 0.01f) vx = 0; if (Mathf.Abs(vy) < 0.01f) vy = 0;
        // move with collision, sliding along walls; in the air you clear rocks, and walking into one hops over it
        float ox = px, oy = py;
        bool over = Time.time - jumpT < JUMP_S || !FreeAt(px + 0.5f, py + 0.82f);
        bool RockAhead(float nx, float ny) => !over && (ix != 0 || iy != 0) && !FreeAt(nx, ny) && FreeAt(nx, ny, true);
        if (RockAhead(px + 0.5f + vx * dt, py + 0.82f) || RockAhead(px + 0.5f, py + 0.82f + vy * dt)) { Jump(); over = true; }
        if (vx != 0 && FreeAt(px + 0.5f + vx * dt, py + 0.82f, over)) px += vx * dt; else vx = 0;
        if (vy != 0 && FreeAt(px + 0.5f, py + 0.82f + vy * dt, over)) py += vy * dt; else vy = 0;
        float speed = Mathf.Sqrt(vx * vx + vy * vy);
        moving = speed > 0.3f;
        var sv = IsoOn ? Iso.ToScreen(vx, vy) * 0.70710678f : new Vector2(vx, vy); // facing follows the way you go on the screen
        if (moving) { walkT += dt; dir = Mathf.Abs(sv.x) > Mathf.Abs(sv.y) * 0.9f ? (sv.x < 0 ? 1 : 2) : (sv.y < 0 ? 3 : 0); }
        if (Mathf.Abs(sv.x) > 0.3f) face = sv.x < 0 ? 1 : 2;
        if (riding && moving) V.horse.dist += speed * dt;
        if (moving) walked += speed * dt;
        if (running && moving) { V.energy = Mathf.Max(0, V.energy - dt * (riding ? 0.6f : 1.1f)); if (V.energy > 0) tiredSaid = false; }
        if (path.Count > 0) { stuck = Mathf.Abs(px - ox) + Mathf.Abs(py - oy) < 0.002f ? stuck + dt : 0; if (stuck > 0.5f) { path.Clear(); pending = null; } }
        int tx = Mathf.FloorToInt(px + 0.5f), ty = Mathf.FloorToInt(py + 0.82f);
        if (tx != mx || ty != my) { mx = tx; my = ty; ctx = ContextAction(); }
        // smooth camera, and your pet trotting after you
        var focus = p2 != null && !p2.hidden ? new Vector2((px + p2.px) / 2, (py + p2.py) / 2) : new Vector2(px, py); // between both players when two play on one screen
        if (IsoOn && p2 == null) focus += new Vector2(vx, vy) * 0.28f; // the isometric camera looks a little ahead of where you're going
        float ca = Mathf.Min(1, dt * (IsoOn ? 4 : 7)); camPos += (focus - camPos) * ca;
        var pt = new Vector2(px - 0.45f, py + 0.25f); if ((pt - petPos).magnitude > 4) petPos = pt; else petPos += (pt - petPos) * Mathf.Min(1, dt * 4);
    }

    /** Basar walks on the ground like you (water, trees and walls stop him) and only follows when you get away from him:
     *  once he is near you he stops and sits down wherever he is. Stuck or left far behind, he catches up at your heels. */
    void Dog(float dt)
    {
        int ddx = DIRS[dir, 0], ddy = DIRS[dir, 1];
        float tx = moving ? px - ddx * 0.95f + (ddy != 0 ? 0.6f : 0) : px, ty = moving ? py - ddy * 0.7f + 0.15f : py; // a step behind you, or you when you stand
        float ox = dgx, oy = dgy, dist = new Vector2(px - dgx, py - dgy).magnitude;
        dgFollow = dist > (dgFollow ? (moving ? 0.6f : 1.3f) : 2.4f); // starts after you a little way off, keeps up while you walk, stops close by when you stop (so he doesn't fidget)
        if (dgFollow)
        {
            var to = new Vector2(tx - dgx, ty - dgy);
            if (to.sqrMagnitude > 0.0025f)
            {
                var step = to.normalized * Mathf.Clamp(dist * 2.2f, 2f, RUN_SPEED * 1.1f) * dt; if (step.sqrMagnitude > to.sqrMagnitude) step = to;
                bool over = !FreeAt(dgx + 0.5f, dgy + 0.82f); // already stuck in something: let him walk out of it
                if (FreeAt(dgx + 0.5f + step.x, dgy + 0.82f, over)) dgx += step.x; // each axis on its own, so he slides along water and walls
                if (FreeAt(dgx + 0.5f, dgy + 0.82f + step.y, over)) dgy += step.y;
            }
        }
        dgStuck = dgFollow && Mathf.Abs(dgx - ox) + Mathf.Abs(dgy - oy) < 0.002f ? dgStuck + dt : 0;
        if (dist > 7 || dgStuck > 0.8f) // round the lake or over the fence: he comes to your heels
        {
            dgStuck = 0;
            if (FreeAt(tx + 0.5f, ty + 0.82f) && Los(px + 0.5f, py + 0.82f, tx + 0.5f, ty + 0.82f)) { dgx = tx; dgy = ty; } else { dgx = px; dgy = py; }
            ox = dgx; oy = dgy;
        }
        float sp = Mathf.Sqrt((dgx - ox) * (dgx - ox) + (dgy - oy) * (dgy - oy)) / Mathf.Max(dt, 1e-3f);
        dgMoving = sp > 0.4f; dgIdle = dgMoving ? 0 : dgIdle + dt;
        if (IsoOn) { float s0 = ox - oy, s1 = dgx - dgy; if (Mathf.Abs(s1 - s0) > 0.002f) dgFlip = s1 < s0; else if (!dgMoving) dgFlip = px - py + 0.2f < s1; }
        else if (Mathf.Abs(dgx - ox) > 0.002f) dgFlip = dgx < ox; else if (!dgMoving) dgFlip = px + 0.2f < dgx;
        DogFacts(dt);
    }

    // ================================================================ drawing: a pool of sprites placed like the web canvas
    readonly List<SpriteRenderer> pool = new List<SpriteRenderer>(); int poolUsed;
    static PxCanvas shadowArt;
    /** Draws art with its top-left at (X, Y) art pixels, sorted with the others by sortY (tiles).
     *  In the isometric view it stands up facing the camera with its feet on the ground, sorted by x + y;
     *  flat art (shadows, the felt) lies on the ground under everything standing. */
    void Spr(PxCanvas art, float X, float Y, bool flip, float sortY, int bias = 0, float alpha = 1, bool flat = false)
    {
        if (poolUsed == pool.Count) pool.Add(new GameObject("Sprite").AddComponent<SpriteRenderer>());
        var r = pool[poolUsed++]; r.enabled = true;
        r.sprite = SprOf(art); r.flipX = flip; r.color = new Color(1, 1, 1, alpha);
        float fx = (X + art.W / 2f) / T;
        r.transform.position = new Vector3(fx, -(Y + art.H - 1) / T, 0);
        r.transform.rotation = IsoOn && !flat ? Iso.Standing : Quaternion.identity;
        r.sortingOrder = !IsoOn ? Mathf.RoundToInt(sortY * 100) + bias : flat ? -20000 + Mathf.RoundToInt((fx + sortY) * 10) + bias : Mathf.RoundToInt((fx + sortY) * 100) + bias;
    }

    // ================================================================ people: a sprite, or in the isometric view a doll with a body (Doll.cs)
    readonly List<Doll> dolls = new List<Doll>(); int dollsUsed;
    /** Someone standing at tile (x, y) (their sprite's top-left at x·T, y·T - 12), z art pixels up, facing dir (0 front,
     *  1 left, 2 right, 3 back) on the screen, or grid direction heading when given; walking swings the legs. */
    void Person(Look look, int dir, int frame, float x, float y, float sortY, float z = 0, Vector2? heading = null, bool walking = false, bool run = false)
    {
        if (!IsoOn) { Spr(Px.Char(look, dir, frame), x * T, y * T - 12 - z, false, sortY); return; }
        if (dollsUsed == dolls.Count) dolls.Add(new Doll());
        var d = dolls[dollsUsed++]; if (!d.root.activeSelf) d.root.SetActive(true);
        var h = heading ?? (dir == 0 ? new Vector2(1, 1) : dir == 3 ? new Vector2(-1, -1) : dir == 1 ? new Vector2(-1, 1) : new Vector2(1, -1));
        float? swing = walking || frame != 0 ? Time.time * (run ? 15 : 10) + x * 1.7f : (float?)null;
        d.Pose(look, x + 0.5f, y + 0.82f, z / T, h.x, h.y, swing); // feet where they collide (Move: px + 0.5, py + 0.82)
    }
    /** A doll with its feet at tile (fx, fy), z tiles up (a rider on a horse's back), facing grid direction h. */
    void DollAt(Look look, float fx, float fy, float z, Vector2 h, float? swing)
    {
        if (dollsUsed == dolls.Count) dolls.Add(new Doll());
        var d = dolls[dollsUsed++]; if (!d.root.activeSelf) d.root.SetActive(true);
        d.Pose(look, fx, fy, z, h.x, h.y, swing);
    }

    // ================================================================ animals: a picture, or in the isometric view a body (Critter.cs)
    readonly List<Critter> critters = new List<Critter>(); int crittersUsed;
    readonly Dictionary<string, (Vector2 at, Vector2 h, float moved)> tracks = new Dictionary<string, (Vector2, Vector2, float)>();
    /** Which way something is going, from how it moved since the last frame (smoothed); fallback until it first moves. */
    Vector2 Track(string id, float x, float y, Vector2 fallback, out bool walking)
    {
        var p = new Vector2(x, y);
        if (!tracks.TryGetValue(id, out var s)) s = (p, fallback, -9);
        var d = p - s.at;
        if (d.sqrMagnitude > 1e-6f) { s.h = Vector2.Lerp(s.h, d.normalized, 0.3f); s.moved = Time.time; }
        s.at = p; tracks[id] = s; walking = Time.time - s.moved < 0.12f;
        return s.h.sqrMagnitude > 1e-4f ? s.h.normalized : fallback;
    }
    /** Facing left or right on the screen, as a grid direction. */
    static Vector2 Facing(bool left) => left ? new Vector2(-1, 1) : new Vector2(1, -1);
    Critter NextCritter() { if (crittersUsed == critters.Count) critters.Add(new Critter()); var c = critters[crittersUsed++]; if (!c.root.activeSelf) c.root.SetActive(true); return c; }
    /** An animal drawn like Spr (art's top-left at X, Y, facing right unless flip), or in the isometric view its body standing at the
     *  picture's feet, turned the way it's going when it has an id to follow. */
    void Animal(string kind, string coat, PxCanvas art, float X, float Y, bool flip, float sortY, string id = null, bool walking = false, bool sit = false)
    {
        if (!IsoOn || !Critter.Has(kind)) { Spr(art, X, Y, flip, sortY); return; }
        float fx = (X + art.W / 2f) / T, fy = (Y + art.H - 1) / T; bool moved = false;
        var h = id != null ? Track(id, fx, fy, Facing(flip), out moved) : Facing(flip);
        NextCritter().Pose(kind, coat, fx, fy, 0, h.x, h.y, walking || moved ? Time.time * 9 + fx : (float?)null, sit);
    }

    readonly List<GameObject> pictures = new List<GameObject>(); int picturesUsed;
    /** An isometric picture (pivot: its feet) standing facing the camera with its feet at tile (fx, fy), each pixel at its own depth (Billboard). */
    void Picture(PxCanvas art, float pivotX, float pivotY, float[] depth, float fx, float fy)
    {
        if (picturesUsed == pictures.Count) { var ng = new GameObject("Picture"); ng.AddComponent<MeshFilter>(); ng.AddComponent<MeshRenderer>(); pictures.Add(ng); }
        var go = pictures[picturesUsed++]; if (!go.activeSelf) go.SetActive(true);
        go.GetComponent<MeshFilter>().sharedMesh = Billboard.Quad(art, pivotX, pivotY, Critter.PPT_BASAR);
        go.GetComponent<MeshRenderer>().sharedMaterial = Billboard.Mat(art, depth);
        go.transform.SetPositionAndRotation(new Vector3(fx, -fy, 0), Iso.Standing);
    }

    // ================================================================ the isometric view (Iso.cs)
    SpriteRenderer flatMap; GameObject isoRoot;
    bool IsoOn => !App.flatView;
    /** Shows the flat map or the isometric ground with its standing trees and buildings, building the latter the first time. */
    void ShowMap()
    {
        if (IsoOn && isoRoot == null)
        {
            isoRoot = new GameObject("Isometric village");
            var props = Px.RenderProps(); var ground = Px.RenderGround(true);
            // each standing thing's shadow on the ground: an oval under its feet, pushed away from the light at the upper left
            foreach (var (art, fx, fy) in props) { int rx = Mathf.Clamp(Mathf.RoundToInt(art.W * 0.42f), 4, 40); Px.Ell(ground, Mathf.RoundToInt(fx * T) + rx / 4, Mathf.RoundToInt(fy * T) - rx / 5, rx, Mathf.Max(2, rx * 2 / 5), "rgba(20,30,10,.3)"); }
            var g = new GameObject("Ground").AddComponent<SpriteRenderer>(); g.transform.SetParent(isoRoot.transform, false);
            g.sprite = ground.ToSprite(0, 0); g.sortingOrder = -30000;
            Bodies.Build().transform.SetParent(isoRoot.transform, false);
            foreach (var (art, ppx, ppy, depth, at) in Bodies.Pictures()) Billboard.Make("Building", art, ppx, ppy, depth, at, isoRoot.transform); // the buildings drawn as isometric pixel art
            foreach (var (art, fx, fy) in props)
            {
                var s = new GameObject("Prop").AddComponent<SpriteRenderer>(); s.transform.SetParent(isoRoot.transform, false);
                s.sprite = art.ToSprite(art.W / 2f, art.H); s.transform.SetPositionAndRotation(new Vector3(fx, -fy, 0), Iso.Standing);
                s.sortingOrder = Mathf.RoundToInt((fx + fy - 1) * 100);
            }
        }
        if (flatMap.gameObject.activeSelf == IsoOn) flatMap.gameObject.SetActive(!IsoOn);
        if (isoRoot != null && isoRoot.activeSelf != IsoOn) isoRoot.SetActive(IsoOn);
    }
    void Shadow(float X, float Y, float sortY)
    {
        if (shadowArt == null) { shadowArt = new PxCanvas(11, 3); var c = Px.Col("rgba(30,20,10,.25)"); for (int x = 1; x < 10; x++) { shadowArt.Put(x, 0, c); shadowArt.Put(x, 2, c); } for (int x = 0; x < 11; x++) shadowArt.Put(x, 1, c); }
        Spr(shadowArt, X + 3, Y + 13, false, sortY, -2, flat: true);
    }
    void Rider(Look look, int fc, bool mov, float wx, float wy, float sortY, float z = 0, bool fast = false, Vector2? heading = null, string id = null)
    {
        if (IsoOn) // a horse with a body, and the rider's doll sitting in its saddle
        {
            float fx = wx + 0.5f, fy = wy + 0.94f; bool moved = false;
            var h = heading ?? (id != null ? Track(id, fx, fy, Facing(fc == 1), out moved) : Facing(fc == 1));
            float? sw = mov || moved ? Time.time * (fast ? 17 : 12) : (float?)null;
            Spr(HorseShadow(), wx * T - 2, wy * T + 15, false, sortY, -2, flat: true);
            NextCritter().Pose("rider", string.IsNullOrEmpty(look.coat) ? "bay" : look.coat, fx, fy, z / T, h.x, h.y, sw);
            var lk = look.Copy(); lk.pet = null; DollAt(lk, fx, fy, z / T + 0.84f, h, null);
            return;
        }
        int hf = z != 0 ? 2 : mov ? 1 + (int)(Time.time * (fast ? 17 : 12)) % 4 : 0; var hs = Px.Horse(string.IsNullOrEmpty(look.coat) ? "bay" : look.coat, hf, true); bool left = fc == 1;
        float HX = wx * T - 6, HY = wy * T + 16 - hs.H - z;
        Spr(HorseShadow(), HX + 4, HY + 21 + z, false, sortY, -2, flat: true);
        var rl = look.Copy(); rl.pet = null;
        Spr(Px.Char(rl, left ? 1 : 2, 0), HX + 6, HY - 15, false, sortY, 0);
        Spr(hs, HX, HY, left, sortY, 1);
    }
    static PxCanvas horseShadow;
    static PxCanvas HorseShadow() { if (horseShadow == null) { horseShadow = new PxCanvas(20, 2); var c = Px.Col("rgba(30,20,10,.25)"); for (int y = 0; y < 2; y++) for (int x = 0; x < 20; x++) horseShadow.Put(x, y, c); } return horseShadow; }
    int Frame(bool mov, bool run) => mov ? 1 + (int)(Time.time * (run ? 13 : 8)) % 2 : 0;

    void Draw()
    {
        poolUsed = 0; dollsUsed = 0; crittersUsed = 0; picturesUsed = 0;
        float t = Time.time;
        if (!InScene)
        {
            foreach (var q in sheep) { Shadow(q.px * T, q.py * T, q.py); var shp = Px.Beast("sheep", (int)(t * 2 + q.i) % 2); Animal(Shearable(q.i) ? "sheep" : "shorn", null, Shearable(q.i) ? shp : Px.Shorn(shp), q.px * T, q.py * T + 2, !q.right, q.py, "sheep" + q.i); }
            // you (on foot or riding), and your pet
            var look = V.look; float z = JumpZ();
            if (!string.IsNullOrEmpty(look.pet)) { var a = Px.Animal(look.pet, Frame(moving, running)); Shadow(petPos.x * T - 2, petPos.y * T - 1, petPos.y); Animal(look.pet, null, a, petPos.x * T + 2, petPos.y * T + 16 - a.H, px < petPos.x - 0.05f, petPos.y, "pet"); }
            if (riding) { var rl = look.Copy(); rl.coat = V.horse.coat; Rider(rl, face, moving, px, py, py, z, running || (race != null && race.sprint), lastMove); }
            else
            {
                Shadow(px * T, py * T, py); var me = Px.Char(look, dir, z != 0 ? 0 : Frame(moving, running));
                if (IsoOn) Person(look, dir, 0, px, py, py, z, lastMove, moving, running); else Spr(me, px * T, py * T - 12 - z, false, py);
            }
            int df = dgMoving ? 1 + (int)(t * 8) % 2 : dgIdle > 0.6f ? 3 : 0;
            Shadow(dgx * T, dgy * T + 1, dgy + 0.05f); if (IsoOn) // Basar: an isometric pixel picture, turned eight ways, walking or sitting (Critter.Picture)
            {
                float bfx = dgx + 0.5f, bfy = dgy + 1f; var bh = Track("basar", bfx, bfy, Facing(dgFlip), out _);
                if (!dgMoving) { var tp = new Vector2(px - dgx, py - dgy); if (tp.sqrMagnitude > 0.01f) bh = tp.normalized; } // standing, he looks at you
                int d8 = ((Mathf.RoundToInt(Mathf.Atan2(bh.y, bh.x) / (Mathf.PI / 4)) % 8) + 8) % 8;
                var (bart, bpx, bpy, bdepth) = Critter.Picture("basar", d8, dgMoving ? (int)(t * 10) % 4 : -1, df == 3);
                Picture(bart, bpx, bpy, bdepth, bfx, bfy);
            }
            else Spr(Px.Bankhar(df), dgx * T - 3, dgy * T - 1, dgFlip, dgy + 0.05f);
            // your own ger on its lot
            if (V.lot.i >= 0 && MyStage > 0 && !IsoOn) { var o =World.objects.Find(q => q.kind == "lot" && q.i == V.lot.i); Spr(Px.GerStage(MyStage, Px.ROOF[V.lot.roof ?? "blue"], Px.DOOR[V.lot.door ?? "red"]), o.x * T, o.y * T - 2, false, o.y + 2); }
            // the dairy yard's animals: cows and goats shift about, mares stand by their foals on the зэл
            foreach (var o in World.objects)
            {
                if (o.kind == "cow" || o.kind == "goat")
                {
                    bool turn = (int)(t / 4 + o.x) % 3 == 0; int f = (int)(t * 2 + o.y) % 5 == 0 ? 1 : 0; var img = Px.Beast(o.kind, f); bool cow = o.kind == "cow";
                    Shadow(o.x * T + (cow ? 6 : 0), o.y * T + 1, o.y + 0.2f); Animal(o.kind, null, img, o.x * T + (cow ? 4 : -1), o.y * T + 14 - img.H, turn, o.y + 0.2f);
                }
                else if (o.kind == "felt" && !IsoOn) Spr(Px.FeltMat(), o.x * T, o.y * T, false, o.y + 0.4f, flat: true); // (isometric: a picture, Bodies.Felt)
                else if (o.kind == "zel")
                {
                    var zl = new[] { (0.6f, "foal"), (2.2f, "foal"), (1.2f, "mare"), (3.1f, "mare") };
                    for (int i = 0; i < 4; i++) { var (dx, k) = zl[i]; var img = Px.Beast(k, k == "foal" && (int)(t * 1.5f + i) % 7 == 0 ? 1 : 0); float my2 = k == "mare" ? 0.75f : 0; Animal(k, null, img, (o.x + dx) * T, (o.y + my2) * T + 12 - img.H, i % 2 == 1, o.y + my2 + 0.1f); }
                }
                else if (o.kind == "npc" || o.kind == "npcq" || o.kind == "emee" || o.kind == "general" || o.kind == "historian")
                {
                    Look lk = o.kind == "npc" ? Rules.ZASUUL : o.kind == "emee" ? Rules.EMEE : o.kind == "general" ? Rules.GENERAL : o.kind == "historian" ? Rules.HISTORIAN : Rules.NpcById(o.id).look;
                    int d = o.kind == "npc" ? ((int)(t / 2.5f) % 4 == 3 ? 2 : 0) : o.kind == "emee" ? ((int)(t / 3) % 4 == 2 ? 1 : 0) : ((int)(t / 3 + o.x) % 5 == 4 ? 2 : 0);
                    Shadow(o.x * T, o.y * T, o.y); Person(lk, d, 0, o.x, o.y, o.y);
                }
            }
            if (IsoOn) foreach (var o in World.objects) // the уяа's two horses (the flat map has them painted in)
            {
                if (o.kind != "uya") continue;
                int X = o.x * T, Y = o.y * T, w = o.w * T;
                Animal("uya", "bay", Px.Horse("bay", 0, true), X + 3, Y - 10, false, o.y);
                Animal("uya", "white", Px.Horse("white", 0, true), X + w - 31, Y - 9, false, o.y);
            }
            if (race != null) RaceDraw(t);
            BattleDraw();
            NetDraw();
            P2Draw(t);
        }
        GerBodyDraw(); // my ger on its plot in the isometric view (GerBuild.cs)
        for (int i = poolUsed; i < pool.Count; i++) pool[i].enabled = false;
        for (int i = dollsUsed; i < dolls.Count; i++) if (dolls[i].root.activeSelf) dolls[i].root.SetActive(false);
        for (int i = crittersUsed; i < critters.Count; i++) if (critters[i].root.activeSelf) critters[i].root.SetActive(false);
        for (int i = picturesUsed; i < pictures.Count; i++) if (pictures[i].activeSelf) pictures[i].SetActive(false);
        // camera: follow, stay inside the map, snap to whole screen pixels so the pixel art doesn't shimmer
        // a whole number of screen pixels per art pixel, about 24 tiles across (11 on a phone), never showing past the map (view.js measure)
        float cssW = Screen.width / Mathf.Max(1, Dpr);
        int zoom = Mathf.Max(1, Mathf.RoundToInt(Screen.width / ((cssW < 640 ? (IsoOn ? 10f : 11f) : IsoOn ? 20f : 24f) * T))); // the isometric camera comes in closer, like Hades
        zoom = Mathf.Max(zoom, Mathf.CeilToInt(Screen.width / (float)(World.W * T)), Mathf.CeilToInt(Screen.height / (float)(World.H * T)));
        cam.orthographicSize = Screen.height / (2f * zoom * T);
        float hh = cam.orthographicSize, hw = hh * cam.aspect;
        float ppu = Screen.height / (2 * hh);
        ShowMap();
        if (IsoOn) { Iso.Place(cam, Mathf.Clamp(camPos.x + 0.5f, 0, World.W), Mathf.Clamp(camPos.y + 0.9f, 0, World.H), ppu); return; }
        float cx = hw * 2 > World.W ? World.W / 2f : Mathf.Clamp(camPos.x + 0.5f, hw, World.W - hw);
        float cy = hh * 2 > World.H ? -World.H / 2f : Mathf.Clamp(-(camPos.y + 0.5f), -World.H + hh, -hh);
        cam.transform.rotation = Quaternion.identity;
        cam.transform.position = new Vector3(Mathf.Round(cx * ppu) / ppu, Mathf.Round(cy * ppu) / ppu, -10);
    }

    // ================================================================ walking to places
    public void GoTo(int tx, int ty, Act action)
    {
        var p = World.FindPath(mx, my, tx, ty);
        if (p == null) { Toast(Loc.L("Тийшээ очих зам алга.", "Can't get there.")); return; }
        if (p.Count == 0) { path.Clear(); if (action != null) DoAction(action); return; } // already standing there
        // string-pull the tile path into a few straight, smooth segments
        path.Clear(); float cxp = px, cyp = py; int i = 0;
        while (i < p.Count)
        {
            int j = p.Count - 1;
            while (j > i && !Los(cxp + 0.5f, cyp + 0.82f, p[j].x + 0.5f, p[j].y + 0.82f)) j--;
            path.Add(new Vector2(p[j].x, p[j].y)); cxp = p[j].x; cyp = p[j].y; i = j + 1;
        }
        stuck = 0; pending = action;
        rings.Add((new Vector2(tx + 0.5f, ty + 0.5f), Time.time));
        ctx = null;
    }

    // ---------------------------------------------------------------- taps in the isometric view
    // people, animals and buildings stand up off the ground there, so a tap is tested against where they show on the screen
    // (the ground point under the tap is behind them), the nearest first
    float ScreenPpu => Screen.height / (2 * cam.orthographicSize);
    /** Whether a screen point falls on something standing at tile (fx, fy), halfW tiles wide and height tiles tall. */
    bool OnStanding(Vector2 screen, float fx, float fy, float halfW, float height)
    {
        var s = cam.WorldToScreenPoint(new Vector3(fx, -fy, 0)); float u = ScreenPpu;
        return Mathf.Abs(screen.x - s.x) < halfW * u && screen.y > s.y - 0.25f * u && screen.y < s.y + height * u;
    }
    static float TallOf(string kind)
    {
        switch (kind)
        {
            case "academy": return 3.1f; case "ger": case "bleachers": return 2.6f; case "shop": case "statue": return 2.4f; case "ovoo": return 2.3f;
            case "lantern": return 2.1f; case "well": return 1.9f; case "board": return 1.85f; case "npc": case "npcq": case "emee": case "general": case "historian": return 1.8f;
            case "churn": case "felt": return 1.5f; case "uya": return 1.3f; case "archery": return 1.2f; case "rack": return 1.1f; default: return 1f;
        }
    }
    /** Whether a screen point falls inside the box an object stands in (its tiles, up to its height). */
    bool OnObject(Vector2 screen, WObj o)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, h = TallOf(o.kind);
        foreach (var gx in new float[] { o.x, o.x + o.w }) foreach (var gy in new float[] { o.y, o.y + o.h }) foreach (var z in new[] { 0, h })
        { var s = cam.WorldToScreenPoint(new Vector3(gx, -gy, -z)); x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y); }
        return screen.x >= x0 && screen.x <= x1 && screen.y >= y0 && screen.y <= y1;
    }
    /** Basar, a sheep or something with a door under the tap: does what tapping it does and says so. */
    bool TapStanding(Vector2 screen)
    {
        if (OnStanding(screen, dgx + 0.5f, dgy + 1f, 0.5f, 1.2f)) { DoAction(new Act { type = "basar" }); return true; }
        Sheep sh = null; float best = float.MinValue;
        foreach (var q in sheep) if (q.px + q.py > best && OnStanding(screen, q.px + 0.5f, q.py + 1f, 0.5f, 0.85f)) { sh = q; best = q.px + q.py; }
        if (sh != null) { var n = World.NearestOpen(sh.x, sh.y, mx, my); if (n != null) GoTo(n.Value.x, n.Value.y, new Act { type = ShearSheep(sh) ? "shear" : "sheep", s = sh }); return true; }
        WObj hit = null; best = float.MinValue;
        foreach (var o in World.objects) { if (!o.hasDoor) continue; float k = o.x + o.w + o.y + o.h; if (k > best && OnObject(screen, o)) { hit = o; best = k; } } // the one nearest the camera
        if (hit != null) { GoTo(hit.doorX, hit.doorY, ActFor(hit)); return true; }
        return false;
    }

    /** A tap or click on the map: Basar, a sheep, a building's door, or just a place to walk to. */
    void TapWorld(Vector2 screen)
    {
        if (race != null) return;
        float wx, wy;
        if (IsoOn) { var gp = Iso.Ground(cam, screen); if (gp == null) return; wx = gp.Value.x; wy = gp.Value.y; }
        else { Vector3 w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10)); wx = w.x; wy = -w.y; }
        int tx0 = Mathf.FloorToInt(wx), ty0 = Mathf.FloorToInt(wy);
        if (IsoOn && battle == null && TapStanding(screen)) return;
        if (battle != null) { var e = NearestEnemy(wx, wy + 0.4f, 1.4f); if (e != null) { ShootAt(e.x, e.y); return; } } // tap a raider to loose a whistling arrow
        // only a tap right on Basar opens his chat; he walks beside you, so a wide area kept stealing taps meant for walking (the 🐕 button opens him too)
        if (Mathf.Abs(dgx + 0.55f - wx) < 0.4f && wy > dgy - 0.1f && wy < dgy + 0.8f) { DoAction(new Act { type = "basar" }); return; }
        var sh = sheep.Find(q => Mathf.Abs(q.px + 0.5f - wx) < 0.7f && Mathf.Abs(q.py + 0.5f - wy) < 0.7f);
        if (sh != null) { var n = World.NearestOpen(sh.x, sh.y, mx, my); if (n != null) GoTo(n.Value.x, n.Value.y, new Act { type = ShearSheep(sh) ? "shear" : "sheep", s = sh }); return; }
        var o = World.ObjAt(tx0, ty0);
        if (o != null && o.hasDoor) { GoTo(o.doorX, o.doorY, ActFor(o)); return; }
        if (tx0 == World.DOCK_X && ty0 == World.DOCK_Y) { GoTo(World.DOCK_X, World.DOCK_Y, new Act { type = "fish" }); return; }
        if (!World.Blocked(tx0, ty0)) { GoTo(tx0, ty0, null); return; }
        var no = World.NearestOpen(tx0, ty0, mx, my); if (no != null) GoTo(no.Value.x, no.Value.y, null);
    }

    static Act ActFor(WObj o) => o.kind == "lot" ? new Act { type = "lot", i = o.i } : o.kind == "npc" ? new Act { type = "zasuul" } : o.kind == "npcq" ? new Act { type = "npcq", id = o.id } : new Act { type = o.id };

    /** What you can do right where you stand (shown on the big action button, E on a keyboard). */
    Act ContextAction()
    {
        if (path.Count > 0 || race != null) return null;
        float fx = px + 0.5f, fy = py + 0.82f; Act best = null; float bd = 1.05f;
        if (battle != null) { var e = NearestEnemy(fx, fy, 1.7f); if (e != null) return new Act { type = "lasso", raider = e }; }
        void Consider(float d, Act a) { if (d < bd) { bd = d; best = a; } }
        foreach (var o in World.objects) if (o.hasDoor) Consider(Dist(fx, fy, o.doorX + 0.5f, o.doorY + 0.7f), ActFor(o));
        Consider(Dist(fx, fy, World.DOCK_X + 0.5f, World.DOCK_Y + 0.7f), new Act { type = "fish" });
        foreach (var q in sheep) Consider(Dist(fx, fy, q.px + 0.5f, q.py + 0.7f) - 0.35f, new Act { type = ShearSheep(q) ? "shear" : "sheep", s = q });
        if (best != null) return best;
        if (World.At(mx, my) == 'a') return new Act { type = "arena" };
        return null;
    }
    static float Dist(float ax, float ay, float bx, float by) => Mathf.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    public string LabelFor(Act a)
    {
        switch (a.type)
        {
            case "home": return Loc.L("Хувцас", "Wardrobe");
            case "arcade": return Loc.L("Сурах", "Learn");
            case "academy": return Loc.L("Монгол бичиг", "Mongol script");
            case "general": return Loc.L("Жанжин", "General");
            case "setsen": return Loc.L("Сэцэн", "Setsen");
            case "historian": return Loc.L("Түүхч", "Historian");
            case "shop": return Loc.L("Дэлгүүр", "Shop");
            case "board": return Loc.L("Даалгавар", "Quests");
            case "well": return Loc.L("Ус уух", "Drink water");
            case "zel": return Loc.L("Гүү саах", "Milk a mare");
            case "cow1": case "cow2": return Loc.L("Үнээ саах", "Milk the cow");
            case "goat1": case "goat2": return Loc.L("Ямаа саах", "Milk the goat");
            case "churn": return Loc.L("Айраг бүлэх", "Churn airag");
            case "tulga": return Loc.L("Өрөм, тараг", "Öröm & tarag");
            case "rack": return Loc.L("Ааруул хийх", "Make aaruul");
            case "fish": return Loc.L("Загасчлах", "Fish");
            case "sheep": return Loc.L("Илбэх", "Pet");
            case "lot": return LotOf(a.i) != null ? (LotOf(a.i).stage > 0 && LotOf(a.i).stage < GerRules.DONE ? Loc.L("Барьж байна", "Being built") : Loc.L("Зочлох", "Visit")) : V.lot.i == a.i && MyStage >= GerRules.DONE ? Loc.L("Миний гэр", "My ger") : Loc.L("Гэр барих", "Build a ger");
            case "shear": return Loc.L("Хяргах", "Shear");
            case "felt": return FeltLabel();
            case "zasuul": case "arena": return Loc.L("Барилдах", "Wrestle");
            case "ovoo": return Loc.L("Овоо тахих", "Visit ovoo");
            case "uya": return Loc.L("Морь", "Horses");
            case "archery": return Loc.L("Сур харваа", "Archery");
            case "emee": return Loc.L("Эмээ", "Grandma");
            case "npcq": return Loc.L("Ярилцах", "Talk");
            case "basar": return Loc.L("Басар", "Basar");
            case "lasso": return Loc.L("Уургадах", "Lasso");
        }
        return "";
    }

    void DoAction(Act a)
    {
        if (a == null || (modal != null && !(modal is FirstPerson)) || race != null) return;
        if (battle != null && a.type != "lasso" && a.type != "sheep") { Toast(Loc.L("Эхлээд нутгаа хамгаал!", "Defend the land first!")); return; }
        switch (a.type)
        {
            case "home": Open(new WardrobeModal()); return;
            case "zel": case "cow1": case "cow2": case "goat1": case "goat2": OpenMilk(a.type); return;
            case "churn": case "tulga": case "rack": Open(new StationModal(a.type)); return;
            case "board": Open(new QuestModal()); return;
            case "shop": Open(new ShopModal("wear")); return;
            case "well": Eat("us", true); return;
            case "sheep": PetSheep(a.s); return;
            case "shear": if (Mathf.Abs(a.s.x - mx) > 1 || Mathf.Abs(a.s.y - my) > 1) { Toast(Loc.L("Хонь холдчихлоо", "The sheep wandered off")); return; } StartCoroutine(Shear(a.s.i)); return;
            case "felt": StartCoroutine(Felt()); return;
            case "fish": Open(new FishingModal(this)); return;
            case "uya": Open(new UyaModal(this)); return;
            case "archery": Open(new ArcheryInfoModal()); return;
            case "zasuul": case "arena": Open(new ArenaModal()); return;
            case "emee": Open(new EmeeModal()); return;
            case "npcq": Open(new NpcModal(a.id)); return;
            case "lot": LotAction(a.i); return;
            case "ovoo": OvooAction(); return;
            case "lasso": Lasso((Raider)a.raider); return;
            case "arcade": Open(new LearnPage()); return;
            case "academy": Open(new BichigPage()); return;
            case "general": Open(new GeneralModal()); return;
            case "setsen": Open(new InfoModal(Loc.L("Сэцэн", "Setsen"), Loc.L("Наадмын талбайн дэргэдэх хүчит бөхийн чулуун хөшөө: Сэцэн.", "A stone statue of a mighty wrestler beside the Naadam ground: Setsen."))); return;
            case "historian": Open(new HistorianModal()); return;
            case "basar": OpenBasar(); return;
        }
    }

    void PetSheep(Sheep s)
    {
        if (Mathf.Abs(s.x - mx) > 1 || Mathf.Abs(s.y - my) > 1) { Toast(Loc.L("Хонь холдчихлоо", "The sheep wandered off")); return; }
        Sfx.Chime(); Float(s.px + 0.5f, s.py - 0.2f, "♥", new Color(0.96f, 0.57f, 0.73f));
        string k = "sheep" + s.i;
        if (!V.pets.Contains(k)) { V.pets.Add(k); Quest("pet"); Reward(0, 2); Toast(Loc.L("Хонь баярлалаа! +2 XP", "The sheep is happy! +2 XP")); }
    }

    void OpenMilk(string id)
    {
        string kind = id == "zel" ? "mare" : id.TrimEnd('1', '2');
        var A = Rules.DAIRY_ANIMALS[kind];
        long wait = (V.MilkedAt(id) + A.wait * 1000L - SaveData.NowMs() + 999) / 1000;
        if (wait > 0) { Toast(Loc.L($"{A.name.mn} амарч байна. {wait} секундын дараа дахин саана.", $"The {A.name.en.ToLower()} is resting. Milk again in {wait}s.")); return; }
        Open(new MilkModal(id, kind));
    }

    void OvooAction()
    {
        string d = SaveData.DayKey(); bool first = V.ovoo != d;
        if (first) { V.ovoo = d; Reward(0, 5); }
        Sfx.Chime();
        Open(new FactModal(Px.Icon("ui:ovoo"), Loc.L("Овоо", "Ovoo"), Loc.L("Монголчууд овоог нар зөв гурав тойрч, чулуу нэмж, хадаг уяж, хүслээ шивнэдэг. Овоо бол байгаль, нутгаа хүндэтгэх бэлгэдэл.", "Mongolians walk clockwise around an ovoo three times, add a stone, tie a blue khadag and make a wish. It is a way of honouring nature and the homeland."), first ? "+5 XP" : null));
    }
}
