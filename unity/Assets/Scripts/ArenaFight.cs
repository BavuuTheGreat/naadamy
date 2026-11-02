// A bout in the online arena, drawn like a fighting game: the stage scrolls with the wrestlers, balance bars meet at the
// clock, round marks, stamina and the eagle meter below, hit sparks, the herald's calls and the crowd. The server runs
// the bout (server/arena-sim.js) and sends a snapshot 30 times a second; this side draws it and sends what is held and
// pressed: the keyboard (← → walk, double-tap dashes, J shove, K sweep, L grab, Space/I brace, O champion's throw) or
// the buttons on a touch screen.
using System.Collections.Generic;
using UnityEngine;

public sealed partial class Game
{
    public sealed class ArenaFightModal : Modal
    {
        // input bits, as server/arena-sim.js
        const int LEFT = 1, RIGHT = 2, UP = 4, DOWN = 8, BL = 16, BH = 32, BG = 64, BBR = 128, BSP = 256, BDF = 512, BDB = 1024;
        const float UNITS_PER_PX = 1.8f, STAGE_OFF = 26.7f, GROUND_BG = 262, VIEW_W = 420, VIEW_H = 236;
        const int BAL = 1000, STA = 1000, MET = 1000, TICK_HZ = 30;
        static readonly string[] CLOTH = { "#c8302a", "#2a56c8" };   // the red and the blue zodog

        public readonly string id, mode; readonly int side, cpu; readonly string stage;
        readonly string[] names = new string[2], tiers = new string[2]; readonly int[] ratings = new int[2]; readonly Look[] looks = new Look[2];
        sealed class FS { public float x; public int face = 1, sf, bal = BAL, sta = STA, met, combo; public string st = "idle", kind = ""; }
        sealed class Snap { public int k, rd = 1, tm = 60 * TICK_HZ, c = -1; public string ph = "intro"; public int[] w = { 0, 0 }; public FS[] f = { new FS { x = 470 }, new FS { x = 730, face = -1 } }; }
        Snap prev = new Snap(), cur = new Snap(); float curAt;
        readonly float[] trail = { BAL, BAL };
        // the end
        bool ended; float endAt; int winner = -2, xpGot, coinsGot, ratingBefore, ratingAfter; bool rated; string reason = "";
        public bool Over => ended;
        // effects
        sealed class Fx { public float x, y, t0, life; public string kind, text; public Color col; public int by; }
        readonly List<Fx> fx = new List<Fx>();
        string callText; float callAt = -9; Color callCol = Color.white;
        float shake, flashAt = -9; Color flashCol;
        // input
        int lastHeld, pend; float lastSend = -9, quitAsk = -9; readonly float[] tapAt = { -9, -9 };
        readonly Dictionary<int, int> pointerBtn = new Dictionary<int, int>(); int touchHeld; bool touchSeen;
        readonly Dictionary<int, Rect> btnRects = new Dictionary<int, Rect>(); Rect quitR;

        public override bool FullScreen => true;
        public override bool CanEscape => false;

        public ArenaFightModal(Game g, J m)
        {
            id = m["id"].S; mode = m["mode"].S; side = m["side"].I == 1 ? 1 : 0; stage = m["stage"].S ?? "naadam";
            var you = m["you"]; var opp = m["opp"];
            for (int s = 0; s < 2; s++)
            {
                var who = s == side ? you : opp;
                names[s] = Clean(who["u"].S); tiers[s] = who["tier"].S ?? "novice"; ratings[s] = who["rating"].I;
                looks[s] = s == side ? g.V.look : (ArenaLook(who["look"]) ?? new Look { skin = s == 0 ? "#EDBB93" : "#D9A06E" });
            }
            cpu = opp["cpu"].I;
            touchSeen = Application.isMobilePlatform;
            if (m["resume"].B) Call(Loc.L("Буцаад ирлээ!", "Back in the bout!"), Color.white);
        }

        // ---------------------------------------------------------------- from the server
        public void OnSnap(J m)
        {
            if (m["id"].S != id) return;
            var s = new Snap { k = m["k"].I, ph = m["ph"].S, rd = m["rd"].I, tm = m["tm"].I, c = m["c"].I, w = new[] { m["w"][0].I, m["w"][1].I } };
            for (int i = 0; i < 2; i++)
            {
                var a = m["f"][i];
                s.f[i] = new FS { x = a[0].F, face = a[1].I >= 0 ? 1 : -1, st = a[2].S ?? "idle", sf = a[3].I, bal = a[4].I, sta = a[5].I, met = a[6].I, combo = a[7].I, kind = a[8].S ?? "" };
            }
            prev = cur.k == 0 ? s : cur; cur = s; curAt = Time.realtimeSinceStartup;
            foreach (var e in m["ev"]) OnEvent(e);
        }
        public void OnEnd(Game g, J m)
        {
            if (m["id"].S != id || ended) return;
            ended = true; endAt = Time.realtimeSinceStartup; winner = m["winner"].I; reason = m["reason"].S ?? "";
            xpGot = m["xp"].I; coinsGot = m["coins"].I;
            if (m.Has("rating")) { rated = mode != "cpu"; ratingBefore = m["rating"]["before"].I; ratingAfter = m["rating"]["after"].I; }
            bool won = winner == side;
            if (won) { Sfx.Win(); g.Confetti(80); } else if (winner >= 0) Sfx.Bad();
            if (mode != "cpu" && winner >= 0) { if (won) g.V.wr.pvpW++; else g.V.wr.pvpL++; } // wins over people count toward the wrestling badges
            g.V.Save(); g.CheckAchievements();
            if (g.arena.fight == this) g.arena.fight = null;
        }
        float X(int i) => Mathf.Lerp(prev.f[i].x, cur.f[i].x, Mathf.Clamp01((Time.realtimeSinceStartup - curAt) * TICK_HZ));
        void OnEvent(J e)
        {
            string k = e["k"].S; int by = e["by"].I; float x = e.Has("x") ? e["x"].F : (cur.f[0].x + cur.f[1].x) / 2;
            bool mine = by == side;
            switch (k)
            {
                case "round": Call(cur.rd >= 5 ? Loc.L("ЭЦСИЙН ДАВАА", "FINAL ROUND") : Loc.L($"{e["n"].I}-Р ДАВАА", $"ROUND {e["n"].I}"), Px.Col("#f7c646")); Sfx.Horn(); break;
                case "fight": Call(Loc.L("БАРИЛД!", "WRESTLE!"), Color.white); Sfx.Crowd(0.5f); break;
                case "hit":
                    Spark(x, 120, e["ch"].I == 1 ? "#ff6a3c" : "#ffe27a", 1); Sfx.Hit(e["c"].I > 1 ? 1.15f : 1); shake = Mathf.Max(shake, 0.12f);
                    if (e["c"].I >= 2) Pop(x, 175, Loc.L($"{e["c"].I} МЭХ!", $"{e["c"].I} HITS!"), Px.Col("#f7c646"), by);
                    if (e["ch"].I == 1) Pop(x, 150, Loc.L("ЭСРЭГ!", "COUNTER!"), Px.Col("#ff6a3c"), by);
                    break;
                case "sweep": Dust(x); Spark(x, 30, "#ffe27a", 1.2f); Sfx.Thud(); shake = Mathf.Max(shake, 0.22f); Pop(x, 150, Loc.L("ШИЛБЭ!", "SWEEP!"), Color.white, by); break;
                case "block": Spark(x, 120, "#9fd2ff", 0.7f); Sfx.Tap(); break;
                case "parry": Ring(x, 120, "#9fd2ff"); Flash("#cfe8ff"); Sfx.Chime(); Pop(x, 170, Loc.L("НЯЦААЛАА!", "PARRY!"), Px.Col("#9fd2ff"), by); break;
                case "guard-break": Ring(x, 120, "#ff6a3c"); Sfx.Bad(); shake = 0.25f; Pop(x, 170, Loc.L("ТЭНЦВЭР АЛДЛАА!", "GUARD BREAK!"), Px.Col("#ff6a3c"), by); break;
                case "grab": Sfx.Swish(); Pop(x, 175, Loc.L("БАРЬЦАА!", "CLINCH!"), Color.white, by); break;
                case "clash": Spark(x, 120, "#ffffff", 0.8f); Sfx.Tap(); Pop(x, 170, Loc.L("ТЭНЦЛЭЭ!", "CLASH!"), Color.white, -1); break;
                case "throw":
                {
                    string kind = e["kind"].S; Dust(x); Dust(x + 30); Sfx.Thud(); Sfx.Crowd(0.4f); shake = Mathf.Max(shake, 0.3f);
                    string n = kind == "hook" ? Loc.L("ДЭГЭЭ!", "HOOK!") : kind == "hip" ? Loc.L("ГУЯ!", "HIP TOSS!") : Loc.L("ӨРГӨЛТ!", "LIFT!");
                    Pop(x, 180, n, Px.Col("#f7c646"), by);
                    if (e["guess"].B) Pop(x, 150, Loc.L("БУРУУ ТААЛАА!", "WRONG GUESS!"), Px.Col("#ff6a3c"), by);
                    break;
                }
                case "reversal": Ring(x, 120, "#f7c646"); Flash("#fff2c0"); Sfx.Good(); Pop(x, 180, Loc.L("ЭРГҮҮЛЛЭЭ!", "REVERSAL!"), Px.Col("#f7c646"), by); break;
                case "jolt-catch": Spark(x, 130, "#ff6a3c", 1); Sfx.Hit(0.8f); Pop(x, 170, Loc.L("БАРИГДЛАА!", "CAUGHT!"), Px.Col("#ff6a3c"), by); break;
                case "jolt": Spark(x, 130, "#ffe27a", 0.6f); Sfx.Hit(0.9f); break;
                case "escape": Sfx.Swish(); Pop(x, 170, Loc.L("МУЛТАРЛАА!", "ESCAPED!"), Color.white, by); break;
                case "grip-swap": Pop(x, 170, Loc.L("БАРЬЦАА СОЛИГДЛОО", "GRIP LOST"), Color.white, by); break;
                case "break": Sfx.Tap(); break;
                case "super": Flash("#ffd34a"); Sfx.Horn(); Call(Loc.L("АВАРГЫН МЭХ!", "CHAMPION'S THROW!"), Px.Col("#ffd34a")); break;
                case "super-throw": Dust(x); Dust(x - 30); Dust(x + 30); Ring(x, 60, "#ffd34a"); Sfx.Thud(); Sfx.Crowd(0.8f); shake = 0.5f; break;
                case "tired": if (mine) Pop(cur.f[side].x, 190, Loc.L("ТЭСВЭР ДУТЛАА", "NO STAMINA"), Px.Col("#8fd18a"), by); break;
                case "ko": Call(Loc.L("УНАЛАА!", "DOWN!"), Px.Col("#ff6a3c")); Sfx.Crowd(0.9f); Flash("#ffffff"); shake = 0.4f; break;
                case "time": Call(Loc.L("ЦАГ ДУУСЛАА!", "TIME!"), Color.white); break;
            }
        }
        void Call(string text, Color col) { callText = text; callAt = Time.realtimeSinceStartup; callCol = col; }
        void Flash(string col) { flashAt = Time.realtimeSinceStartup; flashCol = Px.Col(col); }
        void Pop(float x, float y, string text, Color col, int by) => fx.Add(new Fx { x = x, y = y, t0 = Time.realtimeSinceStartup, life = 1.0f, kind = "text", text = text, col = col, by = by });
        void Spark(float x, float y, string col, float size) { fx.Add(new Fx { x = x, y = y, t0 = Time.realtimeSinceStartup, life = 0.28f * size, kind = "spark", col = Px.Col(col) }); }
        void Ring(float x, float y, string col) => fx.Add(new Fx { x = x, y = y, t0 = Time.realtimeSinceStartup, life = 0.45f, kind = "ring", col = Px.Col(col) });
        void Dust(float x) => fx.Add(new Fx { x = x, y = 0, t0 = Time.realtimeSinceStartup, life = 0.6f, kind = "dust", col = new Color(0.85f, 0.78f, 0.62f) });

        // ---------------------------------------------------------------- input
        int Toward => cur.f[side].face > 0 ? RIGHT : LEFT;
        void Tap(int dir)
        {
            float now = Time.realtimeSinceStartup; int i = dir == LEFT ? 0 : 1;
            if (now - tapAt[i] < 0.28f) { pend |= dir == Toward ? BDF : BDB; tapAt[i] = -9; } else tapAt[i] = now;
        }
        public override void Tick(Game g, float dt)
        {
            shake = Mathf.Max(0, shake - dt);
            for (int i = 0; i < 2; i++) trail[i] = Mathf.Max(cur.f[i].bal, trail[i] - dt * 380);
            fx.RemoveAll(f => Time.realtimeSinceStartup - f.t0 > f.life);
            if (ended) return;
            int held = touchHeld;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) held |= LEFT;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) held |= RIGHT;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) held |= UP;
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) held |= DOWN;
            if (Input.GetKey(KeyCode.J)) held |= BL;
            if (Input.GetKey(KeyCode.K)) held |= BH;
            if (Input.GetKey(KeyCode.L)) held |= BG;
            if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.I)) held |= BBR;
            if (Input.GetKey(KeyCode.O)) held |= BSP;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) Tap(LEFT);
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) Tap(RIGHT);
            if (Input.GetKeyDown(KeyCode.J)) pend |= BL;
            if (Input.GetKeyDown(KeyCode.K)) pend |= BH;
            if (Input.GetKeyDown(KeyCode.L)) pend |= BG;
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.I)) pend |= BBR;
            if (Input.GetKeyDown(KeyCode.O)) pend |= BSP;
            if (Input.GetKeyDown(KeyCode.Escape)) AskQuit(g);
            float now = Time.realtimeSinceStartup;
            if ((held != lastHeld || pend != 0) && now - lastSend >= 0.033f && g.net.Online)
            {
                g.net.Send("{\"t\":\"ar-in\",\"m\":" + held + ",\"p\":" + pend + "}");
                lastHeld = held; pend = 0; lastSend = now;
            }
        }
        void AskQuit(Game g)
        {
            if (ended) { g.Later(() => g.Swap(new ArenaHubModal())); return; }
            if (Time.realtimeSinceStartup - quitAsk < 2.5f) { g.net.Send("{\"t\":\"ar-quit\"}"); quitAsk = -9; }
            else { quitAsk = Time.realtimeSinceStartup; g.Toast(Loc.L("Бууж өгөх бол дахин дарна уу.", "Press again to give up the bout.")); }
        }
        public override void Pointer(Game g, int pid, Vector2 pos, bool down, bool up)
        {
            var gp = new Vector2(pos.x, Screen.height - pos.y);
            if (down)
            {
                touchSeen = touchSeen || Input.touchCount > 0;
                if (quitR.Contains(gp)) { AskQuit(g); return; }
                foreach (var kv in btnRects) if (kv.Value.Contains(gp))
                {
                    pointerBtn[pid] = kv.Key; touchHeld |= kv.Key; Sfx.Tap();
                    if (kv.Key == LEFT || kv.Key == RIGHT) Tap(kv.Key); else pend |= kv.Key;
                    return;
                }
            }
            if (up && pointerBtn.TryGetValue(pid, out var b)) { pointerBtn.Remove(pid); bool still = false; foreach (var v in pointerBtn.Values) if (v == b) still = true; if (!still) touchHeld &= ~b; }
        }
        public override void Closed(Game g) { if (g.arena.fight == this) g.arena.fight = null; }
        public override void Press(Game g) { }

        // ---------------------------------------------------------------- the sprites
        static Color32[] fixedPx, tintPx; static int sheetW, sheetH;
        static readonly Dictionary<string, Texture2D> sheets = new Dictionary<string, Texture2D>();
        static J frames; static Texture2D disc;
        static Color32[] Pixels(string name, out int w, out int h)
        {
            var ta = Resources.Load<TextAsset>("Art/" + name + ".png");
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(ta.bytes);
            w = t.width; h = t.height; var px = t.GetPixels32(); Object.Destroy(t); return px;
        }
        /** The wrestler sheet in this skin and zodog: fixed + skin weight × skin + cloth weight × cloth (Tools/art/fighter.py). */
        static Texture2D Sheet(string skin, string cloth)
        {
            string key = skin + cloth;
            if (sheets.TryGetValue(key, out var have) && have != null) return have;
            if (fixedPx == null) { fixedPx = Pixels("arena_fighter", out sheetW, out sheetH); tintPx = Pixels("arena_fighter_tint", out _, out _); }
            Color32 S = Px.Col(skin), C = Px.Col(cloth); var o = new Color32[fixedPx.Length];
            for (int i = 0; i < o.Length; i++)
            {
                var f = fixedPx[i]; var w = tintPx[i]; float s = w.r / 255f, c = w.g / 255f;
                o[i] = new Color32((byte)Mathf.Min(255, f.r + s * S.r + c * C.r), (byte)Mathf.Min(255, f.g + s * S.g + c * C.g), (byte)Mathf.Min(255, f.b + s * S.b + c * C.b), f.a);
            }
            var tex = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(o); tex.Apply(false, true);
            return sheets[key] = tex;
        }
        static Texture2D Disc()
        {
            if (disc != null) return disc;
            const int n = 32; disc = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f); px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01((1 - d) * 3))); }
            disc.SetPixels32(px); disc.Apply(); return disc;
        }
        static (string pose, int frame) Anim(FS f, float t)
        {
            int sf = f.sf;
            int Phase(int su, int ac, int n) => sf < su ? (n > 3 && sf >= su / 2 ? 1 : 0) : sf < su + ac ? n - 2 : n - 1;
            switch (f.st)
            {
                case "idle": return ("idle", (int)(t * 6) % 4);
                case "walk_f": return ("walk", (sf / 4) % 6);
                case "walk_b": return ("walk", 5 - (sf / 4) % 6);
                case "dash_f": case "dash_b": return ("dash", sf < 6 ? 0 : 1);
                case "guard": return ("guard", 0);
                case "block": case "pushed": return ("block", 0);
                case "parry": return ("parry", sf < 4 ? 0 : 1);
                case "shove": return ("shove", Phase(5, 3, 3));
                case "push": return ("push", Phase(8, 4, 3));
                case "sweep": return ("sweep", Phase(12, 4, 4));
                case "charge": return ("charge", Phase(14, 5, 3));
                case "grab": return ("grab", Phase(6, 3, 3));
                case "lunge": return ("lunge", Phase(12, 4, 3));
                case "super": case "c_super": return ("super", sf < 8 ? 0 : 1);
                case "hit": return ("hit", sf < 5 ? 0 : 1);
                case "staggered": return ("stagger", (sf / 6) % 2);
                case "dizzy": return ("dizzy", (int)(t * 3) % 2);
                case "offbal": return ("offbal", 0);
                case "trip": return ("trip", 0);
                case "down": case "fall": return ("down", 0);
                case "getup": return ("getup", sf < 6 ? 0 : 1);
                case "clinch_lead": return ("clinch_lead", (int)(t * 2.5f) % 2);
                case "clinch_def": return ("clinch_def", (int)(t * 2.5f) % 2);
                case "jolt": return ("jolt", 0);
                case "t_hook": return ("t_hook", sf < 7 ? 0 : 1);
                case "t_hip": return ("t_hip", sf < 7 ? 0 : 1);
                case "t_lift": return ("t_lift", sf < 9 ? 0 : 1);
                case "counter": return ("counter", 0);
                case "escape": return ("escape", sf < 5 ? 0 : 1);
                case "throwing": case "super_throw": return ("throwing", sf < 6 ? 0 : sf < 12 ? 1 : 2);
                case "thrown": return ("thrown", sf < 6 ? 0 : sf < 13 ? 1 : 2);
                case "win": return ("win", (int)(t * 5) % 4);
                case "lose": return ("lose", 0);
                default: return ("idle", 0);
            }
        }
        void DrawWrestler(int i, float sx, float ground, float K, float t)
        {
            if (frames == null) frames = Data.Get("arena_frames");
            var f = cur.f[i]; var (pose, fr) = Anim(f, t);
            var e = frames["poses"][pose]; int start = e[0].I, cnt = Mathf.Max(1, e[1].I), idx = start + Mathf.Clamp(fr, 0, cnt - 1);
            int cell = frames["cell"].I, cols = frames["cols"].I; float anchor = frames.Has("anchor") ? frames["anchor"].F : 56, gy = frames["ground"].F;
            var tex = Sheet(looks[i].skin ?? "#EDBB93", CLOTH[i]);
            float uw = (float)cell / tex.width, vh = (float)cell / tex.height, u = (idx % cols) * uw, v = 1 - (idx / cols + 1) * vh;
            float ax = f.face > 0 ? anchor : cell - anchor;
            var r = new Rect(sx - ax * K, ground - gy * K, cell * K, cell * K);
            // a shadow on the ground
            var old = GUI.color; GUI.color = new Color(0, 0, 0, 0.28f); GUI.DrawTexture(new Rect(sx - 26 * K, ground - 4 * K, 52 * K, 8 * K), Disc()); GUI.color = old;
            if (f.st == "hit" && f.sf < 3) GUI.color = new Color(1, 0.75f, 0.7f);
            GUI.DrawTextureWithTexCoords(r, tex, f.face > 0 ? new Rect(u, v, uw, vh) : new Rect(u + uw, v, -uw, vh));
            GUI.color = old;
        }

        // ---------------------------------------------------------------- every frame
        public override void Draw(Game g)
        {
            float now = Time.realtimeSinceStartup, U = g.UiScale;
            g.Box(new Rect(0, 0, Screen.width, Screen.height), Px.Col("#1a1218"));
            // the camera: the middle of the two wrestlers, kept on the stage
            float K = Mathf.Max(Screen.width / VIEW_W, Screen.height / VIEW_H), vw = Screen.width / K, vh = Screen.height / K;
            float mid = STAGE_OFF + (X(0) + X(1)) / 2 / UNITS_PER_PX;
            float left = Mathf.Clamp(mid - vw / 2, 0, Mathf.Max(0, 720 - vw)), top = Mathf.Clamp(GROUND_BG - vh * 0.84f, 0, Mathf.Max(0, 300 - vh));
            float sh = shake > 0 ? shake * 14 : 0; float jx = (Mathf.PerlinNoise(now * 40, 0) - 0.5f) * sh, jy = (Mathf.PerlinNoise(0, now * 40) - 0.5f) * sh;
            left += jx; top += jy;
            if (Event.current.type == EventType.Repaint)
            {
                var bg = g.Tex(Art.Get("arena_stage_" + stage));
                GUI.DrawTextureWithTexCoords(new Rect(0, 0, Screen.width, Screen.height), bg, new Rect(left / bg.width, 1 - (top + vh) / bg.height, vw / bg.width, vh / bg.height));
                float ground = (GROUND_BG - top) * K;
                float Sx(float units) => (STAGE_OFF + units / UNITS_PER_PX - left) * K;
                // the one making the move is drawn in front
                int front = cur.c >= 0 ? cur.c : Busy(cur.f[1]) && !Busy(cur.f[0]) ? 1 : 0;
                for (int k = 0; k < 2; k++)
                {
                    int i = k == 0 ? 1 - front : front; float x = X(i);
                    if (cur.f[i].st == "thrown" && cur.f[i].sf < 20) x -= cur.f[i].face * Mathf.Min(cur.f[i].sf, 14) * 3; // flung back over the ground
                    DrawWrestler(i, Sx(x), ground, K, now + i * 0.37f);
                }
                // which one is you, for the first seconds of a round
                if (cur.ph == "intro" || (cur.ph == "fight" && cur.tm > 60 * TICK_HZ - 45))
                    g.Outlined(Sx(X(side)), ground - 132 * K, Loc.L("▼ ЧИ", "▼ YOU"), U * 14, Px.Col(side == 0 ? "#ff8a7a" : "#8ab4ff"));
                Effects(g, Sx, ground, K, now);
                if (now - flashAt < 0.25f) g.Box(new Rect(0, 0, Screen.width, Screen.height), new Color(flashCol.r, flashCol.g, flashCol.b, 0.45f * (1 - (now - flashAt) / 0.25f)));
            }
            Hud(g, U, now);
            Controls(g, U);
            if (!g.net.Online && !ended) GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, U * 40), Loc.L("Холболт тасарлаа. Дахин холбогдож байна…", "Connection lost. Reconnecting…"), new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
            if (ended && now - endAt > 1.4f) Results(g, U);
        }
        static bool Busy(FS f) => f.st != "idle" && f.st != "walk_f" && f.st != "walk_b" && f.st != "guard" && f.st != "hit" && f.st != "block" && f.st != "down";

        void Effects(Game g, System.Func<float, float> Sx, float ground, float K, float now)
        {
            foreach (var f in fx)
            {
                float q = (now - f.t0) / f.life, sx = Sx(f.x), sy = ground - f.y * K / UNITS_PER_PX;
                var old = GUI.color;
                if (f.kind == "spark")
                {
                    float r = (14 + 40 * q) * K * 0.6f;
                    GUI.color = new Color(f.col.r, f.col.g, f.col.b, 1 - q); GUI.DrawTexture(new Rect(sx - r, sy - r, r * 2, r * 2), Disc());
                    GUI.color = new Color(1, 1, 1, 1 - q); GUI.DrawTexture(new Rect(sx - r * 0.4f, sy - r * 0.4f, r * 0.8f, r * 0.8f), Disc());
                    for (int k = 0; k < 6; k++) { float a = k * 1.047f + f.t0 * 7, d = r * (0.6f + q * 0.9f); g.Box(new Rect(sx + Mathf.Cos(a) * d - K, sy + Mathf.Sin(a) * d - K, 2.5f * K, 2.5f * K), new Color(f.col.r, f.col.g, f.col.b, 1 - q)); }
                }
                else if (f.kind == "ring")
                {
                    float r = (10 + 70 * q) * K * 0.6f;
                    for (int k = 0; k < 16; k++) { float a = k * 0.3927f, d = r; g.Box(new Rect(sx + Mathf.Cos(a) * d - 1.5f * K, sy + Mathf.Sin(a) * d - 1.5f * K, 3 * K, 3 * K), new Color(f.col.r, f.col.g, f.col.b, 1 - q)); }
                }
                else if (f.kind == "dust")
                {
                    GUI.color = new Color(f.col.r, f.col.g, f.col.b, 0.7f * (1 - q));
                    for (int k = -1; k <= 1; k++) { float r = (8 + 20 * q) * K; GUI.DrawTexture(new Rect(sx + k * 18 * K * (0.5f + q) - r, ground - r * 0.9f - q * 6 * K, r * 2, r * 1.4f), Disc()); }
                }
                else if (f.kind == "text")
                {
                    float pop = q < 0.12f ? 1.35f - q * 3 : 1;
                    g.Outlined(sx, sy - q * 22 * K, f.text, g.UiScale * 18 * pop, f.col, q > 0.7f ? (1 - q) / 0.3f : 1);
                }
                GUI.color = old;
            }
            // the herald's big calls in the middle of the screen
            float ct = Time.realtimeSinceStartup - callAt;
            if (callText != null && ct < 1.3f)
            {
                float pop = ct < 0.15f ? 1.6f - ct * 4 : 1, a = ct > 1f ? (1.3f - ct) / 0.3f : 1;
                g.Outlined(Screen.width / 2f, Screen.height * 0.4f, callText, g.UiScale * 46 * pop, callCol, a);
            }
        }

        // ---------------------------------------------------------------- the bars, the clock and the names
        void Hud(Game g, float U, float now)
        {
            var safe = Screen.safeArea; float topY = Screen.height - safe.yMax + U * 10, W = Screen.width;
            float clockW = U * 64, barW = Mathf.Min(U * 340, (W - clockW) / 2 - U * 24), barH = U * 18;
            var nameSt = new GUIStyle(g.sLabel) { fontSize = g.Ts(13), normal = { textColor = Color.white }, richText = true };
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0; var f = cur.f[i];
                float x = left ? W / 2 - clockW / 2 - barW - U * 6 : W / 2 + clockW / 2 + U * 6;
                // balance: yellow, with the red of what was just lost behind it
                g.Box(new Rect(x - U * 3, topY - U * 3, barW + U * 6, barH + U * 6), Px.Col("#1a1218"));
                g.Box(new Rect(x, topY, barW, barH), Px.Col("#5a1414"));
                float tr = trail[i] / BAL, bl = Mathf.Clamp01(f.bal / (float)BAL);
                Fill(g, x, topY, barW, barH, tr, left, Px.Col("#e14b3d"));
                Fill(g, x, topY, barW, barH, bl, left, bl < 0.25f && (int)(now * 6) % 2 == 0 ? Px.Col("#ff8a3c") : Px.Col("#f7c646"));
                g.Box(new Rect(x, topY, barW, U * 3), new Color(1, 1, 1, 0.25f));
                // the name, the title and the rating
                string nm = $"<b>{names[i]}</b>  {TierTag(tiers[i])}" + (cpu > 0 && i != side ? "" : $" <size={g.Ts(10)}>{ratings[i]}</size>") + (i == side ? Loc.L(" · чи", " · you") : "");
                GUI.Label(new Rect(x, topY + barH + U * 4, barW, U * 22), nm, new GUIStyle(nameSt) { alignment = left ? TextAnchor.UpperLeft : TextAnchor.UpperRight });
                // rounds won: eagle feathers
                for (int r = 0; r < 2; r++)
                {
                    float rx = left ? W / 2 - clockW / 2 - U * 18 - r * U * 16 : W / 2 + clockW / 2 + U * 6 + r * U * 16;
                    g.Box(new Rect(rx, topY + barH + U * 26, U * 12, U * 12), cur.w[i] > r ? Px.Col("#f7c646") : new Color(0, 0, 0, 0.5f));
                }
                // stamina and the eagle meter at the bottom
                float by = Screen.height - safe.y - U * 30, bw = Mathf.Min(U * 220, W / 2 - U * 40);
                float bx = left ? safe.x + U * 14 : W - (Screen.width - safe.xMax) - U * 14 - bw;
                Meter(g, bx, by - U * 18, bw, U * 9, f.sta / (float)STA, left, Px.Col(f.sta < 150 ? "#b55a3c" : "#5fd36a"), Loc.L("ТЭСВЭР", "STAMINA"));
                bool full = f.met >= MET;
                Meter(g, bx, by, bw, U * 11, f.met / (float)MET, left, full && (int)(now * 5) % 2 == 0 ? Px.Col("#ffffff") : Px.Col("#3fa9ff"), full ? Loc.L("БҮРГЭД! (O)", "EAGLE! (O)") : Loc.L("БҮРГЭД", "EAGLE"));
                if (f.combo >= 1 && f.st == "hit")
                    g.Outlined(left ? W * 0.78f : W * 0.22f, Screen.height * 0.3f, Loc.L($"{f.combo + 1} МЭХ", $"{f.combo + 1} HITS"), U * 26, Px.Col("#f7c646"));
            }
            // the clock
            int secs = Mathf.CeilToInt(Mathf.Max(0, cur.tm) / (float)TICK_HZ);
            g.Box(new Rect(W / 2 - clockW / 2, topY - U * 6, clockW, U * 50), Px.Col("#1a1218"));
            GUI.Label(new Rect(W / 2 - clockW / 2, topY - U * 6, clockW, U * 38), secs.ToString(), new GUIStyle(g.sBig) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(U * 30), normal = { textColor = secs <= 10 && (int)(now * 2) % 2 == 0 ? Px.Col("#ff6a3c") : Px.Col("#f7c646") } });
            GUI.Label(new Rect(W / 2 - clockW / 2, topY + U * 28, clockW, U * 16), Loc.L($"{cur.rd}-р даваа", $"Round {cur.rd}"), new GUIStyle(g.sSmall) { alignment = TextAnchor.MiddleCenter, fontSize = g.Ts(9), normal = { textColor = Color.white } });
            string modeT = mode == "ranked" ? Loc.L("Чансааны барилдаан", "Ranked bout") : mode == "friend" ? Loc.L("Найзын барилдаан", "Friendly bout") : Loc.L("Компьютертэй дасгал", "Sparring");
            GUI.Label(new Rect(0, topY + U * 46, W, U * 16), modeT, new GUIStyle(g.sSmall) { alignment = TextAnchor.MiddleCenter, fontSize = g.Ts(9), normal = { textColor = new Color(1, 1, 1, 0.75f) } });
            // in the clinch: who holds the grip, and what the other one can do
            if (cur.c >= 0 && cur.ph == "fight")
            {
                string tip = cur.c == side ? Loc.L("Барьцаа чинийх! →K дэгээ · ←K гуя · K өргөлт · J татах", "Your grip! →K hook · ←K hip toss · K lift · J jolt")
                                           : Loc.L("Мэхийг нь таниад адилхан чиглэл + L! · J мултрах", "Read the throw: same direction + L! · J to escape");
                GUI.Label(new Rect(0, Screen.height * 0.2f, W, U * 24), tip, new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = g.Ts(12), normal = { textColor = Color.white } });
            }
            quitR = new Rect(safe.x + U * 8, topY + U * 70, U * 40, U * 40);
            GUI.Box(quitR, "×", g.sBtn);
        }
        static void Fill(Game g, float x, float y, float w, float h, float f, bool fromRight, Color c)
        {
            float fw = w * Mathf.Clamp01(f);
            g.Box(fromRight ? new Rect(x + w - fw, y, fw, h) : new Rect(x, y, fw, h), c); // the bars drain toward the outside, like a fighting game
        }
        static void Meter(Game g, float x, float y, float w, float h, float f, bool left, Color c, string label)
        {
            g.Box(new Rect(x - 2, y - 2, w + 4, h + 4), new Color(0, 0, 0, 0.6f));
            Fill(g, x, y, w, h, f, !left, c);
            GUI.Label(new Rect(x, y - h - 4, w, h + 4), label, new GUIStyle(g.sSmall) { fontSize = g.Ts(8), alignment = left ? TextAnchor.LowerLeft : TextAnchor.LowerRight, normal = { textColor = new Color(1, 1, 1, 0.8f) } });
        }

        // ---------------------------------------------------------------- touch buttons (and key hints)
        void Controls(Game g, float U)
        {
            btnRects.Clear();
            if (ended) return;
            var safe = Screen.safeArea; float bottom = Screen.height - safe.y - U * 60, s = U * 64;
            var st = new GUIStyle(g.sBtn) { fontSize = g.Ts(11), alignment = TextAnchor.MiddleCenter, wordWrap = true };
            void Btn(int bit, Rect r, string text, Color col)
            {
                btnRects[bit] = r;
                bool on = (touchHeld & bit) != 0;
                g.Box(r, on ? new Color(col.r, col.g, col.b, 0.85f) : new Color(col.r * 0.5f, col.g * 0.5f, col.b * 0.5f, 0.55f));
                GUI.Label(r, text, new GUIStyle(st) { normal = { background = null, textColor = Color.white } });
            }
            if (!touchSeen)
            {
                GUI.Label(new Rect(0, bottom + U * 30, Screen.width, U * 18), Loc.L("← → алхах (2 дарвал гүйх) · J түлхэх · K шилбэ · L барьцаа · Space тэнцвэр · O аваргын мэх · Esc бууж өгөх", "← → walk (double-tap dash) · J shove · K sweep · L grab · Space brace · O champion's throw · Esc give up"),
                    new GUIStyle(g.sSmall) { alignment = TextAnchor.MiddleCenter, fontSize = g.Ts(9), normal = { textColor = new Color(1, 1, 1, 0.7f) } });
                return;
            }
            float lx = safe.x + U * 16, by = bottom - s - U * 40;
            Btn(LEFT, new Rect(lx, by, s, s), "◄", Px.Col("#5a5a7a"));
            Btn(RIGHT, new Rect(lx + s + U * 10, by, s, s), "►", Px.Col("#5a5a7a"));
            float rx = Screen.width - (Screen.width - safe.xMax) - U * 16 - s * 3 - U * 20;
            Btn(BL, new Rect(rx, by, s, s), Loc.L("Түлхэх", "Shove"), Px.Col("#e14b3d"));
            Btn(BH, new Rect(rx + s + U * 10, by, s, s), Loc.L("Шилбэ", "Sweep"), Px.Col("#e9a91f"));
            Btn(BG, new Rect(rx + (s + U * 10) * 2, by, s, s), Loc.L("Барих", "Grab"), Px.Col("#2ea35f"));
            Btn(BBR, new Rect(rx + (s + U * 10) * 0.5f, by - s - U * 10, s, s), Loc.L("Тэнцвэр", "Brace"), Px.Col("#3f76db"));
            Btn(BSP, new Rect(rx + (s + U * 10) * 1.5f, by - s - U * 10, s, s), Loc.L("Аварга", "Champ"), cur.f[side].met >= MET ? Px.Col("#ffd34a") : Px.Col("#6a6a6a"));
        }

        // ---------------------------------------------------------------- the end of the bout
        void Results(Game g, float U)
        {
            bool won = winner == side, draw = winner == -1;
            float w = Mathf.Min(U * 460, Screen.width - U * 32), h = U * 290;
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            g.Box(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.45f));
            GUILayout.BeginArea(r); GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label(draw ? Loc.L("ТЭНЦЛЭЭ", "DRAW") : won ? Loc.L("ЯЛАЛТ!", "VICTORY!") : Loc.L("ЯЛАГДАЛ", "DEFEAT"), new GUIStyle(g.sBig) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(U * 36), normal = { textColor = won ? Px.Col("#f7c646") : draw ? Color.white : Px.Col("#e14b3d") } });
            string why = reason == "quit" ? (won ? Loc.L("Өрсөлдөгч бууж өглөө.", "Your opponent gave up.") : Loc.L("Та бууж өглөө.", "You gave up.")) : reason == "left" ? (won ? Loc.L("Өрсөлдөгчийн холболт тасарлаа.", "Your opponent left.") : Loc.L("Холболт тасарсан.", "You were disconnected.")) : $"{cur.w[side]} : {cur.w[1 - side]}";
            g.Text($"<b>{names[side]}</b> vs <b>{names[1 - side]}</b> · {why}");
            if (rated) { int d = ratingAfter - ratingBefore; g.Text(Loc.L("Оноо", "Rating") + $": {ratingBefore} → <b>{ratingAfter}</b> (" + (d >= 0 ? "<color=#2ea35f>+" : "<color=#e14b3d>") + d + "</color>)"); }
            g.Text($"+{xpGot} XP" + (coinsGot > 0 ? $" · +{coinsGot} " + Loc.L("зоос", "coins") : ""));
            if (won) g.Small(Loc.L("Ялсан бөх бүргэд шиг дэвж, ялагдсан бөх түүний суган доогуур гарч хүндэтгэл үзүүлдэг.", "The winner does the eagle dance; the one who fell passes under the winner's arm as a sign of respect."));
            GUILayout.BeginHorizontal();
            if (g.Btn(Loc.L("Өргөө рүү", "Back to arena"), false, 44)) g.Later(() => g.Swap(new ArenaHubModal()));
            string again = mode == "ranked" ? Loc.L("Дахин хайх", "Find another") : Loc.L("Дахин барилдах", "Rematch");
            if (g.Btn(again, true, 44)) g.Later(() =>
            {
                if (mode == "cpu") g.net.Send("{\"t\":\"ar-cpu\",\"lv\":" + Mathf.Clamp(cpu, 1, 3) + "}");
                else if (mode == "friend") g.ArenaSend("ar-challenge", "u", names[1 - side]);
                else g.net.Send("{\"t\":\"ar-queue\"}");
                if (mode != "cpu") g.Swap(new ArenaHubModal());
            });
            GUILayout.EndHorizontal();
            GUILayout.EndVertical(); GUILayout.EndArea();
        }
    }
}
